using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests
{
    [TestClass]
    public class WmmTests
    {
        // NOAA WMM2025 test values (WMM2025_TEST_VALUES.txt):
        // decimal year, height km, lat, lon, X nT, Y nT, Z nT, declination deg
        static readonly double[,] NoaaTestValues =
        {
            { 2025.0,   0.0,  80.0,   0.0,  6521.6,   145.9,  54791.5,  1.28 },
            { 2025.0,   0.0,   0.0, 120.0, 39677.8,  -109.6, -10580.2, -0.16 },
            { 2025.0,   0.0, -80.0, 240.0,  6117.5, 15751.9, -52022.5, 68.78 },
            { 2025.0, 100.0,  80.0,   0.0,  6216.0,    92.4,  52598.8,  0.85 },
            { 2025.0, 100.0,   0.0, 120.0, 37688.6,   -96.2, -10152.1, -0.15 },
            { 2025.0, 100.0, -80.0, 240.0,  5907.6, 14780.3, -49540.7, 68.21 },
            { 2027.5,   0.0,  80.0,   0.0,  6500.8,   294.5,  54869.4,  2.59 },
            { 2027.5,   0.0,   0.0, 120.0, 39701.6,  -167.4, -10381.8, -0.24 },
            { 2027.5,   0.0, -80.0, 240.0,  6200.7, 15730.3, -51783.7, 68.49 },
            { 2027.5, 100.0,  80.0,   0.0,  6196.7,   233.8,  52670.5,  2.16 },
            { 2027.5, 100.0,   0.0, 120.0, 37711.5,  -148.7,  -9969.8, -0.23 },
            { 2027.5, 100.0, -80.0, 240.0,  5984.0, 14760.1, -49317.7, 67.93 },
        };

        [TestMethod]
        public void Compute_MatchesNoaaTestValues()
        {
            for (var i = 0; i < NoaaTestValues.GetLength(0); i++)
            {
                var field = Wmm.Compute(
                    NoaaTestValues[i, 2],
                    NoaaTestValues[i, 3],
                    NoaaTestValues[i, 1],
                    NoaaTestValues[i, 0]);

                // The published figures are rounded to 0.1 nT and 0.01 deg.
                Assert.AreEqual(NoaaTestValues[i, 4], field.X, 0.1);
                Assert.AreEqual(NoaaTestValues[i, 5], field.Y, 0.1);
                Assert.AreEqual(NoaaTestValues[i, 6], field.Z, 0.1);
                Assert.AreEqual(NoaaTestValues[i, 7], field.Declination, 0.01);
            }
        }

        [TestMethod]
        public void Declination_EastIsPositive()
        {
            var when = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

            // Sydney sits about 12.8 E, Perth about 1.5 W.
            Assert.AreEqual(12.8, Wmm.Declination(-33.87, 151.21, 0, when), 0.2);
            Assert.AreEqual(-1.5, Wmm.Declination(-31.95, 115.86, 0, when), 0.2);
        }

        [TestMethod]
        public void Declination_IsUndefinedAtThePoles()
        {
            var when = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.IsTrue(double.IsNaN(Wmm.Declination(90, 0, 0, when)));
            Assert.IsTrue(double.IsNaN(Wmm.Declination(-90, 0, 0, when)));
        }

        [TestMethod]
        public void Declination_KeepsExtrapolatingPastTheEpoch()
        {
            var inside = Wmm.Declination(-33.87, 151.21, 0, new DateTime(2029, 1, 1));
            var outside = Wmm.Declination(-33.87, 151.21, 0, new DateTime(2035, 1, 1));

            Assert.IsFalse(double.IsNaN(outside));
            Assert.AreEqual(inside, outside, 0.5);
        }

        // Fails on purpose from 2030.0 so the expiry is not forgotten. NOAA
        // publishes the next model the December before: replace the
        // coefficient table in Wmm.cs, bump Epoch and ValidUntil, and swap
        // the NOAA test values above for the new set.
        [TestMethod]
        public void Model_HasNotExpired()
        {
            Assert.IsTrue(Wmm.DecimalYear(DateTime.UtcNow) < Wmm.ValidUntil);
        }

        [TestMethod]
        public void DecimalYear_StartsAtZeroOnNewYearsDay()
        {
            Assert.AreEqual(2025.0, Wmm.DecimalYear(new DateTime(2025, 1, 1)), 1e-9);
            Assert.AreEqual(2025.5, Wmm.DecimalYear(new DateTime(2025, 7, 2, 12, 0, 0)), 1e-9);
            Assert.AreEqual(2028.5, Wmm.DecimalYear(new DateTime(2028, 7, 2, 0, 0, 0)), 1e-9);
        }
    }
}
