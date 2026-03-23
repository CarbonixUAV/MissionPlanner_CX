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

        static readonly ICondition Connected = Condition.Field("linkqualitygcs", CompareOp.GT, 0);

        static readonly ICondition EkfVelocityVariance = Condition.Field("ekfvelv", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfCompassVariance = Condition.Field("ekfcompv", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfPosHorizVariance = Condition.Field("ekfposhor", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfPosVertVariance = Condition.Field("ekfposvert", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfTerrainVariance = Condition.Field("ekfteralt", CompareOp.GTEQ, 1.0, clear: 0.8);
        static readonly ICondition EkfNavVariance = EkfVelocityVariance.Or(EkfPosHorizVariance).Or(EkfPosVertVariance);
        static readonly ICondition InternalError = Condition.Field("errors_count1", CompareOp.GT, 0).Or(Condition.Field("errors_count2", CompareOp.GT, 0));

        // Catch
        // PreArm: Rangefinder 1: not detected
        // PreArm: Rangefinder 1: not connected
        // PreArm: Rangefinder 1: no data
        static readonly ICondition RangeFinderMissing = Condition.StatusText(
            "PreArm.*Rangefinder.*: no.*",
            timeoutMs: 35_000
        );

        /// <summary>
        /// Builds a condition that fires when a SYS_STATUS sensor is present
        /// and enabled but not healthy.
        /// </summary>
        static ICondition SensorUnhealthy(string sensor) =>
            Condition.Field($"sensors_health.{sensor}", CompareOp.EQ, 0)
                .And(Condition.Field($"sensors_enabled.{sensor}", CompareOp.GT, 0))
                .And(Condition.Field($"sensors_present.{sensor}", CompareOp.GT, 0));

        static readonly ICondition PrearmsPassing = Armed.Or(
            // Prearms initialize to passing during connection until the first SYS_STATUS comes in.
            // We gate this on being connected for 10s to avoid false positives.
            SensorUnhealthy("prearm").Not().And(Connected.Sustain(riseMs: 10_000, fallMs: 0))
        );

        static readonly ICondition RcLoss = SensorUnhealthy("rc_receiver").Sustain(riseMs: 2_000, fallMs: 0)
            .Or(Condition.StatusText("RC.*failsafe.*(:|on)", clearPattern: "(RC|Short).*failsafe cleared", timeoutMs: 5_000))
            .Or(Condition.StatusText("Throttle failsafe on", clearPattern: "Throttle failsafe off", timeoutMs: 5_000));
            
        static readonly ICondition FenceBreach = Condition.Field("fenceb_status", CompareOp.GT, 0).Or(SensorUnhealthy("geofence"));

        static readonly List<WarningRule> AllRules = new List<WarningRule>
            {
                new WarningRule(
                    id: "gps1_low_sats",
                    text: "GPS 1 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount", CompareOp.LT, 18, 22),
                    gate: PrearmsPassing),

                new WarningRule(
                    id: "gps1_low_acc",
                    text: "GPS 1 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc", CompareOp.GT, 1.5, 1.4),
                    gate: PrearmsPassing),

                new WarningRule(
                    id: "gps2_low_sats",
                    text: "GPS 2 low satellites",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount2", CompareOp.LT, 18, 22),
                    gate: PrearmsPassing),

                new WarningRule(
                    id: "gps2_low_acc",
                    text: "GPS 2 low accuracy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("gpsh_acc2", CompareOp.GT, 1.5, 1.4),
                    gate: PrearmsPassing),

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
                    trigger: RangeFinderMissing),

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
                    gate: PrearmsPassing),

                // --- SYS_STATUS sensor health ---

                new WarningRule(
                    id: "health_gps",
                    text: "GPS unhealthy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    // We need to debounce this a little. If the GPS has been unhealthy for 2s
                    // in the last 30 minutes or so, we warn about it. Otherwise we ignore it.
                    trigger: SensorUnhealthy("gps").Sustain(riseMs: 2_000, fallMs: 1_800_000, reset: PrearmsPassing.Not()),
                    gate: Armed),

                new WarningRule(
                    id: "health_imu",
                    text: "IMU unhealthy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Attitude,
                    trigger: SensorUnhealthy("gyro").Or(SensorUnhealthy("accelerometer")),
                    gate: Armed),

                new WarningRule(
                    id: "health_compass",
                    text: "Compass unhealthy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Compass,
                    trigger: SensorUnhealthy("compass"),
                    gate: Armed),

                new WarningRule(
                    id: "health_baro",
                    text: "Barometer unhealthy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Baro,
                    trigger: SensorUnhealthy("barometer"),
                    gate: Armed),

                new WarningRule(
                    id: "health_airspeed",
                    text: "Airspeed unhealthy",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Airspeed,
                    trigger: SensorUnhealthy("differential_pressure"),
                    gate: Armed),

                new WarningRule(
                    id: "health_ahrs",
                    text: "Bad AHRS",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Attitude,
                    trigger: SensorUnhealthy("ahrs"),
                    gate: Armed),

                new WarningRule(
                    id: "health_terrain",
                    text: "Bad terrain data",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Terrain,
                    trigger: SensorUnhealthy("terrain"),
                    gate: Armed),

                new WarningRule(
                    id: "health_rc",
                    text: "No controller",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.FlightControl,
                    trigger: RcLoss),

                new WarningRule(
                    id: "health_battery",
                    text: "Battery alert",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Power,
                    trigger: SensorUnhealthy("battery"),
                    gate: Armed),

                // --- Fence breach ---

                new WarningRule(
                    id: "fence_breach",
                    text: "Fence breach",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Geofence,
                    trigger: FenceBreach,
                    gate: Armed),

                // --- Internal error ---

                new WarningRule(
                    id: "internal_error",
                    text: "Internal error",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.FlightControl,
                    trigger: InternalError),

                // --- Data link ---

                new WarningRule(
                    id: "no_data",
                    text: "Comm loss",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.DataLink,
                    trigger: Connected.Not(),
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
