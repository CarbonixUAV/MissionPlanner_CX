using System;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace Carbonix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Trip shape. The tour model flies every edge out and back, so "number of passes"
    // stopped meaning anything beyond on/off for the lane offset. The Pass Layout group
    // now offers: Trip = round trip / one way, and Lane sep (0 = both passes on the
    // centreline). One way ends at a dead-end — the farthest from the start unless the
    // user picks one in the Edit tab — and flies the start→end path once on the
    // centreline; spurs off it are still out and back.
    // ─────────────────────────────────────────────────────────────────────────────
    public partial class CorridorPlanForm
    {
        private CheckBox CHK_oneway;

        // Dead-end the one-way trip should end at (null = automatic: farthest from the start).
        private PointLatLngAlt oneWayEnd;

        // Node (feature endpoint or junction) the tour starts from (null = nearest home).
        private PointLatLngAlt tourStart;

        // ─── Setup (called from the constructor) ────────────────────────────────────
        private void BuildTripControls()
        {
            grp_corridor.Text = "Passes";

            // Row 0: "Trip:" + One way checkbox in place of the pass count.
            NUM_numpasses.Visible = false;
            lbl_passes_unit.Visible = false;
            lbl_numpasses.Text = "Trip:";
            CHK_oneway = new CheckBox
            {
                Text = "One way (end at far end)", AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            tbl_corridor.Controls.Add(CHK_oneway, 1, 0);
            tbl_corridor.SetColumnSpan(CHK_oneway, 2);

            // Row 1: lane separation, which may be zero.
            lbl_passoffset.Text = "Lane sep:";
            NUM_passoffset.Minimum = 0;

            CHK_oneway.CheckedChanged += TripParams_Changed;
            CHK_reverse.CheckedChanged += TripParams_Changed;
        }

        // The trip shape changes the tour, so the Edit tab's numbering is stale until redrawn.
        private void TripParams_Changed(object sender, EventArgs e)
        {
            if (freeze_handlers) return;
            RecalcCoverage();
            if (editMode && legsNumbered) DrawEditColored();
        }

        private string TripSummary()
        {
            double sep = (double)NUM_passoffset.Value;
            string lanes = sep > 0 ? $"out +{sep / 2:F0} m / back −{sep / 2:F0} m" : "both passes on the centreline";
            return CHK_oneway.Checked
                ? $"One way on the centreline; spurs out/back ({lanes})"
                : $"Round trip: {lanes}";
        }
    }
}
