using System;

namespace Carbonix.Weather
{
    /// <summary>
    /// Represents a point-in-time copy of a station's latest values.
    /// </summary>
    public class WeatherSnapshot
    {
        public RapidWind LatestWind;
        public SmoothedWind Wind;
        public StationObservation LatestObservation;
        public DeviceStatus LatestDeviceStatus;
        public HubStatus LatestHubStatus;
        public DateTime LastWindReceivedUtc;
        public DateTime LastObservationReceivedUtc;
        public DateTime LastReceivedUtc;
        public bool WindStale;
        public bool ObservationStale;
    }

    /// <summary>
    /// Represents the low-pass filtered wind, as shown on the GND bug.
    /// </summary>
    public struct SmoothedWind
    {
        public double SpeedMs;

        /// <summary>Direction the wind is coming from, degrees true.</summary>
        public double DirectionDeg;
    }

    /// <summary>
    /// Holds the latest values from one weather station.
    /// </summary>
    public class WeatherStation
    {
        readonly object _lock = new object();

        /// <summary>
        /// Age of the last rapid_wind sample after which the wind is
        /// reported stale.
        /// </summary>
        public TimeSpan WindStaleAfter = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Age of the last obs_st after which the observation is reported
        /// stale. The station sends one a minute.
        /// </summary>
        public TimeSpan ObservationStaleAfter = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Time constant of the first-order low-pass on the wind samples.
        /// </summary>
        public TimeSpan WindSmoothing = TimeSpan.FromSeconds(60);

        RapidWind _latestWind;
        StationObservation _latestObservation;
        DeviceStatus _latestDeviceStatus;
        HubStatus _latestHubStatus;
        DateTime _lastWindReceivedUtc = DateTime.MinValue;
        DateTime _lastObservationReceivedUtc = DateTime.MinValue;
        DateTime _lastReceivedUtc = DateTime.MinValue;

        // Filter state: scalar speed, and the direction as a unit vector so
        // that readings either side of north average to north
        double _speedMs;
        double _dirEast;
        double _dirNorth;

        /// <summary>Decodes a datagram and applies each message it carries.</summary>
        public void Apply(string datagram, DateTime receivedUtc)
        {
            foreach (var msg in TempestParser.Parse(datagram))
                Apply(msg, receivedUtc);
        }

        /// <summary>Applies one message to the latest values and the wind filter.</summary>
        /// <param name="receivedUtc">Local receive time, which drives staleness and the filter step.</param>
        public void Apply(TempestMessage msg, DateTime receivedUtc)
        {
            lock (_lock)
            {
                switch (msg)
                {
                    case RapidWind w:
                        // A failed wind sensor sends nulls: count the packet
                        // as heard but let the wind go stale
                        if (!double.IsNaN(w.SpeedMs) && !double.IsNaN(w.DirectionDeg))
                        {
                            Filter(w, receivedUtc);
                            _latestWind = w;
                            _lastWindReceivedUtc = receivedUtc;
                        }
                        break;
                    case StationObservation o:
                        _latestObservation = o;
                        _lastObservationReceivedUtc = receivedUtc;
                        break;
                    case DeviceStatus d:
                        _latestDeviceStatus = d;
                        break;
                    case HubStatus h:
                        _latestHubStatus = h;
                        break;
                    default:
                        return;
                }
                _lastReceivedUtc = receivedUtc;
            }
        }

        /// <summary>
        /// First-order low-pass with the step computed from the time since
        /// the previous sample.
        /// </summary>
        void Filter(RapidWind w, DateTime receivedUtc)
        {
            var rad = w.DirectionDeg * Math.PI / 180.0;
            var east = Math.Sin(rad);
            var north = Math.Cos(rad);

            double alpha = 1.0;
            if (_latestWind != null)
            {
                var dt = (receivedUtc - _lastWindReceivedUtc).TotalSeconds;
                if (dt <= 0)
                    alpha = 0.0; // clock stepped back: hold rather than reset
                else if (WindSmoothing.TotalSeconds > 0)
                    alpha = 1.0 - Math.Exp(-dt / WindSmoothing.TotalSeconds);
            }

            _speedMs += alpha * (w.SpeedMs - _speedMs);
            _dirEast += alpha * (east - _dirEast);
            _dirNorth += alpha * (north - _dirNorth);
        }

        /// <summary>Returns a point-in-time copy of the latest values.</summary>
        /// <param name="nowUtc">Time the wind staleness is judged against.</param>
        public WeatherSnapshot Snapshot(DateTime nowUtc)
        {
            lock (_lock)
            {
                var deg = Math.Atan2(_dirEast, _dirNorth) * 180.0 / Math.PI;
                return new WeatherSnapshot
                {
                    LatestWind = _latestWind,
                    Wind = new SmoothedWind
                    {
                        SpeedMs = _speedMs,
                        DirectionDeg = (deg + 360.0) % 360.0,
                    },
                    LatestObservation = _latestObservation,
                    LatestDeviceStatus = _latestDeviceStatus,
                    LatestHubStatus = _latestHubStatus,
                    LastWindReceivedUtc = _lastWindReceivedUtc,
                    LastObservationReceivedUtc = _lastObservationReceivedUtc,
                    LastReceivedUtc = _lastReceivedUtc,
                    WindStale = _latestWind == null || nowUtc - _lastWindReceivedUtc > WindStaleAfter,
                    ObservationStale = _latestObservation == null || nowUtc - _lastObservationReceivedUtc > ObservationStaleAfter,
                };
            }
        }
    }
}
