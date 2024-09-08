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
        Dictionary<string, bool> missionResult = new Dictionary<string, bool>()
        {
            {"Valid VTOL Takeoff", false},
            {"VTOL Takeoff is not 1st waypoint",false}, //note - probably a bit too wordy
            {"Missing VTOL Takeoff",false},
            {"Valid VTOL Land",false},
            {"VTOL Land is not last waypoint or altitude > 0m",false}, //note - probably a bit too wordy
            {"Missing VTOL Land",false},
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

            //reset flags in Dictionary - TO DO

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
           checkMission(MainV2.comPort.MAV);           

        }

        // Analyse the proposed mission
        void checkMission(MAVState MAV)
        {

            // initially set the mission validation status flags 
            bool takeoffPass = false;
            bool landPass = false;

            var missionDescriptionList = new List<string>();

            waypoint_list = MAV.wps; //waypoint list to be queried

            // Add to series of mission checks here, these modify the descriptors based on logic checks, as these
            // functions are passed in the full waypoint set currently stored in autopilot
            checkTakeoffLanding(waypoint_list);

            // Iterate through dictionary key-value pairs and check the descriptors to display
            // to determine the pass/fail score for the mission                       
            foreach (var resultCheck in missionResult)
            {
                //CustomMessageBox.Show($"{resultCheck.Key} = {resultCheck.Value}");
                if (resultCheck.Value == true)
                {
                    if (resultCheck.Key == "Valid VTOL Takeoff")
                    {
                        takeoffPass = true;
                        missionDescriptionList.Add(resultCheck.Key);
                    }
                    if (resultCheck.Key == "Valid VTOL Land")
                    {
                        landPass = true;
                        missionDescriptionList.Add(resultCheck.Key);
                    }                    
                    //any other condition being true is invalid waypoint or missing waypoint condition
                }                               
            }

            if (takeoffPass && landPass)
            {
                string message = "Mission - PASSES: " + System.Environment.NewLine + missionDescriptionList[0].ToString()
                    + System.Environment.NewLine + missionDescriptionList[1].ToString();
                CustomMessageBox.Show(message);
            }
            else
            {
                CustomMessageBox.Show("Mission - FAILS: ");
            }              
        }
        
        // Helper function to change the state of the mission condition dictionary elements
        void updateMissionResult(string missionResultDescription, bool wp_outcome)
        {
            missionResult[missionResultDescription] = wp_outcome;
        }

        // Check Takeoff and Landing waypoint validity
        void checkTakeoffLanding(IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>> wp_list)
        {

            bool takeoffExists = false;
            bool landExists = false;
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
                    takeoffExists = true;
                    if (sublist.Key == 1)
                    {
                        updateMissionResult("Valid VTOL Takeoff",true); // TAKEOFF is 'first' WP
                    }
                    else
                    {
                        updateMissionResult("VTOL Takeoff is not 1st waypoint", true); // takeoff is present but is not the first waypoint
                    }                    
                }

                // VTOL Land Conditions
                if (sublist.Value.command == (ushort)MAVLink.MAV_CMD.VTOL_LAND)
                {
                    landExists = true;
                    if (sublist.Key == waypointCount && sublist.Value.z == 0)
                    {
                        updateMissionResult("Valid VTOL Land", true); // LAND is 'last' WP and at 0m altitude
                    }
                    else
                    {   
                        updateMissionResult("VTOL Land is not last waypoint or altitude > 0m", true); // either not the last waypoint or altitude incorrect             
                    }                     
                }

                // DO_LAND_START Conditions
                // TO DO
            }

            // if takeoffExists still false at end of all waypoints - missing takeoff waypoint
            // if landExists still false at end of all waypoints - missing vtol_land waypoint
            if (takeoffExists == false)
            {
                updateMissionResult("Missing VTOL Takeoff", true);
            }

            if (landExists == false)
            {
                updateMissionResult("Missing VTOL Land", true);
            }
        }
    }
}