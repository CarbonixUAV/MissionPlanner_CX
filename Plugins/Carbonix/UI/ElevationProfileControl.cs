using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Carbonix.Planning;

namespace Carbonix.UI
{
    /// <summary>
    /// Custom elevation-profile chart for corridor mission planning. Pure GDI+.
    ///
    /// Y axis  → altitude relative to home (terrain surface varies visually).
    /// Terrain surface drawn as a filled polygon from SRTM data.
    /// Floor/ceiling reference lines drawn from the loaded surface models + live offsets.
    /// Flight path drawn through loiter arcs.
    ///
    /// Interaction:
    ///   Hover near dot/bar → cursor SizeNS + highlight
    ///   Left-drag on dot/bar → adjust altitude
    ///   Left-drag empty → pan X+Y
    ///   Scroll → zoom Y   Ctrl+Scroll → zoom X   Double-click → reset
    /// </summary>
    internal class ElevationProfileControl : Control
    {
        // ── Public API ────────────────────────────────────────────────────────────

        public List<ElevationPoint> Points { get; private set; }
        public double MinAGLMetres { get; private set; } = 20;
        public double MaxAGLMetres { get; private set; } = 200;

        public double AltMultiplier { get; set; } = 1.0;
        public double DistMultiplier { get; set; } = 1.0;
        public string AltUnit { get; set; } = "m";
        public string DistUnit { get; set; } = "m";

        // Floor/ceiling reference lines: home-terrain datum + live offsets (metres). The line
        // at each sample is (surface AMSL + offset - HomeTerrainAlt); offsets are applied at
        // draw time so MSA/ceiling can be tweaked live without re-sampling. NaN offset = hide.
        public double HomeTerrainAlt { get; set; }
        public double MsaM { get; set; } = double.NaN;
        public double CeilingM { get; set; } = double.NaN;
        // Target (scan) AGL over raw SRTM, drawn green: line = terrain + TargetAglM. NaN = hide.
        public double TargetAglM { get; set; } = double.NaN;

        public event EventHandler<AltChangeEventArgs> AltitudeChanged;
        public event EventHandler<WaypointInsertEventArgs> WaypointInsertRequested;

        /// <summary>Fired on mouse-up after an XY drag of an inserted waypoint.</summary>
        public event EventHandler<InsertedWaypointMoveEventArgs> InsertedWaypointMoved;

        /// <summary>Fired when the user selects "Remove waypoint" from the context menu.</summary>
        public event EventHandler<WaypointRemoveEventArgs> WaypointRemoveRequested;

        /// <summary>Raised as the cursor moves over the plot, carrying the geographic
        /// position of the nearest profile sample so the map can mark it (null on leave).
        /// Only fires when the nearest sample changes, so it's cheap to handle.</summary>
        public event EventHandler<ProfileHoverEventArgs> HoverChanged;

        public void SetData(List<ElevationPoint> samples, double minAGLMetres, double maxAGLMetres, bool preserveView = false)
        {
            Points = samples;
            MinAGLMetres = minAGLMetres;
            MaxAGLMetres = maxAGLMetres;
            if (!preserveView) ResetView();
            Invalidate();
        }

        /// <summary>
        /// Programmatically zoom the X axis to show the given distance range
        /// (in the same display units as DistMultiplier, i.e. already converted).
        /// Y range is not affected.
        /// </summary>
        public void SetXRange(double xMin, double xMax)
        {
            if (xMax <= xMin) return;
            vx0 = xMin;
            vx1 = xMax;
            Invalidate();
        }

        // ── Constructor ───────────────────────────────────────────────────────────

        public ElevationProfileControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.DoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        // ── Layout ────────────────────────────────────────────────────────────────

        private const int ML = 58;
        private const int MR = 12;
        private const int MT = 8;
        private const int MB = 36;

        private RectangleF PlotArea
        {
            get
            {
                float w = Math.Max(1, Width - ML - MR);
                float h = Math.Max(1, Height - MT - MB);
                return new RectangleF(ML, MT, w, h);
            }
        }

        // ── View state ────────────────────────────────────────────────────────────

        private double vx0, vx1;
        private double vy0, vy1;

        private void ResetView()
        {
            vx0 = 0;
            vx1 = 1;
            vy0 = 0;
            vy1 = MaxAGLMetres * AltMultiplier * 1.5;

            if (Points == null || Points.Count == 0) return;

            double maxDist = Points.Max(s => s.DistM);
            vx1 = Math.Max(1, maxDist * DistMultiplier * 1.05);

            // Y: span from below terrain to above max AGL
            double minTerr = Points.Min(s => (s.TerrainAlt - s.HomeTerrainAlt) * AltMultiplier);
            double maxAltRel = Points.Max(s => s.AltRelM * AltMultiplier);

            vy0 = Math.Min(0, minTerr - MaxAGLMetres * AltMultiplier * 0.1);
            vy1 = Math.Max(maxAltRel, MaxAGLMetres * AltMultiplier) * 1.25;
        }

        // ── Coordinate transforms ─────────────────────────────────────────────────

        private PointF D2S(double x, double y)
        {
            var r = PlotArea;
            if (vx1 <= vx0 || vy1 <= vy0) return new PointF(r.Left, r.Bottom);
            float px = r.Left + (float)((x - vx0) / (vx1 - vx0) * r.Width);
            float py = r.Bottom - (float)((y - vy0) / (vy1 - vy0) * r.Height);
            return new PointF(px, py);
        }

        private (double x, double y) S2D(float px, float py)
        {
            var r = PlotArea;
            if (r.Width <= 0 || r.Height <= 0) return (vx0, vy0);
            double x = vx0 + (px - r.Left) / r.Width * (vx1 - vx0);
            double y = vy0 + (r.Bottom - py) / r.Height * (vy1 - vy0);
            return (x, y);
        }

        // ── Hit testing ───────────────────────────────────────────────────────────

        private const float DotSnap = 13f;
        private const float BarSnap = 8f;

        // Returns the hit dot/bar sample, or null. All vertices (incl. spurs) are
        // editable; identity is the sample's VertexId.
        private ElevationPoint HitTest(int ex, int ey)
        {
            if (Points == null || !PlotArea.Contains(ex, ey)) return null;

            foreach (var s in Points)
            {
                if (!s.IsLineWaypoint) continue;
                PointF sp = D2S(s.DistM * DistMultiplier, s.AltRelM * AltMultiplier);
                if (Math.Sqrt(Math.Pow(ex - sp.X, 2) + Math.Pow(ey - sp.Y, 2)) <= DotSnap)
                    return s;
            }

            foreach (var s in Points)
            {
                if (!s.IsLoiterWaypoint || s.LoiterArcLengthM <= 0) continue;
                double altRel = s.AltRelM * AltMultiplier;
                PointF p0 = D2S(s.DistM * DistMultiplier, altRel);
                PointF p1 = D2S((s.DistM + s.LoiterArcLengthM) * DistMultiplier, altRel);
                if (Math.Abs(ey - p0.Y) <= BarSnap && ex >= p0.X - 4 && ex <= p1.X + 4)
                    return s;
            }

            return null;
        }

        // ── Interaction state ─────────────────────────────────────────────────────

        private ElevationPoint hoveredSample;
        private ElevationPoint draggedSample;
        private ElevationPoint hoverPosSample;   // nearest sample to the cursor (cross-link)
        private double? hoverDistM;              // X of the cross-link cursor line (this control or the map)
        private bool isDragging;
        private bool isPanning;
        private float panStartX;
        private float panStartY;
        private double panVx0, panVx1;
        private double panVy0, panVy1;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (isDragging && e.Button == MouseButtons.Left)
            {
                HandleDrag(e.X, e.Y);
                return;
            }

            if (isPanning && e.Button == MouseButtons.Left)
            {
                HandlePan(e.X, e.Y);
                return;
            }

            var hit = HitTest(e.X, e.Y);
            if (!ReferenceEquals(hit, hoveredSample))
            {
                hoveredSample = hit;
                Cursor = hit != null ? Cursors.SizeNS : Cursors.Default;
                Invalidate();
            }

            UpdateHoverFromScreen(e.X, e.Y);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverPosSample != null || hoverDistM != null)
            {
                hoverPosSample = null;
                hoverDistM = null;
                Invalidate();
            }
            HoverChanged?.Invoke(this, ProfileHoverEventArgs.None);
        }

        // Cross-link to the map: track which sample the cursor is over and raise
        // HoverChanged when it changes (cheap — fires per sample crossing, not per pixel).
        private void UpdateHoverFromScreen(int ex, int ey)
        {
            ElevationPoint near = null;
            if (Points != null && Points.Count > 0 && PlotArea.Contains(ex, ey))
            {
                var (xData, _) = S2D(ex, ey);
                double distM = xData / (DistMultiplier <= 0 ? 1 : DistMultiplier);
                near = NearestByDist(distM);
            }

            if (ReferenceEquals(near, hoverPosSample)) return;
            hoverPosSample = near;
            hoverDistM = near?.DistM;
            Invalidate();
            HoverChanged?.Invoke(this, near != null
                ? new ProfileHoverEventArgs(near.Lat, near.Lng)
                : ProfileHoverEventArgs.None);
        }

        /// <summary>Show the cursor line at the sample nearest a geographic point (driven by
        /// the map cursor). Pass null to clear. Does not raise HoverChanged.</summary>
        public void SetExternalHover(double? lat, double? lng)
        {
            double? newDist = null;
            if (lat.HasValue && lng.HasValue && Points != null && Points.Count > 0)
                newDist = NearestByLatLng(lat.Value, lng.Value)?.DistM;

            if (Nullable.Equals(newDist, hoverDistM)) return;
            hoverDistM = newDist;
            Invalidate();
        }

        private ElevationPoint NearestByDist(double distM)
        {
            ElevationPoint best = null;
            double bestD = double.MaxValue;
            foreach (var s in Points)
            {
                double d = Math.Abs(s.DistM - distM);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private ElevationPoint NearestByLatLng(double lat, double lng)
        {
            ElevationPoint best = null;
            double bestD = double.MaxValue;
            foreach (var s in Points)
            {
                double dlat = s.Lat - lat, dlng = s.Lng - lng;
                double d = dlat * dlat + dlng * dlng;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // Vertical line marking the cursor position shared with the map.
        private void DrawHoverCursor(Graphics g, RectangleF r)
        {
            if (hoverDistM == null) return;
            float x = D2S(hoverDistM.Value * DistMultiplier, 0).X;
            if (x < r.Left || x > r.Right) return;
            using (var pen = new Pen(Color.FromArgb(180, 255, 210, 80), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, x, r.Top, x, r.Bottom);
        }

        private double _pendingInsertDistM;
        private double _pendingInsertAltRelM;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // Right-click insert/remove is deferred in the tour model.
            if (e.Button != MouseButtons.Left) return;

            var hit = HitTest(e.X, e.Y);
            if (hit != null)
            {
                draggedSample = hit;
                isDragging = true;
                Capture = true;
            }
            else if (PlotArea.Contains(e.X, e.Y))
            {
                isPanning = true;
                panStartX = e.X;
                panStartY = e.Y;
                panVx0 = vx0;
                panVx1 = vx1;
                panVy0 = vy0;
                panVy1 = vy1;
                Capture = true;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isDragging = false;
            isPanning = false;
            draggedSample = null;
            Capture = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            bool zoomX = (ModifierKeys & Keys.Control) != 0;
            double factor = e.Delta > 0 ? 0.8 : 1.25;

            if (zoomX)
            {
                var (cx, _) = S2D(e.X, e.Y);
                double w = (vx1 - vx0) * factor;
                double t = vx1 > vx0 ? (cx - vx0) / (vx1 - vx0) : 0.5;
                vx0 = Math.Max(0, cx - t * w);
                vx1 = vx0 + w;
            }
            else
            {
                var (_, cy) = S2D(e.X, e.Y);
                double h = (vy1 - vy0) * factor;
                double t = vy1 > vy0 ? (cy - vy0) / (vy1 - vy0) : 0.5;
                vy0 = cy - t * h;
                vy1 = vy0 + h;
            }
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            ResetView();
            Invalidate();
        }

        private void HandleDrag(int screenX, int screenY)
        {
            if (Points == null || draggedSample == null) return;

            var (_, newYData) = S2D(0, screenY);
            // Y axis IS AltRelM (altitude relative to home) — no terrain subtraction.
            double newAltRelM = newYData / AltMultiplier;

            draggedSample.AltRelM = newAltRelM;

            bool isLoiter = draggedSample.IsLoiterWaypoint;
            if (isLoiter)
            {
                // Keep the loiter's arc sub-samples at the same (constant) altitude.
                foreach (var arc in Points.Where(s => s.IsLoiterArcSample && s.Vertex == draggedSample.Vertex))
                    arc.AltRelM = newAltRelM;
            }

            // Fire so the form updates every generatedWp / sample sharing this VertexId
            // (a polyline flown out-and-back shares one altitude).
            AltitudeChanged?.Invoke(this, new AltChangeEventArgs(draggedSample.Vertex, isLoiter, newAltRelM));
            Invalidate();
        }

        private void HandlePan(float screenX, float screenY)
        {
            var r = PlotArea;
            if (r.Width <= 0 || r.Height <= 0) return;

            // X pan: no lower-bound clamp so you can pan into negative distances.
            double dx = (panStartX - screenX) / r.Width * (panVx1 - panVx0);
            vx0 = panVx0 + dx;
            vx1 = panVx1 + dx;

            // Y pan: inverted — dragging down raises the view (shows higher altitudes),
            // dragging up lowers it (shows lower altitudes), matching normal chart feel.
            double dy = (screenY - panStartY) / r.Height * (panVy1 - panVy0);
            vy0 = panVy0 + dy;
            vy1 = panVy1 + dy;

            Invalidate();
        }

        private void ShowInsertMenu(Point location)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Add waypoint here", null, (s, ev) =>
                WaypointInsertRequested?.Invoke(this, new WaypointInsertEventArgs(_pendingInsertDistM, _pendingInsertAltRelM)));
            menu.Show(this, location);
        }

        private int _pendingRemoveIdx;

        private void ShowRemoveMenu(int waypointIdx, Point location)
        {
            _pendingRemoveIdx = waypointIdx;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Remove waypoint", null, (s, ev) =>
                WaypointRemoveRequested?.Invoke(this, new WaypointRemoveEventArgs(_pendingRemoveIdx)));
            menu.Show(this, location);
        }

        // ── Paint ─────────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var r = PlotArea;
            if (r.Width < 2 || r.Height < 2) return;

            using (var bg = new SolidBrush(Color.FromArgb(22, 26, 32)))
                g.FillRectangle(bg, r);

            g.SetClip(r);

            DrawGrid(g, r);

            if (Points != null && Points.Count > 0)
            {
                DrawTerrainFill(g, r);
                DrawFloorCeiling(g);
                DrawLoiterArcs(g);
                DrawPlannedPath(g);
                DrawWaypointDots(g);
                DrawHoverCursor(g, r);
            }
            else
            {
                using (var f = new Font("Segoe UI", 9))
                using (var b = new SolidBrush(Color.FromArgb(100, 100, 100)))
                {
                    string msg = "Generate mission to see elevation profile";
                    var sz = g.MeasureString(msg, f);
                    g.DrawString(msg, f, b, r.Left + (r.Width - sz.Width) / 2, r.Top + (r.Height - sz.Height) / 2);
                }
            }

            g.ResetClip();
            DrawAxes(g, r);
            g.DrawRectangle(Pens.DimGray, r.Left, r.Top, r.Width, r.Height);
            DrawLegend(g, r);
        }

        // ── Drawing helpers ───────────────────────────────────────────────────────

        private void DrawGrid(Graphics g, RectangleF r)
        {
            using (var pen = new Pen(Color.FromArgb(38, 46, 52), 1))
            {
                foreach (double t in NiceTicks(vy0, vy1, 6))
                {
                    float py = D2S(0, t).Y;
                    if (py < r.Top || py > r.Bottom) continue;
                    g.DrawLine(pen, r.Left, py, r.Right, py);
                }
                foreach (double t in NiceTicks(vx0, vx1, 8))
                {
                    float px = D2S(t, 0).X;
                    if (px < r.Left || px > r.Right) continue;
                    g.DrawLine(pen, px, r.Top, px, r.Bottom);
                }
            }
        }

        private void DrawTerrainFill(Graphics g, RectangleF r)
        {
            // Exclude user-inserted WP samples (rough interpolated TerrainAlt → spikes) and
            // loiter centre anchors (they sit at the circle centre, off the flown track, and
            // collide in DistM with the entry-leg end → a vertical step into the loiter).
            var terrPts = Points
                .Where(s => !s.IsInserted && !s.IsLoiterWaypoint)
                .OrderBy(s => s.DistM)
                .ToList();

            if (terrPts.Count < 2) return;

            // Build polygon: terrain surface across top, then bottom corners
            var poly = new List<PointF>();
            foreach (var s in terrPts)
            {
                double terrRelM = s.TerrainAlt - s.HomeTerrainAlt;
                poly.Add(D2S(s.DistM * DistMultiplier, terrRelM * AltMultiplier));
            }

            // Close polygon at the bottom
            PointF last = poly.Last();
            PointF first = poly.First();
            // D2S(0, vy0).Y always equals r.Bottom by definition of the transform.
            poly.Add(new PointF(last.X, r.Bottom));
            poly.Add(new PointF(first.X, r.Bottom));

            if (poly.Count < 3) return;

            using (var fill = new SolidBrush(Color.FromArgb(90, 101, 70, 40)))
                g.FillPolygon(fill, poly.ToArray());

            // Terrain surface line
            var surfacePts = poly.Take(poly.Count - 2).ToArray();
            using (var pen = new Pen(Color.FromArgb(180, Color.SaddleBrown), 2))
                g.DrawLines(pen, surfacePts);
        }

        // Reference lines: floor/MSA (red) and ceiling (blue) from the surface models —
        // line value = surface AMSL + live offset - home; and the target/scan altitude
        // (green) = raw terrain + Target AGL. Loiter centre anchors are skipped (they're not
        // on the flown ground track); NaN surface samples break the line into segments.
        private void DrawFloorCeiling(Graphics g)
        {
            if (Points == null) return;
            DrawSurfaceLine(g, s => s.FloorSurfaceAmsl,   MsaM,     Color.Red);
            DrawSurfaceLine(g, s => s.CeilingSurfaceAmsl, CeilingM, Color.DeepSkyBlue);
            DrawTargetLine(g);
        }

        private void DrawSurfaceLine(Graphics g, Func<ElevationPoint, double> surfaceAmsl, double offsetM, Color color)
        {
            if (double.IsNaN(offsetM)) return;

            var seg = new List<PointF>();
            using (var pen = new Pen(Color.FromArgb(200, color), 1.5f) { DashStyle = DashStyle.Dash })
            {
                foreach (var s in Points.OrderBy(p => p.DistM))
                {
                    if (s.IsLoiterWaypoint) continue;   // centre anchor, not on the flown track
                    double amsl = surfaceAmsl(s);
                    if (double.IsNaN(amsl))
                    {
                        if (seg.Count >= 2) g.DrawLines(pen, seg.ToArray());
                        seg.Clear();
                        continue;
                    }
                    double relM = amsl + offsetM - HomeTerrainAlt;
                    seg.Add(D2S(s.DistM * DistMultiplier, relM * AltMultiplier));
                }
                if (seg.Count >= 2) g.DrawLines(pen, seg.ToArray());
            }
        }

        private void DrawTargetLine(Graphics g)
        {
            if (double.IsNaN(TargetAglM)) return;

            var seg = new List<PointF>();
            using (var pen = new Pen(Color.FromArgb(220, Color.LimeGreen), 1.5f) { DashStyle = DashStyle.Dash })
            {
                foreach (var s in Points.OrderBy(p => p.DistM))
                {
                    // Centre anchor / inserted: skip without breaking the line.
                    if (s.IsLoiterWaypoint || s.IsInserted) continue;

                    double relM = (s.TerrainAlt - s.HomeTerrainAlt) + TargetAglM;
                    seg.Add(D2S(s.DistM * DistMultiplier, relM * AltMultiplier));
                }
                if (seg.Count >= 2) g.DrawLines(pen, seg.ToArray());
            }
        }

        private void DrawLoiterArcs(Graphics g)
        {
            if (Points == null) return;
            foreach (var s in Points)
            {
                if (!s.IsLoiterWaypoint || s.LoiterArcLengthM <= 0) continue;
                bool active = ReferenceEquals(s, hoveredSample) || ReferenceEquals(s, draggedSample);
                double altDisp = s.AltRelM * AltMultiplier;
                Color col = active ? Color.Yellow : Color.DarkOrange;
                float thick = active ? 9f : 6f;

                double d0 = s.DistM;
                double total = s.LoiterArcLengthM;   // flown arc

                PointF pa = D2S(d0 * DistMultiplier, altDisp);
                PointF pb = D2S((d0 + total) * DistMultiplier, altDisp);
                using (var pen = new Pen(col, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, pa, pb);

                using (var b = new SolidBrush(col))
                {
                    g.FillEllipse(b, pa.X - 4, pa.Y - 4, 8, 8);
                    g.FillEllipse(b, pb.X - 4, pb.Y - 4, 8, 8);
                }
            }
        }

        private void DrawPlannedPath(Graphics g)
        {
            if (Points == null) return;

            // The aircraft flies at constant altitude (AltRelM) between waypoints — leg
            // terrain samples are excluded so the path is straight between actual waypoints.
            var pts = new List<PointF>();
            foreach (var s in Points.OrderBy(s => s.DistM))
            {
                if (s.IsLegTerrainSample) continue;
                pts.Add(D2S(s.DistM * DistMultiplier, s.AltRelM * AltMultiplier));
            }

            if (pts.Count >= 2)
            {
                using (var pen = new Pen(Color.FromArgb(160, Color.DodgerBlue), 1.5f))
                    g.DrawLines(pen, pts.ToArray());
            }
        }

        private void DrawWaypointDots(Graphics g)
        {
            if (Points == null) return;
            foreach (var s in Points)
            {
                if (!s.IsLineWaypoint) continue;
                bool active = ReferenceEquals(s, hoveredSample) || ReferenceEquals(s, draggedSample);

                PointF cp = D2S(s.DistM * DistMultiplier, s.AltRelM * AltMultiplier);
                float rr = active ? 8f : 5f;
                Color fill = active ? Color.Yellow : Color.DodgerBlue;

                using (var b = new SolidBrush(fill))
                    g.FillEllipse(b, cp.X - rr, cp.Y - rr, rr * 2, rr * 2);
                using (var pen = new Pen(Color.White, 1.2f))
                    g.DrawEllipse(pen, cp.X - rr, cp.Y - rr, rr * 2, rr * 2);
            }
        }

        private static readonly Font AxisFont = new Font("Segoe UI", 7.5f);
        private static readonly Brush AxisBrush = new SolidBrush(Color.FromArgb(170, 170, 170));

        private void DrawAxes(Graphics g, RectangleF r)
        {
            // Y ticks
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                foreach (double t in NiceTicks(vy0, vy1, 6))
                {
                    float py = D2S(0, t).Y;
                    if (py < r.Top - 2 || py > r.Bottom + 2) continue;
                    g.DrawLine(Pens.DimGray, r.Left - 4, py, r.Left, py);
                    g.DrawString(FormatTick(t), AxisFont, AxisBrush,
                        new RectangleF(0, py - 9, r.Left - 5, 18), sf);
                }
            }

            // X ticks
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            {
                foreach (double t in NiceTicks(vx0, vx1, 8))
                {
                    float px = D2S(t, 0).X;
                    if (px < r.Left - 2 || px > r.Right + 2) continue;
                    g.DrawLine(Pens.DimGray, px, r.Bottom, px, r.Bottom + 4);
                    g.DrawString(FormatTick(t), AxisFont, AxisBrush,
                        new RectangleF(px - 25, r.Bottom + 4, 50, 14), sf);
                }
            }

            // Axis unit labels
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(160, 160, 160)))
            {
                g.TranslateTransform(11, r.Top + r.Height / 2);
                g.RotateTransform(-90);
                g.DrawString($"Alt rel home ({AltUnit})", f, b, 0, 0,
                    new StringFormat { Alignment = StringAlignment.Center });
                g.ResetTransform();

                g.DrawString($"Distance ({DistUnit})", f, b,
                    r.Left + r.Width / 2, r.Bottom + 22,
                    new StringFormat { Alignment = StringAlignment.Center });
            }
        }

        private void DrawLegend(Graphics g, RectangleF r)
        {
            var items = new List<(Color c, bool dash, float w, string label)>
            {
                (Color.SaddleBrown,  false, 2.5f, "Terrain"),
            };
            if (!double.IsNaN(MsaM))
                items.Add((Color.Red, true, 1.5f, $"Floor (MSA {MsaM * AltMultiplier:F0} {AltUnit})"));
            if (!double.IsNaN(CeilingM))
                items.Add((Color.DeepSkyBlue, true, 1.5f, $"Ceiling ({CeilingM * AltMultiplier:F0} {AltUnit})"));
            if (!double.IsNaN(TargetAglM))
                items.Add((Color.LimeGreen, true, 1.5f, $"Target ({TargetAglM * AltMultiplier:F0} {AltUnit})"));
            items.Add((Color.DarkOrange, false, 5f, "Loiter arc"));
            items.Add((Color.DodgerBlue, false, 1.5f, "Planned path"));

            float x = r.Left + 6;
            float y = r.Top + 4;
            using (var f = new Font("Segoe UI", 7f))
            using (var bg = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            {
                foreach (var (c, dash, lw, label) in items)
                {
                    float tw = g.MeasureString(label, f).Width;
                    g.FillRectangle(bg, x - 2, y - 1, 24 + tw + 4, 13);
                    using (var pen = new Pen(c, lw))
                    {
                        if (dash) pen.DashStyle = DashStyle.Dash;
                        g.DrawLine(pen, x, y + 6, x + 18, y + 6);
                    }
                    using (var tb = new SolidBrush(c))
                        g.DrawString(label, f, tb, x + 20, y);
                    x += 26 + tw;
                    if (x > r.Right - 60) { x = r.Left + 6; y += 14; }
                }
            }
        }

        // ── Tick helpers ──────────────────────────────────────────────────────────

        private static string FormatTick(double v)
        {
            return Math.Abs(v) >= 10000 ? v.ToString("0.#e0")
                 : Math.Abs(v) >= 100   ? v.ToString("F0")
                 : Math.Abs(v) >= 1     ? v.ToString("F1")
                 : v.ToString("F2");
        }

        private static IEnumerable<double> NiceTicks(double min, double max, int n)
        {
            if (max <= min || n < 1) yield break;
            double range = max - min;
            double rough = range / n;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rough, 1e-10))));
            double[] steps = { 1, 2, 5, 10 };
            double step = steps.First(s => s * mag >= rough) * mag;
            if (step <= 0) yield break;
            double start = Math.Ceiling(min / step) * step;
            for (double v = start; v <= max + step * 0.001; v += step)
                yield return Math.Round(v, 10);
        }
    }

    // ── Event args ────────────────────────────────────────────────────────────────

    internal class AltChangeEventArgs : EventArgs
    {
        public VertexId Vertex { get; }
        public bool IsLoiter { get; }
        /// <summary>New altitude relative to home (metres).</summary>
        public double NewAltRelM { get; }

        public AltChangeEventArgs(VertexId vertex, bool isLoiter, double altRelM)
        {
            Vertex = vertex;
            IsLoiter = isLoiter;
            NewAltRelM = altRelM;
        }
    }

    internal class ProfileHoverEventArgs : EventArgs
    {
        /// <summary>Geographic position of the hovered sample, or null when the cursor
        /// has left the plot.</summary>
        public double? Lat { get; }
        public double? Lng { get; }

        public ProfileHoverEventArgs(double? lat, double? lng) { Lat = lat; Lng = lng; }

        public static readonly ProfileHoverEventArgs None = new ProfileHoverEventArgs(null, null);
    }

    internal class WaypointInsertEventArgs : EventArgs
    {
        /// <summary>Requested insert position in metres along the centreline path.</summary>
        public double DistM { get; }
        /// <summary>Altitude relative to home (metres) at the right-click position.</summary>
        public double AltRelM { get; }

        public WaypointInsertEventArgs(double distM, double altRelM)
        {
            DistM   = distM;
            AltRelM = altRelM;
        }
    }

    internal class InsertedWaypointMoveEventArgs : EventArgs
    {
        public int WaypointIndex { get; }
        /// <summary>Final position in metres along the centreline profile after XY drag.</summary>
        public double NewDistM { get; }
        /// <summary>Final altitude relative to home (metres) after XY drag.</summary>
        public double NewAltRelM { get; }

        public InsertedWaypointMoveEventArgs(int idx, double newDistM, double newAltRelM)
        {
            WaypointIndex = idx;
            NewDistM = newDistM;
            NewAltRelM = newAltRelM;
        }
    }

    internal class WaypointRemoveEventArgs : EventArgs
    {
        public int WaypointIndex { get; }

        public WaypointRemoveEventArgs(int idx)
        {
            WaypointIndex = idx;
        }
    }
}
