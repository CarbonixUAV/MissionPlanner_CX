using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace GMap.NET.WindowsForms
{
    /// <summary>
    /// used to override the drawing of the waypoint box bounding
    /// </summary>
    [Serializable]
    public class GMapMarkerRect : GMapMarker
    {
        public Pen Pen = new Pen(Brushes.White, 2);

        public Color Color
        {
            get { return Pen.Color; }
            set
            {
                if (!initcolor.HasValue) initcolor = value;
                Pen.Color = value;
            }
        }

        Color? initcolor = null;

        public Color? FillColor = null;

        public GMapMarker InnerMarker;

        public double wprad = 0;

        /// <summary>
        /// Direction of travel around the circle: 1 clockwise, -1 counter-clockwise, 0 draws no arrows.
        /// </summary>
        public int LoiterDirection = 0;

        /// <summary>Arrow arm length in pixels, matching <see cref="GMapRoute.ArrowLength"/>.</summary>
        public float ArrowLength = 15;

        public void ResetColor()
        {
            if (initcolor.HasValue)
                Color = initcolor.Value;
            else
                Color = Color.White;
        }

        public GMapMarkerRect(PointLatLng p)
            : base(p)
        {
            Pen.DashStyle = DashStyle.Dash;

            // do not forget set Size of the marker
            // if so, you shall have no event on it ;}
            Size = new System.Drawing.Size(50, 50);
            Offset = new System.Drawing.Point(-Size.Width/2, -Size.Height/2 - 20);
        }

        public override void OnRender(IGraphics g)
        {
            base.OnRender(g);

            if (wprad == 0 || Overlay.Control == null)
                return;

            // if we have drawn it, then keep that color
            if (!initcolor.HasValue)
                Color = Color.White;

            // undo autochange in mouse over
            //if (Pen.Color == Color.Blue)
            //  Pen.Color = Color.White;

            double width =
            (Overlay.Control.MapProvider.Projection.GetDistance(Overlay.Control.FromLocalToLatLng(0, 0),
                 Overlay.Control.FromLocalToLatLng(Overlay.Control.Width, 0))*1000.0);
            double height =
            (Overlay.Control.MapProvider.Projection.GetDistance(Overlay.Control.FromLocalToLatLng(0, 0),
                 Overlay.Control.FromLocalToLatLng(Overlay.Control.Height, 0))*1000.0);
            double m2pixelwidth = Overlay.Control.Width/width;
            double m2pixelheight = Overlay.Control.Height/height;

            GPoint loc = new GPoint((int) (LocalPosition.X - (m2pixelwidth*wprad*2)), LocalPosition.Y);
            // MainMap.FromLatLngToLocal(wpradposition);

            if (m2pixelheight > 0.001 && !double.IsInfinity(m2pixelheight))
            {
                var rect = new System.Drawing.Rectangle(
                    LocalPosition.X - Offset.X - (int) (Math.Abs(loc.X - LocalPosition.X) / 2),
                    LocalPosition.Y - Offset.Y - (int) Math.Abs(loc.X - LocalPosition.X) / 2,
                    (int) Math.Abs(loc.X - LocalPosition.X), (int) Math.Abs(loc.X - LocalPosition.X));

                if (rect.Height == 0 || rect.Width == 0)
                    return;

                g.DrawArc(Pen, rect, 0, 360);

                if (FillColor.HasValue)
                {
                    g.FillPie(new SolidBrush(FillColor.Value), rect, 0, 360);
                }

                if (LoiterDirection != 0)
                {
                    DrawDirectionArrows(g, rect);
                }
            }
        }

        void DrawDirectionArrows(IGraphics g, System.Drawing.Rectangle circle)
        {
            using (var solid = new Pen(Pen.Color, Pen.Width))
            {
                foreach (var arrow in DirectionArrows(circle, LoiterDirection, ArrowLength))
                {
                    g.DrawLine(solid, arrow[0].X, arrow[0].Y, arrow[1].X, arrow[1].Y);
                    g.DrawLine(solid, arrow[0].X, arrow[0].Y, arrow[2].X, arrow[2].Y);
                }
            }
        }

        /// <summary>
        /// Computes arrowheads tangent to the circle at its top and bottom, pointing in the direction of travel.
        /// </summary>
        /// <param name="circle">Bounding box of the circle in pixel coordinates.</param>
        /// <param name="direction">1 clockwise, -1 counter-clockwise; 0 yields no arrows.</param>
        /// <param name="arrowLength">Arm length in pixels.</param>
        /// <returns>One [tip, left arm end, right arm end] triple per arrow; empty when the radius is shorter than an arm.</returns>
        public static List<PointF[]> DirectionArrows(RectangleF circle, int direction, float arrowLength)
        {
            var arrows = new List<PointF[]>();
            float radius = circle.Width / 2f;
            if (direction == 0 || radius < arrowLength)
                return arrows;

            float cx = circle.X + radius;
            float cy = circle.Y + radius;
            double deg2rad = Math.PI / 180.0;

            // Screen coordinates: y grows downward, so compass bearing b is at (sin b, -cos b)
            // and the clockwise tangent there is (cos b, sin b).
            foreach (var bearing in new[] { 0.0, 180.0 })
            {
                double b = bearing * deg2rad;
                float tipX = cx + radius * (float)Math.Sin(b);
                float tipY = cy - radius * (float)Math.Cos(b);
                double travel = Math.Atan2(direction * Math.Sin(b), direction * Math.Cos(b));

                double leftAngle = travel + 210 * deg2rad;
                double rightAngle = travel - 210 * deg2rad;

                arrows.Add(new[]
                {
                    new PointF(tipX, tipY),
                    new PointF(tipX + arrowLength * (float)Math.Cos(leftAngle), tipY + arrowLength * (float)Math.Sin(leftAngle)),
                    new PointF(tipX + arrowLength * (float)Math.Cos(rightAngle), tipY + arrowLength * (float)Math.Sin(rightAngle)),
                });
            }

            return arrows;
        }
    }
}