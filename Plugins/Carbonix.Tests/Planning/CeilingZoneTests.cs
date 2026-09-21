using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    [TestClass]
    public class CeilingZoneTests
    {
        static PointLatLngAlt P(double lat, double lng) => new PointLatLngAlt(lat, lng, 0);

        // Axis-aligned square (lat0..lat1, lng0..lng1), optionally closed by repeating the first point.
        static List<PointLatLngAlt> Square(double lat0, double lng0, double lat1, double lng1, bool closed = false)
        {
            var ring = new List<PointLatLngAlt> { P(lat0, lng0), P(lat0, lng1), P(lat1, lng1), P(lat1, lng0) };
            if (closed) ring.Add(P(lat0, lng0));
            return ring;
        }

        [TestMethod]
        public void Contains_InsideAndOutside()
        {
            var z = new CeilingZone("a", Square(0, 0, 1, 1), 120);
            Assert.IsTrue(z.Contains(0.5, 0.5));
            Assert.IsFalse(z.Contains(1.5, 0.5));
            Assert.IsFalse(z.Contains(0.5, -0.1));
        }

        [TestMethod]
        public void Contains_ClosedRingSameAsOpen()
        {
            var open = new CeilingZone("a", Square(0, 0, 1, 1), 120);
            var closed = new CeilingZone("a", Square(0, 0, 1, 1, closed: true), 120);
            foreach (var (lat, lng) in new[] { (0.5, 0.5), (0.99, 0.01), (1.01, 0.5), (-0.5, 0.5) })
                Assert.AreEqual(open.Contains(lat, lng), closed.Contains(lat, lng));
        }

        [TestMethod]
        public void Contains_ConcaveNotchIsOutside()
        {
            // L-shape: a 2x2 square with its top-right quadrant removed.
            var ring = new List<PointLatLngAlt>
            {
                P(0, 0), P(0, 2), P(1, 2), P(1, 1), P(2, 1), P(2, 0),
            };
            var z = new CeilingZone("L", ring, 120);
            Assert.IsTrue(z.Contains(0.5, 1.5));
            Assert.IsTrue(z.Contains(1.5, 0.5));
            Assert.IsFalse(z.Contains(1.5, 1.5));
        }

        [TestMethod]
        public void Contains_DegenerateRingIsFalse()
        {
            var z = new CeilingZone("line", new[] { P(0, 0), P(0, 1) }, 120);
            Assert.IsFalse(z.Contains(0, 0.5));
        }

        [TestMethod]
        public void CeilingAt_IslandOverridesBlanket()
        {
            var zones = new List<CeilingZone>
            {
                new CeilingZone("blanket", Square(0, 0, 10, 10), 120),
                new CeilingZone("island", Square(4, 4, 6, 6), 300),
            };
            Assert.AreEqual(300, CeilingZone.CeilingAt(zones, 5, 5));
            Assert.AreEqual(120, CeilingZone.CeilingAt(zones, 1, 1));
            Assert.IsNull(CeilingZone.CeilingAt(zones, 11, 5));
        }

        [TestMethod]
        public void CeilingAt_HighestWinsRegardlessOfOrder()
        {
            var zones = new List<CeilingZone>
            {
                new CeilingZone("island", Square(4, 4, 6, 6), 300),
                new CeilingZone("blanket", Square(0, 0, 10, 10), 120),
            };
            Assert.AreEqual(300, CeilingZone.CeilingAt(zones, 5, 5));
        }

        [TestMethod]
        public void CeilingAt_NoZonesIsNull()
        {
            Assert.IsNull(CeilingZone.CeilingAt(new List<CeilingZone>(), 5, 5));
        }

        [TestMethod]
        public void ParseCeilingFromName_Metres()
        {
            Assert.AreEqual(300, CeilingZone.ParseCeilingFromName("Area B 300m"));
            Assert.AreEqual(120, CeilingZone.ParseCeilingFromName("Site 120 m AGL"));
            Assert.AreEqual(150.5, CeilingZone.ParseCeilingFromName("150.5 metres"));
        }

        [TestMethod]
        public void ParseCeilingFromName_FeetConverted()
        {
            Assert.AreEqual(304.8, CeilingZone.ParseCeilingFromName("North (1000 ft)").Value, 1e-9);
            Assert.AreEqual(121.92, CeilingZone.ParseCeilingFromName("400 feet").Value, 1e-9);
        }

        [TestMethod]
        public void ParseCeilingFromName_NoAltitude()
        {
            Assert.IsNull(CeilingZone.ParseCeilingFromName("Zone 3"));
            Assert.IsNull(CeilingZone.ParseCeilingFromName("Mission 2"));
            Assert.IsNull(CeilingZone.ParseCeilingFromName(""));
            Assert.IsNull(CeilingZone.ParseCeilingFromName(null));
        }

        [TestMethod]
        public void SampleCeilingZones_SetsPerSampleCeiling()
        {
            var zones = new List<CeilingZone>
            {
                new CeilingZone("blanket", Square(0, 0, 10, 10), 120),
                new CeilingZone("island", Square(4, 4, 6, 6), 300),
            };
            var samples = new List<ElevationPoint>
            {
                new ElevationPoint { Lat = 1, Lng = 1 },
                new ElevationPoint { Lat = 5, Lng = 5 },
                new ElevationPoint { Lat = 20, Lng = 20 },
            };

            CorridorPlanForm.SampleCeilingZones(samples, zones);

            Assert.AreEqual(120, samples[0].CeilingZoneAglM);
            Assert.AreEqual(300, samples[1].CeilingZoneAglM);
            Assert.IsTrue(double.IsNaN(samples[2].CeilingZoneAglM));
        }
    }
}
