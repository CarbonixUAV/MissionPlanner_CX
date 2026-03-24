using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using MissionPlanner;
using MissionPlanner.Maps;
using MissionPlanner.Utilities;
using Carbonix.Planning;
using Carbonix.UI;
using log4net;

// SharpKml
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Carbonix
{
    public partial class CorridorPlanForm : Form
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly CarbonixPlugin plugin;

        // Loaded main-line segments and branch segments, in load order, plus the
        // chained/attached results used for mission generation.
        private List<string> mainLineSegmentFiles = new List<string>();
        private List<string> branchSegmentFiles = new List<string>();
        private List<List<PointLatLngAlt>> mainLineSegments = new List<List<PointLatLngAlt>>();
        private List<List<PointLatLngAlt>> branchSegmentsRaw = new List<List<PointLatLngAlt>>();

        // Chained main line (auto-joined segments, with junction vertices inserted
        // for branch attachments) and the branches snapped onto it.
        private List<PointLatLngAlt> mainLine;
        private List<BranchAttachment> branchAttachments = new List<BranchAttachment>();

        // Last generated mission waypoints
        private List<CorridorWaypoint> generatedWps;

        // Elevation profile data — drives the elevation chart.
        // Contains waypoint anchors (dots/bars) and terrain fill points.
        private List<ElevationPoint> elevationPoints;

        // Home terrain altitude (m) — computed once per generation
        private double homeTerrainAlt;

        // Tracks which mainLine indices were added interactively (right-click insert).
        // Indices are kept in sync when vertices are inserted or removed.
        private readonly HashSet<int> _insertedVertexIndices = new HashSet<int>();

        // View/alt-edit preservation across geometry-triggered regenerations.
        // Set by the geometry-change handlers before calling BUT_generate_Click;
        // consumed and cleared inside BUT_generate_Click.
        private bool _preserveProfileView;
        private bool _preserveAltEdits;
        // key = (corridorVertexIndex, isLoiter), value = AltRelM (altitude relative to home, metres).
        private Dictionary<(int idx, bool isLoiter), double> _savedAltEdits;

        // Map overlays
        private readonly GMapOverlay layer_corridor;

        // Mission path overlay: full waypoint path with proper loiter arcs, drawn underneath
        // the per-line colour overlay so transitions and circles are visible at all zoom levels.
        private readonly WPOverlay2 _missionOverlay;

        // Suppress control-change handlers while initialising
        private bool freeze_handlers = true;

        public CorridorPlanForm(CarbonixPlugin plugin)
        {
            this.plugin = plugin;

            InitializeComponent();

            _missionOverlay = new WPOverlay2 { ShowPlusMarkers = false };
            layer_corridor = new GMapOverlay("corridor");
            map.Overlays.Add(_missionOverlay.overlay);
            map.Overlays.Add(layer_corridor);

            map.MapProvider = plugin.Host.FDMapType;
            map.MouseDown += map_MouseDown;
            map.MouseMove += map_MouseMove;
            map.MouseUp   += map_MouseUp;
            map.OnMapZoomChanged += () => SyncProfileToMapExtent();

            // Wire up the custom elevation profile control
            elev_profile.AltitudeChanged         += ElevProfile_AltitudeChanged;
            elev_profile.WaypointInsertRequested  += ElevProfile_WaypointInsertRequested;
            elev_profile.InsertedWaypointMoved    += ElevProfile_InsertedWaypointMoved;
            elev_profile.WaypointRemoveRequested  += ElevProfile_WaypointRemoveRequested;
        }

        // ─── Form load ────────────────────────────────────────────────────────────

        private void CorridorPlanForm_Load(object sender, EventArgs e)
        {
            map.Position = plugin.Host.FPGMapControl.Position;
            map.Zoom = plugin.Host.FPGMapControl.Zoom;

            SetAltUnits();
            RecalcCoverage();

            freeze_handlers = false;
        }

        private void SetAltUnits()
        {
            string unit = CurrentState.AltUnit;
            lbl_unit1.Text = unit;
            lbl_unit2.Text = unit;
            lbl_unit3.Text = unit;

            double mult = CurrentState.multiplieralt;
            freeze_handlers = true;
            NUM_minalgl.Value = (decimal)(50 * mult);
            NUM_maxagl.Value = (decimal)(120 * mult);
            NUM_defagl.Value = (decimal)(80 * mult);
            NUM_minalgl.Maximum = (decimal)(3000 * mult);
            NUM_maxagl.Maximum = (decimal)(3000 * mult);
            NUM_defagl.Maximum = (decimal)(3000 * mult);
            freeze_handlers = false;
        }

        // ─── File loading ─────────────────────────────────────────────────────────

        private static List<PointLatLngAlt> LoadCorridorFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".kml" || ext == ".kmz")
                return LoadKml(path);
            if (ext == ".shp")
                return LoadShp(path);
            return null;
        }

        private void BUT_mainline_add_Click(object sender, EventArgs e)
        {
            AddSegmentFiles(mainLineSegmentFiles, mainLineSegments, LST_mainline);
        }

        private void BUT_mainline_remove_Click(object sender, EventArgs e)
        {
            RemoveSelectedSegments(mainLineSegmentFiles, mainLineSegments, LST_mainline);
        }

        private void BUT_branches_add_Click(object sender, EventArgs e)
        {
            AddSegmentFiles(branchSegmentFiles, branchSegmentsRaw, LST_branches);
        }

        private void BUT_branches_remove_Click(object sender, EventArgs e)
        {
            RemoveSelectedSegments(branchSegmentFiles, branchSegmentsRaw, LST_branches);
        }

        private void AddSegmentFiles(List<string> files, List<List<PointLatLngAlt>> segments, ListBox listBox)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Open Corridor File(s)";
                dlg.Filter = "All Supported|*.kml;*.kmz;*.shp|KML/KMZ|*.kml;*.kmz|Shapefile|*.shp";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                foreach (var path in dlg.FileNames)
                {
                    List<PointLatLngAlt> pts;
                    try
                    {
                        pts = LoadCorridorFile(path);
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show("Error loading file:\n" + ex.Message, "Load Error");
                        continue;
                    }

                    if (pts == null || pts.Count < 2)
                    {
                        CustomMessageBox.Show(
                            "No line/polyline geometry found in " + Path.GetFileName(path) + ".\n" +
                            "For KML: ensure the file contains a LineString.\n" +
                            "For SHP: ensure the file contains line features.",
                            "No Corridor Found");
                        continue;
                    }

                    files.Add(path);
                    segments.Add(pts);
                    listBox.Items.Add(Path.GetFileName(path));
                }
            }

            RecomputeNetwork();
            DrawMap();
            ZoomToFitMainLine();
        }

        private void RemoveSelectedSegments(List<string> files, List<List<PointLatLngAlt>> segments, ListBox listBox)
        {
            var selected = listBox.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
            if (selected.Count == 0) return;

            foreach (var i in selected)
            {
                files.RemoveAt(i);
                segments.RemoveAt(i);
                listBox.Items.RemoveAt(i);
            }

            RecomputeNetwork();
            DrawMap();
        }

        private void ZoomToFitMainLine()
        {
            if (mainLine == null || mainLine.Count == 0) return;
            double minLat = mainLine.Min(p => p.Lat), maxLat = mainLine.Max(p => p.Lat);
            double minLng = mainLine.Min(p => p.Lng), maxLng = mainLine.Max(p => p.Lng);
            map.SetZoomToFitRect(new RectLatLng(maxLat, minLng, maxLng - minLng, maxLat - minLat));
        }

        /// <summary>
        /// Rebuilds <c>mainLine</c> (chained main-line segments with junction vertices
        /// inserted for branch attachments) and <c>branchAttachments</c>. Clears any
        /// previously generated mission/profile data since the geometry has changed.
        /// </summary>
        private void RecomputeNetwork()
        {
            generatedWps = null;
            elevationPoints = null;
            _insertedVertexIndices.Clear();
            BUT_accept.Enabled = false;

            if (mainLineSegments.Count == 0)
            {
                mainLine = null;
                branchAttachments = new List<BranchAttachment>();
                return;
            }

            var chained = CorridorPlanner.BuildMainLine(mainLineSegments, out var unconnected);
            if (unconnected.Count > 0)
            {
                var names = unconnected.Select(i => Path.GetFileName(mainLineSegmentFiles[i]));
                CustomMessageBox.Show(
                    "The following main line segment(s) could not be connected to the main chain " +
                    "(no endpoint within tolerance):\n\n" + string.Join("\n", names),
                    "Unconnected Segments");
            }

            mainLine = chained;
            branchAttachments = CorridorPlanner.AttachBranches(ref mainLine, branchSegmentsRaw);
        }

        // ── KML/KMZ loader ───────────────────────────────────────────────────────

        private static List<PointLatLngAlt> LoadKml(string path)
        {
            KmlFile kml;
            if (path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                using (var kmz = KmzFile.Open(File.OpenRead(path)))
                    kml = KmlFile.LoadFromKmz(kmz);
            }
            else
            {
                using (var stream = File.OpenRead(path))
                    kml = KmlFile.Load(stream);
            }

            var lineString = FindFirstLineString(kml.Root);
            if (lineString == null) return null;

            var result = new List<PointLatLngAlt>();
            foreach (var coord in lineString.Coordinates)
            {
                result.Add(new PointLatLngAlt(
                    coord.Latitude,
                    coord.Longitude,
                    coord.Altitude.HasValue ? coord.Altitude.Value : 0));
            }
            return result;
        }

        private static LineString FindFirstLineString(Element element)
        {
            if (element is LineString ls) return ls;
            foreach (var child in element.Flatten().OfType<LineString>())
                return child;
            return null;
        }

        // ── SHP loader ───────────────────────────────────────────────────────────

        private static List<PointLatLngAlt> LoadShp(string path)
        {
            var fs = DotSpatial.Data.FeatureSet.Open(path);
            if (fs == null || fs.Features.Count == 0)
                return null;

            DotSpatial.Projections.ProjectionInfo srcProj = null;
            string prjPath = Path.ChangeExtension(path, ".prj");
            if (File.Exists(prjPath))
                srcProj = DotSpatial.Projections.ProjectionInfo.Open(prjPath);

            var wgs84 = DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

            foreach (var feature in fs.Features)
            {
                var geom = feature.Geometry;
                if (geom == null) continue;
                var coords = geom.Coordinates;
                if (coords == null || coords.Length < 2) continue;
                if (geom.GeometryType != "LineString" && geom.GeometryType != "MultiLineString")
                    continue;

                double[] xs = coords.Select(c => c.X).ToArray();
                double[] ys = coords.Select(c => c.Y).ToArray();
                double[] zs = coords.Select(c => c.Z).ToArray();

                if (srcProj != null && !srcProj.Equals(wgs84))
                {
                    double[] xy = new double[xs.Length * 2];
                    for (int i = 0; i < xs.Length; i++) { xy[i * 2] = xs[i]; xy[i * 2 + 1] = ys[i]; }
                    DotSpatial.Projections.Reproject.ReprojectPoints(xy, zs, srcProj, wgs84, 0, xs.Length);
                    for (int i = 0; i < xs.Length; i++) { xs[i] = xy[i * 2]; ys[i] = xy[i * 2 + 1]; }
                }

                var result = new List<PointLatLngAlt>(xs.Length);
                for (int i = 0; i < xs.Length; i++)
                    result.Add(new PointLatLngAlt(ys[i], xs[i], double.IsNaN(zs[i]) ? 0 : zs[i]));
                return result;
            }

            return null;
        }

        // ─── Map drawing ──────────────────────────────────────────────────────────

        private void DrawMap()
        {
            layer_corridor.Routes.Clear();
            layer_corridor.Markers.Clear();

            if (mainLine == null || mainLine.Count < 2)
            {
                _missionOverlay.overlay.Clear();
                map.Refresh();
                return;
            }

            layer_corridor.Routes.Add(new GMapRoute(
                mainLine.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList(),
                "centerline")
            {
                Stroke = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }
            });

            foreach (var br in branchAttachments)
            {
                layer_corridor.Routes.Add(new GMapRoute(
                    br.Points.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList(),
                    "branch" + br.BranchId)
                {
                    Stroke = new Pen(Color.Cyan, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }
                });

                var junction = mainLine[br.MainLineVertexIndex];
                layer_corridor.Markers.Add(new GMarkerGoogle(
                    new PointLatLng(junction.Lat, junction.Lng),
                    GMarkerGoogleType.yellow_small));
            }

            if (generatedWps == null)
            {
                _missionOverlay.overlay.Clear();
                map.Refresh();
                return;
            }

            // ── Full mission path via WPOverlay2 ──────────────────────────────────
            // Converts CorridorWaypoints to Locationwp items and feeds them through
            // MissionSegmentizer.  With VehicleClass.Plane the segmentizer renders
            // loiter circles as proper entry→arc→exit paths instead of just dots,
            // and draws the straight transition legs between flight lines.
            // Numbered WP markers are suppressed — there are too many in a corridor.
            var homePoint = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation
                : mainLine.First();
            var home = new PointLatLngAlt(homePoint.Lat, homePoint.Lng, 0);

            var locationWps = generatedWps.Select(wp => new Locationwp
            {
                id  = (ushort)wp.Command,
                p1  = wp.P1,
                p2  = wp.P2,
                p3  = wp.P3,
                p4  = wp.P4,
                lat = wp.Lat,
                lng = wp.Lng,
                alt = (float)wp.AltRelM,
            }).ToList();

            _missionOverlay.VehicleClass = VehicleClass.Plane;
            _missionOverlay.CreateOverlay(home, locationWps,
                wpradius: 30, loiterradius: 0,
                altunitmultiplier: CurrentState.multiplieralt);

            map.Refresh();
        }

        // ─── Map pan/drag ─────────────────────────────────────────────────────────

        private PointLatLng mouseDownStart;
        private bool isMouseDown;

        private void map_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mouseDownStart = map.FromLocalToLatLng(e.X, e.Y);
                isMouseDown = true;
            }
        }

        private void map_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isMouseDown)
            {
                PointLatLng cur = map.FromLocalToLatLng(e.X, e.Y);
                map.Position = new PointLatLng(
                    map.Position.Lat + mouseDownStart.Lat - cur.Lat,
                    map.Position.Lng + mouseDownStart.Lng - cur.Lng);
            }
        }

        private void map_MouseUp(object sender, MouseEventArgs e)
        {
            isMouseDown = false;
            SyncProfileToMapExtent();
        }

        // ─── Parameter change handlers ────────────────────────────────────────────

        private void AltParams_ValueChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;
            RecalcCoverage();
        }

        private void CorridorParams_ValueChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;
            RecalcCoverage();
        }

        private void TurnParams_ValueChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;
            RecalcCoverage();
        }

        // ─── Coverage & turn info ─────────────────────────────────────────────────

        private void RecalcCoverage()
        {
            int n = (int)NUM_numpasses.Value;
            double offset = (double)NUM_passoffset.Value;
            bool hasCentre = (n % 2) == 1;
            int perSide = n / 2;

            string layout;
            if (n == 1)
                layout = "centreline only";
            else if (hasCentre)
                layout = perSide == 1
                    ? $"centreline \u00b1 {offset:F0} m"
                    : $"centreline \u00b1 {offset:F0} m \u2026 \u00b1 {offset * perSide:F0} m";
            else
                layout = perSide == 1
                    ? $"\u00b1 {offset:F0} m"
                    : $"\u00b1 {offset:F0} m \u2026 \u00b1 {offset * perSide:F0} m";

            lbl_coverage.Text = $"{n} pass{(n == 1 ? "" : "es")}: {layout}";
            lbl_coverage.ForeColor = SystemColors.ControlText;

            double lowThresh = (double)NUM_low_thresh.Value;
            double highThresh = (double)NUM_high_thresh.Value;
            double turnRadius = (double)NUM_turnradius.Value;
            double cornerRadius = (double)NUM_cornerradius.Value;

            lbl_turninfo.Text =
                $"<{lowThresh}\u00b0: straight | {lowThresh}\u00b0\u2013{highThresh}\u00b0: corner cut | \u2265{highThresh}\u00b0: Dubins S-turn\n" +
                $"S-turn radius: {turnRadius:F0} m  |  Corner radius: {cornerRadius:F0} m";
        }

        // ─── Generate ─────────────────────────────────────────────────────────────

        private async void BUT_generate_Click(object sender, EventArgs e)
        {
            if (mainLine == null || mainLine.Count < 2)
            {
                CustomMessageBox.Show("Please load at least one main line segment first.", "No Corridor");
                return;
            }
            await ExecuteGenerateAsync();
        }

        /// <summary>
        /// Runs <see cref="CorridorPlanner.GenerateMission"/> and
        /// <see cref="CorridorPlanner.BuildElevationProfile"/> on a background thread,
        /// then updates the map and elevation profile.  Consumes and clears
        /// <c>_preserveProfileView</c> and <c>_preserveAltEdits</c> if set.
        /// </summary>
        private async System.Threading.Tasks.Task ExecuteGenerateAsync()
        {
            if (mainLine == null || mainLine.Count < 2) return;

            var p = BuildParameters();
            if (p == null) return;

            PointLatLngAlt homePoint = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation
                : mainLine.First();

            BUT_generate.Enabled = false;
            BUT_accept.Enabled = false;
            lbl_stats.Text = "Fetching terrain data and generating mission…";

            List<CorridorWaypoint> wps;
            List<ElevationPoint> profileSamples;
            double terrAlt;
            try
            {
                var capturedLine     = mainLine;
                var capturedBranches = branchAttachments;
                var capturedP        = p;
                var capturedHome     = homePoint;
                (wps, profileSamples, terrAlt) = await System.Threading.Tasks.Task.Run(() =>
                {
                    double homeT  = CorridorPlanner.GetTerrainAlt(capturedHome.Lat, capturedHome.Lng);
                    var generated = CorridorPlanner.GenerateMission(capturedLine, capturedP, capturedHome, capturedBranches);
                    var profile   = CorridorPlanner.BuildElevationProfile(capturedLine, capturedHome, capturedP, capturedBranches);
                    return (generated, profile, homeT);
                });
            }
            catch (Exception ex)
            {
                log.Error("CorridorPlanner.GenerateMission failed", ex);
                CustomMessageBox.Show("Error generating mission:\n" + ex.Message, "Error");
                BUT_generate.Enabled = true;
                return;
            }
            finally
            {
                BUT_generate.Enabled = true;
            }

            if (wps.Count == 0)
            {
                CustomMessageBox.Show("No waypoints were generated. Check parameters.", "No Waypoints");
                return;
            }

            generatedWps      = wps;
            elevationPoints = profileSamples;
            homeTerrainAlt    = terrAlt;

            // Mark any centreline samples whose corridor vertex was user-inserted.
            foreach (var s in elevationPoints)
                if (s.IsLineWaypoint && _insertedVertexIndices.Contains(s.WaypointIndex))
                    s.IsInserted = true;

            // Restore altitude edits that were saved before the geometry change.
            if (_preserveAltEdits)
                RestoreAltEdits();

            bool preserveView = _preserveProfileView;
            _preserveProfileView = false;
            _preserveAltEdits    = false;
            _savedAltEdits       = null;

            DrawMap();
            UpdateElevationProfile(p, preserveView);
            UpdateStats(p);
            BUT_accept.Enabled = true;
        }

        private CorridorParameters BuildParameters()
        {
            double mult = CurrentState.multiplieralt;
            double minAGL = (double)NUM_minalgl.Value / mult;
            double maxAGL = (double)NUM_maxagl.Value / mult;
            double defAGL = (double)NUM_defagl.Value / mult;

            if (minAGL >= maxAGL)
            {
                CustomMessageBox.Show("Min AGL must be less than Max AGL.", "Invalid Parameters");
                return null;
            }
            if (defAGL < minAGL || defAGL > maxAGL)
            {
                CustomMessageBox.Show("Default AGL must be between Min and Max AGL.", "Invalid Parameters");
                return null;
            }

            double low = (double)NUM_low_thresh.Value;
            double high = (double)NUM_high_thresh.Value;
            if (low >= high)
            {
                CustomMessageBox.Show("Corner-cut threshold must be less than Full-orbit threshold.", "Invalid Parameters");
                return null;
            }

            return new CorridorParameters
            {
                MinAGL = minAGL,
                MaxAGL = maxAGL,
                DefaultAGL = defAGL,
                SpeedMs = (double)NUM_speed.Value,
                NumberOfPasses = (int)NUM_numpasses.Value,
                PassOffsetM = (double)NUM_passoffset.Value,
                ReverseDirection = CHK_reverse.Checked,
                CornerCutThresholdDeg = low,
                FullOrbitThresholdDeg = high,
                OverflyDistM = (double)NUM_extension.Value,
                TurnRadiusM = (double)NUM_turnradius.Value,
                CornerCutRadiusM = (double)NUM_cornerradius.Value,
            };
        }

        // ─── Elevation profile ────────────────────────────────────────────────────

        private void UpdateElevationProfile(CorridorParameters p, bool preserveView = false)
        {
            if (elevationPoints == null || p == null) return;

            elev_profile.AltMultiplier  = CurrentState.multiplieralt;
            elev_profile.DistMultiplier = CurrentState.multiplierdist;
            elev_profile.AltUnit  = CurrentState.AltUnit;
            elev_profile.DistUnit = CurrentState.DistanceUnit;

            elev_profile.SetData(elevationPoints, p.MinAGL, p.MaxAGL, preserveView);
        }

        // ─── Alt-edit preservation helpers ───────────────────────────────────────

        /// <summary>
        /// Snapshots the current AGL for every line-WP and loiter-WP sample so it
        /// can be reapplied after a geometry-triggered regeneration. Branch-vertex
        /// samples are excluded — their altitude always recomputes fresh from terrain.
        /// </summary>
        private void CaptureAltEdits()
        {
            _savedAltEdits = new Dictionary<(int idx, bool isLoiter), double>();
            if (elevationPoints == null) return;
            foreach (var s in elevationPoints)
            {
                if (s.IsBranchVertex) continue;
                if (s.IsLineWaypoint)
                    _savedAltEdits[(s.WaypointIndex, false)] = s.AltRelM;
                else if (s.IsLoiterWaypoint)
                    _savedAltEdits[(s.WaypointIndex, true)] = s.AltRelM;
            }
        }

        /// <summary>
        /// Reapplies saved AGL values to the freshly-generated <c>elevationPoints</c>
        /// and <c>generatedWps</c>.  Called inside BUT_generate_Click when
        /// <c>_preserveAltEdits</c> is set.
        /// </summary>
        private void RestoreAltEdits()
        {
            if (_savedAltEdits == null) return;

            if (elevationPoints != null)
            {
                foreach (var s in elevationPoints)
                {
                    if (s.IsBranchVertex) continue;

                    double savedAltRelM;
                    if (s.IsLineWaypoint)
                    {
                        if (_savedAltEdits.TryGetValue((s.WaypointIndex, false), out savedAltRelM))
                            s.AltRelM = savedAltRelM;
                    }
                    else if (s.IsLoiterWaypoint)
                    {
                        if (_savedAltEdits.TryGetValue((s.WaypointIndex, true), out savedAltRelM))
                            s.AltRelM = savedAltRelM;
                    }
                    else if (s.IsLoiterArcSample)
                    {
                        // Arc sub-samples share the same AltRelM as their loiter anchor.
                        if (_savedAltEdits.TryGetValue((s.WaypointIndex, true), out savedAltRelM))
                            s.AltRelM = savedAltRelM;
                    }
                }
            }

            // Propagate to generated waypoints so the accept export is also correct.
            // Branch waypoints are skipped — their altitude always recomputes fresh from terrain.
            if (generatedWps != null)
            {
                foreach (var wp in generatedWps)
                {
                    if (wp.IsBranchVertex) continue;
                    bool isLoiterWp = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS;
                    if (!_savedAltEdits.TryGetValue((wp.CorridorVertexIndex, isLoiterWp), out double savedAltRelM))
                        continue;
                    wp.AltRelM = savedAltRelM;
                    wp.AltAGL  = savedAltRelM - (wp.TerrainAltM - homeTerrainAlt);
                }
            }
        }

        // ─── Elevation profile altitude-change event ──────────────────────────────

        private void ElevProfile_AltitudeChanged(object sender, AltChangeEventArgs e)
        {
            if (generatedWps == null) return;

            double newAltRelM = e.NewAltRelM;  // unclamped — user can go above/below min/max guides

            // e.WaypointIndex is the corridor vertex index (0..M-1). Branch vertices are
            // never hit-tested by ElevationProfileControl, so e.WaypointIndex always
            // refers to a mainLine vertex — exclude branch waypoints/samples here so a
            // coincidentally-equal branch-internal index isn't matched too.
            // e.IsLoiter distinguishes whether the user dragged a loiter bar or a line-WP dot.
            // Only propagate to the matching type so lead-in WPs and loiters can have
            // independent altitudes.
            foreach (var wp in generatedWps)
            {
                if (wp.IsBranchVertex) continue;
                if (wp.CorridorVertexIndex != e.WaypointIndex) continue;
                bool isLoiterWp = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS;
                if (e.IsLoiter != isLoiterWp) continue;
                wp.AltRelM = newAltRelM;
                wp.AltAGL  = newAltRelM - (wp.TerrainAltM - homeTerrainAlt);
            }

            // Keep stored centreline samples in sync.
            // ElevationProfileControl has already updated the anchor sample (IsLineWaypoint or
            // IsLoiterWaypoint) and the arc sub-samples (via the loiter branch in HandleDrag).
            // Sync the rest of elevationPoints here using the same loiter/non-loiter filter.
            if (elevationPoints != null)
            {
                foreach (var s in elevationPoints)
                {
                    if (s.IsBranchVertex) continue;
                    if (s.WaypointIndex != e.WaypointIndex) continue;
                    if (e.IsLoiter)
                    {
                        // Loiter bar or arc sub-sample: all share the same AltRelM (constant height).
                        if (s.IsLoiterWaypoint || s.IsLoiterArcSample)
                            s.AltRelM = newAltRelM;
                    }
                    else
                    {
                        if (s.IsLineWaypoint)
                            s.AltRelM = newAltRelM;
                    }
                }
            }
        }

        // ─── Leg-fraction helper (loiter-arc corrected) ───────────────────────────

        /// <summary>
        /// Computes where <paramref name="distM"/> sits along the geographic leg
        /// from <paramref name="before"/> to <paramref name="after"/> as a fraction [0,1].
        ///
        /// The raw profile spans between two consecutive line-WP samples can be
        /// inflated by loiter arc lengths (which represent no geographic displacement).
        /// This method subtracts those arc lengths so the fraction maps correctly
        /// to a position on the straight corridor segment.
        /// </summary>
        private double ComputeLegFraction(double distM, ElevationPoint before, ElevationPoint after)
        {
            // When the preceding boundary is a loiter bar, the straight leg begins at
            // the END of the arc (DistM + LoiterArcLengthM), not at the loiter centre.
            // Using the centre would inflate both legSpan and legOffset by arcLen,
            // making a click right after the bar resolve to a fraction well above 0.
            double beforeEffDistM = before.IsLoiterWaypoint
                ? before.DistM + before.LoiterArcLengthM
                : before.DistM;

            double loitersBefore = 0;
            double loitersTotal  = 0;

            foreach (var s in elevationPoints)
            {
                if (!s.IsLoiterWaypoint || s.LoiterArcLengthM <= 0) continue;
                if (s.DistM < beforeEffDistM || s.DistM >= after.DistM) continue;

                loitersTotal += s.LoiterArcLengthM;

                double arcEnd = s.DistM + s.LoiterArcLengthM;
                if (arcEnd <= distM)
                    loitersBefore += s.LoiterArcLengthM;          // entire arc is before click
                else if (s.DistM < distM)
                    loitersBefore += distM - s.DistM;             // click is inside the arc
            }

            double legSpan   = (after.DistM - beforeEffDistM) - loitersTotal;
            double legOffset = (distM - beforeEffDistM)        - loitersBefore;

            if (legSpan <= 0) return 0.5;
            return Math.Max(0.01, Math.Min(0.99, legOffset / legSpan));
        }

        // ─── Elevation profile insert-waypoint event ──────────────────────────────

        private async void ElevProfile_WaypointInsertRequested(object sender, WaypointInsertEventArgs e)
        {
            if (elevationPoints == null || mainLine == null || mainLine.Count < 2) return;

            _preserveProfileView = true;

            // Find the two adjacent vertex samples that bracket the requested DistM.
            // Include both IsLineWaypoint and IsLoiterWaypoint so that loiter-turn
            // vertices (which have no dot, only a bar) still act as segment boundaries.
            // Branch-vertex samples index into a branch's own point list (not mainLine)
            // and are excluded — they can't act as insertion boundaries.
            var vtxSamples = elevationPoints
                .Where(s => (s.IsLineWaypoint || s.IsLoiterWaypoint) && !s.IsBranchVertex)
                .OrderBy(s => s.DistM)
                .ToList();

            if (vtxSamples.Count < 2) return;

            ElevationPoint before = null, after = null;
            for (int i = 0; i < vtxSamples.Count - 1; i++)
            {
                if (vtxSamples[i].DistM <= e.DistM && vtxSamples[i + 1].DistM >= e.DistM)
                {
                    before = vtxSamples[i];
                    after  = vtxSamples[i + 1];
                    break;
                }
            }
            if (before == null)
            {
                before = vtxSamples[vtxSamples.Count - 2];
                after  = vtxSamples[vtxSamples.Count - 1];
            }

            double t = ComputeLegFraction(e.DistM, before, after);

            int vA = Math.Min(before.WaypointIndex, mainLine.Count - 1);
            int vB = Math.Min(after.WaypointIndex,  mainLine.Count - 1);
            var ptA = mainLine[vA];
            var ptB = mainLine[vB];
            double newLat = ptA.Lat + t * (ptB.Lat - ptA.Lat);
            double newLng = ptA.Lng + t * (ptB.Lng - ptA.Lng);

            int insertIdx = Math.Min(vA, vB) + 1;
            mainLine.Insert(insertIdx, new PointLatLngAlt(newLat, newLng, 0));

            // Keep _insertedVertexIndices in sync.
            var toShift = new List<int>();
            foreach (var i in _insertedVertexIndices)
                if (i >= insertIdx) toShift.Add(i);
            foreach (var i in toShift) { _insertedVertexIndices.Remove(i); _insertedVertexIndices.Add(i + 1); }
            _insertedVertexIndices.Add(insertIdx);

            // Keep branch attachment junction indices in sync with the inserted vertex.
            foreach (var br in branchAttachments)
                if (br.MainLineVertexIndex >= insertIdx) br.MainLineVertexIndex++;

            // Patch elevationPoints in-place — do NOT re-sample terrain.
            // Shift WaypointIndex for every mainLine sample whose corridor vertex moved up.
            // Branch-vertex samples index into their branch's own point list and are unaffected.
            foreach (var s in elevationPoints)
                if (!s.IsBranchVertex && s.WaypointIndex >= insertIdx) s.WaypointIndex++;

            // Interpolate terrain linearly between the bracketing vertex samples.
            double span = after.DistM - before.DistM;
            double tTerr = span > 0 ? (e.DistM - before.DistM) / span : 0.5;
            double newTerrAlt = before.TerrainAlt + tTerr * (after.TerrainAlt - before.TerrainAlt);

            // Insert the new ElevationPoint after the last existing sample with DistM ≤ e.DistM.
            int insertPos = elevationPoints.FindLastIndex(s => s.DistM <= e.DistM) + 1;
            elevationPoints.Insert(insertPos, new ElevationPoint
            {
                DistM          = e.DistM,
                AltRelM        = e.AltRelM,
                TerrainAlt     = newTerrAlt,
                HomeTerrainAlt = before.HomeTerrainAlt,
                IsLineWaypoint = true,
                IsInserted     = true,
                WaypointIndex  = insertIdx,
                Lat            = newLat,
                Lng            = newLng,
            });

            DrawMap();

            // Regenerate mission geometry only — no terrain re-sampling.
            var p = BuildParameters();
            if (p == null) return;

            PointLatLngAlt homePoint = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation
                : mainLine.First();

            BUT_generate.Enabled = false;
            BUT_accept.Enabled   = false;

            List<CorridorWaypoint> wps;
            try
            {
                var capturedLine     = mainLine;
                var capturedBranches = branchAttachments;
                var capturedP        = p;
                wps = await System.Threading.Tasks.Task.Run(() =>
                    CorridorPlanner.GenerateMission(capturedLine, capturedP, homePoint, capturedBranches));
            }
            catch (Exception ex)
            {
                log.Error("CorridorPlanner.GenerateMission failed on WP insert", ex);
                CustomMessageBox.Show("Error generating mission:\n" + ex.Message, "Error");
                BUT_generate.Enabled = true;
                return;
            }
            finally
            {
                BUT_generate.Enabled = true;
            }

            if (wps.Count == 0) return;

            generatedWps = wps;

            // Restore AltRelM from the patched elevationPoints to the new generatedWps.
            // Branch waypoints are skipped — their altitude is computed fresh from
            // terrain by GenerateMission and isn't tracked in elevationPoints here.
            foreach (var wp in generatedWps)
            {
                if (wp.CorridorVertexIndex < 0 || wp.IsBranchVertex) continue;
                bool isLoiterWp = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS;
                var matching = elevationPoints.FirstOrDefault(s =>
                    !s.IsBranchVertex && s.WaypointIndex == wp.CorridorVertexIndex &&
                    (isLoiterWp ? s.IsLoiterWaypoint : s.IsLineWaypoint));
                if (matching == null) continue;
                wp.AltRelM = matching.AltRelM;
                wp.AltAGL  = matching.AltRelM - (wp.TerrainAltM - homeTerrainAlt);
            }

            // Re-mark inserted vertices.
            foreach (var s in elevationPoints)
                if (s.IsLineWaypoint && !s.IsBranchVertex && _insertedVertexIndices.Contains(s.WaypointIndex))
                    s.IsInserted = true;

            bool preserveView = _preserveProfileView;
            _preserveProfileView = false;

            UpdateElevationProfile(p, preserveView);
            UpdateStats(p);
            BUT_accept.Enabled = true;
        }

        // ─── Inserted waypoint XY move ────────────────────────────────────────────

        private async void ElevProfile_InsertedWaypointMoved(object sender, InsertedWaypointMoveEventArgs e)
        {
            if (mainLine == null || elevationPoints == null) return;

            _preserveProfileView = true;

            int vIdx = e.WaypointIndex;
            if (vIdx < 0 || vIdx >= mainLine.Count) return;

            // Include IsLoiterWaypoint so loiter-turn vertices act as movement
            // boundaries — same fix as ElevProfile_WaypointInsertRequested.
            // Branch-vertex samples are excluded — see ElevProfile_WaypointInsertRequested.
            var lineWps = elevationPoints
                .Where(s => (s.IsLineWaypoint || s.IsLoiterWaypoint) && !s.IsBranchVertex)
                .OrderBy(s => s.WaypointIndex)
                .ToList();

            var prevS = lineWps.LastOrDefault(s => s.WaypointIndex < vIdx);
            var nextS = lineWps.FirstOrDefault(s => s.WaypointIndex > vIdx);
            if (prevS == null || nextS == null) return;

            int vA = Math.Min(prevS.WaypointIndex, mainLine.Count - 1);
            int vB = Math.Min(nextS.WaypointIndex, mainLine.Count - 1);
            double t = ComputeLegFraction(e.NewDistM, prevS, nextS);

            var ptA = mainLine[vA];
            var ptB = mainLine[vB];
            mainLine[vIdx] = new PointLatLngAlt(
                ptA.Lat + t * (ptB.Lat - ptA.Lat),
                ptA.Lng + t * (ptB.Lng - ptA.Lng),
                0);

            // Regenerate mission geometry only — do NOT re-sample the terrain profile.
            // Re-sampling would shift the terrain bands (the new WP geographic position
            // has slightly different terrain, and cumulative distances shift downstream).
            // The elevationPoints already have the correct AltRelM and DistM for the
            // dragged WP (set during the drag), so we keep them in place.
            var p = BuildParameters();
            if (p == null) return;

            PointLatLngAlt homePoint = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation
                : mainLine.First();

            BUT_generate.Enabled = false;
            BUT_accept.Enabled   = false;

            List<CorridorWaypoint> wps;
            try
            {
                var capturedLine     = mainLine;
                var capturedBranches = branchAttachments;
                var capturedP        = p;
                wps = await System.Threading.Tasks.Task.Run(() =>
                    CorridorPlanner.GenerateMission(capturedLine, capturedP, homePoint, capturedBranches));
            }
            catch (Exception ex)
            {
                log.Error("CorridorPlanner.GenerateMission failed on WP move", ex);
                CustomMessageBox.Show("Error generating mission:\n" + ex.Message, "Error");
                BUT_generate.Enabled = true;
                return;
            }
            finally
            {
                BUT_generate.Enabled = true;
            }

            if (wps.Count == 0) return;

            generatedWps = wps;

            // Restore AltRelM from the live elevationPoints (which carry the user's
            // drag-adjusted values) to the freshly generated waypoints. Branch waypoints
            // are skipped — their altitude is computed fresh from terrain by GenerateMission.
            foreach (var wp in generatedWps)
            {
                if (wp.CorridorVertexIndex < 0 || wp.IsBranchVertex) continue;
                bool isLoiterWp = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS;
                var matching = elevationPoints.FirstOrDefault(s =>
                    !s.IsBranchVertex && s.WaypointIndex == wp.CorridorVertexIndex &&
                    (isLoiterWp ? s.IsLoiterWaypoint : s.IsLineWaypoint));
                if (matching == null) continue;
                wp.AltRelM = matching.AltRelM;
                wp.AltAGL  = matching.AltRelM - (wp.TerrainAltM - homeTerrainAlt);
            }

            // Re-mark inserted vertices (centreline samples unchanged, just refresh flag).
            foreach (var s in elevationPoints)
                if (s.IsLineWaypoint && !s.IsBranchVertex && _insertedVertexIndices.Contains(s.WaypointIndex))
                    s.IsInserted = true;

            bool preserveView = _preserveProfileView;
            _preserveProfileView = false;

            DrawMap();
            UpdateElevationProfile(p, preserveView);
            UpdateStats(p);
            BUT_accept.Enabled = true;
        }

        // ─── Inserted waypoint remove ─────────────────────────────────────────────

        private async void ElevProfile_WaypointRemoveRequested(object sender, WaypointRemoveEventArgs e)
        {
            int removeIdx = e.WaypointIndex;
            if (mainLine == null || removeIdx < 0 || removeIdx >= mainLine.Count) return;
            if (mainLine.Count <= 2) return;

            // Preserve the current profile view so removal doesn't jump the pan/zoom.
            _preserveProfileView = true;

            mainLine.RemoveAt(removeIdx);

            // Shift _insertedVertexIndices.
            var updated = new List<int>();
            foreach (var i in _insertedVertexIndices)
            {
                if (i == removeIdx) continue;
                updated.Add(i > removeIdx ? i - 1 : i);
            }
            _insertedVertexIndices.Clear();
            foreach (var i in updated) _insertedVertexIndices.Add(i);

            // Shift branch attachment junction indices.
            foreach (var br in branchAttachments)
                if (br.MainLineVertexIndex > removeIdx) br.MainLineVertexIndex--;

            DrawMap();
            await ExecuteGenerateAsync();
        }

        // ─── Map→profile zoom sync ────────────────────────────────────────────────

        private void SyncProfileToMapExtent()
        {
            if (elevationPoints == null || elevationPoints.Count == 0) return;

            var area = map.ViewArea; // RectLatLng (Top = maxLat, Bottom = minLat)

            // Find the DistM range of all samples (including intermediate terrain
            // samples) that fall within the current map viewport.
            double dMin = double.MaxValue;
            double dMax = double.MinValue;

            foreach (var s in elevationPoints)
            {
                if (s.Lat < area.Bottom || s.Lat > area.Top) continue;
                if (s.Lng < area.Left   || s.Lng > area.Right) continue;
                if (s.DistM < dMin) dMin = s.DistM;
                if (s.DistM > dMax) dMax = s.DistM;
            }

            if (dMin >= dMax) return;   // nothing visible

            // Snap the extent to the main-line corridor-vertex samples that bracket the
            // visible range, so the profile always starts/ends at a clean waypoint
            // rather than mid-leg. Branch vertices are excluded so the view doesn't
            // snap to a point mid-way through a branch detour.
            var vtxDists = elevationPoints
                .Where(s => s.IsLineWaypoint && !s.IsBranchVertex)
                .Select(s => s.DistM)
                .OrderBy(d => d)
                .ToList();

            if (vtxDists.Count > 0)
            {
                var below = vtxDists.Where(d => d <= dMin).ToList();
                var above = vtxDists.Where(d => d >= dMax).ToList();
                dMin = below.Count > 0 ? below.Max() : vtxDists.First();
                dMax = above.Count > 0 ? above.Min() : vtxDists.Last();
            }

            // Add a small buffer so the bounding vertices aren't right at the edge.
            const double bufferM = 100.0;
            double mult = CurrentState.multiplierdist;
            elev_profile.SetXRange((dMin - bufferM) * mult, (dMax + bufferM) * mult);
        }

        // ─── Statistics ───────────────────────────────────────────────────────────

        private void UpdateStats(CorridorParameters p)
        {
            if (generatedWps == null || generatedWps.Count == 0)
            {
                lbl_stats.Text = "(generate mission to see stats)";
                return;
            }

            var (distM, timeSec) = CorridorPlanner.CalculateStats(generatedWps, p);

            double dMult = CurrentState.multiplierdist;
            string dUnit = CurrentState.DistanceUnit;

            int lineCount = generatedWps.Where(w => w.IsLineWaypoint)
                                        .Select(w => w.LineIndex).Distinct().Count();

            lbl_stats.Text =
                $"Lines: {lineCount}\n" +
                $"Distance: {distM * dMult:F2} {dUnit}\n" +
                $"Est. time: {System.TimeSpan.FromSeconds(timeSec):mm\\:ss}";
        }

        // ─── Accept ───────────────────────────────────────────────────────────────

        private void BUT_accept_Click(object sender, EventArgs e)
        {
            if (generatedWps == null || generatedWps.Count == 0)
            {
                CustomMessageBox.Show("No mission generated yet. Click Generate first.", "No Mission");
                return;
            }

            double aGLMult = CurrentState.multiplieralt;

            // Corridor waypoints use absolute AMSL altitudes so moving the takeoff
            // location later has no effect on the planned flight path.
            // AltRelM is relative to the home terrain at generation time;
            // adding homeTerrainAlt converts it to AMSL.
            var fp = MainV2.instance.FlightPlanner;
            int frameColIndex = fp.Commands.Columns["Frame"]?.Index ?? -1;

            // If requested and there's a return leg, mark its start with a single
            // DO_RETURN_PATH_START so the autopilot flies the return leg backwards
            // along the main line.
            bool insertReturnPath = CHK_returnpath.Checked && generatedWps.Max(w => w.LineIndex) >= 1;

            int exportedCount = 0;
            for (int i = 0; i < generatedWps.Count; i++)
            {
                var wp = generatedWps[i];

                if (insertReturnPath && i > 0 &&
                    generatedWps[i - 1].LineIndex == 0 && wp.LineIndex == 1)
                {
                    plugin.Host.AddWPtoList(
                        MAVLink.MAV_CMD.DO_RETURN_PATH_START,
                        0, 0, 0, 0, 0, 0, 0);
                    exportedCount++;
                    insertReturnPath = false;
                }

                int rowIdx = plugin.Host.AddWPtoList(
                    wp.Command,
                    wp.P1, wp.P2, wp.P3, wp.P4,
                    wp.Lng, wp.Lat,
                    (wp.AltRelM + homeTerrainAlt) * aGLMult);

                if (frameColIndex >= 0 && rowIdx >= 0 && rowIdx < fp.Commands.Rows.Count)
                    fp.Commands.Rows[rowIdx].Cells[frameColIndex].Value = (int)MAVLink.MAV_FRAME.GLOBAL;

                exportedCount++;
            }

            log.Info($"CorridorPlanForm: appended {exportedCount} waypoints to mission (absolute AMSL frame).");
            this.Close();
        }
    }
}
