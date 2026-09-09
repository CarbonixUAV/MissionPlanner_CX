using System;
using System.Text;
using MissionPlanner;
using MissionPlanner.Utilities;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Provides the mapping from an ADSB_VEHICLE message onto a GDL 90 traffic report.
    /// </summary>
    public static class Gdl90Traffic
    {
        /// <summary>
        /// Represents the age below which a target counts as received this second;
        /// older reports carry the extrapolated bit (ICD 3.5.1.5).
        /// </summary>
        public static readonly TimeSpan UpdatedWithin = TimeSpan.FromSeconds(1);

        /// <summary>Represents the farthest a position is carried forward, in meters.</summary>
        public const double MaxPropagationMeters = 10000.0;

        const double MetersPerFoot = 0.3048;
        const double MetersPerSecondPerKnot = 1852.0 / 3600.0;

        /// <summary>Determines whether a collision threat level counts as a traffic alert (ICD 3.5.1.1).</summary>
        /// <param name="threat">The aircraft's assessment of the target.</param>
        /// <returns>true if the aircraft has flagged the target; otherwise, false.</returns>
        public static bool IsAlert(MAVLink.MAV_COLLISION_THREAT_LEVEL threat)
        {
            return threat == MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW
                || threat == MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH;
        }

        /// <summary>Builds the traffic report for one target.</summary>
        /// <param name="vehicle">The target as received.</param>
        /// <param name="age">How old the target's data is.</param>
        /// <param name="threat">The aircraft's collision assessment of the target.</param>
        /// <returns>The report, with the position carried forward by <paramref name="age"/>.</returns>
        public static Gdl90TrafficReport BuildReport(MAVLink.mavlink_adsb_vehicle_t vehicle,
            TimeSpan age,
            MAVLink.MAV_COLLISION_THREAT_LEVEL threat = MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE)
        {
            // Every validity bit with a traffic-report equivalent is honored, or data
            // the receiver said it did not have becomes a confident claim on the wire.
            var flags = (MAVLink.ADSB_FLAGS)vehicle.flags;
            bool coords = (flags & MAVLink.ADSB_FLAGS.VALID_COORDS) != 0;
            bool heading = (flags & MAVLink.ADSB_FLAGS.VALID_HEADING) != 0;
            bool velocity = (flags & MAVLink.ADSB_FLAGS.VALID_VELOCITY) != 0;
            bool verticalVelocity = (flags & MAVLink.ADSB_FLAGS.VERTICAL_VELOCITY_VALID) != 0;
            bool callsign = (flags & MAVLink.ADSB_FLAGS.VALID_CALLSIGN) != 0;

            // GDL 90 only supports pressure altitude for the traffic report. If we receive a
            // different type of altitude, we report unknown instead of trying to convert a
            // geometric altitude to a pressure altitude, which would be deeply nuanced and prone to
            // error. This means we still get the traffic icon on the map, but don't get a
            // potentially false claim about its altitude. 100% of ADSB_VEHICLE reports in my logs
            // are pressure altitude anyway.
            // (heads-up: ADSB_ALTITUDE_TYPE.PRESSURE_QNH is poorly named: it is not QNH adjusted)
            bool altitude = (flags & MAVLink.ADSB_FLAGS.VALID_ALTITUDE) != 0
                && (MAVLink.ADSB_ALTITUDE_TYPE)vehicle.altitude_type
                    == MAVLink.ADSB_ALTITUDE_TYPE.PRESSURE_QNH;

            double seconds = age.TotalSeconds;
            if (seconds < 0.0) seconds = 0.0;

            long latitudeE7 = vehicle.lat;
            long longitudeE7 = vehicle.lon;
            if (coords && heading && velocity)
                Propagate(ref latitudeE7, ref longitudeE7, vehicle.heading / 100.0,
                    vehicle.hor_velocity * 0.01, seconds);

            int altitudeMillimeters = vehicle.altitude;
            if (altitude && verticalVelocity)
                altitudeMillimeters += (int)Math.Round(vehicle.ver_velocity * 10.0 * seconds);

            return new Gdl90TrafficReport
            {
                // Logged so that we can back out the propagation in analysis if needed.
                // This is not transmitted in the GDL 90 message itself.
                AgeSeconds = Math.Round(seconds, 3),
                // Threat level is also logged but not transmitted (the TrafficAlert bit is)
                ThreatLevel = (int)threat,

                TrafficAlert = IsAlert(threat),

                // ADSB_VEHICLE carries no address type; ICAO_address is all it claims.
                AddressType = Gdl90AddressType.AdsbIcao,
                Address = (int)vehicle.ICAO_address,

                PositionValid = coords,
                LatitudeE7 = latitudeE7,
                LongitudeE7 = longitudeE7,

                AltitudeValid = altitude,
                PressureAltitudeFeet = altitude ? MillimetersToFeet(altitudeMillimeters) : 0,

                // ADSB_VEHICLE has no air/ground state and the bit has no "unknown", so
                // the emitter category is the only evidence. Everything but a surface
                // vehicle reads as airborne: a ground target drawn at its real level is
                // a lesser error than an airborne one a receiver suppresses.
                Airborne = !IsSurfaceVehicle(vehicle.emitter_type),
                Extrapolated = age >= UpdatedWithin,

                // MAVLink's "heading" is course over ground: true track or nothing,
                // never either heading type.
                TrackType = heading ? Gdl90TrackType.TrueTrack : Gdl90TrackType.Invalid,
                TrackDegrees = heading ? vehicle.heading / 100.0 : 0.0,

                // ADSB_VEHICLE carries no integrity or accuracy figure of any kind.
                Nic = Gdl90Ownship.NicUnknown,
                Nacp = Gdl90Ownship.NacpUnknown,

                // Zero knots is a claim that the target is stationary, not an absence
                // of data; VALID_VELOCITY is the only thing that tells the two apart.
                HorizontalVelocityValid = velocity,
                HorizontalVelocityKnots = velocity
                    ? CentimetersPerSecondToKnots(vehicle.hor_velocity)
                    : 0,

                VerticalVelocityValid = verticalVelocity,
                VerticalVelocityFpm = verticalVelocity
                    ? CentimetersPerSecondToFeetPerMinute(vehicle.ver_velocity)
                    : 0,

                // ADSB_EMITTER_TYPE is byte-identical to ICD Table 11 over its range.
                EmitterCategory = vehicle.emitter_type,

                Callsign = callsign ? DecodeCallsign(vehicle.callsign) : null,

                // ADSB_VEHICLE has no emergency or priority field.
                EmergencyPriorityCode = 0,
            };
        }

        // ICD 3.5 dates every report to the current second and has no time-of-reception
        // field, so the position is carried forward along the target's course. Only run
        // with a valid course and ground speed; without both, the last known position
        // is the only statement available.
        static void Propagate(ref long latitudeE7, ref long longitudeE7,
            double trackDegrees, double groundSpeedMetersPerSecond, double seconds)
        {
            if (groundSpeedMetersPerSecond <= 0.0 || seconds <= 0.0) return;
            if (!IsFinite(trackDegrees) || !IsFinite(groundSpeedMetersPerSecond)) return;

            // Five seconds, the most a target can age before it is dropped, at the
            // field's top speed is under 4 km: only wrong inputs get past this.
            double distance = groundSpeedMetersPerSecond * seconds;
            if (distance > MaxPropagationMeters) return;

            var moved = new PointLatLngAlt(latitudeE7 / 1e7, longitudeE7 / 1e7)
                .newpos(trackDegrees, distance);
            if (!IsFinite(moved.Lat) || !IsFinite(moved.Lng)) return;

            latitudeE7 = (long)Math.Round(moved.Lat * 1e7);
            longitudeE7 = (long)Math.Round(NormalizeLongitude(moved.Lng) * 1e7);
        }

        // newpos adds an offset without wrapping, so a target crossing the antimeridian
        // comes back as 180.5. The encoder's two's-complement semicircles would wrap it,
        // but the frame log records what the encoder was given, and 180.5 is not a
        // longitude.
        static double NormalizeLongitude(double degrees)
        {
            if (degrees >= -180.0 && degrees < 180.0) return degrees;

            double wrapped = (degrees + 180.0) % 360.0;
            if (wrapped < 0.0) wrapped += 360.0;
            return wrapped - 180.0;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // ICD Table 11 categories 17 and 18. A point obstacle (19) is left airborne:
        // a tethered balloon is not something a receiver should hide as ground traffic.
        static bool IsSurfaceVehicle(byte emitterType)
        {
            return emitterType == (byte)MAVLink.ADSB_EMITTER_TYPE.EMERGENCY_SURFACE
                || emitterType == (byte)MAVLink.ADSB_EMITTER_TYPE.SERVICE_SURFACE;
        }

        /// <summary>Decodes a MAVLink callsign field as ASCII, up to its terminating NUL.</summary>
        /// <param name="callsign">The field as received.</param>
        /// <returns>The callsign, or null if the field is absent or empty.</returns>
        public static string DecodeCallsign(byte[] callsign)
        {
            if (callsign == null) return null;

            var text = new StringBuilder(callsign.Length);
            foreach (byte b in callsign)
            {
                if (b == 0) break;
                if (b >= 0x20 && b <= 0x7E) text.Append((char)b);
            }

            string result = text.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        static int MillimetersToFeet(int millimeters)
        {
            return (int)Math.Round(millimeters / 1000.0 / MetersPerFoot);
        }

        static int CentimetersPerSecondToKnots(ushort centimetersPerSecond)
        {
            return (int)Math.Round(centimetersPerSecond * 0.01 / MetersPerSecondPerKnot);
        }

        // Both positive up, so the sign passes through.
        static int CentimetersPerSecondToFeetPerMinute(short centimetersPerSecond)
        {
            return (int)Math.Round(centimetersPerSecond * 0.01 / MetersPerFoot * 60.0);
        }
    }
}
