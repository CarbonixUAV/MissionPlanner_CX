using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using log4net;
using MissionPlanner;
using MissionPlanner.Controls;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;
using Xamarin.Essentials;

namespace Carbonix
{
    public partial class TakeoffTab : UserControl
    {
        private readonly PluginHost Host;

        //Records tab can arm check
        public bool CanArm = false;

        volatile int updateBindingSourcecount;
        DateTime lastscreenupdate = DateTime.Now;
        readonly object updateBindingSourcelock = new object();

        private readonly Timer bindingSourceTimer = new Timer();
        private readonly Timer messageBoxTimer = new Timer();
        private readonly Timer finalButtonTimer = new Timer();

        HashSet<int> final_wps = new HashSet<int>();

        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public TakeoffTab(PluginHost Host, AircraftSettings aircraft_settings)
        {
            this.Host = Host;

            InitializeComponent();

            bindingSourceTimer.Tick += bindingSourceTimer_Tick;
            messageBoxTimer.Tick += messageBoxTimer_Tick;
            finalButtonTimer.Tick += finalButtonTimer_Tick;

            Color nvColor = ThemeManager.TextColor.WithAlpha(255);
            foreach (var color in ThemeManager.thmColor.colors)
            {
                if (color.strVariableName == "NVColor")
                {
                    nvColor = color.clrColor;
                    break;
                }
            }

            numberView1.numberColor = nvColor;
            numberView1.numberColorBackup = nvColor;
            numberView1.numberformat = aircraft_settings.takeofftab_displays[0].numberformat;
            numberView1.desc = aircraft_settings.takeofftab_displays[0].description;
            numberView1.charWidth = aircraft_settings.takeofftab_displays[0].charwidth;
            AddBinding(numberView1, aircraft_settings.takeofftab_displays[0].variable);

            numberView2.numberColor = nvColor;
            numberView2.numberColorBackup = nvColor;
            numberView2.numberformat = aircraft_settings.takeofftab_displays[1].numberformat;
            numberView2.desc = aircraft_settings.takeofftab_displays[1].description;
            numberView2.charWidth = aircraft_settings.takeofftab_displays[1].charwidth;
            AddBinding(numberView2, aircraft_settings.takeofftab_displays[1].variable);

            numberView3.numberColor = nvColor;
            numberView3.numberColorBackup = nvColor;
            numberView3.numberformat = aircraft_settings.takeofftab_displays[2].numberformat;
            numberView3.desc = aircraft_settings.takeofftab_displays[2].description;
            numberView3.charWidth = aircraft_settings.takeofftab_displays[2].charwidth;
            AddBinding(numberView3, aircraft_settings.takeofftab_displays[2].variable);

            numberView4.numberColor = nvColor;
            numberView4.numberColorBackup = nvColor;
            numberView4.numberformat = aircraft_settings.takeofftab_displays[3].numberformat;
            numberView4.desc = aircraft_settings.takeofftab_displays[3].description;
            numberView4.charWidth = aircraft_settings.takeofftab_displays[3].charwidth;
            AddBinding(numberView4, aircraft_settings.takeofftab_displays[3].variable);

            // Trigger table_numberViews handler
            table_numberViews_Resize(null, null);

        }

        private void AddBinding(NumberView nv, string name)
        {
            // Check if "name" is an existing property in bindingSource1
            if (Host.cs.GetType().GetProperty(name) != null)
            {
                // Add binding
                nv.DataBindings.Add("number", bindingSource1, name);
                return;
            }
            // Otherwise check if any customfields match
            var field = CurrentState.custom_field_names.FirstOrDefault(x => x.Value == name).Key;
            if (field == null)
            {
                // Reserve the first available customfield
                for (int i = 0; i < 20; i++)
                {
                    field = $"customfield{i}";
                    if (!CurrentState.custom_field_names.ContainsKey(field))
                    {
                        CurrentState.custom_field_names[$"customfield{i}"] = name;
                        break;
                    }
                }

            }
            // Add binding
            nv.DataBindings.Add("number", bindingSource1, field);
            return;
        }

        private void bindingSourceTimer_Tick(object sender, EventArgs e)
        {
            if(!this.Visible)
            {
                bindingSourceTimer.Stop();
                return;
            }
            // update all linked controls - 20hz
            updateBindingSource();
        }

        int last_msg_time;
        private void messageBoxTimer_Tick(object sender, EventArgs e)
        {
            if (!this.Visible)
            {
                messageBoxTimer.Stop();
                return;
            }

            var messagetime = Host.comPort.MAV.cs.messages.LastOrDefault().time;
            if (last_msg_time != messagetime.toUnixTime())
            {
                try
                {
                    StringBuilder message = new StringBuilder();
                    Host.comPort.MAV.cs.messages.ForEach(x =>
                    {
                        message.Insert(0, x.time + " : " + x.message + "\r\n");
                    });
                    txt_messagebox.Text = message.ToString();

                    last_msg_time = messagetime.toUnixTime();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            // Disable preflight cal button when armed
            but_calibrate.Enabled = !Host.cs.armed;

            // Disable the safety switch button when armed
            but_safety.Enabled = !Host.cs.armed;

            // Change the text of the safety button toggle based on safety state
            but_safety.Text = Host.cs.safetyactive ? "Disable Safety" : "Engage Safety";
    
            // Disable the manual mode button if we are likely flying
            but_manual.Enabled = !(Host.cs.armed && (CurrentState.fromSpeedDisplayUnit(Host.cs.groundspeed) > 3 || Host.cs.ch3percent > 12));


            //Disable the Reboot Button When armed
            but_reboot.Enabled = !Host.cs.armed;

            //Disable Engine Start stop Control with safety on and also when flying, allow in VTOL climb and descents
            but_engControl.Enabled = !(Host.cs.safetyactive || CurrentState.fromSpeedDisplayUnit(Host.cs.groundspeed) > 4 || Host.cs.sonarrange > 30);
        }
        
        private void finalButtonTimer_Tick(object sender, EventArgs e)
        {
            if (!this.Visible)
            {
                finalButtonTimer.Stop();
                return;
            }

            var wps = Host.comPort.MAV.wps;

            var final_wps = new HashSet<int>();

            // This flag tracks whether we are searching for a VTOL_LAND or a final marker
            bool looking_for_land = false;
            // Loop backwards through the waypoints
            for(int i = wps.Count - 1; i > 1; i--)
            {
                // Look for VTOL_LAND
                if (wps[i].command == (ushort)MAVLink.MAV_CMD.VTOL_LAND)
                {
                    looking_for_land = false;
                    continue;
                }

                if (looking_for_land)
                {
                    continue;
                }

                // The final leg is marked by a DO_LAND_START with a very high altitude, or by a zero-turn
                // loiter. The latter is the old way of doing it. I am including it in case someone loads
                // an old mission (I went back and forth on whether I should allow this).
                bool final_wp_candidate = false;
                if (wps[i].command == (ushort)MAVLink.MAV_CMD.DO_LAND_START && wps[i].z > 9000)
                {
                    final_wp_candidate = true;
                }
                else if (wps[i].command == (ushort)MAVLink.MAV_CMD.LOITER_TURNS && wps[i].param1 == 0)
                {
                    final_wp_candidate = true;
                }

                // Make sure the waypoint before is a non-zero-turn loiter
                if (final_wp_candidate)
                {
                    if (wps[i - 1].command == (ushort)MAVLink.MAV_CMD.LOITER_TURNS && wps[i - 1].param1 != 0)
                    {
                        final_wps.Add(i);
                        looking_for_land = true;
                        continue;
                    }
                }
            }

            this.final_wps = final_wps;
        }
        private void updateBindingSource()
        {
            lock (updateBindingSourcelock)
            {
                // this is an attempt to prevent an invoke queue on the binding update on slow machines
                if (updateBindingSourcecount > 0)
                {
                    if (lastscreenupdate < DateTime.Now.AddSeconds(-5))
                    {
                        updateBindingSourcecount = 0;
                    }

                    return;
                }

                updateBindingSourcecount++;
            }

            if (Disposing)
                return;

            this.BeginInvokeIfRequired(delegate
            {
                updateBindingSourceWork();

                lock (updateBindingSourcelock)
                {
                    updateBindingSourcecount--;
                }
            });
        }

        private void updateBindingSourceWork()
        {
            try
            {
                Host.comPort.MAV.cs.UpdateCurrentSettings(bindingSource1.UpdateDataSource(Host.comPort.MAV.cs));

                lastscreenupdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void table_numberViews_Resize(object sender, EventArgs e)
        {
            // Calculate which, 4x1, or 2x2, gives the better aspect ratio
            // and set the number of columns accordingly.
            int width = table_numberViews.Width;
            int height = table_numberViews.Height;
            int numCols = 2;
            int numRows = 2;
            if (width / 2.5 > height)
            {
                numCols = 4;
                numRows = 1;
            }
            table_numberViews.ColumnCount = numCols;
            table_numberViews.RowCount = numRows;
        }

        private void TakeoffTab_VisibleChanged(object sender, EventArgs e)
        {
            bindingSourceTimer.Enabled = true;
            bindingSourceTimer.Interval = 50;
            bindingSourceTimer.Start();

            messageBoxTimer.Enabled = true;
            messageBoxTimer.Interval = 200;
            messageBoxTimer.Start();

            finalButtonTimer.Enabled = true;
            finalButtonTimer.Interval = 5000;
            finalButtonTimer.Start();
        }
        
        private void but_arm_Click(object sender, EventArgs e)
        {
            if (!Host.comPort.BaseStream.IsOpen) return;

            var isitarmed = Host.comPort.MAV.cs.armed;
            //Check if user has completed the Records Tab only check when disarmed to avoid disabling disarm on edge case
            if (!CanArm && !isitarmed)
            {
                CustomMessageBox.Show("Please complete the Records Tab");
                return;
            }

            // arm the MAV
            try
            {
                var action = Host.comPort.MAV.cs.armed ? "Disarm" : "Arm";

                if (isitarmed)
                    if (CustomMessageBox.Show("Are you sure you want to " + action, action,
                            CustomMessageBox.MessageBoxButtons.YesNo) !=
                        CustomMessageBox.DialogResult.Yes)
                        return;
                StringBuilder sb = new StringBuilder();
                var sub = Host.comPort.SubscribeToPacketType(MAVLink.MAVLINK_MSG_ID.STATUSTEXT, message =>
                {
                    sb.AppendLine(Encoding.ASCII.GetString(((MAVLink.mavlink_statustext_t)message.data).text)
                        .TrimEnd('\0'));
                    return true;
                }, (byte)Host.comPort.sysidcurrent, (byte)Host.comPort.compidcurrent);
                bool ans = Host.comPort.doARM(!isitarmed);
                if(ans == false)
                {
                    // Sleep 0.25 second to allow the error message to come through
                    System.Threading.Thread.Sleep(250);
                }
                Host.comPort.UnSubscribeToPacketType(sub);
                if (ans == false)
                {
                    if (isitarmed)
                    {
                        if (CustomMessageBox.Show(
                            "Disarm failed.\n" + sb.ToString() + "\nForce Disarm? (not recommended)", Strings.ERROR, 
                            CustomMessageBox.MessageBoxButtons.YesNo, CustomMessageBox.MessageBoxIcon.Exclamation,
                            "Force Disarm", "Cancel") == CustomMessageBox.DialogResult.Yes)
                        {
                            ans = Host.comPort.doARM(!isitarmed, true);
                            if (ans == false)
                            {
                                CustomMessageBox.Show(Strings.ErrorRejectedByMAV, Strings.ERROR);
                            }
                        }
                    }
                    else
                    {
                        CustomMessageBox.Show("Arm failed.\n" + sb.ToString(), Strings.ERROR);
                    }
                    
                }
            }
            catch
            {
                CustomMessageBox.Show(Strings.ErrorNoResponce, Strings.ERROR);
            }

        }

        private void but_manual_Click(object sender, EventArgs e)
        {
            if (!Host.comPort.BaseStream.IsOpen) return;

            byte sysid = (byte)Host.comPort.sysidcurrent;
            byte compid = (byte)Host.comPort.compidcurrent;
            try
            {
                ((Control)sender).Enabled = false;
                MainV2.comPort.setMode(sysid, compid, "Manual");
            }
            catch
            {
                CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
            }

            ((Control)sender).Enabled = true;
        }

        private void but_calibrate_Click(object sender, EventArgs e)
        {
            if (!Host.comPort.BaseStream.IsOpen) return;

            if (CustomMessageBox.Show("Calibrate baro and airspeed?", "Action", MessageBoxButtons.YesNo)
                    == (int)DialogResult.Yes)
            {
                try
                {
                    // Param 3 corresponds to baro/airspeed
                    if (MainV2.comPort.doCommand(MAVLink.MAV_CMD.PREFLIGHT_CALIBRATION, 0, 0, 1, 0, 0, 0, 0))
                    {

                    }
                    else
                    {
                        CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
                    }
                }
                catch
                {
                    CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
                }
            }
        }

        private void but_safety_Click(object sender, EventArgs e)
        {
            // Do not allow the user to enable safety while armed
            // (even though we disable the button, this is a good check)
            if (Host.cs.armed)
                return;

            // Disable the button while we send the message
            ((Control)sender).Enabled = false;

            if (!Host.cs.safetyactive)
            {
                SendEngineCommand(false);
            }
            set_safety_switch(!Host.cs.safetyactive);
        }

        private void set_safety_switch(bool enable)
        {
            // Setting custom_mode to 1 forces the safety state to ON, setting to zero forces it OFF
            var custom_mode = enable ? 1u : 0u;
            var mode = new MAVLink.mavlink_set_mode_t() { custom_mode = custom_mode, target_system = (byte)Host.comPort.sysidcurrent };
            MainV2.comPort.setMode(mode, MAVLink.MAV_MODE_FLAG.SAFETY_ARMED);
        }

        //reboot the flight conroller
        private void but_reboot_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("Are you sure you want to Reboot?", "Reboot",
        MessageBoxButtons.YesNo) == (int)DialogResult.Yes)
            {
                try
                {
                    // Disable the button while we send the message
                    ((Control)sender).Enabled = false;
                    MainV2.comPort.doReboot();
                }
                catch
                {
                    CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
                }
                //renable the button
                ((Control)sender).Enabled = true;
            }
        }

        //One button to start and stop the engine. 
        private void but_engControl_Click(object sender, EventArgs e)
        {
            var result = CustomMessageBox.Show("", "Engine Control", CustomMessageBox.MessageBoxButtons.YesNo, 0, "Start Engine", "Kill Engine");
            if ((int)result == (int)DialogResult.Yes)
            {
                SendEngineCommand(true);
            }
            else if ((int)result == (int)DialogResult.No) 
            {
                SendEngineCommand(false);
            }


        }

        private void SendEngineCommand(bool startEngine)
        {
            Host.comPort.doCommand(MAVLink.MAV_CMD.DO_ENGINE_CONTROL, startEngine ? 1 : 0, 0, 0, 0, 0, 0, 0);
        }
    }
}
