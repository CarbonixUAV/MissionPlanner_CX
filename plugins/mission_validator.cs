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

// This example is currently modified to be used for Carbonix mission validator plugin tests

namespace Shortcuts
{
    public class Plugin : MissionPlanner.Plugin.Plugin
    {
        ToolStripMenuItem but;
		MissionPlanner.Controls.MyDataGridView commands;

        IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>> waypoint_list;

        // Create a new string variable for the end page to say why it fails
        string whyItFails;

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
            // i) Add in right-click option for validator test to appear as "Test the Mission" item (legacy)
            but = new ToolStripMenuItem("Test the Mission");

            // Option should appear in the FPMenuMap (the 2nd tab - Flight Planner page) at bottom of existing item list
            ToolStripItemCollection col = Host.FPMenuMap.Items;
            col.Add(but);
            commands =
                Host.MainForm.FlightPlanner.Controls.Find("Commands", true).FirstOrDefault() as
                    MissionPlanner.Controls.MyDataGridView;

            // ii) Button on Flight Planner page (preferred)
            var button = new MissionPlanner.Controls.MyButton();
            button.Text = "Run Validator";

            button.Click += (sender, e) =>
            {
                CustomMessageBox.Show("Hello from Mission Test Validator V1.5!!");
            };

            // Place button under existing left panel in Planner (from Mary)
            Host.MainForm.FlightPlanner.flowLayoutPanel1.Controls.Add(button);

            // If this "Run Validator" button option is clicked
            button.Click += but_Click;

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

        // When the "Run Validator" button is clicked in the Flight Planner page        
        void but_Click(object sender, EventArgs e)
        {  
            CustomMessageBox.Show("Mission check in Progress...");

            // note - requires serial connection to autopilot to check
            // the current waypoint file stored in flight controller
            bool isMissionValid = checkMission(MainV2.comPort.MAV);

            if (isMissionValid)
            {
                CustomMessageBox.Show("Mission - PASSES");
            }
            else
            {
                CustomMessageBox.Show("Mission - FAILS: " + whyItFails);
            }
                
        }

        // Analyse the proposed mission
        bool checkMission(MAVState MAV)
        {

            // initially set the mission validation status flag to false to be proven true if conditions met
            bool MissionStatus = false;

            waypoint_list = MAV.wps; //waypoint list to be queried

            // Initial test code adapted from example3-fencedist.cs
            // Add to series of mission checks here
            if (checkTakeoffLanding(waypoint_list)) MissionStatus = true; 

            return MissionStatus;
        }

        // Check Takeoff and Landing waypoint validity
        bool checkTakeoffLanding(IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>> wp_list)
        {

            bool takeoffExists = false;
            bool landExists = false;
            int waypointCount = commands.Rows.Count;

            // Adding a flag to help check if VTOL_TAKEOFF command exists
            bool takeoffFound = false;

            // The .Where query filters the autopilot's current loaded mission to only collect the VTOL_TAKEOFF, VTOL_LAND and DO_LAND_START points
            // to make this check time-efficient?
            var takeofflanding_wps = wp_list
                   .Where(a => a.Value.command == (ushort) MAVLink.MAV_CMD.DO_LAND_START ||
                          a.Value.command == (ushort) MAVLink.MAV_CMD.VTOL_TAKEOFF ||
                          a.Value.command == (ushort) MAVLink.MAV_CMD.VTOL_LAND).ToList();

            // Iterating over the filtered waypoint command list. 
            foreach (var sublist in takeofflanding_wps) //var sublist is an implicit type for the keyvalue pair of "int" and "mavlink mission message item"
            {
                // debug only - remove later
                if (sublist.Value.command == (ushort) MAVLink.MAV_CMD.VTOL_TAKEOFF)
                {
                    CustomMessageBox.Show("Waypoint Index: " + sublist.Key.ToString() +
                    " Found - Waypoint Command: VTOL TAKEOFF");
                }              
                else if (sublist.Value.command == (ushort) MAVLink.MAV_CMD.VTOL_LAND)
                {
                    CustomMessageBox.Show("Waypoint Index: " + sublist.Key.ToString() +
                    " Found - Waypoint Command: VTOL LAND");
                }
                // debug only - remove later

                //i) Check 'first' WP is VTOL_TAKEOFF
                if (sublist.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF && sublist.Key == 1)
                {
                    takeoffFound = true;
                    takeoffExists = true;
                }

                // check if VTOL_TAKEOFF is not there - when flag is still false
                else if (takeoffFound = false)
                {
                    takeoffExists = false;
                    whyItFails = "VTOL_TAKEOFF command does not exist in mission plan";
                }

                //check if VTOL_TAKEOFF is there but not at the first waypoint
                else if (sublist.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF && sublist.Key != 1)
                {
                    takeoffFound = true;
                    takeoffExists = false;
                    whyItFails = "VTOL_TAKEOFF command is not the first command";
                }

                //ii) Check 'last' WP is VTOL_LAND & at 0m altitude
                if (sublist.Value.command == (ushort) MAVLink.MAV_CMD.VTOL_LAND && (sublist.Key == waypointCount && sublist.Value.z == 0))
                {
                    landExists = true;
                }

                //iii) Check a DO_LAND_START is present
                // TO DO
            }

            return (takeoffExists && landExists);
        }
    }
}
