using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using log4net;
using MissionPlanner;
using MissionPlanner.Controls;
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
    /// running range and bearing.
    ///
    /// The plugin constructs one of these and disposes it on unload.
    /// </summary>
    public class MapPinTool : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

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

        // Drives the two things that have to happen on a clock rather than on
        // an event: Shift can go down or up without the mouse moving and the
        // map has no keyboard focus to hear about it, and the TCPA filter needs
        // a fixed rate. Only runs while a pin is up.
        const double TickSeconds = 0.120;

        readonly System.Windows.Forms.Timer _tick =
            new System.Windows.Forms.Timer { Interval = (int)(TickSeconds * 1000) };

        GMapMarkerMapPin _pin;
        Point _pressed;
        Keys _pressed_modifiers;

        // Whether the pointer is on the map. Not the same as being inside its
        // client rectangle: the CAS alert panel and other plugin controls are
        // children of the map, so a cursor resting on one of those is still in
        // the rectangle but has left the map.
        bool _over_map;

        // Terrain under the pin, resolved off-thread - srtm reads the whole
        // .hgt file synchronously the first time a tile is touched.
        srtm.altresponce _terrain;
        bool _terrain_busy;
        int _terrain_tries;
        DateTime _terrain_next = DateTime.MinValue;

        // Magnetic declination from the vehicle, refreshed lazily: the param
        // list is a linear scan under a lock, and declination only moves when
        // the vehicle travels a long way.
        double? _declination;
        DateTime _declination_stale = DateTime.MinValue;

        // Latest honest TCPA, and how long the gates have been failing. A
        // valid answer shows at once; losing one rides through on the last
        // good figure for a while rather than blanking the line mid-glance.
        double? _tcpa;
        DateTime? _tcpa_fail_since;

        // Ground speed in m/s, low-passed. True ground speed moves slowly, so
        // smoothing it costs nothing; the geometry is left unfiltered so
        // turning onto the point tightens the estimate straight away.
        double? _groundspeed;

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
            _map.MouseEnter += OnMouseEnter;
            _map.MouseLeave += OnMouseLeave;
            _map.VisibleChanged += OnMapVisibleChanged;

            _tick.Tick += OnTick;
        }

        public void Dispose()
        {
            _map.MouseDown -= OnMouseDown;
            _map.MouseMove -= OnMouseMove;
            _map.MouseUp -= OnMouseUp;
            _map.MouseEnter -= OnMouseEnter;
            _map.MouseLeave -= OnMouseLeave;
            _map.VisibleChanged -= OnMapVisibleChanged;

            _tick.Stop();
            _tick.Tick -= OnTick;
            _tick.Dispose();

            _map.Overlays.Remove(_readout);
            _readout.Dispose();

            _map.Overlays.Remove(_overlay);
            _overlay.Dispose();
        }

        /// <summary>
        /// True when the ruler should be on screen: a pin exists, the pointer
        /// is on the map, Mission Planner has the keyboard, and Shift is down.
        /// ModifierKeys is machine-wide, so without the focus test a Shift-click
        /// in another application would drive the ruler on the map behind it.
        /// </summary>
        bool RulerWanted()
        {
            return _pin != null
                   && _over_map
                   && (Control.ModifierKeys & Keys.Shift) == Keys.Shift
                   && _map.FindForm()?.ContainsFocus == true;
        }

        void SyncTick()
        {
            // Mission Planner hides the whole FlightData view when another
            // screen is up, so there is nothing to project or repaint until it
            // comes back.
            var running = _pin != null && _map.Visible;

            if (!running)
            {
                // Re-seed on the way back in rather than answering out of
                // samples from before the operator went elsewhere.
                _groundspeed = null;
                ForgetTcpa();
            }

            _tick.Enabled = running;
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
                PlacePin(_map.FromLocalToLatLng(e.X, e.Y));

            _map.Invalidate();
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            // MouseEnter is missed when a control sitting on the map goes away
            // with the pointer already over it.
            _over_map = true;

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

        void OnMouseEnter(object sender, EventArgs e)
        {
            _over_map = true;
        }

        void OnMouseLeave(object sender, EventArgs e)
        {
            _over_map = false;
            HideRuler();
        }

        void OnMapVisibleChanged(object sender, EventArgs e)
        {
            SyncTick();
        }

        void OnTick(object sender, EventArgs e)
        {
            UpdateTerrain();
            UpdateGroundSpeed();
            UpdateTcpa();

            // The ruler is refreshed on the clock as well as on mouse moves:
            // holding the cursor on a spot to read its TCPA to a spotter is the
            // point of putting one there, and a readout that froze the moment
            // the mouse stopped would be worse than useless for that.
            if (RulerWanted())
                UpdateRulerAtCursor();
            else
                HideRuler();
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

        void PlacePin(PointLatLng position)
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
            _terrain_tries = 0;
            _terrain_next = DateTime.MinValue;

            // The geometry is entirely different now; nothing about the old
            // estimate carries over. The speed filter does, since it knows
            // nothing about the pin.
            ForgetTcpa();

            SyncTick();
        }

        void RemovePin()
        {
            if (_pin != null)
            {
                _overlay.Markers.Remove(_pin);
                _pin.Dispose();
                _pin = null;
            }

            ForgetTcpa();

            SyncTick();
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
        /// bearing and the grid octant. Below, the aviation pair - nautical
        /// miles and magnetic - which reads straight onto a radio without
        /// picking figures out of two rows.
        ///
        /// Unlike the ruler readout, the box keeps its nautical miles below
        /// 1 NM, since the slot is standing there either way. Columns are
        /// space-padded to line up under the monospaced box font.
        /// </remarks>
        string BuildPinText(GMapMarkerMapPin marker)
        {
            PointLatLngAlt pin = marker.Position;

            // Coordinates get read out loud and typed into other things, so
            // they stay in one format whatever locale the machine is set to.
            var text = pin.Lat.ToString("0.0000000", CultureInfo.InvariantCulture) + "  " +
                       pin.Lng.ToString("0.0000000", CultureInfo.InvariantCulture);

            if (_terrain != null)
            {
                text += "\n" + Col("Terrain") +
                        CurrentState.toAltDisplayUnit(_terrain.alt).ToString("0") + " " + CurrentState.AltUnit;
            }

            var aircraft = _host.cs.Location;
            if (aircraft.Lat == 0 && aircraft.Lng == 0)
                return text;

            text += "\n" + RangeAndBearing("A/C",
                        pin.GetDistance(aircraft),
                        pin.GetBearing(aircraft),
                        suppressShortNauticalMiles: false);

            if (_tcpa != null)
                text += "\n" + Col("TCPA") + Duration(_tcpa.Value);

            return text;
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
        /// width serves the label and distance columns both - the ruler's
        /// TCPA tag sits in the distance slot, so they have to agree anyway.
        /// </summary>
        const int ColumnWidth = 9;

        static string Col(string text)
        {
            return text.PadRight(ColumnWidth);
        }

        /// <summary>
        /// The magnetic bearing, or an empty string when the vehicle has not
        /// given us a declination to work from.
        /// </summary>
        string Magnetic(double trueBearing)
        {
            var declination = Declination();

            if (declination == null)
                return "";

            return Angles.Wrap360(Math.Round(trueBearing - declination.Value)).ToString("000") + "°M";
        }

        /// <summary>
        /// Declination in degrees, east positive, taken from the vehicle rather
        /// than a model of our own - Mission Planner does not carry one, and
        /// the vehicle's figure is the one its own heading is referenced to.
        /// </summary>
        double? Declination()
        {
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

        // --------------------------------------------------
        //                      The TCPA
        // --------------------------------------------------

        /// <summary>
        /// How near the aircraft has to pass for this to count as arriving.
        /// You can pick the aircraft up visually from two or three kilometres,
        /// so inside one it is effectively there.
        /// </summary>
        internal const double MissDistance = 1000;

        /// <summary>
        /// Beyond this there is a flight plan with checkpoints on it, and a
        /// straight-line extrapolation is a fiction anyway - over a quarter of
        /// an hour the aircraft will certainly turn.
        /// </summary>
        internal const double Horizon = 15 * 60;

        /// <summary>
        /// Below this the ground track is GPS noise rather than a direction,
        /// and the arithmetic divides by something near zero.
        /// </summary>
        internal const double MinGroundSpeed = 3;

        /// <summary>
        /// Below this the countdown has run out and the readout goes at once,
        /// skipping the hide delay - which is there to ride out a gust, not to
        /// leave a stale figure up after the aircraft has gone past.
        /// </summary>
        internal const double ArrivedSeconds = 5;

        const double GroundSpeedTau = 8;

        static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Seconds until the aircraft is nearest the pin, extrapolating the
        /// current ground track in a straight line - or null when that answer
        /// would not be honest.
        /// </summary>
        /// <remarks>
        /// Time to closest approach, not range over closing speed - closure
        /// decays to zero at the closest point, so that form overestimates
        /// whenever the track is off the point. The same trig hands back the
        /// miss distance that decides whether to answer at all.
        /// </remarks>
        internal static double? TcpaSeconds(double range, double bearingToPin, double groundTrack, double groundSpeed)
        {
            if (groundSpeed < MinGroundSpeed)
                return null;

            var off = Math.Abs(Angles.Wrap180(bearingToPin - groundTrack)) * MathHelper.deg2rad;
            var closing = Math.Cos(off);

            // Ninety degrees or worse: abeam or opening. Nothing to promise.
            if (closing <= 0)
                return null;

            var seconds = range * closing / groundSpeed;
            var miss = range * Math.Sin(off);

            if (miss > MissDistance || seconds > Horizon)
                return null;

            return seconds;
        }

        /// <summary>True once the countdown has effectively run out.</summary>
        static bool HasArrived(double? seconds)
        {
            return seconds != null && seconds.Value < ArrivedSeconds;
        }

        /// <summary>
        /// The ruler's TCPA line: how long until the aircraft is at whatever the
        /// cursor is over, which is the figure a spotter is waiting to hear.
        /// </summary>
        /// <remarks>
        /// Undebounced, unlike the box: the cursor is the operator's own hand,
        /// so the line coming and going as they sweep across the gates reads
        /// as an answer rather than as flicker.
        /// </remarks>
        string RulerTcpaLine(PointLatLngAlt target)
        {
            var seconds = TcpaTo(target);

            if (seconds == null || HasArrived(seconds))
                return "";

            // No label column here, so the tag takes the distance slot and the
            // figure lands under the bearings.
            return "\n" + Col("TCPA") + Duration(seconds.Value);
        }

        void UpdateTcpa()
        {
            var seconds = CurrentTcpa();
            var now = DateTime.UtcNow;

            if (HasArrived(seconds))
            {
                ForgetTcpa();
                return;
            }

            // A valid answer shows at once - the gates already did the
            // filtering, and a pin just dropped is a question just asked.
            // Only the disappearance is debounced.
            if (seconds != null)
            {
                _tcpa = seconds;
                _tcpa_fail_since = null;
            }
            else if (_tcpa != null)
            {
                _tcpa_fail_since = _tcpa_fail_since ?? now;

                if (now - _tcpa_fail_since >= HideDelay)
                    ForgetTcpa();
            }
        }

        double? CurrentTcpa()
        {
            if (_pin == null)
                return null;

            return TcpaTo(_pin.Position);
        }

        /// <summary>
        /// Raw TCPA from the aircraft to any point, ungated by arrival and
        /// undebounced.
        /// </summary>
        double? TcpaTo(PointLatLngAlt target)
        {
            PointLatLngAlt aircraft = _host.cs.Location;
            if (aircraft.Lat == 0 && aircraft.Lng == 0)
                return null;

            return TcpaSeconds(
                aircraft.GetDistance(target),
                aircraft.GetBearing(target),
                _host.cs.groundcourse,
                GroundSpeed());
        }

        /// <summary>
        /// Advances the ground-speed low-pass. Tick only - the readers run at
        /// whatever rate the mouse or the map feels like, and a filter driven
        /// from those would have a time constant to match.
        /// </summary>
        void UpdateGroundSpeed()
        {
            // cs.groundspeed comes back in the operator's display units; the
            // geometry here is metric throughout.
            var raw = CurrentState.fromSpeedDisplayUnit(_host.cs.groundspeed);

            // Seeded from the first sample rather than from zero, so a freshly
            // dropped pin does not spend the filter's settling time reporting
            // an aircraft that is barely moving.
            _groundspeed = _groundspeed == null
                ? raw
                : _groundspeed + (raw - _groundspeed) * (TickSeconds / GroundSpeedTau);
        }

        double GroundSpeed()
        {
            return _groundspeed ?? CurrentState.fromSpeedDisplayUnit(_host.cs.groundspeed);
        }

        void ForgetTcpa()
        {
            _tcpa = null;
            _tcpa_fail_since = null;
        }

        /// <summary>
        /// Quantised so the figure sits still: whole minutes above a minute,
        /// ten-second steps below. A readout ticking 4:32, 4:29, 4:33 is noise
        /// dressed up as precision.
        /// </summary>
        internal static string Duration(double seconds)
        {
            if (seconds >= 60)
                return Math.Round(seconds / 60.0).ToString("0") + " min";

            return Math.Max(10, (int)(seconds / 10) * 10) + " s";
        }

        // --------------------------------------------------
        //                   The terrain
        // --------------------------------------------------

        /// <summary>
        /// Ground under the pin is asked for once when the pin lands. srtm
        /// answers "invalid" while it queues the tile it needs, so an
        /// unresolved answer is worth a few more goes as the download arrives -
        /// a handful over half a minute, not a standing poll. If the data is
        /// not there at all, asking forever will not make it appear.
        /// </summary>
        const int TerrainTries = 10;

        static readonly TimeSpan TerrainRetry = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Starts a terrain lookup for the pin, if one is wanted, none is
        /// already running and the budget has not run out. The answer comes
        /// back on the UI thread.
        /// </summary>
        void UpdateTerrain()
        {
            if (_pin == null || _terrain != null || _terrain_busy)
                return;

            if (_terrain_tries >= TerrainTries || DateTime.UtcNow < _terrain_next)
                return;

            _terrain_tries++;
            _terrain_next = DateTime.UtcNow + TerrainRetry;
            _terrain_busy = true;

            var wanted = _pin.Position;

            Task.Run(() =>
            {
                srtm.altresponce alt = null;

                try
                {
                    alt = srtm.getAltitude(wanted.Lat, wanted.Lng);
                }
                catch (Exception ex)
                {
                    log.Error("terrain lookup failed", ex);
                }

                try
                {
                    _map.BeginInvokeIfRequired(() => TerrainAnswered(wanted, alt));
                }
                catch (Exception ex)
                {
                    // The map went away underneath us; nothing left to tell.
                    log.Error("terrain lookup could not report back", ex);
                }
            });
        }

        void TerrainAnswered(PointLatLng at, srtm.altresponce alt)
        {
            _terrain_busy = false;

            // The pin moved or went away while the tile was being read, so
            // this answer is for somewhere else.
            if (_pin == null || _pin.Position != at)
                return;

            // Ocean tiles carry no samples but do carry a real answer.
            if (alt == null ||
                (alt.currenttype != srtm.tiletype.valid && alt.currenttype != srtm.tiletype.ocean))
                return;

            _terrain = alt;

            // The box is built during the paint pass, so it needs a repaint to
            // pick up a line it did not have a moment ago.
            _map.Invalidate();
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
            // that, from here" - the same sense as the box. The TCPA is the one
            // figure here that is referenced to the aircraft instead, because
            // it is the only thing that could be moving.
            _label.Text = RangeAndBearing("",
                              pin.GetDistance(target),
                              pin.GetBearing(target),
                              suppressShortNauticalMiles: true)
                          + RulerTcpaLine(target);

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
