using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows.Forms;
using Carbonix.GDL90;
using log4net;
using MissionPlanner.Controls;

namespace Carbonix.UI
{
    /// <summary>
    /// Provides the operator surface for the GDL 90 stream to an electronic flight bag.
    /// </summary>
    /// <remarks>
    /// Holds no decisions of its own: it reads the boxes into a
    /// <see cref="Gdl90TabPresenter"/>, paints what the presenter answers, and owns the
    /// timer that decides how often to probe the destination.
    /// </remarks>
    public partial class EFBTab : UserControl
    {
        static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // How far to blend the address box towards red when it will not parse.
        const double ErrorTint = 0.3;

        readonly Gdl90Service _service;
        readonly Gdl90TabPresenter _presenter;

        // The box's color before it was tinted, captured at the moment of tinting:
        // ThemeManager may set BackColor after this control is built.
        Color _neutralBack;
        bool _tinted;

        DateTime _lastProbeUtc = DateTime.MinValue;
        IPAddress _probedAddress;
        bool _probing;

        /// <summary>Initializes a new instance of the <see cref="EFBTab"/> class.</summary>
        /// <param name="service">The stream the tab starts and stops.</param>
        /// <param name="presenter">The decisions behind the tab.</param>
        public EFBTab(Gdl90Service service, Gdl90TabPresenter presenter)
        {
            InitializeComponent();

            DoubleBuffered = true;

            // Before anything touches a control: assigning Text below raises the
            // handlers, which dereference these.
            _service = service;
            _presenter = presenter;

            CMB_icao.Items.AddRange(presenter.IcaoOptions.ToArray());

            // Nothing preselected: the operator picks every session.
            CMB_icao.SelectedIndex = -1;
            CMB_callsign.Items.AddRange(presenter.CallsignOptions.ToArray());

            TXT_destination.Text = presenter.Destination;
            TXT_destination.Leave += TXT_destination_Leave;
            VisibleChanged += EFBTab_VisibleChanged;

            Repaint();

            _timer.Start();
        }

        // Runs the tab only while somebody is looking at it.
        void EFBTab_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                _timer.Start();
                Repaint();
                Probe();
            }
            else
            {
                _timer.Stop();
            }
        }

        /// <summary>Repaints the children as well as the control.</summary>
        /// <param name="e">The event data.</param>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate(true);
        }

        void TXT_destination_TextChanged(object sender, EventArgs e)
        {
            if (_presenter == null) return;

            _presenter.Destination = TXT_destination.Text;
            Repaint();
        }

        // Leaving the box is the address being finished. Probing on each keystroke
        // would ask about every address passed through on the way to the real one.
        void TXT_destination_Leave(object sender, EventArgs e)
        {
            Repaint();
            Probe();
        }

        void CMB_icao_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_presenter == null) return;

            _presenter.SelectedIcao = CMB_icao.SelectedItem as string;
            Repaint();
        }

        void CMB_callsign_TextChanged(object sender, EventArgs e)
        {
            if (_presenter == null) return;

            _presenter.Callsign = CMB_callsign.Text;
            Repaint();
        }

        // Replaces what was typed with what will actually be transmitted.
        void CMB_callsign_Leave(object sender, EventArgs e)
        {
            CMB_callsign.Text = _presenter.NormalizedCallsign;
        }

        void BUT_start_Click(object sender, EventArgs e)
        {
            if (_service.Enabled)
            {
                _service.Disable();
                Repaint();
                return;
            }

            string message;
            if (_presenter.DecideStart(_service, out message) != Gdl90StartDecision.Ready)
            {
                // Should be unreachable: the button is dark in exactly these cases.
                CustomMessageBox.Show(message, "EFB");
                return;
            }

            // The service is told what was claimed, not asked to work it out.
            _service.SitlStreamingEnabled = _presenter.SitlAttested;

            _presenter.Save();
            _service.Enable(_presenter.Endpoint, _presenter.BuildConfiguration());
            Repaint();
        }

        void CHK_sitl_ack_CheckedChanged(object sender, EventArgs e)
        {
            if (_presenter == null) return;

            _presenter.SitlAttested = CHK_sitl_ack.Checked;
            Repaint();
        }

        void _timer_Tick(object sender, EventArgs e)
        {
            Repaint();
            Probe();
        }

        void Repaint()
        {
            var view = _presenter.Describe(_service);

            LBL_status.Text = view.StatusText;
            BUT_start.Text = view.ButtonText;
            BUT_start.Enabled = view.ButtonEnabled;

            // The service holds the configuration it was started with, so a box that
            // disagreed with the wire would be worse than one that cannot be typed in.
            TXT_destination.Enabled = !view.Running;
            CMB_icao.Enabled = !view.Running;
            CMB_callsign.Enabled = !view.Running;
            CHK_sitl_ack.Enabled = !view.Running;
            CHK_sitl_ack.Visible = view.ShowSitlAttestation;

            // Taken off screen means taken back, so a claim cannot stand unseen into
            // the next session.
            if (!view.ShowSitlAttestation && CHK_sitl_ack.Checked) CHK_sitl_ack.Checked = false;

            LBL_callsign_out.Text = view.NormalizedCallsign == CMB_callsign.Text
                ? "" : "-> " + view.NormalizedCallsign;

            LBL_liveness.Text = view.LivenessText;

            LED_liveness.On = view.Liveness == Gdl90Liveness.Answered;

            // Not while the caret is still in the box, so a half-typed address does
            // not flash red. Clicking Start takes the focus first, so the tint still
            // lands before anything can be switched on.
            Tint(view.DestinationInError && !TXT_destination.Focused);
        }

        void Tint(bool error)
        {
            if (error == _tinted) return;

            if (error)
            {
                _neutralBack = TXT_destination.BackColor;
                TXT_destination.BackColor = Blend(_neutralBack, Color.Red, ErrorTint);
            }
            else
            {
                TXT_destination.BackColor = _neutralBack;
            }

            _tinted = error;
        }

        // Blends towards a color, so the tint works in any theme.
        static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        // Asks the configured address whether it is there, at most once every
        // Gdl90DeviceProbe.Interval and immediately when the address changes.
        // Overlapping probes are refused rather than queued, and a failed probe counts
        // against the interval like a successful one.
        async void Probe()
        {
            if (_probing) return;

            var address = _presenter.ProbeAddress;
            if (address == null) return;

            if (address.Equals(_probedAddress)
                && DateTime.UtcNow - _lastProbeUtc < Gdl90DeviceProbe.Interval) return;

            _probing = true;
            _probedAddress = address;
            try
            {
                var result = await Gdl90DeviceProbe.ProbeAsync(
                    address, _presenter.NeedsName(address));

                // Seconds may have passed; the tab may be gone.
                if (IsDisposed || Disposing) return;

                _presenter.ApplyProbe(address, result, DateTime.UtcNow);
                Repaint();
            }
            catch (Exception ex)
            {
                log.Warn("EFB device probe failed: " + ex.Message);
            }
            finally
            {
                _lastProbeUtc = DateTime.UtcNow;
                _probing = false;
            }
        }
    }
}
