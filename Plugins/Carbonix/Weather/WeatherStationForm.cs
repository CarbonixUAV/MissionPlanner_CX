using MissionPlanner;
using MissionPlanner.Utilities;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Carbonix.Weather
{
    /// <summary>
    /// Shows live readings from the ground weather station.
    /// </summary>
    public class WeatherStationForm : Form
    {
        readonly WeatherStation _station;
        readonly TempestListener _listener;
        readonly Timer _timer;
        readonly Label _status;
        readonly ValueTile _wind, _gust, _lull, _windAvg;
        readonly ValueTile _temperature, _dewPoint, _humidity, _pressure;
        readonly ValueTile _rain, _battery, _signal, _dataAge;

        public WeatherStationForm(WeatherStation station, TempestListener listener)
        {
            _station = station;
            _listener = listener;

            Text = "Weather Station";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var tiles = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4),
            };
            _wind = Tile(tiles, "Wind");
            _gust = Tile(tiles, "Gust");
            _lull = Tile(tiles, "Lull");
            _windAvg = Tile(tiles, "Wind avg");
            _temperature = Tile(tiles, "Temperature");
            _dewPoint = Tile(tiles, "Dew point");
            _humidity = Tile(tiles, "Humidity");
            _pressure = Tile(tiles, "Pressure");
            _rain = Tile(tiles, "Rain");
            _battery = Tile(tiles, "Battery");
            _signal = Tile(tiles, "Signal");
            _dataAge = Tile(tiles, "Data age");

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Padding = new Padding(8, 4, 8, 4),
                AutoEllipsis = true,
            };

            Controls.Add(tiles);
            Controls.Add(_status);

            const int columns = 4;
            const int rows = 3;
            ClientSize = new Size(
                columns * (ValueTile.TileWidth + 6) + 8,
                rows * (ValueTile.TileHeight + 6) + 8 + _status.Height);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => RefreshNow();

            Load += (s, e) =>
            {
                RefreshNow();
                _timer.Start();
            };
            FormClosed += (s, e) =>
            {
                _timer.Stop();
                _timer.Dispose();
            };
        }

        static ValueTile Tile(Control parent, string caption)
        {
            var tile = new ValueTile(caption);
            parent.Controls.Add(tile);
            return tile;
        }

        static string SpeedUnit => string.IsNullOrEmpty(CurrentState.SpeedUnit) ? "m/s" : CurrentState.SpeedUnit;

        static double Speed(double ms) => ms * CurrentState.multiplierspeed;

        static string Wind(double ms, double deg)
        {
            if (double.IsNaN(ms) || double.IsNaN(deg))
                return "--";
            return Speed(ms).ToString("0.0") + " " + SpeedUnit + "  " + deg.ToString("000") + "°";
        }

        /// <summary>Formats a reading, "--" for one the sensor did not give.</summary>
        static string Num(double value, string format, string unit)
        {
            return double.IsNaN(value) ? "--" : value.ToString(format) + unit;
        }

        /// <summary>Reads the station and updates every tile and the status line.</summary>
        public void RefreshNow()
        {
            if (IsDisposed)
                return;

            var now = DateTime.UtcNow;
            var snap = _station.Snapshot(now);
            UpdateTiles(snap, now);
            UpdateStatus(snap);
        }

        void UpdateTiles(WeatherSnapshot snap, DateTime now)
        {
            var wind = snap.LatestWind;
            var obs = snap.LatestObservation;
            var dev = snap.LatestDeviceStatus;
            var hub = snap.LatestHubStatus;
            var unit = SpeedUnit;

            _wind.Set(wind == null ? "--" : Wind(snap.Wind.SpeedMs, snap.Wind.DirectionDeg),
                wind == null ? "" : "1 min smoothed, now " + Wind(wind.SpeedMs, wind.DirectionDeg),
                stale: snap.WindStale);

            if (obs == null)
            {
                foreach (var tile in new[] { _gust, _lull, _windAvg, _temperature, _dewPoint, _humidity, _pressure, _rain })
                    tile.Set("--");
            }
            else
            {
                var stale = snap.ObservationStale;
                var interval = "last " + Num(obs.ReportIntervalMin, "0", " min");
                _gust.Set(Num(Speed(obs.WindGustMs), "0.0", " " + unit), interval, stale);
                _lull.Set(Num(Speed(obs.WindLullMs), "0.0", " " + unit), interval, stale);
                _windAvg.Set(Wind(obs.WindAvgMs, obs.WindDirectionDeg), interval, stale);
                _temperature.Set(Num(obs.AirTemperatureC, "0.0", " °C"), "", stale);
                _dewPoint.Set(Num(obs.DewPointC, "0.0", " °C"), "", stale);
                _humidity.Set(Num(obs.RelativeHumidityPct, "0", " %"), "", stale);
                _pressure.Set(Num(obs.StationPressureHpa, "0.0", " hPa"), "at station, not QNH", stale);
                _rain.Set(Num(obs.RainMm, "0.00", " mm"), Precip(obs.PrecipType) + ", " + interval, stale);
            }

            var battery = dev?.VoltageV ?? obs?.BatteryV;
            _battery.Set(battery.HasValue ? Num(battery.Value, "0.00", " V") : "--",
                dev == null ? "" : dev.SensorsOk ? "sensors OK" : string.Join(", ", dev.SensorFaults));

            _signal.Set(dev == null && hub == null ? "--" :
                (dev?.RssiDbm.ToString() ?? "--") + " / " + (hub?.RssiDbm.ToString() ?? "--") + " dBm",
                "station / hub");

            _dataAge.Set(Age(snap.LastWindReceivedUtc, now) + " / " + Age(snap.LastObservationReceivedUtc, now),
                "wind / observation", stale: snap.WindStale);
        }

        void UpdateStatus(WeatherSnapshot snap)
        {
            string text;
            if (_listener.Error != null)
                text = "UDP port " + _listener.Port + " listener failed: " + _listener.Error;
            else if (snap.LastReceivedUtc == DateTime.MinValue)
                text = "Listening on UDP port " + _listener.Port + ", nothing heard yet. The hub must be on the same network as this computer.";
            else
            {
                var stn = snap.LatestObservation?.SerialNumber ?? snap.LatestWind?.SerialNumber ?? "?";
                var fw = snap.LatestDeviceStatus?.FirmwareRevision.ToString() ?? snap.LatestObservation?.FirmwareRevision.ToString();
                text = "Station " + stn + (fw == null ? "" : " fw " + fw);
                if (snap.LatestHubStatus != null)
                    text += "   Hub " + snap.LatestHubStatus.SerialNumber + " fw " + snap.LatestHubStatus.FirmwareRevision;
                text += "   UDP port " + _listener.Port;
            }
            if (_status.Text != text)
                _status.Text = text;
        }

        static string Precip(PrecipType type)
        {
            switch (type)
            {
                case PrecipType.Rain: return "rain";
                case PrecipType.Hail: return "hail";
                case PrecipType.RainAndHail: return "rain and hail";
                default: return "no precip";
            }
        }

        static string Age(DateTime utc, DateTime now)
        {
            if (utc == DateTime.MinValue)
                return "--";
            var age = now - utc;
            if (age.TotalSeconds < 90)
                return age.TotalSeconds.ToString("0") + " s";
            if (age.TotalMinutes < 90)
                return age.TotalMinutes.ToString("0") + " min";
            return age.TotalHours.ToString("0.0") + " h";
        }

        /// <summary>
        /// Represents one reading: a small caption, a large value, and an
        /// optional detail line under it.
        /// </summary>
        class ValueTile : Panel
        {
            public const int TileWidth = 200;
            public const int TileHeight = 64;

            readonly Label _value;
            readonly Label _detail;
            readonly Color _normal;
            readonly Color _dim;

            public ValueTile(string caption)
            {
                Width = TileWidth;
                Height = TileHeight;
                Margin = new Padding(3);
                Padding = new Padding(4, 2, 4, 2);
                BackColor = ThemeManager.ControlBGColor;
                _normal = ThemeManager.TextColor;
                _dim = Color.FromArgb(140, _normal);

                var cap = new Label
                {
                    Text = caption,
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 16,
                    Font = new Font(FontFamily.GenericSansSerif, 8),
                    ForeColor = _dim,
                };
                _value = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 24,
                    Font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold),
                    ForeColor = _normal,
                    Text = "--",
                };
                _detail = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 16,
                    Font = new Font(FontFamily.GenericSansSerif, 8),
                    ForeColor = _dim,
                };
                Controls.Add(_detail);
                Controls.Add(_value);
                Controls.Add(cap);
            }

            public void Set(string value, string detail = "", bool stale = false)
            {
                if (_value.Text != value)
                    _value.Text = value;
                if (_detail.Text != detail)
                    _detail.Text = detail;
                var color = stale ? _dim : _normal;
                if (_value.ForeColor != color)
                    _value.ForeColor = color;
            }
        }
    }
}
