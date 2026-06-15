using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.Mission;

namespace Carbonix.Tests.Mission
{
    [TestClass]
    public class MissionSegmentizerTests
    {
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

        // Bookmarks (DO_LAND_START / DO_RETURN_PATH_START) carry no location.
        static Locationwp Bookmark(ushort id)
        {
            return new Locationwp { id = id };
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

        static List<MissionSegmentizer.Segment> BuildSegs(List<Locationwp> items)
        {
            var graph = MissionGraph.Create(PointLatLngAlt.Zero, items);
            return MissionSegmentizer.BuildSegments(graph, VehicleClass.Plane, 0);
        }

        // Flags of the first segment between two nodes, by 0-based mission index.
        static SegmentFlags FlagsOf(List<MissionSegmentizer.Segment> segs, int fromMissionIndex, int toMissionIndex)
        {
            foreach (var s in segs)
            {
                if (s.StartNode != null && s.EndNode != null &&
                    s.StartNode.MissionIndex == fromMissionIndex && s.EndNode.MissionIndex == toMissionIndex)
                {
                    return s.Flags;
                }
            }
            Assert.Fail($"no segment found for {fromMissionIndex} -> {toMissionIndex}");
            return SegmentFlags.None;
        }

        [TestMethod]
        public void LinearReturnThenLand_PathsDoNotOverlap()
        {
            // A, [DO_RETURN_PATH_START]->B, C, [DO_LAND_START]->D, LAND.
            // The return path owns the approach up to the landing start; the
            // landing sequence owns from the landing start onward.
            var items = new List<Locationwp>
            {
                Wp(0.0),                                                       // 0: A
                Bookmark((ushort)MAVLink.MAV_CMD.DO_RETURN_PATH_START),        // 1: -> B
                Wp(0.1),                                                       // 2: B
                Wp(0.2),                                                       // 3: C
                Bookmark((ushort)MAVLink.MAV_CMD.DO_LAND_START),              // 4: -> D
                Wp(0.3),                                                       // 5: D
                Wp(0.4, (ushort)MAVLink.MAV_CMD.VTOL_LAND),                    // 6: LAND
            };

            var segs = BuildSegs(items);

            // Approach into the return path: neither flag.
            Assert.IsFalse(FlagsOf(segs, 0, 2).HasFlag(SegmentFlags.ReturnPath));
            Assert.IsFalse(FlagsOf(segs, 0, 2).HasFlag(SegmentFlags.LandSequence));

            // Return path covers B->C and the edge into the landing start (C->D).
            Assert.IsTrue(FlagsOf(segs, 2, 3).HasFlag(SegmentFlags.ReturnPath));
            Assert.IsFalse(FlagsOf(segs, 2, 3).HasFlag(SegmentFlags.LandSequence));
            Assert.IsTrue(FlagsOf(segs, 3, 5).HasFlag(SegmentFlags.ReturnPath));
            Assert.IsFalse(FlagsOf(segs, 3, 5).HasFlag(SegmentFlags.LandSequence));

            // Landing sequence owns from the landing start onward.
            Assert.IsTrue(FlagsOf(segs, 5, 6).HasFlag(SegmentFlags.LandSequence));
            Assert.IsFalse(FlagsOf(segs, 5, 6).HasFlag(SegmentFlags.ReturnPath));
        }

        [TestMethod]
        public void GoAroundJump_LandSequenceDoesNotBleedIntoReturnPath()
        {
            // A go-around: from the landing start D, a DO_JUMP loops back to the
            // return path start B. Without bounding the landing traversal, it would
            // follow the jump backward and mark B->C / C->D as landing edges,
            // drawing the DLS path over the return path. The traversal must stop
            // where the return path begins.
            var items = new List<Locationwp>
            {
                Wp(0.0),                                                       // 0: A
                Bookmark((ushort)MAVLink.MAV_CMD.DO_RETURN_PATH_START),        // 1: -> B
                Wp(0.1),                                                       // 2: B
                Wp(0.2),                                                       // 3: C
                Bookmark((ushort)MAVLink.MAV_CMD.DO_LAND_START),              // 4: -> D
                Wp(0.3),                                                       // 5: D
                Jump(3, 1),                                                    // 6: go-around -> item 3 (B)
                Wp(0.4, (ushort)MAVLink.MAV_CMD.VTOL_LAND),                    // 7: LAND
            };

            var segs = BuildSegs(items);

            // The return-path legs keep their flag and are NOT claimed by landing.
            Assert.IsTrue(FlagsOf(segs, 2, 3).HasFlag(SegmentFlags.ReturnPath));
            Assert.IsFalse(FlagsOf(segs, 2, 3).HasFlag(SegmentFlags.LandSequence));
            Assert.IsTrue(FlagsOf(segs, 3, 5).HasFlag(SegmentFlags.ReturnPath));
            Assert.IsFalse(FlagsOf(segs, 3, 5).HasFlag(SegmentFlags.LandSequence));

            // The actual landing leg is still flagged as the landing sequence.
            Assert.IsTrue(FlagsOf(segs, 5, 7).HasFlag(SegmentFlags.LandSequence));
        }
    }
}
