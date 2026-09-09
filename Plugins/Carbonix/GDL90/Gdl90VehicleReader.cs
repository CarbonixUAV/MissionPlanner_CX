using System;
using System.Collections.Generic;
using MissionPlanner;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Reads a link's most recent messages into a <see cref="Gdl90VehicleState"/>,
    /// leaving out any that are no longer current.
    /// </summary>
    /// <remarks>
    /// One instance serves one stream and is read once per second: the track source
    /// carries over from one read to the next.
    /// </remarks>
    public sealed class Gdl90VehicleReader
    {
        /// <summary>Represents the age past which a message's values are no longer current.</summary>
        // Six to twelve packets on the streams this gates (2 Hz leases, 4 Hz attitude),
        // so tripping it is a dropout rather than jitter.
        public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

        /// <summary>Represents the age past which GLOBAL_POSITION_INT's values are no longer current.</summary>
        // Tighter than StaleAfter: the position is the one field a receiver acts on, and
        // it moves while it sits there. Four packets at the 2 Hz default.
        public static readonly TimeSpan PositionStaleAfter = TimeSpan.FromSeconds(2);

        /// <summary>Represents the ground speed below which the report carries heading instead of track.</summary>
        // Two thresholds rather than one: the track type tells the receiver which
        // quantity the field holds, so a bare threshold would alternate the field's
        // meaning every second rather than merely jitter a number.
        public const double HeadingBelowMetersPerSecond = 3.0;

        /// <summary>Represents the ground speed above which the report returns to carrying track.</summary>
        public const double TrackAboveMetersPerSecond = 6.0;

        Gdl90TrackType _trackSource = Gdl90TrackType.TrueTrack;

        /// <summary>Reads the link's most recent messages as of the given instant.</summary>
        /// <param name="mav">The vehicle on the link, or null when there is no link.</param>
        /// <param name="nowUtc">The instant every age is measured against.</param>
        /// <returns>The observation, holding only what is current.</returns>
        public Gdl90VehicleState Read(MAVState mav, DateTime nowUtc)
        {
            var state = new Gdl90VehicleState();
            var missing = new List<string>();

            var estimator = Expect(mav, MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT, PositionStaleAfter, nowUtc, missing);
            bool receiverCurrent = Expect(mav, MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, StaleAfter, nowUtc, missing) != null;
            var pressure = Expect(mav, MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE, StaleAfter, nowUtc, missing);
            var attitude = Expect(mav, MAVLink.MAVLINK_MSG_ID.ATTITUDE, StaleAfter, nowUtc, missing);
            Expect(mav, MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE, StaleAfter, nowUtc, missing);

            var receiver = Last(mav, MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT);
            if (estimator != null) ReadPosition(state, estimator, receiver);
            state.Gps = ReadGps(receiver, receiverCurrent);

            // A second receiver is optional, so its absence is not a missing message.
            var receiver2 = Last(mav, MAVLink.MAVLINK_MSG_ID.GPS2_RAW);
            state.Gps2 = ReadGps2(receiver2, IsCurrent(receiver2, nowUtc, StaleAfter));

            if (pressure != null)
                state.StaticPressureHpa = pressure.ToStructure<MAVLink.mavlink_scaled_pressure_t>().press_abs;

            if (attitude != null)
                state.YawDegrees = ReadYaw(attitude.ToStructure<MAVLink.mavlink_attitude_t>());

            // Held for the life of the connection rather than aged: the message only
            // goes missing when the whole link does, and a parked aircraft should stay
            // on the ground through a dropout. The tab does not start the stream until
            // it has arrived.
            var landed = Last(mav, MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);
            if (landed != null)
            {
                state.LandedState = (MAVLink.MAV_LANDED_STATE)landed
                    .ToStructure<MAVLink.mavlink_extended_sys_state_t>().landed_state;
            }

            state.MissingMessages = missing;
            state.TrackSource = ResolveTrackSource(state.GroundVector);

            return state;
        }

        static MAVLink.MAVLinkMessage Last(MAVState mav, MAVLink.MAVLINK_MSG_ID id)
        {
            return mav?.getPacketLast((uint)id);
        }

        static bool IsCurrent(MAVLink.MAVLinkMessage last, DateTime nowUtc, TimeSpan staleAfter)
        {
            return AgeOf(last, nowUtc) <= staleAfter;
        }

        // The message if it is current, otherwise null with its name noted as missing.
        static MAVLink.MAVLinkMessage Expect(MAVState mav, MAVLink.MAVLINK_MSG_ID id,
            TimeSpan staleAfter, DateTime nowUtc, List<string> missing)
        {
            var last = Last(mav, id);
            if (IsCurrent(last, nowUtc, staleAfter)) return last;

            missing.Add(id.ToString());
            return null;
        }

        // The position and altitude come from the estimator, falling back to the primary
        // receiver when the estimator sends the "no position" pair. Either way they are
        // aged by GLOBAL_POSITION_INT, the stream the position belongs to.
        static void ReadPosition(Gdl90VehicleState state, MAVLink.MAVLinkMessage estimator,
            MAVLink.MAVLinkMessage receiver)
        {
            var loc = estimator.ToStructure<MAVLink.mavlink_global_position_int_t>();
            state.GroundVector = new Gdl90GroundVector(loc.vx * 0.01, loc.vy * 0.01, loc.vz * 0.01);

            var position = Gdl90Position.From(loc.lat, loc.lon);
            if (position != null)
            {
                state.Position = position;
                state.AltitudeMslMeters = loc.alt / 1000.0;
                return;
            }

            if (receiver == null) return;

            var gps = receiver.ToStructure<MAVLink.mavlink_gps_raw_int_t>();
            state.Position = Gdl90Position.From(gps.lat, gps.lon);
            state.AltitudeMslMeters = gps.alt / 1000.0;
        }

        static Gdl90GpsSample ReadGps(MAVLink.MAVLinkMessage receiver, bool isCurrent)
        {
            if (receiver == null) return Gdl90GpsSample.Absent;

            var gps = receiver.ToStructure<MAVLink.mavlink_gps_raw_int_t>();
            return Sample(gps.fix_type, gps.h_acc, receiver.ismavlink2, isCurrent);
        }

        static Gdl90GpsSample ReadGps2(MAVLink.MAVLinkMessage receiver, bool isCurrent)
        {
            if (receiver == null) return Gdl90GpsSample.Absent;

            var gps = receiver.ToStructure<MAVLink.mavlink_gps2_raw_t>();
            return Sample(gps.fix_type, gps.h_acc, receiver.ismavlink2, isCurrent);
        }

        // The fix type is kept from a stale message: it still says the estimator's
        // position is GPS-backed. Only the accuracy expires, since it is the one claim
        // taken from the receiver directly. h_acc is a MAVLink 2 extension, so a
        // MAVLink 1 message carries none.
        static Gdl90GpsSample Sample(byte fixType, uint accuracyMillimeters, bool mavlink2, bool isCurrent)
        {
            return new Gdl90GpsSample(fixType,
                isCurrent && mavlink2 ? accuracyMillimeters / 1000.0 : double.NaN);
        }

        // ATTITUDE.yaw is radians over +/-pi, referenced to true north. Folding it to a
        // 0-360 bearing keeps the logged value looking like one.
        static double ReadYaw(MAVLink.mavlink_attitude_t attitude)
        {
            double degrees = attitude.yaw * (180.0 / Math.PI);
            return degrees < 0.0 ? degrees + 360.0 : degrees;
        }

        // A live packet is stamped in UTC, but one read out of a tlog is stamped with the
        // recording's time converted to local (MAVLinkInterface.cs:6532). Normalizing
        // here is what stops a converted log aging every message by the UTC offset.
        static TimeSpan AgeOf(MAVLink.MAVLinkMessage last, DateTime nowUtc)
        {
            if (last == null || last.rxtime == DateTime.MinValue) return TimeSpan.MaxValue;

            var age = nowUtc - last.rxtime.ToUniversalTime();
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }

        // Hysteresis between the two speed thresholds. No usable ground speed holds the
        // previous choice rather than reinterpreting the field.
        Gdl90TrackType ResolveTrackSource(Gdl90GroundVector vector)
        {
            if (vector == null) return _trackSource;

            double speed = vector.HorizontalSpeed;
            if (speed > TrackAboveMetersPerSecond)
                _trackSource = Gdl90TrackType.TrueTrack;
            else if (speed < HeadingBelowMetersPerSecond)
                _trackSource = Gdl90TrackType.TrueHeading;

            return _trackSource;
        }
    }
}
