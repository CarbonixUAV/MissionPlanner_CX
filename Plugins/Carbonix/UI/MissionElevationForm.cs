using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using MissionPlanner;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;
using Carbonix.Planning;
using log4net;

namespace Carbonix.UI
{
    /// <summary>
    /// Elevation profile over the FlightPlanner mission with a target AGL band. "Apply target"
    /// shifts every waypoint to the middle of the band (loiters at their orbit exit); "Fit to
    /// band" fits heights into it, adding waypoints if allowed; dots can be dragged and
    /// waypoints inserted on straight legs. MSA and Ceiling are surface-model reference lines.
    /// Accept writes altitudes back, in each row's own frame or one frame chosen for all.
    /// </summary>
    internal class MissionElevationForm : Form
    {
        private static readonly ILog log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Inserted waypoints have no grid row yet; their ids start here, clear of row indices.
        private const int InsertIdBase = 100000;
        // Grid row Tag (TagData cell) on inserted WAYPOINTs, so a reopen in the same session
        // knows they can still slide along their leg. Not saved with the mission.
        internal const string InsertRowTag = "CarbonixElevInsert";

        // Box values from the last Accept (altitudes in metres, gradients in percent).
        private const string BandLoKey = "carbonix_elev_band_lo";
        private const string BandHiKey = "carbonix_elev_band_hi";
        private const string MsaKey = "carbonix_elev_msa";
        private const string CeilingKey = "carbonix_elev_ceiling";
        private const string GradWarnKey = "carbonix_elev_gradwarn";
        private const string GradMaxKey = "carbonix_elev_gradmax";
        private static double Saved(string key, double fallback) =>
            MissionPlanner.Utilities.Settings.Instance.GetDouble(key, fallback);

        private readonly CarbonixPlugin plugin;
        private readonly ElevationProfileControl elev_profile = new ElevationProfileControl { Dock = DockStyle.Fill };
        private readonly NumericUpDown NUM_targetlo = MakeAltNum(Saved(BandLoKey, 70));
        private readonly NumericUpDown NUM_targethi = MakeAltNum(Saved(BandHiKey, 90));
        private readonly NumericUpDown NUM_msa = MakeAltNum(Saved(MsaKey, 50));
        private readonly NumericUpDown NUM_ceiling = MakeAltNum(Saved(CeilingKey, 120));
        private readonly NumericUpDown NUM_gradwarn = MakePctNum(Saved(GradWarnKey, 4));
        private readonly NumericUpDown NUM_gradmax = MakePctNum(Saved(GradMaxKey, 5));
        private readonly ComboBox CMB_frame = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly MyButton BUT_apply = new MyButton { Text = "Apply target", Width = 90 };
        private readonly MyButton BUT_accept = new MyButton { Text = "Accept", Width = 75 };
        private readonly MyButton BUT_fit = new MyButton { Text = "Fit to band", Width = 90 };
        private readonly CheckBox CHK_suggest = new CheckBox
        {
            Text = "Add waypoints", Checked = true, AutoSize = true, Padding = new Padding(0, 4, 0, 0),
        };
        private readonly Label LBL_fit = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

        // Cursor cross-link marker on the FlightPlanner map.
        private readonly GMapOverlay layer_hover = new GMapOverlay("elevhover");
        private GMapMarker hoverMarker;

        private readonly SurfaceProvider floorSurface = new SurfaceProvider();
        private readonly SurfaceProvider ceilingSurface = new SurfaceProvider();

        // Positioned nav waypoints in mission order. Id = Commands row index, or an id >=
        // InsertIdBase for an inserted one.
        private readonly List<ProfileWaypoint> wps = new List<ProfileWaypoint>();
        // Per Id: the row's frame and the terrain under the point (for the
        // terrain frame; TerrainAltM may move to a loiter's exit).
        private readonly Dictionary<int, (MAVLink.MAV_FRAME frame, double terr)> info =
            new Dictionary<int, (MAVLink.MAV_FRAME, double)>();
        private readonly List<int> removedRows = new List<int>();
        // Grid rows carrying InsertRowTag (inserted by an earlier Accept this session).
        private readonly HashSet<int> taggedRows = new HashSet<int>();
        // Inserts the last fit suggested; a re-fit drops and reconsiders them.
        private readonly List<int> suggested = new List<int>();
        private List<ElevationPoint> samples;
        private double homeAmsl;
        private int nextInsertId = InsertIdBase;

        private static NumericUpDown MakeAltNum(double metres) => new NumericUpDown
        {
            Maximum = (decimal)(3000 * CurrentState.multiplieralt),
            Value = (decimal)Math.Max(0, Math.Min(3000, metres) * CurrentState.multiplieralt),
            Width = 60,
        };

        private static NumericUpDown MakePctNum(double pct) => new NumericUpDown
        {
            DecimalPlaces = 1,
            Increment = 0.5m,
            Maximum = 100m,
            Value = (decimal)Math.Max(0, Math.Min(100, pct)),
            Width = 50,
        };

        public MissionElevationForm(CarbonixPlugin plugin)
        {
            this.plugin = plugin;
            Text = "Elevation Graph (Target AGL)";
            Width = 1200;
            Height = 500;
            StartPosition = FormStartPosition.CenterParent;

            CMB_frame.Items.AddRange(new object[] { "Keep existing", "Relative", "Absolute", "Terrain" });
            CMB_frame.SelectedIndex = 0;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            string unit = CurrentState.AltUnit;
            Label Lbl(string t) => new Label { Text = t, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            bar.Controls.AddRange(new Control[]
            {
                Lbl($"Target AGL ({unit}):"), NUM_targetlo, Lbl("to"), NUM_targethi,
                Lbl($"MSA ({unit}):"), NUM_msa,
                Lbl($"Ceiling ({unit}):"), NUM_ceiling,
                Lbl("Grad warn %:"), NUM_gradwarn,
                Lbl("Grad max %:"), NUM_gradmax,
                BUT_apply, BUT_fit, CHK_suggest,
                Lbl("Frame:"), CMB_frame,
                BUT_accept,
                LBL_fit,
            });
            Controls.Add(elev_profile);
            Controls.Add(bar);

            elev_profile.AltMultiplier = CurrentState.multiplieralt;
            elev_profile.AltUnit = unit;
            elev_profile.DistMultiplier = 0.001;
            elev_profile.DistUnit = "km";
            elev_profile.HomeTerrainAlt = 0;   // AMSL datum, as in the corridor planner

            foreach (var n in new[] { NUM_targetlo, NUM_targethi, NUM_msa, NUM_ceiling, NUM_gradwarn, NUM_gradmax })
            {
                n.ValueChanged += (s, e) => ApplyOffsets();
                n.Enter += (s, e) => n.Select(0, n.Text.Length);   // tab in with the value selected: typing replaces it
            }
            for (int i = 0; i < bar.Controls.Count; i++) bar.Controls[i].TabIndex = i;   // left to right
            BUT_apply.Click += async (s, e) => await Guarded(ApplyTarget);
            BUT_fit.Click += async (s, e) => await Guarded(FitToBand);
            BUT_accept.Click += async (s, e) => await Guarded(() => { Accept(); return Task.CompletedTask; });
            elev_profile.AltitudeChanged += (s, e) => SetWpAlt(e.WaypointIndex, e.NewAltRelM);
            elev_profile.LoiterToAltTargetChanged += (s, e) => SetWpAlt(e.LoiterId, e.NewTargetAltRelM);
            elev_profile.WaypointInsertRequested += ElevProfile_InsertRequested;
            elev_profile.InsertedWaypointMoved += ElevProfile_InsertedMoved;
            elev_profile.WaypointRemoveRequested += ElevProfile_RemoveRequested;
            elev_profile.HoverChanged += ElevProfile_HoverChanged;

            Load += async (s, e) =>
            {
                LockPlanner(true);
                await LoadMission();
            };
            FormClosed += (s, e) => LockPlanner(false);
        }

        // ─── FlightPlanner lock + hover cross-link ──────────────────────────────────
        // Non-modal, so the main map stays navigable, but the mission must not change under
        // us (wps are keyed by grid row). FlightPlanner pans in its own MouseDown/MouseMove,
        // so only MouseUp (where clicks and drags become edits) is swapped for one that just
        // ends the gesture. The context menu and the waypoint/action panels are disabled.

        private static readonly string[] DragFields =
            { "CurentRectMarker", "CurrentGMapMarker", "CurrentMidLine", "CurrentPOIMarker", "CurrentRallyPt" };
        private MouseEventHandler fpMouseUp;
        private ContextMenuStrip fpMenu;

        private FlightPlanner FP => plugin.Host.MainForm.FlightPlanner;

        private FieldInfo FpField(string name) =>
            typeof(FlightPlanner).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private void LockPlanner(bool locked)
        {
            var map = FP.MainMap;
            if (fpMouseUp == null)
                fpMouseUp = (MouseEventHandler)Delegate.CreateDelegate(typeof(MouseEventHandler), FP,
                    typeof(FlightPlanner).GetMethod("MainMap_MouseUp", BindingFlags.NonPublic | BindingFlags.Instance));

            if (locked)
            {
                map.MouseUp -= fpMouseUp;
                map.MouseUp += LockedMouseUp;
                fpMenu = map.ContextMenuStrip;
                map.ContextMenuStrip = null;
                map.MouseMove += Map_HoverMove;
                map.MouseLeave += Map_HoverLeave;
                map.Overlays.Add(layer_hover);
            }
            else
            {
                map.MouseUp -= LockedMouseUp;
                map.MouseUp += fpMouseUp;
                map.ContextMenuStrip = fpMenu;
                map.MouseMove -= Map_HoverMove;
                map.MouseLeave -= Map_HoverLeave;
                map.Overlays.Remove(layer_hover);
                map.Invalidate();
            }
            FP.panelWaypoints.Enabled = FP.panelAction.Enabled = !locked;
        }

        private void LockedMouseUp(object sender, MouseEventArgs e)
        {
            var groups = (List<int>)FpField("groupmarkers").GetValue(FP);
            bool dragged = groups.Count > 0 || DragFields.Any(f => FpField(f).GetValue(FP) != null);
            foreach (var f in DragFields) FpField(f).SetValue(FP, null);
            groups.Clear();
            FpField("isMouseDown").SetValue(FP, false);
            FpField("isMouseDraging").SetValue(FP, false);
            if (dragged) FP.writeKML();   // snap any dragged marker back to the mission
        }

        private void Map_HoverMove(object sender, MouseEventArgs e)
        {
            if (samples == null) return;
            var ll = FP.MainMap.FromLocalToLatLng(e.X, e.Y);
            elev_profile.SetExternalHover(ll.Lat, ll.Lng);
        }

        private void Map_HoverLeave(object sender, EventArgs e) => elev_profile.SetExternalHover(null, null);

        private void ElevProfile_HoverChanged(object sender, ProfileHoverEventArgs e)
        {
            if (e.Lat == null || e.Lng == null)
            {
                layer_hover.Markers.Clear();
                hoverMarker = null;
            }
            else if (hoverMarker == null)
            {
                hoverMarker = new GMarkerGoogle(new PointLatLng(e.Lat.Value, e.Lng.Value), GMarkerGoogleType.yellow_dot);
                layer_hover.Markers.Add(hoverMarker);
            }
            else
            {
                hoverMarker.Position = new PointLatLng(e.Lat.Value, e.Lng.Value);
            }
            FP.MainMap.Invalidate();
        }

        // Slides along its leg: inserted this session, or a tagged insert from a previous one.
        private bool Movable(ProfileWaypoint w) =>
            w.Id >= InsertIdBase || taggedRows.Contains(w.Id);

        // Mission Planner's global handler silently drops some exception types (e.g.
        // InvalidOperationException), which left buttons dead with no clue why. Show them, and
        // re-enable the buttons.
        private async Task Guarded(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                log.Error("MissionElevationForm", ex);
                CustomMessageBox.Show(ex.Message, "Elevation Graph");
                BUT_apply.Enabled = BUT_accept.Enabled = BUT_fit.Enabled = true;
            }
        }

        private ProfileWaypoint Wp(int id) => wps.FirstOrDefault(w => w.Id == id);

        private void SetWpAlt(int id, double altAmsl)
        {
            var wp = Wp(id);
            if (wp != null) wp.AltM = altAmsl;
        }

        private void ApplyOffsets()
        {
            double mult = CurrentState.multiplieralt;
            elev_profile.TargetAglM = (double)Math.Min(NUM_targetlo.Value, NUM_targethi.Value) / mult;
            elev_profile.TargetBandTopM = (double)Math.Max(NUM_targetlo.Value, NUM_targethi.Value) / mult;
            elev_profile.MsaM = floorSurface.Loaded ? (double)NUM_msa.Value / mult : double.NaN;
            elev_profile.CeilingM = ceilingSurface.Loaded ? (double)NUM_ceiling.Value / mult : double.NaN;
            elev_profile.GradYellowPct = (double)NUM_gradwarn.Value;
            elev_profile.GradRedPct = (double)NUM_gradmax.Value;
            elev_profile.Invalidate();
        }

        // Metres added to an altitude in this frame to get AMSL.
        private double FrameOffset(MAVLink.MAV_FRAME frame, double terr)
        {
            switch (frame)
            {
                case MAVLink.MAV_FRAME.GLOBAL:
                case MAVLink.MAV_FRAME.GLOBAL_INT:
                    return 0;
                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT:
                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT:
                    return terr;
                default:
                    return homeAmsl;
            }
        }

        private async Task LoadMission()
        {
            var fp = plugin.Host.MainForm.FlightPlanner;
            var toLoc = fp.GetType().GetMethod("DataViewtoLocationwp", BindingFlags.NonPublic | BindingFlags.Instance);
            homeAmsl = double.Parse(fp.TXT_homealt.Text) / CurrentState.multiplieralt;

            var rows = new List<(int row, Locationwp loc)>();
            try
            {
                for (int i = 0; i < fp.Commands.Rows.Count; i++)
                {
                    var loc = (Locationwp)toLoc.Invoke(fp, new object[] { i });
                    if (loc.id < (ushort)MAVLink.MAV_CMD.LAST && (loc.lat != 0 || loc.lng != 0))
                    {
                        rows.Add((i, loc));
                        if (InsertRowTag.Equals(loc.Tag)) taggedRows.Add(i);
                    }
                }
            }
            catch (TargetInvocationException ex)
            {
                CustomMessageBox.Show(ex.InnerException?.Message ?? ex.Message, "Elevation Graph");
                Close();
                return;
            }

            if (rows.Count == 0)
            {
                CustomMessageBox.Show("No positioned waypoints in the mission.", "Elevation Graph");
                Close();
                return;
            }

            await Task.Run(() =>
            {
                string cacheDir = Path.Combine(MissionPlanner.Utilities.Settings.GetUserDataDirectory(), "DSM", "cache");
                floorSurface.Load(plugin.CorridorFloorSurfacePath, cacheDir);
                ceilingSurface.Load(plugin.CorridorCeilingSurfacePath, cacheDir);

                foreach (var (row, loc) in rows)
                {
                    var cmd = (MAVLink.MAV_CMD)loc.id;
                    var frame = (MAVLink.MAV_FRAME)loc.frame;
                    double terr = ProfileBuilder.GetTerrainAlt(loc.lat, loc.lng);
                    info[row] = (frame, terr);
                    wps.Add(new ProfileWaypoint
                    {
                        Command = cmd,
                        Lat = loc.lat,
                        Lng = loc.lng,
                        P1 = loc.p1, P2 = loc.p2, P3 = loc.p3, P4 = loc.p4,
                        AltM = loc.alt + FrameOffset(frame, terr),
                        TerrainAltM = terr,
                        Id = row,
                    });
                }
            });

            ApplyOffsets();
            await Rebuild(false);
            GuessFitRegion();
        }

        private async Task ApplyTarget()
        {
            // Middle of the band.
            double target = (double)(NUM_targetlo.Value + NUM_targethi.Value) / 2 / CurrentState.multiplieralt;
            foreach (var wp in wps) wp.AltM = wp.TerrainAltM + target;
            await Rebuild(true);
        }

        private async Task Rebuild(bool preserveView)
        {
            BUT_apply.Enabled = BUT_accept.Enabled = BUT_fit.Enabled = false;
            var home = plugin.Host.cs.PlannedHomeLocation;
            var list = wps.ToList();
            samples = await Task.Run(() =>
            {
                var pts = ProfileBuilder.Build(list, home, 0);

                // A plain loiter's target height is taken at its orbit exit, so Apply target
                // lands the bar on the target line there.
                foreach (var anchor in pts.Where(p => p.IsLoiterWaypoint && !p.IsLoiterToAlt))
                {
                    var exit = pts.LastOrDefault(p => p.IsLoiterArcSample && p.WaypointIndex == anchor.WaypointIndex);
                    if (exit != null) list.First(w => w.Id == anchor.WaypointIndex).TerrainAltM = exit.TerrainAlt;
                }

                foreach (var p in pts)
                {
                    if (p.IsLineWaypoint && Movable(list.First(w => w.Id == p.WaypointIndex)))
                        p.IsInserted = true;
                    p.FloorSurfaceAmsl = floorSurface.SampleAmsl(p.Lat, p.Lng) ?? double.NaN;
                    p.CeilingSurfaceAmsl = ceilingSurface.SampleAmsl(p.Lat, p.Lng) ?? double.NaN;
                }
                return pts;
            });

            ApplyOffsets();
            elev_profile.SetData(samples, (double)NUM_msa.Value / CurrentState.multiplieralt,
                (double)NUM_ceiling.Value / CurrentState.multiplieralt, preserveView);
            BUT_apply.Enabled = BUT_accept.Enabled = BUT_fit.Enabled = true;
        }

        // ─── Fit to band ─────────────────────────────────────────────────────────────
        // Waypoints inside the fit region get a box: terrain + band, tightened by MSA and
        // ceiling. Those outside it (and loiter-to-alts, which change height on the spot) stay
        // fixed. The fit pulls a taut string through the boxes, which minimises the steepest
        // leg. Only waypoints are constrained, so the result reports legs that leave the band
        // between them.

        private (double lo, double hi) BandM()
        {
            double mult = CurrentState.multiplieralt;
            return ((double)Math.Min(NUM_targetlo.Value, NUM_targethi.Value) / mult,
                    (double)Math.Max(NUM_targetlo.Value, NUM_targethi.Value) / mult);
        }

        // Guess the cruise stretch: first to last station that has climbed to 75% of the band
        // floor AGL, so the takeoff and landing ends stay fixed.
        // ponytail: crude heuristic; the user corrects it by dragging the flags.
        private void GuessFitRegion()
        {
            var st = Stations().ToList();
            double climbed = BandM().lo * 0.75;
            bool Up(ElevationPoint s) => s.AltRelM - Wp(s.WaypointIndex).TerrainAltM >= climbed;
            int a = st.FindIndex(Up), b = st.FindLastIndex(Up);
            if (a < 0) { a = 0; b = st.Count - 1; }
            elev_profile.FitStartM = a > 0 ? (EndDist(st[a - 1]) + st[a].DistM) / 2 : 0;
            elev_profile.FitEndM = b < st.Count - 1 ? (EndDist(st[b]) + st[b + 1].DistM) / 2 : samples.Max(s => s.DistM);
            elev_profile.Invalidate();
        }

        private async Task FitToBand()
        {
            if (samples == null || elev_profile.FitStartM == null) return;
            foreach (int id in suggested) RemoveInsert(id);
            suggested.Clear();

            var (bandLo, bandHi) = BandM();
            double mult = CurrentState.multiplieralt;
            double msa = floorSurface.Loaded ? (double)NUM_msa.Value / mult : double.NaN;
            double ceilOff = ceilingSurface.Loaded ? (double)NUM_ceiling.Value / mult : double.NaN;
            double start = elev_profile.FitStartM.Value, end = elev_profile.FitEndM.Value;

            // Terrain + band, tightened by MSA and ceiling (NaN = none), plus the ceiling alone as
            // the cap the fit may climb to when the band and the gradient limit can't both hold.
            (double lo, double hi, double cap) Box(double terr, ElevationPoint s)
            {
                double floor = s.FloorSurfaceAmsl + msa, ceil = s.CeilingSurfaceAmsl + ceilOff;
                double l = terr + bandLo, u = terr + bandHi;
                if (!double.IsNaN(floor)) l = Math.Max(l, floor);
                if (!double.IsNaN(ceil)) u = Math.Min(u, ceil);
                if (l <= u) return (l, u, double.IsNaN(ceil) ? double.PositiveInfinity : ceil);
                // Clash: nearest the band middle, MSA winning over ceiling.
                double v = terr + (bandLo + bandHi) / 2;
                if (!double.IsNaN(ceil)) v = Math.Min(v, ceil);
                if (!double.IsNaN(floor)) v = Math.Max(v, floor);
                return (v, v, v);
            }

            var nodes = new List<FitNode>();
            int clashes = 0;
            foreach (var s in Stations())
            {
                if (s.IsLoiterToAlt)
                {
                    nodes.Add(FitNode.Fixed(s.DistM, s.DistM, s.LtaStartAltRelM));
                    nodes.Add(FitNode.Fixed(EndDist(s), EndDist(s), s.AltRelM));
                    continue;
                }
                if (s.DistM < start || s.DistM > end)
                {
                    nodes.Add(FitNode.Fixed(s.DistM, EndDist(s), s.AltRelM));
                    continue;
                }
                var box = Box(Wp(s.WaypointIndex).TerrainAltM, s);
                bool clash = box.lo == box.hi;
                if (s.IsLoiterWaypoint)
                {
                    // Flat from entry to exit, so the band has to hold at both ends; where it
                    // can't, keep the exit's (the loiter's reference) and count it.
                    var arc = samples.Where(p => p.IsLoiterArcSample && p.WaypointIndex == s.WaypointIndex)
                                     .OrderBy(p => p.DistM).ToList();
                    if (arc.Count > 0)
                    {
                        var a = Box(arc[0].TerrainAlt, arc[0]);
                        var b = Box(arc[arc.Count - 1].TerrainAlt, arc[arc.Count - 1]);
                        double l = Math.Max(a.lo, b.lo), u = Math.Min(a.hi, b.hi);
                        box = l <= u ? (l, u, Math.Min(a.cap, b.cap)) : b;
                        clash |= l > u || box.lo == box.hi;
                    }
                }
                if (clash) clashes++;
                nodes.Add(new FitNode { WpId = s.WaypointIndex, X0 = s.DistM, X1 = EndDist(s), Lo = box.lo, Hi = box.hi, Cap = box.cap, H = s.AltRelM });
            }

            // Leg samples in the region: where the band is checked between waypoints, and where
            // a waypoint can be added. Clashing samples can't be satisfied, so they're left out.
            var track = new List<FitNode>();
            foreach (var p in samples.Where(p => p.IsLegTerrainSample && p.DistM >= start && p.DistM <= end).OrderBy(p => p.DistM))
            {
                var box = Box(p.TerrainAlt, p);
                if (box.lo < box.hi)
                    track.Add(new FitNode { Sample = p, X0 = p.DistM, X1 = p.DistM, Lo = box.lo, Hi = box.hi, Cap = box.cap, H = (box.lo + box.hi) / 2 });
            }

            BUT_apply.Enabled = BUT_accept.Enabled = BUT_fit.Enabled = false;
            LBL_fit.Text = "Fitting...";
            bool addWps = CHK_suggest.Checked;
            double gradMax = (double)NUM_gradmax.Value;
            var (all, h) = await Task.Run(() =>
            {
                var a = BandFit.Fit(nodes, track, addWps, gradMax, out var hh);
                return (a, hh);
            });

            // In track order, so each insert lands after the one before it on the same leg.
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                if (n.WpId >= 0) Wp(n.WpId).AltM = h[i];
                else if (n.Sample != null)
                {
                    int id = AddInsert(n.X0, h[i], n.Sample.Lat, n.Sample.Lng, n.Sample.TerrainAlt);
                    if (id >= 0) suggested.Add(id);
                }
            }

            var (maxGrad, outLegs) = BandFit.Stats(all, h, track);
            LBL_fit.Text = $"Max grad {maxGrad:F1}%" + (maxGrad > gradMax + 0.05 ? $" (over the {gradMax:F1}% limit)" : "")
                + (suggested.Count > 0 ? $" · {suggested.Count} waypoint(s) added" : "")
                + (outLegs > 0 ? $" · {outLegs} leg(s) leave the band between waypoints" : "")
                + (clashes > 0 ? $" · {clashes} waypoint(s) where band clashes with MSA/ceiling" : "");
            await Rebuild(true);
        }

        // ─── Inserted waypoints ─────────────────────────────────────────────────────
        // Inserts sit on a straight leg, so the ground track doesn't change: the sample is
        // added/moved directly (no re-sample) with geo + terrain interpolated off the track.

        private IEnumerable<ElevationPoint> Stations(ElevationPoint except = null) =>
            samples.Where(s => (s.IsLineWaypoint || s.IsLoiterWaypoint) && s != except).OrderBy(s => s.DistM);

        private static double EndDist(ElevationPoint s) => s.DistM + (s.IsLoiterWaypoint ? s.LoiterArcLengthM : 0);

        // The straight leg containing distM, as its bracketing stations; false on a loiter arc
        // or outside the mission.
        private bool TryLeg(double distM, ElevationPoint except, out ElevationPoint before, out ElevationPoint after)
        {
            var st = Stations(except).ToList();
            before = st.LastOrDefault(s => s.DistM <= distM);
            after = st.FirstOrDefault(s => s.DistM > distM);
            return before != null && after != null && EndDist(before) < distM;
        }

        // Lat/lng/terrain at distM, interpolated between the flown-track samples around it.
        private (double lat, double lng, double terr) TrackAt(double distM)
        {
            var track = samples.Where(s => s.IsLegTerrainSample || (s.IsLineWaypoint && !s.IsInserted) || s.IsLoiterArcSample)
                               .OrderBy(s => s.DistM).ToList();
            var a = track.LastOrDefault(s => s.DistM <= distM) ?? track.First();
            var b = track.FirstOrDefault(s => s.DistM > distM) ?? track.Last();
            double f = b.DistM > a.DistM ? (distM - a.DistM) / (b.DistM - a.DistM) : 0;
            return (a.Lat + f * (b.Lat - a.Lat), a.Lng + f * (b.Lng - a.Lng),
                    a.TerrainAlt + f * (b.TerrainAlt - a.TerrainAlt));
        }

        private void ElevProfile_InsertRequested(object sender, WaypointInsertEventArgs e)
        {
            if (samples == null) return;
            var (lat, lng, terr) = TrackAt(e.DistM);
            AddInsert(e.DistM, e.AltRelM, lat, lng, terr);
            elev_profile.Invalidate();
        }

        // Adds a WAYPOINT on the straight leg at distM (AMSL altitude); returns its id, or -1 if
        // distM isn't on a straight leg.
        private int AddInsert(double distM, double altAmsl, double lat, double lng, double terr)
        {
            if (!TryLeg(distM, null, out var before, out _)) return -1;
            int id = nextInsertId++;
            info[id] = (info[before.WaypointIndex].frame, terr);   // inherit the previous row's frame
            wps.Insert(wps.IndexOf(Wp(before.WaypointIndex)) + 1, new ProfileWaypoint
            {
                Command = MAVLink.MAV_CMD.WAYPOINT,
                Lat = lat,
                Lng = lng,
                AltM = altAmsl,
                TerrainAltM = terr,
                Id = id,
            });
            samples.Add(new ElevationPoint
            {
                DistM = distM,
                AltRelM = altAmsl,
                TerrainAlt = terr,
                IsLineWaypoint = true,
                IsInserted = true,
                WaypointIndex = id,
                Lat = lat,
                Lng = lng,
            });
            return id;
        }

        private void ElevProfile_InsertedMoved(object sender, InsertedWaypointMoveEventArgs e)
        {
            var wp = Wp(e.WaypointIndex);
            var sample = samples?.FirstOrDefault(s => s.IsInserted && s.WaypointIndex == e.WaypointIndex);
            if (wp == null || sample == null) return;

            // Clamp to its own leg so the mission order stays valid.
            if (!TryLeg(sample.DistM, sample, out var before, out var after)) return;
            double lo = EndDist(before), hi = after.DistM, margin = (hi - lo) * 1e-4;
            double dist = Math.Max(lo + margin, Math.Min(hi - margin, e.NewDistM));

            var (lat, lng, terr) = TrackAt(dist);
            sample.DistM = dist;
            sample.AltRelM = wp.AltM = e.NewAltRelM;
            sample.Lat = wp.Lat = lat;
            sample.Lng = wp.Lng = lng;
            sample.TerrainAlt = wp.TerrainAltM = terr;
            info[e.WaypointIndex] = (info[e.WaypointIndex].frame, terr);
            elev_profile.Invalidate();
        }

        private void ElevProfile_RemoveRequested(object sender, WaypointRemoveEventArgs e)
        {
            if (e.WaypointIndex < InsertIdBase) removedRows.Add(e.WaypointIndex);   // tagged row
            RemoveInsert(e.WaypointIndex);
            elev_profile.Invalidate();
        }

        private void RemoveInsert(int id)
        {
            wps.RemoveAll(w => w.Id == id);
            samples?.RemoveAll(s => s.IsInserted && s.WaypointIndex == id);
        }

        // ─── Accept ─────────────────────────────────────────────────────────────────

        private void Accept()
        {
            var fp = plugin.Host.MainForm.FlightPlanner;
            int altCol = fp.Commands.Columns["Alt"].Index;
            int frameCol = fp.Commands.Columns["Frame"].Index;
            int latCol = fp.Commands.Columns["Lat"].Index;
            int lngCol = fp.Commands.Columns["Lon"].Index;
            double mult = CurrentState.multiplieralt;

            // Index 0 = keep each row's frame; otherwise one frame for every waypoint.
            MAVLink.MAV_FRAME? forced = CMB_frame.SelectedIndex == 0 ? (MAVLink.MAV_FRAME?)null
                : new[] { MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, MAVLink.MAV_FRAME.GLOBAL,
                          MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT }[CMB_frame.SelectedIndex - 1];

            (MAVLink.MAV_FRAME frame, double alt) Out(ProfileWaypoint wp)
            {
                var (frame, terr) = info[wp.Id];
                frame = forced ?? frame;
                return (frame, Math.Round((wp.AltM - FrameOffset(frame, terr)) * mult, 1));
            }

            // Existing rows first, while their indices are still valid.
            foreach (var wp in wps.Where(w => w.Id < InsertIdBase))
            {
                var (frame, alt) = Out(wp);
                var row = fp.Commands.Rows[wp.Id];
                row.Cells[altCol].Value = alt;
                row.Cells[frameCol].Value = (int)frame;
                if (Movable(wp))
                {
                    row.Cells[latCol].Value = wp.Lat;
                    row.Cells[lngCol].Value = wp.Lng;
                }
            }

            // Then row deletes (tagged inserts removed here) and new inserts, from the bottom up
            // so lower row indices stay valid; on a tie the delete goes first. Each insert goes
            // just before the next existing waypoint (so DO_ commands stay attached to the leg
            // they were on), or after the last one.
            var ops = removedRows.Select(r => (row: r, del: true, wp: (ProfileWaypoint)null)).ToList();
            for (int i = wps.Count - 1; i >= 0; i--)
            {
                if (wps[i].Id < InsertIdBase) continue;
                var next = wps.Skip(i + 1).FirstOrDefault(w => w.Id < InsertIdBase);
                var prev = wps.Take(i).LastOrDefault(w => w.Id < InsertIdBase);
                ops.Add((next?.Id ?? prev.Id + 1, false, wps[i]));
            }
            // Stable sort keeps same-row inserts last-first, which lands them in mission order.
            foreach (var op in ops.OrderByDescending(o => o.row).ThenByDescending(o => o.del))
            {
                if (op.del)
                {
                    fp.Commands.Rows.RemoveAt(op.row);
                    continue;
                }
                var (frame, alt) = Out(op.wp);
                // FlightPlanner truncates a WAYPOINT's altitude to whole units; round it first.
                fp.InsertCommand(op.row, MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, op.wp.Lng, op.wp.Lat, Math.Round(alt), InsertRowTag);
                fp.Commands.Rows[op.row].Cells[frameCol].Value = (int)frame;
            }

            var cfg = MissionPlanner.Utilities.Settings.Instance;
            cfg[BandLoKey] = ((double)Math.Min(NUM_targetlo.Value, NUM_targethi.Value) / mult).ToString();
            cfg[BandHiKey] = ((double)Math.Max(NUM_targetlo.Value, NUM_targethi.Value) / mult).ToString();
            cfg[MsaKey] = ((double)NUM_msa.Value / mult).ToString();
            cfg[CeilingKey] = ((double)NUM_ceiling.Value / mult).ToString();
            cfg[GradWarnKey] = NUM_gradwarn.Value.ToString();
            cfg[GradMaxKey] = NUM_gradmax.Value.ToString();

            fp.writeKML();
            log.Info($"MissionElevationForm: updated {wps.Count} waypoints (frame: {CMB_frame.Text}).");
            Close();
        }
    }
}
