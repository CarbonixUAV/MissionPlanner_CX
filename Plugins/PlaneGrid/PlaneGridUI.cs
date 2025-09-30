using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using GeoAPI.CoordinateSystems;
using GeoAPI.CoordinateSystems.Transformations;
using MissionPlanner.Controls;
using MissionPlanner.Grid;
using MissionPlanner;
using MissionPlanner.Maps;
using System.Threading.Tasks;

namespace PlaneGrid
{
    public partial class PlaneGridUI : Form
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Variables
        const double rad2deg = (180 / Math.PI);
        const double deg2rad = (1.0 / rad2deg);

        private readonly PlaneGridPlugin plugin;
        static public object thisLock = new object();

        readonly GMapOverlay routesOverlay;
        readonly GMapOverlay markersOverlay;
        readonly GMapOverlay boundaryOverlay;
        readonly GMapOverlay polygonsOverlay;   // persistent poly_points markers — used for zoom-to-fit
        readonly GMapOverlay kmlpolygonsoverlay;
        List<PointLatLngAlt> poly_points = new List<PointLatLngAlt>();
        List<PointLatLngAlt> grid_points;
        bool loadedfromfile = false;
        bool loading = false;

        readonly Dictionary<string, camerainfo> cameras = new Dictionary<string, camerainfo>();

        public string DistUnits = "";
        public string inchpixel = "";
        public string feet_fovH = "";
        public string feet_fovV = "";

        internal PointLatLng MouseDownStart = new PointLatLng();
        internal PointLatLngAlt CurrentGMapMarkerStartPos;
        GMapMarker CurrentGMapMarker = null;
        int CurrentGMapMarkerIndex = 0;
        bool isMouseDown = false;
        bool isMouseDraging = false;
        public PlaneGridUI(PlaneGridPlugin plugin)
        {
            this.plugin = plugin;

            InitializeComponent();

            loading = true;

            map.MapProvider = plugin.Host.FDMapType;
            map.MaxZoom = plugin.Host.FDGMapControl.MaxZoom;
            TRK_zoom.Maximum = map.MaxZoom;

            kmlpolygonsoverlay = new GMapOverlay("kmlpolygons");
            map.Overlays.Add(kmlpolygonsoverlay);

            polygonsOverlay = new GMapOverlay("polygons");
            map.Overlays.Add(polygonsOverlay);

            boundaryOverlay = new GMapOverlay("boundary");
            map.Overlays.Add(boundaryOverlay);

            routesOverlay = new GMapOverlay("routes");
            map.Overlays.Add(routesOverlay);

            markersOverlay = new GMapOverlay("markers");
            map.Overlays.Add(markersOverlay);

            plugin.Host.FPDrawnPolygon.Points.ForEach(x => { poly_points.Add(x); });

            // Populate polygonsOverlay with the boundary points as markers so
            // ZoomAndCenterMarkers("polygons") has geometry to fit on first load.
            foreach (var pt in poly_points)
                polygonsOverlay.Markers.Add(new GMarkerGoogle(pt, GMarkerGoogleType.red));

            if (plugin.Host.config["distunits"] != null)
                DistUnits = plugin.Host.config["distunits"].ToString();

            CMB_startfrom.DataSource = Enum.GetNames(typeof(Grid.StartPosition));
            CMB_startfrom.SelectedIndex = 0;

            // Infer a good angle from the polygon
            NUM_angle.Value = (decimal)((getAngleOfLongestSide(poly_points) + 360) % 360);

            if (plugin.Host.cs.firmware == Firmwares.ArduPlane)
                NUM_UpDownFlySpeed.Value = (decimal)(12 * CurrentState.multiplierspeed);

            map.MapScaleInfoEnabled = true;
            map.ScalePen = new Pen(Color.Orange);
            foreach (var temp in FlightData.kmlpolygons.Polygons)
            {
                kmlpolygonsoverlay.Polygons.Add(new GMapPolygon(temp.Points, "") { Fill = Brushes.Transparent });
            }
            foreach (var temp in FlightData.kmlpolygons.Routes)
            {
                kmlpolygonsoverlay.Routes.Add(new GMapRoute(temp.Points, ""));
            }

            xmlcamera(false, Settings.GetRunningDirectory() + "camerasBuiltin.xml");

            xmlcamera(false, Settings.GetUserDataDirectory() + "cameras.xml");

            loading = false;
        }
        private void PlaneGridUI_Load(object sender, EventArgs e)
        {
            loading = true;
            if (!loadedfromfile)
                loadsettings();

            TRK_zoom.Value = (float)map.Zoom;

            label1.Text += " (" + CurrentState.DistanceUnit + ")";
            label24.Text += " (" + CurrentState.SpeedUnit + ")";

            loading = false;

            map.ZoomAndCenterMarkers("polygons");

            any_ValueChanged(this, null);
        }

        private void PlaneGridUI_Resize(object sender, EventArgs e)
        {
            map.ZoomAndCenterMarkers("polygons");
        }

        private void PlaneGridUI_FormClosing(object sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            savesettings();
        }

        // Load/Save
        public void loadFileMenuItem_Click(object sender, EventArgs e)
        {
            System.Xml.Serialization.XmlSerializer reader = new System.Xml.Serialization.XmlSerializer(typeof(PlaneGridData));

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "*.pgrd|*.pgrd";
                ofd.ShowDialog();

                if (File.Exists(ofd.FileName))
                {
                    using (StreamReader sr = new StreamReader(ofd.FileName))
                    {
                        var test = (PlaneGridData)reader.Deserialize(sr);

                        loading = true;
                        loadgriddata(test);
                        loading = false;
                    }

                    any_ValueChanged(this, null);
                    map.ZoomAndCenterMarkers("polygons");
                }
            }
        }

        public void saveFileMenuItem_Click(object sender, EventArgs e)
        {
            System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(PlaneGridData));

            var griddata = savegriddata();

            // Save config too
            savesettings();

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "*.pgrd|*.pgrd";
                var result = sfd.ShowDialog();

                if (sfd.FileName != "" && result == DialogResult.OK)
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName))
                    {
                        writer.Serialize(sw, griddata);
                    }
                }
            }
        }

        void loadgriddata(PlaneGridData griddata)
        {
            poly_points = griddata.poly;

            polygonsOverlay.Markers.Clear();
            foreach (var pt in poly_points)
                polygonsOverlay.Markers.Add(new GMarkerGoogle(pt, GMarkerGoogleType.red));

            CMB_camera.Text = griddata.camera;
            NUM_altitude.Value = griddata.alt;
            NUM_angle.Value = griddata.angle;
            NUM_UpDownFlySpeed.Value = griddata.speed;

            NUM_SidelapDist.Value = griddata.dist;
            NUM_overshoot.Value = griddata.overshoot1;
            NUM_overshoot2.Value = griddata.overshoot2;
            NUM_turnradius.Value = griddata.leadin;
            CMB_startfrom.Text = griddata.startfrom;
            num_overlap.Value = griddata.overlap;
            num_sidelap.Value = griddata.sidelap;
            NUM_OverlapDist.Value = griddata.spacing;
            chk_crossgrid.Checked = griddata.crossgrid;

            chk_triginturns.Checked = !griddata.breaktrigdist;

            // Plane Settings
            NUM_Lane_Dist.Value = griddata.minlaneseparation;

            loadedfromfile = true;
        }

        PlaneGridData savegriddata()
        {
            PlaneGridData griddata = new PlaneGridData();

            griddata.poly = poly_points;

            griddata.camera = CMB_camera.Text;
            griddata.alt = NUM_altitude.Value;
            griddata.angle = NUM_angle.Value;
            griddata.speed = NUM_UpDownFlySpeed.Value;

            griddata.dist = NUM_SidelapDist.Value;
            griddata.overshoot1 = NUM_overshoot.Value;
            griddata.overshoot2 = NUM_overshoot2.Value;
            griddata.leadin = NUM_turnradius.Value;
            griddata.startfrom = CMB_startfrom.Text;
            griddata.overlap = num_overlap.Value;
            griddata.sidelap = num_sidelap.Value;
            griddata.spacing = NUM_OverlapDist.Value;
            griddata.crossgrid = chk_crossgrid.Checked;

            griddata.breaktrigdist = !chk_triginturns.Checked;

            // Plane Settings
            griddata.minlaneseparation = NUM_Lane_Dist.Value;

            return griddata;
        }

        void loadsettings()
        {
            loadsetting("grid_alt", NUM_altitude);
            loadsetting("grid_angle", NUM_angle);
            loadsetting("grid_speed", NUM_UpDownFlySpeed);

            loadsetting("grid_dist", NUM_SidelapDist);
            loadsetting("grid_overshoot1", NUM_overshoot);
            loadsetting("grid_overshoot2", NUM_overshoot2);
            loadsetting("grid_turnradius", NUM_turnradius);
            loadsetting("grid_turnradius2", NUM_turnradius2);
            loadsetting("grid_startfrom", CMB_startfrom);
            loadsetting("grid_overlap", num_overlap);
            loadsetting("grid_sidelap", num_sidelap);
            loadsetting("grid_spacing", NUM_OverlapDist);
            loadsetting("grid_crossgrid", chk_crossgrid);
            loadsetting("grid_triginturns", chk_triginturns);
            loadsetting("grid_markers", CHK_markers);
            loadsetting("grid_boundary", CHK_boundary);
            loadsetting("grid_showgrid", CHK_grid);

            // Plane Settings
            loadsetting("grid_min_lane_separation", NUM_Lane_Dist);

            // camera last so it triggers a recalc
            loadsetting("grid_camera", CMB_camera);
        }

        void loadsetting(string key, Control item)
        {
            try
            {
                if (plugin.Host.config.ContainsKey(key))
                {
                    if (item is NumericUpDown)
                        ((NumericUpDown)item).Value = decimal.Parse(plugin.Host.config[key].ToString());
                    else if (item is ComboBox)
                        ((ComboBox)item).Text = plugin.Host.config[key].ToString();
                    else if (item is CheckBox)
                        ((CheckBox)item).Checked = bool.Parse(plugin.Host.config[key].ToString());
                    else if (item is RadioButton)
                        ((RadioButton)item).Checked = bool.Parse(plugin.Host.config[key].ToString());
                }
            }
            catch { }
        }

        void savesettings()
        {
            plugin.Host.config["grid_camera"] = CMB_camera.Text;
            plugin.Host.config["grid_alt"] = NUM_altitude.Value.ToString();
            plugin.Host.config["grid_angle"] = NUM_angle.Value.ToString();
            plugin.Host.config["grid_speed"] = NUM_UpDownFlySpeed.Value.ToString();

            plugin.Host.config["grid_dist"] = NUM_SidelapDist.Value.ToString();
            plugin.Host.config["grid_overshoot1"] = NUM_overshoot.Value.ToString();
            plugin.Host.config["grid_overshoot2"] = NUM_overshoot2.Value.ToString();
            plugin.Host.config["grid_turnradius"] = NUM_turnradius.Value.ToString();
            plugin.Host.config["grid_turnradius2"] = NUM_turnradius2.Value.ToString();
            plugin.Host.config["grid_overlap"] = num_overlap.Value.ToString();
            plugin.Host.config["grid_sidelap"] = num_sidelap.Value.ToString();
            plugin.Host.config["grid_spacing"] = NUM_OverlapDist.Value.ToString();
            plugin.Host.config["grid_crossgrid"] = chk_crossgrid.Checked.ToString();
            plugin.Host.config["grid_triginturns"] = chk_triginturns.Checked.ToString();
            plugin.Host.config["grid_markers"] = CHK_markers.Checked.ToString();
            plugin.Host.config["grid_boundary"] = CHK_boundary.Checked.ToString();
            plugin.Host.config["grid_showgrid"] = CHK_grid.Checked.ToString();

            plugin.Host.config["grid_startfrom"] = CMB_startfrom.Text;

            // Plane Settings
            plugin.Host.config["grid_min_lane_separation"] = NUM_Lane_Dist.Value.ToString();

            // Plane grid always uses landscape camera orientation
            plugin.Host.config["grid_camdir"] = "True";
        }

        private void xmlcamera(bool write, string filename)
        {
            bool exists = File.Exists(filename);

            if (write || !exists)
            {
                try
                {
                    XmlTextWriter xmlwriter = new XmlTextWriter(filename, Encoding.ASCII);
                    xmlwriter.Formatting = Formatting.Indented;

                    xmlwriter.WriteStartDocument();

                    xmlwriter.WriteStartElement("Cameras");

                    foreach (string key in cameras.Keys)
                    {
                        try
                        {
                            if (key == "")
                                continue;
                            xmlwriter.WriteStartElement("Camera");
                            xmlwriter.WriteElementString("name", cameras[key].name);
                            xmlwriter.WriteElementString("flen", cameras[key].focallen.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("imgh", cameras[key].imageheight.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("imgw", cameras[key].imagewidth.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("senh", cameras[key].sensorheight.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("senw", cameras[key].sensorwidth.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteEndElement();
                        }
                        catch { }
                    }

                    xmlwriter.WriteEndElement();

                    xmlwriter.WriteEndDocument();
                    xmlwriter.Close();

                }
                catch (Exception ex) { CustomMessageBox.Show(ex.ToString()); }
            }
            else
            {
                try
                {
                    using (XmlTextReader xmlreader = new XmlTextReader(filename))
                    {
                        while (xmlreader.Read())
                        {
                            xmlreader.MoveToElement();
                            try
                            {
                                switch (xmlreader.Name)
                                {
                                    case "Camera":
                                        {
                                            camerainfo camera = new camerainfo();

                                            while (xmlreader.Read())
                                            {
                                                bool dobreak = false;
                                                xmlreader.MoveToElement();
                                                switch (xmlreader.Name)
                                                {
                                                    case "name":
                                                        camera.name = xmlreader.ReadString();
                                                        break;
                                                    case "imgw":
                                                        camera.imagewidth = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "imgh":
                                                        camera.imageheight = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "senw":
                                                        camera.sensorwidth = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "senh":
                                                        camera.sensorheight = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "flen":
                                                        camera.focallen = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "Camera":
                                                        cameras[camera.name] = camera;
                                                        dobreak = true;
                                                        break;
                                                }
                                                if (dobreak)
                                                    break;
                                            }
                                            xmlreader.ReadString();
                                        }
                                        break;
                                    case "Config":
                                        break;
                                    case "xml":
                                        break;
                                    default:
                                        if (xmlreader.Name == "") // line feeds
                                            break;
                                        //config[xmlreader.Name] = xmlreader.ReadString();
                                        break;
                                }
                            }
                            catch (Exception ee) { Console.WriteLine(ee.Message); } // silent fail on bad entry
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("Bad Camera File: " + ex.ToString()); } // bad config file

                // populate list
                foreach (var camera in cameras.Values)
                {
                    if (!CMB_camera.Items.Contains(camera.name))
                        CMB_camera.Items.Add(camera.name);
                }
            }
        }

        /// <summary>
        /// Caclulates the key points of the grid mission.
        /// 
        /// This calls Grid.CreateGridAsync which does the heavy lifting, but we are only interested in the
        /// lane start/stop/overshoot/leadin points, so we pass it a photo spacing of 0. We will handle overlap
        /// ourselves in a later step. Once we have the points we care about, we adjust the leadin point to be
        /// the center of a circle which will exit tangent to the lane start point.
        /// </summary>
        /// <returns></returns>
        private async Task recalculate_gridAsync()
        {
            var altitude = CurrentState.fromDistDisplayUnit((double)NUM_altitude.Value);
            var start_position = (Grid.StartPosition)Enum.Parse(typeof(Grid.StartPosition), CMB_startfrom.Text);
            grid_points = await Grid.CreateGridAsync(
                poly_points,
                altitude,
                (double)NUM_SidelapDist.Value,
                0, // Distance between images, we don't need this, we just want lane start/stop points
                (double)NUM_angle.Value,
                (double)NUM_overshoot.Value,  // leadin1
                (double)NUM_overshoot2.Value, // leadin2
                start_position,
                false, // "shutter", this does nothing as far as I can tell
                (float)NUM_Lane_Dist.Value,
                (float)NUM_overshoot.Value,
                (float)NUM_overshoot2.Value,
                MainV2.comPort.MAV.cs.PlannedHomeLocation,
                useextendedendpoint: true // Finds next closest lane from the leadout (not the end of lane)
            ).ConfigureAwait(true);

            if (chk_crossgrid.Checked)
            {
                // add crossover
                Grid.StartPointLatLngAlt = grid_points[grid_points.Count - 1];

                grid_points.AddRange(await Grid.CreateGridAsync(
                    poly_points,
                    altitude,
                    (double)NUM_SidelapDist.Value,
                    0, // Distance between images, we don't need this, we just want lane start/stop points
                    (double)NUM_angle.Value + 90,
                    (double)NUM_overshoot.Value,  // leadin1
                    (double)NUM_overshoot2.Value, // leadin2
                    start_position,
                    false, // "shutter", this does nothing as far as I can tell
                    (float)NUM_Lane_Dist.Value,
                    (float)NUM_overshoot.Value,
                    (float)NUM_overshoot2.Value,
                    MainV2.comPort.MAV.cs.PlannedHomeLocation,
                    useextendedendpoint: true // Finds next closest lane from the leadout (not the end of lane)
                ).ConfigureAwait(true));
            }

            if (grid_points.Count < 2)
            {
                return;
            }


            bool lane_toggle = false; // switch between turnradius and turnradius2
            for (int a = 2; a < grid_points.Count; a++)
            {
                if (grid_points[a].Tag != "S")
                {
                    continue;
                }
                double turn_radius = lane_toggle ? (double)NUM_turnradius2.Value : (double)NUM_turnradius.Value;
                double dist = grid_points[a - 1].GetDistance(grid_points[a]);
                double turn_bearing = grid_points[a - 1].GetBearing(grid_points[a]);
                double next_lane_bearing = grid_points[a - 2].GetBearing(grid_points[a - 1]);
                lane_toggle = !lane_toggle;

                // Always move one turn radius towards the previous point
                grid_points[a] = grid_points[a].newpos(turn_bearing + 180, turn_radius);

                // If distance is less than 2x turn radius, move the point further out along the previous lane direction.
                // If the lanes are too close together, you need more space to be able to capture the circle.
                // (see the included `turn_center_calculator.ggb` GeoGebra file for a diagram of the geometry)
                if (dist < (turn_radius * 2))
                {
                    double extra_dist = Math.Sqrt(4 * turn_radius * turn_radius - dist * dist);
                    grid_points[a] = grid_points[a].newpos(next_lane_bearing, extra_dist);
                }

                // Store whether the turn is clockwise or anticlockwise
                double angle = (turn_bearing - next_lane_bearing + 360) % 360;
                double radius = turn_radius * (angle < 180 ? 1 : -1);
                grid_points[a].Tag2 = radius.ToString("0");
            }

        }

        private void draw_lanes()
        {
            // Build route content
            var newRoutes = new GMapOverlay("routes");
            var newMarkers = new GMapOverlay("markers");
            var newBoundary = new GMapOverlay("boundary");

            if (grid_points != null && grid_points.Count > 0)
            {
                // Convert grid_points to Locationwp for WPOverlay2/MissionSegmentizer
                var missionItems = new List<Locationwp>();
                foreach (var pt in grid_points)
                {
                    Locationwp wp;
                    if (pt.Tag == "S" && double.TryParse(pt.Tag2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var radius))
                    {
                        // Loiter center: radius stored as signed value in Tag2 (positive=CW, negative=CCW).
                        // Only "S" points processed by recalculate_gridAsync (index >= 2) have Tag2 set.
                        wp = new Locationwp
                        {
                            id = (ushort)MAVLink.MAV_CMD.LOITER_TURNS,
                            lat = pt.Lat,
                            lng = pt.Lng,
                            alt = (float)pt.Alt,
                            p3 = (float)radius,
                            p4 = 1,  // exit at tangent
                        };
                    }
                    else
                    {
                        wp = new Locationwp
                        {
                            id = (ushort)MAVLink.MAV_CMD.WAYPOINT,
                            lat = pt.Lat,
                            lng = pt.Lng,
                            alt = (float)pt.Alt,
                        };
                    }
                    missionItems.Add(wp);
                }

                // Pass Zero home so WPOverlay2 doesn't add a home marker or home→start route segment.
                var wpOverlay2 = new WPOverlay2 { VehicleClass = VehicleClass.Plane, ShowPlusMarkers = false };
                wpOverlay2.CreateOverlay(PointLatLngAlt.Zero, missionItems, wpradius: 0, loiterradius: 0, altunitmultiplier: 1.0);
                newRoutes.Routes.AddRange(wpOverlay2.overlay.Routes);
                newRoutes.Polygons.AddRange(wpOverlay2.overlay.Polygons);
                // WP markers from WPOverlay2 go into the markers overlay
                newMarkers.Markers.AddRange(wpOverlay2.overlay.Markers);

                // Debug markers showing raw grid_points (including loiter centers)
                for (int a = 0; a < grid_points.Count; a++)
                {
                    newMarkers.Markers.Add(new GMarkerGoogle(grid_points[a], GMarkerGoogleType.green)
                    {
                        ToolTipText = $"{a}: {grid_points[a].Tag}",
                        ToolTipMode = MarkerTooltipMode.OnMouseOver
                    });
                }
            }

            // Boundary polygon
            if (poly_points.Count > 0)
            {
                var list2 = new List<PointLatLng>();
                poly_points.ForEach(x => list2.Add(x));
                newBoundary.Polygons.Add(new GMapPolygon(list2, "poly")
                {
                    Stroke = new Pen(Color.Red, 2),
                    Fill = Brushes.Transparent
                });
            }

            map.BeginInvokeIfRequired(() =>
            {
                map.HoldInvalidation = true;

                routesOverlay.Clear();
                routesOverlay.Routes.AddRange(newRoutes.Routes);
                routesOverlay.Polygons.AddRange(newRoutes.Polygons);
                routesOverlay.IsVisibile = CHK_grid.Checked;

                markersOverlay.Clear();
                markersOverlay.Markers.AddRange(newMarkers.Markers);
                markersOverlay.IsVisibile = CHK_markers.Checked;

                boundaryOverlay.Clear();
                boundaryOverlay.Polygons.AddRange(newBoundary.Polygons);
                boundaryOverlay.IsVisibile = CHK_boundary.Checked;

                map.Refresh();
            });
        }

        private async void any_ValueChanged(object sender, EventArgs e)
        {
            if (loading)
                return;

            if (CMB_camera.Text != "")
            {
                doCalc();
            }

            await recalculate_gridAsync();

            draw_lanes();

            calculate_stats();
        }

        private void calculate_stats()
        {
            if (grid_points == null || grid_points.Count < 2)
                return;

            // --- Route distance (km) ---
            // Sum straight-line distances between consecutive grid_points.
            // Loiter "S" points are loiter centers so we skip them in the tally
            // and count only the lane waypoints (same points the plane actually flies).
            double routeKm = 0;
            PointLatLngAlt prev = null;
            int strips = 0;
            foreach (var pt in grid_points)
            {
                if (pt.Tag == "S")
                    continue;
                if (prev != null)
                    routeKm += prev.GetDistance(pt) / 1000.0;
                if (pt.Tag == "E" || pt.Tag == "ME")
                    strips++;
                prev = pt;
            }

            // --- Area ---
            double areaSqM = 0;
            if (poly_points.Count >= 3)
                areaSqM = calcpolygonarea(new List<PointLatLngAlt>(poly_points));

            // --- Ground elevation range (from SRTM) ---
            double minElev = double.MaxValue;
            double maxElev = double.MinValue;
            foreach (var pt in poly_points)
            {
                var srt = srtm.getAltitude(pt.Lat, pt.Lng);
                if (srt.currenttype != srtm.tiletype.invalid)
                {
                    minElev = Math.Min(minElev, srt.alt);
                    maxElev = Math.Max(maxElev, srt.alt);
                }
            }

            double flyspeedMs = CurrentState.fromSpeedDisplayUnit((double)NUM_UpDownFlySpeed.Value);
            double flightSecs = flyspeedMs > 0 ? (routeKm * 1000.0) / (flyspeedMs * 0.8) : 0;

            if (DistUnits == "Feet")
            {
                double areaSqFt = areaSqM * 10.7639;
                if (areaSqFt < 21780)
                    lbl_area.Text = areaSqFt.ToString("#") + " ft²";
                else if (areaSqFt / 43560 < 640)
                    lbl_area.Text = (areaSqFt / 43560).ToString("0.##") + " acres";
                else
                    lbl_area.Text = (areaSqFt / 43560 / 640).ToString("0.##") + " mi²";

                double distFt = routeKm * 3280.84;
                lbl_distance.Text = distFt < 5280
                    ? distFt.ToString("#") + " ft"
                    : (distFt / 5280).ToString("0.##") + " miles";

                lbl_spacing.Text = (NUM_OverlapDist.Value * 3.2808399m).ToString("#.#") + " ft";
                lbl_distbetweenlines.Text = (NUM_SidelapDist.Value * 3.2808399m).ToString("0.##") + " ft";
                lbl_footprint.Text = feet_fovH + " x " + feet_fovV + " ft";
                lbl_grndres.Text = inchpixel;

                if (minElev != double.MaxValue)
                    lbl_gndelev.Text = (minElev * 3.2808399).ToString("0") + "–" + (maxElev * 3.2808399).ToString("0") + " ft";
            }
            else
            {
                if (areaSqM < 10000)
                    lbl_area.Text = areaSqM.ToString("#") + " m²";
                else if (areaSqM < 1e6)
                    lbl_area.Text = (areaSqM / 10000).ToString("0.##") + " ha";
                else
                    lbl_area.Text = (areaSqM / 1e6).ToString("0.##") + " km²";

                lbl_distance.Text = routeKm.ToString("0.##") + " km";
                lbl_spacing.Text = NUM_OverlapDist.Value.ToString("0.#") + " m";
                lbl_distbetweenlines.Text = NUM_SidelapDist.Value.ToString("0.##") + " m";
                lbl_footprint.Text = feet_fovH != "" ? NUM_fovH.Value.ToString("0.#") + " x " + NUM_fovV.Value.ToString("0.#") + " m" : "-";
                lbl_grndres.Text = inchpixel;

                if (minElev != double.MaxValue)
                    lbl_gndelev.Text = minElev.ToString("0") + "–" + maxElev.ToString("0") + " m";
            }

            lbl_strips.Text = strips.ToString();
            lbl_pictures.Text = flyspeedMs > 0 && NUM_OverlapDist.Value > 0
                ? ((int)(routeKm * 1000.0 / (double)NUM_OverlapDist.Value)).ToString()
                : "-";
            lbl_flighttime.Text = secondsToNice(flightSecs);
            lbl_photoevery.Text = flyspeedMs > 0 && NUM_OverlapDist.Value > 0
                ? secondsToNice((double)NUM_OverlapDist.Value / flyspeedMs)
                : "-";
        }

        string secondsToNice(double seconds)
        {
            if (seconds < 0)
                return "∞";
            int secs = (int)(seconds % 60);
            int mins = (int)(seconds / 60) % 60;
            int hours = (int)(seconds / 3600);
            if (hours > 0)
                return hours + ":" + mins.ToString("00") + ":" + secs.ToString("00") + " hrs";
            if (mins > 0)
                return mins + ":" + secs.ToString("00") + " min";
            return secs.ToString("0") + " sec";
        }

        double calcpolygonarea(List<PointLatLngAlt> polygon)
        {
            if (polygon.Count < 3)
                return 0;

            bool closeit = polygon[0] != polygon[polygon.Count - 1];
            if (closeit)
                polygon.Add(polygon[0]);

            var ctfac = new CoordinateTransformationFactory();
            var wgs84 = GeographicCoordinateSystem.WGS84;
            int utmzone = (int)((polygon[0].Lng - -186.0) / 6.0);
            var utm = ProjectedCoordinateSystem.WGS84_UTM(utmzone, polygon[0].Lat >= 0);
            var trans = ctfac.CreateFromCoordinateSystems(wgs84, utm);

            double prod1 = 0, prod2 = 0;
            for (int a = 0; a < polygon.Count - 1; a++)
            {
                double[] p1 = trans.MathTransform.Transform(new[] { polygon[a].Lng, polygon[a].Lat });
                double[] p2 = trans.MathTransform.Transform(new[] { polygon[a + 1].Lng, polygon[a + 1].Lat });
                prod1 += p1[0] * p2[1];
                prod2 += p1[1] * p2[0];
            }

            if (closeit)
                polygon.RemoveAt(polygon.Count - 1);

            return Math.Abs((prod1 - prod2) / 2.0);
        }

        private void AddWP(double Lng, double Lat, double Alt, object gridobject = null)
        {
            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, Lng, Lat, (int)(Alt * CurrentState.multiplierdist), gridobject);
        }

        double getAngleOfLongestSide(List<PointLatLngAlt> list)
        {
            if (list.Count == 0)
                return 0;
            double angle = 0;
            double maxdist = 0;
            PointLatLngAlt last = list[list.Count - 1];
            foreach (var item in list)
            {
                if (item.GetDistance(last) > maxdist)
                {
                    angle = item.GetBearing(last);
                    maxdist = item.GetDistance(last);
                }
                last = item;
            }

            return (angle + 360) % 360;
        }

        void getFOV(double flyalt, ref double fovh, ref double fovv)
        {
            double focallen = (double)NUM_focallength.Value;
            double sensorwidth = (double)NUM_senswidth.Value;
            double sensorheight = (double)NUM_sensheight.Value;

            // scale      mm / mm
            double flscale = (1000 * flyalt) / focallen;

            //   mm * mm / 1000
            double viewwidth = (sensorwidth * flscale / 1000);
            double viewheight = (sensorheight * flscale / 1000);

            fovh = viewwidth;
            fovv = viewheight;
        }

        void getFOVangle(ref double fovh, ref double fovv)
        {
            double focallen = (double)NUM_focallength.Value;
            double sensorwidth = (double)NUM_senswidth.Value;
            double sensorheight = (double)NUM_sensheight.Value;

            fovh = (float)(Math.Atan(sensorwidth / (2 * focallen)) * rad2deg * 2);
            fovv = (float)(Math.Atan(sensorheight / (2 * focallen)) * rad2deg * 2);
        }

        void doCalc()
        {
            try
            {
                // entered values
                float flyalt = (float)CurrentState.fromDistDisplayUnit((float)NUM_altitude.Value);
                int imagewidth = (int)NUM_imgwidth.Value;
                int imageheight = (int)NUM_imgheight.Value;

                int overlap = (int)num_overlap.Value;
                int sidelap = (int)num_sidelap.Value;

                double viewwidth = 0;
                double viewheight = 0;

                getFOV(flyalt, ref viewwidth, ref viewheight);

                NUM_fovH.Value = (decimal)viewwidth;
                NUM_fovV.Value = (decimal)viewheight;

                // Imperial
                feet_fovH = (viewwidth * 3.2808399f).ToString("#.#");
                feet_fovV = (viewheight * 3.2808399f).ToString("#.#");

                double cmpixel = viewheight / imageheight * 100; // cm per pixel
                inchpixel = DistUnits == "Feet"
                    ? (cmpixel * 0.393701).ToString("0.00") + " in/px"
                    : cmpixel.ToString("0.00") + " cm/px";

                NUM_OverlapDist.ValueChanged -= any_ValueChanged;
                NUM_SidelapDist.ValueChanged -= any_ValueChanged;

                // Landscape orientation (camera top facing forward): sensor width spans cross-track,
                // sensor height spans along-track.
                NUM_OverlapDist.Value = (decimal)((1 - (overlap / 100.0f)) * viewheight);
                NUM_SidelapDist.Value = (decimal)((1 - (sidelap / 100.0f)) * viewwidth);

                NUM_OverlapDist.ValueChanged += any_ValueChanged;
                NUM_SidelapDist.ValueChanged += any_ValueChanged;
            }
            catch { return; }
        }

        private void map_OnMarkerLeave(GMapMarker item)
        {
            if (!isMouseDown)
            {
                // when you click the context menu this triggers and causes problems
                CurrentGMapMarker = null;
            }
        }

        private void map_OnMarkerEnter(GMapMarker item)
        {
            if (!isMouseDown)
            {
                CurrentGMapMarker = item;
                CurrentGMapMarkerStartPos = CurrentGMapMarker.Position;
            }
        }

        private void map_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) // ignore right clicks
                return;

            if (isMouseDown && e.Button == MouseButtons.Left)
                isMouseDown = false;

            isMouseDraging = false;
            CurrentGMapMarker = null;
            CurrentGMapMarkerIndex = 0;
            CurrentGMapMarkerStartPos = null;
        }

        private void map_MouseDown(object sender, MouseEventArgs e)
        {
            MouseDownStart = map.FromLocalToLatLng(e.X, e.Y);

            if (e.Button == MouseButtons.Left && Control.ModifierKeys != Keys.Alt)
            {
                isMouseDown = true;
                isMouseDraging = false;

                if (CurrentGMapMarkerStartPos != null)
                    CurrentGMapMarkerIndex = poly_points.FindIndex(c => c.ToString() == CurrentGMapMarkerStartPos.ToString());
            }
        }

        private void map_MouseMove(object sender, MouseEventArgs e)
        {
            PointLatLng point = map.FromLocalToLatLng(e.X, e.Y);

            if (MouseDownStart == point)
                return;

            //draging
            if (e.Button == MouseButtons.Left && isMouseDown)
            {
                isMouseDraging = true;

                if (CurrentGMapMarker != null)
                {
                    if (CurrentGMapMarkerIndex == -1)
                    {
                        isMouseDraging = false;
                        return;
                    }

                    PointLatLng pnew = map.FromLocalToLatLng(e.X, e.Y);

                    CurrentGMapMarker.Position = pnew;

                    poly_points[CurrentGMapMarkerIndex] = new PointLatLngAlt(pnew);
                    any_ValueChanged(sender, e);
                }
                else // left click pan
                {
                    double latdif = MouseDownStart.Lat - point.Lat;
                    double lngdif = MouseDownStart.Lng - point.Lng;

                    try
                    {
                        lock (thisLock)
                        {
                            map.Position = new PointLatLng(map.Position.Lat + latdif, map.Position.Lng + lngdif);
                        }
                    }
                    catch { }
                }
            }
        }

        private void map_OnMapZoomChanged()
        {
            if (map.Zoom > 0)
            {
                try
                {
                    TRK_zoom.Value = (float)map.Zoom;
                }
                catch { }
            }
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            try
            {
                lock (thisLock)
                {
                    map.Zoom = TRK_zoom.Value;
                }
            }
            catch { }
        }

        private void CMB_camera_SelectedIndexChanged(object sender, EventArgs e)
        {
            loading = true;
            if (cameras.ContainsKey(CMB_camera.Text))
            {
                camerainfo camera = cameras[CMB_camera.Text];

                NUM_focallength.Value = (decimal)camera.focallen;
                NUM_imgheight.Value = (decimal)camera.imageheight;
                NUM_imgwidth.Value = (decimal)camera.imagewidth;
                NUM_sensheight.Value = (decimal)camera.sensorheight;
                NUM_senswidth.Value = (decimal)camera.sensorwidth;
            }
            loading = false;
            any_ValueChanged(null, null);
        }

        private void BUT_save_Click(object sender, EventArgs e)
        {
            camerainfo camera = new camerainfo();

            string camname = "Default";

            if (MissionPlanner.Controls.InputBox.Show("Camera Name", "Please and a camera name", ref camname) != System.Windows.Forms.DialogResult.OK)
                return;

            CMB_camera.Text = camname;

            // check if camera exists alreay
            if (cameras.ContainsKey(CMB_camera.Text))
            {
                camera = cameras[CMB_camera.Text];
            }
            else
            {
                cameras.Add(CMB_camera.Text, camera);
            }

            try
            {
                camera.name = CMB_camera.Text;
                camera.focallen = (float)NUM_focallength.Value;
                camera.imageheight = (float)NUM_imgheight.Value;
                camera.imagewidth = (float)NUM_imgwidth.Value;
                camera.sensorheight = (float)NUM_sensheight.Value;
                camera.sensorwidth = (float)NUM_senswidth.Value;
            }
            catch { CustomMessageBox.Show("One of your entries is not a valid number"); return; }

            cameras[CMB_camera.Text] = camera;

            xmlcamera(true, Settings.GetUserDataDirectory() + "cameras.xml");
        }

        private void BUT_Accept_Click(object sender, EventArgs e)
        {
            if (grid_points != null && grid_points.Count > 0)
            {
                MainV2.instance.FlightPlanner.quickadd = true;

                var gridobject = savegriddata();


                bool startedtrigdist = false;
                for (int i = 0; i < grid_points.Count; i++)
                {
                    var plla = grid_points[i];
                    // Add the waypoints and loiters
                    if (plla.Tag == "S" && double.TryParse(plla.Tag2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var loiterRadius))
                    {
                        // Tag2 holds the signed radius (positive=CW, negative=CCW) computed in recalculate_gridAsync,
                        // alternating between NUM_turnradius and NUM_turnradius2 per lane.
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.LOITER_TURNS,
                            0, 0, loiterRadius, 1,
                            plla.Lng, plla.Lat, plla.Alt,
                            gridobject);
                    }
                    else
                    {
                        AddWP(plla.Lng, plla.Lat, plla.Alt);
                    }

                    // Add the triggers
                    if (!chk_triginturns.Checked)
                    {
                        if (plla.Tag == "SM")
                        {
                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST,
                                (float)NUM_OverlapDist.Value,
                                0, 1, 0, 0, 0, 0, gridobject);
                        }
                        else if (plla.Tag == "ME")
                        {
                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, 0, 0, 1, 0,
                                0, 0, 0, gridobject);
                        }
                    }
                    else if (!startedtrigdist)
                    {
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST,
                            (float)NUM_OverlapDist.Value,
                            0, 1, 0, 0, 0, 0, gridobject);
                        startedtrigdist = true;
                    }
                }

                // Add the final stop-trigger
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, 0, 0, 1, 0, 0, 0, 0, gridobject);

                // Redraw the polygon in FP
                plugin.Host.RedrawFPPolygon(poly_points);

                // save camera fov's for use with footprints
                double fovha = 0;
                double fovva = 0;
                try
                {
                    getFOVangle(ref fovha, ref fovva);

                    Settings.Instance["camera_fovh"] = fovha.ToString();
                    Settings.Instance["camera_fovv"] = fovva.ToString();
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }

                savesettings();

                MainV2.instance.FlightPlanner.quickadd = false;

                MainV2.instance.FlightPlanner.writeKML();

                this.Close();
            }
            else
            {
                CustomMessageBox.Show("Bad Grid", "Error");
            }
        }

        private void CMB_startfrom_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading)
                return;

            if (CMB_startfrom.Text == Grid.StartPosition.Point.ToString())
            {
                int pnt = 1;
                InputBox.Show("Enter point #", "Please enter a boundary point number", ref pnt);

                if (poly_points.Count > pnt)
                    Grid.StartPointLatLngAlt = poly_points[pnt - 1];
            }

            any_ValueChanged(sender, e);
        }
    }
}
