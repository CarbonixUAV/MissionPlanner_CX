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
            // i) Add in right-click option for validator test to appear as "Test the Mission" item
            but = new ToolStripMenuItem("Test the Mission");

            // If this option is clicked
            but.Click += but_Click;

            // Option should appear in the FPMenuMap (the 2nd tab - Flight Planner page) at bottom of existing item list
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

<<<<<<< HEAD
=======
        // List of waypoint commands to analyse
        //private IEnumerable<IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>>> list;
        //private int listhash;

        // When the "Test the Mission" option is selected in the Flight Planner page,
        // the three waypoints (vtol takeoff, do land start, vtol land) should appear as waypoints
        // to start to play around with how a plugin option can 'affect' the mission planning page,
        // intention is to then 'verify' the mission is ok logically as the basic MVP check
>>>>>>> 043e9c3e4 (Validator: Finetune waypoint additions and add basic browse)
        void but_Click(object sender, EventArgs e)
        {

            // Initial GUI testing
            //string angle = "0";
            //InputBox.Show("Load Mission to be validated - v1.3: ", "", ref angle);

            // Basic pop up file browse for user-created mission file 
            OpenFileDialog ofd = new OpenFileDialog();
            //.ofd.Filter = "waypoint file|*.waypoint"; // Filter for only .waypoint files
            ofd.ShowDialog();

            // Line 90 does not physically load that waypoint file yet. Still using the hardcoded waypoints
            // below for the logic analysis

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                CustomMessageBox.Show("Waypoint Mission File found (do nothing)");
            }

            // Mavlink commands for reference from documentation - https://mavlink.io/en/messages/common.html
<<<<<<< HEAD
            //MAV_CMD_DO_LAND_START(189)
            //MAV_CMD_NAV_VTOL_TAKEOFF (84)
            //MAV_CMD_NAV_VTOL_LAND(85)

            // Some sample mission waypoints
=======
            // Some sample mission waypoints (around CX Office)
            Host.InsertWP(0, MAVLink.MAV_CMD.VTOL_TAKEOFF, 0, 90, 0, 0, 151.1820459, -33.8159426, 40.000000);
            Host.InsertWP(1, MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, 151.1802243, -33.8154884, 20.000000);
            Host.InsertWP(2, MAVLink.MAV_CMD.DO_LAND_START, 0, 90, 0, 0, 151.1802220, -33.8154880, 35.000000);
            Host.InsertWP(3, MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, 151.1802220, -33.8154884, 40.000000);
            Host.InsertWP(4, MAVLink.MAV_CMD.VTOL_LAND, 0, 90, 0, 0, 151.1827433, -33.8144451, 40.000000);
            
            // Analyse the provided 5 x waypoints command list...
            // TO DO
  
>>>>>>> 043e9c3e4 (Validator: Finetune waypoint additions and add basic browse)
        }

        // Pop up GUI to browse for a file
        /*
        public bool SetupUI(int gui = 0)
        {
            if (gui == 0)
            {
                Plugin.FilePick form = new Plugin.FilePick();
                form.Show();
            }

            return true;
        }
        */
    }
}