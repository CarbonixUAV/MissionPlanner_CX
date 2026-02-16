using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews;
using MissionPlanner.Warnings;

namespace AnnunciatorExample
{
    /// <summary>
    /// Demonstrates replacing the VIBE icon slot with a caution/warning
    /// light using the IHudIconRenderer interface. Loosely inspired by the
    /// caution/warning light in aircraft annunciator panels.
    /// See: https://en.wikipedia.org/wiki/Annunciator_panel
    ///
    /// The state is sticky: once the icon goes yellow (caution) or red
    /// (warning) it latches at that level until the operator clicks and
    /// presses Acknowledge.
    /// </summary>
    public class Plugin : MissionPlanner.Plugin.Plugin
    {
        public override string Name => "Annunciator";
        public override string Version => "0.1";
        public override string Author => "Bob Long";

        AnnunciatorRenderer _renderer;
        int? _sub;
        MAVLinkInterface _subscribedPort;

        // Change this to true to enable the plugin.
        public override bool Init() => false;

        public override bool Loaded()
        {
            _renderer = new AnnunciatorRenderer();
            FlightData.myhud.CustomVibeRenderer = _renderer;

            SubscribeCurrentMav();

            loopratehz = 1;

            return true;
        }

        void SubscribeCurrentMav()
        {
            var port = MainV2.comPort;
            if (port == null) return;

            if (_sub.HasValue)
                _subscribedPort.UnSubscribeToPacketType(_sub.Value);

            _subscribedPort = port;

            // Subscribe with 0,0 (all sysid/compid) and filter in the
            // handler.  Avoids the race where sysidcurrent isn't set yet
            // at plugin load time.
            _sub = _subscribedPort.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.STATUSTEXT,
                OnStatusText,
                0, 0);
        }

        bool OnStatusText(MAVLink.MAVLinkMessage message)
        {
            // Filter to the currently selected vehicle
            var port = MainV2.comPort;
            if (port == null ||
                message.sysid != port.sysidcurrent ||
                message.compid != port.compidcurrent)
                return true;

            var msg = (MAVLink.mavlink_statustext_t)message.data;
            var severity = (MAVLink.MAV_SEVERITY)msg.severity;

            // Ignore NOTICE / INFO / DEBUG (numerically above WARNING)
            if (severity > MAVLink.MAV_SEVERITY.WARNING)
                return true;

            var text = Encoding.UTF8.GetString(msg.text);
            int idx = text.IndexOf('\0');
            if (idx >= 0)
                text = text.Substring(0, idx);

            _renderer?.OnStatusText(severity, text);
            return true;
        }

        public override bool Loop()
        {
            // Resubscribe on connection changes
            if (MainV2.comPort != _subscribedPort)
                SubscribeCurrentMav();

            var cs = MainV2.comPort?.MAV?.cs;
            if (cs != null)
                _renderer?.Update(cs);

            return true;
        }

        public override bool Exit()
        {
            FlightData.myhud.CustomVibeRenderer = null;
            if (_sub.HasValue)
                _subscribedPort?.UnSubscribeToPacketType(_sub.Value);
            _renderer = null;
            return true;
        }
    }

    public class AnnunciatorRenderer : IHudIconRenderer
    {
        public bool ShowHandCursor => true;

        enum CautionLevel { Green = 0, Yellow = 1, Red = 2 }

        const int MaxMessages = 50;

        // Latched level: worst seen since last acknowledge
        CautionLevel _latchedLevel = CautionLevel.Green;

        // Messages captured since last acknowledge
        readonly object _lock = new object();
        readonly Dictionary<string, (DateTime time, MAVLink.MAV_SEVERITY severity, string text)> _messages
            = new Dictionary<string, (DateTime, MAVLink.MAV_SEVERITY, string)>();

        // Icon bitmaps (384x128 RGBA PNGs, matching HUD icon style).
        // Bitmap requires its source stream to remain open, so the
        // MemoryStream is anchored alongside it in the returned tuple.
        static readonly (MemoryStream ms, Bitmap bmp) _iconGreen = DecodeIcon(IconData.Green);
        static readonly (MemoryStream ms, Bitmap bmp) _iconYellow = DecodeIcon(IconData.Yellow);
        static readonly (MemoryStream ms, Bitmap bmp) _iconRed = DecodeIcon(IconData.Red);

        static (MemoryStream, Bitmap) DecodeIcon(string base64)
        {
            var ms = new MemoryStream(Convert.FromBase64String(base64));
            return (ms, new Bitmap(ms));
        }

        static CautionLevel LevelForSeverity(MAVLink.MAV_SEVERITY severity)
            => severity <= MAVLink.MAV_SEVERITY.ERROR ? CautionLevel.Red
             : CautionLevel.Yellow;

        /// <summary>
        /// Records or updates a message and latches the icon to the appropriate caution level.
        /// </summary>
        public void OnStatusText(MAVLink.MAV_SEVERITY severity, string text, string key = null)
        {
            var level = LevelForSeverity(severity);

            lock (_lock)
            {
                _messages[key ?? text] = (DateTime.Now, severity, text);
                if (level > _latchedLevel)
                    _latchedLevel = level;

                if (_messages.Count > MaxMessages)
                {
                    string oldestKey = null;
                    var oldestTime = DateTime.MaxValue;
                    foreach (var kv in _messages)
                    {
                        if (kv.Value.time < oldestTime)
                        {
                            oldestTime = kv.Value.time;
                            oldestKey = kv.Key;
                        }
                    }
                    _messages.Remove(oldestKey);
                }
            }
        }

        public void Render(HUD hud, Rectangle hitZone, int fontsize, bool useIcons)
        {
            // _latchedLevel is read without _lock — enum-sized reads are
            // atomic on .NET, so the worst case is showing the previous
            // level for one frame.
            if (useIcons)
            {
                Bitmap icon;
                switch (_latchedLevel)
                {
                    case CautionLevel.Red:    icon = _iconRed.bmp;    break;
                    case CautionLevel.Yellow: icon = _iconYellow.bmp; break;
                    default:                  icon = _iconGreen.bmp;  break;
                }
                // + 2 matches the vertical fudge the HUD uses for its built-in icon rendering
                hud.DrawImage(icon, hitZone.X, hitZone.Y + 2, hitZone.Width, hitZone.Height);
            }
            else
            {
                Color color;
                string label;
                switch (_latchedLevel)
                {
                    case CautionLevel.Red:
                        color = Color.Red;
                        label = "WARN";
                        break;
                    case CautionLevel.Yellow:
                        color = Color.Orange;
                        label = "CAUT";
                        break;
                    default:
                        color = hud.hudcolor;
                        label = "OK";
                        break;
                }
                using (var brush = new SolidBrush(color))
                    hud.DrawString(label, fontsize + 2, brush, hitZone.X, hitZone.Y);
            }
        }

        Form _popup;

        public void OnClick()
        {
            // Close any existing popup before opening a new one
            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.Close();
                _popup = null;
            }

            var ackCutoff = DateTime.Now;
            var active = new List<(DateTime time, MAVLink.MAV_SEVERITY severity, string text)>();

            lock (_lock)
            {
                foreach (var kv in _messages)
                    active.Add(kv.Value);
            }

            // Most recent first — actively polled conditions stay near the top
            active.Sort((a, b) => b.time.CompareTo(a.time));

            var lines = new List<string>();
            foreach (var msg in active)
            {
                var age = ackCutoff - msg.time;
                string ts = age.TotalSeconds < 60 ? (int)age.TotalSeconds + "s"
                          : age.TotalMinutes < 60 ? (int)age.TotalMinutes + "m"
                          : (int)age.TotalHours + "h";
                lines.Add("[" + ts + "] " + SeverityPrefix(msg.severity) + msg.text);
            }

            if (lines.Count == 0)
                lines.Add("All systems nominal.");

            // Non-modal popup so the operator can still interact with the
            // main window.  Closes automatically on focus loss.
            var form = new Form
            {
                Text = "Caution/Warning Summary",
                TopMost = true,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(420, 260),
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = string.Join(Environment.NewLine, lines),
            };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(4),
            };

            var ackBtn = new Button { Text = "Acknowledge" };
            var closeBtn = new Button { Text = "Close" };

            ackBtn.Click += (s, e) => { Acknowledge(ackCutoff); form.Close(); };
            closeBtn.Click += (s, e) => form.Close();
            form.Deactivate += (s, e) => { if (!form.IsDisposed) form.Close(); };

            btnPanel.Controls.Add(closeBtn);
            btnPanel.Controls.Add(ackBtn);
            form.Controls.Add(textBox);
            form.Controls.Add(btnPanel);

            _popup = form;
            form.Show();
        }

        void Acknowledge(DateTime ackCutoff)
        {
            lock (_lock)
            {
                var staleKeys = new List<string>();
                foreach (var kv in _messages)
                {
                    if (kv.Value.time <= ackCutoff)
                        staleKeys.Add(kv.Key);
                }
                foreach (var k in staleKeys)
                    _messages.Remove(k);

                // Recompute from whatever arrived while the popup was open
                _latchedLevel = CautionLevel.Green;
                foreach (var kv in _messages)
                {
                    var lvl = LevelForSeverity(kv.Value.severity);
                    if (lvl > _latchedLevel)
                        _latchedLevel = lvl;
                }
            }
        }

        static string SeverityPrefix(MAVLink.MAV_SEVERITY severity)
        {
            switch (severity)
            {
                case MAVLink.MAV_SEVERITY.EMERGENCY:
                case MAVLink.MAV_SEVERITY.ALERT:
                case MAVLink.MAV_SEVERITY.CRITICAL:
                case MAVLink.MAV_SEVERITY.ERROR:
                    return "WARN: ";
                case MAVLink.MAV_SEVERITY.WARNING:
                    return "CAUT: ";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Polls sensor health and custom warnings, upserting any active conditions.
        /// </summary>
        public void Update(CurrentState cs)
        {
            UpdateSensorAlerts(cs);
            UpdateWarningAlerts();
        }

        void UpdateSensorAlerts(CurrentState cs)
        {
            var h = cs.sensors_health;
            var e = cs.sensors_enabled;
            var p = cs.sensors_present;

            // Flight-critical → ERROR (Red / WARN)
            CheckSensor(h.gyro, e.gyro, p.gyro, "gyro", Strings.BadGyroHealth, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.accelerometer, e.accelerometer, p.accelerometer, "accel", Strings.BadAccelHealth, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.compass, e.compass, p.compass, "compass", Strings.BadCompassHealth, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.barometer, e.barometer, p.barometer, "baro", Strings.BadBaroHealth, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.gps, e.gps, p.gps, "gps", Strings.BadGPSHealth, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.ahrs, e.ahrs, p.ahrs, "ahrs", Strings.BadAHRS, MAVLink.MAV_SEVERITY.ERROR);
            CheckSensor(h.battery, e.battery, p.battery, "battery", Strings.Bad_Battery, MAVLink.MAV_SEVERITY.ERROR);

            // Non-flight-critical → WARNING (Yellow / CAUT)
            CheckSensor(h.rc_receiver, e.rc_receiver, p.rc_receiver, "rc", Strings.NORCReceiver, MAVLink.MAV_SEVERITY.WARNING);
            CheckSensor(h.logging, e.logging, p.logging, "logging", Strings.BadLogging, MAVLink.MAV_SEVERITY.WARNING);
            CheckSensor(h.terrain, e.terrain, p.terrain, "terrain", Strings.BadorNoTerrainData, MAVLink.MAV_SEVERITY.WARNING);
            CheckSensor(h.geofence, e.geofence, p.geofence, "geofence", Strings.GeofenceBreach, MAVLink.MAV_SEVERITY.WARNING);
        }

        void CheckSensor(bool healthy, bool enabled, bool present,
            string key, string text, MAVLink.MAV_SEVERITY severity)
        {
            if (!healthy && enabled && present)
                OnStatusText(severity, text, key);
        }

        void UpdateWarningAlerts()
        {
            // No lock — we don't want to contend with the warning
            // engine or the UI.  Stale data is fine at 1 Hz; just
            // guard against concurrent mutation (captured ref +
            // indexed loop + catch on shrink).
            var warnings = WarningEngine.warnings;

            for (var i = 0; i < warnings.Count; i++)
            {
                CustomWarning w;
                try { w = warnings[i]; }
                catch (ArgumentOutOfRangeException) { break; }

                try
                {
                    // Skip QuickViewColoring warnings. They color a box, not flag an error condition
                    if (w.type != CustomWarning.WarningType.SpeakAndText)
                        continue;
                    // CustomWarning has no severity field. We have to
                    // assume one, and I have picked WARNING (Yellow / CAUT),
                    // but change as appropriate for your use case.
                    if (CheckWarningCond(w))
                        OnStatusText(MAVLink.MAV_SEVERITY.WARNING, w.SayText(), "w:" + w.Name);
                }
                catch (Exception ex)
                {
                    // Don't let a misconfigured warning break the poll loop
                    System.Diagnostics.Debug.WriteLine(
                        $"Annunciator: warning check failed for {w.Name}: {ex.Message}");
                }
            }
        }

        // Mirrors the warning engine's condition check with
        // userepeattime=false so the 1 Hz poll isn't throttled by the
        // speak/text repeat timer.
        static bool CheckWarningCond(CustomWarning w)
        {
            if (w.Child != null)
                return w.CheckValue(false) && CheckWarningCond(w.Child);
            return w.CheckValue(false);
        }

    }

    #region Base64IconData
    /// <summary>
    /// Provides base64-encoded icon images for the HUD.
    /// </summary>
    static class IconData
    {
        public const string Green =
            "iVBORw0KGgoAAAANSUhEUgAAAYAAAACACAYAAAACsL4LAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAAGXRFWHRTb2Z0d2FyZQB3d3cuaW5rc2NhcGUub3Jnm+48GgAAEeJJREFUeJzt3Xl0FNWeB/B7q/fsW5OFELInhIQAo0BAfLgx6iiKDiAPlQCKTzTIk01URN+qx3d86rgBKkLU54zHbcCniMqmJGSBgEICSSAhG2TrLJ1O73f+mGFORCDdt6q7SOr7+UuTvrd+ejr1rbq36l7KGCMXonuoWt0YnucmqimUuFMIIZGEUf2vPggAAFceyqyEkA7mZrWCQIqd8aYiNoM5f/2xCwJAUxhxDSM0nxAa7adSAQDAlyg7Syl7z7Gg88df/vj/AoA+RwVVUsRiQukdshQIAAA+xj53nercwjYwNyGECOd/jJM/AMBwR+9UpUbkn/83gZD/HfbByR8AQAHcdLbm/ciphBAi0D1UzQi9X+6aAADAPxgji+kmqhHU" +
            "jeF5hNBYuQsCAAB/odFqQ/hkwc2EPLlLAQAA/3JTOkWghKXKXQgAAPgXJSRVIJSEy10IAAD4XaSAN3wBABSIUb0w+KcAAGA4QgAAACgUAgAAQKEQAAAACoUAAABQKAQAAIBCIQAAABQKAQAAoFAIAAAAhUIAAAAoFAIAAEChEAAAAAqFAAAAUCgEAACAQiEAAAAUCgEAAKBQCAAAAIVCAAAAKBQCAABAoRAAAAAKpZa7AFAWraChM2KuCZ1svCo0OSgxYHRgfGCgKkAdrA3RnP9Mr73HYXXbXPWWxr7TvXWWw51He75t2ddtdphdctYOMNwgAMDngjRBqvyU+THzEmePnBCRG6FX6Tz63k0b8M9O5nRXdp/s2tHwdcvbNYXN9eYGm4/KJbfFz4x4c8rfx/O0/ePRFys3nXyvhaftq5NeSJudcNsonrYXOtPXaL7hmzvKrC6rW4r+YHhCAIDPROuNmg3j1yYvSJqbFKgOEPVdU1O1kBOWFZETlhWxJvux" +
            "rO9b9jU/e+T5moPt5b1S1XueQRMoxBqiA3jahmiCuf4770i4JeJ36YvSBSpQnvYD9buszrv33nccJ38YDAIAJEcFStbnrB69MuvRMWJP/Bejoip6U9x1I2+MmxG3vfGrhoeKfn+8zdrhlPo4/mLUR6rfmPzSBClO/owxsqJkXUVJ2yGzFLXB8IZJYJBUUlCC7uAtu6Y8M25Nji9O/gNRQums+FsTfpp1YMbdCbOifHksX9o27a2caL3RIEVfm2u2Vr9TU3hWir5g+EMAgGSmGicF/3DLzmkTI8b79WQcpYvUf3jt5snP5j6R6M/jSmFF1sMjb4q7bqQUfZW2H2pbXvLESSn6AmXAEBBIYkb0tNAvrv9Hnq+v+i9FRVX06XGrsvUqveqJQ8/WylGDt8aGZQQ8l7suR4q+Gi3NfbN2zy93up1Miv5AGXAHAKJNiMgJ/OS6wklynfwHWpn1yJi12SsS5K5jMGpBTd+fvnm8FP/P+l1W57y9i8qG8jwIyAMB" +
            "AKKEaoNVn/ym8OpQTYhO7loIIYRSSv4wfl3ObfEzI+Su5XJeuurPqTlhWaJrZIyRVWXrj/jiaSgY/hAAIErhtI05CUHxQXLXMZCKqujGvFcmROuNmsE/7X8z464LW5q2MF2Kvt6u2Va98eQWrvcOABAAwG1h6m+jb42fGS93HRcTrTcaXpv8YqbcdVwoVBus2pT3ygS1oBb9yGdZ++G2gpK1mPQFbggA4KJX6YXncp/IkruOy7kz4d8Sbor9TZjcdQz03rQ3s+MD4gLF9tPU39I3a/f8Q5j0BTEQAMDl6ZyVo6U4kfkSJZQ+l/tkhtx1nLc0PT/29vibRS/10O+yOufuyS9rtbY7pKgLlEv2pzZg6NEKGroo7d5kKfpijJFa8+nuJktzv8neZY/URWpj9NGG1JCkEEqo6GGSq6MmGq8ZMSXkh9biHinq5ZUWkqx/YeKzoh/5ZIyRx8ueqsCkL0gBAQBeW5qeHyf2zVUXc7EPTn186uXKN+qPmo5bLvx9" +
            "Zlia4ZH0B0ctSbs3RStoVbzHoZSSVWMLkn5oLT4ipl4xVFSgH0zfND5YE6QV29e7tYXVm09uxZu+IAkMAYHXFiTNETXx22Zrt96x+7cHFh94tPJiJ39CCKnqqu4vKFlzcsY3t+0/1Vsn6mr3+phrY4M0QdwhItafJ65PkuLt6OK20tZlxasw6QuSQQCAV0YGxGonRuZyn8wszn7nnD35JV83fWfy5PMlbYfMN3w7q6ipv6WP95gBaoN6QdKcEbztxZhqnBS8PPMh0U8jNfW39N21577DLubGpC9IBgEAXrk3eV6Miqq4x+YLStce9nY8vsHcbJ+7J79MzBMvt4+6OZq3La8gTZCqcPqmiVpBK+rvzOqyOe/ZuxiTviA5BAB4ZUb0tEjethWmnzu21nx4jqftwfby3k/O/Hcd77H/RcRdC6938l4ZMzowPlhMH4wxsvrQM0eL2kox6QuSQwCAVyZG5XIHwHMVz58Qc+w15RuqHW4H1yYnRl2UPjd8LNcm" +
            "LzzuS7lnxF0JsxLF9vNu7fs1b1a90yxBSQC/ggAAj2WGpRkitRF6nrYd9k7rjuadnWKO32RpsZd3VLTztp86YkqomON7anTQKN3LV/8ll4p8ivVge3nrsuKVokIT4HIQAOCxqyIncA9n7D93oFWK+csdjd9wPwKZFZohajjGU+9fs3Gc2MXxzlpbLXP2LqzApC/4EgIAPJYdNoZ70bfvzu7nvnIf6J9NO7n7yQxN9XkA3J9yT0KecZKoCWdGGJu3b1Fps+WsXaq6AC4GAQAeSw5M5F764afOY5LsUXusq6rf7ra7eNrGGGIl2XbxcrJCM8LF9kEJpenBqX6brwDlQgCAx0YYjFzDGowx8lPXMe7n+AdyMTdrtDRz9RWpC78i9izwxNrsx9Il2CMe4LIQAOCxCM4TaLu9w9pt7+W6ar+Yxr6mi749PJhwbahuqJxUU4OTQ5ek3hcjdx0wvCEAwGNh2lCuAOi29Ug6lt3ntHBtfagRNEKwOlC2JSG8tTqr" +
            "AHcB4FMIAPCYTtBxnTytbpuke9VanBbuu4lAdeCQ+c6nBCeFPJi6MFbuOmD4GjJ/DCA/raDh+r7YXDbJhn8IIaTP2c8dKAGqgCFzB0AIIWvGLk9X4TYAfAQBAB7TCBquE5HVZeV6e/dSnIx/TSCNSvxWjP6UGJQQvDRjEe4CwCcQAOAxF+O7kKdE2nNugNrAfRXf5+yT9G7EH1ZlFaThLgB8AQEAHrO6rVxDL3o139zBJftT8fdndpglvRvxh9GB8cEPZyyJk7sOGH4QAOAxK+dYvl7Ff8V+0f4E/v56HfwTyHJanb08Xcs5BAdwKQgA8Fi/o58vAASdpFuPBqj1XAHgZE63mD0F5DTSEBu4LPMB3AWApBAA4LF+ziGgKH2kpG/gxhhiuJZ0MDv6ZN1QxS1yYbffZy3DXQBICgEAHmuxnO3naReiCdZG640aqeqIC4jhWienpb+Vq36pvPDzy8cru6u7eNuPNMQGFmQ9NFLKmkDZEADgsTrzGe71fHIj" +
            "srkXkhtohD5KE6QO5AqTs/0tsgXArubdTesr/nL63eptdWL6eSzzd7gLAMkgAMBj1T21XGvwEELIhIjcEClqmBx1FfeSzo2WFu76xajtPd0zb/+io4QQsrH6veZuR4+Nt684Q0zAiqxl8dJVB0qGAACPneit5r4DmBk3wyhFDbeMvJF7b9/T5nq/B0Cvw+yYs3dhWY/d7CKEEIuz3/1fdZ+dEdPn8jEPpenUOtwFgGgIAPBYUVtJL+9TNJOjrjYGqA2iv2/XROdxB8n+1gPc4+88GGHs0YOrDx81Hf9F8Pzt2Gv1Yp5GitGPCHh8zCOjxFcISocAAI+ZbD3Oqh6+SUy9SqdenCZueeOMkDRDZkhaGE9bq8vm+vHcwR4xx/fWa1WbT35w+uPWC39e23vaur/1APfWloQQ8mjmg2l6lR5/vyAKvkDglYPtpdxbMq4eW5AhZgLzqXErkwXOJREqu0902d0Ov70DsL+16Ozj5U9VX+r3r594+7SY/qP1RsPq" +
            "7ALcBYAoCADwyq7mfR28bUcaYgP/OmFDCk/bG2Knh85NvHM077GL2kq56/ZWo6XZ/O9776+43GP/n5/5Z+cpc52oO5JlGUtSpRhWA+XClwe8sr3pq44Om4n7KZblYx7KXJH1sFfPsmeHZQZ8dO2WSWqq5v6+bjv1j2bett6wumzO+fuWlHdYTYO+NLel5sM6Mccy6qIMq8YuTxDTBygbAgC8YnPa2BcNXzbwtqeUkucnbBj/yqTn09TC4Eszz0+62/jtzC+mhmvDuN8mPtFT01XWXiHJpvSD+Y+qjSeL2kp7Pfns6yc2NZmd4t5OXpaxOHUobXIDVxZ8ccBrr1a+dYYRxj2erhbU9JGMBzKO31E8/Q/j1yWmhSTrB/4+Uh+uXpqeH7vnX3dctW3aW5OjdJH6S/Xlic/qtzeJae+NDpvJ4xN6j93s+rx+B3eYEkJIlC5S/0T2Y9xDY6Bski7SBcrwc1eVpbittDXPOClaTD/JQYkhT+aszH4yZ2W2w+1w" +
            "d9q7bCGaYI1BpZfse9nj6LW/VPm6qJOsL71U+XrdgpS5SZRQ7snxpemLUl849uoZs8M8JFc6BfngDgC4rCpbX+ViLsmeqtEIGiFabzRIefInhJB3qgtrO21dku5JLKWjpuOW4rbSXz0q6o1IXbhuXc4KzAWA1xAAwOVge3nv9savrtgra0IIMdm7bH/66cU6uesYzBsn3qkT28fStPzUEG3QkNrvGOSHAABuK8vWn+hzWmRdYvlynjz8x5+67b1X/LDIR/WftjVamkVNUodrw3RPZq9KlKgkUAgEAHCrNzfYCkrWVDD++WCf2dG4s2Hzya2i3rb1F+ZmpLD2o3qx/SxNX5gSoQvDvB54DAEAomyr/ejcf9Z/KuqtVqk1WprNS4oePSZ3Hd54uerNhn4X34Y754VogrXrsh/HE0HgMQQAiPbAgccqyzsq2uSugxBCWq3t/Td/d/dBT17EupJ0WE3OHY1fN4rtZ0nafbgLAI8hAEA0q8vqvmHXnWWl7Ydk" +
            "DQGTvct2+/fziqu6qmXd+YvX3yvfqBM7nBaiCdY+PW51ojQVwXCHAABJmB1m1427ZpfuPruvRY7jn+yp7Z6+85YfyjuOcO9ZILeStkPmQx0V3IvtnbcoZUFypD4cdwEwKAQASKbP2ee+addd5U8d/tNRq8vmtyGYHY07G67+8vofh+qV/0BvVW+tE9tHsCZI+8y4tUkSlAPDHAIAJPfCzy+fmfbVzH3fNH/fJOXLYhc60VPTNWfvwqI7dy840ufsc/vqOP5UeOqjc+esbaJ3LluYMj/ZqI/EXQBcFgIAfOKI6Zjl1u/mHp705fW7P6n/or7b3mOXol83c7OStvK2gpK15dnb83747MyXflvm2R+cbif78NTHoh8JDVIHap4ZtzZZippg+KKqbZHb5S4Chj+1oKY3x10fPjvh9uhxEVmhSYGJwWHa0EFX+GSEsSZLi+VET3V3cVtp57s1H7TUmxu4l6P2RFpIsn7u6NkjeNp+3fxth9h5iGi9UfNA2v2x" +
            "YvoghBCTo9v5RtXbflkGG4YmBADIJjYgWpMZmhYQpTdqQlRB6mBtkIoyQrscPY4ue7fTZDM5j3Yd67uS1/IBGMowRgiyabGcc7RYznXLXQeAUmEOAABAoRAAAAAKhQAAAFAoBAAAgEIhAAAAFAoBAACgUAgAAACFQgAAACgUAgAAQKEQAAAACoUAAABQKAQAAIBCIQAAABQKAQAAoFAIAAAAhUIAAAAoFAIAAEChEAAAAAqFAAAAUCgEAACAQiEAAAAUCgEAAKBQCAAAAIVCAAAAKBQCAABAoRAAAAAKJRDKrHIXAQAAfkaZRSCEdMhdBwAA+BmjnQJzs1q56wAAAP9ilFULgkCK5S4EAAD8S2CsRHDWmooIYS1yFwMAAH5C2VnnKFOxwDYwJxXYVrnrAQAA/6Bu8i6bwZwCIYQ4FnT+SAj7XO6iAADAxyj71HF/RxEhA94DcJ3q3EIE9pl8VQEAgE8x9pmrtvP/R3woY+wXv9e8HzmVMZJPCI31d20A" +
            "AOALrIUysuX8lf95vwoAQgihe6ha3RA+xU3pFMpoCqEsijCq91utAADAjzIrYbSdEVYjCKzYWWM6yDYw54Uf+x8ayciY0LmtLQAAAABJRU5ErkJggg==";

        public const string Yellow =
            "iVBORw0KGgoAAAANSUhEUgAAAYAAAACACAYAAAACsL4LAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAAGXRFWHRTb2Z0d2FyZQB3d3cuaW5rc2NhcGUub3Jnm+48GgAAE1dJREFUeJzt3XlwVPdhB/Dve3tqJa2OvbSSQBLCgGwu2xyCEBA+cLAxpvE0nmnSxmPjpG7TOk478bSZ1nXajhtPO26ajj12mxC3jT12OrabGHcSbMDBiMPY3OaS0AFIi7QridXuaqVd7esfFMIhsft+u/veivf9zPAH6P3e+yHE77vvd0qKouA621ebEe1aBgnNUJRGQHJBgf36C4mIqOBIiANKCIrUDgm7UVy3Cy3bktdddl0AvNe4ApLyKBT4tKorERHlVQAm+adY27bzyj/8bQA8J8m4s+ExAA/pUDkiIso3Ce9iX8cmPKukAEC+/AU2/kRENzcFG7BoxqOXfnsxAN5rXAE2/kRENz9F+R1srl8OAJKyrcWMaNdLUODXu15E" +
            "RKQBCedxrvJJGdGuZWz8iYgMRIEP1YNLZSjKMr3rQkREGpOUZhmQZupdDyIi0poyUwZQoXc1iIhIY4rkkgGu8CUiMiC7nP4aIiK6GTEAiIgMigFARGRQDAAiIoNiABARGRQDgIjIoBgAREQGxQAgIjIoBgARkUExAIiIDIoBQERkUAwAIiKDYgAQERkUA4CIyKAYAEREBsUAICIyKAYAEZFBMQCIiAyKAUBEZFAMACIigzLrXQFSqXxuMSqbK1BSX4yiWges5TaYHCbAJAPjKSSHk0hEE0gOJxDvHUH48zD6W4cQD4zpXXUiKiwMgEInS4D37krU/m4tKhf7YC23qb+JoiDSGUb/1h50/ewcIp3x3FdUpcYnajHj8VnC5aNdYbQ+si+HNbqo8fEazHhitlDZT5/8BAP7h4WfveAHc+BdVS1Udkvz1ht+/Z6dqyGZJKF76+Hs/3Th2PPtelfjZscAKFSSLKHh69VoePwWFFWXZHkzCSUNZSh5vAx1X5+D" +
            "/h1nceL5kwi3jeSmsgL8a2tg8zqEy9s8RXBMsyF2ZjSHtQLMJRbhekm27LpULeXWrL4nN2L1OCCbp04AWMsselfBCDgGUIg8y8uweusXcetf3Z59438N2SzBt3oaVrzXgqa/aISsQ5tgrTCjbJ4ru5tIEmo3+HJTISJjYgAUElkC5j53C5a8tgKO6c78PstqQuMTTWh+czGspaa8Putateu9kEzZ/+x5W6pyUBsiw2IAFApzkYwlr92O+t+frWlfbeUiH5b9dzPsLu26Az1356bhds5zw1rObkwiQQyAQmC2SWj+r0Vwr6jR5fmlt1RgyX8shtme/58Hs01C5R2enNxLNsuoXpebexEZEANAb7IE3PnyApTf7tW1Hs4mF+b+/Zy8P8d7nxsmR+4G+Hw5epsgMiAGgN5m/VkDPC21elcDAFC7oQFVa7IcnE3Dvya3DXblYi9MVv4cEwngfxw9uZrLMOMbTXpX47ckCfOem5+3QWFZAlzNuZ25Y3JY4FtT" +
            "mdN7EhkEA0AvsgTM/f48yObC+jew+Yox688b83Jv1xfKYa205/y+VfexG4hIQGE1PkbSsLEWpTPL9a7GhGq/3ACLM/eza/wP5Kehdi+r0mU9A9EUxwDQg2yWstoGId/MxRbMfHJ6zu/rWZGfhVvWSjtcy8rycm+imxjnUOth2ld8OVvyH+0M48LhEEZ6RyDLEuw1DjhvrUBxXXYLyaZ/ZQZOvtiJ8bFUTurpvLUYRTWlObnXRPz3V6G/9ULe7k90E2IA6KHu9xqyvkekbRBH//Yo+ncMTfj1qjUuND3ThOIGsW4mS4Ud1evcOPN2XzbVvKxmfX63bXCv8AE4kddnEN1kGABaK5lug7Mpu1krgS3d+Oxbh5FKKJNf8+sQQh/vwh0vzYdnpdgCM/8D1TkLAO/K/A7UOqY74ZzjQPh4LK/PmaoOPL0HEBgnMdlNWPDCYqFnDn16Hqdf6xAqGzmt30aFBsIA0Frtw35AEh+xHNrfh8/+8BBSk7f9lyVi4/hk" +
            "4wGseMcG521u1c9yLfXBZJWz7gYqqrahZFaFqjLho0HVda5eX4Xw8dOqyhhFz+agUDmL04wFL4g9c6Qvjp73xJ5LmuAgsNbcWQyEJqNjOPD0wYwa/0tSSQX7/mg/khH1B8KYHBZUr1MfHNeqXe+FpHKazskfnoSi4u8JAN5V3B2USAUGgJZki4TSJnWfhK90+tXjiHSr3/8+dmYUZ9/J7FVcSSmItA2i+/VT2PfNVvRu7lf9vGt57lLX/TM2EEfggwHEeyOqypXOroS9yqqqDJGBsQtIS64lTpjsYt/zZHQMHT8+J/zs9le6UffVWRN+Eo8Hohj8LIjgzn4EtoQwGkwIP+da1lITyheoe4u4cOhit8HQwaCq8xAkWULteh/aXj2j6nlEBsUA0FL5QvG56n3be5CIjQuXH+kZReTUIEpnVyIZGcPQoSBCrUEEtgQxfCp/A6f+dR7IFnVbSwzsDQEAgq0h+NfWqyrrvYsBQJQhBoCWShrFT/cK7cp+MO3z" +
            "fzgGJFII7g5DUTOQkAXfPepn/5zffjEA+reGoHxf3Zh5+UIPLA5TVmFJZBAcA9BScZ3YQihFAfq3DWT9/P6PBtHfekGzxl+2SKhcom6b69HgyOWpnLHeMcTOqDtkXbaa4OcZAUSZYABoye4vEiqXHB5FrFf9LB69Vd1TCXOxukHZocNXv+lcOKj+zcd3L2cDEWWAAaAli9MmVG60f2ouihHZpXNgd+iq3wd3hSa5cnKuZh9kC3eHI0qDAaAVk1WGXCR2Elb8/NQMAPdy9Z/E+7dd3eD3fRCC2gUB5mIrvKvEp9sSGQQDQCs2n0V4AXAyPPW6f1xLnLC61W14N9oXQ7jt6rCLBxOIdoVVP79qLc8IIEqDAaAVs038ez0en3ozWqrXqW+Ahw5N3N8/uF99N5B7OQOAKA0GgFbkIvHvdSpHWzJryb1CoP9/z8QNfbBVfQDYfQ5U3pm/7aeJbgIMAK3IWRxcPtUCwDmzCA6B8wjOfzhxQ9+3NQRlXP3U1eoH" +
            "+RZAdAMMAK2kRsUbcckytf6dqjdUqR7viAeiiHTGJ/za2GAS0S71h714vsjpoEQ3MLUalqksMSzej5/N24MePKvUf/IePHjjbh6RcQBHfTlKGsTWXhAZwNRqWKay8UhSuKzJpm4vHT3ZPRY456g/8Gay/v9LQjvVLwiTJKDmIb4FEE2CAaCVZCSLN4AsBpC1VrPeC8mkfr5r/9YbN/Dntw4ilVQ/DuBtYQAQTWLqNCxTXXJUQSopNg5gKRNbQawHr8q9/wEgHoikPecgEU4i2j7x+cc3UnqrGza32AI8opscA0BLY0GxFb1Fvtz0Y1tLTVi9bSVu+14jSurtObnnlcxFMiruUL8RW6b9+0P71XcDyWYJ1esy35AupdFGeTml9ug0oou4HbSW4oEo7FXFqsvZ3LkJgMrl5Siuc6LhcScaHpuDC4dD6Nl8Ft1vBpAIi49RXOJf64ZsU/8zZS4yYdafTE9/nVPstC/fPT50/DSzw3RSWSy6k7NY7AcAJsHZ" +
            "Xtme2UyGxQDQUuxMFOUL1ZeTbWY4amyInVN/HOSV3Euv2B9HklA2342y+W7M+s44QrsCOPvuWQTeDwr1tQOAb43YvHtPSy08LbVCZTNRcbsXZruMZDx9Q5mMigehrTy7/0+mErGuqtTY1FspTgWBXUBainVGhct6WtTPrLnWZAfSm2wmeFtqcMc/L8W9e+7Gwn9sgmuRulW0kizB1VyYA66mIjOqvuTK6NpsQtbmzW6sxlohVj45PPX2iqKCwADQUvi4usNNruRellkDNpmSejtKGsvTXmepsKP2y41Y9tYqrN66ErO+XZfR/b0t5cLbXWuhKsO3k5Eu8Z1Xy24TP/LTbJPgmC62dcXYwMQL6IjSYABoqW/7IFIJsdf1yjs9mOA894zN2Fin7mxFAMX1TnhXZtZwFvrum66lPkgZfANjXXGMxxJCzyhbIB7SrhUVqs9OvmQkMDW3CyfdMQC0lBxJIXxU7GhHm68YVQ+4hcpanGZUr8/sk/y1Bg9kVl9P" +
            "ge++aamww7Mi/Sf0lAJEO9RvOwEAxXVOuJrF3gKmPTJNqBwARNrE3yzJ0BgAWgt9In64++zvNMEksC3E3L+ZBXOJ2AyaUAYnclUsKIHdr352k9b8D2QWUuETYgEAAE3fna26TPn8EnhX1Qg/M3xYvL5kaAwArfX+b59w2eL6Mtz58nxVZWo3eFC9vkHoecloAsHfDKa9bqrsuun+QoYBkEWDWr7Qi9lP1Wd8vbXUhIX/tBCy4BTQVFJB8GP1C+SIwADQ3tCBYYSPiXUDAYB3dS2W/HghrBlMOaz7qh/zf7Aoo77viQzsPY/kaPopoZ6VhTn751pF1SUon5f+TWXgs+w+Uc/807m47S8bIZtv/H0vqbdj2VtLMxqcn0y0fRBjWWw0SIbGdQB66H6jA3O/Lz6t07u6Fi2/rsS5d7vQ/fNeDJ+KXf6atdQE/4MeTHt4Ospvz3wF7ER63u9Ne03JdFtWDZjW/OuqMHS4/YbXhI9EMTYQh7VSbLW0JAENG5vg" +
            "vbcGZ9/uQu/7fYh1xZFKKrCUmFBxRyn8D/pRs65OaOHclYIfn8+qPBkaA0AP3W8GMOvb4g0MAFjdDjRsbELDxiakEikkLozC5DDD7MjNvjejwRH0/CJ9d5X/oSrVs4v05F1ZhWPP3zgAUgoQ2nMe/rViA+eXFNc5MfvpeZj9NABFQWpsPOsG/0qKAnS/lT6kiSbBLiA9pBIKun/WlrP7yRYZNndRzhp/AOh5txOpRPruH9/qqdH9c0nprHI4atKvV+h+vTu3D5aknDb+ABA+HLzq7Y9IJQaAXk7+axei3YU5fS8ZS6D91fQNoLXCDOdcsamp4yNJJGMJ4V/jI4JbNkhSRmcE9O+8gOHj4mM1WujYdOM3GaI02AWkl1RCwbHnj2DRy8v0rsp1uv7zFOLB9Iuhqtd5IZvVf4hIJRV82Lwlq8FLa4UZ9+69T+jsAc9dVTj1UvqAO/HicSx6ZblI9fJu+MQAen7Rr3c1aGrjG4CeAr8K4fy2M3pX4yrxQBSn" +
            "ftiZ0bW+u8W6f6IdQ1nPXBE9JxgAyue5YXGm//AT2DKA4MeZ7SKqJSWl4MhzRzEVd66mgsIA0NuBp44UTFdQKqng4Hf3Z7RrptkmoXKR2CyjoUO56VoZOqD+nGDg4phJ9YOZnVtw6JmjSISz24U11878vB2h3Vz8RVljAOgtERnH/m99ivF49vvxZ6tj03H0Z7ioyLvGDZPgoPNABquLMxHaLR4kVRm+vcR6x3Dwmc+gjBfGx+3h4wM48tcn9K4G3RwYAIVg6EgE+5/ai9SofiHQs7kz7fTIK/nvE+v+UVIK+j/KzRtA3/YB4dOwKhb7IFsyGz8I/CqEU/9yWPeDt+K9Eex5bF9Gs7OIMsAAKBSBLQP49I/3IBnTPgQCW7qx/6mjGV8vS4CrWWz7h1h3GPFQbv6Oo8EEol1hobLmYgt892S+e+fJH3Wj/ZXPdQuBeCCK3X+wB/EA9/6nnGEAFJLzWwex+2s7Ee3Upn9XSSlo/7dj2PfNQ1BUjCi6lpcJ" +
            "L2K7kKP+/0sGD4rfz/8ldW8xx184jWN/tx/jo9puvTB8YhCtD+9EpJ3bPlNOMQAKzdCBYexY+zG63zglfHZAJmLdYXzy2E5V3T6X+O8X3/wttDc3/f+XDOwRv59rWZXqMxZObzqHXY/syGo/p0ylkil0vX4SO9a3ItbLT/6Uc1wHUIiSowoOfe8ETv6oAzOfrEPN+npYynJz2tbI2WF0vdGBzp+cyWijt4m4MtxV81qKAvRvy23D2bf1/8cBBLajsLmLULG0TPWMmqFDEfzmgVbUbvBi+tfqUbHQI7zh3kTGYwn0fdSDUy+2IdzGT/2UN5Lyy4Zf6l0JSkOSJbiXl8F3rweVi9woqi2FpTSz/f1TyRRi3WEM7u1H4MN+9G8dyGr+uNkmYcY3xA4vSY6kcPrfz4o/fBIzHquBuVjsNK2+HYMYOpDdNFy71wr//R5ULqpASaMT9qpidYGtKBjpiWDoUAjB1hDOvX0eyZH0U3G1kM2/d/hYBIEPCns1tcEx" +
            "AKYqu9sC563FsLqsMBebYHFaIJmAVBJIDicwFk4icjKKSNsIUknOGtGa2S7DUWeHY5odVrcVskW+vIW3kgISFxJIRsYRaYsh0hbLaO0FUY4xAIiIDIqDwEREBsUAICIyKAYAEZFBMQCIiAyKAUBEZFAMACIig2IAEBEZFAOAiMigGABERAbFACAiMigGABGRQTEAiIgMigFARGRQDAAiIoNiABARGRQDgIjIoBgAREQGxQAgIjIoBgARkUExAIiIDIoBQERkUAwAIiKDYgAQERkUA4CIyKAYAEREBsUAICIyKBlAXO9KEBGRxiTEZEhSSO96EBGRxhRlQIaitOtdDyIi0pgsn5IhYbfe9SAiIo2lsFfGvo5dkNCrd12IiEgzAZRM3y3jWSUJWX5N79oQEZFWUj9By7bkxWmga9t2QsK7OteIiIjyTnkb6zp3AVeuA9jXsQmS9I5udSIiovySpHfwaeflHh9JUZSrL9hcvxyQH4UCv9Z1IyKiPJDQCyW1" +
            "6dIn/8t/fF0AAMD21WZEupshKc1QlEZAcgOwa1RVIiLKThxQgpCkNsjybuxt34NnleS1F/0fYnugI3onnhsAAAAASUVORK5CYII=";

        public const string Red =
            "iVBORw0KGgoAAAANSUhEUgAAAYAAAACACAYAAAACsL4LAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAAGXRFWHRTb2Z0d2FyZQB3d3cuaW5rc2NhcGUub3Jnm+48GgAAGp1JREFUeJzt3XtwVNUZAPBz7r6zyT6yu0lIAuRJeCVBHgKhIE/BJy/BwHSqtS0tji3DiO1MC1OdimNR66Mz6rRVK6Ojdazo2BlBQIGaJ0kkIRFIIIEACXmQfdx9JPu4p39oKISE7Dn37t4N+/1m/EPYe88JSc5373fO+Q4mhKChjmCsxErlfA6heQTjXEyIBSGkvemDAAAAYlE/wfgqJuScgFAlCQYrFhMSHPohPDQAHFGpfsRh/CgmJDVqXQUAABA5GF9BhPxzYSBQdsMfXwsAGHP/VakeQ4SslqN/AAAAIotg/OmiQOAdRIiAEELc4F/A4A8AALc3TMiab1SqRwf/n0Po+7QPDP4AAHD7I4SsPaZSlSCEEHcEY6UC45/I3SkA" +
            "AABRgvFjtRirOKxUzkeEjJO7PwAAAKIDE5LKK5VzOQ6h+XJ3BgAAQHRxCM3jEMZ5cncEAABAlGGcxyFCzHL3AwAAQHRhQiwcgh2+AAAQj7Tc6J8BAABwO4IAAAAAcQoCAAAAxCkIAAAAEKcgAAAAQJyCAAAAAHEKAgAAAMQpCAAAABCnIAAAAECcggAAAABxCgIAAADEKQgAAAAQpyAAAABAnIIAAAAAcQoCAAAAxCkIAAAAEKcgAAAAQJyCAAAAAHEKAgAAAMQpCAAAABCnIAAAAECcUsrdgbFo0ptvFpjvvTeD9rpz27bV9+7bdzXcz+e88kq+bd268bTtXHn33bYLu3a1hft5W2mpLWfPnkLadrree+/8+d//vpX2uuGorFblzJqahYjjMOs9mtatq3bX1Lil6M9wJv3tb5PNq1alR+r+CCFE/P5QyO8XMEIkcPWqP9jXN+Dv7BzwNTe7+ZoaF19ZyQt+P5GqvdxXX823rl1L/TPWtnNnU/fevV1i" +
            "2laaTIpZDQ130V7naWqyN95zz7di2gbfgwDAwN/dPaAeNy6B9rrEWbMMNAHAtHixjaUd85IltgsIhR0AkmbNMrC04798uZ/2mpFYN2xIUWdk6MXcI7W0NDWSAUBpsahZ/p1Y6fLzb/ozweMJOCsqurv27r3U88EHPWLbUJpMTF9T1jPPTO3917+6hYEB9mDEcZjp566ry8fcJrgBpIAYuGtrXSzX6fLzE8P9LJeQwOny800s7SRMm2ZGCkXYT9K6vLyw+3U9vrqa6d9hOJYHH0wVew/jihVpUvQllnF6vcq8fHnG5L17585qaFiQNG9ekhz90GRm6ifs2pUlR9tAOhAAGDgqKnhE6B98tDk5YQ+0psWLjZxazfT9USQmqox33hl2WxqKfg0ifn+IP3HCQ3vdcDiNBhvnz08Rex/9lCkmXU6ORoo+jQUJU6aYiw4e/NG4X/1qnBztpz/++CR1WppKjraBNCAAMAh2dwcC3d1e2us048fTBIBk2vtfz7h0" +
            "qTncz2op+jXI19bGo0BAkly0Ze1aK6fXix9IMMa2TZtEv0mMJZxWq8h79dWZ47ZsifrbjyIpSZX95z/fnKcCYwYEAEa+lhbq9IfKYtEoU1LCGuiSZs8OewAf9vq5c8MKINqsLI0iKYl68PWdOSNZ+se2Zo1kg7Z51arbPg10E47DOS+9dId+xgxRcygsbBs3ZifNmhX1doE0IAAw8nz3HdMAaLjjjrB+WRIKC0UFgMTi4rCuTywuZvrl9TY1SRMAOA4ZFi2SbNBOmjXLqkxOjrvFDZxWqyh4660ixEX3VxorlTjnhRemRLVRIBkIAIw89fVMA2DC9Omjplv0M2bolSaTqFy2Oj1dr83OHvUeusJCpklE1onwocxLl5pUNptWinshhBBWqbiUhx+2SXW/sURfVGRJ3bxZ9FwKLcPChWm2jRut0W4XiAcBgJGrqootABQUjBoAzBT5+1veZ9myUe+TkJ9P/wZACHJVVfFMnRrCsm6d5Dn75Pvvj7800A8y" +
            "duyQJSef9cwzU2hWnoHYAAGAkefkSW/I6w3SXhfOksuk+fNFTQBfu8+CBaPeR5ebSz0BHOju9vmvXAmw9epGyXffLflgbViwIJXT6eLyZ1s/bZpZP3Nm1HPy2rw8Y+b27ZnRbheIE5e/JJIQBNTf1kb9FKzJyhp1wE2cMUOSN4DEmTNHvY924kT6FUBnz0qS/tEXFydoJk6UfB27Qq9XWh54QJIgOhbZNmyQZSXU+B07CpQmk0KOtgGbuJssk5Lv9GmXfto0qsFaPW6cntNo8Eg7KJUpKSqWQXk4Cfn5RoVez4U8HmG4v+eSkhSq1FQd7X19p09LEgBspaURS9VYVq9O6/noo95I3Z+W5+TJPsHvH/77oFJhpdGoVmdkJGKlUnQaxfj9CjBJSnTQUFos2qxnn809+8QTzdFuG7CBACCCu7HRZV2/nuoarFRifVGRnj9+fNiSBZbly00IY0lyqVil4oxLlpj6/vOfvuH+PmnGDD1LW27GCfChkleujFgA" +
            "MC1Zkoo4rhEJw465UXfqoYdqfa2tA7f6DJeQwNlKS1Mm/Pa3+drcXCNrW9rcXFl2ByOEUOojj+RefvXVdl9Li2RlQkDkQApIBPfx42wTwUVFIz7hh5O3p2FevHjENxT9LfpxK67jx0VPAGvGj1fTvj3RUNlsOtPixcyDqBwEr1foevvtK7UzZ5a5GxqGDdrhUKelJXBqtSwTspxWq8h58cXJcrQN6EEAEIGvqnIhQl8TQj916sgBYM4cSQfFxNmzRwwoCVOmUAcAob8/5P72W9ElIGybN6eJqfwZDuv69WNyNZDg9Qotv/xlA8vPFkIIIY7Dcu6FSL7nngzTsmVjKvjGKwgAIgQdjtDA5cvUJSF0eXnDrtLg1GqcMGUKUwG4kegLC80jbQ7SMqwA8rW18SgUEl0CwnLvvVQTld4zZxyCz0e16ip5xYoxWxbCXVPj9oqYbFfIuRkOY5zz0kvTo70pDdCD75BIXoaSEJrs7GFztEklJQZOq5X0F1dhMKgT" +
            "Z84cdqDXZWdTB4D+5mbR+X+lwaBImj2bauOQ65tvej2UaRFNdrZBX1QUtfLNUutn+NkaxEkwmSyGfto087jHHhuTb2DxBAKASF6GkhDaCROGHXhNS5ZEJCc+7IYwhQKrMzOp14t7JCgBYdmwwYbVaqrlgo6vvup1lZeHfZbCoEiuNIq0kMcTYr024HAwXyuVCbt2TYnX/RhjBXxzRHLX1VEPiAq9Xjlc2eKkO++MSAAwzJ170311BQVaTqOhXrPNS1ACwvrgg1SDMgkGBfv+/fa+Aweol3Umr1w5ZtNAmvR06iW6CCGECCHB3l5JNuqJoU5PhzMDYhwEAJF4xpIQ+hkzbnoLSCwsjMjmJf2MGTfdN7G4mH4FECGIr6gQFwBUKmxcuJCqXo339GlH0OUKOY4dc4U8Hqp5AP306cnqzEw1XSflx+l0nG7yZKb5oEB3t0/wemNi/Wv61q356vT0MffvHy8gAIjkO3PGF+J56qct/ZCicLr8fC3LpqxwaDIz" +
            "E4f+EuqnTWMpAeEN9PRQl7+4nuW++5IVSUlUA8K11E8gQDz19XRpII7DKaWlY+4tIP2JJzJUFgtTQcD+8+cjdiwmLUVioipnzx44MyBGQQCQAEtpBN2kSTcMwKbly6me/oN2+y03Fd0AY2S+++4b0kA0x1MO8p09K3r9v3XtWuqcvOOrr64N+s6yMup5ANoVR3IzLl5snLhr1zTW610VFdT/RpFkXb8+K3H2bEl2twNpQQCQgPfUKfqJ4CFLMA0lJVT5/6733gv70HeEEDIOuT/N8ZSDpJgANi9dSjUYk0BA6PvyS/vg/9v376eeB0i8806b0mCI6Ro1SrNZaSsttRW8++7U6Z9/XsLpdMyrwa7u29ctZd/Ewkolzn3pJTgzIAZBKQgJeE6eFL0SKJzCbYMEv1+4tGfPhfStWwvCrR0zdEMYS70hlq/zekkLFhhUaWlUyzK9p07ZBZ6/tqLFWVbGh3jeT5NG4jQaheWhh2xdb799haZtKU16552i0MDA" +
            "TXl5hUqF1enpCZqJE5OwBOWUvWfOOJzl5ZKU6paSoaQk1bp+vbX33/+OmfpMAAKAJPiaGvrjIW02ndJkUgQdjpDSYFDocnLC3jnpa2lx+K9cCfjOnXMmFBSENVGYUFBg5HQ6TvD5BGVKikppNlPnl13V1aICQMrDD1Onf5xD0xmhEHHX1fUZ77qL6l7WBx5IlTMAGEpKopKG6nzzzagXgQtX9u7dU3s//fS/UmwkBNKAFJAE+OpqngSDdD/UGCP9DytxTMuWmWiqQLprauwIIeSurQ17YxRWqxXGRYuMCCGUdMcdTCUgPPX11Luer8eyM9dx6NBN+WxXeTn1U6Rh4cJUpFLd1geWeE6dcnS8/nqH3P0YiTY31zB+xw44MyCGQACQgOD1CgOXLlGvvBgsxma46y6qCWBnWVkfQgi5ysrso332esYfCsPpCwvpJ4BbW11intx0U6boaCtcCn6/4Dh48Kav0f7FF9STnEqjUW29996IFZ+Tm+D3Cy1btzZE" +
            "q/qpq7y8i+W6zB07JqusVsg8xAgIABLxMZRISJg8WY8QQkkU+X9ECHIcOmRHCCHHoUNUpREMc+YkI4RQwqRJ1DuAfc3NovLKqZs2pSLKytPepqa+4c4ycFZV8SGnM/xVUD+wMKxAGhMIQRd27Wrgy8okKdMdjksvv9wa6OmhLvmsNJk0E595JjcSfQL0IABIxMuwQkaXl5eIOA7pp08POwAMXL7sGbh40Y8QQr7W1gF/Z2fYaRl9UZH5WruUPI2NogYXM0Ptf1dl5fBP+oKA+Npa6rcA05Ilt18AIARd3LOn6dJf/nIpms0Kbnfo4gsvnGa5NvWRR3J0BQUR2fMC6EAAkAjPUBJCl52dmDRnTqIiKUkV7jWeEydueOr31NeH/RagNJs1+uLihHCOpRzKLaIEhDotTaUvKrLQXmf/8ssRB3nXN99QBwB1enqCsaREtsNSpCb094fObdtWe37nTqolwVK5/Nprl30tLQ7a6ziNBs4MiBEQACTiLC+nXwmU" +
            "kZFoWrGCamB0VlbekBPnq6up5gGSV62yqMeNo0sBEYL4ykrmAGDbtCmV9qhDYWAg5Dh8eMTBpW//fqbNTlaGlUgxhxBk//rrzm/nzTva8cYbnbL1IxQirU8+2YQYji1IXrkyw7RkiaSlzwE9CAAS8be3D1DtzkUIcWo1Z1u7Np3mGueRIzc88TsOH6aaB7Bt3JhJOxgHurq8gd5e5hIQyQw7cb2NjX2CzzfijCZ//Lg7ePUqdQ7aPIbPCPB3dHg7//73lhMLFhxpvPvuWm9Tk6hVWVLo++ILu+PoUfoghDHK3r0bNofJDAKAhFhKQugLC8N+AwjxvH/oWcLOqiq34PGEXYuIpr1BPhF16RV6PWeYN89Ge1045Qz4ujrqtwBdXp5JN2mSlvY6uQl+v9D72WcX2//0p9aRzpOWS+uTT54igQD18iMx5x4DaUAAkBDL2QA0K2O8jY32m5b5hULE09gYfhqI4bx5D0Opi0HW9eutLIfc3Cr/P8h57Bh9Gghj" +
            "lLJ585hLA3FqNZe+dWvBnObmZVnPPZeDJNg1LBVPQ4O358MPZZmHAOJAAJCQu6EhosvwXD9sABuKr62lmgeg5amvZ/66LJS1/xFCSOjvDzq++mrUycW+L75gKiuQvGpV1NNAp3/yk6qTK1f+t3nLluN9n3/eToJBpgX7nFarHP/UU1OLv/56DqfRxEwQaP3d71qCTqdf7n4AOrAhQ0K8yFIJo3EdPTpsvt959Ghf+uOPR67d6mq2PQAKBTYuWkQ92Ab7+vyZO3aMD+ezwsBAiPZgG31xsVWZkqIKdndH7dAUd1WVy9faOoAQcna9806XcdGi89M++WSuwmhkKvlsmD8/peC99wpPbdjQIHFXmQR6eoKX//rX5ok7d06Xuy8gfBAAJOSpq3MLfr/AqdWSv1mRYJA4vv7aOdzfOQ4dcpBgkNBO7oZD8PmCnpMnmSYbk1euNLHUHFKnpydMfPrpIpY2w4GVSpxSWprS8dprlyPVxmicx465mrdsqZvywQfz" +
            "EMcxfd+sa9ZMSN+6tUfWlUDXaX/uuQupP/7xRG1W1m2z1PZ2BykgCQl+P+m/cCEilRi9LS3OoMs17DmvQZcr5GttHTY4iOVra+NZS0BY162L2Vy75b77ZF8N1PvJJ1d79+1rF3OPrGefLdSMHx8bJ24FAuTC00+fkrsbIHwQACTWf+ZMRNJAoxV+c9fVRWQewCdiAti8bJnsg+xIDCUlKQq9Xvaf/7adO1uI3898gLvCYFDnv/76VCn7JEb3++9388eP98jdDxAe2X8BbjdSHJoyHL6i4pYDPF9eTrUfIFxMK5sQQklz5iSqMzNj9hQoTqtVWtasscrdj/6zZ/t7P/vsoph7mFetyrSuXh2R86RZtD355HdIEKDk8xgAAUBiYkom3Mpohd/6hqmaKQWWsw4QQshWWhqz6Z9B1tWrY+INpX337lYiskb+hD/+MWZKKzgrKvjeTz8VldoC0QEBQGKOsjIXy9b4W/F3dHh/WEEyov6zZ/sDXV3S7gwlBPGM" +
            "K4BMK1bEfAAwLlmSFgvr6b1NTV4ny27a6+gLC5OTV66MmdIKrTt2nAl5PMy7x0F0QACQWLC7OxDo6fFJeU93mAXf3PX1kr4F+Ds7PSwlILR5eVr95MkxMxiNRGkyaZKXL4+J3agX9+w5J/Ye6b/5TbYUfZHCwMWL/q5//KNF7n6AW4MAEAEsJSFuxRVmwTe+qkrSeQBfSwvT0z9L7X+5xMpKJcfhw05PU5Oo759p6dJx2uxspn0FkdC2a1ebv6ND9npFYGQQACJA6olgV5gF3/oOH5b0DcDDOAFsWrUqJgbVcJiWL4+Zvna+/rqo83yxUsllbN8+Uar+iCX4fEL7888znRkAogMCQAR4JCwJEfJ6g87q6rCKf/GVlbyUeVc3QwkIpdmsTJwxg7rgnFw0EyYkJs6eHROrlTrfeqtL7BOzbePGiZxOFzO/151vvNHhaWyMyAo1IF7M/KDcTlwSloTwNDbaw96IFQoR73ffSfYWwFLaImXz5pRI7ISOpJSH" +
            "H46J1UAoFCJd774r6i1AZbFo0n72s5h5q0EIoXPbtzchIvHKCCCJMfWLOlZ46uu9Qn+/JE/io20AG4qvqZHkaUvw+YIs9eaTGXfYCn6/EHK7A2L+Y11KGUsrli69/PKlkNstqkZR2s9/HjOTwQgh5DxyxGk/eLBD7n6Am0EtoEgIhYivtZXXT50a/mHvIxh6AMyonz961J6+davYZpHv3DnXTaWnR8FpNNgwf34KS3sXn3uusX33blFrxwsPHJhpWrqU6oAdhBDST51q0mZna/rb2qgPmpda0G4P9nz8cXvao48yH5yunzbNbF6+3GQ/dIj6uMZIObd9+6mZdXVptIX7QGTBG0CE+E6fFp0GIqHQiAXgRuI4fNgudlMRQmwlLSxr11oViYlhn298PfuXX4p+c3Gy7obGGNs2bYqNNBBC6NKePW2s5aIHpf/611kSdUcSvubm/q69e0Wlt4D0IABEiLexUXQA8LW2uoJ2O1UqKehwhHytraLbdp88SX0P" +
            "25o1TINoyOkc4GtrRZ9yZT9wgOmcYIQQSo6hlUu+lpZ++6FDojaGmVesSNdmZcXMklCEEDr/hz+cC1y9KvtbFvg/CAARwlpC4XqeujqmJ1rW667H19TQ7QHgOGRgqP2PEEKuuro+2nTTcPjqaj7kdDINMEmzZ1uVyckxkxJtf/75c2J2lGOVisvYti2sMxWiJWi3By+9/PIZufsB/g8CQIS4KipcYlc+OMvKmFb0sF53DSGEtgSEeelSk8pm07E0566sZH5yv4EgIPeJE0xfO1apONvGjdRnF0cKX1bmcp84IerfJWXTpqxYOjUMIYQuvfjiRW9LS0RKlwN6EAAiJOhwhPydnaLWdDvC3AA2lP3gQVFvAP6ODi9t6sn20EPMOXSx/b2eM4zD5EeSfN99MZMGQgihDpEbw5QWizb1pz+Nqa8JhULkws6d38ndDfA9CAAR5G1pYU4DBbq7fb7m5n6Wa38oDMdcj4il38Zly5gGmpDbHXCWl0t2iI4jjMPk" +
            "R2JauDAllp6Yu/bu7Rpobxc1N5L2i1/E1JJQhL4/CMdVVtYldz8ABICIYq2ljxBC7oYGUU/FYq73Uh4Coy8uTmA9BtDT0NDHeuLYcJzl5XyI55kOJ+f0elXy/ffHzi5mQUBd//xnm5hbJBYVJZuWLo2JgnfXO7ttW5PYlU5APAgAEeT+9lvmAMBXVYnK47tEXE/b75RNm5jTDM7KSmnLBIRCxNPQwPy1W9eujZnloAghdPGVVy6FXC6mgDYo44knYqY+0CBPfb235+OPL8jdj3gXM6sebkf2/fv7Ljz9dAPLtT0ffdQtpu3u99+/ghifsOwHDlANoO66Ohfz1/nxx5IfH9j+7LMtSXPnMqUYBq5cGXGw7d6795K7ro5pcxVLWW2EEBJ4PtS8ZUtdwuTJCSzXI4RQyOsd8eeg58MPO3zNzdQpOO+ZM6KrfLY99VSz7/Rp6rYHLl+GpaQSwceUys/l7gQAAIDogxQQAADEKQgAAAAQpyAAAABAnIIAAAAA" +
            "cQoCAAAAxCkIAAAAEKcgAAAAQJyCAAAAAHEKAgAAAMQpCAAAABCnIAAAAECcggAAAABxCgIAAADEKQgAAAAQpyAAAABAnIIAAAAAcQoCAAAAxCkIAAAAEKcgAAAAQJyCAAAAAHEKAgAAAMQpCAAAABCnIAAAAECcggAAAABxCgIAAADEKQgAAAAQpyAAAABAnOIQQv1ydwIAAEB0YYy9HMb4qtwdAQAAEF0EoT6OEHJO7o4AAACILkxICycgVCl3RwAAAERXEKFqjgSDFQjjTrk7AwAAIEowvoKCwUpuMSFBRMi7cvcHAABAdAiEvL2YkCCHEEILA4EygvGncncKAABAZGGMP7krEKhA6Lp9AIsCgXcwxvvk6xYAAIBIIhjv+1EgcC3jgwkhN3zgmEpVgjF+FBEyLuq9AwAAID2MOwVC3hl88r/2x0MDAEIIHcFYiZTKeRxC8zDGuYgQK0JIG62+AgAAEKUfYdxLCDmLEaoMBYNViwkJDv3Q/wB7J4yu" +
            "It33lwAAAABJRU5ErkJggg==";
    }
    #endregion
}
