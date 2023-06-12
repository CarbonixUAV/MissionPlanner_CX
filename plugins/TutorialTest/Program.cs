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
using System.Threading.Tasks;
using log4net;
using static MAVLink;

namespace TutorialTest
{
    public class Program : MissionPlanner.Plugin.Plugin
    {
        private string _Name = "Loki test 2 ";
        private string _Version = "0.2";
        private string _Author = "Lokesh";
        public override string Name { get { return _Name; } }
        public override string Version { get { return _Version; } }
        public override string Author { get { return _Author; } }

        public override bool Init()
        {
            //CustomMessageBox.Show("Hello World");
            return false;
        }

        public override bool Loaded()
        {
            using (Form tempForm = new Form2())
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