using System;
using System.Drawing;
using System.Windows.Forms;
using Carbonix.Warnings;
using MissionPlanner.Controls;

namespace Carbonix.CAS
{
    /// <summary>
    /// Coordinates the Crew Alerting System (CAS) UI components.
    /// </summary>
    /// <remarks>
    /// Wires together <see cref="AlertManager"/>, master lights, the alert panel,
    /// warning-engine events, and speech output. All MAVLink data ingestion is
    /// handled by <see cref="CarbonixWarningEngine"/>; this class is pure UI
    /// orchestration.
    /// The owning plugin should call <see cref="Tick"/> each loop iteration and
    /// <see cref="Dispose"/> on shutdown.
    /// </remarks>
    public class CasCoordinator : IDisposable
    {
        public AlertManager AlertManager => _alertManager;
        readonly AlertManager _alertManager;
        readonly AlertPanelControl _alertPanel;
        readonly MasterLightRenderer _warnLight;
        readonly MasterLightRenderer _cautLight;
        readonly CarbonixWarningEngine _warningEngine;
        readonly HUD _hud;
        bool _hadUnclearedAlerts;

        public CasCoordinator(
            CarbonixWarningEngine warningEngine,
            SpeechWarningConsumer speechConsumer,
            HUD hud,
            Control mapControl)
        {
            _warningEngine = warningEngine;
            _hud = hud;

            _alertManager = new AlertManager();

            // Master lights on the HUD
            _warnLight = new MasterLightRenderer(_alertManager, WarningSeverity.Warning);
            _cautLight = new MasterLightRenderer(_alertManager, WarningSeverity.Caution);
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
        }

        /// <summary>
        /// Performs periodic housekeeping for the alerting system.
        /// </summary>
        public void Tick()
        {
            _alertManager.SweepAutoResolve();
        }

        public void Dispose()
        {
            _hud.CustomEkfRenderer = null;
            _hud.CustomVibeRenderer = null;

            _warningEngine.WarningStateChanged -= OnWarningStateChanged;

            _alertPanel?.Dispose();
        }

        void OnWarningStateChanged(object sender, WarningStateChangedEventArgs e)
        {
            if (e.IsActive)
                _alertManager.Fire(e.Severity, e.Text, e.AutoResolveAfter);
            else
                _alertManager.Resolve(e.Text);
        }
    }
}
