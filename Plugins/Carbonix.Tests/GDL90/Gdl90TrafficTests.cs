using System;
using System.Linq;
using System.Text;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Utilities;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The ADSB_VEHICLE to traffic report mapping. Most of these are about the
    /// validity flags: an unhonored one turns data the receiver told us it did not
    /// have into a confident claim on the wire.
    /// </summary>
    [TestClass]
    public class Gdl90TrafficMappingTests
    {
        const MAVLink.ADSB_FLAGS AllValid =
            MAVLink.ADSB_FLAGS.VALID_COORDS |
            MAVLink.ADSB_FLAGS.VALID_ALTITUDE |
            MAVLink.ADSB_FLAGS.VALID_HEADING |
            MAVLink.ADSB_FLAGS.VALID_VELOCITY |
            MAVLink.ADSB_FLAGS.VALID_CALLSIGN |
            MAVLink.ADSB_FLAGS.VERTICAL_VELOCITY_VALID;

        internal static MAVLink.mavlink_adsb_vehicle_t Target(
            uint icao = 0x7C4B1A,
            MAVLink.ADSB_FLAGS flags = AllValid,
            byte tslc = 0,
            int latE7 = -338688000,
            int lonE7 = 1512093000,
            int altitudeMillimeters = 1000000,
            ushort headingCentidegrees = 27140,
            ushort horizontalVelocityCms = 4500,
            short verticalVelocityCms = 325,
            string callsign = "QFA123",
            MAVLink.ADSB_EMITTER_TYPE emitter = MAVLink.ADSB_EMITTER_TYPE.LARGE,
            MAVLink.ADSB_ALTITUDE_TYPE altitudeType = MAVLink.ADSB_ALTITUDE_TYPE.PRESSURE_QNH)
        {
            var raw = new byte[9];
            if (callsign != null)
            {
                byte[] encoded = Encoding.ASCII.GetBytes(callsign);
                Array.Copy(encoded, raw, Math.Min(encoded.Length, 8));
            }

            return new MAVLink.mavlink_adsb_vehicle_t
            {
                ICAO_address = icao,
                lat = latE7,
                lon = lonE7,
                altitude = altitudeMillimeters,
                heading = headingCentidegrees,
                hor_velocity = horizontalVelocityCms,
                ver_velocity = verticalVelocityCms,
                flags = (ushort)flags,
                squawk = 1200,
                altitude_type = (byte)altitudeType,
                callsign = raw,
                emitter_type = (byte)emitter,
                tslc = tslc,
            };
        }

        static MAVLink.ADSB_FLAGS Without(MAVLink.ADSB_FLAGS flag)
        {
            return AllValid & ~flag;
        }

        [TestMethod]
        public void BuildReport_MapsAFullyValidTarget()
        {
            var report = Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero);

            Assert.AreEqual(0x7C4B1A, report.Address);
            Assert.AreEqual(Gdl90AddressType.AdsbIcao, report.AddressType);
            Assert.IsFalse(report.TrafficAlert, "no collision avoidance is computed here");

            Assert.IsTrue(report.PositionValid);
            Assert.AreEqual(-338688000L, report.LatitudeE7);
            Assert.AreEqual(1512093000L, report.LongitudeE7);

            Assert.IsTrue(report.AltitudeValid);
            Assert.AreEqual(3281, report.PressureAltitudeFeet, "1000 m in whole feet");

            Assert.IsTrue(report.Airborne);
            Assert.IsFalse(report.Extrapolated);

            Assert.AreEqual(Gdl90TrackType.TrueTrack, report.TrackType);
            Assert.AreEqual(271.4, report.TrackDegrees, 1e-9);

            Assert.IsTrue(report.HorizontalVelocityValid);
            Assert.AreEqual(87, report.HorizontalVelocityKnots, "45 m/s in knots");

            Assert.IsTrue(report.VerticalVelocityValid);
            Assert.AreEqual(640, report.VerticalVelocityFpm, "3.25 m/s up in fpm");

            Assert.AreEqual((int)MAVLink.ADSB_EMITTER_TYPE.LARGE, report.EmitterCategory);
            Assert.AreEqual("QFA123", report.Callsign);
            Assert.AreEqual(0, report.EmergencyPriorityCode);
        }

        /// <summary>
        /// ADSB_VEHICLE carries no integrity or accuracy field of any kind, so neither
        /// can be claimed. Zero is the ICD's "unknown" (Table 10).
        /// </summary>
        [TestMethod]
        public void BuildReport_ReportsIntegrityAndAccuracyAsUnknown()
        {
            var report = Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero);

            Assert.AreEqual(Gdl90Ownship.NicUnknown, report.Nic);
            Assert.AreEqual(Gdl90Ownship.NacpUnknown, report.Nacp);
        }

        [TestMethod]
        public void BuildReport_WithoutValidCoords_MarksThePositionInvalid()
        {
            var target = Target(flags: Without(MAVLink.ADSB_FLAGS.VALID_COORDS));

            var report = Gdl90Traffic.BuildReport(target, TimeSpan.Zero);

            Assert.IsFalse(report.PositionValid);
            Assert.AreEqual(target.lat, report.LatitudeE7,
                "what the target claimed reaches the log beside the flag that rejected it");
            Assert.AreEqual(target.lon, report.LongitudeE7);
        }

        [TestMethod]
        public void BuildReport_WithoutValidAltitude_ReportsNoAltitude()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VALID_ALTITUDE)), TimeSpan.Zero);

            Assert.IsFalse(report.AltitudeValid);
        }

        /// <summary>
        /// The GDL 90 altitude field is pressure altitude. A geometric one would need to be
        /// converted to pressure altitude somehow, which would need to be done with care and would
        /// need dedicated testing. We don't expect any of these to come in, so we are side-stepping
        /// the problem and more honestly marking the altitude as unknown in the unlikely case we
        /// receive one.
        /// </summary>
        [TestMethod]
        public void BuildReport_WithAGeometricAltitude_ReportsNoAltitude()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(altitudeType: MAVLink.ADSB_ALTITUDE_TYPE.GEOMETRIC), TimeSpan.Zero);

            Assert.IsFalse(report.AltitudeValid,
                "a geometric altitude is not the quantity this field carries");
        }

        /// <summary>
        /// MAVLink's heading is course over ground, so the 1090ES heading-versus-track
        /// distinction is already gone. True Track or nothing, never either heading type.
        /// </summary>
        [TestMethod]
        public void BuildReport_WithoutValidHeading_ReportsAnInvalidTrack()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VALID_HEADING)), TimeSpan.Zero);

            Assert.AreEqual(Gdl90TrackType.Invalid, report.TrackType);
            Assert.AreEqual(0.0, report.TrackDegrees, 1e-9);
        }

        [TestMethod]
        public void BuildReport_WithoutValidVelocity_ReportsNoHorizontalVelocity()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VALID_VELOCITY)), TimeSpan.Zero);

            Assert.IsFalse(report.HorizontalVelocityValid);
        }

        /// <summary>
        /// Zero velocity is a valid value, and must not be treated as missing/invalid.
        /// </summary>
        [TestMethod]
        public void BuildReport_WithAValidZeroVelocity_ReportsAStationaryTarget()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(horizontalVelocityCms: 0), TimeSpan.Zero);

            Assert.IsTrue(report.HorizontalVelocityValid, "zero knots is a claim, not an absence");
            Assert.AreEqual(0, report.HorizontalVelocityKnots);
        }

        [TestMethod]
        public void BuildReport_WithoutVerticalVelocityValid_ReportsNoVerticalVelocity()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VERTICAL_VELOCITY_VALID)),
                TimeSpan.Zero);

            Assert.IsFalse(report.VerticalVelocityValid);
        }

        [TestMethod]
        public void BuildReport_WithADescendingTarget_KeepsTheSign()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(verticalVelocityCms: -325), TimeSpan.Zero);

            Assert.AreEqual(-640, report.VerticalVelocityFpm,
                "both are positive up, so the sign passes straight through");
        }

        /// <summary>
        /// Zero climb rate is a valid value, and must not be treated as missing/invalid.
        /// </summary>
        [TestMethod]
        public void BuildReport_WithAValidZeroVerticalVelocity_ReportsALevelTarget()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(verticalVelocityCms: 0), TimeSpan.Zero);

            Assert.IsTrue(report.VerticalVelocityValid);
            Assert.AreEqual(0, report.VerticalVelocityFpm);
        }

        /// <summary>The encoder pads a blank callsign out to eight spaces (ICD 3.5.1.11).</summary>
        [TestMethod]
        public void BuildReport_WithoutValidCallsign_TransmitsABlankCallsign()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VALID_CALLSIGN)), TimeSpan.Zero);

            Assert.IsNull(report.Callsign);

            byte[] message = Gdl90Messages.TrafficReport(report);
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)0x20, 8).ToArray(),
                Gdl90TestUtil.Slice(message, 19, 8));
        }

        /// <summary>
        /// ADSB_EMITTER_TYPE is byte-identical to the ICD Table 11 categories over its
        /// whole range, so every value must survive the mapping unchanged.
        /// </summary>
        [TestMethod]
        public void BuildReport_PassesEveryEmitterCategoryStraightThrough()
        {
            foreach (MAVLink.ADSB_EMITTER_TYPE emitter in
                Enum.GetValues(typeof(MAVLink.ADSB_EMITTER_TYPE)))
            {
                var report = Gdl90Traffic.BuildReport(Target(emitter: emitter), TimeSpan.Zero);

                Assert.AreEqual((int)emitter, report.EmitterCategory, emitter.ToString());
                Assert.AreEqual((byte)emitter, Gdl90Messages.TrafficReport(report)[18]);
            }
        }

        /// <summary>
        /// ADSB_VEHICLE has no air/ground state, so the emitter category is the only
        /// evidence: surface vehicles are on the ground and everything else, a point
        /// obstacle included, stays airborne where a receiver cannot hide it.
        /// </summary>
        [TestMethod]
        public void BuildReport_MarksSurfaceVehiclesOnTheGround()
        {
            Assert.IsFalse(Airborne(MAVLink.ADSB_EMITTER_TYPE.EMERGENCY_SURFACE));
            Assert.IsFalse(Airborne(MAVLink.ADSB_EMITTER_TYPE.SERVICE_SURFACE));
            Assert.IsTrue(Airborne(MAVLink.ADSB_EMITTER_TYPE.POINT_OBSTACLE));
            Assert.IsTrue(Airborne(MAVLink.ADSB_EMITTER_TYPE.NO_INFO));
        }

        static bool Airborne(MAVLink.ADSB_EMITTER_TYPE emitter)
        {
            return Gdl90Traffic.BuildReport(Target(emitter: emitter), TimeSpan.Zero).Airborne;
        }

        [TestMethod]
        public void BuildReport_MarksAnExtrapolatedReport()
        {
            byte[] updated = Gdl90Messages.TrafficReport(
                Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero));
            byte[] extrapolated = Gdl90Messages.TrafficReport(
                Gdl90Traffic.BuildReport(Target(), TimeSpan.FromSeconds(3)));

            // ICD 3.5.1.5: the miscellaneous nibble is the low half of byte 12, bit 2.
            Assert.AreEqual(0x0, updated[12] & 0x4);
            Assert.AreEqual(0x4, extrapolated[12] & 0x4);
        }

        [TestMethod]
        public void BuildReport_EncodesAsATrafficReport()
        {
            byte[] message = Gdl90Messages.TrafficReport(
                Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero));

            Assert.AreEqual(Gdl90Messages.TrafficReportId, message[0]);
            Assert.AreEqual(28, message.Length);
        }

        /// <summary>
        /// ICD 3.5: every traffic report has a time of applicability of the current
        /// second, so the position must be carried forward to it. Due north at
        /// 100 m/s for 10 s is 1000 m, which is a shade under a hundredth of a degree
        /// of latitude whichever Earth radius the move uses.
        /// </summary>
        [TestMethod]
        public void BuildReport_CarriesThePositionForwardAlongItsTrack()
        {
            var north = Target(latE7: 0, lonE7: 0, headingCentidegrees: 0,
                horizontalVelocityCms: 10000);

            var report = Gdl90Traffic.BuildReport(north, TimeSpan.FromSeconds(10));

            Assert.AreEqual(89900, report.LatitudeE7, 500, "1000 m north");
            Assert.AreEqual(0L, report.LongitudeE7, "due north moves no longitude");
        }

        /// <summary>The move is the distance and bearing asked for.</summary>
        [TestMethod]
        public void BuildReport_MovesTheDistanceAndBearingItWasGiven()
        {
            var start = new PointLatLngAlt(-33.8688, 151.2093);
            var target = Target(latE7: -338688000, lonE7: 1512093000,
                headingCentidegrees: 4500, horizontalVelocityCms: 15000);

            var report = Gdl90Traffic.BuildReport(target, TimeSpan.FromSeconds(8));
            var moved = new PointLatLngAlt(report.LatitudeE7 / 1e7, report.LongitudeE7 / 1e7);

            // 150 m/s for 8 s, north-east. The tolerance covers Mission Planner's own
            // helpers disagreeing about the Earth: newpos moves on a 6378.1 km sphere
            // and GetDistance measures on a 6371 km one, so a measured 1200 m comes
            // back 1.3 m short.
            Assert.AreEqual(1200.0, start.GetDistance(moved), 3.0);
            Assert.AreEqual(45.0, start.GetBearing(moved), 0.1);
        }

        /// <summary>
        /// newpos adds an offset without wrapping, so a target crossing the
        /// antimeridian comes back as 180.5 unless it is folded. The log records what
        /// the encoder was given, and 180.5 is not a longitude.
        /// </summary>
        [TestMethod]
        public void BuildReport_FoldsALongitudeAcrossTheAntimeridian()
        {
            var crossing = Target(latE7: 0, lonE7: 1799990000, headingCentidegrees: 9000,
                horizontalVelocityCms: 20000);

            var report = Gdl90Traffic.BuildReport(crossing, TimeSpan.FromSeconds(10));

            Assert.IsTrue(report.LongitudeE7 < 0,
                $"expected a wrap into the western hemisphere, got {report.LongitudeE7}");
            Assert.IsTrue(report.LongitudeE7 > -1800000000);
        }

        [TestMethod]
        public void BuildReport_CarriesEastingWithTheLatitudeScaling()
        {
            // Due east at 60 degrees of latitude, where a degree of longitude is half
            // its length at the equator.
            var east = Target(latE7: 600000000, lonE7: 0, headingCentidegrees: 9000,
                horizontalVelocityCms: 10000);

            var report = Gdl90Traffic.BuildReport(east, TimeSpan.FromSeconds(10));

            Assert.AreEqual(600000000L, report.LatitudeE7, 2000, "due east holds latitude");
            Assert.AreEqual(2 * 1000.0 / 111195.0 * 1e7, report.LongitudeE7, 4000,
                "1000 m east is twice the longitude change it would be at the equator");
        }

        [TestMethod]
        public void BuildReport_CarriesTheAltitudeForwardAtItsClimbRate()
        {
            // 5 m/s up for 10 s is 50 m on top of the 1000 m it was at.
            var climbing = Target(altitudeMillimeters: 1000000, verticalVelocityCms: 500);

            var report = Gdl90Traffic.BuildReport(climbing, TimeSpan.FromSeconds(10));

            Assert.AreEqual((int)Math.Round(1050 / 0.3048), report.PressureAltitudeFeet);
        }

        /// <summary>
        /// Nothing legitimate moves that far within the drop timeout, so a move past
        /// the cap is a wrong input and the position is held instead.
        /// </summary>
        [TestMethod]
        public void BuildReport_HoldsThePositionRatherThanCarryItPastTheCap()
        {
            var runaway = Target(horizontalVelocityCms: ushort.MaxValue);
            var age = TimeSpan.FromSeconds(
                Gdl90Traffic.MaxPropagationMeters / (ushort.MaxValue * 0.01) + 1.0);

            var report = Gdl90Traffic.BuildReport(runaway, age);

            Assert.AreEqual(-338688000L, report.LatitudeE7);
            Assert.AreEqual(1512093000L, report.LongitudeE7);
            Assert.IsTrue(report.Extrapolated);
        }

        /// <summary>
        /// Without a course and ground speed there is nothing to propagate along, and
        /// the last known position is the only statement available.
        /// </summary>
        [TestMethod]
        public void BuildReport_WithoutAVelocityVector_HoldsThePosition()
        {
            foreach (var missing in new[]
                     { MAVLink.ADSB_FLAGS.VALID_HEADING, MAVLink.ADSB_FLAGS.VALID_VELOCITY })
            {
                var report = Gdl90Traffic.BuildReport(
                    Target(flags: Without(missing)), TimeSpan.FromSeconds(10));

                Assert.AreEqual(-338688000L, report.LatitudeE7, missing.ToString());
                Assert.AreEqual(1512093000L, report.LongitudeE7, missing.ToString());
            }
        }

        /// <summary>An unknown climb rate is not an assumption that it is level.</summary>
        [TestMethod]
        public void BuildReport_WithoutAClimbRate_HoldsTheAltitude()
        {
            var report = Gdl90Traffic.BuildReport(
                Target(flags: Without(MAVLink.ADSB_FLAGS.VERTICAL_VELOCITY_VALID)),
                TimeSpan.FromSeconds(10));

            Assert.AreEqual(3281, report.PressureAltitudeFeet);
        }

        [TestMethod]
        public void BuildReport_WithFreshData_ChangesNothing()
        {
            var report = Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero);

            Assert.AreEqual(-338688000L, report.LatitudeE7);
            Assert.AreEqual(1512093000L, report.LongitudeE7);
            Assert.AreEqual(3281, report.PressureAltitudeFeet);
            Assert.IsFalse(report.Extrapolated);
        }

        /// <summary>
        /// The bit is thresholded at one transmit interval; the position is carried
        /// forward by the actual age however small it is.
        /// </summary>
        [TestMethod]
        public void BuildReport_SetsTheExtrapolatedBitFromTheAge()
        {
            Assert.IsFalse(Gdl90Traffic.BuildReport(Target(),
                TimeSpan.FromMilliseconds(999)).Extrapolated);
            Assert.IsTrue(Gdl90Traffic.BuildReport(Target(),
                Gdl90Traffic.UpdatedWithin).Extrapolated);
        }

        [TestMethod]
        public void IsAlert_TreatsBothOfArduPilotsThreatLevelsAsAnAlert()
        {
            Assert.IsFalse(Gdl90Traffic.IsAlert(MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE));
            Assert.IsTrue(Gdl90Traffic.IsAlert(MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW));
            Assert.IsTrue(Gdl90Traffic.IsAlert(MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH));
        }

        [TestMethod]
        public void BuildReport_LogsTheThreatLevelBehindTheAlert()
        {
            var report = Gdl90Traffic.BuildReport(Target(), TimeSpan.Zero,
                MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW);

            Assert.IsTrue(report.TrafficAlert);
            Assert.AreEqual((int)MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW, report.ThreatLevel);
        }

        [TestMethod]
        public void DecodeCallsign_StopsAtTheTerminatorAndRejectsRubbish()
        {
            Assert.AreEqual("VHABC", Gdl90Traffic.DecodeCallsign(
                new byte[] { (byte)'V', (byte)'H', (byte)'A', (byte)'B', (byte)'C', 0, (byte)'X', 0, 0 }));
            Assert.IsNull(Gdl90Traffic.DecodeCallsign(new byte[9]));
            Assert.IsNull(Gdl90Traffic.DecodeCallsign(null));
            Assert.IsNull(Gdl90Traffic.DecodeCallsign(
                new byte[] { 0x20, 0x20, 0x20, 0, 0, 0, 0, 0, 0 }), "spaces are not a callsign");
        }
    }

    [TestClass]
    public class Gdl90TrafficAgingTests
    {
        static readonly DateTime T = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// tslc is the source's own view of how stale the target already was when it
        /// told us; the transport delay is on top of that. Neither alone is the age of
        /// the position about to go on the wire.
        /// </summary>
        [TestMethod]
        public void EffectiveAge_AddsTheSourcesStalenessToOurs()
        {
            var vehicle = Gdl90TrafficMappingTests.Target(tslc: 4);

            Assert.AreEqual(TimeSpan.FromSeconds(6),
                Gdl90TrafficTracker.EffectiveAge(vehicle, T, T.AddSeconds(2)));
        }

        [TestMethod]
        public void EffectiveAge_TreatsAFutureReceiveTimeAsNow()
        {
            var vehicle = Gdl90TrafficMappingTests.Target(tslc: 1);

            Assert.AreEqual(TimeSpan.FromSeconds(1),
                Gdl90TrafficTracker.EffectiveAge(vehicle, T.AddSeconds(5), T));
        }

        [TestMethod]
        public void ClassifyDrop_ForwardsAHealthyTarget()
        {
            Assert.IsNull(Gdl90TrafficTracker.ClassifyDrop(Gdl90TrafficMappingTests.Target(),
                TimeSpan.Zero, ownshipIcao: 0x7C1A2B));
        }

        /// <summary>Our own transponder comes back over the link; forwarding it would show us twice.</summary>
        [TestMethod]
        public void ClassifyDrop_DropsOurOwnAddress()
        {
            var vehicle = Gdl90TrafficMappingTests.Target(icao: 0x7C1A2B);

            Assert.AreEqual(Gdl90TrafficDropReason.Ownship,
                Gdl90TrafficTracker.ClassifyDrop(vehicle, TimeSpan.Zero, ownshipIcao: 0x7C1A2B));
        }

        /// <summary>
        /// With no identity configured the service does not transmit at all, so the
        /// zero must not match a target that happens to carry a zero address either -
        /// that one is dropped as unaddressed instead.
        /// </summary>
        [TestMethod]
        public void ClassifyDrop_DropsAnUnusableAddress()
        {
            Assert.AreEqual(Gdl90TrafficDropReason.InvalidAddress,
                Gdl90TrafficTracker.ClassifyDrop(Gdl90TrafficMappingTests.Target(icao: 0),
                    TimeSpan.Zero, ownshipIcao: 0));

            Assert.AreEqual(Gdl90TrafficDropReason.InvalidAddress,
                Gdl90TrafficTracker.ClassifyDrop(Gdl90TrafficMappingTests.Target(icao: 0x1000000),
                    TimeSpan.Zero, ownshipIcao: 0x7C1A2B), "25 bits");
        }

        [TestMethod]
        public void ClassifyDrop_DropsATargetNobodyHasHeardFromRecently()
        {
            var vehicle = Gdl90TrafficMappingTests.Target();

            Assert.IsNull(Gdl90TrafficTracker.ClassifyDrop(vehicle,
                Gdl90TrafficTracker.DropAfter, ownshipIcao: 0x7C1A2B));

            Assert.AreEqual(Gdl90TrafficDropReason.Stale,
                Gdl90TrafficTracker.ClassifyDrop(vehicle,
                    Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(1),
                    ownshipIcao: 0x7C1A2B));
        }

        /// <summary>
        /// A quiet target is normal where quiet ownship telemetry is a fault, so the
        /// traffic timeout is deliberately the looser of the two.
        /// </summary>
        [TestMethod]
        public void DropAfter_IsLongerThanTheOwnshipPositionTimeout()
        {
            Assert.IsTrue(Gdl90TrafficTracker.DropAfter > Gdl90VehicleReader.PositionStaleAfter);
        }
    }

    [TestClass]
    public class Gdl90TrafficTrackerTests
    {
        static readonly DateTime T = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);
        const int OwnshipIcao = 0x7C1A2B;

        static Gdl90TrafficTracker Tracker()
        {
            return new Gdl90TrafficTracker(() => T);
        }

        /// <summary>
        /// The reason traffic cannot be polled from getPacketLast: that is keyed by
        /// message id alone, so it would return whichever aircraft arrived most
        /// recently and quietly discard the rest of the sky.
        /// </summary>
        [TestMethod]
        public void Collect_KeepsEveryTargetSeparately()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x111111), T);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x222222), T);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), T);

            var forwarded = tracker.Collect(T, OwnshipIcao)
                .Where(r => r.DropReason == null)
                .Select(r => (int)r.Vehicle.ICAO_address)
                .OrderBy(a => a)
                .ToList();

            CollectionAssert.AreEqual(new[] { 0x111111, 0x222222, 0x333333 }, forwarded);
        }

        [TestMethod]
        public void Accept_ReplacesWhatIsKnownAboutATarget()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(altitudeMillimeters: 1000000), T);
            tracker.Accept(Gdl90TrafficMappingTests.Target(altitudeMillimeters: 2000000), T);

            var results = tracker.Collect(T, OwnshipIcao);

            Assert.AreEqual(1, tracker.Count);
            Assert.AreEqual(2000000, results.Single().Vehicle.altitude);
        }

        [TestMethod]
        public void Collect_AgesATargetFromItsArrival()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(), T);

            var fresh = tracker.Collect(T.AddMilliseconds(200), OwnshipIcao).Single();
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), fresh.Age);
            Assert.IsFalse(Gdl90Traffic.BuildReport(fresh.Vehicle, fresh.Age).Extrapolated);

            var coasted = tracker.Collect(T.AddSeconds(3), OwnshipIcao).Single();
            Assert.AreEqual(TimeSpan.FromSeconds(3), coasted.Age);
            Assert.IsTrue(Gdl90Traffic.BuildReport(coasted.Vehicle, coasted.Age).Extrapolated);
        }

        /// <summary>The source's own staleness counts, even on a message that just arrived.</summary>
        [TestMethod]
        public void Collect_CountsTheSourcesOwnStaleness()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(tslc: 5), T);

            Assert.AreEqual(TimeSpan.FromSeconds(5), tracker.Collect(T, OwnshipIcao).Single().Age);
        }

        [TestMethod]
        public void Collect_DropsAndForgetsATargetThatStoppedArriving()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(), T);

            var late = T + Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(1);
            var result = tracker.Collect(late, OwnshipIcao).Single();

            Assert.AreEqual(Gdl90TrafficDropReason.Stale, result.DropReason);
            Assert.IsTrue(result.DropIsNew, "the drop must reach the log");
            Assert.AreEqual(0, tracker.Count, "and the target is forgotten");
        }

        /// <summary>
        /// Targets arrive whether or not the stream is on, and with it off nothing
        /// collects them, so silence alone has to forget them.
        /// </summary>
        [TestMethod]
        public void Prune_ForgetsTheTargetsThatStoppedArrivingAndNothingElse()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x111111), T);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x222222), T + Gdl90TrafficTracker.DropAfter);

            var late = T + Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(1);
            tracker.Prune(late);

            Assert.AreEqual(1, tracker.Count);
            Assert.AreEqual(0x222222u, tracker.Collect(late, OwnshipIcao).Single().Vehicle.ICAO_address);
        }

        /// <summary>
        /// An undroppable target keeps arriving for as long as the receiver keeps
        /// sending it, and its drop belongs in the log once rather than every second.
        /// </summary>
        [TestMethod]
        public void Collect_ReportsARepeatedDropOnlyOnce()
        {
            var tracker = Tracker();
            var unusable = Gdl90TrafficMappingTests.Target(icao: 0);

            tracker.Accept(unusable, T);
            Assert.IsTrue(tracker.Collect(T, OwnshipIcao).Single().DropIsNew);

            tracker.Accept(unusable, T.AddSeconds(1));
            var second = tracker.Collect(T.AddSeconds(1), OwnshipIcao).Single();
            Assert.AreEqual(Gdl90TrafficDropReason.InvalidAddress, second.DropReason);
            Assert.IsFalse(second.DropIsNew);
        }

        /// <summary>
        /// A target the source keeps describing as long-stale is still arriving, so it
        /// must not be forgotten and re-added every second - that would log the same
        /// drop over and over.
        /// </summary>
        [TestMethod]
        public void Collect_KeepsAStaleTargetThatIsStillArriving()
        {
            var tracker = Tracker();
            var stale = Gdl90TrafficMappingTests.Target(tslc: 200);

            tracker.Accept(stale, T);
            Assert.IsTrue(tracker.Collect(T, OwnshipIcao).Single().DropIsNew);

            tracker.Accept(stale, T.AddSeconds(1));
            Assert.IsFalse(tracker.Collect(T.AddSeconds(1), OwnshipIcao).Single().DropIsNew);
            Assert.AreEqual(1, tracker.Count);
        }

        [TestMethod]
        public void Collect_ReportsADropAgainAfterTheTargetRecovers()
        {
            var tracker = Tracker();

            // Stale and back: the same target heard, lost past DropAfter, then heard
            // again. The second drop is a new one, not a continuation of the first.
            tracker.Accept(Gdl90TrafficMappingTests.Target(), T);
            var stale = T + Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(1);
            Assert.IsTrue(tracker.Collect(stale, OwnshipIcao).Single().DropIsNew);

            tracker.Accept(Gdl90TrafficMappingTests.Target(), stale.AddSeconds(1));
            Assert.IsNull(tracker.Collect(stale.AddSeconds(1), OwnshipIcao).Single().DropReason);

            var staleAgain = stale + Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(2);
            Assert.IsTrue(tracker.Collect(staleAgain, OwnshipIcao).Single().DropIsNew);
        }

        [TestMethod]
        public void Collect_CarriesTheThreatTheAircraftReported()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), T);

            Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE,
                tracker.Collect(T, OwnshipIcao).Single().ThreatLevel,
                "nothing is alerted until the aircraft says so");

            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW);

            Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW,
                tracker.Collect(T, OwnshipIcao).Single().ThreatLevel);
        }

        [TestMethod]
        public void Collect_ClearsTheThreatWhenTheAircraftDoes()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), T);
            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);
            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE);

            Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE,
                tracker.Collect(T, OwnshipIcao).Single().ThreatLevel);
        }

        /// <summary>
        /// The threat has no clock of its own: it stands for as long as the target it
        /// belongs to, which is already aged on its own position stream. Holding an
        /// alert too long is the safe direction; dropping one is not.
        /// </summary>
        [TestMethod]
        public void Collect_HoldsTheThreatWhileOnlyTheCollisionMessagesStop()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), T);
            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);

            // Position messages keep arriving; nothing more is ever said about the threat.
            var later = T.AddSeconds(60);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), later);

            Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH,
                tracker.Collect(later, OwnshipIcao).Single().ThreatLevel);
        }

        /// <summary>And it dies with the target, rather than outliving it.</summary>
        [TestMethod]
        public void Collect_ForgetsTheThreatWithTheTarget()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), T);
            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);

            var gone = T + Gdl90TrafficTracker.DropAfter + TimeSpan.FromSeconds(1);
            tracker.Collect(gone, OwnshipIcao);
            Assert.AreEqual(0, tracker.Count);

            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), gone);

            Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE,
                tracker.Collect(gone, OwnshipIcao).Single().ThreatLevel,
                "a target that comes back is not still carrying the old alert");
        }

        /// <summary>
        /// A threat naming an aircraft we have never heard of would be an alarm with
        /// no target behind it.
        /// </summary>
        [TestMethod]
        public void AcceptCollision_IgnoresATargetItHasNeverSeen()
        {
            var tracker = Tracker();
            tracker.AcceptCollision(0x444444, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);

            Assert.AreEqual(0, tracker.Count);
            Assert.AreEqual(0, tracker.Collect(T, OwnshipIcao).Count);
        }

        [TestMethod]
        public void Collect_DoesNotForwardOurself()
        {
            var tracker = Tracker();
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: (uint)OwnshipIcao), T);

            Assert.AreEqual(Gdl90TrafficDropReason.Ownship,
                tracker.Collect(T, OwnshipIcao).Single().DropReason);
        }
    }

    /// <summary>
    /// The tracker's MAVLink subscriptions: which messages it believes, and from whom.
    /// </summary>
    [TestClass]
    public class Gdl90TrafficSubscriptionTests
    {
        static readonly DateTime T = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);

        static MAVLink.MAVLinkMessage From(byte sysid, MAVLink.MAVLINK_MSG_ID id, object data)
        {
            byte[] packet = new MAVLink.MavlinkParse().GenerateMAVLinkPacket20(id, data, false, sysid, 1);
            return new MAVLink.MAVLinkMessage(packet, T);
        }

        static MAVLink.mavlink_collision_t Collision(MAVLink.MAV_COLLISION_SRC src, uint id)
        {
            return new MAVLink.mavlink_collision_t
            {
                src = (byte)src,
                id = id,
                threat_level = (byte)MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH,
            };
        }

        [TestMethod]
        public void Bind_AcceptsTargetsFromTheSelectedVehicleOnly()
        {
            using (var port = new MAVLinkInterface())
            {
                port.sysidcurrent = 1;
                port.compidcurrent = 1;

                var tracker = new Gdl90TrafficTracker(() => T);
                tracker.Bind(port);

                tracker.OnAdsbVehicle(From(2, MAVLink.MAVLINK_MSG_ID.ADSB_VEHICLE,
                    Gdl90TrafficMappingTests.Target(icao: 0x222222)));
                Assert.AreEqual(0, tracker.Count, "another system's targets are not ours");

                tracker.OnAdsbVehicle(From(1, MAVLink.MAVLINK_MSG_ID.ADSB_VEHICLE,
                    Gdl90TrafficMappingTests.Target(icao: 0x111111)));
                Assert.AreEqual(1, tracker.Count);

                tracker.Unbind();
            }
        }

        [TestMethod]
        public void Bind_BelievesACollisionOnlyFromTheAdsbSource()
        {
            using (var port = new MAVLinkInterface())
            {
                port.sysidcurrent = 1;
                port.compidcurrent = 1;

                var tracker = new Gdl90TrafficTracker(() => T);
                tracker.Bind(port);
                tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000002), T);

                tracker.OnCollision(From(1, MAVLink.MAVLINK_MSG_ID.COLLISION,
                    Collision(MAVLink.MAV_COLLISION_SRC.MAVLINK_GPS_GLOBAL_INT, 2)));
                Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE,
                    tracker.Collect(T, 0x7C1A2B).Single().ThreatLevel);

                tracker.OnCollision(From(1, MAVLink.MAVLINK_MSG_ID.COLLISION,
                    Collision(MAVLink.MAV_COLLISION_SRC.ADSB, 2)));
                Assert.AreEqual(MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH,
                    tracker.Collect(T, 0x7C1A2B).Single().ThreatLevel);

                tracker.Unbind();
            }
        }

        [TestMethod]
        public void Unbind_StopsListening()
        {
            using (var port = new MAVLinkInterface())
            {
                port.sysidcurrent = 1;
                port.compidcurrent = 1;

                var tracker = new Gdl90TrafficTracker(() => T);
                tracker.Bind(port);
                tracker.Unbind();

                tracker.OnAdsbVehicle(From(1, MAVLink.MAVLINK_MSG_ID.ADSB_VEHICLE,
                    Gdl90TrafficMappingTests.Target()));

                Assert.AreEqual(0, tracker.Count);
            }
        }
    }
}
