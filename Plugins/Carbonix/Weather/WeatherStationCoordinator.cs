using log4net;
using MissionPlanner;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Carbonix.Weather
{
    /// <summary>
    /// Owns the ground weather station feature: the UDP listener, the AIR
    /// and GND wind bugs on the flight map, and the readings window the GND
    /// bug opens.
    /// </summary>
    public class WeatherStationCoordinator : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const double KnotsPerMs = 1.94384449;

        readonly PluginHost _host;
        readonly WeatherStation _station;
        readonly TempestListener _listener;
        readonly WindBarb _airBug;
        readonly WindBarb _groundBug;
        readonly Timer _timer;
        readonly ToolTip _tip;
        WeatherStationForm _form;

        public WeatherStation Station => _station;

        public WeatherStationCoordinator(PluginHost host, int port)
        {
            _host = host;
            _station = new WeatherStation();
            _listener = new TempestListener(_station, port);

            var flightData = host.MainForm.FlightData;

            // The embedded flight planner re-shows every control on the map
            // panel when it closes, so the stock arrow is collapsed rather
            // than hidden
            var stock = flightData.windDir1;
            stock.Size = Size.Empty;

            _airBug = new WindBarb
            {
                Name = "windBarbAir",
                Location = stock.Location,
                Caption = "AIR",
                Stale = true,
            };
            _groundBug = new WindBarb
            {
                Name = "windBarbGround",
                Location = new Point(_airBug.Right, _airBug.Top),
                Caption = "GND",
                Stale = true,
                Cursor = Cursors.Hand,
            };
            stock.Parent.Controls.Add(_airBug);
            stock.Parent.Controls.Add(_groundBug);
            _airBug.BringToFront();
            _groundBug.BringToFront();
            _groundBug.Click += (s, e) => ShowWindow();

            _tip = new ToolTip();
            _tip.SetToolTip(_airBug, "Wind estimated by the aircraft");
            _tip.SetToolTip(_groundBug, "Ground weather station, 1 minute smoothed - click for details");

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => RefreshBugs();
            _timer.Start();
            RefreshBugs();

            // Last, so a failure above cannot leave the port bound and the
            // thread running with nothing to dispose them
            _listener.Start();
        }

        void RefreshBugs()
        {
            if (_groundBug.IsDisposed || _airBug.IsDisposed)
                return;

            // cs.wind_vel is already in the GCS speed units
            var cs = _host.cs;
            var airMs = cs.wind_vel / CurrentState.multiplierspeed;
            _airBug.Stale = !(_host.comPort?.BaseStream?.IsOpen ?? false);
            _airBug.SpeedKnots = airMs * KnotsPerMs;
            _airBug.DirectionDeg = cs.wind_dir;
            _airBug.DisplaySpeed = cs.wind_vel;

            var snap = _station.Snapshot(DateTime.UtcNow);
            _groundBug.Stale = snap.WindStale;
            if (snap.LatestWind != null)
            {
                _groundBug.SpeedKnots = snap.Wind.SpeedMs * KnotsPerMs;
                _groundBug.DirectionDeg = snap.Wind.DirectionDeg;
                _groundBug.DisplaySpeed = snap.Wind.SpeedMs * CurrentState.multiplierspeed;
            }
        }

        void ShowWindow()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new WeatherStationForm(_station, _listener);
                ThemeManager.ApplyThemeTo(_form);
                // An owner keeps it in front of the main window
                _form.Show(_host.MainForm);
            }
            else
            {
                if (_form.WindowState == FormWindowState.Minimized)
                    _form.WindowState = FormWindowState.Normal;
                _form.Activate();
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _tip.Dispose();
            _listener.Dispose();
            if (_form != null && !_form.IsDisposed)
                _form.Close();
            foreach (var bug in new[] { _airBug, _groundBug })
            {
                if (!bug.IsDisposed)
                {
                    bug.Parent?.Controls.Remove(bug);
                    bug.Dispose();
                }
            }
        }
    }
}
