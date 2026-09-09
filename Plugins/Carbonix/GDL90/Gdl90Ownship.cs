using System;
using MissionPlanner;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents the identity the ownship reports claim.
    /// </summary>
    public sealed class Gdl90OwnshipIdentity
    {
        /// <summary>Represents the largest ICAO address (UINT24_MAX).</summary>
        public const int Icao24BitMax = 0xFFFFFF;

        /// <summary>Gets or sets the 24-bit ICAO address, reported with address type 0.</summary>
        public int IcaoAddress { get; set; }

        /// <summary>Gets or sets the flight ID; for a VH- aircraft, its registration.</summary>
        public string Callsign { get; set; }
    }

    /// <summary>
    /// Represents one second of ownship output.
    /// </summary>
    public sealed class Gdl90OwnshipState
    {
        /// <summary>Gets or sets the ownship report.</summary>
        public Gdl90OwnshipReport Report { get; set; }

        /// <summary>Gets or sets a value indicating whether the geometric altitude message may be sent.</summary>
        public bool GeometricAltitudeValid { get; set; }

        /// <summary>Gets or sets the geometric altitude in feet above mean sea level.</summary>
        public int GeometricAltitudeFeet { get; set; }
    }

    /// <summary>
    /// Provides the mapping from a vehicle observation onto the GDL 90 ownship messages.
    /// </summary>
    public static class Gdl90Ownship
    {
        /// <summary>Represents ICD Table 11 category 14, unmanned aerial vehicle.</summary>
        public const int EmitterCategory = 14;

        /// <summary>Represents ICD Table 10 value 0, an unknown containment radius.</summary>
        // NIC is a containment radius from integrity monitoring, which nothing in this
        // chain produces. The receiver's accuracy figure is a different quantity and
        // goes to NACp instead.
        public const int NicUnknown = 0;

        /// <summary>Represents ICD Table 10 value 0, an unknown position accuracy.</summary>
        public const int NacpUnknown = 0;

        /// <summary>
        /// Represents the conversion factor from reported GNSS accuracy to the 95% uncertainty.
        /// </summary>
        // R95 for a one-sigma per-axis figure. ArduPilot is inconsistent about h_acc
        // (AP_GPS documents one sigma; its uAvionix path reads a two-sigma HFOM), so
        // this takes the conservative reading.
        public const double EpuFromReportedAccuracy = 2.45;

        /// <summary>Represents the ground speed below which the ground vector has no usable direction, in meters per second.</summary>
        // This is strictly about numerical stability, and is separate from the consideration of
        // whether its more useful to report track or heading.
        public const double MinimumTrackSpeedMetersPerSecond = 0.5;

        const int MinimumGpsFixType = 3;

        const double MetersPerFoot = 0.3048;
        const double MetersPerSecondPerKnot = 1852.0 / 3600.0;

        // The geometric altitude field is a signed 16-bit count of 5 ft (ICD 3.8).
        const int GeometricAltitudeMinFeet = short.MinValue * 5;
        const int GeometricAltitudeMaxFeet = short.MaxValue * 5;

        // Wide enough that only nonsense is rejected; the encoder pins the real limits
        // and emits its own sentinels inside them.
        const int VelocityGuardKnots = 100000;
        const int VerticalVelocityGuardFpm = 1000000;

        /// <summary>Builds one second of ownship output from an observation.</summary>
        /// <param name="vehicle">What the aircraft was last observed doing.</param>
        /// <param name="identity">The transponder identity to report under.</param>
        /// <returns>The ownship report and the geometric altitude.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="vehicle"/> or <paramref name="identity"/> is null.
        /// </exception>
        public static Gdl90OwnshipState Build(Gdl90VehicleState vehicle, Gdl90OwnshipIdentity identity)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (identity == null) throw new ArgumentNullException(nameof(identity));

            var gps = SelectGps(vehicle);
            bool hasPosition = gps.HasFix && vehicle.Position != null;

            var report = new Gdl90OwnshipReport
            {
                TrafficAlert = false,
                AddressType = Gdl90AddressType.AdsbIcao,
                Address = identity.IcaoAddress,
                Callsign = identity.Callsign,
                EmitterCategory = EmitterCategory,
                EmergencyPriorityCode = 0,
                Extrapolated = false,
                PositionValid = hasPosition,
                LatitudeE7 = vehicle.Position?.LatitudeE7 ?? 0L,
                LongitudeE7 = vehicle.Position?.LongitudeE7 ?? 0L,
                Nic = NicUnknown,
                Nacp = hasPosition ? DeriveNacp(gps) : NacpUnknown,

                // Set unless the aircraft is known to be on the ground. A landed state
                // not yet received reads as airborne: a receiver may suppress ownship
                // altitude while On Ground is set.
                Airborne = vehicle.LandedState != MAVLink.MAV_LANDED_STATE.ON_GROUND,
            };

            SetPressureAltitude(report, vehicle);
            SetGroundSpeed(report, vehicle.GroundVector);
            SetVerticalVelocity(report, vehicle.GroundVector);
            SetTrack(report, vehicle, hasPosition ? vehicle.GroundVector : null);

            var state = new Gdl90OwnshipState { Report = report };
            SetGeometricAltitude(state, vehicle, gps.HasFix);
            return state;
        }

        sealed class GpsSource
        {
            public GpsSource(Gdl90GpsSample sample)
            {
                HasFix = sample.FixType >= MinimumGpsFixType;
                HorizontalAccuracyMeters = sample.HorizontalAccuracyMeters;
            }

            public bool HasFix { get; }

            public double HorizontalAccuracyMeters { get; }
        }

        // GPS1 while it holds a fix, GPS2 when it does not: the accuracy has to describe
        // the receiver the estimator is flying on, which is the aircraft's own
        // primary-then-fallback order, not whichever reads better.
        static GpsSource SelectGps(Gdl90VehicleState vehicle)
        {
            var primary = new GpsSource(vehicle.Gps);
            if (primary.HasFix) return primary;

            var secondary = new GpsSource(vehicle.Gps2);
            return secondary.HasFix ? secondary : primary;
        }

        // Bins the receiver's 95% horizontal uncertainty against ICD Table 10. Stops at
        // 9: below that the table is nautical-mile categories, and an EPU past 30 m
        // from this receiver means the fix has failed rather than coarsened.
        static int DeriveNacp(GpsSource gps)
        {
            if (!gps.HasFix) return NacpUnknown;

            // Zero means unknown, not perfect: it is what ArduPilot initializes h_acc to.
            double accuracy = gps.HorizontalAccuracyMeters;
            if (double.IsNaN(accuracy) || accuracy <= 0.0) return NacpUnknown;

            double epuMeters = EpuFromReportedAccuracy * accuracy;

            if (epuMeters < 3.0) return 11;
            if (epuMeters < 10.0) return 10;
            if (epuMeters < 30.0) return 9;
            return NacpUnknown;
        }

        // From the barometer on the 1013.25 hPa datum
        static void SetPressureAltitude(Gdl90Report report, Gdl90VehicleState vehicle)
        {
            double feet;
            bool valid = PressureAltitude.TryFromStaticPressure(vehicle.StaticPressureHpa, out feet);

            int rounded = 0;
            valid = valid && TryRound(feet, Gdl90Messages.MinimumAltitudeFeet,
                Gdl90Messages.MaximumAltitudeFeet, out rounded);

            report.AltitudeValid = valid;
            report.PressureAltitudeFeet = valid ? rounded : 0;
        }

        static void SetGroundSpeed(Gdl90Report report, Gdl90GroundVector vector)
        {
            int knots = 0;
            bool valid = vector != null
                && TryRound(vector.HorizontalSpeed / MetersPerSecondPerKnot, 0, VelocityGuardKnots, out knots);

            report.HorizontalVelocityValid = valid;
            report.HorizontalVelocityKnots = valid ? knots : 0;
        }

        // The vector's Down is positive down; the report is positive up.
        static void SetVerticalVelocity(Gdl90Report report, Gdl90GroundVector vector)
        {
            double feetPerMinute = vector == null ? 0.0 : -vector.Down / MetersPerFoot * 60.0;

            int fpm = 0;
            bool valid = vector != null
                && TryRound(feetPerMinute, -VerticalVelocityGuardFpm, VerticalVelocityGuardFpm, out fpm);

            report.VerticalVelocityValid = valid;
            report.VerticalVelocityFpm = valid ? fpm : 0;
        }

        static void SetTrack(Gdl90Report report, Gdl90VehicleState vehicle, Gdl90GroundVector vector)
        {
            bool valid;
            double degrees = 0.0;
            Gdl90TrackType type;

            if (vehicle.TrackSource == Gdl90TrackType.TrueHeading)
            {
                // ATTITUDE.yaw is referenced to true north.
                valid = IsFinite(vehicle.YawDegrees);
                degrees = vehicle.YawDegrees;
                type = Gdl90TrackType.TrueHeading;
            }
            else
            {
                valid = TryGroundTrack(vector, out degrees);
                type = Gdl90TrackType.TrueTrack;
            }

            report.TrackType = valid ? type : Gdl90TrackType.Invalid;
            report.TrackDegrees = valid ? degrees : 0.0;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // Course over the ground from the estimator's velocity
        static bool TryGroundTrack(Gdl90GroundVector vector, out double degrees)
        {
            degrees = 0.0;
            if (vector == null) return false;

            double north = vector.North;
            double east = vector.East;
            if (!IsFinite(north) || !IsFinite(east)) return false;
            if (Math.Sqrt(north * north + east * east) < MinimumTrackSpeedMetersPerSecond)
                return false;

            degrees = Math.Atan2(east, north) * (180.0 / Math.PI);
            if (degrees < 0.0) degrees += 360.0;

            // A hair west of north folds to exactly 360.0, which is not a course.
            if (degrees >= 360.0) degrees = 0.0;
            return true;
        }

        // Above mean sea level, not above the ellipsoid: GLOBAL_POSITION_INT's altitude is AMSL,
        // and the ForeFlight ID message tells the receiver so.
        static void SetGeometricAltitude(Gdl90OwnshipState state, Gdl90VehicleState vehicle,
            bool available)
        {
            double feet = vehicle.AltitudeMslMeters / MetersPerFoot;

            int rounded = 0;
            bool valid = available
                && TryRound(feet, GeometricAltitudeMinFeet, GeometricAltitudeMaxFeet, out rounded);

            state.GeometricAltitudeValid = valid;
            state.GeometricAltitudeFeet = valid ? rounded : 0;
        }

        static bool TryRound(double value, int min, int max, out int result)
        {
            result = 0;
            if (double.IsNaN(value) || value < min || value > max) return false;
            result = (int)Math.Round(value);
            return true;
        }
    }
}
