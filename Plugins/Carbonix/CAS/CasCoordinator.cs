using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Carbonix.Warnings;
using log4net;
using System.Reflection;
using MissionPlanner.Controls;

namespace Carbonix.CAS
{
    /// <summary>
    /// Coordinates the Crew Alerting System (CAS) components.
    /// </summary>
    /// <remarks>
    /// Wires together <see cref="AlertManager"/>, master lights, the alert panel,
    /// STATUSTEXT ingestion, warning-engine events, and speech output.
    /// The owning plugin should call <see cref="Tick"/> each loop iteration and
    /// <see cref="Dispose"/> on shutdown.
    /// </remarks>
    public class CasCoordinator : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly AlertManager _alertManager;
        readonly AlertPanelControl _alertPanel;
        readonly MasterLightRenderer _warnLight;
        readonly MasterLightRenderer _cautLight;
        readonly CarbonixWarningEngine _warningEngine;
        readonly HUD _hud;
        readonly PrearmTracker _prearmTracker;

        int? _statusTextSub;
        MissionPlanner.MAVLinkInterface _subscribedPort;
        bool _hadUnclearedAlerts;

        public CasCoordinator(
            CarbonixWarningEngine warningEngine,
            SpeechWarningConsumer speechConsumer,
            HUD hud,
            Control mapControl,
            MissionPlanner.MAVLinkInterface comPort,
            Action<string> onMessageHigh)
        {
            _warningEngine = warningEngine;
            _hud = hud;

            _alertManager = new AlertManager();

            // Master lights on the HUD
            _warnLight = new MasterLightRenderer(_alertManager, AlertTier.Warning);
            _cautLight = new MasterLightRenderer(_alertManager, AlertTier.Caution);
            hud.CustomEkfRenderer = _warnLight;
            hud.CustomVibeRenderer = _cautLight;

            // Alert panel overlay on the map
            _alertPanel = new AlertPanelControl(_alertManager);
            _alertPanel.Location = new Point(0, 65);
            mapControl.Controls.Add(_alertPanel);

            // Master light clicks -> open panel
            _warnLight.Clicked += () => mapControl.BeginInvokeIfRequired(() => _alertPanel.Open());
            _cautLight.Clicked += () => mapControl.BeginInvokeIfRequired(() => _alertPanel.Open());

            // Warning engine conditions -> alert manager
            _warningEngine.WarningStateChanged += OnWarningStateChanged;

            // New alerts -> speech
            if (speechConsumer != null)
                _alertManager.NewAlertFired += speechConsumer.OnNewAlert;

            // New alerts -> messageHigh (bottom bar)
            if (onMessageHigh != null)
                _alertManager.NewAlertFired += entry => onMessageHigh(entry.Message);

            // New alerts -> auto-open panel when transitioning from clean state
            _alertManager.NewAlertFired += entry =>
            {
                if (!_hadUnclearedAlerts)
                    mapControl.BeginInvokeIfRequired(() => _alertPanel.Open());
            };

            // Track uncleared alert state for auto-open logic
            _alertManager.AlertsChanged += () =>
            {
                _hadUnclearedAlerts = _alertManager.HasUnclearedAlerts();
            };

            // Prearm tracking
            _prearmTracker = new PrearmTracker(_alertManager, () =>
            {
                var port = _subscribedPort;
                if (port == null) return;
                port.doCommand(
                    (byte)port.sysidcurrent, (byte)port.compidcurrent,
                    MAVLink.MAV_CMD.RUN_PREARM_CHECKS,
                    0, 0, 0, 0, 0, 0, 0,
                    false);
            });

            // STATUSTEXT ingestion
            SubscribeStatusText(comPort);
        }

        /// <summary>
        /// Performs periodic housekeeping for the alerting system.
        /// </summary>
        /// <remarks>
        /// Re-subscribes to STATUSTEXT if the connection has changed and
        /// sweeps auto-resolve deadlines.
        /// </remarks>
        /// <param name="comPort">The current MAVLink interface.</param>
        public void Tick(MissionPlanner.MAVLinkInterface comPort)
        {
            if (comPort != _subscribedPort)
                SubscribeStatusText(comPort);

            _alertManager.SweepAutoResolve();

            var cs = comPort?.MAV?.cs;
            if (cs != null)
                _prearmTracker.Tick(cs.prearmstatus, cs.armed);
        }

        public void Dispose()
        {
            _hud.CustomEkfRenderer = null;
            _hud.CustomVibeRenderer = null;

            _warningEngine.WarningStateChanged -= OnWarningStateChanged;

            if (_statusTextSub.HasValue)
                _subscribedPort?.UnSubscribeToPacketType(_statusTextSub.Value);

            _alertPanel?.Dispose();
        }

        void OnWarningStateChanged(object sender, WarningStateChangedEventArgs e)
        {
            var tier = e.Rule.Severity == WarningSeverity.Warning
                ? AlertTier.Warning
                : AlertTier.Caution;

            if (e.IsActive)
                _alertManager.Fire(tier, e.Rule.Text);
            else
                _alertManager.Resolve(e.Rule.Text);
        }

        void SubscribeStatusText(MissionPlanner.MAVLinkInterface port)
        {
            if (port == null) return;

            if (_statusTextSub.HasValue)
                _subscribedPort?.UnSubscribeToPacketType(_statusTextSub.Value);

            _subscribedPort = port;

            // Subscribe with 0,0 (all sysid/compid) and filter in the
            // handler.  Avoids the race where sysidcurrent isn't set yet
            // at plugin load time.
            _statusTextSub = _subscribedPort.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.STATUSTEXT,
                OnStatusText,
                0, 0);
        }

        bool OnStatusText(MAVLink.MAVLinkMessage message)
        {
            // Filter to the currently selected vehicle
            if (_subscribedPort == null ||
                message.sysid != _subscribedPort.sysidcurrent ||
                message.compid != _subscribedPort.compidcurrent)
                return true;

            var msg = (MAVLink.mavlink_statustext_t)message.data;
            var severity = (MAVLink.MAV_SEVERITY)msg.severity;

            AlertTier tier;
            if (severity <= MAVLink.MAV_SEVERITY.ERROR)
                tier = AlertTier.Warning;
            else if (severity <= MAVLink.MAV_SEVERITY.WARNING)
                tier = AlertTier.Caution;
            else
                return true;

            var text = Encoding.UTF8.GetString(msg.text);
            int idx = text.IndexOf('\0');
            if (idx >= 0)
                text = text.Substring(0, idx);

            // Determine with alert tracker should handle this message
            if (text.StartsWith("PreArm:", StringComparison.OrdinalIgnoreCase))
            {
                _prearmTracker.OnPrearmMessage(text);
                return true;
            }

            _alertManager.Fire(tier, text, TimeSpan.FromSeconds(5));
            return true;
        }
    }
}
