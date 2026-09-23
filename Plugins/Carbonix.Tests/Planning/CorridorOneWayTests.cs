using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// One-way tours: the start→end path is flown once on the centreline, spurs off it are
    /// still out and back. Fixture at the equator so degrees are the same size both ways:
    /// trunk east along lat 0 to lng 0.05 (longest leaf), branch B south from (0, 0.02),
    /// sub-branch C east from B's midpoint.
    /// </summary>
    [TestClass]
    public class CorridorOneWayTests
    {
        static PointLatLngAlt P(double lat, double lng) => new PointLatLngAlt(lat, lng, 0);

        static List<List<PointLatLngAlt>> Tree()
        {
            var trunk = new List<PointLatLngAlt> { P(0, 0.00), P(0, 0.01), P(0, 0.02), P(0, 0.03), P(0, 0.05) };
            var b     = new List<PointLatLngAlt> { P(0, 0.02), P(-0.01, 0.02), P(-0.02, 0.02) };
            var c     = new List<PointLatLngAlt> { P(-0.01, 0.02), P(-0.01, 0.03) };
            return new List<List<PointLatLngAlt>> { trunk, b, c };
        }

        static readonly PointLatLngAlt Home = P(0, -0.001);

        static bool Near(PointLatLngAlt p, double lat, double lng) =>
            Math.Abs(p.Lat - lat) < 1e-6 && Math.Abs(p.Lng - lng) < 1e-6;

        static PointLatLngAlt StepStart(List<Polyline> polys, TourStep s)
        {
            var pts = polys.First(p => p.Id == s.PolylineId).Points;
            return s.Direction == TraverseDir.Forward ? pts[0] : pts[pts.Count - 1];
        }

        static PointLatLngAlt StepEnd(List<Polyline> polys, TourStep s)
        {
            var pts = polys.First(p => p.Id == s.PolylineId).Points;
            return s.Direction == TraverseDir.Forward ? pts[pts.Count - 1] : pts[0];
        }

        [TestMethod]
        public void StraightLine_OneWay_SingleStepEndToEnd()
        {
            var line = new List<List<PointLatLngAlt>> { new List<PointLatLngAlt> { P(0, 0), P(0, 0.01), P(0, 0.02) } };
            var (polys, tour) = CorridorTourBuilder.Build(line, Home, 100, oneWay: true);

            Assert.AreEqual(1, tour.Count);
            Assert.IsTrue(tour[0].OneWay);
            Assert.AreEqual(0, tour[0].LaneOffsetM);
            Assert.IsTrue(Near(StepStart(polys, tour[0]), 0, 0));
            Assert.IsTrue(Near(StepEnd(polys, tour[0]), 0, 0.02));
        }

        [TestMethod]
        public void StraightLine_RoundTrip_StillOutAndBack()
        {
            var line = new List<List<PointLatLngAlt>> { new List<PointLatLngAlt> { P(0, 0), P(0, 0.02) } };
            var (_, tour) = CorridorTourBuilder.Build(line, Home, 100);
            Assert.AreEqual(2, tour.Count);
            Assert.IsFalse(tour.Any(s => s.OneWay));
        }

        [TestMethod]
        public void Tree_OneWay_FarthestLeafPathFlownOnce_SpursOutAndBack()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Tree(), Home, 100, oneWay: true);

            // 5 edges: trunk west/east, B upper/lower, C. Exit path = the two trunk edges.
            Assert.AreEqual(5, polys.Count);
            Assert.AreEqual(2 * 5 - 2, tour.Count);

            var perEdge = tour.GroupBy(s => s.PolylineId).ToDictionary(g => g.Key, g => g.ToList());
            int trunkWest = polys.First(p => p.Points.Any(q => Near(q, 0, 0.00))).Id;
            int trunkEast = polys.First(p => p.Points.Any(q => Near(q, 0, 0.05))).Id;
            foreach (var pl in polys)
            {
                bool onPath = pl.Id == trunkWest || pl.Id == trunkEast;
                Assert.AreEqual(onPath ? 1 : 2, perEdge[pl.Id].Count);
                Assert.AreEqual(onPath, perEdge[pl.Id].All(s => s.OneWay));
            }

            Assert.IsTrue(Near(StepStart(polys, tour[0]), 0, 0));
            Assert.IsTrue(Near(StepEnd(polys, tour[tour.Count - 1]), 0, 0.05));
        }

        [TestMethod]
        public void Tree_OneWay_WalkIsConnected()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Tree(), Home, 100, oneWay: true);
            for (int i = 0; i + 1 < tour.Count; i++)
            {
                var a = StepEnd(polys, tour[i]);
                var b = StepStart(polys, tour[i + 1]);
                Assert.IsTrue(Near(a, b.Lat, b.Lng), $"step {i} → {i + 1}");
            }
        }

        [TestMethod]
        public void Tree_OneWay_ManualEnd_EndsAtNearestDeadEnd()
        {
            // Ask for the tip of C: the exit path becomes trunk-west, B-upper, C.
            var (polys, tour) = CorridorTourBuilder.Build(Tree(), Home, 100, oneWay: true,
                oneWayEnd: P(-0.0101, 0.0301));

            Assert.IsTrue(Near(StepEnd(polys, tour[tour.Count - 1]), -0.01, 0.03));
            Assert.AreEqual(2 * 5 - 3, tour.Count);

            int cId = polys.First(p => p.Points.Any(q => Near(q, -0.01, 0.03))).Id;
            int trunkEast = polys.First(p => p.Points.Any(q => Near(q, 0, 0.05))).Id;
            Assert.IsTrue(tour.Single(s => s.PolylineId == cId).OneWay);
            Assert.AreEqual(2, tour.Count(s => s.PolylineId == trunkEast));
        }

        [TestMethod]
        public void Tree_OneWay_Reverse_RunsFromEndToStart()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Tree(), Home, 100, oneWay: true, reverse: true);
            Assert.IsTrue(Near(StepStart(polys, tour[0]), 0, 0.05));
            Assert.IsTrue(Near(StepEnd(polys, tour[tour.Count - 1]), 0, 0));
        }

        [TestMethod]
        public void Loop_OneWay_FallsBackToRoundTrip()
        {
            var square = new List<List<PointLatLngAlt>>
            {
                new List<PointLatLngAlt> { P(0, 0), P(0, 0.01), P(0.01, 0.01), P(0.01, 0), P(0, 0) },
            };
            var (polys, tour) = CorridorTourBuilder.Build(square, Home, 100, oneWay: true);
            Assert.AreEqual(2 * polys.Count, tour.Count);
            Assert.IsFalse(tour.Any(s => s.OneWay));
        }

        [TestMethod]
        public void Engine_OneWayStep_FliesCentreline()
        {
            var orig = CorridorPlanner.TerrainProvider;
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;
            try
            {
                var poly = new Polyline
                {
                    Id = VertexId.MainLine,
                    Points = new List<PointLatLngAlt> { P(-33, 151), P(-33, 151.02) },
                };
                var tour = new List<TourStep>
                {
                    new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward, OneWay = true },
                };
                var p = new CorridorParameters { MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 120 };

                var wps = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, p, poly.Points[0]);
                var lineWps = wps.Where(w => w.IsLineWaypoint).ToList();
                Assert.AreEqual(2, lineWps.Count);
                foreach (var w in lineWps)
                    Assert.AreEqual(-33, w.Lat, 1e-5);
            }
            finally
            {
                CorridorPlanner.TerrainProvider = orig;
            }
        }

        [TestMethod]
        public void Engine_ZeroLaneSep_BothPassesOnCentreline()
        {
            var orig = CorridorPlanner.TerrainProvider;
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;
            try
            {
                var poly = new Polyline
                {
                    Id = VertexId.MainLine,
                    Points = new List<PointLatLngAlt> { P(-33, 151), P(-33, 151.02) },
                };
                var tour = new List<TourStep>
                {
                    new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                    new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
                };
                var p = new CorridorParameters { MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 0 };

                var wps = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, p, poly.Points[0]);
                foreach (var w in wps.Where(w => w.IsLineWaypoint))
                    Assert.AreEqual(-33, w.Lat, 1e-5);
            }
            finally
            {
                CorridorPlanner.TerrainProvider = orig;
            }
        }
    }
}
