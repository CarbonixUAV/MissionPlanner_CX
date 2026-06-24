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
using MissionPlanner.Utilities.Mission;
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

        // Loaded corridor features (every polyline from each loaded file) and the file
        // names shown in the UI. Branches are detected automatically by exact shared
        // vertices (CorridorTourBuilder) — no manual main/branch split.
        private readonly List<string> featureFiles = new List<string>();
        private readonly List<List<List<PointLatLngAlt>>> featuresByFile = new List<List<List<PointLatLngAlt>>>();

        private List<List<PointLatLngAlt>> AllFeatures() => featuresByFile.SelectMany(f => f).ToList();

        // Built model: polyline edges + Euler tour (from CorridorTourBuilder).
        private List<Carbonix.Planning.Polyline> polylines;
        private List<TourStep> tour;

        // Last generated mission waypoints
        private List<CorridorWaypoint> generatedWps;

        // Elevation profile data — read-only tour view (interactive editing returns in a
        // follow-up). Contains waypoint anchors and terrain fill points.
        private List<ElevationPoint> elevationPoints;

        // Home terrain altitude (m) — computed once per generation
        private double homeTerrainAlt;

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
            // Pan with the control's built-in (buffer-offset) drag on the left button —
            // GMap.NET defaults DragButton to Right. The old manual MouseDown/Move pan
            // reset map.Position every mouse-move, forcing a full tile reload + overlay
            // re-render each time (a multi-second hitch with a 600-WP corridor overlay).
            map.DragButton = System.Windows.Forms.MouseButtons.Left;
            map.MouseUp += map_MouseUp;   // re-sync the profile X-range after a pan
            map.OnMapZoomChanged += () => SyncProfileToMapExtent();

            elev_profile.AltitudeChanged += ElevProfile_AltitudeChanged;
        }

        /// <summary>
        /// Apply a profile altitude drag to every waypoint/sample sharing the dragged
        /// vertex. Keyed on VertexId, so a polyline flown out-and-back (and any spur) is
        /// edited consistently across all its occurrences.
        /// </summary>
        private void ElevProfile_AltitudeChanged(object sender, AltChangeEventArgs e)
        {
            if (generatedWps != null)
            {
                foreach (var wp in generatedWps)
                {
                    if (wp.Vertex != e.Vertex) continue;
                    bool isLoiterWp = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS;
                    if (e.IsLoiter != isLoiterWp) continue;
                    wp.AltRelM = e.NewAltRelM;
                    wp.AltAGL  = e.NewAltRelM - (wp.TerrainAltM - homeTerrainAlt);
                }
            }

            // Keep the other profile samples of the same vertex in sync (the control has
            // already updated the dragged sample + its loiter arc sub-samples).
            if (elevationPoints != null)
            {
                foreach (var s in elevationPoints)
                {
                    if (s.Vertex != e.Vertex) continue;
                    if (e.IsLoiter)
                    {
                        if (s.IsLoiterWaypoint || s.IsLoiterArcSample) s.AltRelM = e.NewAltRelM;
                    }
                    else if (s.IsLineWaypoint)
                    {
                        s.AltRelM = e.NewAltRelM;
                    }
                }
            }

            // No DrawMap here: an altitude change doesn't move the ground track, and
            // rebuilding the map overlay every mouse-move made dragging crawl. The profile
            // control repaints itself; the map refreshes on the next generate/accept.
        }

        // ─── Form load ────────────────────────────────────────────────────────────

        private void CorridorPlanForm_Load(object sender, EventArgs e)
        {
            map.Position = plugin.Host.FPGMapControl.Position;
            map.Zoom = plugin.Host.FPGMapControl.Zoom;

            // Single auto-detected feature set: the manual branch list and the
            // return-path option are no longer used (branches are detected from the
            // loaded features; the return leg is enumerated and DO_RETURN_PATH_START is
            // added by hand afterward).
            LST_branches.Visible = false;
            BUT_branches_add.Visible = false;
            BUT_branches_remove.Visible = false;
            CHK_returnpath.Visible = false;

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

        private static List<List<PointLatLngAlt>> LoadCorridorFeatures(string path)
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
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Open Corridor File(s)";
                dlg.Filter = "All Supported|*.kml;*.kmz;*.shp|KML/KMZ|*.kml;*.kmz|Shapefile|*.shp";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                foreach (var path in dlg.FileNames)
                {
                    List<List<PointLatLngAlt>> feats;
                    try
                    {
                        feats = LoadCorridorFeatures(path);
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show("Error loading file:\n" + ex.Message, "Load Error");
                        continue;
                    }

                    feats = feats?.Where(f => f != null && f.Count >= 2).ToList();
                    if (feats == null || feats.Count == 0)
                    {
                        CustomMessageBox.Show(
                            "No line/polyline geometry found in " + Path.GetFileName(path) + ".",
                            "No Corridor Found");
                        continue;
                    }

                    featureFiles.Add(path);
                    featuresByFile.Add(feats);
                    LST_mainline.Items.Add(
                        $"{Path.GetFileName(path)}  ({feats.Count} feature{(feats.Count == 1 ? "" : "s")})");
                }
            }

            RebuildModel();
            DrawMap();
            ZoomToFitFeatures();
        }

        private void BUT_mainline_remove_Click(object sender, EventArgs e)
        {
            var selected = LST_mainline.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
            if (selected.Count == 0) return;

            foreach (var i in selected)
            {
                featureFiles.RemoveAt(i);
                featuresByFile.RemoveAt(i);
                LST_mainline.Items.RemoveAt(i);
            }

            RebuildModel();
            DrawMap();
        }

        // Branch list is hidden in the single-feature-set model; these remain as no-ops
        // to satisfy the designer's event wiring.
        private void BUT_branches_add_Click(object sender, EventArgs e) { }
        private void BUT_branches_remove_Click(object sender, EventArgs e) { }

        private void ZoomToFitFeatures()
        {
            var all = AllFeatures().SelectMany(f => f).ToList();
            if (all.Count == 0) return;
            double minLat = all.Min(p => p.Lat), maxLat = all.Max(p => p.Lat);
            double minLng = all.Min(p => p.Lng), maxLng = all.Max(p => p.Lng);
            map.SetZoomToFitRect(new RectLatLng(maxLat, minLng, maxLng - minLng, maxLat - minLat));
        }

        /// <summary>
        /// Clears any previously generated mission/profile data after the loaded feature
        /// set changes. The polyline+tour model is built fresh at generation time.
        /// </summary>
        private void RebuildModel()
        {
            generatedWps = null;
            elevationPoints = null;
            polylines = null;
            tour = null;
            BUT_accept.Enabled = false;
        }

        // ── KML/KMZ loader ───────────────────────────────────────────────────────

        private static List<List<PointLatLngAlt>> LoadKml(string path)
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

            var features = new List<List<PointLatLngAlt>>();
            foreach (var ls in kml.Root.Flatten().OfType<LineString>())
            {
                if (ls.Coordinates == null) continue;
                var pts = ls.Coordinates
                    .Select(c => new PointLatLngAlt(c.Latitude, c.Longitude, c.Altitude ?? 0))
                    .ToList();
                if (pts.Count >= 2) features.Add(pts);
            }
            return features;
        }

        // ── SHP loader ───────────────────────────────────────────────────────────

        private static List<List<PointLatLngAlt>> LoadShp(string path)
        {
            var fs = DotSpatial.Data.FeatureSet.Open(path);
            if (fs == null || fs.Features.Count == 0)
                return null;

            DotSpatial.Projections.ProjectionInfo srcProj = null;
            string prjPath = Path.ChangeExtension(path, ".prj");
            if (File.Exists(prjPath))
                srcProj = DotSpatial.Projections.ProjectionInfo.Open(prjPath);

            var wgs84 = DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

            var features = new List<List<PointLatLngAlt>>();
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

                var pts = new List<PointLatLngAlt>(xs.Length);
                for (int i = 0; i < xs.Length; i++)
                    pts.Add(new PointLatLngAlt(ys[i], xs[i], double.IsNaN(zs[i]) ? 0 : zs[i]));
                features.Add(pts);
            }

            return features;
        }

        // ─── Map drawing ──────────────────────────────────────────────────────────

        private void DrawMap()
        {
            layer_corridor.Routes.Clear();
            layer_corridor.Markers.Clear();

            var features = AllFeatures();
            if (features.Count == 0)
            {
                _missionOverlay.overlay.Clear();
                map.Refresh();
                return;
            }

            // Draw every loaded feature (the corridor + spurs) as a dashed reference line.
            foreach (var feat in features)
                layer_corridor.Routes.Add(new GMapRoute(
                    feat.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList(), "feature")
                {
                    Stroke = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }
                });

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
                : features[0].First();
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
        // Panning is the control's built-in left-button drag (configured in the ctor);
        // we only re-sync the elevation profile's X-range to the new extent on mouse-up.

        private void map_MouseUp(object sender, MouseEventArgs e)
        {
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
                $"<{lowThresh}\u00b0: straight | {lowThresh}\u00b0\u2013{highThresh}\u00b0: corner cut | \u2265{highThresh}\u00b0: sharp turn\n" +
                $"Turn radius: {turnRadius:F0} m  |  Corner radius: {cornerRadius:F0} m";
        }

        // ─── Generate ─────────────────────────────────────────────────────────────

        private async void BUT_generate_Click(object sender, EventArgs e)
        {
            if (AllFeatures().Count == 0)
            {
                CustomMessageBox.Show("Please load a corridor file first.", "No Corridor");
                return;
            }
            await ExecuteGenerateAsync();
        }

        /// <summary>
        /// Builds the polyline+tour model from the loaded features and the FlightPlanner
        /// home point, then generates the mission and a read-only elevation profile on a
        /// background thread and updates the map and profile.
        /// </summary>
        private async System.Threading.Tasks.Task ExecuteGenerateAsync()
        {
            var features = AllFeatures();
            if (features.Count == 0) return;

            var p = BuildParameters();
            if (p == null) return;

            PointLatLngAlt homePoint = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation
                : features[0].First();
            bool reverse = CHK_reverse.Checked;

            BUT_generate.Enabled = false;
            BUT_accept.Enabled = false;
            lbl_stats.Text = "Fetching terrain data and generating mission…";

            List<Carbonix.Planning.Polyline> builtPolylines;
            List<TourStep> builtTour;
            List<CorridorWaypoint> wps;
            List<ElevationPoint> profileSamples;
            double terrAlt;
            try
            {
                var capturedFeatures = features;
                var capturedP        = p;
                var capturedHome     = homePoint;
                (builtPolylines, builtTour, wps, profileSamples, terrAlt) =
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        double homeT = CorridorPlanner.GetTerrainAlt(capturedHome.Lat, capturedHome.Lng);
                        var (pls, tr) = CorridorTourBuilder.Build(
                            capturedFeatures, capturedHome, capturedP.PassOffsetM, capturedP.NumberOfPasses, reverse);
                        var generated = CorridorPlanner.GenerateMissionFromTour(pls, tr, capturedP, capturedHome);
                        var profile = BuildTourProfile(generated, capturedHome, homeT);
                        return (pls, tr, generated, profile, homeT);
                    });
            }
            catch (Exception ex)
            {
                log.Error("Corridor tour generation failed", ex);
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
                CustomMessageBox.Show(
                    "No waypoints were generated. Check the loaded features and parameters.", "No Waypoints");
                return;
            }

            polylines       = builtPolylines;
            tour            = builtTour;
            generatedWps    = wps;
            elevationPoints = profileSamples;
            homeTerrainAlt  = terrAlt;

            DrawMap();
            UpdateElevationProfile(p);
            UpdateStats(p);
            BUT_accept.Enabled = true;
        }

        /// <summary>
        /// Read-only elevation profile of the flown mission. Straight legs come from the
        /// same <see cref="MissionSegmentizer"/> the map uses (exact tangent entry/exit),
        /// so the profile matches the map. Each loiter is unwrapped over the FULL circle
        /// (entry → full turn → exit) so an errant extra turn can't hide unvetted terrain.
        /// Interactive editing and first-pass-per-leg de-dup are a follow-up.
        /// </summary>
        private static List<ElevationPoint> BuildTourProfile(
            List<CorridorWaypoint> wps, PointLatLngAlt home, double homeTerrainAlt)
        {
            const double SampleSpacingM = 25.0;
            var samples = new List<ElevationPoint>();
            if (wps == null || wps.Count == 0) return samples;

            // Run the waypoints through the same segmentizer the map uses, so the profile's
            // ground track is identical to what's drawn.
            var locationWps = wps.Select(wp => new Locationwp
            {
                id = (ushort)wp.Command, p1 = wp.P1, p2 = wp.P2, p3 = wp.P3, p4 = wp.P4,
                lat = wp.Lat, lng = wp.Lng, alt = (float)wp.AltRelM,
            }).ToList();
            var graph = MissionGraph.Create(new PointLatLngAlt(home.Lat, home.Lng, 0), locationWps);
            var segments = MissionSegmentizer.BuildSegments(graph, VehicleClass.Plane, 0);

            // Index primary (flown) segments by their start node (= index into wps).
            var straightByFrom = new Dictionary<int, MissionSegmentizer.Segment>();
            var loiterByNode   = new Dictionary<int, MissionSegmentizer.Segment>();
            foreach (var s in segments)
            {
                if (s.StartNode == null || (s.Flags & SegmentFlags.Alternate) != 0) continue;
                if (s.Kind == SegmentKind.LoiterArc)
                {
                    if (!loiterByNode.ContainsKey(s.StartNode.MissionIndex))
                        loiterByNode[s.StartNode.MissionIndex] = s;
                }
                else if (s.EndNode != null && !straightByFrom.ContainsKey(s.StartNode.MissionIndex))
                {
                    straightByFrom[s.StartNode.MissionIndex] = s;
                }
            }

            double cum = 0;

            double AngleDiffDeg(double a, double b, bool clockwise)
            {
                double sgn = clockwise ? 1.0 : -1.0;
                while (sgn * b < sgn * a) b += sgn * 360.0;
                return Math.Abs(b - a);
            }

            void SampleArc(PointLatLngAlt center, double radius, double startBearing, double sweepDeg, CorridorWaypoint owner)
            {
                double arcLen = 2.0 * Math.PI * radius * Math.Abs(sweepDeg) / 360.0;
                int n = Math.Max(2, (int)(arcLen / SampleSpacingM));
                for (int k = 1; k <= n; k++)
                {
                    var pt = center.newpos(startBearing + sweepDeg * k / n, radius);
                    samples.Add(new ElevationPoint
                    {
                        DistM             = cum + arcLen * k / n,
                        AltRelM           = owner.AltRelM,
                        TerrainAlt        = CorridorPlanner.GetTerrainAlt(pt.Lat, pt.Lng),
                        HomeTerrainAlt    = homeTerrainAlt,
                        IsLoiterArcSample = true,
                        WaypointIndex     = owner.CorridorVertexIndex,
                        IsBranchVertex    = owner.IsBranchVertex,
                        BranchId          = owner.BranchId,
                        Lat               = pt.Lat,
                        Lng               = pt.Lng,
                    });
                }
                cum += arcLen;
            }

            void SampleStraight(PointLatLngAlt a, PointLatLngAlt b, CorridorWaypoint owner)
            {
                double dist = a.GetDistance(b);
                int n = Math.Max(1, (int)(dist / SampleSpacingM));
                for (int k = 1; k <= n; k++)
                {
                    double t = (double)k / n;
                    var pt = new PointLatLngAlt(a.Lat + t * (b.Lat - a.Lat), a.Lng + t * (b.Lng - a.Lng), 0);
                    samples.Add(new ElevationPoint
                    {
                        DistM              = cum + dist * t,
                        AltRelM            = owner.AltRelM,
                        TerrainAlt         = CorridorPlanner.GetTerrainAlt(pt.Lat, pt.Lng),
                        HomeTerrainAlt     = homeTerrainAlt,
                        IsLegTerrainSample = true,
                        WaypointIndex      = owner.CorridorVertexIndex,
                        IsBranchVertex     = owner.IsBranchVertex,
                        BranchId           = owner.BranchId,
                        Lat                = pt.Lat,
                        Lng                = pt.Lng,
                    });
                }
                cum += dist;
            }

            // First pass per leg: every polyline edge is flown twice (out + back); show only
            // its first traversal. Identity is the edge's PolylineId; LineIndex is the tour
            // step, so the lowest LineIndex for a polyline is its first pass.
            var firstStep = new Dictionary<int, int>();
            foreach (var w in wps)
            {
                int pl = w.Vertex.PolylineId;
                if (!firstStep.TryGetValue(pl, out var st) || w.LineIndex < st) firstStep[pl] = w.LineIndex;
            }
            bool Keep(CorridorWaypoint w) => w.LineIndex == firstStep[w.Vertex.PolylineId];

            foreach (var node in graph.Nodes)
            {
                int idx = node.MissionIndex;
                if (idx < 0 || idx >= wps.Count) continue;
                var wp = wps[idx];
                if (!Keep(wp)) continue;   // skip repeat (back-pass) traversals

                if (loiterByNode.TryGetValue(idx, out var arc) && arc.Path != null && arc.Path.Count >= 2)
                {
                    var center = new PointLatLngAlt(wp.Lat, wp.Lng, 0);
                    double radius = wp.LoiterRadiusM > 0 ? wp.LoiterRadiusM : Math.Abs(wp.P3);
                    bool cw = wp.P3 >= 0;
                    double entry = center.GetBearing(arc.Path[0]);
                    double exit  = center.GetBearing(arc.Path[arc.Path.Count - 1]);
                    double sign = cw ? 1.0 : -1.0;
                    double primary = AngleDiffDeg(entry, exit, cw);   // flown arc, [0,360)
                    double altSweep = 360.0 - primary;
                    double circumference = 2.0 * Math.PI * radius;
                    double primaryLen = circumference * primary / 360.0;
                    double totalLen = circumference;   // full circle (primary + alternate)

                    // Loiter bar anchor spanning the full circle.
                    samples.Add(new ElevationPoint
                    {
                        DistM             = cum,
                        AltRelM           = wp.AltRelM,
                        TerrainAlt        = wp.TerrainAltM,
                        HomeTerrainAlt    = homeTerrainAlt,
                        IsLoiterWaypoint  = true,
                        LoiterRadiusM     = radius,
                        LoiterArcLengthM  = totalLen,
                        LoiterPrimaryLenM = primaryLen,
                        WaypointIndex    = wp.CorridorVertexIndex,
                        IsBranchVertex   = wp.IsBranchVertex,
                        BranchId         = wp.BranchId,
                        Lat              = wp.Lat,
                        Lng              = wp.Lng,
                    });

                    // Full circle for worst-case terrain: primary (flown) + alternate
                    // (remainder). Ends at the entry tangent; the discontinuity to the
                    // exit leg is accepted.
                    SampleArc(center, radius, entry, sign * primary,  wp);
                    SampleArc(center, radius, exit,  sign * altSweep, wp);
                }
                else
                {
                    samples.Add(new ElevationPoint
                    {
                        DistM          = cum,
                        AltRelM        = wp.AltRelM,
                        TerrainAlt     = wp.TerrainAltM,
                        HomeTerrainAlt = homeTerrainAlt,
                        IsLineWaypoint = true,
                        WaypointIndex  = wp.CorridorVertexIndex,
                        IsBranchVertex = wp.IsBranchVertex,
                        BranchId       = wp.BranchId,
                        Lat            = wp.Lat,
                        Lng            = wp.Lng,
                    });
                }

                // Outgoing straight leg — only when the destination is also a first-pass
                // node, so we don't draw a leg into a dropped back-pass (a clean break).
                if (straightByFrom.TryGetValue(idx, out var seg) && seg.Path != null && seg.Path.Count >= 2
                    && seg.EndNode != null
                    && seg.EndNode.MissionIndex >= 0 && seg.EndNode.MissionIndex < wps.Count
                    && Keep(wps[seg.EndNode.MissionIndex]))
                    SampleStraight(seg.Path[0], seg.Path[seg.Path.Count - 1], wp);
            }

            return samples;
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

            // The tour enumerates the return leg as explicit waypoints; a
            // DO_RETURN_PATH_START marker, if wanted, is added by hand afterward.
            //
            // AddWPtoList -> AddCommand calls writeKML() per waypoint, which rebuilds the
            // whole mission each time — O(n²), ~a minute for a 600-WP corridor. quickadd
            // suppresses that (MP's own bulk-load guard); rebuild once at the end.
            int exportedCount = 0;
            fp.quickadd = true;
            try
            {
                for (int i = 0; i < generatedWps.Count; i++)
                {
                    var wp = generatedWps[i];

                    int rowIdx = plugin.Host.AddWPtoList(
                        wp.Command,
                        wp.P1, wp.P2, wp.P3, wp.P4,
                        wp.Lng, wp.Lat,
                        (wp.AltRelM + homeTerrainAlt) * aGLMult);

                    if (frameColIndex >= 0 && rowIdx >= 0 && rowIdx < fp.Commands.Rows.Count)
                        fp.Commands.Rows[rowIdx].Cells[frameColIndex].Value = (int)MAVLink.MAV_FRAME.GLOBAL;

                    exportedCount++;
                }
            }
            finally
            {
                fp.quickadd = false;
            }
            fp.writeKML();

            log.Info($"CorridorPlanForm: appended {exportedCount} waypoints to mission (absolute AMSL frame).");
            this.Close();
        }
    }
}
