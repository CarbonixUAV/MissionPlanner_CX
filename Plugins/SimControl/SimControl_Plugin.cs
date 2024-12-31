using MissionPlanner.Plugin;
using System.Windows.Forms;


namespace SimControl
{
    public class SimControl_Plugin : Plugin
    {
        public override string Name { get; } = "SimControl";
        public override string Version { get; } = "0.1";
        public override string Author { get; } = "Lachlan Conn";

        public override bool Init() { return true; }
        public override bool Loaded()
        {
            LoadTabs();

            loopratehz = 1;
            return true;
        }
        public override bool Exit() { return true; }

        private void LoadTabs()
        {
            // Add SimControl tab
            TabPage tabPageSimControl = new TabPage
            {
                Text = "Sim Control",
                Name = "tabSimControl"
            };
            SimControlTab tabSimControl = new SimControlTab(Host) { Dock = DockStyle.Fill };
            tabPageSimControl.Controls.Add(tabSimControl);
            Host.MainForm.FlightData.TabListOriginal.Insert(5, tabPageSimControl);

            // refilter the display list based on user selection
            Host.MainForm.FlightData.loadTabControlActions();
        }

    }

}
