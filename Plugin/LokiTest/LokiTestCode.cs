using System;
using MissionPlanner;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;
using System.Windows.Forms;
using MissionPlanner.Controls;
using System.Linq;
using GMap.NET.MapProviders;
using GMap.NET;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Reflection;
using MissionPlanner.Joystick;
using MissionPlanner.ArduPilot;
using MissionPlanner.ArduPilot.Mavlink;
using System.Threading.Tasks;
using log4net;
using static MAVLink;

namespace LokiTest
{
    public class LokiTestCode : MissionPlanner.Plugin.Plugin
    {
        private string _Name = "Loki test";
        private string _Version = "0.2";
        private string _Author = "Lokesh";

        public override string Name { get { return _Name; } }
        public override string Version { get { return _Version; } }
        public override string Author { get { return _Author; } }

        public override bool Init() 
        {
            CustomMessageBox.Show("Hello World");
            return true; 
        
        }
        public override bool Loaded()
        {
            using (Form tempForm = new MainForm())
            {
                ThemeManager.ApplyThemeTo(tempForm);
                tempForm.ShowDialog();
            }
            return true;
        }
        public override bool Loop() { return true; }


        public override bool Exit() { return true; }
    }
}