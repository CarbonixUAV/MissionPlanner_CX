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

// SharpKml
using SharpKml.Dom;
using SharpKml.Engine;

namespace Carbonix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Ceiling zones: the approval areas. Each is a closed polygon with its own AGL
    // ceiling — typically one blanket area (say 120 m) plus islands approved higher
    // (300 m). Where zones overlap the highest ceiling applies; outside every zone the
    // Options tab's global Ceiling field applies, so the blanket area needn't be a
    // polygon at all. Zone ceilings are offsets over the ceiling surface, the same
    // quantity as that field. Reference lines only — generation never clamps to them.
    // ─────────────────────────────────────────────────────────────────────────────
    public partial class CorridorPlanForm
    {
        private readonly List<CeilingZone> ceilingZones = new List<CeilingZone>();

        // Zone polygons + labels, underneath the corridor/mission overlays.
        private GMapOverlay layer_zones;

        // Options-tab group (built in code to avoid hand-editing the Designer).
        private GroupBox grp_zones;
        private DataGridView DGV_zones;
        private MissionPlanner.Controls.MyButton BUT_zones_add;
        private MissionPlanner.Controls.MyButton BUT_zones_remove;
        private bool zonesGridUpdating;

        private const string ZoneColName = "Zone";
        private const string ZoneColCeiling = "Ceiling";

        private static readonly Color ZoneColor = Color.DeepSkyBlue;   // the profile's ceiling-line colour

        // ─── Setup (called from the constructor) ────────────────────────────────────
        private void BuildZonesGroup()
        {
            layer_zones = new GMapOverlay("zones");
            map.Overlays.Insert(0, layer_zones);

            grp_zones = new GroupBox
            {
                Text = "Ceiling Zones",
                Dock = DockStyle.Top,
                Padding = new Padding(4),
                Height = 134,
            };

            DGV_zones = new DataGridView
            {
                Location = new System.Drawing.Point(6, 18),
                Size = new Size(261, 82),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            };
            DGV_zones.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ZoneColName, HeaderText = "Zone", ReadOnly = true, FillWeight = 62,
            });
            DGV_zones.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ZoneColCeiling, HeaderText = "Ceiling", FillWeight = 38,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
            });
            DGV_zones.CellValueChanged += DGV_zones_CellValueChanged;
            DGV_zones.DataError += (s, ev) => ev.ThrowException = false;

            BUT_zones_add = new MissionPlanner.Controls.MyButton
            {
                Text = "Add…", Location = new System.Drawing.Point(6, 106), Size = new Size(80, 23),
            };
            BUT_zones_remove = new MissionPlanner.Controls.MyButton
            {
                Text = "Remove", Location = new System.Drawing.Point(92, 106), Size = new Size(80, 23),
            };
            BUT_zones_add.Click += BUT_zones_add_Click;
            BUT_zones_remove.Click += BUT_zones_remove_Click;

            grp_zones.Controls.Add(DGV_zones);
            grp_zones.Controls.Add(BUT_zones_add);
            grp_zones.Controls.Add(BUT_zones_remove);

            // Top-docked children stack from the END of the collection, so index 0 is the
            // bottom of the panel: directly under the corridor file list (grp_mainline).
            pnl_scroll.Controls.Add(grp_zones);
            pnl_scroll.Controls.SetChildIndex(grp_zones, 0);
        }

        // ─── Add / remove / edit ────────────────────────────────────────────────────
        private void BUT_zones_add_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Open Ceiling Zone File(s)";
                dlg.Filter = "All Supported|*.kml;*.kmz;*.shp|KML/KMZ|*.kml;*.kmz|Shapefile|*.shp";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                // A zone whose file carries no altitude starts at the global Ceiling value.
                double defaultM = (double)NUM_maxagl.Value / CurrentState.multiplieralt;
                int added = 0;
                foreach (var path in dlg.FileNames)
                {
                    List<CeilingZone> zones;
                    try
                    {
                        zones = LoadCeilingZones(path, defaultM);
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show("Error loading file:\n" + ex.Message, "Load Error");
                        continue;
                    }

                    if (zones == null || zones.Count == 0)
                    {
                        CustomMessageBox.Show(
                            "No polygon geometry found in " + Path.GetFileName(path) + ".",
                            "No Zones Found");
                        continue;
                    }

                    ceilingZones.AddRange(zones);
                    added += zones.Count;
                }
                if (added == 0) return;
            }

            ZonesChanged();
            if (AllFeatures().Count == 0) ZoomToFitZones();
        }

        private void BUT_zones_remove_Click(object sender, EventArgs e)
        {
            var rows = DGV_zones.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Index).OrderByDescending(i => i).ToList();
            if (rows.Count == 0) return;

            foreach (var i in rows) ceilingZones.RemoveAt(i);
            ZonesChanged();
        }

        // Ceiling cell edited: parse in display units, store metres. An unparseable entry just
        // redraws the old value. The grid rebuild is deferred — clearing rows from inside the
        // grid's own edit-commit event is a reentrant call it rejects.
        private void DGV_zones_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (zonesGridUpdating || e.RowIndex < 0 || e.RowIndex >= ceilingZones.Count) return;
            if (DGV_zones.Columns[e.ColumnIndex].Name != ZoneColCeiling) return;

            var text = DGV_zones.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            if (double.TryParse(text, out double v) && v >= 0)
                ceilingZones[e.RowIndex].CeilingAglM = v / CurrentState.multiplieralt;

            BeginInvoke(new Action(ZonesChanged));
        }

        // Zones or their ceilings changed: refresh the grid + map and re-sample the profile.
        private void ZonesChanged()
        {
            RefreshZonesGrid();
            DrawZones();

            if (elevationPoints == null) return;
            SampleCeilingZones(elevationPoints, ceilingZones);
            ApplySurfaceOffsets();
            elev_profile.Invalidate();
        }

        private void RefreshZonesGrid()
        {
            zonesGridUpdating = true;
            try
            {
                double mult = CurrentState.multiplieralt;
                DGV_zones.Columns[ZoneColCeiling].HeaderText = $"Ceiling ({CurrentState.AltUnit})";
                DGV_zones.Rows.Clear();
                foreach (var z in ceilingZones)
                    DGV_zones.Rows.Add(z.Name, (z.CeilingAglM * mult).ToString("F0"));
            }
            finally
            {
                zonesGridUpdating = false;
            }
        }

        // Per-sample zone ceiling for the profile. A few ray casts per sample, so it simply
        // runs again whenever a zone changes — no regenerate needed.
        internal static void SampleCeilingZones(List<ElevationPoint> samples, List<CeilingZone> zones)
        {
            foreach (var ep in samples)
                ep.CeilingZoneAglM = CeilingZone.CeilingAt(zones, ep.Lat, ep.Lng) ?? double.NaN;
        }

        // ─── Map ────────────────────────────────────────────────────────────────────
        private void DrawZones()
        {
            layer_zones.Polygons.Clear();
            layer_zones.Markers.Clear();

            double mult = CurrentState.multiplieralt;
            foreach (var z in ceilingZones)
            {
                var pts = z.Ring.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList();
                layer_zones.Polygons.Add(new GMapPolygon(pts, z.Name)
                {
                    IsHitTestVisible = false,   // never steal clicks from the Edit tab's handles
                    Fill = new SolidBrush(Color.FromArgb(40, ZoneColor)),
                    Stroke = new Pen(Color.FromArgb(200, ZoneColor), 2),
                });

                // Label at the ring's mean point. Fine for approval areas; a very concave
                // shape may put it outside the boundary.
                var at = new PointLatLng(z.Ring.Average(p => p.Lat), z.Ring.Average(p => p.Lng));
                var m = new GMapMarkerRect(at)
                {
                    IsHitTestVisible = false,
                    ToolTipMode = MarkerTooltipMode.Always,
                    ToolTipText = $"{z.Name}: {z.CeilingAglM * mult:F0} {CurrentState.AltUnit}",
                };
                m.ToolTip = new GMapToolTip(m)
                {
                    Offset = new System.Drawing.Point(0, -6),
                    Fill = new SolidBrush(Color.FromArgb(200, ZoneColor)),
                    Foreground = new SolidBrush(Color.Black),
                    Stroke = new Pen(Color.FromArgb(180, Color.Black), 1),
                };
                layer_zones.Markers.Add(m);
            }
            map.Invalidate();
        }

        private void ZoomToFitZones()
        {
            var all = ceilingZones.SelectMany(z => z.Ring).ToList();
            if (all.Count == 0) return;
            double minLat = all.Min(p => p.Lat), maxLat = all.Max(p => p.Lat);
            double minLng = all.Min(p => p.Lng), maxLng = all.Max(p => p.Lng);
            map.SetZoomToFitRect(new RectLatLng(maxLat, minLng, maxLng - minLng, maxLat - minLat));
        }

        // ─── Loaders ────────────────────────────────────────────────────────────────
        private static List<CeilingZone> LoadCeilingZones(string path, double defaultCeilingM)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".kml" || ext == ".kmz")
                return LoadKmlZones(path, defaultCeilingM);
            if (ext == ".shp")
                return LoadShpZones(path, defaultCeilingM);
            return null;
        }

        // Each Placemark polygon (outer boundary only) becomes a zone. The ceiling comes from
        // an altitude in the placemark name ("Area B 300m", "North (1000 ft)"), else the default.
        private static List<CeilingZone> LoadKmlZones(string path, double defaultCeilingM)
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

            string fileBase = Path.GetFileNameWithoutExtension(path);
            var zones = new List<CeilingZone>();
            foreach (var pm in kml.Root.Flatten().OfType<SharpKml.Dom.Placemark>())
            {
                if (pm.Geometry == null) continue;
                var polys = pm.Geometry.Flatten().OfType<Polygon>().ToList();
                for (int i = 0; i < polys.Count; i++)
                {
                    var coords = polys[i].OuterBoundary?.LinearRing?.Coordinates;
                    if (coords == null) continue;
                    var ring = coords
                        .Select(c => new PointLatLngAlt(c.Latitude, c.Longitude, c.Altitude ?? 0))
                        .ToList();
                    if (ring.Count < 3) continue;

                    string name = string.IsNullOrWhiteSpace(pm.Name) ? $"{fileBase} {zones.Count + 1}" : pm.Name;
                    if (polys.Count > 1) name += $" #{i + 1}";
                    zones.Add(new CeilingZone(name, ring, CeilingZone.ParseCeilingFromName(name) ?? defaultCeilingM));
                }
            }
            return zones;
        }

        // Shapefile polygons; each part of a multipolygon is its own zone. Name from a "name"
        // attribute when there is one; ceiling from a "ceiling" attribute, else the name, else
        // the default.
        private static List<CeilingZone> LoadShpZones(string path, double defaultCeilingM)
        {
            var fs = DotSpatial.Data.FeatureSet.Open(path);
            if (fs == null || fs.Features.Count == 0)
                return null;

            DotSpatial.Projections.ProjectionInfo srcProj = null;
            string prjPath = Path.ChangeExtension(path, ".prj");
            if (File.Exists(prjPath))
                srcProj = DotSpatial.Projections.ProjectionInfo.Open(prjPath);

            var wgs84 = DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

            string fileBase = Path.GetFileNameWithoutExtension(path);
            string nameCol = null, ceilCol = null;
            try
            {
                var cols = fs.DataTable.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList();
                nameCol = cols.FirstOrDefault(c => c.Equals("name", StringComparison.OrdinalIgnoreCase));
                ceilCol = cols.FirstOrDefault(c => c.IndexOf("ceil", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                log.Warn("Ceiling zone shapefile attributes unavailable: " + ex.Message);
            }

            var zones = new List<CeilingZone>();
            int featureNo = 0;
            foreach (var feature in fs.Features)
            {
                featureNo++;
                var geom = feature.Geometry;
                if (geom == null) continue;
                if (geom.GeometryType != "Polygon" && geom.GeometryType != "MultiPolygon")
                    continue;

                string name = Attribute(feature, nameCol);
                if (string.IsNullOrWhiteSpace(name)) name = $"{fileBase} {featureNo}";

                double? ceiling = null;
                if (double.TryParse(Attribute(feature, ceilCol), out double attrCeiling))
                    ceiling = attrCeiling;
                ceiling = ceiling ?? CeilingZone.ParseCeilingFromName(name) ?? defaultCeilingM;

                for (int gi = 0; gi < geom.NumGeometries; gi++)
                {
                    if (!(geom.GetGeometryN(gi) is GeoAPI.Geometries.IPolygon poly)) continue;
                    var coords = poly.ExteriorRing?.Coordinates;
                    if (coords == null || coords.Length < 3) continue;

                    double[] xs = coords.Select(k => k.X).ToArray();
                    double[] ys = coords.Select(k => k.Y).ToArray();
                    double[] zs = coords.Select(k => k.Z).ToArray();

                    if (srcProj != null && !srcProj.Equals(wgs84))
                    {
                        double[] xy = new double[xs.Length * 2];
                        for (int i = 0; i < xs.Length; i++) { xy[i * 2] = xs[i]; xy[i * 2 + 1] = ys[i]; }
                        DotSpatial.Projections.Reproject.ReprojectPoints(xy, zs, srcProj, wgs84, 0, xs.Length);
                        for (int i = 0; i < xs.Length; i++) { xs[i] = xy[i * 2]; ys[i] = xy[i * 2 + 1]; }
                    }

                    var ring = new List<PointLatLngAlt>(xs.Length);
                    for (int i = 0; i < xs.Length; i++)
                        ring.Add(new PointLatLngAlt(ys[i], xs[i], double.IsNaN(zs[i]) ? 0 : zs[i]));

                    zones.Add(new CeilingZone(
                        geom.NumGeometries > 1 ? $"{name} #{gi + 1}" : name, ring, ceiling.Value));
                }
            }

            return zones;
        }

        private static string Attribute(DotSpatial.Data.IFeature feature, string column)
        {
            if (column == null) return null;
            try
            {
                return feature.DataRow?[column]?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
