using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// Tests for the auto-tour builder over a synthetic tree shaped like the real
    /// test_pipeline.shp fixture: a trunk, a branch off it, and a branch off that branch.
    /// Junctions are exact shared vertices (snapping).
    /// </summary>
    [TestClass]
    public class CorridorTourBuilderTests
    {
        static PointLatLngAlt P(double lat, double lng) => new PointLatLngAlt(lat, lng, 0);

        // Trunk runs east at lat 0; branch B drops south from trunk vertex #2; sub-branch
        // C runs east from B vertex #1. Shared vertices are bit-identical (snapped).
        static List<List<PointLatLngAlt>> Fixture()
        {
            var trunk = new List<PointLatLngAlt> { P(0, 0.00), P(0, 0.01), P(0, 0.02), P(0, 0.03), P(0, 0.04) };
            var b     = new List<PointLatLngAlt> { P(0, 0.02), P(-0.01, 0.02), P(-0.02, 0.02) };
            var c     = new List<PointLatLngAlt> { P(-0.01, 0.02), P(-0.01, 0.03) };
            return new List<List<PointLatLngAlt>> { trunk, b, c };
        }

        [TestMethod]
        public void Build_SplitsAtJunctions()
        {
            var (polys, _) = CorridorTourBuilder.Build(Fixture(), P(0, -0.001), 100, 2);
            // trunk -> 2 edges (split at #2); B -> 2 edges (split at #1); C -> 1 edge.
            Assert.AreEqual(5, polys.Count);
        }

        [TestMethod]
        public void Build_EveryEdgeFlownOutAndBack_OnOppositeOffsets()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Fixture(), P(0, -0.001), 100, 2);

            Assert.AreEqual(polys.Count * 2, tour.Count, "each edge appears as out + back");
            foreach (var pl in polys)
            {
                var steps = tour.Where(s => s.PolylineId == pl.Id).ToList();
                Assert.AreEqual(2, steps.Count, $"edge {pl.Id} flown twice");
                Assert.IsTrue(steps.Any(s => s.LaneOffsetM > 0) && steps.Any(s => s.LaneOffsetM < 0),
                    $"edge {pl.Id} has one +offset pass and one -offset pass");
            }
        }

        [TestMethod]
        public void Build_StartsAtEndpointNearestHome()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Fixture(), P(0, -0.001), 100, 2);
            var firstEdge = polys.First(pl => pl.Id == tour[0].PolylineId);
            bool touchesWestEnd = new[] { firstEdge.Points.First(), firstEdge.Points.Last() }
                .Any(pt => Math.Abs(pt.Lng) < 1e-6 && Math.Abs(pt.Lat) < 1e-6);
            Assert.IsTrue(touchesWestEnd, "tour starts at the west trunk end (nearest home)");
        }

        [TestMethod]
        public void Build_HomeAtFarEnd_StartsThere()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Fixture(), P(0, 0.05), 100, 2);
            var firstEdge = polys.First(pl => pl.Id == tour[0].PolylineId);
            bool touchesEastEnd = new[] { firstEdge.Points.First(), firstEdge.Points.Last() }
                .Any(pt => Math.Abs(pt.Lng - 0.04) < 1e-6 && Math.Abs(pt.Lat) < 1e-6);
            Assert.IsTrue(touchesEastEnd, "with home at the east end the tour starts there");
        }

        [TestMethod]
        public void Build_DetoursSpurBeforeContinuingTrunk()
        {
            var (polys, tour) = CorridorTourBuilder.Build(Fixture(), P(0, -0.001), 100, 2);

            int cId = polys.First(pl => pl.Points.Any(p => Math.Abs(p.Lat + 0.01) < 1e-6 && Math.Abs(p.Lng - 0.03) < 1e-6)).Id;
            int trunkEastId = polys.First(pl => pl.Points.Any(p => Math.Abs(p.Lat) < 1e-6 && Math.Abs(p.Lng - 0.04) < 1e-6)).Id;

            int firstC = tour.FindIndex(s => s.PolylineId == cId);
            int firstTrunkEast = tour.FindIndex(s => s.PolylineId == trunkEastId);
            Assert.IsTrue(firstC >= 0 && firstC < firstTrunkEast,
                "the spur subtree is detoured before the trunk continues past the junction");
        }

        [TestMethod]
        public void Build_FeedsGenerateMissionFromTour()
        {
            var orig = CorridorPlanner.TerrainProvider;
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;
            try
            {
                var (polys, tour) = CorridorTourBuilder.Build(Fixture(), P(0, -0.001), 100, 2);
                var p = new CorridorParameters { MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25 };
                var wps = CorridorPlanner.GenerateMissionFromTour(polys, tour, p, P(0, -0.001));
                Assert.IsTrue(wps.Count > 0, "the auto-built tour generates a waypoint list");
            }
            finally
            {
                CorridorPlanner.TerrainProvider = orig;
            }
        }
    }
}
