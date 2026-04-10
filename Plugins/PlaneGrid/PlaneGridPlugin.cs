using System;
using System.Windows.Forms;
using MissionPlanner.Plugin;

namespace PlaneGrid
{
    public class PlaneGridPlugin : Plugin
    {
        private string _Name = "Plane Grid";
        private string _Version = "1.0";
        private string _Author = "Lachlan Conn";

        public override string Name { get { return _Name; } }
        public override string Version { get { return _Version; } }
        public override string Author { get { return _Author; } }
        public override bool Init() { return true; }
        public override bool Loaded() 
        {
            AddToolStrip(); 
            return true; 
        }
        private void AddToolStrip()
        {
            var planeGridToolStripMenuItem = new ToolStripMenuItem("Plane Grid");
            planeGridToolStripMenuItem.Click += (o, e) =>
            {
                using (var planegridui = new PlaneGridUI(this))
                {
                    MissionPlanner.Utilities.ThemeManager.ApplyThemeTo(planegridui);

                    if (Host.FPDrawnPolygon != null && Host.FPDrawnPolygon.Points.Count > 2)
                    {
                        planegridui.ShowDialog();
                    }
                    else
                    {
                        if (
                            CustomMessageBox.Show("No polygon defined. Load a file?", "Load File", MessageBoxButtons.YesNo) ==
                            (int)DialogResult.Yes)
                        {
                            planegridui.loadFileMenuItem_Click(null, null);
                            planegridui.ShowDialog();
                        }
                        else
                        {
                            CustomMessageBox.Show("Please define a polygon.", "Error");
                        }
                    }
                }
            };
            ((ToolStripMenuItem)Host.FPMenuMap.Items["autoWPToolStripMenuItem"]).DropDownItems.Insert(0, planeGridToolStripMenuItem);
        }
        public override bool Exit() { return true; }
    }
}
