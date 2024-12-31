using System;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Controls;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;



namespace SimControl
{
    public partial class SimControlTab : UserControl
    {
        private readonly PluginHost Host;

        public bool run_once = true;

        private readonly Timer btn_timer = new Timer();
        public SimControlTab(PluginHost Host)
        {
            this.Host = Host;

            InitializeComponent();

            btn_timer.Tick += btn_timer_Tick;

            var num_servos = 16;

            if (Host.comPort.MAV.param.ContainsKey("SIM_ENGINE_FAIL"))
            {
                foreach (var i in Enumerable.Range(1, num_servos))
                {
                    setup(i);
                }

            }
        }
        int last_msg_time;
        private void btn_timer_Tick(object sender, EventArgs e)
        {
            if (!this.Visible)
            {
                btn_timer.Stop();
                return;
            }

            var messagetime = Host.comPort.MAV.cs.messages.LastOrDefault().time;
            if (last_msg_time != messagetime.toUnixTime())
            {
                try
                {
                    var num_servos = 16;

                    if (Host.comPort.MAV.param.ContainsKey("SIM_ENGINE_FAIL"))
                    {

                        if (run_once)
                        {
                            foreach (var i in Enumerable.Range(1, num_servos))
                            {
                                setup(i);
                            }

                        }
                        run_once = false;

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

        }
        private void SimControlTab_VisibleChanged(object sender, EventArgs e)
        {
            btn_timer.Enabled = true;
            btn_timer.Interval = 200;
            btn_timer.Start();
        }
        private void setup(int servono)
        {

            var servo = String.Format("SERVO{0}", servono);
            
            string paramname = servo + "_FUNCTION";


            try
            {
                MAVLink.MAVLinkParamList paramlist = MainV2.comPort.MAV.param;
                var functions = ParameterMetaDataRepository.GetParameterOptionsInt(paramname,
                        MainV2.comPort.MAV.cs.firmware.ToString());

                string func = "";

                var value = paramlist[paramname].Value;
                foreach (var f in functions)
                {
                    if (f.Key == value)
                    {
                        func = f.Value;
                        break;
                    }
                }


                if (value > 0)
                {
                    MyButton b = new MyButton()
                    { Enabled = true, Dock = DockStyle.Fill, Text = func.ToString() };
                    b.Tag = servono;
                    b.Click += MotorFail;
                    this.tableLayoutPanel1.Controls.Add(b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }



        private void UpdateParameter(string param_name, double value, string fail_message)
        {
            // Update the specified parameter in the aircraft
            byte sysid = (byte)Host.comPort.sysidcurrent;
            byte compid = (byte)Host.comPort.compidcurrent;
            bool result = Host.comPort.setParam(sysid, compid, param_name, value);
            if (result)
            {

            }
            else
            {
                CustomMessageBox.Show(fail_message);
            }
        }

        bool fail = false;
        
        private void MotorFail(object sender, EventArgs e)
        {
            var buttonPressed = sender as MyButton;
            int number = (int)buttonPressed.Tag -1;


            if (fail)
            {
                UpdateParameter("SIM_ENGINE_FAIL",number, "fail to change param");
                UpdateParameter("SIM_ENGINE_MUL", 0, "fail to change param");
                //check if number in list of buttons to grey out the rest
                foreach (MyButton button in tableLayoutPanel1.Controls) { button.Enabled = false; }
                buttonPressed.Enabled = true;
            }
            else
            {
                UpdateParameter("SIM_ENGINE_MUL", 1, "fail to change param");
                foreach (MyButton button in tableLayoutPanel1.Controls) { button.Enabled = true; }
            }
            fail = !fail;
        }

        bool freezeHandler = false;
        private void myTrackBar1_Scroll(object sender, EventArgs e)
        {
            var trackBar = sender as MyTrackBar;
            if (freezeHandler) return;
            freezeHandler = true;
            numericUpDown1.Value = (decimal)TrackWindSpd.Value;
            freezeHandler = false;
         }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            UpdateParameter("SIM_WIND_SPD", (double)numericUpDown1.Value, "No Sim Running");
            if (freezeHandler) return;
            freezeHandler = true;
            TrackWindSpd.Value = (float)numericUpDown1.Value;
            freezeHandler = false;
        }

        bool freezeHandler2 = false;
        

        private void myTrackBar2_Scroll(object sender, EventArgs e)
        {
            var trackBar = sender as MyTrackBar;
            if (freezeHandler2) return;
            freezeHandler2 = true;
            numericUpDown2.Value = (decimal)TrackWindDir.Value;
            freezeHandler2 = false;
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            UpdateParameter("SIM_WIND_DIR", (double)numericUpDown2.Value, "No Sim Running");
            if (freezeHandler2) return;
            freezeHandler2 = true;
            TrackWindDir.Value = (float)numericUpDown2.Value;
            freezeHandler2 = false;
        }

        private void myTrackBar3_Scroll(object sender, EventArgs e)
        {
            label6.Text = (string.Format)("{0}x",myTrackBar3.Value);
            UpdateParameter("SIM_SPEEDUP", (double)myTrackBar3.Value, "No Simulation Vehicl Found");
        }
    }
}