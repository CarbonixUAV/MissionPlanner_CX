using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// Conformance suite for the static pressure to pressure altitude conversion.
    /// Expected altitudes are transcribed from the US Standard Atmosphere 1976,
    /// Table IV (geopotential altitude, English altitudes). The rows span the GDL 90
    /// altitude field's full range and cross both ISA layer boundaries.
    /// </summary>
    [TestClass]
    public class PressureAltitudeTests
    {
        // Layer base pressures as published, and as printed in the fixture's _about.
        // The implementation derives its own; these classify rows by layer and check
        // that derivation against the source.
        private const double TropopauseBasePressureHpa = 226.3204;
        private const double StratosphereBasePressureHpa = 54.7488;

        private const double TropopauseAltitudeFeet = 11000.0 / 0.3048;
        private const double StratosphereBaseAltitudeFeet = 20000.0 / 0.3048;

        private class FixtureFile
        {
            [JsonProperty("vectors")] public List<Vector> Vectors { get; set; }
        }

        private class Vector
        {
            [JsonProperty("press_hpa")] public double PressureHpa { get; set; }
            [JsonProperty("expect_ft")] public double ExpectFeet { get; set; }
            [JsonProperty("page")] public int Page { get; set; }
            [JsonProperty("uncertainty_ft")] public double UncertaintyFeet { get; set; }
        }

        private static readonly Lazy<Dictionary<double, Vector>> VectorsByAltitude =
            new Lazy<Dictionary<double, Vector>>(LoadVectors);

        private static string FixturePath
        {
            get
            {
                string baseDir = Path.GetDirectoryName(typeof(PressureAltitudeTests).Assembly.Location);
                return Path.Combine(baseDir, "Fixtures", "isa_pressure_altitude.json");
            }
        }

        private static Dictionary<double, Vector> LoadVectors()
        {
            var file = JsonConvert.DeserializeObject<FixtureFile>(File.ReadAllText(FixturePath));
            var byAltitude = new Dictionary<double, Vector>();
            foreach (var v in file.Vectors) byAltitude.Add(v.ExpectFeet, v);
            return byAltitude;
        }

        public static IEnumerable<object[]> VectorAltitudes()
        {
            foreach (double altitude in VectorsByAltitude.Value.Keys) yield return new object[] { altitude };
        }

        [TestMethod]
        public void Fixture_CarriesAll12Vectors()
        {
            Assert.AreEqual(12, VectorsByAltitude.Value.Count);
        }

        // Tolerance is exactly uncertainty_ft, nothing more. The source tabulates by
        // altitude and prints the pressure reached at it to five significant figures, so
        // expect_ft is exact and press_hpa carries a last-place window; uncertainty_ft is
        // that window in feet.
        [DataTestMethod]
        [DynamicData(nameof(VectorAltitudes), DynamicDataSourceType.Method)]
        public void TryFromStaticPressure_MatchesUsStandardAtmosphereTable(double expectFeet)
        {
            Vector vector = VectorsByAltitude.Value[expectFeet];

            double actualFeet;
            Assert.IsTrue(
                PressureAltitude.TryFromStaticPressure(vector.PressureHpa, out actualFeet),
                string.Format(CultureInfo.InvariantCulture,
                    "{0} hPa is a published altitude of {1} ft and must be encodable",
                    vector.PressureHpa, vector.ExpectFeet));

            Assert.AreEqual(vector.ExpectFeet, actualFeet, vector.UncertaintyFeet,
                string.Format(CultureInfo.InvariantCulture,
                    "{0} hPa (Table IV p.{1}): expected {2} ft +/- {3}, got {4:F3} ft",
                    vector.PressureHpa, vector.Page, vector.ExpectFeet, vector.UncertaintyFeet, actualFeet));
        }

        /// <summary>
        /// Every layer must carry rows, or a layer ships unexercised and the suite
        /// would pass with it unimplemented.
        /// </summary>
        [TestMethod]
        public void Fixture_ExercisesAllThreeLayers()
        {
            int troposphere = 0, tropopause = 0, stratosphere = 0;
            foreach (Vector v in VectorsByAltitude.Value.Values)
            {
                if (v.PressureHpa >= TropopauseBasePressureHpa) troposphere++;
                else if (v.PressureHpa >= StratosphereBasePressureHpa) tropopause++;
                else stratosphere++;
            }

            Assert.IsTrue(troposphere > 0, "no row in the troposphere gradient layer");
            Assert.IsTrue(tropopause > 0, "no row in the isothermal layer, where the gradient form divides by zero");
            Assert.IsTrue(stratosphere > 0, "no row in the stratosphere gradient layer");
        }

        /// <summary>
        /// The implementation derives each layer's base pressure from the layer below.
        /// Evaluating at the published base pressures must return the published boundary
        /// altitudes, which pins the derivation and the continuity across it. The
        /// allowance is for the published pressures being rounded to seven figures, not
        /// for any slack in the model.
        /// </summary>
        [TestMethod]
        public void LayerBoundariesLandAtTheirPublishedAltitudes()
        {
            double feet;

            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(TropopauseBasePressureHpa, out feet));
            Assert.AreEqual(TropopauseAltitudeFeet, feet, 0.05, "tropopause base is not at 11 km");

            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(StratosphereBasePressureHpa, out feet));
            Assert.AreEqual(StratosphereBaseAltitudeFeet, feet, 0.05, "stratosphere base is not at 20 km");
        }

        /// <summary>
        /// A discontinuity at a layer boundary is the signature of a wrong base pressure.
        /// Stepping across each boundary by an ulp must not move the altitude measurably.
        /// Probe at the derived pressures, not the published ones: the two differ by up
        /// to 2.6e-5 hPa, so a probe around the published value stays inside one layer
        /// and never crosses the switch it is meant to test.
        /// </summary>
        [TestMethod]
        public void AltitudeIsContinuousAcrossLayerBoundaries()
        {
            AssertContinuousAt(PressureAltitude.Layer1BasePressureHpa);
            AssertContinuousAt(PressureAltitude.Layer2BasePressureHpa);
        }

        private static void AssertContinuousAt(double boundaryPressureHpa)
        {
            double below, above;
            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(boundaryPressureHpa * (1.0 - 1e-9), out above));
            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(boundaryPressureHpa * (1.0 + 1e-9), out below));

            Assert.AreEqual(above, below, 1e-3,
                string.Format(CultureInfo.InvariantCulture,
                    "altitude jumps across the layer boundary at {0} hPa", boundaryPressureHpa));
        }

        [TestMethod]
        public void SeaLevelPressureIsTheZeroOfTheDatum()
        {
            double feet;
            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(PressureAltitude.SeaLevelPressureHpa, out feet));
            Assert.AreEqual(0.0, feet, 1e-9);
        }

        /// <summary>
        /// Altitude must fall monotonically as pressure rises, across every layer and
        /// through both boundaries. A sign error in one layer's lapse rate or exponent
        /// shows up here even where no fixture row sits.
        /// </summary>
        [TestMethod]
        public void AltitudeDecreasesMonotonicallyWithPressure()
        {
            double previous = double.MaxValue;
            for (double pressure = 11.0; pressure <= 1050.0; pressure += 0.5)
            {
                double feet;
                Assert.IsTrue(PressureAltitude.TryFromStaticPressure(pressure, out feet),
                    string.Format(CultureInfo.InvariantCulture, "{0} hPa is within the encodable range", pressure));
                Assert.IsTrue(feet < previous,
                    string.Format(CultureInfo.InvariantCulture,
                        "altitude did not decrease at {0} hPa: {1:F3} ft followed {2:F3} ft", pressure, feet, previous));
                previous = feet;
            }
        }

        [DataTestMethod]
        [DataRow(0.0, DisplayName = "zero")]
        [DataRow(-1.0, DisplayName = "negative")]
        [DataRow(-1013.25, DisplayName = "negated sea level")]
        [DataRow(double.NaN, DisplayName = "NaN")]
        [DataRow(double.PositiveInfinity, DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, DisplayName = "negative infinity")]
        public void NonPhysicalPressureIsRejected(double pressureHpa)
        {
            double feet;
            Assert.IsFalse(PressureAltitude.TryFromStaticPressure(pressureHpa, out feet));
            Assert.AreEqual(0.0, feet);
        }

        [DataTestMethod]
        [DataRow(5.0, DisplayName = "above the ceiling")]
        [DataRow(1200.0, DisplayName = "below the floor")]
        public void PressureOutsideTheEncodableRangeIsRejected(double pressureHpa)
        {
            double feet;
            Assert.IsFalse(PressureAltitude.TryFromStaticPressure(pressureHpa, out feet));
            Assert.AreEqual(0.0, feet);
        }

        [DataTestMethod]
        [DataRow(1e-45, DisplayName = "float32 denormal, finite altitude")]
        [DataRow(1e-300, DisplayName = "far below the layer 2 base, finite altitude")]
        [DataRow(1e-322, DisplayName = "underflows the layer 2 ratio, infinite altitude")]
        [DataRow(double.Epsilon, DisplayName = "smallest denormal, infinite altitude")]
        public void ImplausiblySmallPressureIsRejected(double pressureHpa)
        {
            double feet;
            Assert.IsFalse(PressureAltitude.TryFromStaticPressure(pressureHpa, out feet));
            Assert.AreEqual(0.0, feet);
        }

        /// <summary>
        /// The bound is the GDL 90 altitude field's own, and nothing tighter: bisecting
        /// for the pressure where acceptance flips must land on the field limit itself.
        /// </summary>
        [TestMethod]
        public void RejectionBeginsExactlyAtTheEncodableBounds()
        {
            double feet;

            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(LastAcceptedPressure(20.0, 1.0), out feet));
            Assert.AreEqual(Gdl90Messages.MaximumAltitudeFeet, feet, 1e-6);

            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(LastAcceptedPressure(1013.25, 1200.0), out feet));
            Assert.AreEqual(Gdl90Messages.MinimumAltitudeFeet, feet, 1e-6);
        }

        /// <summary>Bisects toward the rejected end and returns the last pressure still accepted.</summary>
        private static double LastAcceptedPressure(double acceptedHpa, double rejectedHpa)
        {
            double unused;
            Assert.IsTrue(PressureAltitude.TryFromStaticPressure(acceptedHpa, out unused));
            Assert.IsFalse(PressureAltitude.TryFromStaticPressure(rejectedHpa, out unused));

            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5 * (acceptedHpa + rejectedHpa);
                if (PressureAltitude.TryFromStaticPressure(mid, out unused)) acceptedHpa = mid;
                else rejectedHpa = mid;
            }
            return acceptedHpa;
        }
    }
}
