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
    /// Provides a click-to-drop pin on the flight map with a live range and
    /// bearing readout, and a Shift-held ruler from the pin to the cursor.
    /// </summary>
    /// <remarks>
    /// A left click drops the pin, a click elsewhere moves it and a click on
    /// the pin removes it. Create one per flight map and dispose it when the
    /// plugin unloads.
    /// </remarks>
    public class MapPinTool : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Maximum pointer travel, in pixels, between press and release for
        /// the release to count as a click rather than a pan.
        /// </summary>
        const int ClickSlop = 3;

        readonly PluginHost _host;
        readonly GMapControl _map;

        // An overlay draws its routes, then markers, then tooltips, so nothing
        // in it can paint above its own tooltips. The cursor readout gets an
        // overlay of its own, stacked above, so it stays on top of the pin's
        // info box at close range; the rubber band stays below the box.
        readonly GMapOverlay _overlay = new GMapOverlay("cbx map pin");
        readonly GMapOverlay _readout = new GMapOverlay("cbx map pin readout");

        readonly GMapRoute _ruler = new GMapRoute("cbx ruler");
        readonly GMapMarkerRulerLabel _label = new GMapMarkerRulerLabel(PointLatLng.Empty);

        // Shift can change state without a mouse event, and the map has no
        // keyboard focus to hear about it, so the ruler and the TCPA filter
        // run on a clock. Only runs while a pin is up.
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

        // Terrain under the pin, resolved on a worker thread: srtm reads a
        // whole .hgt file synchronously the first time a tile is touched.
        srtm.altresponce _terrain;
        bool _terrain_busy;
        int _terrain_tries;
        DateTime _terrain_next = DateTime.MinValue;

        // Magnetic declination at the pin from the World Magnetic Model,
        // worked out once when the pin lands. NaN at the poles.
        double _declination = double.NaN;

        // Logged once per session rather than on every pin.
        static bool _wmm_expiry_logged;

        // Displayed TCPA, and when its gates started failing. A lost answer
        // rides on the last good figure for a while before the line goes.
        double? _tcpa;
        DateTime? _tcpa_fail_since;

        // Ground speed in m/s, low-passed. The geometry is deliberately not
        // filtered, so turning onto the point tightens the estimate at once.
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
        /// Determines whether the ruler should be on screen.
        /// </summary>
        bool RulerWanted()
        {
            // ModifierKeys is machine-wide: without the focus test, a
            // Shift-click in another application drives the ruler on the map
            // behind it.
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
                // Re-seed on return rather than carry samples from before the
                // view was hidden.
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

            // Refreshed on the clock as well as on mouse moves, so a cursor
            // held still on a spot keeps a live TCPA.
            if (RulerWanted())
                UpdateRulerAtCursor();
            else
                HideRuler();
        }

        /// <summary>
        /// Refreshes the ruler at the current pointer position without waiting
        /// for a mouse event.
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

            // Height moves the declination by hundredths of a degree, so the
            // terrain answer is not worth waiting for.
            var now = DateTime.UtcNow;
            _declination = Wmm.Declination(position.Lat, position.Lng, 0, now);

            if (Wmm.DecimalYear(now) >= Wmm.ValidUntil && !_wmm_expiry_logged)
            {
                _wmm_expiry_logged = true;
                log.Warn("WMM is past its validity; magnetic bearings are extrapolated");
            }

            // The old estimate was for a different point. The speed filter
            // carries over, since it does not depend on the pin.
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
        /// Builds the pin's info-box text, with range and bearing measured from
        /// the pin toward the aircraft.
        /// </summary>
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
        /// Formats the range-and-bearing block shared by the pin box and the
        /// ruler.
        /// </summary>
        /// <param name="label">Left-column heading, or empty for no column.</param>
        /// <param name="suppressShortNauticalMiles">
        /// Omit the nautical-mile line under 1 NM.
        /// </param>
        string RangeAndBearing(string label, double metres, double bearing, bool suppressShortNauticalMiles)
        {
            // With a label column, the second line lines up under the first.
            var indent = label.Length > 0 ? ColumnWidth : 0;

            var text = label.PadRight(indent) + Col(DistanceText.Format(metres)) +
                       Angles.Wrap360(Math.Round(bearing)).ToString("000") +
                       "°T (" + Angles.Compass(bearing) + ")";

            var nauticalMiles = DistanceText.NauticalMiles(metres, suppressShortNauticalMiles);

            // No nautical miles, no second line.
            if (nauticalMiles.Length > 0)
            {
                text += "\n" + (new string(' ', indent) + Col(nauticalMiles) +
                                Magnetic(bearing)).TrimEnd();
            }

            return text;
        }

        // One width serves the label and distance columns: the ruler's TCPA tag
        // sits in the distance slot, so the two have to agree. Fixed so the
        // figure beside it does not jitter as its neighbor changes length.
        const int ColumnWidth = 9;

        static string Col(string text)
        {
            return text.PadRight(ColumnWidth);
        }

        /// <summary>
        /// Formats the magnetic bearing, or an empty string at the poles where
        /// declination is undefined.
        /// </summary>
        string Magnetic(double trueBearing)
        {
            if (double.IsNaN(_declination))
                return "";

            return Angles.Wrap360(Math.Round(trueBearing - _declination)).ToString("000") + "°M";
        }

        // --------------------------------------------------
        //                      The TCPA
        // --------------------------------------------------

        /// <summary>
        /// Closest approach, in meters, inside which the aircraft counts as
        /// arriving at the point.
        /// </summary>
        internal const double MissDistance = 1000; // visual pickup range is 2-3 km

        /// <summary>
        /// Furthest ahead, in seconds, that a straight-line TCPA is reported.
        /// </summary>
        internal const double Horizon = 15 * 60; // the aircraft will have turned by then

        /// <summary>
        /// Ground speed, in m/s, below which the ground track is treated as
        /// undefined.
        /// </summary>
        internal const double MinGroundSpeed = 3;

        /// <summary>
        /// TCPA, in seconds, below which the countdown is treated as complete
        /// and the readout removed at once.
        /// </summary>
        internal const double ArrivedSeconds = 5;

        // Low-pass time constant for ground speed, seconds.
        const double GroundSpeedTau = 8;

        // How long a lost TCPA stays on the pin box before the line is dropped.
        static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Calculates seconds to closest approach from range in meters, true
        /// bearing to the point and ground track in degrees, and ground speed
        /// in m/s, or null when the aircraft is not closing, would miss by more
        /// than <see cref="MissDistance"/>, or is beyond <see cref="Horizon"/>.
        /// </summary>
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

        /// <summary>Determines whether the countdown has effectively run out.</summary>
        static bool HasArrived(double? seconds)
        {
            return seconds != null && seconds.Value < ArrivedSeconds;
        }

        /// <summary>
        /// Formats the ruler's TCPA line for the point under the cursor, or an
        /// empty string when there is nothing to report.
        /// </summary>
        string RulerTcpaLine(PointLatLngAlt target)
        {
            // Deliberately undebounced, unlike the box: the cursor is the
            // operator's own hand, so the line coming and going as they sweep
            // across the gates reads as an answer rather than flicker.
            var seconds = TcpaTo(target);

            if (seconds == null || HasArrived(seconds))
                return "";

            // No label column here, so the tag takes the distance slot.
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

            // A valid answer shows at once; only the disappearance is
            // debounced.
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
        /// Calculates the raw TCPA, in seconds, from the aircraft to a point,
        /// with no arrival gate or debounce.
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
        /// Advances the ground-speed low-pass filter by one tick.
        /// </summary>
        void UpdateGroundSpeed()
        {
            // Tick only: the time constant assumes a fixed call rate.

            // cs.groundspeed comes back in the operator's display units; the
            // geometry here is metric throughout.
            var raw = CurrentState.fromSpeedDisplayUnit(_host.cs.groundspeed);

            // Seeded from the first sample so a fresh pin does not wait out the
            // filter's settling time.
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
        /// Formats a TCPA for display, quantized to whole minutes above a
        /// minute and ten-second steps below.
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

        // srtm answers "invalid" while the tile it needs is queued for
        // download, so an unresolved lookup is retried a few times over about
        // half a minute.
        const int TerrainTries = 10;

        static readonly TimeSpan TerrainRetry = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Starts a terrain lookup for the pin when none is running and the
        /// retry budget remains; the answer arrives on the UI thread.
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

            // The box is built during paint, so it needs a repaint to pick up
            // the new line.
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

            // Range and bearing are from the pin to the cursor, the same sense
            // as the box. The TCPA alone is referenced to the aircraft.
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
