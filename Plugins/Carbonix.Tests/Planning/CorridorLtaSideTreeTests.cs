using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    [TestClass]
    public class CorridorLtaSideTreeTests
    {
        Func<double, double, double> _origTerrain;

        [TestInitialize]
        public void Setup()
        {
            _origTerrain = CorridorPlanner.TerrainProvider;
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;
        }

        [TestCleanup]
        public void Teardown() => CorridorPlanner.TerrainProvider = _origTerrain;

        static PointLatLngAlt P(double lat, double lng) => new PointLatLngAlt(lat, lng, 0);

        static List<List<PointLatLngAlt>> Tree()
        {
            var trunk = new List<PointLatLngAlt> { P(-33.0, 151.00), P(-33.0, 151.01), P(-33.0, 151.02), P(-33.0, 151.03), P(-33.0, 151.05) };
            var b     = new List<PointLatLngAlt> { P(-33.0, 151.02), P(-33.01, 151.02), P(-33.02, 151.02) };
            var c     = new List<PointLatLngAlt> { P(-33.01, 151.02), P(-33.01, 151.03) };
            return new List<List<PointLatLngAlt>> { trunk, b, c };
        }

        static CorridorParameters Params() => new CorridorParameters
        {
            MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 100,
        };

        static LoiterToAlt Lta(int edge, int seg, double t, int id, int side = 1) => new LoiterToAlt
        {
            PolylineId = edge, SegmentIndex = seg, T = t, Id = id, LoiterId = id + 1, Side = side,
            LtaAltRelM = 500, LeadInAltRelM = 400,
        };

        // (lat, lng, sign of P2) of every spiral with this id, in mission order.
        static List<(double, double, int)> Spirals(List<CorridorWaypoint> wps, int loiterId) =>
            wps.Where(w => w.Command == MAVLink.MAV_CMD.LOITER_TO_ALT && w.CorridorVertexIndex == loiterId)
               .Select(w => (Math.Round(w.Lat, 7), Math.Round(w.Lng, 7), Math.Sign(w.P2))).ToList();

        static void AssertExistingSpiralsUnchanged(bool reverse, bool oneWay, LoiterToAlt added)
        {
            var home = P(-33.0, 150.999);
            var (polys, tour) = CorridorTourBuilder.Build(Tree(), home, 100, oneWay, reverse);
            int trunkWest = polys.First(p => p.Points.Any(q => Math.Abs(q.Lng - 151.00) < 1e-9)).Id;
            int bUpper = polys.First(p => p.Points.Any(q => Math.Abs(q.Lat + 33.0) < 1e-9) && p.Points.Any(q => Math.Abs(q.Lat + 33.01) < 1e-9)).Id;
            int cEdge = polys.First(p => p.Points.Any(q => Math.Abs(q.Lng - 151.03) < 1e-9 && Math.Abs(q.Lat + 33.01) < 1e-9)).Id;

            var existing = new List<LoiterToAlt>
            {
                Lta(trunkWest, 0, 0.5, 100000),
                Lta(bUpper, 0, 0.4, 100002, side: -1),
                Lta(cEdge, 0, 0.6, 100004),
            };
            var before = CorridorPlanner.GenerateMissionFromTour(polys, tour, Params(), home, null, existing);
            var with = existing.ToList();
            with.Add(added);
            var after = CorridorPlanner.GenerateMissionFromTour(polys, tour, Params(), home, null, with);

            foreach (var lta in existing)
            {
                var b = Spirals(before, lta.LoiterId);
                var a = Spirals(after, lta.LoiterId);
                Assert.IsTrue(b.Count > 0, $"lta {lta.Id} present before");
                CollectionAssert.AreEqual(b, a, $"lta {lta.Id} (edge {lta.PolylineId}) reverse={reverse} oneWay={oneWay}");
            }
        }

        [TestMethod]
        public void Reversed_AddOnOtherEdge() =>
            AssertExistingSpiralsUnchanged(reverse: true, oneWay: false, added: Lta(3, 0, 0.5, 100010));

        [TestMethod]
        public void Reversed_AddOnSameEdgeAfter() =>
            AssertExistingSpiralsUnchanged(reverse: true, oneWay: false, added: Lta(0, 1, 0.5, 100010));

        [TestMethod]
        public void Reversed_AddOnSameEdgeBefore() =>
            AssertExistingSpiralsUnchanged(reverse: true, oneWay: false, added: Lta(0, 0, 0.2, 100010));

        [TestMethod]
        public void Forward_AddOnOtherEdge() =>
            AssertExistingSpiralsUnchanged(reverse: false, oneWay: false, added: Lta(3, 0, 0.5, 100010));

        [TestMethod]
        public void ReversedOneWay_AddOnOtherEdge() =>
            AssertExistingSpiralsUnchanged(reverse: true, oneWay: true, added: Lta(3, 0, 0.5, 100010));

        [TestMethod]
        public void ReversedOneWay_AddOnSameEdgeBefore() =>
            AssertExistingSpiralsUnchanged(reverse: true, oneWay: true, added: Lta(0, 0, 0.2, 100010));
    }
}
