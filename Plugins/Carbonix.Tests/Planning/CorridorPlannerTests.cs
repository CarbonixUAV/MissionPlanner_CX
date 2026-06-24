using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// Characterization tests for <see cref="CorridorPlanner"/>. They snapshot current
    /// behaviour so the polyline/tour refactor (.claude/corridor-tree-design.md) can be
    /// verified as behaviour-preserving for the cases meant to stay identical.
    ///
    /// Terrain is stubbed via <see cref="CorridorPlanner.TerrainProvider"/> for
    /// determinism — the real SRTM source is non-deterministic and slow in a test env.
    /// </summary>
    [TestClass]
    public class CorridorPlannerTests
    {
        const double BaseLat = -33.0;
        const double BaseLng = 151.0;
        const double MetresPerDegLat = 111319.5;
        static double MetresPerDegLng => MetresPerDegLat * Math.Cos(BaseLat * Math.PI / 180.0);

        Func<double, double, double> _origTerrain;

        [TestInitialize]
        public void Setup()
        {
            _origTerrain = CorridorPlanner.TerrainProvider;
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;   // flat unless a test overrides
        }

        [TestCleanup]
        public void Teardown()
        {
            CorridorPlanner.TerrainProvider = _origTerrain;
        }

        static PointLatLngAlt P(double lat, double lng) => new PointLatLngAlt(lat, lng, 0);

        static CorridorParameters Params(int passes = 1, double offset = 0) => new CorridorParameters
        {
            MinAGL = 50,
            MaxAGL = 120,
            DefaultAGL = 80,
            SpeedMs = 25,
            NumberOfPasses = passes,
            PassOffsetM = offset,
        };

        // ── Geometry ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void StraightLine_SinglePass_TwoWaypointsAtEndpoints()
        {
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            Assert.AreEqual(2, wps.Count);
            Assert.IsTrue(wps.All(w => w.Command == MAVLink.MAV_CMD.WAYPOINT));
            Assert.AreEqual(BaseLng, wps[0].Lng, 1e-6);
            Assert.AreEqual(BaseLng + 0.02, wps[1].Lng, 1e-6);
        }

        [TestMethod]
        public void SharpCorner_ProducesLoiter()
        {
            var line = new List<PointLatLngAlt>
            {
                P(BaseLat, BaseLng),
                P(BaseLat, BaseLng + 0.02),
                P(BaseLat - 0.02, BaseLng + 0.02),   // ~90° turn — above full-orbit threshold
            };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            Assert.IsTrue(wps.Any(w => w.Command == MAVLink.MAV_CMD.LOITER_TURNS),
                "a ~90° corner should generate a loiter turn");
        }

        [TestMethod]
        public void GentleBend_StaysPlainWaypoints()
        {
            var line = new List<PointLatLngAlt>
            {
                P(BaseLat, BaseLng),
                P(BaseLat, BaseLng + 0.02),
                P(BaseLat - 0.0005, BaseLng + 0.04),   // ~2° bend — below corner-cut threshold
            };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            Assert.IsFalse(wps.Any(w => w.Command == MAVLink.MAV_CMD.LOITER_TURNS),
                "a shallow bend should stay plain waypoints");
        }

        [TestMethod]
        public void TwoPasses_ProduceOffsetLanesOnBothSides()
        {
            // Even pass count → no centerline lane; the two passes sit at ±PassOffsetM
            // (GenerateFlightLines), straddling the centerline. Assert a lane on each
            // side rather than an exact span (the snake U-turn bulges the cross-track).
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) };

            var wps = CorridorPlanner.GenerateMission(line, Params(passes: 2, offset: 100), line[0]);

            var laneLats = wps.Where(w => w.IsLineWaypoint).Select(w => w.Lat).ToList();
            Assert.IsTrue(laneLats.Any(lat => lat > BaseLat + 0.0005),
                "expected a lane offset north of the centerline");
            Assert.IsTrue(laneLats.Any(lat => lat < BaseLat - 0.0005),
                "expected a lane offset south of the centerline");
        }

        [TestMethod]
        public void OffsetPolyline_PreservesCornerAngle_BothSides()
        {
            // A ~90° corner: east, then south. A true parallel offset must keep the corner
            // angle identical on both lanes — otherwise the cut↔Dubins decision can differ
            // between the outbound and return passes.
            var line = new List<PointLatLngAlt>
            {
                P(BaseLat, BaseLng),
                P(BaseLat, BaseLng + 0.02),
                P(BaseLat - 0.02, BaseLng + 0.02),
            };
            double orig = HeadingChangeAt(line);

            foreach (double off in new[] { 120.0, -120.0 })
            {
                var lane = CorridorPlanner.OffsetPolyline(line, off);
                Assert.AreEqual(orig, HeadingChangeAt(lane), 0.5,
                    $"offset {off} m must preserve the corner's heading change");
            }
        }

        static double HeadingChangeAt(List<PointLatLngAlt> p)
        {
            double b1 = p[0].GetBearing(p[1]);
            double b2 = p[1].GetBearing(p[2]);
            return CorridorPlanner.NormalizeHeadingChange(b2 - b1);
        }

        // ── Altitude ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void FlatTerrain_EveryWaypointAtDefaultAgl()
        {
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            Assert.IsTrue(wps.All(w => Math.Abs(w.AltRelM - 80) < 0.5),
                "flat terrain → every waypoint flies at DefaultAGL");
        }

        [TestMethod]
        public void AltitudeFollowsTerrainAlongTrack()
        {
            // Terrain rises with easting (lng). Home is the west end (terrain 0).
            CorridorPlanner.TerrainProvider = (lat, lng) => (lng - BaseLng) * MetresPerDegLng;
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.01) };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            double endTerr = 0.01 * MetresPerDegLng;
            Assert.AreEqual(80, wps.First().AltRelM, 1.0);
            Assert.AreEqual(endTerr + 80, wps.Last().AltRelM, 2.0);
        }

        [TestMethod]
        public void MultiplePasses_AllLanesShareCenterlineAltitude()
        {
            // Terrain varies ONLY cross-track (with lat); the centerline lat is constant.
            // If altitude were sampled at each lane's offset position, the two lanes would
            // differ by ~PassOffsetM of terrain. Sampling on the centerline → identical.
            // This locks in the centerline-altitude behaviour.
            CorridorPlanner.TerrainProvider = (lat, lng) => (lat - BaseLat) * MetresPerDegLat;
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) };

            var wps = CorridorPlanner.GenerateMission(line, Params(passes: 2, offset: 100), line[0]);

            Assert.IsTrue(wps.Count > 2, "two passes produce more than one lane");
            Assert.IsTrue(wps.All(w => Math.Abs(w.AltRelM - 80) < 1.0),
                "every lane flies the centerline altitude (DefaultAGL on a flat centerline), " +
                "not its own offset terrain");
        }

        // ── Stats ───────────────────────────────────────────────────────────────────

        [TestMethod]
        public void CalculateStats_StraightLine_MatchesGeographicDistance()
        {
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) };
            var p = Params();
            var wps = CorridorPlanner.GenerateMission(line, p, line[0]);

            var (dist, time) = CorridorPlanner.CalculateStats(wps, p);

            double expected = line[0].GetDistance(line[1]);
            Assert.AreEqual(expected, dist, expected * 0.02);
            Assert.AreEqual(dist / p.SpeedMs, time, 1e-6);
        }

        // ── Tour path: single-lane-per-step pass model ────────────────────────────

        [TestMethod]
        public void TourStep_FliesSingleOffsetLane()
        {
            // A single Forward step → one offset lane (offset = PassOffsetM/2, one side).
            var poly = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, tour, Params(passes: 2, offset: 120), poly.Points[0]);

            var lineWps = wps.Where(w => w.IsLineWaypoint).ToList();
            Assert.AreEqual(2, lineWps.Count, "single lane over a 2-point line → 2 waypoints");
            double shiftM = Math.Abs(lineWps[0].Lat - BaseLat) * MetresPerDegLat;
            Assert.AreEqual(60, shiftM, 5, "lane offset = PassOffsetM/2 from the centerline");
            Assert.IsTrue(lineWps.All(w => Math.Sign(w.Lat - BaseLat) == Math.Sign(lineWps[0].Lat - BaseLat)),
                "all waypoints on the same side (one lane)");
        }

        [TestMethod]
        public void OutAndBackTour_FliesBothOffsetLanes()
        {
            // Flying a polyline Forward then Reverse → out and back land on opposite sides
            // (the two passes), at ±PassOffsetM/2.
            var poly = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, tour, Params(passes: 2, offset: 100), poly.Points[0]);

            var lats = wps.Where(w => w.IsLineWaypoint).Select(w => w.Lat).ToList();
            Assert.IsTrue(lats.Any(l => l > BaseLat + 0.0002), "a lane on one side of the centerline");
            Assert.IsTrue(lats.Any(l => l < BaseLat - 0.0002), "a lane on the other side");
        }

        [TestMethod]
        public void TourPath_MultiPolyline_SamplesTerrainPerPolyline()
        {
            // Terrain rises to the south (lower lat = higher). The main line runs east-west
            // at the home latitude (terrain ~0); a spur runs south, so its terrain climbs.
            // Each waypoint must sample ITS OWN polyline's terrain: if the spur sampled the
            // main line instead, it would stay ~DefaultAGL.
            CorridorPlanner.TerrainProvider = (lat, lng) => (BaseLat - lat) * MetresPerDegLat;

            var main = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02) },
            };
            var spur = new Polyline
            {
                Id = 0,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng + 0.02), P(BaseLat - 0.01, BaseLng + 0.02) },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = 0, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = 0, Direction = TraverseDir.Reverse },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { main, spur }, tour, Params(), main.Points[0]);

            var mainWps = wps.Where(w => !w.IsBranchVertex).ToList();
            var spurWps = wps.Where(w => w.IsBranchVertex).ToList();

            Assert.IsTrue(mainWps.Count > 0 && spurWps.Count > 0, "both polylines contribute waypoints");
            Assert.IsTrue(mainWps.All(w => Math.Abs(w.AltRelM - 80) < 5),
                "main line sits at ~DefaultAGL over its flat home-latitude terrain");
            Assert.IsTrue(spurWps.Max(w => w.AltRelM) > 300,
                $"spur follows its own rising terrain (peak {spurWps.Max(w => w.AltRelM):F0} m); " +
                "would stay ~80 if it sampled the main line");
        }

        // ── Identity (VertexId) ───────────────────────────────────────────────────

        [TestMethod]
        public void VertexId_IsCollisionFreeAcrossPolylines()
        {
            Assert.AreEqual(new VertexId(VertexId.MainLine, 2), new VertexId(VertexId.MainLine, 2));
            Assert.AreNotEqual(new VertexId(VertexId.MainLine, 2), new VertexId(0, 2));

            var set = new HashSet<VertexId>
            {
                new VertexId(VertexId.MainLine, 2),
                new VertexId(0, 2),
                new VertexId(1, 2),
            };
            Assert.AreEqual(3, set.Count, "same index on different polylines must not collide");
        }

        [TestMethod]
        public void CorridorWaypoint_Vertex_DerivesFromLegacyFields()
        {
            var main = new CorridorWaypoint { CorridorVertexIndex = 5, IsBranchVertex = false };
            Assert.AreEqual(new VertexId(VertexId.MainLine, 5), main.Vertex);

            var branch = new CorridorWaypoint { CorridorVertexIndex = 5, IsBranchVertex = true, BranchId = 2 };
            Assert.AreEqual(new VertexId(2, 5), branch.Vertex);
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void NormalizeHeadingChange_WrapsToPlusMinus180()
        {
            Assert.AreEqual(0, CorridorPlanner.NormalizeHeadingChange(360), 1e-9);
            Assert.AreEqual(-179, CorridorPlanner.NormalizeHeadingChange(181), 1e-9);
            Assert.AreEqual(-90, CorridorPlanner.NormalizeHeadingChange(270), 1e-9);
            Assert.AreEqual(10, CorridorPlanner.NormalizeHeadingChange(10), 1e-9);
        }

        [TestMethod]
        public void WrapBearing_WrapsTo0To360()
        {
            Assert.AreEqual(0, CorridorPlanner.WrapBearing(360), 1e-9);
            Assert.AreEqual(350, CorridorPlanner.WrapBearing(-10), 1e-9);
            Assert.AreEqual(180, CorridorPlanner.WrapBearing(180), 1e-9);
        }

        [TestMethod]
        public void Clamp_BoundsValue()
        {
            Assert.AreEqual(5, CorridorPlanner.Clamp(5, 0, 10), 1e-9);
            Assert.AreEqual(0, CorridorPlanner.Clamp(-1, 0, 10), 1e-9);
            Assert.AreEqual(10, CorridorPlanner.Clamp(99, 0, 10), 1e-9);
        }
    }
}
