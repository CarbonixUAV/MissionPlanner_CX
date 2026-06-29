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
        public void MediumCorner_CutAsStraightChord_NoLoiter()
        {
            // ~45° turn — within the corner-cut band (15°..60°). It used to make an inscribed
            // loiter; now it's cut with a straight chord (entry + exit waypoints), no loiter.
            var line = new List<PointLatLngAlt>
            {
                P(BaseLat, BaseLng),
                P(BaseLat, BaseLng + 0.02),
                P(BaseLat - 0.02, BaseLng + 0.044),
            };

            var wps = CorridorPlanner.GenerateMission(line, Params(), line[0]);

            Assert.IsFalse(wps.Any(w => w.Command == MAVLink.MAV_CMD.LOITER_TURNS),
                "a corner-cut turn should be a straight chord, not a loiter");
            Assert.IsTrue(wps.Count(w => w.IsLineWaypoint) >= 4,
                "the cut replaces the bare vertex with entry + exit cut waypoints");
        }

        [TestMethod]
        public void Checkpoint_SplicedIntoBothPasses()
        {
            // 3-vertex straight line flown out + back (2 passes). A checkpoint on segment 0 at
            // t=0.5 should splice into both passes as a plain waypoint near the segment midpoint.
            var poly = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt>
                {
                    P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02), P(BaseLat, BaseLng + 0.04),
                },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
            };
            var cps = new List<Checkpoint>
            {
                new Checkpoint { PolylineId = VertexId.MainLine, SegmentIndex = 0, T = 0.5, Id = 100000 },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, tour, Params(passes: 2, offset: 100), poly.Points[0], cps);

            var cpWps = wps.Where(w => w.CorridorVertexIndex == 100000).ToList();
            Assert.AreEqual(2, cpWps.Count, "checkpoint spliced into both passes");
            Assert.IsTrue(cpWps.All(w => w.Command == MAVLink.MAV_CMD.WAYPOINT),
                "a mid-segment checkpoint is a plain waypoint, not a loiter");
            Assert.IsTrue(cpWps.All(w => Math.Abs(w.Lng - (BaseLng + 0.01)) < 1e-4),
                "checkpoint sits at the segment midpoint, offset only across-track (lat)");
        }

        [TestMethod]
        public void Checkpoint_OnSharpTurnApproachLeg_KeepsLoiter()
        {
            // A ~90° corner at vertex 1 renders as a loiter. A checkpoint on the APPROACH
            // segment (segment 0) must splice in as a plain waypoint without disturbing the
            // turn — this is what lets the UI insert on legs leading into / out of a loiter.
            var poly = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt>
                {
                    P(BaseLat, BaseLng),
                    P(BaseLat, BaseLng + 0.02),
                    P(BaseLat - 0.02, BaseLng + 0.02),   // ~90° turn — above full-orbit threshold
                },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
            };
            var cps = new List<Checkpoint>
            {
                new Checkpoint { PolylineId = VertexId.MainLine, SegmentIndex = 0, T = 0.5, Id = 100000 },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, tour, Params(passes: 2, offset: 100), poly.Points[0], cps);

            Assert.IsTrue(wps.Any(w => w.Command == MAVLink.MAV_CMD.LOITER_TURNS),
                "the sharp corner still loiters with a checkpoint on its approach leg");

            var cpWps = wps.Where(w => w.CorridorVertexIndex == 100000).ToList();
            Assert.AreEqual(2, cpWps.Count, "approach-leg checkpoint spliced into both passes");
            Assert.IsTrue(cpWps.All(w => w.Command == MAVLink.MAV_CMD.WAYPOINT),
                "an approach-leg checkpoint is a plain waypoint, not a loiter");
            Assert.IsTrue(cpWps.All(w => Math.Abs(w.Lng - (BaseLng + 0.01)) < 1e-3),
                "checkpoint sits mid-approach (segment 0), offset only across-track");
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
        public void LoiterLeadIns_InheritLoiterAltitude()
        {
            // Terrain rises to the south. The main line runs east (flat, terrain ~0); a spur
            // drops south (terrain climbs). The junction is a sharp turn → loiter whose exit is
            // over the rising spur terrain. Its lead-in helpers must inherit the loiter
            // altitude — not their own (here flat / main-line-projected) terrain, which made
            // them dive well below the loiter.
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

            int helpers = 0;
            for (int i = 0; i < wps.Count; i++)
            {
                if (!wps[i].IsTurnHelper) continue;
                helpers++;
                var loiter = wps.Skip(i + 1).First(w => w.Command == MAVLink.MAV_CMD.LOITER_TURNS);
                Assert.AreEqual(loiter.AltRelM, wps[i].AltRelM, 1e-6,
                    "a lead-in helper flies at its loiter's altitude, not its own terrain");
            }
            Assert.IsTrue(helpers >= 1, "the junction is a sharp turn with lead-in helpers");
        }

        [TestMethod]
        public void Altitudes_AreAbsoluteAmsl_IndependentOfHomeTerrain()
        {
            // Terrain rises with easting. Put HOME at the HIGH (east) end — altitudes must still
            // be terrain + AGL in absolute AMSL, NOT shifted down by the home terrain. (Pre-MSL
            // this returned terr - homeTerrain + AGL, so the west end would have read 80-endTerr.)
            CorridorPlanner.TerrainProvider = (lat, lng) => (lng - BaseLng) * MetresPerDegLng;
            var line = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.01) };
            var home = P(BaseLat, BaseLng + 0.01);   // home over high terrain (terrain != 0)

            var wps = CorridorPlanner.GenerateMission(line, Params(), home);

            double endTerr = 0.01 * MetresPerDegLng;
            Assert.AreEqual(80, wps.Min(w => w.AltRelM), 2.0,
                "west end is terrain(0) + AGL in AMSL, not offset by the home terrain");
            Assert.AreEqual(endTerr + 80, wps.Max(w => w.AltRelM), 2.0,
                "east end is terrain + AGL in absolute AMSL");
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
            Assert.IsTrue(mainWps.Where(w => !w.IsTurnHelper).All(w => Math.Abs(w.AltRelM - 80) < 5),
                "main line waypoints sit at ~DefaultAGL over flat home-latitude terrain " +
                "(turn lead-ins are excluded — they inherit their loiter's altitude)");
            Assert.IsTrue(spurWps.Max(w => w.AltRelM) > 300,
                $"spur follows its own rising terrain (peak {spurWps.Max(w => w.AltRelM):F0} m); " +
                "would stay ~80 if it sampled the main line");
        }

        [TestMethod]
        public void Profile_StubThenStraightLeg_LegIsNotSkipped()
        {
            // A dead-end stub (flown out + back) followed by a long, perfectly straight 2-point
            // leg. The junction between the stub's return and the leg is de-duplicated to the
            // stub's back-pass identity, which fails the first-pass Keep test — so the leg's one
            // and only segment was being dropped from the profile. It must be sampled.
            var main = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.05) },
            };
            var stub = new Polyline
            {
                Id = 0,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat + 0.004, BaseLng) },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = 0, Direction = TraverseDir.Forward },                 // out the stub
                new TourStep { PolylineId = 0, Direction = TraverseDir.Reverse },                 // back to junction
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward }, // the long leg
            };

            var mission = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { main, stub }, tour, Params(), main.Points[0]);

            var profile = Carbonix.CorridorPlanForm.BuildTourProfile(mission, main.Points[0], 0);

            // The long leg runs east from the junction (lng BaseLng) to BaseLng+0.05; the stub is
            // vertical at lng ~ BaseLng, so any leg-terrain sample east of +0.025 can only be the
            // long leg. Without it, the leg's terrain is missing entirely.
            Assert.IsTrue(profile.Any(s => s.IsLegTerrainSample && s.Lng > BaseLng + 0.025),
                "the straight leg after the stub is sampled, not skipped");
        }

        [TestMethod]
        public void Profile_StubReentryLoiter_IsShown()
        {
            // After a stub, the sharp turn that re-enters the main line is on the stub's hidden
            // back-pass, but it's a real maneuver flown differently from the entry turn — it must
            // still appear on the profile. Stub runs north; main runs east, so the return (south)
            // → main (east) is a ~90 deg sharp turn → loiter at the junction.
            var main = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.05) },
            };
            var stub = new Polyline
            {
                Id = 0,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat + 0.02, BaseLng) },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = 0, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = 0, Direction = TraverseDir.Reverse },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
            };

            var mission = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { main, stub }, tour, Params(), main.Points[0]);

            var profile = Carbonix.CorridorPlanForm.BuildTourProfile(mission, main.Points[0], 0);

            // The re-entry loiter sits near the junction (lat ~ BaseLat); the dead-end U-turn
            // loiter is far up the stub (lat ~ BaseLat+0.02). A loiter near the junction = re-entry.
            Assert.IsTrue(profile.Any(s => s.IsLoiterWaypoint && s.Lat < BaseLat + 0.01),
                "the sharp turn re-entering the main line after the stub is shown");
        }

        [TestMethod]
        public void JunctionLeg_MapsToDestinationPolylineSegment()
        {
            // The straight out of a stub's re-entry loiter: anchor A is the loiter at the
            // junction (carrying the STUB's identity), anchor B is the main-line vertex it leads
            // to. The checkpoint must land on the MAIN line, segment 0, at the click fraction —
            // not be rejected for the two anchors being on different polylines.
            var main = new Carbonix.Planning.Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.05) },
            };
            var stub = new Carbonix.Planning.Polyline
            {
                Id = 0,
                Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat + 0.02, BaseLng) },
            };

            var loiter = new ElevationPoint
            {
                IsLoiterWaypoint = true, Lat = BaseLat, Lng = BaseLng,
                WaypointIndex = 0, IsBranchVertex = true, BranchId = 0,   // stub identity
            };
            var lineF = new ElevationPoint
            {
                IsLineWaypoint = true, Lat = BaseLat, Lng = BaseLng + 0.05,
                WaypointIndex = 1, IsBranchVertex = false, BranchId = -1,  // main vtx 1
            };

            bool ok = Carbonix.CorridorPlanForm.TryJunctionLeg(
                new List<Carbonix.Planning.Polyline> { main, stub }, loiter, lineF, 0.25,
                out int edge, out int seg, out double t);

            Assert.IsTrue(ok, "a loiter to other-polyline leg is recognised as a junction leg");
            Assert.AreEqual(VertexId.MainLine, edge, "checkpoint lands on the main line, not the stub");
            Assert.AreEqual(0, seg, "on the main's first segment (junction -> F)");
            Assert.AreEqual(0.25, t, 1e-6, "fraction from the junction toward F");

            // Two line waypoints on different polylines is NOT a junction loiter leg.
            Assert.IsFalse(Carbonix.CorridorPlanForm.TryJunctionLeg(
                new List<Carbonix.Planning.Polyline> { main, stub }, lineF,
                new ElevationPoint { IsLineWaypoint = true, IsBranchVertex = true, BranchId = 0, WaypointIndex = 1 },
                0.5, out _, out _, out _));
        }

        [TestMethod]
        public void LoiterToAlt_EmitsLeadInPlusOffsetSpiral_BothPasses()
        {
            // A 3-vertex straight line flown out + back. An LTA on segment 0 should produce, in
            // each pass, a plain lead-in waypoint on the line plus a LOITER_TO_ALT one radius to
            // the side. Forward (toward the far vertex) targets the LTA altitude; reverse targets
            // the lead-in altitude — climb one way, descend the other.
            var poly = new Polyline
            {
                Id = VertexId.MainLine,
                Points = new List<PointLatLngAlt>
                {
                    P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02), P(BaseLat, BaseLng + 0.04),
                },
            };
            var tour = new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
            };
            var ltas = new List<LoiterToAlt>
            {
                new LoiterToAlt { PolylineId = VertexId.MainLine, SegmentIndex = 0, T = 0.5,
                                  Id = 100000, LoiterId = 200000, Side = 1, LtaAltRelM = 999 },
            };

            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, tour, Params(passes: 2, offset: 100), poly.Points[0], null, ltas);

            var leadIns = wps.Where(w => w.CorridorVertexIndex == 100000).ToList();
            var spirals = wps.Where(w => w.Command == MAVLink.MAV_CMD.LOITER_TO_ALT).ToList();
            Assert.AreEqual(2, leadIns.Count, "lead-in waypoint in both passes");
            Assert.AreEqual(2, spirals.Count, "one spiral per pass");
            Assert.IsTrue(spirals.All(w => w.CorridorVertexIndex == 200000 && w.P4 == 1),
                "spirals carry the LTA identity and exit tangent");
            Assert.IsTrue(spirals.Any(w => Math.Abs(w.AltRelM - 999) < 1e-6),
                "forward spiral targets the LTA altitude");
            Assert.IsTrue(spirals.Any(w => Math.Abs(w.AltRelM - leadIns[0].AltRelM) < 1e-6),
                "reverse spiral targets the lead-in altitude");

            // The segment runs east, so a side offset shifts the spiral ~one radius in latitude.
            foreach (var sp in spirals)
            {
                var lead = leadIns.First(l => l.LineIndex == sp.LineIndex);
                Assert.AreEqual(300.0 / MetresPerDegLat, Math.Abs(sp.Lat - lead.Lat), 5e-4,
                    "spiral sits about one turn radius to the side of its lead-in");
            }
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
