using System;
using System.Linq;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;

namespace Carbonix.Tests.GDL90
{
    [TestClass]
    public class Gdl90PositionTests
    {
        /// <summary>
        /// Both components zero is what an estimator with no position sends, and 0,0 is
        /// a real place in the Atlantic that a receiver would happily draw.
        /// </summary>
        [DataTestMethod]
        [DataRow(0, 0, DisplayName = "null island")]
        [DataRow(int.MaxValue, 1508543972, DisplayName = "latitude sentinel")]
        [DataRow(-336671869, int.MaxValue, DisplayName = "longitude sentinel")]
        public void From_ACoordinateThatIsNotAPosition_IsNull(int latitudeE7, int longitudeE7)
        {
            Assert.IsNull(Gdl90Position.From(latitudeE7, longitudeE7));
        }

        /// <summary>The equator and the Greenwich meridian both run through places an aircraft can be.</summary>
        [DataTestMethod]
        [DataRow(0, 1508543972, DisplayName = "on the equator")]
        [DataRow(-336671869, 0, DisplayName = "on the Greenwich meridian")]
        public void From_ASingleZeroComponent_IsAPosition(int latitudeE7, int longitudeE7)
        {
            var position = Gdl90Position.From(latitudeE7, longitudeE7);

            Assert.IsNotNull(position);
            Assert.AreEqual(latitudeE7, position.LatitudeE7);
            Assert.AreEqual(longitudeE7, position.LongitudeE7);
        }
    }

    /// <summary>
    /// Reading a link into a vehicle state: which message each value comes from, and
    /// what is left out once a message is no longer current.
    /// </summary>
    [TestClass]
    public class Gdl90VehicleReaderTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 9, 3, 0, 0, DateTimeKind.Utc);

        // The test area, stationary.
        const int FixtureLatE7 = -336671869;
        const int FixtureLonE7 = 1508543972;
        const int FixtureAltMillimeters = 27800;

        static readonly string[] Expected =
        {
            "GLOBAL_POSITION_INT", "GPS_RAW_INT", "SCALED_PRESSURE", "ATTITUDE", "EXTENDED_SYS_STATE",
        };

        /// <summary>A link whose vehicle has received exactly the packets the test hands it.</summary>
        sealed class Link : IDisposable
        {
            readonly MAVLinkInterface _port = new MAVLinkInterface();
            readonly MAVLink.MavlinkParse _parse = new MAVLink.MavlinkParse();

            public MAVState Mav
            {
                get { return _port.MAV; }
            }

            public void Receive(MAVLink.MAVLINK_MSG_ID id, object data, DateTime rxtime)
            {
                Mav.addPacket(new MAVLink.MAVLinkMessage(
                    _parse.GenerateMAVLinkPacket20(id, data, false, 1, 1), rxtime));
            }

            public void ReceiveMavlink1(MAVLink.MAVLINK_MSG_ID id, object data, DateTime rxtime)
            {
                Mav.addPacket(new MAVLink.MAVLinkMessage(
                    _parse.GenerateMAVLinkPacket10(id, data, 1, 1), rxtime));
            }

            public void Dispose()
            {
                _port.Dispose();
            }
        }

        static MAVLink.mavlink_global_position_int_t Estimator(int lat = FixtureLatE7,
            int lon = FixtureLonE7, int altMillimeters = FixtureAltMillimeters,
            short vx = 0, short vy = 0, short vz = 0)
        {
            return new MAVLink.mavlink_global_position_int_t
            {
                lat = lat,
                lon = lon,
                alt = altMillimeters,
                vx = vx,
                vy = vy,
                vz = vz,
            };
        }

        static MAVLink.mavlink_gps_raw_int_t Receiver(byte fixType = 3, int lat = FixtureLatE7,
            int lon = FixtureLonE7, int altMillimeters = FixtureAltMillimeters, uint hAccMillimeters = 500)
        {
            return new MAVLink.mavlink_gps_raw_int_t
            {
                fix_type = fixType,
                lat = lat,
                lon = lon,
                alt = altMillimeters,
                h_acc = hAccMillimeters,
            };
        }

        static void LandedState(Link link, MAVLink.MAV_LANDED_STATE state, DateTime at)
        {
            link.Receive(MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE,
                new MAVLink.mavlink_extended_sys_state_t { landed_state = (byte)state }, at);
        }

        /// <summary>Every message the reader looks at, all received at one instant.</summary>
        static Link Everything(DateTime at)
        {
            var link = new Link();
            link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT, Estimator(vx: 1500, vy: 800, vz: -200), at);
            link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(), at);
            link.Receive(MAVLink.MAVLINK_MSG_ID.GPS2_RAW,
                new MAVLink.mavlink_gps2_raw_t { fix_type = 2, h_acc = 4000 }, at);
            link.Receive(MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE,
                new MAVLink.mavlink_scaled_pressure_t { press_abs = 1009.88f }, at);
            link.Receive(MAVLink.MAVLINK_MSG_ID.ATTITUDE,
                new MAVLink.mavlink_attitude_t { yaw = (float)(130.0 * Math.PI / 180.0) }, at);
            LandedState(link, MAVLink.MAV_LANDED_STATE.IN_AIR, at);
            return link;
        }

        static void AssertNothing(Gdl90VehicleState state)
        {
            Assert.IsNull(state.Position);
            Assert.IsNull(state.GroundVector);
            Assert.IsTrue(double.IsNaN(state.AltitudeMslMeters));
            Assert.IsTrue(double.IsNaN(state.StaticPressureHpa));
            Assert.IsTrue(double.IsNaN(state.YawDegrees));
            Assert.AreEqual(0, state.Gps.FixType);
            Assert.IsTrue(double.IsNaN(state.Gps.HorizontalAccuracyMeters));
            Assert.AreEqual(0, state.Gps2.FixType);
            Assert.IsTrue(double.IsNaN(state.Gps2.HorizontalAccuracyMeters));
            Assert.IsNull(state.LandedState);
            CollectionAssert.AreEquivalent(Expected, state.MissingMessages.ToList());
            Assert.AreEqual(Gdl90TrackType.TrueTrack, state.TrackSource);
        }

        [TestMethod]
        public void Read_WithNoLink_ReportsNothing()
        {
            AssertNothing(new Gdl90VehicleReader().Read(null, Now));
        }

        [TestMethod]
        public void Read_WithNoPackets_ReportsNothing()
        {
            using (var link = new Link())
            {
                AssertNothing(new Gdl90VehicleReader().Read(link.Mav, Now));
            }
        }

        [TestMethod]
        public void Read_TakesEachValueOffItsOwnMessage()
        {
            using (var link = Everything(Now))
            {
                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.AreEqual(FixtureLatE7, state.Position.LatitudeE7);
                Assert.AreEqual(FixtureLonE7, state.Position.LongitudeE7);
                Assert.AreEqual(27.8, state.AltitudeMslMeters, 1e-9);
                Assert.AreEqual(15.0, state.GroundVector.North, 1e-9);
                Assert.AreEqual(8.0, state.GroundVector.East, 1e-9);
                Assert.AreEqual(-2.0, state.GroundVector.Down, 1e-9);
                Assert.AreEqual(1009.88, state.StaticPressureHpa, 1e-3);
                Assert.AreEqual(130.0, state.YawDegrees, 1e-3);
                Assert.AreEqual(3, state.Gps.FixType);
                Assert.AreEqual(0.5, state.Gps.HorizontalAccuracyMeters, 1e-9);
                Assert.AreEqual(2, state.Gps2.FixType);
                Assert.AreEqual(4.0, state.Gps2.HorizontalAccuracyMeters, 1e-9);
                Assert.AreEqual(MAVLink.MAV_LANDED_STATE.IN_AIR, state.LandedState);
                Assert.AreEqual(0, state.MissingMessages.Count);
            }
        }

        /// <summary>
        /// An estimator with no position sends zeros while the receiver may still have
        /// one, and a receiver's position is worth more to an EFB than going dark.
        /// </summary>
        [TestMethod]
        public void Read_FallsBackToTheReceiverWhenTheEstimatorHasNoPosition()
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
                    Estimator(lat: 0, lon: 0, altMillimeters: 0, vx: 300), Now);
                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(altMillimeters: 400000), Now);

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.AreEqual(FixtureLatE7, state.Position.LatitudeE7);
                Assert.AreEqual(400.0, state.AltitudeMslMeters, 1e-9);
                Assert.AreEqual(3.0, state.GroundVector.North, 1e-9, "the vector is still the estimator's");
            }
        }

        [TestMethod]
        public void Read_WithNeitherMessageCarryingAPosition_ReportsNone()
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT, Estimator(lat: 0, lon: 0), Now);
                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(lat: 0, lon: 0), Now);

                Assert.IsNull(new Gdl90VehicleReader().Read(link.Mav, Now).Position);
            }
        }

        /// <summary>
        /// GLOBAL_POSITION_INT is the position stream whichever message supplied the
        /// coordinates, so a receiver's position does not outlive the estimator's silence.
        /// </summary>
        [TestMethod]
        public void Read_DropsAReceiverPositionWithTheEstimatorsStream()
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
                    Estimator(lat: 0, lon: 0), Now.AddSeconds(-10));
                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(), Now);

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.IsNull(state.Position);
                Assert.AreEqual(3, state.Gps.FixType, "the receiver itself is still current");
                Assert.AreEqual(0.5, state.Gps.HorizontalAccuracyMeters, 1e-9);
            }
        }

        [TestMethod]
        public void Read_ConvertsYawToABearing()
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.ATTITUDE,
                    new MAVLink.mavlink_attitude_t { yaw = (float)(-Math.PI / 2.0) }, Now);

                Assert.AreEqual(270.0, new Gdl90VehicleReader().Read(link.Mav, Now).YawDegrees, 1e-3);
            }
        }

        /// <summary>
        /// h_acc is a MAVLink 2 extension. A MAVLink 1 message carries none, and that
        /// has to read as unknown rather than as a perfect fix.
        /// </summary>
        [TestMethod]
        public void Read_ReadsHorizontalAccuracyOnlyFromMavlink2()
        {
            using (var link = new Link())
            {
                link.ReceiveMavlink1(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(hAccMillimeters: 500), Now);
                Assert.IsTrue(double.IsNaN(new Gdl90VehicleReader().Read(link.Mav, Now).Gps.HorizontalAccuracyMeters));

                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(hAccMillimeters: 500), Now);
                Assert.AreEqual(0.5, new Gdl90VehicleReader().Read(link.Mav, Now).Gps.HorizontalAccuracyMeters, 1e-9);
            }
        }

        [TestMethod]
        public void Read_LeavesOutEveryMessageThatIsNotCurrent()
        {
            using (var link = Everything(Now.AddSeconds(-10)))
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE,
                    new MAVLink.mavlink_scaled_pressure_t { press_abs = 1009.88f }, Now);

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.AreEqual(1009.88, state.StaticPressureHpa, 1e-3);

                Assert.IsNull(state.Position);
                Assert.IsNull(state.GroundVector);
                Assert.IsTrue(double.IsNaN(state.AltitudeMslMeters));
                Assert.IsTrue(double.IsNaN(state.YawDegrees));
                Assert.IsTrue(double.IsNaN(state.Gps.HorizontalAccuracyMeters));
                Assert.IsTrue(double.IsNaN(state.Gps2.HorizontalAccuracyMeters));

                CollectionAssert.AreEquivalent(
                    new[] { "GLOBAL_POSITION_INT", "GPS_RAW_INT", "ATTITUDE", "EXTENDED_SYS_STATE" },
                    state.MissingMessages.ToList());
            }
        }

        /// <summary>
        /// The landed state is held for the life of the connection: a parked aircraft
        /// stays on the ground through a dropout, and the tab does not start the stream
        /// until the message has arrived. Its absence is still reported.
        /// </summary>
        [TestMethod]
        public void Read_HoldsTheLandedStateHoweverOldItIs()
        {
            using (var link = new Link())
            {
                LandedState(link, MAVLink.MAV_LANDED_STATE.ON_GROUND, Now.AddHours(-1));

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.AreEqual(MAVLink.MAV_LANDED_STATE.ON_GROUND, state.LandedState);
                CollectionAssert.Contains(state.MissingMessages.ToList(), "EXTENDED_SYS_STATE");
            }
        }

        /// <summary>
        /// A stale fix type still says the estimator's position is GPS-backed; only the
        /// accuracy, the one claim taken from the receiver directly, expires with it.
        /// </summary>
        [TestMethod]
        public void Read_KeepsAStaleReceiversFixTypeAndDropsItsAccuracy()
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(), Now.AddSeconds(-10));

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.AreEqual(3, state.Gps.FixType);
                Assert.IsTrue(double.IsNaN(state.Gps.HorizontalAccuracyMeters));
            }
        }

        /// <summary>A second receiver is optional, so a single-GPS aircraft is not missing anything.</summary>
        [TestMethod]
        public void Read_DoesNotExpectASecondReceiver()
        {
            using (var link = Everything(Now))
            {
                var state = new Gdl90VehicleReader().Read(link.Mav, Now);
                Assert.AreEqual(0, state.MissingMessages.Count);
            }

            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT, Estimator(), Now);
                link.Receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, Receiver(), Now);
                link.Receive(MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE,
                    new MAVLink.mavlink_scaled_pressure_t { press_abs = 1009.88f }, Now);
                link.Receive(MAVLink.MAVLINK_MSG_ID.ATTITUDE, new MAVLink.mavlink_attitude_t(), Now);
                LandedState(link, MAVLink.MAV_LANDED_STATE.ON_GROUND, Now);

                var state = new Gdl90VehicleReader().Read(link.Mav, Now);
                Assert.AreEqual(0, state.MissingMessages.Count, "no GPS2_RAW at all");
            }
        }

        /// <summary>
        /// The position expires sooner than everything else: it is the one field a
        /// receiver acts on, and it moves while it sits there.
        /// </summary>
        [TestMethod]
        public void Read_AgesThePositionMoreTightlyThanTheRest()
        {
            Assert.IsTrue(Gdl90VehicleReader.PositionStaleAfter < Gdl90VehicleReader.StaleAfter);

            var between = (Gdl90VehicleReader.PositionStaleAfter + Gdl90VehicleReader.StaleAfter)
                .TotalSeconds / 2.0;

            using (var link = Everything(Now.AddSeconds(-between)))
            {
                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.IsNull(state.Position);
                Assert.IsFalse(double.IsNaN(state.StaticPressureHpa));
                CollectionAssert.AreEqual(new[] { "GLOBAL_POSITION_INT" }, state.MissingMessages.ToList());
            }

            using (var link = Everything(Now - Gdl90VehicleReader.PositionStaleAfter))
            {
                Assert.IsNotNull(new Gdl90VehicleReader().Read(link.Mav, Now).Position,
                    "the threshold itself is still current");
            }
        }

        /// <summary>
        /// A packet read out of a tlog is stamped in local time. Measured against a UTC
        /// instant without conversion, every message would be the UTC offset old.
        /// </summary>
        [TestMethod]
        public void Read_TreatsALocalReceiveTimeAsTheInstantItNames()
        {
            using (var link = Everything(Now.ToLocalTime()))
            {
                var state = new Gdl90VehicleReader().Read(link.Mav, Now);

                Assert.IsNotNull(state.Position);
                Assert.AreEqual(0, state.MissingMessages.Count);
            }
        }

        /// <summary>One read with a current ground vector at the given speed, due north.</summary>
        static Gdl90TrackType ReadAtSpeed(Gdl90VehicleReader reader, double metersPerSecond)
        {
            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
                    Estimator(vx: (short)Math.Round(metersPerSecond * 100.0)), Now);
                return reader.Read(link.Mav, Now).TrackSource;
            }
        }

        /// <summary>A reader already holding the given choice, driven there by a speed that selects it.</summary>
        static Gdl90VehicleReader StartedAt(Gdl90TrackType source)
        {
            var reader = new Gdl90VehicleReader();
            var settled = ReadAtSpeed(reader, source == Gdl90TrackType.TrueHeading ? 0.0 : 21.0);

            Assert.AreEqual(source, settled, "reader did not start where the test wanted");
            return reader;
        }

        /// <summary>
        /// A fresh reader starts on track, matching the ICD's expectation of an airborne
        /// target, and holds it through a first read inside the dead band.
        /// </summary>
        [TestMethod]
        public void TrackSource_StartsOnTrack()
        {
            Assert.AreEqual(Gdl90TrackType.TrueTrack, ReadAtSpeed(new Gdl90VehicleReader(), 4.5));
        }

        [TestMethod]
        public void TrackSource_BetweenTheThresholds_HoldsThePreviousChoice()
        {
            Assert.AreEqual(Gdl90TrackType.TrueHeading,
                ReadAtSpeed(StartedAt(Gdl90TrackType.TrueHeading), 4.5));
            Assert.AreEqual(Gdl90TrackType.TrueTrack,
                ReadAtSpeed(StartedAt(Gdl90TrackType.TrueTrack), 4.5));
        }

        /// <summary>
        /// The ground speed decides which quantity to report, so an unusable one has to
        /// hold rather than silently reinterpret the field.
        /// </summary>
        [TestMethod]
        public void TrackSource_WithoutACurrentGroundVector_HoldsThePreviousChoice()
        {
            var reader = StartedAt(Gdl90TrackType.TrueHeading);

            using (var link = new Link())
            {
                link.Receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
                    Estimator(vx: 2100), Now.AddSeconds(-10));
                Assert.AreEqual(Gdl90TrackType.TrueHeading, reader.Read(link.Mav, Now).TrackSource,
                    "a stale vector");
            }

            using (var link = new Link())
            {
                Assert.AreEqual(Gdl90TrackType.TrueHeading, reader.Read(link.Mav, Now).TrackSource,
                    "no vector at all");
            }
        }

        /// <summary>One transition per takeoff and one per landing, with nothing in between.</summary>
        [TestMethod]
        public void TrackSource_SwitchesOnceEachWayAcrossATakeoffAndLanding()
        {
            var accelerating = new[] { 0.0, 1.0, 2.9, 3.5, 5.9, 6.1, 12.0, 21.0 };
            var decelerating = new[] { 21.0, 12.0, 6.1, 5.9, 3.5, 2.9, 1.0, 0.0 };

            var expectedUp = new[]
            {
                Gdl90TrackType.TrueHeading, Gdl90TrackType.TrueHeading, Gdl90TrackType.TrueHeading,
                Gdl90TrackType.TrueHeading, Gdl90TrackType.TrueHeading, Gdl90TrackType.TrueTrack,
                Gdl90TrackType.TrueTrack, Gdl90TrackType.TrueTrack,
            };
            CollectionAssert.AreEqual(expectedUp, Run(accelerating, Gdl90TrackType.TrueHeading));

            var expectedDown = new[]
            {
                Gdl90TrackType.TrueTrack, Gdl90TrackType.TrueTrack, Gdl90TrackType.TrueTrack,
                Gdl90TrackType.TrueTrack, Gdl90TrackType.TrueTrack, Gdl90TrackType.TrueHeading,
                Gdl90TrackType.TrueHeading, Gdl90TrackType.TrueHeading,
            };
            CollectionAssert.AreEqual(expectedDown, Run(decelerating, Gdl90TrackType.TrueTrack));
        }

        static Gdl90TrackType[] Run(double[] groundSpeeds, Gdl90TrackType initial)
        {
            var reader = StartedAt(initial);
            var observed = new Gdl90TrackType[groundSpeeds.Length];

            for (int i = 0; i < groundSpeeds.Length; i++)
            {
                observed[i] = ReadAtSpeed(reader, groundSpeeds[i]);
            }
            return observed;
        }
    }
}
