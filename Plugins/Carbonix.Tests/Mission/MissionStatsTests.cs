using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.Mission;

namespace Carbonix.Tests.Mission
{
    [TestClass]
    public class MissionStatsTests
    {
        // Somewhere with non-zero lat/lng so HasLocation is satisfied.
        const double BaseLat = -33.0;
        const double BaseLng = 151.0;

        static Locationwp Wp(double latOffsetDeg, ushort id = (ushort)MAVLink.MAV_CMD.WAYPOINT)
        {
            return new Locationwp
            {
                id = id,
                lat = BaseLat + latOffsetDeg,
                lng = BaseLng,
                alt = 100,
                frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
            };
        }

        static Locationwp Jump(int target1Based, int repeat)
        {
            return new Locationwp
            {
                id = (ushort)MAVLink.MAV_CMD.DO_JUMP,
                p1 = target1Based,
                p2 = repeat,
            };
        }

        static Locationwp Loiter(double latOffsetDeg, ushort id, float p1 = 0, float radius = 0)
        {
            var wp = new Locationwp
            {
                id = id,
                lat = BaseLat + latOffsetDeg,
                lng = BaseLng,
                alt = 100,
                frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                p1 = p1,
            };
            // LOITER_TO_ALT carries its radius in param2; the others use param3.
            if (id == (ushort)MAVLink.MAV_CMD.LOITER_TO_ALT)
            {
                wp.p2 = radius;
            }
            else
            {
                wp.p3 = radius;
            }
            return wp;
        }

        static Locationwp WpAt(double latOffsetDeg, double lngOffsetDeg, ushort id = (ushort)MAVLink.MAV_CMD.WAYPOINT)
        {
            return new Locationwp
            {
                id = id,
                lat = BaseLat + latOffsetDeg,
                lng = BaseLng + lngOffsetDeg,
                alt = 100,
                frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
            };
        }

        static PointLatLngAlt Pos(double latOffsetDeg)
        {
            return new PointLatLngAlt(BaseLat + latOffsetDeg, BaseLng, 100);
        }

        static double Circumference(double radius)
        {
            return 2 * System.Math.PI * radius;
        }

        static List<MissionSegmentizer.Segment> BuildSegs(List<Locationwp> items, VehicleClass vehicleClass)
        {
            var graph = MissionGraph.Create(PointLatLngAlt.Zero, items);
            return MissionSegmentizer.BuildSegments(graph, vehicleClass, 0);
        }

        // Builds the graph and segments for a mission, then computes its stats.
        // The production path does this inside WPOverlay2; tests do it explicitly.
        static MissionStats ComputeStats(PointLatLngAlt home, List<Locationwp> items,
            VehicleClass vehicleClass = VehicleClass.Plane, double loiterRadius = 0)
        {
            var graph = MissionGraph.Create(home, items);
            var segments = MissionSegmentizer.BuildSegments(graph, vehicleClass, loiterRadius);
            return MissionStats.Compute(graph, segments, loiterRadius);
        }

        static double PathLen(List<PointLatLngAlt> path)
        {
            double d = 0;
            for (int i = 1; i < path.Count; i++)
            {
                d += path[i - 1].GetDistance(path[i]);
            }
            return d;
        }

        // Sum of the primary (drawn, non-alternate) transit segment lengths -- the
        // distance the renderer actually traces, excluding loiter arcs.
        static double PrimaryTransit(List<MissionSegmentizer.Segment> segs)
        {
            double d = 0;
            foreach (var s in segs)
            {
                if (!s.Flags.HasFlag(SegmentFlags.Alternate) && s.StartNode != null && s.EndNode != null)
                {
                    d += PathLen(s.Path);
                }
            }
            return d;
        }

        // Length of the primary transit segment between two nodes, by 0-based mission index.
        static double SegLen(List<MissionSegmentizer.Segment> segs, int fromMissionIndex, int toMissionIndex)
        {
            foreach (var s in segs)
            {
                if (!s.Flags.HasFlag(SegmentFlags.Alternate) && s.StartNode != null && s.EndNode != null &&
                    s.StartNode.MissionIndex == fromMissionIndex && s.EndNode.MissionIndex == toMissionIndex)
                {
                    return PathLen(s.Path);
                }
            }
            return 0;
        }

        // Length of the first drawn segment between two nodes (any flag), matching
        // MissionStats' endpoint-keyed, first-wins leg lookup. Used for jump legs,
        // which are drawn as alternates.
        static double LegLen(List<MissionSegmentizer.Segment> segs, int fromMissionIndex, int toMissionIndex)
        {
            foreach (var s in segs)
            {
                if (s.StartNode != null && s.EndNode != null &&
                    s.StartNode.MissionIndex == fromMissionIndex && s.EndNode.MissionIndex == toMissionIndex)
                {
                    return PathLen(s.Path);
                }
            }
            return 0;
        }

        // Length of the primary (non-alternate) entry->exit loiter arc at a node.
        static double LoiterArcLen(List<MissionSegmentizer.Segment> segs, int loiterMissionIndex)
        {
            foreach (var s in segs)
            {
                if (s.Kind == SegmentKind.LoiterArc && !s.Flags.HasFlag(SegmentFlags.Alternate) &&
                    s.StartNode != null && s.StartNode.MissionIndex == loiterMissionIndex)
                {
                    return PathLen(s.Path);
                }
            }
            return 0;
        }

        // Expected leg length straight from the same great-circle helper the
        // implementation uses, so tests stay exact regardless of Earth radius.
        static double Leg(double aOffset, double bOffset)
        {
            return Pos(aOffset).GetDistance(Pos(bOffset));
        }

        [TestMethod]
        public void LinearMission_NoHome_SumsConsecutiveLegs()
        {
            var items = new List<Locationwp> { Wp(0.0), Wp(0.1), Wp(0.3) };

            var stats = ComputeStats(PointLatLngAlt.Zero, items);

            Assert.AreEqual(Leg(0.0, 0.1) + Leg(0.1, 0.3), stats.TotalDistance, 1e-3);
            Assert.IsFalse(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void LinearMission_WithHome_IncludesHomeToFirstLeg()
        {
            var home = Pos(-0.1);
            var items = new List<Locationwp> { Wp(0.0), Wp(0.2) };

            var stats = ComputeStats(home, items);

            Assert.AreEqual(Leg(-0.1, 0.0) + Leg(0.0, 0.2), stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void Takeoff_ResolvesToHome_FirstLegIsHomeToNextNode()
        {
            var home = Pos(0.0);
            var items = new List<Locationwp>
            {
                Wp(0.0, (ushort)MAVLink.MAV_CMD.TAKEOFF), // lat/lng ignored, launches from home
                Wp(0.2),
            };

            var stats = ComputeStats(home, items);

            // home->takeoff is zero-length; takeoff->wp flies home->wp.
            Assert.AreEqual(Leg(0.0, 0.2), stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void FiniteJumpLoop_CountsBodyAndJumpLegByRepeat()
        {
            // wp1, wp2, wp3, DO_JUMP -> wp1 repeat 2
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Wp(0.1),
                Wp(0.3),
                Jump(1, 2),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items);

            // body flown 3x (1 + 2 repeats), jump leg wp3->wp1 flown 2x.
            double expected = 3 * Leg(0.0, 0.1) + 3 * Leg(0.1, 0.3) + 2 * Leg(0.3, 0.0);
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
            Assert.IsFalse(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void InfiniteJumpLoop_CountsSinglePass_AndFlagsInfinite()
        {
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Wp(0.1),
                Wp(0.3),
                Jump(1, -1), // jump forever
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items);

            Assert.AreEqual(Leg(0.0, 0.1) + Leg(0.1, 0.3), stats.TotalDistance, 1e-3);
            Assert.IsTrue(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void NestedJumpLoops_MultiplyOnInnerBody()
        {
            // wp1, wp2, wp3, inner DO_JUMP -> wp2 (repeat 1), wp4, outer DO_JUMP -> wp1 (repeat 1)
            // nodes: wp1(0) wp2(1) wp3(2) wp4(3)
            var items = new List<Locationwp>
            {
                Wp(0.0), // 1
                Wp(0.1), // 2
                Wp(0.3), // 3
                Jump(2, 1), // inner: back to wp2, body = wp2..wp3
                Wp(0.6), // 4 (mission item 4)
                Jump(1, 1), // outer: back to wp1, body = wp1..wp4
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items);

            // Inner loop body (wp2->wp3) sits inside the outer loop: flown
            // (1+1)*(1+1) = 4 times. wp1->wp2 and wp3->wp4 are only in the outer
            // loop: 2 times. Inner jump leg (wp3->wp2): 1 jump * (outer+1)=2 = 2.
            // Outer jump leg (wp4->wp1): 1 jump * 1 = 1.
            double expected =
                2 * Leg(0.0, 0.1) +   // wp1->wp2 (outer only)
                4 * Leg(0.1, 0.3) +   // wp2->wp3 (inner & outer)
                2 * Leg(0.3, 0.6) +   // wp3->wp4 (outer only)
                2 * Leg(0.3, 0.1) +   // inner jump wp3->wp2
                1 * Leg(0.6, 0.0);    // outer jump wp4->wp1
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void LoiterTurns_Plane_FullTurns_AddsDwell()
        {
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_TURNS, p1: 3, radius: 100),
                Wp(0.3),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            // 3 full circles plus the entry->exit transit arc.
            var segs = BuildSegs(items, VehicleClass.Plane);
            double expected = PrimaryTransit(segs) + 3 * Circumference(100) + LoiterArcLen(segs, 1);
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void LoiterTurns_Plane_SubOneTurn_UsesTransitArcOnly()
        {
            // A fractional-turn LOITER_TURNS is an arc-shaping waypoint: it flies
            // the transit arc but no full circle.
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_TURNS, p1: 0.5f, radius: 100),
                Wp(0.3),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            var segs = BuildSegs(items, VehicleClass.Plane);
            double arc = LoiterArcLen(segs, 1);
            Assert.IsTrue(arc > 0, "expected a drawn transit arc");
            Assert.AreEqual(PrimaryTransit(segs) + arc, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void LoiterTime_Plane_TreatedAsOneTurn()
        {
            // A LOITER_TIME's turn count depends on vehicle speed and wind, which
            // aren't known here, so it's counted as a single turn.
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_TIME, radius: 80),
                Wp(0.3),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            var segs = BuildSegs(items, VehicleClass.Plane);
            Assert.AreEqual(PrimaryTransit(segs) + Circumference(80) + LoiterArcLen(segs, 1), stats.TotalDistance, 1e-3);
            Assert.IsFalse(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void LoiterToAlt_Plane_TreatedAsOneTurn_RadiusFromParam2()
        {
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_TO_ALT, radius: 120),
                Wp(0.3),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            var segs = BuildSegs(items, VehicleClass.Plane);
            Assert.AreEqual(PrimaryTransit(segs) + Circumference(120) + LoiterArcLen(segs, 1), stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void LoiterUnlim_Plane_OneTurn_AndFlagsInfinite()
        {
            // LOITER_UNLIM is terminal: nothing after it is reached.
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_UNLIM, radius: 60),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            var segs = BuildSegs(items, VehicleClass.Plane);
            Assert.AreEqual(PrimaryTransit(segs) + Circumference(60), stats.TotalDistance, 1e-3);
            Assert.IsTrue(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void LoiterUnlim_Copter_NoDwell_ButStillFlagsInfinite()
        {
            // A hover loiter contributes no circling distance, but the mission
            // still never terminates.
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_UNLIM, radius: 60),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Copter);

            Assert.AreEqual(Leg(0.0, 0.1), stats.TotalDistance, 1e-3);
            Assert.IsTrue(stats.HasInfiniteLoop);
        }

        [TestMethod]
        public void LoiterInLoop_Plane_DwellCountedPerVisit()
        {
            // wp1, LOITER_TURNS x2 r=50, wp2, DO_JUMP -> wp1 repeat 1.
            var items = new List<Locationwp>
            {
                Wp(0.0),
                Loiter(0.1, (ushort)MAVLink.MAV_CMD.LOITER_TURNS, p1: 2, radius: 50),
                Wp(0.3),
                Jump(1, 1),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            // Loop body flown twice; the loiter is reached twice so both its
            // transit legs and its dwell count twice. Jump leg flown once.
            var segs = BuildSegs(items, VehicleClass.Plane);
            double dwellPerVisit = 2 * Circumference(50) + LoiterArcLen(segs, 1); // 2 turns + transit arc
            double expected =
                2 * SegLen(segs, 0, 1) +          // wp1 -> loiter
                2 * SegLen(segs, 1, 2) +          // loiter -> wp2
                1 * Leg(0.3, 0.0) +               // jump leg wp2 -> wp1 (straight)
                2 * dwellPerVisit;                // loiter reached twice
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void SplineWaypoint_UsesCurveLengthNotChord()
        {
            // An L-shaped path through a spline waypoint bulges, so the spline
            // leg must measure longer than the straight chord between the points.
            var items = new List<Locationwp>
            {
                WpAt(0.0, 0.0),
                WpAt(0.1, 0.0, (ushort)MAVLink.MAV_CMD.SPLINE_WAYPOINT),
                WpAt(0.1, 0.1),
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Copter);

            double straightBaseline =
                new PointLatLngAlt(BaseLat, BaseLng).GetDistance(new PointLatLngAlt(BaseLat + 0.1, BaseLng)) +
                new PointLatLngAlt(BaseLat + 0.1, BaseLng).GetDistance(new PointLatLngAlt(BaseLat + 0.1, BaseLng + 0.1));

            Assert.IsTrue(stats.TotalDistance > straightBaseline,
                $"expected spline distance {stats.TotalDistance} > chord baseline {straightBaseline}");
        }

        [TestMethod]
        public void JumpLegIntoSpline_UsesSplineLengthNotChord()
        {
            // wp1, spline wp2, wp3, DO_JUMP -> wp2. The jump leg wp3->wp2 lands on
            // a spline waypoint, so it is drawn (and measured) as a curve.
            var items = new List<Locationwp>
            {
                WpAt(0.0, 0.0),
                WpAt(0.1, 0.0, (ushort)MAVLink.MAV_CMD.SPLINE_WAYPOINT),
                WpAt(0.1, 0.1),
                Jump(2, 1), // back to the spline waypoint (mission item 2, 1-based)
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Copter);

            var graph = MissionGraph.Create(PointLatLngAlt.Zero, items);
            var segs = MissionSegmentizer.BuildSegments(graph, VehicleClass.Copter, 0);
            double jumpLeg = LegLen(segs, 2, 1); // wp3 -> spline wp2

            double jumpChord = new PointLatLngAlt(BaseLat + 0.1, BaseLng + 0.1)
                .GetDistance(new PointLatLngAlt(BaseLat + 0.1, BaseLng));
            Assert.IsTrue(jumpLeg > jumpChord, $"expected spline jump leg {jumpLeg} > chord {jumpChord}");

            // Loop body (wp2->wp3) flown twice; jump leg once.
            double expected = LegLen(segs, 0, 1) + 2 * LegLen(segs, 1, 2) + jumpLeg;
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void ForwardJump_UnreachableLegs_AreStillCounted()
        {
            // A, B, DO_JUMP -> D (forward, over C), C, D. At runtime this flies
            // A->B then jumps to D; C is never reached. The skipped B->C and C->D
            // legs are counted anyway -- a deliberate resolution: the operator
            // presumably means to reach C (e.g. a manual jump), same spirit as
            // resolving an infinite loop to a single pass.
            var items = new List<Locationwp>
            {
                Wp(0.0),    // 0: A
                Wp(0.1),    // 1: B
                Jump(5, 1), // 2: DO_JUMP -> item 5 (mission index 4 = D)
                Wp(0.2),    // 3: C (skipped at runtime)
                Wp(0.3),    // 4: D (jump target)
            };

            var stats = ComputeStats(PointLatLngAlt.Zero, items, VehicleClass.Plane);

            var graph = MissionGraph.Create(PointLatLngAlt.Zero, items);
            var segs = MissionSegmentizer.BuildSegments(graph, VehicleClass.Plane, 0);
            double expected =
                LegLen(segs, 0, 1) + // A->B  (reachable)
                LegLen(segs, 1, 3) + // B->C  (skipped, still counted)
                LegLen(segs, 3, 4) + // C->D  (skipped, still counted)
                LegLen(segs, 1, 4);  // B->D  forward jump (reachable)
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void NodesAfterTerminal_AreStillCounted()
        {
            // A, B, RTL, C, D. The mission ends at RTL; C and D are orphaned (the
            // RTL->C fall-through is dropped and nothing jumps in). Same deliberate
            // resolution as the forward jump: the orphaned C->D leg is counted, and
            // the orphan entry C is treated as reached from home (the operator
            // presumably jumps there), just like the mission's own start.
            var home = Pos(-0.1);
            var items = new List<Locationwp>
            {
                Wp(0.0), // 0: A
                Wp(0.1), // 1: B (last reachable)
                new Locationwp
                {
                    id = (ushort)MAVLink.MAV_CMD.RETURN_TO_LAUNCH,
                    frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                },       // 2: terminal
                Wp(0.2), // 3: C (orphaned)
                Wp(0.3), // 4: D (orphaned)
            };

            var stats = ComputeStats(home, items, VehicleClass.Plane);

            var graph = MissionGraph.Create(home, items);
            var segs = MissionSegmentizer.BuildSegments(graph, VehicleClass.Plane, 0);
            // Home->A starts the main path; Home->C starts the orphaned segment.
            // A->B and C->D are counted; B->RTL adds nothing (RTL has no location).
            double expected =
                home.GetDistance(Pos(0.0)) + LegLen(segs, 0, 1) + // Home->A, A->B
                home.GetDistance(Pos(0.2)) + LegLen(segs, 3, 4);  // Home->C, C->D
            Assert.AreEqual(expected, stats.TotalDistance, 1e-3);
        }

        [TestMethod]
        public void EmptyMission_IsZero()
        {
            var stats = ComputeStats(PointLatLngAlt.Zero, new List<Locationwp>());

            Assert.AreEqual(0.0, stats.TotalDistance, 1e-9);
            Assert.IsFalse(stats.HasInfiniteLoop);
        }
    }
}
