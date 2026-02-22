using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bulb;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;

namespace Carbonix.CAS
{
    /// <summary>
    /// Overlay panel that displays the alert list on the map control.
    /// </summary>
    public class AlertPanelControl : UserControl
    {
        readonly AlertManager _alertManager;

        readonly Panel _headerPanel;
        readonly Label _titleLabel;
        readonly Button _closeButton;
        readonly Panel _listPanel;
        readonly LinkLabel _historyToggle;
        readonly Panel _historyPanel;
        readonly Timer _refreshTimer;

        // Currently highlighted alert for dismiss
        int? _selectedId;
        bool _historyOpen;

        // Avoid rebuilding the list while already rebuilding
        bool _rebuilding;

        // Tracks previous state so we only auto-hide on the falling edge
        bool _hadVisibleAlerts;

        static readonly Color WarnColor = Color.FromArgb(255, 51, 51);
        static readonly Color CautColor = Color.FromArgb(255, 170, 0);

        // Theme-derived colors, computed once at construction
        readonly Color _bgColor;
        readonly Color _headerBgColor;
        readonly Color _textColor;
        readonly Color _dimColor;
        readonly Color _selectColor;

        public AlertPanelControl(AlertManager alertManager)
        {
            _alertManager = alertManager;

            // Derive palette from ThemeManager
            _bgColor = ThemeManager.ControlBGColor;
            _headerBgColor = ThemeManager.BGColor;
            _textColor = ThemeManager.TextColor;
            _dimColor = BlendColors(_textColor, _bgColor, 0.4);
            _selectColor = BlendColors(Color.CornflowerBlue, _bgColor, 0.35);

            // Outer control
            DoubleBuffered = true;
            BorderStyle = BorderStyle.FixedSingle;
            Width = 380;
            Visible = false;
            BackColor = _bgColor;

            // Header
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = _headerBgColor,
                Padding = new Padding(6, 3, 4, 3),
            };

            _titleLabel = new Label
            {
                Text = "ALERTS",
                Font = new Font(FontFamily.GenericMonospace, 9f, FontStyle.Bold),
                ForeColor = _textColor,
                AutoSize = true,
                Dock = DockStyle.Left,
            };

            _closeButton = new Button
            {
                Text = "\u2715",
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 20),
                Dock = DockStyle.Right,
                ForeColor = _textColor,
                BackColor = _headerBgColor,
                Cursor = Cursors.Hand,
            };
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Click += (s, e) => Hide();

            _headerPanel.Controls.Add(_titleLabel);
            _headerPanel.Controls.Add(_closeButton);

            // History toggle
            _historyToggle = new LinkLabel
            {
                Text = "\u25B8 History",
                Dock = DockStyle.Bottom,
                Height = 22,
                Padding = new Padding(6, 3, 0, 3),
                Font = new Font(FontFamily.GenericMonospace, 8f),
                LinkColor = _dimColor,
                ActiveLinkColor = _textColor,
                Visible = false,
            };
            _historyToggle.LinkClicked += (s, e) => ToggleHistory();

            // History panel
            _historyPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Visible = false,
            };

            // Alert list area
            _listPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            // Build control tree (order matters for Dock)
            Controls.Add(_listPanel);
            Controls.Add(_historyToggle);
            Controls.Add(_historyPanel);
            Controls.Add(_headerPanel);

            _alertManager.AlertsChanged += OnAlertsChanged;

            // 1-second timer to keep elapsed times ticking
            _refreshTimer = new Timer { Interval = 1000 };
            _refreshTimer.Tick += (s, e) => RefreshTimes();

            // Keyboard handling
            KeyDown += OnKeyDown;

            VisibleChanged += (s, e) =>
            {
                if (Visible) _refreshTimer.Start();
                else _refreshTimer.Stop();
            };
        }

        void OnAlertsChanged()
        {
            this.BeginInvokeIfRequired(RebuildList);
        }

        void RebuildList()
        {
            if (_rebuilding) return;
            _rebuilding = true;

            try
            {
                RebuildVisibleAlerts();
                RebuildHistory();
                AutoSize();
                AutoShowHide();
            }
            finally
            {
                _rebuilding = false;
            }
        }

        void RefreshTimes()
        {
            var now = DateTime.UtcNow;
            UpdateTimesIn(_listPanel, now);
            if (_historyPanel.Visible)
                UpdateTimesIn(_historyPanel, now);
        }

        void UpdateTimesIn(Panel container, DateTime now)
        {
            foreach (Control row in container.Controls)
            {
                if (!(row.Tag is AlertEntry alert)) continue;
                var lbl = row.Controls["elapsed"];
                if (lbl == null) continue;

                var timeBase = alert.IsResolved && alert.ResolvedUtc.HasValue
                    ? alert.ResolvedUtc.Value
                    : alert.FiredUtc;
                lbl.Text = FormatElapsed(now - timeBase);
            }
        }

        void RebuildVisibleAlerts()
        {
            _listPanel.SuspendLayout();
            _listPanel.Controls.Clear();

            var alerts = _alertManager.GetUnclearedAlerts();
            int y = 0;

            foreach (var alert in alerts)
            {
                var row = CreateAlertRow(alert);
                row.Location = new Point(0, y);
                row.Width = _listPanel.ClientSize.Width;
                row.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                _listPanel.Controls.Add(row);
                y += row.Height;
            }

            _listPanel.ResumeLayout();
        }

        Panel CreateAlertRow(AlertEntry alert)
        {
            var row = new Panel
            {
                Height = 28,
                Cursor = GetCursorForState(alert.State),
                Tag = alert,
            };

            // Severity indicator (LedBulb)
            // On = active, Off = resolved, Blink = unacked
            var bulb = new LedBulb
            {
                Size = new Size(12, 12),
                Location = new Point(6, 8),
                Color = alert.Tier == AlertTier.Warning ? WarnColor : CautColor,
                On = alert.IsActive,
            };

            if (alert.IsUnacked)
                bulb.Blink(500);

            // Tier label
            var tierColor = alert.Tier == AlertTier.Warning ? WarnColor : CautColor;
            if (alert.IsResolved)
                tierColor = BlendColors(tierColor, _bgColor, 0.6);

            var tierLabel = new Label
            {
                Text = alert.Tier == AlertTier.Warning ? "W" : "C",
                Font = new Font(FontFamily.GenericMonospace, 8f, FontStyle.Bold),
                ForeColor = tierColor,
                AutoSize = true,
                Location = new Point(22, 6),
            };

            var msgText = alert.Message;
            if (alert.IsResolved) msgText += "  [RESOLVED]";
            // Elapsed time: since fired when active, since resolved when resolved
            var timeBase = alert.IsResolved && alert.ResolvedUtc.HasValue
                ? alert.ResolvedUtc.Value
                : alert.FiredUtc;
            var timeColor = alert.IsResolved ? _dimColor : _textColor;
            var timeLabel = new Label
            {
                Name = "elapsed",
                Text = FormatElapsed(DateTime.UtcNow - timeBase),
                Font = new Font(FontFamily.GenericMonospace, 7.5f),
                ForeColor = timeColor,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            timeLabel.Location = new Point(row.Width - timeLabel.PreferredWidth - 36, 7);

            // Message: truncate with ellipsis so the timer always fits
            var msgLabel = new Label
            {
                Text = msgText,
                Font = GetFontForState(alert.State),
                ForeColor = GetForeColorForState(alert.State),
                AutoEllipsis = true,
                Location = new Point(40, 6),
                Size = new Size(timeLabel.Location.X - 40 - 4, row.Height - 12),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            };

            row.Controls.Add(bulb);
            row.Controls.Add(tierLabel);
            row.Controls.Add(msgLabel);
            row.Controls.Add(timeLabel);

            // CLR button (only for resolved+acked, visible when selected)
            if (alert.State == AlertState.ResolvedAcked)
            {
                var clrBtn = new Button
                {
                    Text = "CLR",
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(32, 20),
                    Font = new Font(FontFamily.GenericMonospace, 7f),
                    ForeColor = _textColor,
                    BackColor = _bgColor,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Visible = _selectedId == alert.Id,
                    Cursor = Cursors.Hand,
                };
                clrBtn.Location = new Point(row.Width - 34, 4);
                clrBtn.FlatAppearance.BorderSize = 1;
                clrBtn.FlatAppearance.BorderColor = _dimColor;
                clrBtn.Click += (s, e) => DismissSelected();
                row.Controls.Add(clrBtn);
            }

            // Highlight if selected
            if (_selectedId == alert.Id)
            {
                row.BackColor = _selectColor;
                foreach (Control c in row.Controls)
                    if (c is Label lbl)
                        lbl.ForeColor = _textColor;
            }

            // Row click handler
            row.Click += (s, e) => OnAlertRowClick(alert);
            foreach (Control c in row.Controls)
            {
                if (c is Button) continue; // Button has its own handler
                c.Click += (s, e) => OnAlertRowClick(alert);
            }

            return row;
        }

        void OnAlertRowClick(AlertEntry alert)
        {
            switch (alert.State)
            {
                case AlertState.ActiveUnacked:
                    _alertManager.AckSingle(alert.Id);
                    _selectedId = null;
                    break;

                case AlertState.ResolvedUnacked:
                    // Ack + highlight in one click
                    _alertManager.AckSingle(alert.Id);
                    _selectedId = alert.Id;
                    break;

                case AlertState.ResolvedAcked:
                    // Toggle highlight
                    _selectedId = _selectedId == alert.Id ? (int?)null : alert.Id;
                    RebuildList();
                    break;

                // ActiveAcked: no action
            }
        }

        void DismissSelected()
        {
            if (_selectedId.HasValue)
            {
                _alertManager.Dismiss(_selectedId.Value);
                _selectedId = null;
            }
        }

        void RebuildHistory()
        {
            var history = _alertManager.GetHistoryAlerts();
            _historyToggle.Visible = history.Count > 0;

            _historyPanel.SuspendLayout();
            _historyPanel.Controls.Clear();

            if (_historyOpen && history.Count > 0)
            {
                _historyPanel.Visible = true;
                int y = 0;
                foreach (var alert in history)
                {
                    var row = CreateHistoryRow(alert);
                    row.Location = new Point(0, y);
                    row.Width = _historyPanel.ClientSize.Width;
                    row.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                    _historyPanel.Controls.Add(row);
                    y += row.Height;
                }
                _historyPanel.Height = y;
            }
            else
            {
                _historyPanel.Visible = false;
            }

            _historyPanel.ResumeLayout();
        }

        Panel CreateHistoryRow(AlertEntry alert)
        {
            var row = new Panel { Height = 24, Tag = alert };

            var tierLabel = new Label
            {
                Text = alert.Tier == AlertTier.Warning ? "W" : "C",
                Font = new Font(FontFamily.GenericMonospace, 7.5f),
                ForeColor = _dimColor,
                AutoSize = true,
                Location = new Point(22, 4),
            };

            var msgLabel = new Label
            {
                Text = alert.Message,
                Font = new Font(FontFamily.GenericSansSerif, 8f),
                ForeColor = _dimColor,
                AutoSize = true,
                Location = new Point(40, 4),
            };

            var timeLabel = new Label
            {
                Name = "elapsed",
                Text = FormatElapsed(DateTime.UtcNow - alert.FiredUtc),
                Font = new Font(FontFamily.GenericMonospace, 7f),
                ForeColor = _dimColor,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            timeLabel.Location = new Point(row.Width - timeLabel.PreferredWidth - 6, 5);

            row.Controls.Add(tierLabel);
            row.Controls.Add(msgLabel);
            row.Controls.Add(timeLabel);

            return row;
        }

        void ToggleHistory()
        {
            _historyOpen = !_historyOpen;
            _historyToggle.Text = _historyOpen ? "\u25BE History" : "\u25B8 History";
            RebuildList();
        }

        new void AutoSize()
        {
            int contentHeight = _headerPanel.Height;

            // Visible alert rows
            int listHeight = 0;
            foreach (Control c in _listPanel.Controls)
                listHeight += c.Height;
            contentHeight += Math.Min(listHeight, 300); // cap at 300px
            _listPanel.MaximumSize = new Size(0, 300);

            if (_historyToggle.Visible)
                contentHeight += _historyToggle.Height;
            if (_historyPanel.Visible)
                contentHeight += _historyPanel.Height;

            Height = contentHeight + 4; // border padding
        }

        void AutoShowHide()
        {
            bool hasVisible = _alertManager.HasUnclearedAlerts();

            // Only auto-hide on the falling edge (had alerts → no alerts)
            if (_hadVisibleAlerts && !hasVisible)
                Hide();

            _hadVisibleAlerts = hasVisible;
        }

        /// <summary>
        /// Shows the alert panel and brings it to the front.
        /// </summary>
        public void Open()
        {
            Visible = true;
            BringToFront();
            Focus();
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_selectedId.HasValue &&
                (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Enter))
            {
                e.Handled = true;
                DismissSelected();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                _selectedId = null;
                RebuildList();
            }
        }

        Color GetForeColorForState(AlertState state)
        {
            switch (state)
            {
                case AlertState.ActiveUnacked:
                case AlertState.ActiveAcked:
                    return _textColor;
                case AlertState.ResolvedUnacked:
                case AlertState.ResolvedAcked:
                default:
                    return _dimColor;
            }
        }

        static Cursor GetCursorForState(AlertState state)
        {
            switch (state)
            {
                case AlertState.ActiveUnacked:
                case AlertState.ResolvedUnacked:
                case AlertState.ResolvedAcked:
                    return Cursors.Hand;
                default:
                    return Cursors.Default;
            }
        }

        static Font GetFontForState(AlertState state)
        {
            switch (state)
            {
                case AlertState.ActiveUnacked:
                case AlertState.ResolvedUnacked:
                    return new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
                default:
                    return new Font(FontFamily.GenericSansSerif, 8.5f);
            }
        }

        static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 60)
                return $"{(int)elapsed.TotalSeconds}s";
            return $"{(int)elapsed.TotalMinutes}m";
        }

        static Color BlendColors(Color foreground, Color background, double foregroundWeight)
        {
            double bgWeight = 1.0 - foregroundWeight;
            return Color.FromArgb(
                (int)(foreground.R * foregroundWeight + background.R * bgWeight),
                (int)(foreground.G * foregroundWeight + background.G * bgWeight),
                (int)(foreground.B * foregroundWeight + background.B * bgWeight));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
                _alertManager.AlertsChanged -= OnAlertsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
