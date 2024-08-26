using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MissionPlanner.Utilities;
using MissionPlanner.Controls;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using MissionPlanner;
using System.Drawing;

// this example taken from https://discuss.ardupilot.org/t/adding-mission-parts-at-the-beginning-and-end-of-new-mission/56579/12
// modified for this sample

namespace Shortcuts
{
    public class Plugin : MissionPlanner.Plugin.Plugin
    {
        ToolStripMenuItem but;
		MissionPlanner.Controls.MyDataGridView commands;

        public override string Name
        {
            get { return "Small stuff"; }
        }

        public override string Version
        {
            get { return "0.10"; }
        }

        public override string Author
        {
            get { return "EOSBandi"; }
        }

        public override bool Init()
        {
            return true;
        }

        public override bool Loaded()
        {
            but = new ToolStripMenuItem("Test the Mission");
            but.Click += but_Click;
            ToolStripItemCollection col = Host.FPMenuMap.Items;
            col.Add(but);
            commands =
                Host.MainForm.FlightPlanner.Controls.Find("Commands", true).FirstOrDefault() as
                    MissionPlanner.Controls.MyDataGridView;
            return true;
        }

        public override bool Loop()
        {
            return true;
        }

        public override bool Exit()
        {
            return true;
        }

        void but_Click(object sender, EventArgs e)
        {
            CustomMessageBox.Show("Successful Right-click option");

            string angle = "0";
            InputBox.Show("Load Mission to be validated: ", "", ref angle);

            // Mavlink commands for reference from documentation - https://mavlink.io/en/messages/common.html
            //MAV_CMD_DO_LAND_START(189)
            //MAV_CMD_NAV_VTOL_TAKEOFF (84)
            //MAV_CMD_NAV_VTOL_LAND(85)

            // Some sample mission waypoints
        }
    }
}