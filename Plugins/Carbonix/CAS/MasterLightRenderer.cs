using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Carbonix.Warnings;
using MissionPlanner.Controls;

namespace Carbonix.CAS
{
    /// <summary>
    /// Renders a WARN or CAUT master light in a HUD icon slot.
    /// </summary>
    public class MasterLightRenderer : IHudIconRenderer
    {
        readonly AlertManager _alertManager;
        readonly WarningSeverity _severity;
        readonly string _label;
        readonly Color _activeColor;
        readonly Bitmap _iconOn;
        readonly Bitmap _iconOff;

        public bool ShowHandCursor => true;

        /// <summary>Occurs when the operator clicks the master light.</summary>
        public event Action Clicked;

        public MasterLightRenderer(AlertManager alertManager, WarningSeverity severity)
        {
            _alertManager = alertManager;
            _severity = severity;

            if (severity == WarningSeverity.Warning)
            {
                _label = "WARN";
                _activeColor = Color.Red;
                _iconOn = Resources.warn_on;
                _iconOff = Resources.warn_off;
            }
            else
            {
                _label = "CAUT";
                _activeColor = Color.FromArgb(255, 170, 0); // amber
                _iconOn = Resources.caut_on;
                _iconOff = Resources.caut_off;
            }
        }

        internal static LightState DeriveLightState(IEnumerable<AlertEntry> visibleAlerts, WarningSeverity severity)
        {
            bool anyUnacked = visibleAlerts.Any(a => a.Severity == severity && !a.IsAcked);
            if (anyUnacked)
                return LightState.Flashing;

            bool anyActiveAcked = visibleAlerts.Any(
                a => a.Severity == severity && a.State == AlertState.Active && a.IsAcked);
            if (anyActiveAcked)
                return LightState.Steady;

            return LightState.Off;
        }

        public void Render(HUD hud, Rectangle hitZone, int fontsize, bool useIcons)
        {
            var state = DeriveLightState(_alertManager.GetUnclearedAlerts(), _severity);

            if (useIcons)
            {
                Bitmap icon;
                switch (state)
                {
                    case LightState.Flashing:
                        bool on = (Environment.TickCount / 500) % 2 == 0;
                        icon = on ? _iconOn : _iconOff;
                        break;
                    case LightState.Steady:
                        icon = _iconOn;
                        break;
                    default:
                        icon = _iconOff;
                        break;
                }
                // +2 matches the vertical fudge the HUD uses for its built-in icons
                hud.DrawImage(icon, hitZone.X, hitZone.Y + 2, hitZone.Width, hitZone.Height);
            }
            else
            {
                Color color;
                switch (state)
                {
                    case LightState.Flashing:
                        bool on = (Environment.TickCount / 500) % 2 == 0;
                        color = on ? _activeColor : hud.hudcolor;
                        break;
                    case LightState.Steady:
                        color = _activeColor;
                        break;
                    default:
                        color = hud.hudcolor;
                        break;
                }

                using (var brush = new SolidBrush(color))
                    hud.DrawString(_label, fontsize + 2, brush, hitZone.X, hitZone.Y);
            }
        }

        public void OnClick()
        {
            _alertManager.AckSeverity(_severity);
            Clicked?.Invoke();
        }
    }
}
