using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;

namespace Carbonix
{
    public partial class ActionsControl : UserControl
    {
        readonly PluginHost Host;

        readonly private Dictionary<string, decimal> value_backups = new Dictionary<string, decimal>();

        // Used to prevent change handlers from running with programatic updates
        private bool freeze_handlers = true;

        public ActionsControl(PluginHost Host, GeneralSettings settings, AircraftSettings aircraft_settings)
        {
            this.Host = Host;
            
            InitializeComponent();
            
            // Bind event handler for mav parameter changes
            Host.comPort.ParamListChanged += ParamListChanged;
            Host.comPort.CommsClose += CommsClose;

            // Set up guided altitude control
            NUM_guidedalt.Increment = CurrentState.AltUnit == "m" ? 10 : 50;
            NUM_guidedalt.Minimum = (decimal)CurrentState.toAltDisplayUnit(aircraft_settings.guidedalt_min);
            NUM_guidedalt.Maximum = (decimal)CurrentState.toAltDisplayUnit(aircraft_settings.guidedalt_max);
            // Round to nearest 10 increments
            NUM_guidedalt.Minimum = Math.Round(NUM_guidedalt.Minimum / NUM_guidedalt.Increment / 10) * NUM_guidedalt.Increment * 10;
            NUM_guidedalt.Maximum = Math.Round(NUM_guidedalt.Maximum / NUM_guidedalt.Increment / 10) * NUM_guidedalt.Increment * 10;
            decimal guidedalt = decimal.Parse(Host.config["guided_alt", "100"], CultureInfo.InvariantCulture);
            guidedalt = Math.Max(guidedalt, NUM_guidedalt.Minimum);
            guidedalt = Math.Min(guidedalt, NUM_guidedalt.Maximum);
            NUM_guidedalt.Value = guidedalt;
            value_backups[NUM_guidedalt.Name] = NUM_guidedalt.Value;
            NUM_guidedalt.Enabled = true; // This one is not param-based, we can change it while disconnected

            // Initialize the alt frame combobox
            CMB_altframe.DropDownStyle = ComboBoxStyle.DropDownList;
            CMB_altframe.DataSource = new BindingList<KeyValuePair<string, byte>>
            {
                new KeyValuePair<string, byte>(CurrentState.AltUnit + " MSL", (byte)MAVLink.MAV_FRAME.GLOBAL),
                new KeyValuePair<string, byte>(CurrentState.AltUnit + " Rel", (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT),
                new KeyValuePair<string, byte>(CurrentState.AltUnit + " AGL", (byte)MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT)
            };
            CMB_altframe.DisplayMember = "Key";
            CMB_altframe.ValueMember = "Value";
            // Parse the altitude frame setting from the config file
            byte.TryParse(Host.config["guided_alt_frame"], out byte alt_frame);
            // Limit the frame to one of these three options
            switch (alt_frame)
            {
                case (byte)MAVLink.MAV_FRAME.GLOBAL:
                case (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT:
                case (byte)MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT:
                    break;
                default:
                    alt_frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT;
                    // Save that setting back to the config file
                    Host.config["guided_alt_frame"] = alt_frame.ToString();
                    break;
            }
            CMB_altframe.SelectedValue = alt_frame;
            value_backups[CMB_altframe.Name] = (byte)CMB_altframe.SelectedValue;

            // Set up loiter radius control
            NUM_loitradius.Increment = CurrentState.DistanceUnit == "m" ? 25 : 100;
            NUM_loitradius.Minimum = (decimal)CurrentState.toDistDisplayUnit(aircraft_settings.loitradius_min);
            NUM_loitradius.Maximum = (decimal)CurrentState.toDistDisplayUnit(aircraft_settings.loitradius_max);
            // Round to nearest increment, ceiling rounding the minimum
            NUM_loitradius.Minimum = Math.Ceiling(NUM_loitradius.Minimum / NUM_loitradius.Increment) * NUM_loitradius.Increment;
            NUM_loitradius.Maximum = Math.Round(NUM_loitradius.Maximum / NUM_loitradius.Increment) * NUM_loitradius.Increment;
            NUM_loitradius.Value = NUM_loitradius.Minimum;
            NUM_loitradius.Enabled = false;
            CHK_loitdirection.Enabled = false;
            LBL_loitradiusunits.Text = CurrentState.DistanceUnit;

            // Set up the airspeed control
            NUM_airspeed.Increment = CurrentState.SpeedUnit == "m/s" ? 0.5m : 1;
            NUM_airspeed.DecimalPlaces = CurrentState.SpeedUnit == "m/s" ? 1 : 0;
            // Airspeed min/max will be determined by params, so skip those
            NUM_airspeed.Enabled = false;
            LBL_airspeedunits.Text = CurrentState.SpeedUnit;

            freeze_handlers = false;
        }

        // Updates certain control values based on mavlink parameters
        private void ParamListChanged(object sender, EventArgs e)
        {
            if (Host.comPort.BaseStream == null || !Host.comPort.BaseStream.IsOpen)
            {
                return;
            }

            freeze_handlers = true;

            // This may be the first connection, so we may need to initialize the GuidedMode object. FlightData.cs
            // checks this to see what altitude and frame to send when the user clicks "Fly To Here" in the map.
            if (float.TryParse(Host.config["guided_alt"], out Host.comPort.MAV.GuidedMode.z))
            {
                Host.comPort.MAV.GuidedMode.z /= CurrentState.multiplieralt;
            }
            byte.TryParse(Host.config["guided_alt_frame"], out Host.comPort.MAV.GuidedMode.frame);
            // A z value of 0 is a flag for FlightData.cs to say "nobody has set the guided altitude yet; show the the
            // altitude popup when someone tries to do a Guided command". We need to avoid setting this to exactly 0.
            if (Host.comPort.MAV.GuidedMode.z == 0)
            {
                Host.comPort.MAV.GuidedMode.z = 0.001f;
            }

            // Sync the controls with the current values
            NUM_guidedalt.Value = (decimal)CurrentState.toAltDisplayUnit(Host.comPort.MAV.GuidedMode.z);
            value_backups[NUM_guidedalt.Name] = NUM_guidedalt.Value;
            NUM_guidedalt.BackColor = ThemeManager.ControlBGColor;
            CMB_altframe.SelectedValue = Host.comPort.MAV.GuidedMode.frame;
            value_backups[CMB_altframe.Name] = (byte)CMB_altframe.SelectedValue;
            CMB_altframe.BackColor = ThemeManager.ControlBGColor;

            // Get param for loiter radius
            if (Host.comPort.MAV.param["WP_LOITER_RAD"] != null)
            {
                decimal loitradius = (decimal)CurrentState.toDistDisplayUnit((float)Host.comPort.MAV.param["WP_LOITER_RAD"]);
                CHK_loitdirection.Checked = loitradius < 0;
                loitradius = Math.Abs(loitradius);
                // This should not happen, but if this is outside the bounds of the control, we will change the bounds
                if (loitradius > NUM_loitradius.Maximum) NUM_loitradius.Maximum = Math.Ceiling(loitradius * NUM_loitradius.Increment) / NUM_loitradius.Increment;
                if(loitradius < NUM_loitradius.Minimum) NUM_loitradius.Minimum = Math.Floor(loitradius * NUM_loitradius.Increment) / NUM_loitradius.Increment;
                NUM_loitradius.Value = Math.Abs(loitradius);
                NUM_loitradius.Enabled = true;
                CHK_loitdirection.Enabled = true;
            }
            else
            {
                NUM_loitradius.Enabled = false;
            }
            value_backups[CHK_loitdirection.Name] = CHK_loitdirection.Checked ? -1 : 1;
            CHK_loitdirection.BackColor = Color.Transparent;
            value_backups[NUM_loitradius.Name] = NUM_loitradius.Value;
            NUM_loitradius.BackColor = ThemeManager.ControlBGColor;

            // Get params for airspeed
            if (Host.comPort.MAV.param["AIRSPEED_CRUISE"] != null &&
                Host.comPort.MAV.param["AIRSPEED_MIN"] != null &&
                Host.comPort.MAV.param["AIRSPEED_MAX"] != null)
            {
                NUM_airspeed.Minimum = (decimal)CurrentState.toSpeedDisplayUnit((float)Host.comPort.MAV.param["AIRSPEED_MIN"]);
                NUM_airspeed.Maximum = (decimal)CurrentState.toSpeedDisplayUnit((float)Host.comPort.MAV.param["AIRSPEED_MAX"]);
                NUM_airspeed.Value = (decimal)CurrentState.toSpeedDisplayUnit((float)Host.comPort.MAV.param["AIRSPEED_CRUISE"]);
                NUM_airspeed.Enabled = true;
            }
            else
            {
                NUM_airspeed.Enabled = false;
            }
            value_backups[NUM_airspeed.Name] = NUM_airspeed.Value;
            NUM_airspeed.BackColor = ThemeManager.ControlBGColor;

            freeze_handlers = false;
        }

        private void CommsClose(object sender, EventArgs e)
        {
            NUM_loitradius.Enabled = false;
            CHK_loitdirection.Enabled = false;
            NUM_airspeed.Enabled = false;
        }

        private void UpdateParameter(NumericUpDown num, string param_name, double value, string fail_message)
        {
            // Update the specified parameter in the aircraft
            byte sysid = (byte)Host.comPort.sysidcurrent;
            byte compid = (byte)Host.comPort.compidcurrent;
            bool result = Host.comPort.setParam(sysid, compid, param_name, value);
            if (result)
            {
                // Update the backup value
                value_backups[num.Name] = num.Value;
                num.BackColor = ThemeManager.ControlBGColor;
                toolTip1.SetToolTip((Control)num, null);
            }
            else
            {
                CustomMessageBox.Show(fail_message);
            }
        }

        // This flag is used to detect if the user has changed the value of a control by typing instead of with the mouse
        private bool _num_changed_manually = false;
        
        private void NUM_KeyDown(object sender, KeyEventArgs e)
        {
            // Check for escape key and reset value to backup
            if (e.KeyCode == Keys.Escape)
            {
                NumericUpDown num = (NumericUpDown)sender;
                // Handler will trigger and handle the bg color change
                num.Value = value_backups[num.Name];
            }

            _num_changed_manually = true;
        }

        private void CMB_KeyDown(object sender, KeyEventArgs e)
        {
            // Check for escape key and reset value to backup
            if (e.KeyCode == Keys.Escape)
            {
                ComboBox cmb = (ComboBox)sender;
                // Handler will trigger and handle the bg color change
                cmb.SelectedValue = (byte)value_backups[cmb.Name];
            }
        }

        private void NUM_ValueChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;
            
            NumericUpDown control = (NumericUpDown)sender;
            // If the value changed from the mouse wheel or arrow buttons, round to nearest increment
            if (!_num_changed_manually)
            {
                // Round to increment
                decimal rounded = Math.Round(control.Value / control.Increment) * control.Increment;

                // clamp to the min/max
                if (rounded > control.Maximum) rounded = control.Maximum;
                if (rounded < control.Minimum) rounded = control.Minimum;

                // Set the value
                freeze_handlers = true;
                control.Value = rounded;
                freeze_handlers = false;
                
            }

            // Clear the change-type flag
            _num_changed_manually = false;

            // If the value is different than the backup value, set the background color to green
            if(Math.Round(control.Value, control.DecimalPlaces) != Math.Round(value_backups[control.Name], control.DecimalPlaces))
            {
                // Use a color that contrasts with the text color
                control.BackColor = ThemeManager.ControlBGColor.GetBrightness() < 0.5 ? Color.DarkGreen : Color.LightGreen;
                // Display tooltip explaining how to discard changes
                toolTip1.SetToolTip(control, "Hit escape to cancel changes");
            }
            else
            {
                control.BackColor = ThemeManager.ControlBGColor;
                toolTip1.SetToolTip(control, null);
            }
        }

        private void CMB_altframe_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;

            ComboBox control = (ComboBox)sender;

            // If the value is different than the backup value, set the background color to green
            if ((byte)control.SelectedValue != value_backups[control.Name])
            {
                // Use a color that contrasts with the text color
                control.BackColor = ThemeManager.ControlBGColor.GetBrightness() < 0.5 ? Color.DarkGreen : Color.LightGreen;
                // Display tooltip explaining how to discard changes
                toolTip1.SetToolTip(control, "Hit escape to cancel changes");
            }
            else
            {
                control.BackColor = ThemeManager.ControlBGColor;
                toolTip1.SetToolTip(control, null);
            }
        }

        private void ActionsControl_VisibleChanged(object sender, EventArgs e)
        {
            ParamListChanged(null, null);
        }

        private void CHK_loitdirection_CheckedChanged(object sender, EventArgs e)
        {
            if (freeze_handlers) return;

            if (CHK_loitdirection.Checked == (value_backups["CHK_loitdirection"] < 0))
            {
                CHK_loitdirection.BackColor = Color.Transparent;
            }
            else
            {
                CHK_loitdirection.BackColor = ThemeManager.BGColor.GetBrightness() < 0.5 ? Color.DarkGreen : Color.LightGreen;
            }
        }

        private void BUT_guidedalt_Click(object sender, EventArgs e)
        {
            Host.config["guided_alt"] = NUM_guidedalt.Value.ToString();
            Host.config["guided_alt_frame"] = CMB_altframe.SelectedValue.ToString();

            value_backups[NUM_guidedalt.Name] = NUM_guidedalt.Value;
            value_backups[CMB_altframe.Name] = (byte)CMB_altframe.SelectedValue;
            NUM_guidedalt.BackColor = ThemeManager.ControlBGColor;
            CMB_altframe.BackColor = ThemeManager.ControlBGColor;

            Host.comPort.MAV.GuidedMode.z = (float)NUM_guidedalt.Value / CurrentState.multiplieralt;
            Host.comPort.MAV.GuidedMode.frame = (byte)CMB_altframe.SelectedValue;

            // "0" has special meaning. We need to use 0.001 instead.
            if (Host.comPort.MAV.GuidedMode.z == 0)
            {
                Host.comPort.MAV.GuidedMode.z = 0.001f;
            }

            if (Host.comPort.MAV.cs.mode == "Guided")
            {
                Host.comPort.setGuidedModeWP(new Locationwp
                {
                    alt = Host.comPort.MAV.GuidedMode.z,
                    lat = Host.comPort.MAV.GuidedMode.x / 1e7,
                    lng = Host.comPort.MAV.GuidedMode.y / 1e7,
                    frame = Host.comPort.MAV.GuidedMode.frame
                });
            }
        }

        private void BUT_loitradius_Click(object sender, EventArgs e)
        {
            int sign = CHK_loitdirection.Checked ? -1 : 1;
            double radius = sign * CurrentState.fromDistDisplayUnit((double)NUM_loitradius.Value);

            // Handle background color stuff for direction checkbox
            value_backups[CHK_loitdirection.Name] = CHK_loitdirection.Checked ? -1 : 1;
            CHK_loitdirection.BackColor = Color.Transparent;

            UpdateParameter(NUM_loitradius, "WP_LOITER_RAD", radius, "Failed to set loiter radius");

        }

        private void BUT_airspeed_Click(object sender, EventArgs e)
        {
            double airspeed = CurrentState.fromSpeedDisplayUnit((double)NUM_airspeed.Value);

            UpdateParameter(NUM_airspeed, "AIRSPEED_CRUISE", airspeed, "Failed to set airspeed");
        }

        private void BUT_mode_Click(object sender, EventArgs e)
        {
            byte sysid = (byte)Host.comPort.sysidcurrent;
            byte compid = (byte)Host.comPort.compidcurrent;
            try
            {
                ((Control)sender).Enabled = false;
                MainV2.comPort.setMode(sysid, compid, ((Control)sender).Text);
            }
            catch
            {
                CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
            }

            ((Control)sender).Enabled = true;
        }

        private void BUT_setwp_Click(object sender, EventArgs e)
        {
            byte sysid = (byte)Host.comPort.sysidcurrent;
            byte compid = (byte)Host.comPort.compidcurrent;
            
            // Get the number of waypoints in the currently loaded mission
            int wpCount = Host.comPort.MAV.wps.Count - 1;
            string wpno_str = Host.cs.wpno.ToString();

            // Launch selection dialog and get resulting selected waypoint number
            MissionPlanner.Controls.InputBox.Show("Enter waypoint number", "Set Waypoint", ref wpno_str);

            if(!ushort.TryParse(wpno_str, out ushort wpno))
            {
                CustomMessageBox.Show("Invalid number format");
                return;
            }

            if (wpno > wpCount)
            {
                CustomMessageBox.Show("Waypoint number is greater than the number of waypoints in the mission", Strings.ERROR);
                return;
            }

            // Send the command to the autopilot
            try
            {
                Host.comPort.setWPCurrent(sysid, compid, wpno);
            }
            catch
            {
                CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
            }
        }
    }

}
