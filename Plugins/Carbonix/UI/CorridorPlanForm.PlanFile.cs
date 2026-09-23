using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Save / Load the whole form as a plan file (.cxplan, JSON — see CorridorPlanFile),
    // so a corridor can be reopened and replanned later: geometry, ceiling zones, every
    // parameter, leg order and all profile edits. Loading replaces the current state and
    // regenerates so the edits are re-applied and the profile shows straight away.
    // ─────────────────────────────────────────────────────────────────────────────
    public partial class CorridorPlanForm
    {
        private const string PlanFilter = "Corridor Plan|*.cxplan|All files|*.*";

        private MissionPlanner.Controls.MyButton BUT_plan_save;
        private MissionPlanner.Controls.MyButton BUT_plan_load;

        // ─── Setup (called from the constructor) ────────────────────────────────────
        private void BuildPlanFileButtons()
        {
            BUT_plan_save = new MissionPlanner.Controls.MyButton
            {
                Text = "Save Plan…", Location = new System.Drawing.Point(4, 34), Size = new Size(80, 26),
            };
            BUT_plan_load = new MissionPlanner.Controls.MyButton
            {
                Text = "Load Plan…", Location = new System.Drawing.Point(90, 34), Size = new Size(80, 26),
            };
            BUT_plan_save.Click += BUT_plan_save_Click;
            BUT_plan_load.Click += BUT_plan_load_Click;

            pnl_buttons.Controls.Add(BUT_plan_save);
            pnl_buttons.Controls.Add(BUT_plan_load);
            pnl_buttons.Height += 30;   // second row under Generate / Accept
        }

        // ─── Save ───────────────────────────────────────────────────────────────────
        private void BUT_plan_save_Click(object sender, EventArgs e)
        {
            if (AllFeatures().Count == 0 && ceilingZones.Count == 0)
            {
                CustomMessageBox.Show("Nothing to save — load a corridor first.", "No Plan");
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save Corridor Plan";
                dlg.Filter = PlanFilter;
                dlg.DefaultExt = "cxplan";
                dlg.FileName = featureFiles.Count > 0
                    ? Path.GetFileNameWithoutExtension(featureFiles[0]) + ".cxplan"
                    : "corridor.cxplan";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dlg.FileName, CapturePlan().ToJson());
                }
                catch (Exception ex)
                {
                    log.Error("Corridor plan save failed", ex);
                    CustomMessageBox.Show("Error saving plan:\n" + ex.Message, "Save Error");
                }
            }
        }

        // Snapshot of the form's model. Altitudes come off the controls in display units and
        // are stored in metres, like BuildParameters.
        private CorridorPlanFile CapturePlan()
        {
            double mult = CurrentState.multiplieralt;
            var home = plugin.Host.cs.PlannedHomeLocation;

            var plan = new CorridorPlanFile
            {
                SavedUtc = DateTime.UtcNow.ToString("o"),
                Home = home.Lat != 0 ? new[] { home.Lat, home.Lng } : null,
                Params = new CorridorPlanFile.Parameters
                {
                    MinAGL = (double)NUM_minalgl.Value / mult,
                    MaxAGL = (double)NUM_maxagl.Value / mult,
                    DefaultAGL = (double)NUM_defagl.Value / mult,
                    SpeedMs = (double)NUM_speed.Value,
                    PassOffsetM = (double)NUM_passoffset.Value,
                    OneWay = CHK_oneway.Checked,
                    OneWayEnd = oneWayEnd != null ? new[] { oneWayEnd.Lat, oneWayEnd.Lng } : null,
                    TourStart = tourStart != null ? new[] { tourStart.Lat, tourStart.Lng } : null,
                    Reverse = CHK_reverse.Checked,
                    CornerCutThresholdDeg = (double)NUM_low_thresh.Value,
                    FullOrbitThresholdDeg = (double)NUM_high_thresh.Value,
                    OverflyDistM = (double)NUM_extension.Value,
                    TurnRadiusM = (double)NUM_turnradius.Value,
                    CornerCutRadiusM = (double)NUM_cornerradius.Value,
                    GradWarnPct = (double)NUM_gradwarn.Value,
                    GradMaxPct = (double)NUM_gradmax.Value,
                },
                LegOrder = IntentionalLegOrder(),
                AltOverrides = CorridorPlanFile.FromVertexAlts(altOverrides),
                CornerCutAlts = CorridorPlanFile.FromVertexAlts(cornerCutAlts),
                Checkpoints = checkpoints.ToList(),
                LoiterToAlts = loiterToAlts.ToList(),
                NextCheckpointId = nextCheckpointId,
            };

            for (int i = 0; i < featureFiles.Count; i++)
                plan.Features.Add(new CorridorPlanFile.FeatureFile
                {
                    Name = Path.GetFileName(featureFiles[i]),
                    Polylines = featuresByFile[i]
                        .Select(f => f.Select(CorridorPlanFile.ToArray).ToList()).ToList(),
                });

            foreach (var z in ceilingZones)
                plan.Zones.Add(new CorridorPlanFile.Zone
                {
                    Name = z.Name,
                    CeilingAglM = z.CeilingAglM,
                    Ring = z.Ring.Select(CorridorPlanFile.ToArray).ToList(),
                });

            return plan;
        }

        // Number Legs captures the tour's actual order into legOrder even when the user never
        // reordered anything, and that order came from the current home. Save it only when it
        // differs from what the builder would produce unaided, so the file carries a
        // preference the user expressed, not an accident of where home was.
        private List<int> IntentionalLegOrder()
        {
            if (legOrder.Count == 0) return new List<int>();
            var unaided = ComputeLegOrder(null, out _);
            return legOrder.SequenceEqual(unaided) ? new List<int>() : legOrder.ToList();
        }

        // ─── Load ───────────────────────────────────────────────────────────────────
        private async void BUT_plan_load_Click(object sender, EventArgs e)
        {
            string path;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Load Corridor Plan";
                dlg.Filter = PlanFilter;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }

            CorridorPlanFile plan;
            try
            {
                plan = CorridorPlanFile.FromJson(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                log.Error("Corridor plan load failed", ex);
                CustomMessageBox.Show("Error loading plan:\n" + ex.Message, "Load Error");
                return;
            }

            ApplyPlan(plan);
            WarnIfHomeMoved(plan);

            if (AllFeatures().Count > 0)
                await ExecuteGenerateAsync();
        }

        // Replace the form's model with the plan's. Order matters: RebuildModel wipes the
        // edit collections and leg order, so the plan's copies go in after it.
        private void ApplyPlan(CorridorPlanFile plan)
        {
            featureFiles.Clear();
            featuresByFile.Clear();
            foreach (var f in plan.Features)
            {
                var polylines = f.Polylines
                    .Select(pl => pl.Select(CorridorPlanFile.ToPoint).ToList())
                    .Where(pl => pl.Count >= 2)
                    .ToList();
                if (polylines.Count == 0) continue;
                featureFiles.Add(f.Name ?? "plan");
                featuresByFile.Add(polylines);
            }

            ceilingZones.Clear();
            foreach (var z in plan.Zones)
            {
                var ring = z.Ring.Select(CorridorPlanFile.ToPoint).ToList();
                if (ring.Count >= 3)
                    ceilingZones.Add(new CeilingZone(z.Name ?? "zone", ring, z.CeilingAglM));
            }

            RebuildModel();
            ApplyPlanParameters(plan.Params);   // after RebuildModel: it clears the start/end picks
            legOrder = plan.LegOrder.ToList();
            foreach (var kv in CorridorPlanFile.ToVertexAlts(plan.AltOverrides)) altOverrides[kv.Key] = kv.Value;
            foreach (var kv in CorridorPlanFile.ToVertexAlts(plan.CornerCutAlts)) cornerCutAlts[kv.Key] = kv.Value;
            checkpoints.AddRange(plan.Checkpoints);
            loiterToAlts.AddRange(plan.LoiterToAlts);
            nextCheckpointId = Math.Max(plan.NextCheckpointId, 100000);

            RefreshFileList();
            RecalcCoverage();
            ZonesChanged();
            DrawMap();
            ZoomToFitFeatures();
        }

        private void ApplyPlanParameters(CorridorPlanFile.Parameters p)
        {
            double mult = CurrentState.multiplieralt;
            freeze_handlers = true;
            try
            {
                SetNum(NUM_minalgl, p.MinAGL * mult);
                SetNum(NUM_maxagl, p.MaxAGL * mult);
                SetNum(NUM_defagl, p.DefaultAGL * mult);
                SetNum(NUM_speed, p.SpeedMs);
                SetNum(NUM_passoffset, p.PassOffsetM);
                CHK_oneway.Checked = p.OneWay;
                oneWayEnd = p.OneWayEnd != null && p.OneWayEnd.Length >= 2
                    ? new PointLatLngAlt(p.OneWayEnd[0], p.OneWayEnd[1], 0) : null;
                tourStart = p.TourStart != null && p.TourStart.Length >= 2
                    ? new PointLatLngAlt(p.TourStart[0], p.TourStart[1], 0) : null;
                CHK_reverse.Checked = p.Reverse;
                SetNum(NUM_low_thresh, p.CornerCutThresholdDeg);
                SetNum(NUM_high_thresh, p.FullOrbitThresholdDeg);
                SetNum(NUM_extension, p.OverflyDistM);
                SetNum(NUM_turnradius, p.TurnRadiusM);
                SetNum(NUM_cornerradius, p.CornerCutRadiusM);
                SetNum(NUM_gradwarn, p.GradWarnPct);
                SetNum(NUM_gradmax, p.GradMaxPct);
            }
            finally
            {
                freeze_handlers = false;
            }
        }

        private static void SetNum(NumericUpDown n, double value)
        {
            if (double.IsNaN(value)) return;
            var v = (decimal)value;
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        // The tour roots at the endpoint nearest home (unless the plan picked its start), so a
        // plan saved from a different launch point may fly the legs in a different order.
        private void WarnIfHomeMoved(CorridorPlanFile plan)
        {
            if (plan.Home == null || plan.Home.Length < 2) return;
            if (plan.Params.TourStart != null) return;
            var now = plugin.Host.cs.PlannedHomeLocation;
            if (now.Lat == 0) return;

            double distM = new PointLatLngAlt(plan.Home[0], plan.Home[1], 0).GetDistance(now);
            if (distM > 1000)
                CustomMessageBox.Show(
                    $"This plan was saved with home {distM / 1000:F1} km from the current Flight Planner home.\n" +
                    "The tour starts from the end nearest home, so the leg order may differ.",
                    "Home Moved");
        }
    }
}
