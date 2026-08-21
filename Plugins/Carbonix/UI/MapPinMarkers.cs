using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace Carbonix
{
    /// <summary>
    /// Provides the shared colors, font and box geometry for the map pin, its
    /// info box and the ruler readout.
    /// </summary>
    static class MapPinStyle
    {
        // A fixed palette rather than ThemeManager's: these draw over satellite
        // and street imagery, not over application chrome.

        /// <summary>
        /// Pin and rubber-band color; neither red nor amber, which mean
        /// something else on a GCS.
        /// </summary>
        public static readonly Color Accent = Color.FromArgb(120, 220, 255);

        /// <summary>
        /// Outline laid under the accent so the pin survives a white rooftop.
        /// </summary>
        public static readonly Color Halo = Color.FromArgb(190, 0, 0, 0);

        public static readonly Font TextFont = new Font("Consolas", 12f, FontStyle.Bold, GraphicsUnit.Pixel);

        public static readonly Brush BoxFill = new SolidBrush(Color.FromArgb(215, 20, 22, 26));
        public static readonly Brush TextBrush = new SolidBrush(Color.FromArgb(235, 245, 250));
        public static readonly Pen BoxBorder = new Pen(Color.FromArgb(180, 120, 220, 255), 1.5f);

        /// <summary>
        /// Left-aligned, non-wrapping layout for the readout boxes, where the
        /// explicit newlines are the only line breaks.
        /// </summary>
        public static readonly StringFormat LeftAligned = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        static readonly Size Padding = new Size(8, 5);

        const int CornerRadius = 6;

        /// <summary>
        /// Determines whether a local position is too far off the map for GDI+
        /// to draw sanely; the same guard the stock markers use.
        /// </summary>
        public static bool OffTheDeepEnd(Point local)
        {
            return Math.Abs(local.X) > 100000 || Math.Abs(local.Y) > 100000;
        }

        /// <summary>
        /// Calculates the outer size of a box holding <paramref name="text"/>.
        /// </summary>
        public static Size Measure(IGraphics g, string text)
        {
            // Ceiling, not truncate - the box has to hold the text.
            var size = Size.Ceiling(g.MeasureString(text, TextFont));
            return new Size(size.Width + Padding.Width * 2, size.Height + Padding.Height * 2);
        }

        /// <summary>
        /// Calculates the outer rectangle of a box holding
        /// <paramref name="text"/>, hung above and to the right of
        /// <paramref name="anchor"/>.
        /// </summary>
        public static Rectangle BoxAbove(IGraphics g, string text, Point anchor, Point offset)
        {
            var size = Measure(g, text);
            return new Rectangle(
                anchor.X + offset.X,
                anchor.Y + offset.Y - size.Height,
                size.Width,
                size.Height);
        }

        /// <summary>
        /// Draws a rounded box with <paramref name="text"/> inside it.
        /// </summary>
        public static void DrawBox(IGraphics g, string text, Rectangle box)
        {
            using (var path = Rounded(box, CornerRadius))
            {
                g.FillPath(BoxFill, path);
                g.DrawPath(BoxBorder, path);
            }

            var textArea = new Rectangle(
                box.X + Padding.Width,
                box.Y + Padding.Height,
                box.Width - Padding.Width * 2,
                box.Height - Padding.Height * 2);

            g.DrawString(text, TextFont, TextBrush, textArea, LeftAligned);
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    /// <summary>
    /// Represents the pin's persistent info box, laid out left-aligned above
    /// and to the right of the pin.
    /// </summary>
    class MapPinToolTip : GMapToolTip
    {
        public MapPinToolTip(GMapMarker marker) : base(marker)
        {
            Offset = new Point(20, -18);
        }

        public override void OnRender(IGraphics g)
        {
            var text = Marker.ToolTipText;
            if (string.IsNullOrEmpty(text))
                return;

            // ToolTipPosition is the marker's anchor, i.e. the pinned point.
            var anchor = Marker.ToolTipPosition;
            if (MapPinStyle.OffTheDeepEnd(anchor))
                return;

            var box = MapPinStyle.BoxAbove(g, text, anchor, Offset);

            g.DrawLine(MapPinStyle.BoxBorder, anchor.X, anchor.Y, box.X, box.Bottom);
            MapPinStyle.DrawBox(g, text, box);
        }
    }

    /// <summary>
    /// Represents the dropped pin: a crosshair on the exact point, carrying a
    /// persistent info box.
    /// </summary>
    public class GMapMarkerMapPin : GMapMarker
    {
        /// <summary>
        /// Supplies the info-box text; called on every repaint.
        /// </summary>
        public Func<string> BuildText;

        const int Radius = 7;
        const int Arm = 15;

        static readonly Pen HaloPen = new Pen(MapPinStyle.Halo, 4f);
        static readonly Pen AccentPen = new Pen(MapPinStyle.Accent, 2f);
        static readonly Brush AccentFill = new SolidBrush(MapPinStyle.Accent);

        public GMapMarkerMapPin(PointLatLng position) : base(position)
        {
            Size = new Size(34, 34);
            Offset = new Point(-Size.Width / 2, -Size.Height / 2);

            // Clicks on the pin are resolved by MapPinTool against
            // LocalAreaInControlSpace. Staying out of the map's own hit test
            // keeps the pin from claiming IsMouseOverMarker, which would
            // swallow pan starts and flip the cursor to a hand whenever the
            // pointer crossed it. Wheel zoom is unaffected either way, since
            // the flight map sets IgnoreMarkerOnMouseWheel.
            IsHitTestVisible = false;

            ToolTip = new MapPinToolTip(this);
            ToolTipMode = MarkerTooltipMode.Always;
        }

        public override void OnRender(IGraphics g)
        {
            // Markers render before the tooltip pass, so refreshing here lands
            // in the same frame as the box that displays it.
            if (BuildText != null)
                ToolTipText = BuildText();

            // The marker's anchor, i.e. the pinned point itself.
            var c = ToolTipPosition;
            if (MapPinStyle.OffTheDeepEnd(c))
                return;

            DrawCrosshair(g, HaloPen, c);
            DrawCrosshair(g, AccentPen, c);

            g.FillEllipse(AccentFill, c.X - 2, c.Y - 2, 4, 4);
        }

        static void DrawCrosshair(IGraphics g, Pen pen, Point c)
        {
            g.DrawEllipse(pen, c.X - Radius, c.Y - Radius, Radius * 2, Radius * 2);

            g.DrawLine(pen, c.X, c.Y - Arm, c.X, c.Y - Radius - 2);
            g.DrawLine(pen, c.X, c.Y + Radius + 2, c.X, c.Y + Arm);
            g.DrawLine(pen, c.X - Arm, c.Y, c.X - Radius - 2, c.Y);
            g.DrawLine(pen, c.X + Radius + 2, c.Y, c.X + Arm, c.Y);
        }
    }

    /// <summary>
    /// Represents the ruler readout: a box riding just off the cursor with its
    /// distance and bearing from the pin.
    /// </summary>
    public class GMapMarkerRulerLabel : GMapMarker
    {
        public string Text = "";

        // Up and to the right, clear of the pointer itself.
        static readonly Point BoxOffset = new Point(18, -14);

        public GMapMarkerRulerLabel(PointLatLng position) : base(position)
        {
            Size = Size.Empty;
            Offset = Point.Empty;

            // Size is empty and the box is painted off to one side, but stay
            // out of the hit test anyway - nothing here is clickable.
            IsHitTestVisible = false;

            IsVisible = false;
        }

        public override void OnRender(IGraphics g)
        {
            if (string.IsNullOrEmpty(Text) || MapPinStyle.OffTheDeepEnd(LocalPosition))
                return;

            MapPinStyle.DrawBox(g, Text,
                MapPinStyle.BoxAbove(g, Text, LocalPosition, BoxOffset));
        }
    }
}
