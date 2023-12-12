using System;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Plugin;


namespace CX_Silvus
{
    public class Silvus_Plugin : Plugin
    {
        public override string Name { get; } = "Carbonix Silvus Plugin";
        public override string Version { get; } = "1.0";
        public override string Author { get; } = "Carbonix";

        public override bool Init()
        {
            loopratehz = 1;
            return true;
        }

        public override bool Loaded()
        {
            ToolStripButton menutool = new ToolStripButton("Silvus");
            menutool.Click += menuItem1_Click;
            MainV2.instance.MainMenu.Items.Add(menutool);
            menutool.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;

            return true;
        }

        public override bool Loop()
        {
            // add menu item on main mission planner Main menubar next to MenuArduPilot Menu
            ToolStripButton menutool = new ToolStripButton("Silvus");
            menutool.Click += menuItem1_Click;
            MainV2.instance.MainMenu.Items.Add(menutool);



            // MainV2.instance.MenuArduPilot .MainMenuMap.Items.Add(menutool);
            // MainV2.instance. = menutool;
            // MainV2.instance.Add(menutool);

            return true;
        }

        private void menuItem1_Click(object sender, EventArgs e)
        {
            // Load new form for viewing silvus radio information
            Silvus_Form silvusForm = new Silvus_Form();
            silvusForm.Show();

        }

        public override bool Exit() { return true; }
    }
}
