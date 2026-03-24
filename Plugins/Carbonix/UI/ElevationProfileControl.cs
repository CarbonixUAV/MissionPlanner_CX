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
    /// Min/max AGL shown as terrain-following dashed bands.
    /// Flight path drawn through loiter arcs.
    ///
    /// Interaction:
    ///   Hover near dot/bar → cursor SizeNS + highlight
    ///   Left-drag on dot/bar → adjust AGL (clamped to min/max)
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

        public event EventHandler<AltChangeEventArgs> AltitudeChanged;
        public event EventHandler<WaypointInsertEventArgs> WaypointInsertRequested;

        /// <summary>Fired on mouse-up after an XY drag of an inserted waypoint.</summary>
        public event EventHandler<InsertedWaypointMoveEventArgs> InsertedWaypointMoved;

        /// <summary>Fired when the user selects "Remove waypoint" from the context menu.</summary>
        public event EventHandler<WaypointRemoveEventArgs> WaypointRemoveRequested;

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

        private (int idx, bool isLoiter) HitTest(int ex, int ey)
        {
            if (Points == null) return (-1, false);
            if (!PlotArea.Contains(ex, ey)) return (-1, false);

            // Line waypoint dots
            foreach (var s in Points)
            {
                if (!s.IsLineWaypoint) continue;
                PointF sp = D2S(s.DistM * DistMultiplier, s.AltRelM * AltMultiplier);
                if (Math.Sqrt(Math.Pow(ex - sp.X, 2) + Math.Pow(ey - sp.Y, 2)) <= DotSnap)
                    return (s.WaypointIndex, false);
            }

            // Loiter arc bars
            foreach (var s in Points)
            {
                if (!s.IsLoiterWaypoint || s.LoiterArcLengthM <= 0) continue;
                double altRel = s.AltRelM * AltMultiplier;
                PointF p0 = D2S(s.DistM * DistMultiplier, altRel);
                PointF p1 = D2S((s.DistM + s.LoiterArcLengthM) * DistMultiplier, altRel);
                if (Math.Abs(ey - p0.Y) <= BarSnap && ex >= p0.X - 4 && ex <= p1.X + 4)
                    return (s.WaypointIndex, true);
            }

            return (-1, false);
        }

        // ── Interaction state ─────────────────────────────────────────────────────

        private int hoveredIdx = -1;
        private bool hoveredIsLoiter;
        private int draggedIdx = -1;
        private bool draggedIsLoiter;
        private bool isDragging;
        private bool _isDraggingXY;   // true when the dragged dot is an inserted (XY-movable) WP
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

            var (idx, isLoiter) = HitTest(e.X, e.Y);
            if (idx != hoveredIdx || isLoiter != hoveredIsLoiter)
            {
                hoveredIdx = idx;
                hoveredIsLoiter = isLoiter;
                if (idx >= 0 && !isLoiter)
                {
                    var hs = Points?.FirstOrDefault(s => s.WaypointIndex == idx && s.IsLineWaypoint);
                    Cursor = hs?.IsInserted == true ? Cursors.SizeAll : Cursors.SizeNS;
                }
                else
                {
                    Cursor = idx >= 0 ? Cursors.SizeNS : Cursors.Default;
                }
                Invalidate();
            }
        }

        private double _pendingInsertDistM;
        private double _pendingInsertAltRelM;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                if (PlotArea.Contains(e.X, e.Y))
                {
                    // If right-clicking on an inserted WP dot, offer removal instead.
                    var (hitIdx, hitIsLoiter) = HitTest(e.X, e.Y);
                    if (hitIdx >= 0 && !hitIsLoiter)
                    {
                        var hs = Points?.FirstOrDefault(s => s.WaypointIndex == hitIdx && s.IsLineWaypoint);
                        if (hs?.IsInserted == true)
                        {
                            ShowRemoveMenu(hitIdx, e.Location);
                            return;
                        }
                    }
                    var (distX, altY) = S2D(e.X, e.Y);
                    _pendingInsertDistM   = distX / DistMultiplier;
                    _pendingInsertAltRelM = altY  / AltMultiplier;
                    ShowInsertMenu(e.Location);
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            var (idx, isLoiter) = HitTest(e.X, e.Y);
            if (idx >= 0)
            {
                draggedIdx = idx;
                draggedIsLoiter = isLoiter;
                isDragging = true;
                // Inserted (user-added) line-WP dots support XY dragging.
                var ds = Points?.FirstOrDefault(s => s.WaypointIndex == idx && s.IsLineWaypoint);
                _isDraggingXY = !isLoiter && ds?.IsInserted == true;
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
            // Commit an XY drag before clearing state.
            if (_isDraggingXY && isDragging && draggedIdx >= 0)
            {
                var ms = Points?.FirstOrDefault(s => s.WaypointIndex == draggedIdx && s.IsLineWaypoint);
                if (ms != null)
                    InsertedWaypointMoved?.Invoke(this, new InsertedWaypointMoveEventArgs(draggedIdx, ms.DistM, ms.AltRelM));
            }
            isDragging = false;
            _isDraggingXY = false;
            isPanning = false;
            draggedIdx = -1;
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
            if (Points == null || draggedIdx < 0) return;

            var (_, newYData) = S2D(0, screenY);
            // Y axis is AltRelM (altitude relative to home) — no terrain subtraction needed.
            double newAltRelM = newYData / AltMultiplier;

            // Match by type so a dot drag finds the line-WP sample and a bar drag
            // finds the loiter-anchor sample — they can have independent altitudes.
            var sample = draggedIsLoiter
                ? Points.FirstOrDefault(s => s.WaypointIndex == draggedIdx && s.IsLoiterWaypoint)
                : Points.FirstOrDefault(s => s.WaypointIndex == draggedIdx && s.IsLineWaypoint);
            if (sample == null) return;

            // Update the sample's AltRelM directly — the Y axis IS AltRelM.
            sample.AltRelM = newAltRelM;

            if (_isDraggingXY)
            {
                // XY drag: both X (position along corridor) and Y (altitude) are live.
                // Move the dot along X.  Clamp to stay between neighbouring vertex
                // samples, including loiter bars.  For a preceding loiter the lower
                // bound is the ARC END (DistM + LoiterArcLengthM), not the arc start —
                // dragging into the arc's DistM range would make ComputeLegFraction
                // return a negative offset, snapping the geographic position to nearly
                // the loiter vertex and causing a large Y jump on mouse-up.
                var (newDistX, _) = S2D(screenX, 0);
                double newDistM = newDistX / DistMultiplier;

                var vtxWps = Points
                    .Where(s => s.IsLineWaypoint || s.IsLoiterWaypoint)
                    .OrderBy(s => s.WaypointIndex)
                    .ToList();
                var prevWp = vtxWps.LastOrDefault(s => s.WaypointIndex < draggedIdx);
                var nextWp = vtxWps.FirstOrDefault(s => s.WaypointIndex > draggedIdx);

                double prevBound = prevWp == null ? double.MinValue
                    : prevWp.IsLoiterWaypoint ? prevWp.DistM + prevWp.LoiterArcLengthM + 0.5
                    : prevWp.DistM + 0.5;
                double nextBound = nextWp == null ? double.MaxValue
                    : nextWp.DistM - 0.5;

                newDistM = Math.Max(newDistM, prevBound);
                newDistM = Math.Min(newDistM, nextBound);
                sample.DistM = newDistM;
                // AltitudeChanged not fired during XY drag — the move event on MouseUp handles it.
            }
            else
            {
                // Y-only drag: fire AltitudeChanged so the form updates generatedWps immediately.
                // For a loiter bar also update arc sub-samples to keep them at constant AltRelM.
                if (draggedIsLoiter)
                {
                    foreach (var arc in Points.Where(s => s.IsLoiterArcSample && s.WaypointIndex == draggedIdx))
                        arc.AltRelM = newAltRelM;
                }
                AltitudeChanged?.Invoke(this, new AltChangeEventArgs(draggedIdx, draggedIsLoiter, newAltRelM));
            }
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
                DrawMinMaxBands(g, r);
                DrawLoiterArcs(g);
                DrawPlannedPath(g);
                DrawWaypointDots(g);
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
            // Exclude: (a) the XY-dragged sample (stale terrain at old position),
            //          (b) user-inserted WP samples (TerrainAlt is a rough interpolation,
            //              not an actual SRTM query — including them creates spikes).
            var terrPts = Points
                .Where(s => !s.IsInserted)
                .Where(s => !(_isDraggingXY && s.WaypointIndex == draggedIdx && s.IsLineWaypoint))
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

        private void DrawMinMaxBands(Graphics g, RectangleF r)
        {
            // Same exclusion as DrawTerrainFill: skip the XY-dragged sample and
            // user-inserted WP samples (approximate terrain → would distort the bands).
            var ordered = Points
                .Where(s => !s.IsInserted)
                .Where(s => !(_isDraggingXY && s.WaypointIndex == draggedIdx && s.IsLineWaypoint))
                .OrderBy(s => s.DistM)
                .ToList();

            if (ordered.Count < 2) return;

            var minPts = new List<PointF>();
            var maxPts = new List<PointF>();

            foreach (var s in ordered)
            {
                double terrRelDisp = (s.TerrainAlt - s.HomeTerrainAlt) * AltMultiplier;
                double x = s.DistM * DistMultiplier;
                minPts.Add(D2S(x, terrRelDisp + MinAGLMetres * AltMultiplier));
                maxPts.Add(D2S(x, terrRelDisp + MaxAGLMetres * AltMultiplier));
            }

            if (minPts.Count >= 2)
            {
                using (var pen = new Pen(Color.FromArgb(200, Color.Red), 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawLines(pen, minPts.ToArray());
            }
            if (maxPts.Count >= 2)
            {
                using (var pen = new Pen(Color.FromArgb(200, Color.Orange), 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawLines(pen, maxPts.ToArray());
            }
        }

        private void DrawLoiterArcs(Graphics g)
        {
            if (Points == null) return;
            foreach (var s in Points)
            {
                if (!s.IsLoiterWaypoint || s.LoiterArcLengthM <= 0) continue;
                bool active = s.WaypointIndex == hoveredIdx || s.WaypointIndex == draggedIdx;
                double altDisp = s.AltRelM * AltMultiplier;
                PointF p0 = D2S(s.DistM * DistMultiplier, altDisp);
                PointF p1 = D2S((s.DistM + s.LoiterArcLengthM) * DistMultiplier, altDisp);

                using (var pen = new Pen(active ? Color.Yellow : Color.DarkOrange, active ? 9f : 6f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, p0, p1);

                // Endpoint markers
                using (var b = new SolidBrush(active ? Color.Yellow : Color.DarkOrange))
                {
                    g.FillEllipse(b, p0.X - 4, p0.Y - 4, 8, 8);
                    g.FillEllipse(b, p1.X - 4, p1.Y - 4, 8, 8);
                }
            }
        }

        private void DrawPlannedPath(Graphics g)
        {
            if (Points == null) return;

            // The aircraft flies at constant altitude (AltRelM) between waypoints —
            // it does NOT terrain-follow along straight legs.  Intermediate leg terrain
            // samples are therefore excluded so the path is straight lines between
            // actual waypoints.  Loiter arc sub-samples carry adjusted AltAGL to keep
            // the plotted altitude constant through the arc, which is correct.
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
                bool active = (s.WaypointIndex == hoveredIdx && !hoveredIsLoiter)
                           || (s.WaypointIndex == draggedIdx && !draggedIsLoiter);

                PointF cp = D2S(s.DistM * DistMultiplier, s.AltRelM * AltMultiplier);
                float rr = active ? 8f : 5f;
                // Inserted (user-added) WPs are lime green; original WPs are blue.
                Color fill = active ? Color.Yellow : (s.IsInserted ? Color.LimeGreen : Color.DodgerBlue);

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
            var items = new (Color c, bool dash, float w, string label)[]
            {
                (Color.SaddleBrown,  false, 2.5f, "Terrain"),
                (Color.Red,          true,  1.5f, $"Min AGL ({MinAGLMetres * AltMultiplier:F0} {AltUnit})"),
                (Color.Orange,       true,  1.5f, $"Max AGL ({MaxAGLMetres * AltMultiplier:F0} {AltUnit})"),
                (Color.DarkOrange,   false, 5f,   "Loiter arc"),
                (Color.DodgerBlue,   false, 1.5f, "Planned path"),
            };

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
        public int WaypointIndex { get; }
        public bool IsLoiter { get; }
        /// <summary>New altitude relative to home (metres).</summary>
        public double NewAltRelM { get; }

        public AltChangeEventArgs(int idx, bool isLoiter, double altRelM)
        {
            WaypointIndex = idx;
            IsLoiter = isLoiter;
            NewAltRelM = altRelM;
        }
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
