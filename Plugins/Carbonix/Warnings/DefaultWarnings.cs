using System.Collections.Generic;
using System.Reflection;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Container for a complete set of warning definitions: named reusable
    /// conditions and the rules that reference them.
    /// </summary>
    public class WarningDefinitions
    {
        /// <summary>
        /// Named conditions in dependency order. Later entries may reference
        /// earlier ones via <c>{"ref": "Name"}</c> in JSON.
        /// </summary>
        public Dictionary<string, ICondition> Conditions { get; set; }
            = new Dictionary<string, ICondition>();

        /// <summary>
        /// Warning rules. Triggers and gates may reference named conditions.
        /// </summary>
        public List<WarningRule> Rules { get; set; }
            = new List<WarningRule>();
    }

    /// <summary>
    /// Provides the default set of warning rules for all aircraft types.
    /// </summary>
    public static class DefaultWarnings
    {
        static readonly ICondition Armed = Condition.Field("armed", CompareOp.GT, 0);
        static readonly ICondition SafetyOff = Condition.Field("safetyactive", CompareOp.EQ, 0);
        static readonly ICondition VtolStateToFW = Condition.Field("vtol_state", CompareOp.EQ, (double)MAVLink.MAV_VTOL_STATE.TRANSITION_TO_FW);
        static readonly ICondition VtolStateToMC = Condition.Field("vtol_state", CompareOp.EQ, (double)MAVLink.MAV_VTOL_STATE.TRANSITION_TO_MC);
        static readonly ICondition VtolStateMC = Condition.Field("vtol_state", CompareOp.EQ, (double)MAVLink.MAV_VTOL_STATE.MC);
        static readonly ICondition VtolStateFW = Condition.Field("vtol_state", CompareOp.EQ, (double)MAVLink.MAV_VTOL_STATE.FW);
        static readonly ICondition IntendedFixedWing = VtolStateFW.Latch(VtolStateMC.Or(VtolStateToMC));
        static readonly ICondition QAssist = VtolStateToFW.Or(Condition.StatusText("QASSIST"));
        static readonly ICondition OnGround = Condition.Field("landed_state", CompareOp.EQ, (double)MAVLink.MAV_LANDED_STATE.ON_GROUND);
        // IsLanding starts at the airbrake stage, or, if that gets skipped, as soon as landed_state is LANDING.
        static readonly ICondition IsLanding = VtolStateToMC.Latch(VtolStateToFW.Or(VtolStateFW).Or(OnGround))
            .Or(Condition.Field("landed_state", CompareOp.EQ, (double)MAVLink.MAV_LANDED_STATE.LANDING));
        static readonly ICondition EngineOut = Condition.StatusText("Engine out", clearPattern: "Engine running")
            .Or(Condition.StatusText("Uncommanded engine stop", timeoutMs: 1_000))
            .Or(Condition.Field("efi_rpm", CompareOp.LT, 500));

        static readonly ICondition EkfVelocityVariance = Condition.Field("ekfvelv", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfCompassVariance = Condition.Field("ekfcompv", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfPosHorizVariance = Condition.Field("ekfposhor", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfPosVertVariance = Condition.Field("ekfposvert", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfTerrainVariance = Condition.Field("ekfteralt", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfNavVariance = EkfVelocityVariance.Or(EkfPosHorizVariance).Or(EkfPosVertVariance);
        static readonly ICondition FenceBreach = Condition.Field("fenceb_status", CompareOp.GT, 0);

        // Catch
        // PreArm: Rangefinder 1: not detected
        // PreArm: Rangefinder 1: not connected
        // PreArm: Rangefinder 1: no data
        static readonly ICondition RangeFinderMissing = Condition.StatusText(
            "PreArm.*Rangefinder.*: no.*",
            timeoutMs: 35_000
        );

        static readonly List<WarningRule> AllRules = new List<WarningRule>
            {
                new WarningRule(
                    id: "gps1_low_sats",
                    text: "GPS 1 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount", CompareOp.LT, 18, 22),
                    gate: Armed),

                new WarningRule(
                    id: "gps1_low_acc",
                    text: "GPS 1 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc", CompareOp.GT, 1.5, 1.4),
                    gate: Armed),

                new WarningRule(
                    id: "gps2_low_sats",
                    text: "GPS 2 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount2", CompareOp.LT, 18, 22),
                    gate: Armed),

                new WarningRule(
                    id: "gps2_low_acc",
                    text: "GPS 2 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc2", CompareOp.GT, 1.5, 1.4),
                    gate: Armed),

                new WarningRule(
                    id: "pusher_esc_temp",
                    text: "Pusher ESC hot",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.ESC,
                    trigger: Condition.Field("esc5_temp", CompareOp.GT, 100, 80),
                    aircraft: Aircraft.Volanti),

                new WarningRule(
                    id: "engine_out",
                    text: "Engine out",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Engine,
                    trigger: EngineOut,
                    gate: Armed.And(OnGround.Not()).And(IsLanding.Not()),
                    aircraft: Aircraft.Ottano),

                new WarningRule(
                    id: "qassist",
                    text: "QAssist",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.FlightControl,
                    trigger: QAssist,
                    gate: Armed.And(IntendedFixedWing)),

                new WarningRule(
                    id: "rangefinder_missing",
                    text: "Rangefinder missing",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Terrain,
                    trigger: RangeFinderMissing,
                    gate: Armed.Not()),

                // --- EKF variance ---

                new WarningRule(
                    id: "ekf_nav_variance",
                    text: "EKF nav variance",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Navigation,
                    trigger: EkfNavVariance,
                    gate: SafetyOff),

                new WarningRule(
                    id: "ekf_compass_variance",
                    text: "EKF compass variance",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Compass,
                    trigger: EkfCompassVariance,
                    gate: SafetyOff),

                // In ArduPilot, this is hard-coded to report 0 if you are not not using rangefinder
                // as a z-position source (which will always be the case for us; it was designed for
                // low alt, optical-flow type setups). Nonetheless, I might as well have this rule.
                new WarningRule(
                    id: "ekf_terrain_variance",
                    text: "EKF terrain variance",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Terrain,
                    trigger: EkfTerrainVariance,
                    gate: Armed),

                // --- Fence breach ---

                new WarningRule(
                    id: "fence_breach",
                    text: "Fence breach",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Geofence,
                    trigger: FenceBreach,
                    gate: Armed),
            };

        /// <summary>
        /// Bundles named conditions (in dependency order) and rules into a
        /// <see cref="WarningDefinitions"/> for serialization with ref support.
        /// </summary>
        public static WarningDefinitions Defaults { get; } = BuildDefaults();

        static WarningDefinitions BuildDefaults()
        {
            // Field declaration order in C# matches GetFields order, which
            // gives us dependency order (earlier fields are defined first).
            var conditions = new Dictionary<string, ICondition>();
            foreach (var fi in typeof(DefaultWarnings).GetFields(
                BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (typeof(ICondition).IsAssignableFrom(fi.FieldType))
                    conditions[fi.Name] = (ICondition)fi.GetValue(null);
            }

            return new WarningDefinitions
            {
                Conditions = conditions,
                Rules = AllRules,
            };
        }
    }
}
