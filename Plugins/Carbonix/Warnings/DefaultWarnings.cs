using System.Collections.Generic;
using System.Linq;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Provides the default set of warning rules for all aircraft types.
    /// </summary>
    public static class DefaultWarnings
    {
        static readonly ICondition Armed = Condition.Field("armed", CompareOp.GT, 0);

        public static readonly List<(Aircraft? aircraft, WarningRule rule)> AllRules =
            new List<(Aircraft?, WarningRule)>
            {
                (null, new WarningRule(
                    id: "gps1_low_sats",
                    text: "GPS 1 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount", CompareOp.LT, 20),
                    gate: Armed)),

                (null, new WarningRule(
                    id: "gps1_low_acc",
                    text: "GPS 1 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc", CompareOp.GT, 1.5),
                    gate: Armed)),

                (null, new WarningRule(
                    id: "gps2_low_sats",
                    text: "GPS 2 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount2", CompareOp.LT, 20),
                    gate: Armed)),

                (null, new WarningRule(
                    id: "gps2_low_acc",
                    text: "GPS 2 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc2", CompareOp.GT, 1.5),
                    gate: Armed)),

                (Aircraft.Ottano, new WarningRule(
                    id: "engine_stopped",
                    text: "Engine Stopped",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Engine,
                    trigger: Condition.Field("efi_rpm", CompareOp.LT, 500),
                    gate: Armed)),

                (Aircraft.Volanti, new WarningRule(
                    id: "pusher_esc_temp",
                    text: "Pusher ESC temp high",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.ESC,
                    trigger: Condition.Field("esc5_temp", CompareOp.GT, 100),
                    gate: Armed)),
            };

        public static List<WarningRule> GetAll(Aircraft aircraft)
        {
            return AllRules
                .Where(r => r.aircraft == null || r.aircraft == aircraft)
                .Select(r => r.rule)
                .ToList();
        }
    }
}
