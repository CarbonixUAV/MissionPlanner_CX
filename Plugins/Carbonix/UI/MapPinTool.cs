using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;

namespace Carbonix
{
    /// <summary>
    /// A drop-a-pin-and-measure tool for the flight map, hung off the plain
    /// left click - the one map gesture Mission Planner leaves unused.
    ///
    /// A click drops a pin carrying a persistent info box. Another click
    /// somewhere else moves it; a click back on the pin takes it away. Hold
    /// Shift with the pin up and a rubber band follows the cursor with a
    /// running range and bearing - the quick "how far is that from there"
    /// answer that otherwise means opening the planner.
    ///
    /// The ruler is on Shift rather than standing up on its own because it is
    /// a measuring aid, not scenery: it should be there for the few seconds
    /// you are asking and gone the rest of the time.
    ///
    /// The plugin constructs one of these and disposes it on unload.
    /// </summary>
    public class MapPinTool : IDisposable
    {
        /// <summary>
        /// How far the mouse may travel between press and release and still
        /// count as a click. Panning the map ends in a mouse-up too.
        /// </summary>
        const int ClickSlop = 3;

        readonly PluginHost _host;
        readonly GMapControl _map;

        // An overlay renders routes, then markers, then tooltips, so nothing
        // inside one can be lifted above its own tooltips. The readout that
        // rides the cursor gets an overlay of its own, stacked above, or it
        // ends up underneath the pin it is measuring from - which is exactly
        // where the eye is at close range. The rubber band stays below so it
        // does not draw across the face of the info box.
        readonly GMapOverlay _overlay = new GMapOverlay("cbx map pin");
        readonly GMapOverlay _readout = new GMapOverlay("cbx map pin readout");

        readonly GMapRoute _ruler = new GMapRoute("cbx ruler");
        readonly GMapMarkerRulerLabel _label = new GMapMarkerRulerLabel(PointLatLng.Empty);

        // Shift can go down or up without the mouse moving, and the map has no
        // keyboard focus to hear about it, so the state gets polled. Only runs
        // while a pin is up.
        readonly System.Windows.Forms.Timer _shift_watch =
            new System.Windows.Forms.Timer { Interval = 120 };

        GMapMarkerMapPin _pin;
        Point _pressed;
        Keys _pressed_modifiers;

        // Terrain under the pin. srtm queues a tile fetch and answers "invalid"
        // until it lands, so an unresolved lookup is worth retrying - but not
        // on every repaint.
        srtm.altresponce _terrain;
        DateTime _terrain_retry = DateTime.MinValue;

        public MapPinTool(PluginHost host)
        {
            _host = host;
            _map = host.FDGMapControl;

            _ruler.Stroke = new Pen(Color.FromArgb(200, MapPinStyle.Accent), 2f)
            {
                DashStyle = DashStyle.Dash,
            };
            _ruler.IsVisible = false;

            _overlay.Routes.Add(_ruler);
            _readout.Markers.Add(_label);

            // Added last so the pin and its box sit over the mission, the
            // tracks and the aircraft rather than under them.
            _map.Overlays.Add(_overlay);
            _map.Overlays.Add(_readout);

            _map.MouseDown += OnMouseDown;
            _map.MouseMove += OnMouseMove;
            _map.MouseUp += OnMouseUp;
            _map.MouseLeave += OnMouseLeave;
            _map.OnMapZoomChanged += OnMapZoomChanged;

            _shift_watch.Tick += OnShiftWatch;
        }

        public void Dispose()
        {
            _map.MouseDown -= OnMouseDown;
            _map.MouseMove -= OnMouseMove;
            _map.MouseUp -= OnMouseUp;
            _map.MouseLeave -= OnMouseLeave;
            _map.OnMapZoomChanged -= OnMapZoomChanged;

            _shift_watch.Stop();
            _shift_watch.Tick -= OnShiftWatch;
            _shift_watch.Dispose();

            _map.Overlays.Remove(_readout);
            _readout.Dispose();

            _map.Overlays.Remove(_overlay);
            _overlay.Dispose();
        }

        /// <summary>
        /// True when the ruler should be on screen: a pin exists and the
        /// operator is holding Shift right now.
        /// </summary>
        bool RulerWanted()
        {
            return _pin != null && (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        }

        void SyncShiftWatch()
        {
            _shift_watch.Enabled = _pin != null;
        }

        // --------------------------------------------------
        //                  Event handlers
        // --------------------------------------------------

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = e.Location;

                // Sampled at press, when the gesture was classified: the
                // flight map acts on ctrl at MouseDown, and the key is often
                // gone again by the time the button comes back up.
                _pressed_modifiers = Control.ModifierKeys;
            }
        }

        void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            // Ctrl-click is "fly to here"; alt and shift drive the selection
            // box. None of those should leave a pin behind.
            if (_pressed_modifiers != Keys.None)
                return;

            // The flight map pans left drags itself without engaging the map
            // engine's drag state, so it is the slop test below that actually
            // catches pans. This catches a left release during an engine drag
            // on the right button, where IsDragging is still true at MouseUp.
            if (_map.Core.IsDragging)
                return;

            if (Math.Abs(e.X - _pressed.X) > ClickSlop || Math.Abs(e.Y - _pressed.Y) > ClickSlop)
                return;

            if (_pin != null && _pin.LocalAreaInControlSpace.Contains(e.Location))
                RemovePin();
            else
                PlacePin(_map.FromLocalToLatLng(e.X, e.Y), e.Location);

            _map.Invalidate();
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_pin == null)
                return;

            // A button down means the map is being panned or an area selected,
            // and the rubber band would just trail along behind.
            if (e.Button != MouseButtons.None || !RulerWanted())
            {
                HideRuler();
                return;
            }

            UpdateRuler(e.Location);
        }

        void OnMouseLeave(object sender, EventArgs e)
        {
            HideRuler();
        }

        void OnMapZoomChanged()
        {
            // A wheel zoom leaves the cursor over new ground without moving
            // it, so nothing else would refresh the reading.
            if (!RulerWanted() || _map.InvokeRequired)
                return;

            UpdateRulerAtCursor();
        }

        void OnShiftWatch(object sender, EventArgs e)
        {
            var wanted = RulerWanted();

            if (!wanted)
            {
                HideRuler();
                return;
            }

            if (!_ruler.IsVisible)
                UpdateRulerAtCursor();
        }

        /// <summary>
        /// Refreshes the ruler against wherever the pointer happens to be, for
        /// the cases where it moved under the cursor rather than the other way
        /// round.
        /// </summary>
        void UpdateRulerAtCursor()
        {
            var cursor = _map.PointToClient(Cursor.Position);

            if (_map.ClientRectangle.Contains(cursor))
                UpdateRuler(cursor);
        }

        // --------------------------------------------------
        //                     The pin
        // --------------------------------------------------

        void PlacePin(PointLatLng position, Point cursor)
        {
            if (_pin == null)
            {
                var pin = new GMapMarkerMapPin(position);
                pin.BuildText = () => BuildPinText(pin);

                _pin = pin;
                _overlay.Markers.Add(pin);
            }
            else
            {
                _pin.Position = position;
            }

            _terrain = null;
            _terrain_retry = DateTime.MinValue;

            SyncShiftWatch();

            if (RulerWanted())
                UpdateRuler(cursor);
        }

        void RemovePin()
        {
            if (_pin != null)
            {
                _overlay.Markers.Remove(_pin);
                _pin.Dispose();
                _pin = null;
            }

            SyncShiftWatch();
            HideRuler();
        }

        /// <summary>
        /// Builds the pin's info box. Everything is measured from the pin
        /// outwards, so the bearing is the one you would give someone standing
        /// on the point looking for the aircraft.
        /// </summary>
        /// <remarks>
        /// The aircraft reading runs over two lines, each referenced to one
        /// thing. On top, everything the map knows: chart distance, true
        /// bearing, and the octant that follows the grid. Below, the aviation
        /// pair - nautical miles and magnetic - which reads straight onto a
        /// radio without picking figures out of two rows. The octant is up top
        /// because it is grid-referenced, and because it serves whoever is
        /// looking at the screen rather than whoever is talking.
        ///
        /// Unlike the ruler readout, the box keeps its nautical miles below
        /// 1 NM: the slot is standing there either way, so filling it costs
        /// nothing. The line still drops out on its own if it ends up with
        /// nothing to say. Columns are space-padded to line up under the
        /// monospaced box font.
        /// </remarks>
        string BuildPinText(GMapMarkerMapPin marker)
        {
            PointLatLngAlt pin = marker.Position;

            var text = pin.Lat.ToString("0.0000000") + "  " + pin.Lng.ToString("0.0000000");

            var terrain = Terrain(pin);
            if (terrain != null)
            {
                text += "\n" + Col("Terrain") +
                        CurrentState.toAltDisplayUnit(terrain.alt).ToString("0") + " " + CurrentState.AltUnit;
            }

            var aircraft = _host.cs.Location;
            if (aircraft.Lat == 0 && aircraft.Lng == 0)
                return text;

            return text + "\n" + RangeAndBearing("A/C",
                       pin.GetDistance(aircraft),
                       pin.GetBearing(aircraft),
                       suppressShortNauticalMiles: false);
        }

        /// <summary>
        /// The range-and-bearing block shared by the pin box and the ruler.
        /// Chart distance, true bearing and the grid octant on top; the
        /// aviation pair - nautical miles and magnetic - below.
        /// </summary>
        /// <param name="label">Left-column heading, or empty for no column at all.</param>
        /// <param name="suppressShortNauticalMiles">
        /// Drop the second line entirely under 1 NM. The ruler wants that; the
        /// box has a standing slot and would rather fill it.
        /// </param>
        string RangeAndBearing(string label, double metres, double bearing, bool suppressShortNauticalMiles)
        {
            // The ruler carries no label column; the box does, and its second
            // line has to line up under the first.
            var indent = label.Length > 0 ? ColumnWidth : 0;

            var text = label.PadRight(indent) + Col(DistanceText.Format(metres)) +
                       Angles.Wrap360(Math.Round(bearing)).ToString("000") +
                       "°T (" + Angles.Compass(bearing) + ")";

            var nauticalMiles = DistanceText.NauticalMiles(metres, suppressShortNauticalMiles);

            // No nautical miles, no second line - a lone magnetic bearing is
            // not worth the height.
            if (nauticalMiles.Length > 0)
            {
                text += "\n" + (new string(' ', indent) + Col(nauticalMiles) +
                                Magnetic(bearing)).TrimEnd();
            }

            return text;
        }

        /// <summary>
        /// Fixed-width column, so the figure beside it does not jitter left
        /// and right as its neighbour changes length under the mouse. One
        /// width serves the label and distance columns both.
        /// </summary>
        const int ColumnWidth = 9;

        static string Col(string text)
        {
            return text.PadRight(ColumnWidth);
        }

        /// <summary>
        /// The magnetic bearing, or nothing at all when the vehicle has not
        /// given us a declination to work from. The true bearing and the
        /// octant carry the line above regardless, so there is nothing to
        /// apologise for here.
        /// </summary>
        string Magnetic(double trueBearing)
        {
            var declination = Declination();

            if (declination == null)
                return "";

            return Angles.Wrap360(Math.Round(trueBearing - declination.Value)).ToString("000") + "°M";
        }

        double? _declination;
        DateTime _declination_stale = DateTime.MinValue;

        /// <summary>
        /// Declination in degrees, east positive, taken from the vehicle rather
        /// than a model of our own - Mission Planner does not carry one, and
        /// the vehicle's figure is the one its own heading is referenced to.
        /// </summary>
        double? Declination()
        {
            // The param list is a linear scan under a lock, which has no place
            // on the paint path. Declination only moves when the vehicle
            // travels a long way, so a lazy refresh is ample.
            if (DateTime.UtcNow >= _declination_stale)
            {
                _declination_stale = DateTime.UtcNow.AddSeconds(10);

                var param = _host.comPort?.MAV?.param?["COMPASS_DEC"];
                _declination = param == null
                    ? (double?)null
                    : (float)param * MathHelper.rad2deg;
            }

            return _declination;
        }

        srtm.altresponce Terrain(PointLatLngAlt pin)
        {
            if (_terrain != null)
                return _terrain;

            if (DateTime.UtcNow < _terrain_retry)
                return null;

            _terrain_retry = DateTime.UtcNow.AddSeconds(1);

            var alt = srtm.getAltitude(pin.Lat, pin.Lng);

            // Ocean tiles carry no samples but do carry a real answer.
            if (alt.currenttype == srtm.tiletype.valid || alt.currenttype == srtm.tiletype.ocean)
                _terrain = alt;

            return _terrain;
        }

        // --------------------------------------------------
        //                    The ruler
        // --------------------------------------------------

        void UpdateRuler(Point cursor)
        {
            if (_pin == null)
                return;

            PointLatLngAlt pin = _pin.Position;
            PointLatLngAlt target = _map.FromLocalToLatLng(cursor.X, cursor.Y);

            _ruler.Points.Clear();
            _ruler.Points.Add(pin);
            _ruler.Points.Add(target);
            _ruler.IsVisible = true;
            _map.UpdateRouteLocalPosition(_ruler);

            // Measured outwards from the pin, so the bearing answers "where is
            // that, from here" - the same sense as the box.
            _label.Text = RangeAndBearing("",
                pin.GetDistance(target),
                pin.GetBearing(target),
                suppressShortNauticalMiles: true);

            // Visible first: a hidden marker ignores position changes, so the
            // other order would leave the box a frame behind on the way back.
            _label.IsVisible = true;
            _label.Position = target;

            _map.Invalidate();
        }

        void HideRuler()
        {
            if (!_ruler.IsVisible && !_label.IsVisible)
                return;

            _ruler.IsVisible = false;
            _label.IsVisible = false;

            _map.Invalidate();
        }
    }
}
