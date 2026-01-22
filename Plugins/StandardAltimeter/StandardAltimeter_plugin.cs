using MissionPlanner.Plugin;
using MissionPlanner.Utilities;
using System;
using System.Windows.Forms;
namespace StandardAltimeter
{
    public class StandardAltimeterPlugin : Plugin
    {
        public override string Name { get; } = "Standard Altimeter";
        public override string Version { get; } = "0.1";
        public override string Author { get; } = "Bob Long";

        // Reference to MSL Indicator label for updating
        private readonly ToolStripControlHost msltoolstrip = new ToolStripControlHost(new StandardAltimeter());

        // Stored previous value of kollsman for MSL Indicator to detect changes for logging
        private decimal last_kollsman;

        public override bool Init() { return true; }
        public override bool Loaded()
        {
            ThemeManager.ApplyThemeTo(msltoolstrip.Control);
            Host.MainForm.MainMenu.Items.Add(msltoolstrip);
            loopratehz = 1;
            return true;
        }
        public override bool Exit() { return true; }

        public override bool Loop()
        {
            if (msltoolstrip.Control.Visible)
            {
                string msl_alt_string;
                double msl_alt;
                var control = (StandardAltimeter)msltoolstrip.Control;

                decimal kollsman_value = 0;
                control.Invoke((MethodInvoker)delegate
                {
                    kollsman_value = control.num_kollsman.Value;
                });

                if (Host.comPort.BaseStream.IsOpen)
                {
                    double ratio = Host.cs.press_abs / (double)StandardAltimeter.FromKollsmanDisplayUnit(kollsman_value);

                    // This needs to match the indicated altitude for manned aircraft,
                    // which do not correct for temperature
                    double temp = 288.15; // ISA std temperature at sea level

                    if (ratio > 0.25)
                    {
                        msl_alt = Math.Floor(153.846154 * temp * (1.0 - Math.Pow(ratio, 0.1902631)));
                        msl_alt_string = Math.Round(msl_alt * 3.28084) + " ft MSL";
                    }
                    else
                    {
                        msl_alt_string = "∞";
                    }

                }
                else
                {
                    msl_alt_string = "Disconnected";
                }

                // Invoke to update text of the label
                control.Invoke((MethodInvoker)delegate
                {
                    control.lbl_msl.Text = msl_alt_string;
                });

                // If the Kollsman window has been adjusted, send a message to the autopilot to
                // capture it in the log. This is useful for post-flight analysis. Only do this
                // when armed so we are 100% sure the aircraft is logging.
                if (Host.cs.armed)
                {
                    if (Host.comPort.BaseStream.IsOpen && kollsman_value != last_kollsman)
                    {
                        Host.comPort.send_text(5, "QNH: " + kollsman_value);
                        last_kollsman = kollsman_value;
                    }
                }
            }
            return true;
        }
    }
}