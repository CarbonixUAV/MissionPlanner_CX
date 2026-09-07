using System;
using System.Collections.Generic;
using System.IO;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// Differential conformance suite for the traffic report encoder. The fixture was
    /// generated from Stratux's unmodified encoder across the field space, with four
    /// vectors substituted where Stratux departs from the GDL 90 ICD; those carry the
    /// superseded bytes and the reason inline. "expect" is authoritative and all
    /// vectors must match. If a vector disagrees with the encoder, read its reason
    /// and adjudicate against the ICD before changing anything.
    /// </summary>
    [TestClass]
    public class Gdl90TrafficVectorTests
    {
        // Stratux decides the traffic alert bit itself, from the target's distance,
        // and does not take it as a generator input. So the fixture records no alert
        // flag - only the distance that produced one. The generator was driven at two
        // widely separated distances, 1 km and 100 km, and the bit is recovered from
        // which of those bands a vector falls in. Anything between them is rejected
        // rather than guessed at: picking a threshold across the gap would assert a
        // proximity rule that neither Stratux nor this encoder actually applies.
        private const double AlertingAtOrBelowMeters = 1000;
        private const double NotAlertingAtOrAboveMeters = 100000;

        /// <summary>
        /// Whether the fixture generator raised the alert bit for this vector.
        /// </summary>
        private static bool AlertedFor(VectorInput input)
        {
            if (!input.BearingDist_valid) return false;
            if (input.Distance <= AlertingAtOrBelowMeters) return true;
            if (input.Distance >= NotAlertingAtOrAboveMeters) return false;

            Assert.Fail("distance " + input.Distance + " m falls between the fixture's"
                + " alerting and non-alerting bands, so the alert bit cannot be recovered");
            return false;
        }

        private class VectorFile
        {
            [JsonProperty("vectors")] public List<Vector> Vectors { get; set; }
        }

        private class Vector
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("in")] public VectorInput Input { get; set; }
            [JsonProperty("expect")] public string Expect { get; set; }
            [JsonProperty("superseded_stratux")] public string SupersededStratux { get; set; }
            [JsonProperty("reason")] public string Reason { get; set; }
        }

        private class VectorInput
        {
            public long Icao_addr { get; set; }
            public int Emitter_category { get; set; }
            public bool OnGround { get; set; }
            public int Addr_type { get; set; }
            public double Lat { get; set; }
            public double Lng { get; set; }
            public int Alt { get; set; }
            public bool AltIsGNSS { get; set; }
            public double Track { get; set; }
            public int Speed { get; set; }
            public bool Speed_valid { get; set; }
            public int Vvel { get; set; }
            public int NIC { get; set; }
            public int NACp { get; set; }
            public int PriorityStatus { get; set; }
            public string Tail { get; set; }
            public bool ExtrapolatedPosition { get; set; }
            public bool BearingDist_valid { get; set; }
            public double Distance { get; set; }
        }

        private static readonly Lazy<Dictionary<string, Vector>> VectorsByName =
            new Lazy<Dictionary<string, Vector>>(LoadVectors);

        private static string FixturePath
        {
            get
            {
                string baseDir = Path.GetDirectoryName(typeof(Gdl90TrafficVectorTests).Assembly.Location);
                return Path.Combine(baseDir, "Fixtures", "gdl90_traffic_vectors.json");
            }
        }

        private static Dictionary<string, Vector> LoadVectors()
        {
            var file = JsonConvert.DeserializeObject<VectorFile>(File.ReadAllText(FixturePath));
            var byName = new Dictionary<string, Vector>();
            foreach (var v in file.Vectors) byName.Add(v.Name, v);
            return byName;
        }

        public static IEnumerable<object[]> VectorNames()
        {
            foreach (string name in VectorsByName.Value.Keys) yield return new object[] { name };
        }

        [TestMethod]
        public void Fixture_CarriesAll79Vectors()
        {
            Assert.AreEqual(79, VectorsByName.Value.Count);
        }

        /// <summary>
        /// One vector alerts and every other carries a distance that says it does not.
        /// </summary>
        /// <remarks>
        /// Recovering the bit from Distance holds only while the fixture keeps the
        /// shape it was generated with, so the shape is asserted rather than assumed.
        /// </remarks>
        [TestMethod]
        public void Fixture_HasExactlyOneAlertingVector()
        {
            int alerting = 0;
            foreach (Vector vector in VectorsByName.Value.Values)
            {
                Assert.IsTrue(vector.Input.BearingDist_valid,
                    vector.Name + " has no distance for the alert bit to come from");

                if (AlertedFor(vector.Input)) alerting++;
            }

            Assert.AreEqual(1, alerting);
        }

        [DataTestMethod]
        [DynamicData(nameof(VectorNames), DynamicDataSourceType.Method)]
        public void TrafficReport_MatchesFixtureVector(string name)
        {
            Vector vector = VectorsByName.Value[name];
            VectorInput input = vector.Input;

            Assert.IsFalse(input.AltIsGNSS, "fixture altitudes are pressure altitudes; a GNSS input needs a new mapping decision");

            var report = new Gdl90Report
            {
                TrafficAlert = AlertedFor(input),
                AddressType = (Gdl90AddressType)input.Addr_type,
                Address = (int)input.Icao_addr,
                LatitudeE7 = (long)Math.Round(input.Lat * 1e7),
                LongitudeE7 = (long)Math.Round(input.Lng * 1e7),
                PressureAltitudeFeet = input.Alt,
                AltitudeValid = true,
                Airborne = !input.OnGround,
                Extrapolated = input.ExtrapolatedPosition,
                // In the fixture's input model, Speed_valid gates the track-validity
                // bits of the misc field; the speed field itself is always encoded.
                TrackType = input.Speed_valid ? Gdl90TrackType.TrueTrack : Gdl90TrackType.Invalid,
                Nic = input.NIC,
                Nacp = input.NACp,
                HorizontalVelocityKnots = input.Speed,
                HorizontalVelocityValid = true,
                VerticalVelocityFpm = input.Vvel,
                VerticalVelocityValid = true,
                TrackDegrees = input.Track,
                EmitterCategory = input.Emitter_category,
                Callsign = input.Tail,
                EmergencyPriorityCode = input.PriorityStatus,
            };

            string actual = Gdl90TestUtil.ToHex(Gdl90Frame.Frame(Gdl90Messages.TrafficReport(report)));

            string context = vector.Reason == null
                ? $"vector '{name}'"
                : $"vector '{name}' (adjudicated against the ICD: {vector.Reason})";
            Assert.AreEqual(vector.Expect, actual, context);
        }
    }
}
