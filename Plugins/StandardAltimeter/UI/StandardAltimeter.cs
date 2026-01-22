using MissionPlanner.Utilities;
using System;
using System.Windows.Forms;

namespace StandardAltimeter
{
    public enum KollsmanUnit
    {
        inHg,
        hPa
    }

    public partial class StandardAltimeter : UserControl
    {
        private const string SettingsKey = "stdalt_kollsman_unit";
        private const decimal InHgToHPa = 33.8639m;

        public StandardAltimeter()
        {
            InitializeComponent();
            ApplyUnitSettings();
        }

        public static KollsmanUnit GetKollsmanUnit()
        {
            var setting = Settings.Instance[SettingsKey];
            if (Enum.TryParse(setting, out KollsmanUnit unit))
                return unit;
            return KollsmanUnit.hPa;
        }

        public static void SetKollsmanUnit(KollsmanUnit unit)
        {
            Settings.Instance[SettingsKey] = unit.ToString();
        }

        public static decimal FromKollsmanDisplayUnit(decimal value)
        {
            var unit = GetKollsmanUnit();
            if (unit == KollsmanUnit.inHg)
                return value * InHgToHPa;
            return value;
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var current = GetKollsmanUnit();
            inHgToolStripMenuItem.Checked = current == KollsmanUnit.inHg;
            hPaToolStripMenuItem.Checked = current == KollsmanUnit.hPa;
        }

        private void inHgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SwitchUnit(KollsmanUnit.inHg);
        }

        private void hPaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SwitchUnit(KollsmanUnit.hPa);
        }

        private void SwitchUnit(KollsmanUnit newUnit)
        {
            var oldUnit = GetKollsmanUnit();
            if (oldUnit == newUnit)
                return;

            decimal newValue;
            if (newUnit == KollsmanUnit.hPa)
                newValue = Math.Round(num_kollsman.Value * InHgToHPa, 0);
            else
                newValue = Math.Round(num_kollsman.Value / InHgToHPa, 2);

            SetKollsmanUnit(newUnit);
            ApplyUnitSettings();
            num_kollsman.Value = Math.Max(num_kollsman.Minimum, Math.Min(num_kollsman.Maximum, newValue));
        }

        private void ApplyUnitSettings()
        {
            var unit = GetKollsmanUnit();
            if (unit == KollsmanUnit.hPa)
            {
                num_kollsman.DecimalPlaces = 0;
                num_kollsman.Increment = 1;
                num_kollsman.Minimum = 948;
                num_kollsman.Maximum = 1050;
                num_kollsman.Value = 1013;
            }
            else
            {
                num_kollsman.DecimalPlaces = 2;
                num_kollsman.Increment = 0.01m;
                num_kollsman.Minimum = 28;
                num_kollsman.Maximum = 31;
                num_kollsman.Value = 29.92m;
            }
        }
    }
}
