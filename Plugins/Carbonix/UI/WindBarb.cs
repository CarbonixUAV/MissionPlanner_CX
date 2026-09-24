using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Carbonix
{
    /// <summary>
    /// Represents the elements of a wind barb symbol: how many pennants,
    /// full barbs, and half barbs to draw.
    /// </summary>
    public struct BarbSymbol
    {
        /// <summary>Under 1 kt: station circle only, no staff.</summary>
        public bool Calm;
        public int Pennants;
        public int FullBarbs;
        public int HalfBarbs;

        /// <summary>Gets a value indicating whether the staff has nothing on it (1 to 2 kt).</summary>
        public bool BareStaff => !Calm && Pennants == 0 && FullBarbs == 0 && HalfBarbs == 0;
    }

    /// <summary>
    /// Wind bug drawn as a standard station-plot wind barb.
    /// </summary>
    public class WindBarb : UserControl
    {
        public const int BugSize = 80;

        double _speedKnots;
        double _directionDeg;
        double _displaySpeed;
        string _caption = "";
        bool _stale;

        public WindBarb()
        {
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            DoubleBuffered = true;
            Size = new Size(BugSize, BugSize);
            Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
        }

        /// <summary>Gets or sets the speed the barbs are drawn for, knots.</summary>
        public double SpeedKnots { get { return _speedKnots; } set { if (_speedKnots == value) return; _speedKnots = value; Invalidate(); } }

        /// <summary>Gets or sets the direction the wind is coming from, degrees true.</summary>
        public double DirectionDeg { get { return _directionDeg; } set { if (_directionDeg == value) return; _directionDeg = value; Invalidate(); } }

        /// <summary>Gets or sets the speed drawn as a number in the top-left corner, in the GCS units.</summary>
        public double DisplaySpeed { get { return _displaySpeed; } set { if (_displaySpeed == value) return; _displaySpeed = value; Invalidate(); } }

        /// <summary>Gets or sets the caption drawn in the bottom-right corner.</summary>
        public string Caption { get { return _caption; } set { value = value ?? ""; if (_caption == value) return; _caption = value; Invalidate(); } }

        /// <summary>
        /// Gets or sets a value indicating whether the source has stopped
        /// updating: the symbol is dimmed and the speed shows as "--".
        /// </summary>
        public bool Stale { get { return _stale; } set { if (_stale == value) return; _stale = value; Invalidate(); } }

        /// <summary>
        /// Formats a speed as a whole number to two significant figures:
        /// 7.4 reads 7, 123 reads 120.
        /// </summary>
        public static string FormatSpeed(double speed)
        {
            var step = Math.Abs(speed) < 99.5 ? 1 : 10;
            return (Math.Round(speed / step, MidpointRounding.AwayFromZero) * step).ToString("0");
        }

        /// <summary>
        /// Rounds a speed to the nearest 5 kt and splits it into pennants,
        /// full barbs, and half barbs.
        /// </summary>
        public static BarbSymbol Symbol(double knots)
        {
            var symbol = new BarbSymbol();
            if (knots < 1.0)
            {
                symbol.Calm = true;
                return symbol;
            }
            int fives = (int)Math.Round(knots / 5.0, MidpointRounding.AwayFromZero);
            symbol.Pennants = fives / 10;
            fives -= symbol.Pennants * 10;
            symbol.FullBarbs = fives / 2;
            symbol.HalfBarbs = fives % 2;
            return symbol;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var color = _stale ? Color.FromArgb(110, ForeColor) : ForeColor;
            var center = new PointF(Width / 2f, Height / 2f);

            // Number top-left, caption bottom-right, and the ring is the
            // largest circle 3 px clear of both boxes. The boxes are
            // measured for "000" and "GND", not the actual text, so the
            // ring is the same on every bug and does not change size with
            // the value.
            var speedText = _stale ? "--" : FormatSpeed(_displaySpeed);
            var tight = StringFormat.GenericTypographic;
            var speedBox = g.MeasureString("000", Font, Width, tight);
            var captionFont = new Font(Font.FontFamily, Font.Size - 1, Font.Style);
            var captionReserve = g.MeasureString("GND", captionFont, Width, tight);
            var captionBox = g.MeasureString(_caption, captionFont, Width, tight);
            float ringRadius = Math.Min(
                Distance(center, new PointF(speedBox.Width, speedBox.Height)),
                Distance(center, new PointF(Width - captionReserve.Width, Height - captionReserve.Height))) - 3;
            ringRadius = Math.Min(ringRadius, Math.Min(Width, Height) / 2f - 1);

            using (captionFont)
            using (var ringPen = new Pen(Color.FromArgb(90, ForeColor), 1))
            using (var pen = new Pen(color, Math.Max(2f, ringRadius / 13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                g.DrawEllipse(ringPen, center.X - ringRadius, center.Y - ringRadius, 2 * ringRadius, 2 * ringRadius);

                DrawSymbol(g, pen, brush, center, ringRadius);

                g.DrawString(speedText, Font, brush, 0, 0, tight);
                if (_caption.Length > 0)
                    g.DrawString(_caption, captionFont, brush, Width - captionBox.Width, Height - captionBox.Height, tight);
            }
        }

        static float Distance(PointF a, PointF b)
        {
            return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>
        /// Draws the station circle, staff, and barbs, all sized from the
        /// ring radius.
        /// </summary>
        void DrawSymbol(Graphics g, Pen pen, Brush brush, PointF center, float ringRadius)
        {
            float k = ringRadius / 26f;
            float stationRadius = 3 * k;
            g.DrawEllipse(pen, center.X - stationRadius, center.Y - stationRadius, 2 * stationRadius, 2 * stationRadius);

            var symbol = Symbol(_speedKnots);
            if (symbol.Calm)
            {
                float calmRadius = 7 * k;
                g.DrawEllipse(pen, center.X - calmRadius, center.Y - calmRadius, 2 * calmRadius, 2 * calmRadius);
                return;
            }

            // Unit vector along the staff, from the station towards where
            // the wind is coming from. Screen y points down.
            double rad = _directionDeg * Math.PI / 180.0;
            var u = new PointF((float)Math.Sin(rad), (float)-Math.Cos(rad));
            // Perpendicular on the barb side: clockwise from the staff looking
            // out from the station, as on NWS charts
            // https://www.weather.gov/hfo/windbarbinfo
            var p = new PointF(-u.Y, u.X);

            float staffLength = 21 * k;
            var tip = Add(center, u, staffLength);
            g.DrawLine(pen, Add(center, u, stationRadius), tip);

            float barbLength = 9.5f * k;
            float spacing = 4.2f * k;
            float pennantBase = 5.2f * k;

            // Barbs lean towards the tip, about 60 degrees off the staff
            PointF Lean(PointF from, float length)
            {
                return new PointF(
                    from.X + length * (0.5f * u.X + 0.866f * p.X),
                    from.Y + length * (0.5f * u.Y + 0.866f * p.Y));
            }

            float along = 0;
            for (int i = 0; i < symbol.Pennants; i++)
            {
                var a = Add(tip, u, -along);
                var b = Add(tip, u, -(along + pennantBase));
                g.FillPolygon(brush, new[] { a, Lean(a, barbLength), b });
                along += pennantBase + k;
            }
            for (int i = 0; i < symbol.FullBarbs; i++)
            {
                var a = Add(tip, u, -along);
                g.DrawLine(pen, a, Lean(a, barbLength));
                along += spacing;
            }
            if (symbol.HalfBarbs > 0)
            {
                // A half barb on its own sits one slot in, so it is not read
                // as a short full barb
                if (along == 0)
                    along = spacing;
                var a = Add(tip, u, -along);
                g.DrawLine(pen, a, Lean(a, barbLength / 2));
            }
        }

        static PointF Add(PointF from, PointF direction, float distance)
        {
            return new PointF(from.X + direction.X * distance, from.Y + direction.Y * distance);
        }
    }
}
