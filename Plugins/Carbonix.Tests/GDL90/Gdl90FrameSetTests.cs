using System;
using System.Linq;
using System.Reflection;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The per-second message set: which messages a second contains, and in what order.
    /// </summary>
    [TestClass]
    public class Gdl90FrameSetTests
    {
        static readonly Gdl90OwnshipIdentity Identity =
            new Gdl90OwnshipIdentity { IcaoAddress = 0x7C1A2B, Callsign = "VHTEST" };

        static readonly DateTime Now = new DateTime(2026, 9, 3, 4, 5, 6, DateTimeKind.Utc);

        /// <summary>Enough of a fix for the ownship report to claim a position.</summary>
        static Gdl90VehicleState Located()
        {
            return new Gdl90VehicleState
            {
                Gps = new Gdl90GpsSample(3, 0.5),
                Position = Gdl90Position.From(-338688000, 1512093000),
                AltitudeMslMeters = 400.0,
            };
        }

        static Gdl90FrameSet Build(Gdl90TrafficTracker tracker = null)
        {
            var traffic = tracker == null
                ? Enumerable.Empty<Gdl90TrafficResult>()
                : tracker.Collect(Now, Identity.IcaoAddress);
            return Gdl90FrameSet.Build(Located(), Identity, Now, traffic);
        }

        static byte[] Message(Gdl90FrameSet set, string name)
        {
            return set.Emissions.Single(e => e.Name == name).Message;
        }

        [TestMethod]
        public void Build_EmitsTheOwnshipMessagesInIcdOrder()
        {
            var names = Build().Emissions.Select(e => e.Name).ToList();

            CollectionAssert.AreEqual(
                new[] { "heartbeat", "ownship", "ownship_geo_altitude", "foreflight_id" },
                names);
        }

        [TestMethod]
        public void Build_WithAFix_SetsTheHeartbeatsPositionBit()
        {
            Assert.AreEqual(0x80, Message(Build(), "heartbeat")[1] & 0x80);
        }

        /// <summary>ICD 3.8: the geometric altitude message is sent only when there is one.</summary>
        [TestMethod]
        public void Build_WithoutAFix_OmitsTheGeometricAltitude()
        {
            var set = Gdl90FrameSet.Build(new Gdl90VehicleState(), Identity, Now,
                Enumerable.Empty<Gdl90TrafficResult>());

            var names = set.Emissions.Select(e => e.Name).ToList();
            CollectionAssert.AreEqual(new[] { "heartbeat", "ownship", "foreflight_id" }, names);
            Assert.AreEqual(0x00, Message(set, "heartbeat")[1] & 0x80, "and the heartbeat says so");
        }

        /// <summary>ICD 2.3: alert reports go out first, then the rest nearest the ownship first.</summary>
        [TestMethod]
        public void Build_EmitsAlertedTrafficFirstAndTheRestNearestFirst()
        {
            // Targets about 1, 5 and 3 km north of the ownship, arriving in that order;
            // the far one is alerted.
            var tracker = new Gdl90TrafficTracker(() => Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000001, latE7: -338598000), Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000005, latE7: -338238000), Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000003, latE7: -338418000), Now);
            tracker.AcceptCollision(0x000005, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);

            CollectionAssert.AreEqual(new[] { 0x000005, 0x000001, 0x000003 },
                TrafficAddresses(Build(tracker)));
        }

        /// <summary>A target without a position has no range, and follows every one that has.</summary>
        [TestMethod]
        public void Build_EmitsTrafficWithoutAPositionLast()
        {
            var tracker = new Gdl90TrafficTracker(() => Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000009,
                flags: MAVLink.ADSB_FLAGS.VALID_ALTITUDE), Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x000001, latE7: -338598000), Now);

            CollectionAssert.AreEqual(new[] { 0x000001, 0x000009 }, TrafficAddresses(Build(tracker)));
        }

        static int[] TrafficAddresses(Gdl90FrameSet set)
        {
            return set.Emissions
                .Where(e => e.Name == "traffic")
                .Select(e => (e.Message[2] << 16) | (e.Message[3] << 8) | e.Message[4])
                .ToArray();
        }

        [TestMethod]
        public void Build_ForwardsEveryTrackedTarget()
        {
            var tracker = new Gdl90TrafficTracker(() => Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x111111), Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x222222), Now);

            var addresses = Build(tracker).Emissions
                .Where(e => e.Name == "traffic")
                .Select(e => (e.Message[2] << 16) | (e.Message[3] << 8) | e.Message[4])
                .OrderBy(a => a)
                .ToList();

            CollectionAssert.AreEqual(new[] { 0x111111, 0x222222 }, addresses);
        }

        [TestMethod]
        public void Build_CarriesTheAircraftsAlertOntoTheReport()
        {
            var tracker = new Gdl90TrafficTracker(() => Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), Now);
            tracker.AcceptCollision(0x333333, MAVLink.MAV_COLLISION_THREAT_LEVEL.HIGH);

            var traffic = Message(Build(tracker), "traffic");

            Assert.AreEqual(0x01, traffic[1] >> 4, "ICD 3.5.1.1 traffic alert");
        }

        /// <summary>A dropped target is recorded once, the first second it is dropped.</summary>
        [TestMethod]
        public void Build_RecordsANewDropAndNotARepeatedOne()
        {
            var tracker = new Gdl90TrafficTracker(() => Now);
            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0), Now);

            var first = Gdl90FrameSet.Build(Located(), Identity, Now,
                tracker.Collect(Now, Identity.IcaoAddress));
            var drop = first.Emissions.Single(e => e.Name == "drop");

            Assert.IsFalse(drop.IsFrame);
            Assert.AreEqual(Gdl90TrafficDropReason.InvalidAddress, (string)drop.Describe()["reason"]);

            tracker.Accept(Gdl90TrafficMappingTests.Target(icao: 0), Now.AddSeconds(1));
            var second = Gdl90FrameSet.Build(Located(), Identity, Now.AddSeconds(1),
                tracker.Collect(Now.AddSeconds(1), Identity.IcaoAddress));

            Assert.IsFalse(second.Emissions.Any(e => e.Name == "drop"));
            Assert.IsFalse(second.Emissions.Any(e => e.Name == "traffic"));
        }

        /// <summary>
        /// The set is dated to the instant it is handed rather than to the clock, which
        /// is what lets a caller other than the transmit loop drive it.
        /// </summary>
        [TestMethod]
        public void Build_DatesTheHeartbeatToTheInstantItIsGiven()
        {
            byte[] heartbeat = Message(Build(), "heartbeat");
            int seconds = heartbeat[3] | (heartbeat[4] << 8) | ((heartbeat[2] >> 7) << 16);

            Assert.AreEqual(Gdl90FrameSet.SecondsSinceUtcMidnight(Now), seconds);
        }

        [TestMethod]
        public void SecondsSinceUtcMidnight_CountsWholeSecondsFromMidnight()
        {
            Assert.AreEqual(0,
                Gdl90FrameSet.SecondsSinceUtcMidnight(new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(3661,
                Gdl90FrameSet.SecondsSinceUtcMidnight(new DateTime(2026, 8, 31, 1, 1, 1, DateTimeKind.Utc)));
            Assert.AreEqual(86399,
                Gdl90FrameSet.SecondsSinceUtcMidnight(new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc)));
        }

        /// <summary>An emission's bytes and its logged values come from the same object.</summary>
        [TestMethod]
        public void Frame_DescribesTheMessageItEncodes()
        {
            var report = new Gdl90TrafficReport { Address = 0x333333, Callsign = "QFA123" };

            var emission = Gdl90Emission.Frame(report);

            Assert.IsTrue(emission.IsFrame);
            Assert.AreEqual("traffic", emission.Name);
            Assert.AreEqual(Gdl90Messages.TrafficReportId, emission.Message[0]);
            Assert.AreEqual(0x333333, (int)emission.Describe()["address"]);
        }
    }

    /// <summary>The projection of encoder inputs into the frame log's fields.</summary>
    [TestClass]
    public class Gdl90LogShapeTests
    {
        static Gdl90OwnshipReport SampleReport()
        {
            return new Gdl90OwnshipReport
            {
                Address = 0x7C1A2B,
                LatitudeE7 = -338688000,
                LongitudeE7 = 1512093000,
                PressureAltitudeFeet = 1013,
                TrackDegrees = 271.4,
                HorizontalVelocityKnots = 87,
                VerticalVelocityFpm = 640,
                EmitterCategory = 14,
                Callsign = "VHTEST",
                Nacp = 10,
            };
        }

        /// <summary>
        /// One rendering per quantity. degE7 is what the encoder was handed and what
        /// acceptance quantizes; degrees was the same number again.
        /// </summary>
        [TestMethod]
        public void Describe_LogsPositionOnlyInTheEncodersOwnUnit()
        {
            var values = Gdl90LogShape.Describe(SampleReport());

            Assert.AreEqual(-338688000L, (long)values["latitude_dege7"]);
            Assert.AreEqual(1512093000L, (long)values["longitude_dege7"]);
            Assert.IsNull(values["latitude_deg"]);
            Assert.IsNull(values["longitude_deg"]);
            Assert.IsNull(values["address_hex"]);
            Assert.IsNull(values["track_type_name"]);
        }

        /// <summary>
        /// The log records what the encoder was handed, not what the encoder did with
        /// it. Zeroing an unusable position is ICD 3.4's rule about the wire, and
        /// applying it here too would hide the position that was withheld.
        /// </summary>
        [TestMethod]
        public void Describe_LogsThePositionItWasGivenEvenWhenItIsNotTransmitted()
        {
            var report = SampleReport();
            report.PositionValid = false;

            var values = Gdl90LogShape.Describe(report);

            Assert.IsFalse((bool)values["position_valid"]);
            Assert.AreEqual(-338688000L, (long)values["latitude_dege7"]);
        }

        [TestMethod]
        public void Drop_NamesTheTargetAndTheReason()
        {
            var result = new Gdl90TrafficResult
            {
                Vehicle = Gdl90TrafficMappingTests.Target(icao: 0x4B1A2C, tslc: 3),
                Age = TimeSpan.FromSeconds(3.5),
                DropReason = Gdl90TrafficDropReason.Stale,
                DropIsNew = true,
            };

            var record = Gdl90LogShape.Drop(result);

            Assert.AreEqual("stale", (string)record["reason"]);
            Assert.AreEqual(0x4B1A2C, (int)record["address"]);
            Assert.AreEqual(3.5, (double)record["age_s"], 1e-9);
            Assert.AreEqual("QFA123", (string)record["callsign"]);
        }

        /// <summary>
        /// The frame log is generated from the message classes, so a field carrying
        /// neither <see cref="JsonPropertyAttribute"/> nor <see cref="JsonIgnoreAttribute"/>
        /// would drop out of it silently and stop being checked by acceptance.
        /// </summary>
        [TestMethod]
        public void Describe_CoversEveryMessageFieldOrItIsExplicitlyExcluded()
        {
            foreach (var message in new IGdl90Message[]
            {
                new Gdl90OwnshipReport(),
                new Gdl90TrafficReport(),
                new Gdl90Heartbeat(),
                new Gdl90GeometricAltitude(),
                new Gdl90ForeFlightId(),
            })
            {
                AssertEveryMemberIsLogged(message.GetType(), Gdl90LogShape.Describe(message));
            }
        }

        static void AssertEveryMemberIsLogged(Type type, JObject logged)
        {
            var members = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .ToList();

            Assert.AreNotEqual(0, members.Count, type.Name + " has no public members");

            int expected = 0;
            foreach (var member in members)
            {
                string where = type.Name + "." + member.Name;

                var declared = (JsonPropertyAttribute)member
                    .GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                    .FirstOrDefault();
                bool ignored = member
                    .GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Any();

                if (ignored)
                {
                    Assert.IsNull(declared, where + " is both named and ignored");
                    continue;
                }

                Assert.IsNotNull(declared,
                    where + " has neither [JsonProperty] nor [JsonIgnore], so it reaches"
                        + " the log under its C# name or not at all");
                Assert.IsTrue(logged.ContainsKey(declared.PropertyName),
                    where + " declares " + declared.PropertyName
                        + ", which the log does not carry");
                expected++;
            }

            Assert.AreEqual(expected, logged.Count,
                type.Name + " logs a field that no member declares");
        }
    }
}
