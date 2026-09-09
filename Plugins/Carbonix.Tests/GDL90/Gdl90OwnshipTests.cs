using System;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The mapping from a vehicle observation onto the ownship messages: which field is
    /// withheld, which altitude is which, and whether the aircraft counts as airborne.
    /// </summary>
    [TestClass]
    public class Gdl90OwnshipTests
    {
        // The test area. The only position permitted in a fixture, and stationary.
        const double FixtureLat = -33.6671869;
        const double FixtureLng = 150.8543972;
        const double FixtureAltAmslMeters = 27.8;

        // Measured at the test area on 2026-07-13 with GPS AMSL at 27.9 m.
        const double FixturePressureHpa = 1009.88f;

        // A healthy F9P hAcc, in meters. EPU 1.22 m, comfortably NACp 11.
        const double FixtureHorizontalAccuracyMeters = 0.5f;

        // The standby receiver, in a different NACp bin (EPU 9.8 m) from the primary
        // so a test can tell which one answered.
        const double FixtureHorizontalAccuracy2Meters = 4.0f;

        // Cruise: 21 m/s over the ground, tracking 137.5 degrees true, climbing at 5 m/s,
        // and crabbed 7.5 degrees so track and heading are distinguishable.
        const double FixtureGroundSpeedMeters = 21.0;
        const double FixtureTrackDegrees = 137.5;
        const double FixtureClimbMetersPerSecond = -5.0;
        const double FixtureYawDegrees = 130.0;

        const int FixtureFixType = 3;

        static readonly Gdl90OwnshipIdentity Identity = new Gdl90OwnshipIdentity
        {
            IcaoAddress = 0x7C1A2B,
            Callsign = "VHTEST",
        };

        /// <summary>
        /// A vehicle in cruise with everything current: 3D fix on both receivers,
        /// barometer, ground vector, EXTENDED_SYS_STATE reporting in-air.
        /// </summary>
        static Gdl90VehicleState Flying()
        {
            return new Gdl90VehicleState
            {
                GroundVector = Vector(FixtureGroundSpeedMeters, FixtureTrackDegrees,
                    FixtureClimbMetersPerSecond),
                LandedState = MAVLink.MAV_LANDED_STATE.IN_AIR,
                YawDegrees = FixtureYawDegrees,
                StaticPressureHpa = FixturePressureHpa,
                Gps = new Gdl90GpsSample(FixtureFixType, FixtureHorizontalAccuracyMeters),
                Gps2 = new Gdl90GpsSample(FixtureFixType, FixtureHorizontalAccuracy2Meters),
                Position = Gdl90Position.From(E7(FixtureLat), E7(FixtureLng)),
                AltitudeMslMeters = FixtureAltAmslMeters,
            };
        }

        /// <summary>GLOBAL_POSITION_INT has gone stale, taking everything it carries with it.</summary>
        static Gdl90VehicleState WithStalePosition(Gdl90VehicleState vehicle)
        {
            vehicle.Position = null;
            vehicle.AltitudeMslMeters = double.NaN;
            vehicle.GroundVector = null;
            return vehicle;
        }

        /// <summary>
        /// The estimator's velocity, as GLOBAL_POSITION_INT carries it: NED components
        /// in m/s. Down is positive down, so a climb is negative.
        /// </summary>
        static Gdl90GroundVector Vector(double metersPerSecond, double trackDegrees,
            double downMetersPerSecond = 0.0)
        {
            double radians = trackDegrees * Math.PI / 180.0;
            return new Gdl90GroundVector(
                metersPerSecond * Math.Cos(radians),
                metersPerSecond * Math.Sin(radians),
                downMetersPerSecond);
        }

        /// <summary>Both receivers at the same fix type, since either alone would carry the report.</summary>
        static void SetBothFixTypes(Gdl90VehicleState vehicle, int fixType)
        {
            vehicle.Gps = new Gdl90GpsSample(fixType, vehicle.Gps.HorizontalAccuracyMeters);
            vehicle.Gps2 = new Gdl90GpsSample(fixType, vehicle.Gps2.HorizontalAccuracyMeters);
        }

        static Gdl90OwnshipState Build(Gdl90VehicleState vehicle)
        {
            return Gdl90Ownship.Build(vehicle, Identity);
        }

        /// <summary>Degrees as the messages carry them, for a fixture written in degrees.</summary>
        static int E7(double degrees)
        {
            return (int)Math.Round(degrees * 1e7);
        }

        [TestMethod]
        public void Build_FromHealthyTelemetry_MapsEveryField()
        {
            var state = Build(Flying());
            var report = state.Report;

            Assert.AreEqual(0x7C1A2B, report.Address);
            Assert.AreEqual("VHTEST", report.Callsign);
            Assert.AreEqual(Gdl90AddressType.AdsbIcao, report.AddressType);
            Assert.AreEqual(14, report.EmitterCategory, "UAV");
            Assert.IsFalse(report.TrafficAlert);
            Assert.IsFalse(report.Extrapolated);
            Assert.AreEqual(0, report.EmergencyPriorityCode);

            Assert.IsTrue(report.PositionValid);
            Assert.AreEqual(-336671869L, report.LatitudeE7);
            Assert.AreEqual(1508543972L, report.LongitudeE7);
            Assert.AreEqual(0, report.Nic, "no containment radius is ever asserted");
            Assert.AreEqual(11, report.Nacp, "hAcc 0.5 m is an EPU of 1.22 m");

            Assert.IsTrue(report.AltitudeValid);
            Assert.AreEqual(92, report.PressureAltitudeFeet);

            Assert.IsTrue(report.HorizontalVelocityValid);
            Assert.AreEqual(41, report.HorizontalVelocityKnots, "21 m/s");

            Assert.IsTrue(report.VerticalVelocityValid);
            Assert.AreEqual(984, report.VerticalVelocityFpm, "5 m/s climb");

            Assert.AreEqual(Gdl90TrackType.TrueTrack, report.TrackType);
            Assert.AreEqual(FixtureTrackDegrees, report.TrackDegrees, 1e-3);

            Assert.IsTrue(report.Airborne);

            Assert.IsTrue(state.GeometricAltitudeValid);
            Assert.AreEqual(91, state.GeometricAltitudeFeet, "27.8 m AMSL");
        }

        [TestMethod]
        public void Build_WithNullVehicle_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => Gdl90Ownship.Build(null, Identity));
        }

        [TestMethod]
        public void Build_WithNullIdentity_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => Gdl90Ownship.Build(Flying(), null));
        }

        /// <summary>
        /// Pressure altitude comes from the barometer on the standard datum, never from
        /// the GPS altitude. On a non-standard day the two differ by hundreds of feet,
        /// and a receiver correlating our ownship against Mode C traffic needs the
        /// barometric one.
        /// </summary>
        [TestMethod]
        public void Build_TakesPressureAltitudeFromBarometerNotGpsAltitude()
        {
            var vehicle = Flying();
            vehicle.AltitudeMslMeters = 1000.0;            // 3,281 ft AMSL
            vehicle.StaticPressureHpa = FixturePressureHpa; // ~92 ft pressure altitude

            var state = Build(vehicle);

            Assert.AreEqual(92, state.Report.PressureAltitudeFeet);
            Assert.AreEqual(3281, state.GeometricAltitudeFeet);
        }

        /// <summary>
        /// A position with no fix behind it is withheld from the wire, where the
        /// encoder zeroes it, but the report still carries what the estimator said.
        /// </summary>
        [TestMethod]
        public void Build_WithoutGpsFix_MarksThePositionInvalid()
        {
            var vehicle = Flying();
            SetBothFixTypes(vehicle, 1); // no fix on either receiver

            var state = Build(vehicle);

            Assert.IsFalse(state.Report.PositionValid);
            Assert.AreEqual(E7(FixtureLat), state.Report.LatitudeE7);
            Assert.AreEqual(E7(FixtureLng), state.Report.LongitudeE7);
            Assert.AreEqual(0, state.Report.Nic);
            Assert.AreEqual(0, state.Report.Nacp, "no position means no claimed accuracy");
            Assert.IsFalse(state.GeometricAltitudeValid, "ICD 3.8: only when GPS altitude is available");
        }

        /// <summary>
        /// Position, ground vector and geometric altitude all ride GLOBAL_POSITION_INT,
        /// so they expire together and no one of them can outlive the message.
        /// </summary>
        [TestMethod]
        public void Build_WithStalePosition_DropsEverythingThatMessageCarries()
        {
            var state = Build(WithStalePosition(Flying()));

            Assert.IsFalse(state.Report.PositionValid);
            Assert.AreEqual(0L, state.Report.LatitudeE7);
            Assert.AreEqual(0L, state.Report.LongitudeE7);
            Assert.IsFalse(state.GeometricAltitudeValid);
            Assert.IsFalse(state.Report.HorizontalVelocityValid);
            Assert.IsFalse(state.Report.VerticalVelocityValid,
                "speed and climb rate are two components of one vector, aged as one");
            Assert.AreEqual(Gdl90TrackType.Invalid, state.Report.TrackType,
                "and so is the direction that vector points");

            Assert.IsTrue(state.Report.AltitudeValid,
                "pressure is on its own message and its own threshold");
        }

        [TestMethod]
        public void Build_WithNoBarometer_MarksAltitudeInvalid()
        {
            var vehicle = Flying();
            vehicle.StaticPressureHpa = 0;

            var state = Build(vehicle);

            Assert.IsFalse(state.Report.AltitudeValid);
            Assert.IsTrue(state.Report.PositionValid, "one dead sensor does not invalidate the rest");
        }

        [TestMethod]
        public void Build_WithStaleBarometer_MarksAltitudeInvalid()
        {
            var vehicle = Flying();
            vehicle.StaticPressureHpa = double.NaN;

            var state = Build(vehicle);

            Assert.IsFalse(state.Report.AltitudeValid);
            Assert.IsTrue(state.Report.PositionValid);
        }

        /// <summary>
        /// Out-of-range pressure altitude is dropped, not clamped: the encoder's
        /// sentinel says "no altitude", where a clamp would state a wrong one confidently.
        /// </summary>
        [TestMethod]
        public void Build_WithPressureBelowTheEncodableRange_MarksAltitudeInvalid()
        {
            var vehicle = Flying();
            vehicle.StaticPressureHpa = 5.0; // far above the 101,350 ft ceiling of the altitude field

            Assert.IsFalse(Build(vehicle).Report.AltitudeValid);
        }

        /// <summary>
        /// Message 11's field is a signed 16-bit count of 5 ft, so it runs out long
        /// before a double does. Past the end it is withheld rather than wrapped.
        /// </summary>
        [TestMethod]
        public void Build_WithGeometricAltitudeBeyondTheField_WithholdsIt()
        {
            var vehicle = Flying();
            vehicle.AltitudeMslMeters = 60000.0; // 196,850 ft, past the 163,835 ft limit

            var state = Build(vehicle);

            Assert.IsFalse(state.GeometricAltitudeValid);
            Assert.AreEqual(0, state.GeometricAltitudeFeet);
            Assert.IsTrue(state.Report.PositionValid, "only the one field is withheld");
        }

        /// <summary>
        /// Cruise reports course over the ground, and specifically not yaw - the fixture
        /// is crabbed 7.5 degrees, so the two are distinguishable.
        /// </summary>
        [TestMethod]
        public void Build_WhileTranslating_ReportsTrueTrack()
        {
            var vehicle = Flying();
            vehicle.TrackSource = Gdl90TrackType.TrueTrack;

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.TrueTrack, state.Report.TrackType);
            Assert.AreEqual(FixtureTrackDegrees, state.Report.TrackDegrees, 1e-3);
        }

        /// <summary>
        /// In a hover the course over the ground is undefined, so the report carries
        /// the nose direction and says so. True, not magnetic: ArduPilot references
        /// ATTITUDE.yaw to true north.
        /// </summary>
        [TestMethod]
        public void Build_WhileHovering_ReportsTrueHeading()
        {
            var vehicle = Flying();
            vehicle.GroundVector = Vector(0.4, FixtureTrackDegrees);
            vehicle.TrackSource = Gdl90TrackType.TrueHeading;

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.TrueHeading, state.Report.TrackType);
            Assert.AreEqual(FixtureYawDegrees, state.Report.TrackDegrees, 1e-3);
        }

        /// <summary>
        /// The report names the quantity it carries itself, so a track source it does
        /// not recognize still comes out as track rather than being echoed.
        /// </summary>
        [DataTestMethod]
        [DataRow(Gdl90TrackType.Invalid)]
        [DataRow(Gdl90TrackType.MagneticHeading)]
        public void Build_FromAnUnusableTrackSource_ReportsTrack(Gdl90TrackType source)
        {
            var vehicle = Flying();
            vehicle.TrackSource = source;

            Assert.AreEqual(Gdl90TrackType.TrueTrack, Build(vehicle).Report.TrackType);
        }

        /// <summary>Yaw comes from the estimator, so a hover with no fix still has a direction.</summary>
        [TestMethod]
        public void Build_HoveringWithoutGpsFix_StillReportsHeading()
        {
            var vehicle = Flying();
            vehicle.GroundVector = Vector(0.4, FixtureTrackDegrees);
            vehicle.TrackSource = Gdl90TrackType.TrueHeading;
            SetBothFixTypes(vehicle, 0);

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.TrueHeading, state.Report.TrackType);
            Assert.AreEqual(FixtureYawDegrees, state.Report.TrackDegrees, 1e-3);
            Assert.IsFalse(state.Report.PositionValid, "still no position, though");
        }

        /// <summary>
        /// No yaw, whether it never arrived or has gone stale, is NaN. Reporting it
        /// would put NaN through the encoder's angular quantization.
        /// </summary>
        [TestMethod]
        public void Build_HoveringWithoutAttitude_MarksTrackInvalid()
        {
            var vehicle = Flying();
            vehicle.GroundVector = Vector(0.4, FixtureTrackDegrees);
            vehicle.TrackSource = Gdl90TrackType.TrueHeading;
            vehicle.YawDegrees = double.NaN;

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.Invalid, state.Report.TrackType);
            Assert.AreEqual(0.0, state.Report.TrackDegrees);
        }

        /// <summary>
        /// A non-finite vector component has no direction and no magnitude worth
        /// reporting, so both the track and the speed go rather than reaching atan2.
        /// </summary>
        [TestMethod]
        public void Build_WithANonFiniteGroundVector_ReportsNoTrackOrSpeed()
        {
            var vehicle = Flying();
            vehicle.GroundVector = new Gdl90GroundVector(double.NaN, 0.0, 0.0);

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.Invalid, state.Report.TrackType);
            Assert.AreEqual(0.0, state.Report.TrackDegrees);
            Assert.IsFalse(state.Report.HorizontalVelocityValid);
        }

        /// <summary>
        /// The track comes out of GLOBAL_POSITION_INT, so neither GPS stream stopping
        /// takes it away. Only the accuracy, which is the one thing a receiver alone
        /// can say, goes unknown.
        /// </summary>
        [TestMethod]
        public void Build_WithBothGpsStreamsStale_StillReportsTrack()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(FixtureFixType, double.NaN);
            vehicle.Gps2 = new Gdl90GpsSample(FixtureFixType, double.NaN);

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.TrueTrack, state.Report.TrackType);
            Assert.AreEqual(FixtureTrackDegrees, state.Report.TrackDegrees, 1e-3);
            Assert.IsTrue(state.Report.PositionValid);
            Assert.AreEqual(0, state.Report.Nacp);
        }

        /// <summary>
        /// Every compass point, since the report wants 0..360 clockwise from true north
        /// and the vector arrives as signed north/east components.
        /// </summary>
        [DataTestMethod]
        [DataRow(10.0, 0.0, 0.0)]      // due north
        [DataRow(0.0, 10.0, 90.0)]     // due east
        [DataRow(-10.0, 0.0, 180.0)]   // due south
        [DataRow(0.0, -10.0, 270.0)]   // due west
        [DataRow(7.07, 7.07, 45.0)]    // north-east
        [DataRow(-7.07, -7.07, 225.0)] // south-west
        public void Build_MapsTheGroundVectorOntoACourse(double north, double east, double expected)
        {
            var vehicle = Flying();
            vehicle.GroundVector = new Gdl90GroundVector(north, east, FixtureClimbMetersPerSecond);

            var state = Build(vehicle);

            Assert.AreEqual(expected, state.Report.TrackDegrees, 1e-2);
        }

        /// <summary>
        /// The track and the climb rate are both read off the ground vector, and off
        /// nothing else, so a vector that disagrees with every other field still decides
        /// them.
        /// </summary>
        [TestMethod]
        public void Build_DerivesTrackAndClimbFromTheGroundVector()
        {
            var vehicle = Flying();

            // Due west, descending, against a fixture that is heading south-east climbing.
            vehicle.GroundVector = new Gdl90GroundVector(0.0, -12.0, 3.0);

            var state = Build(vehicle);

            Assert.AreEqual(270.0, state.Report.TrackDegrees, 1e-3);
            Assert.AreEqual(-591, state.Report.VerticalVelocityFpm, "3 m/s down");
        }

        /// <summary>
        /// No vector costs the two fields that are the vector, and nothing else: the
        /// position and the geometric altitude ride the same message but are read
        /// separately.
        /// </summary>
        [TestMethod]
        public void Build_WithNoGroundVector_DropsTheWholeVelocityButKeepsThePosition()
        {
            var vehicle = Flying();
            vehicle.GroundVector = null;

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.Invalid, state.Report.TrackType);
            Assert.AreEqual(0.0, state.Report.TrackDegrees);
            Assert.IsFalse(state.Report.VerticalVelocityValid);

            Assert.IsFalse(state.Report.HorizontalVelocityValid,
                "the speed is the same vector's magnitude, so it goes with it");

            Assert.IsTrue(state.Report.PositionValid);
            Assert.IsTrue(state.GeometricAltitudeValid);
        }

        /// <summary>
        /// atan2 of a stationary vector is due north, which is a confident answer to a
        /// question with no answer. Zero speed selects the heading branch before it can
        /// be asked.
        /// </summary>
        [TestMethod]
        public void Build_WithNoGroundVelocity_ReportsHeadingRatherThanDueNorth()
        {
            var vehicle = Flying();
            vehicle.GroundVector = new Gdl90GroundVector(0.0, 0.0, FixtureClimbMetersPerSecond);
            vehicle.TrackSource = Gdl90TrackType.TrueHeading;

            var state = Build(vehicle);

            Assert.AreEqual(Gdl90TrackType.TrueHeading, state.Report.TrackType);
            Assert.AreEqual(FixtureYawDegrees, state.Report.TrackDegrees, 1e-3);
            Assert.IsTrue(state.Report.PositionValid, "one dead field, not a dead fix");
        }

        [TestMethod]
        public void Build_WithoutGpsFix_MarksTrackInvalid()
        {
            var vehicle = Flying();
            SetBothFixTypes(vehicle, 0);

            Assert.AreEqual(Gdl90TrackType.Invalid, Build(vehicle).Report.TrackType);
        }

        [TestMethod]
        public void Build_WithNoTelemetryAtAll_ReportsIdentityAndNothingElse()
        {
            var state = Build(new Gdl90VehicleState());

            Assert.AreEqual(0x7C1A2B, state.Report.Address, "identity is ours, not the vehicle's");
            Assert.IsFalse(state.Report.PositionValid);
            Assert.AreEqual(0L, state.Report.LatitudeE7);
            Assert.AreEqual(0L, state.Report.LongitudeE7);
            Assert.IsFalse(state.Report.AltitudeValid);
            Assert.IsFalse(state.Report.HorizontalVelocityValid);
            Assert.IsFalse(state.Report.VerticalVelocityValid);
            Assert.AreEqual(Gdl90TrackType.Invalid, state.Report.TrackType);
            Assert.AreEqual(0, state.Report.Nacp, "unknown accuracy, not perfect accuracy");
            Assert.IsFalse(state.GeometricAltitudeValid);
            Assert.IsTrue(state.Report.Airborne);
        }

        /// <summary>Descent is negative; Down is positive down and the report is positive up.</summary>
        [TestMethod]
        public void Build_VerticalVelocitySignFollowsClimb()
        {
            var vehicle = Flying();
            vehicle.GroundVector = Vector(FixtureGroundSpeedMeters, FixtureTrackDegrees, 2.5);

            Assert.AreEqual(-492, Build(vehicle).Report.VerticalVelocityFpm);
        }

        /// <summary>
        /// NACp is the receiver's own horizontal accuracy, scaled to a 95% figure and
        /// binned against ICD Table 10. Each threshold is straddled, because the bins
        /// are what the receiving EFB acts on: below NACp 5 it is entitled to draw the
        /// target as degraded.
        /// </summary>
        [DataTestMethod]
        [DataRow(0.02, 11)]  // RTK fixed, EPU 0.05 m
        [DataRow(0.5, 11)]   // healthy, EPU 1.22 m
        [DataRow(1.2, 11)]   // EPU 2.94 m, just inside the 3 m bound
        [DataRow(1.25, 10)]  // EPU 3.06 m, just outside it
        [DataRow(4.0, 10)]   // EPU 9.80 m, just inside the 10 m bound
        [DataRow(4.1, 9)]    // EPU 10.05 m, just outside it
        [DataRow(12.0, 9)]   // EPU 29.4 m, just inside the 30 m bound
        [DataRow(12.3, 0)]   // EPU 30.1 m, past the last bound the table is used for
        [DataRow(500.0, 0)]  // nonsense, and not to be reported as a coarse category
        public void Build_BinsHorizontalAccuracyIntoNacp(double hAccMeters, int expected)
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(FixtureFixType, hAccMeters);

            var state = Build(vehicle);

            Assert.AreEqual(expected, state.Report.Nacp);
            Assert.IsTrue(state.Report.PositionValid,
                "a coarse fix is still a fix; only the accuracy claim changes");
        }

        /// <summary>
        /// hAcc is a one-sigma figure and NACp is defined on a 95% one, so the scale
        /// factor is load-bearing: without it this reading claims the top category.
        /// </summary>
        [TestMethod]
        public void Build_ScalesHorizontalAccuracyToNinetyFivePercent()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(FixtureFixType, 1.3); // under 3 m raw; EPU 3.19 m scaled

            Assert.AreEqual(10, Build(vehicle).Report.Nacp);
            Assert.AreEqual(2.45, Gdl90Ownship.EpuFromReportedAccuracy, 1e-9);
        }

        /// <summary>
        /// Zero is what ArduPilot initializes h_acc to, NaN is what the reader leaves
        /// when the receiver's message carried none or has gone stale, and MAVLink has
        /// no invalid sentinel. None of them means the position is perfect, which is
        /// what the top of the table would otherwise be told.
        /// </summary>
        [DataTestMethod]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        [DataRow(double.NaN)]
        public void Build_WithNoHorizontalAccuracy_ReportsUnknownNacp(double hAccMeters)
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(FixtureFixType, hAccMeters);

            var state = Build(vehicle);

            Assert.AreEqual(0, state.Report.Nacp);
            Assert.IsTrue(state.Report.PositionValid,
                "an unknown accuracy withholds the accuracy, not the position");
        }

        [TestMethod]
        public void Build_WithOnlyA2dFix_ReportsUnknownNacp()
        {
            var vehicle = Flying();
            SetBothFixTypes(vehicle, 2); // 2D on both

            Assert.AreEqual(0, Build(vehicle).Report.Nacp);
        }

        /// <summary>
        /// GPS1 is primary and the flight controller falls back to GPS2 only on losing
        /// it, so the report follows the same order rather than taking whichever
        /// receiver reads better.
        /// </summary>
        [TestMethod]
        public void Build_WithBothReceiversInFix_SpeaksForThePrimary()
        {
            Assert.AreEqual(11, Build(Flying()).Report.Nacp, "GPS1's 0.5 m, not GPS2's 4 m");
        }

        /// <summary>
        /// With GPS1 out the estimator is flying on GPS2, so the accuracy reported
        /// alongside its position has to be GPS2's. Reporting no position at all here
        /// would hide an aircraft that is navigating perfectly well.
        /// </summary>
        [TestMethod]
        public void Build_WithThePrimaryOutOfFix_SpeaksForTheSecondReceiver()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(1, FixtureHorizontalAccuracyMeters);

            var state = Build(vehicle);

            Assert.IsTrue(state.Report.PositionValid);
            Assert.IsTrue(state.GeometricAltitudeValid);
            Assert.AreEqual(10, state.Report.Nacp, "GPS2's 4 m is an EPU of 9.8 m");
            Assert.AreEqual(Gdl90TrackType.TrueTrack, state.Report.TrackType);
            Assert.AreEqual(FixtureTrackDegrees, state.Report.TrackDegrees, 1e-3,
                "the track is the estimator's, so failing over does not move it");
        }

        /// <summary>
        /// A single-GPS aircraft never sends GPS2_RAW at all, which leaves the second
        /// receiver at fix type zero - so there is nothing to fall back to and losing
        /// GPS1 is losing the position.
        /// </summary>
        [TestMethod]
        public void Build_WithNoSecondReceiverFitted_ReportsNoPosition()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(1, FixtureHorizontalAccuracyMeters);
            vehicle.Gps2 = Gdl90GpsSample.Absent;

            var state = Build(vehicle);

            Assert.IsFalse(state.Report.PositionValid);
            Assert.AreEqual(0, state.Report.Nacp);
        }

        /// <summary>
        /// A GPS2_RAW that stopped arriving still says the estimator has a GPS behind it
        /// - GLOBAL_POSITION_INT is what ages the position - but the accuracy it last
        /// claimed is no longer worth repeating.
        /// </summary>
        [TestMethod]
        public void Build_OnAStaleSecondReceiver_KeepsThePositionAndDropsTheAccuracy()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(1, FixtureHorizontalAccuracyMeters);
            vehicle.Gps2 = new Gdl90GpsSample(FixtureFixType, double.NaN);

            var state = Build(vehicle);

            Assert.IsTrue(state.Report.PositionValid);
            Assert.AreEqual(0, state.Report.Nacp);
            Assert.AreEqual(Gdl90TrackType.TrueTrack, state.Report.TrackType,
                "the track never came from the receiver");
        }

        /// <summary>
        /// NIC is a containment radius under integrity monitoring. Nothing in this
        /// chain computes one, so none is ever claimed - not even alongside the
        /// tightest accuracy the receiver can report.
        /// </summary>
        [TestMethod]
        public void Build_NeverClaimsIntegrity()
        {
            var vehicle = Flying();
            vehicle.Gps = new Gdl90GpsSample(FixtureFixType, 0.01);

            var state = Build(vehicle);

            Assert.AreEqual(11, state.Report.Nacp, "the accuracy is as good as it gets");
            Assert.AreEqual(0, state.Report.Nic, "and the integrity is still unknown");
        }

        [TestMethod]
        public void Build_OnGround_ClearsAirborne()
        {
            var vehicle = Flying();
            vehicle.LandedState = MAVLink.MAV_LANDED_STATE.ON_GROUND;

            Assert.IsFalse(Build(vehicle).Report.Airborne);
        }

        /// <summary>
        /// Everything that is not a definite on-ground reads as airborne: a receiver
        /// may suppress ownship altitude while On Ground is set, which is the worse
        /// error.
        /// </summary>
        [TestMethod]
        public void Build_WithAnyOtherLandedState_SetsAirborne()
        {
            foreach (var landedState in new MAVLink.MAV_LANDED_STATE?[]
            {
                MAVLink.MAV_LANDED_STATE.TAKEOFF,
                MAVLink.MAV_LANDED_STATE.LANDING,
                MAVLink.MAV_LANDED_STATE.IN_AIR,
                MAVLink.MAV_LANDED_STATE.UNDEFINED,
                null,
            })
            {
                var vehicle = Flying();
                vehicle.LandedState = landedState;

                Assert.IsTrue(Build(vehicle).Report.Airborne, landedState?.ToString() ?? "not received");
            }
        }

        /// <summary>
        /// The invalid decisions above only matter if they survive encoding, so check
        /// they land on the ICD's sentinels rather than on plausible-looking zeroes.
        /// </summary>
        [TestMethod]
        public void Build_InvalidFieldsReachTheWireAsSentinels()
        {
            var state = Build(new Gdl90VehicleState());
            byte[] msg = Gdl90Messages.OwnshipReport(state.Report);

            // Altitude is the top 12 bits of bytes 11..12 (ICD 3.5.1.4).
            int altitude = (msg[11] << 4) | (msg[12] >> 4);
            Assert.AreEqual(0xFFF, altitude, "no altitude");

            // Horizontal velocity is byte 14 plus the top nibble of byte 15 (3.5.1.7).
            int horizontal = (msg[14] << 4) | (msg[15] >> 4);
            Assert.AreEqual(0xFFF, horizontal, "no horizontal velocity");

            // Vertical velocity is the low nibble of byte 15 plus byte 16 (3.5.1.8).
            int vertical = ((msg[15] & 0x0F) << 8) | msg[16];
            Assert.AreEqual(0x800, vertical, "no vertical velocity");

            // ICD 3.4: latitude, longitude and NIC all zero when the fix is invalid.
            Assert.AreEqual(0, msg[5] | msg[6] | msg[7] | msg[8] | msg[9] | msg[10]);
            Assert.AreEqual(0, msg[13] >> 4, "NIC");
            Assert.AreEqual(0, msg[13] & 0xF, "NACp");
            Assert.AreEqual(0, msg[17], "track");
            Assert.AreEqual(0, msg[12] & 0x3, "track type invalid");
        }
    }
}
