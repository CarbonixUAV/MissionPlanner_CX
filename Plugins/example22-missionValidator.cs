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
using static MAVLink;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MissionValidator
{
    public class MissionValidator : MissionPlanner.Plugin.Plugin
    {
        ToolStripMenuItem but;
        MissionPlanner.Controls.MyDataGridView commands;

        IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>> waypoint_list;

        // Create a new string variable for the end page to say why it fails
        //string whyItFails;

        // Dictionary to hold the missionResult descriptors
        Dictionary<int, string> missionResult = new Dictionary<int, string>()
        {
            {1, "Valid VTOL Takeoff"},
            {2, "Invalid VTOL Takeoff"},
            {3, "Missing VTOL Takeoff"},
            {4, "Valid VTOL Land"},
            {5, "Invalid VTOL Land"},
            {6, "Missing VTOL Land"},
        };

        public override string Name
        {
            get { return "Carbonix Mission Validator Plugin"; }
        }

        public override string Version
        {
            get { return "0.10"; }
        }

        public override string Author
        {
            get { return "Carbonix"; }
        }

        public override bool Init()
        {
            return true;
        }

        public override bool Loaded()
        {
            //Unused for now
            but = new ToolStripMenuItem("Test the Mission");

            ToolStripItemCollection col = Host.FPMenuMap.Items;
            col.Add(but);
            commands =
                Host.MainForm.FlightPlanner.Controls.Find("Commands", true).FirstOrDefault() as
                    MissionPlanner.Controls.MyDataGridView;

            //Button on Flight Planner page 
            var button = new MissionPlanner.Controls.MyButton();
            button.Text = "Run Validator";
            
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
            // note - requires serial connection to autopilot to check
            // the current waypoint file stored in flight controller
            bool isMissionValid = checkMission(MainV2.comPort.MAV);

            if (isMissionValid)
            {
                CustomMessageBox.Show("Mission - PASSES: ");
                // Add explanation of why mission passed
            }
            else
            {
                CustomMessageBox.Show("Mission - FAILS: ");
                // Add explanation of why mission failed
                //CustomMessageBox.Show("Mission - FAILS: " + whyItFails);
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
            bool validTakeoff = false; // Adding a flag to help check if VTOL_TAKEOFF command is the 'first' waypoint
            bool validLand = false;
            int waypointCount = commands.Rows.Count;

            // The .Where query filters the autopilot's current loaded mission to only collect the VTOL_TAKEOFF, VTOL_LAND and DO_LAND_START points
            // to make this check time-efficient?
            var takeofflanding_wps = wp_list
                   .Where(a => a.Value.command == (ushort)MAVLink.MAV_CMD.DO_LAND_START ||
                          a.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF ||
                          a.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_LAND).ToList();

            // Iterating over the filtered waypoint command list. 
            // var sublist is an implicit type for the keyvalue pair of "int" and "mavlink mission message item"
            foreach (var sublist in takeofflanding_wps) 
            {

                // VTOL Takeoff Conditions
                if (sublist.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF)
                {
                    if (sublist.Key == 1)
                    {
                        CustomMessageBox.Show("Valid vtol takeoff"); // TAKEOFF is 'first' WP
                        validTakeoff = true;
                    }
                    else
                    {
                        takeoffExists = true;
                        CustomMessageBox.Show("VTOL takeoff is not 1st waypoint"); // takeoff is present but is not the first waypoint
                    }                    
                }

                // VTOL Land Conditions
                if (sublist.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_LAND)
                {
                    if (sublist.Key == waypointCount && sublist.Value.z == 0)
                    {
                        validLand = true;
                        CustomMessageBox.Show("Valid vtol land"); // LAND is 'last' WP and at 0m altitude
                    }
                    else
                    {
                        landExists = true;
                        CustomMessageBox.Show("VTOL land is not last waypoint or altitude > 0m"); // either not the last waypoint or altitude incorrect                        
                    }                     
                }

                // DO_LAND_START Conditions
                // TO DO
            }

            // if takeoffExists still false at end of all waypoints - missing takeoff waypoint
            // if landExists still false at end of all waypoints - missing vtol_land waypoint

            return (validLand && validTakeoff);
        }
    }
}