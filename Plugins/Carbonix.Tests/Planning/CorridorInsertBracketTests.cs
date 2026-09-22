using System.Collections.Generic;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;
using Carbonix.UI;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// Bracketing a profile click onto a corridor segment when loiter-to-alt lead-ins/spirals
    /// and corner-cut chords sit on the leg, and gradient classification across a spiral.
    /// Samples are synthetic: one edge (id 0) running east, vertices 1 km apart.
    /// </summary>
    [TestClass]
    public class CorridorInsertBracketTests
    {
        const double SpiralArcM = 1885;   // full turn at 300 m radius

        static ElevationPoint Vertex(int index, double distM, double alt = 400) => new ElevationPoint
        {
            IsLineWaypoint = true, IsBranchVertex = true, BranchId = 0, WaypointIndex = index,
            DistM = distM, AltRelM = alt, Lng = distM / 111319.5,
        };

        static ElevationPoint LeadIn(int id, double distM, double alt = 400) => new ElevationPoint
        {
            IsLineWaypoint = true, IsLoiterToAltLeadIn = true, IsBranchVertex = true, BranchId = 0,
            WaypointIndex = id, DistM = distM, AltRelM = alt,
        };

        static ElevationPoint Spiral(int loiterId, double distM, double start = 400, double target = 500) => new ElevationPoint
        {
            IsLoiterWaypoint = true, IsLoiterToAlt = true, IsBranchVertex = true, BranchId = 0,
            WaypointIndex = loiterId, DistM = distM, LoiterArcLengthM = SpiralArcM,
            LtaStartAltRelM = start, AltRelM = target,
        };

        static ElevationPoint ArcSample(int loiterId, double distM, double alt) => new ElevationPoint
        {
            IsLoiterArcSample = true, IsLoiterToAlt = true, IsBranchVertex = true, BranchId = 0,
            WaypointIndex = loiterId, DistM = distM, AltRelM = alt,
        };

        static ElevationPoint Loiter(int index, double distM, double arcM, double alt = 400) => new ElevationPoint
        {
            IsLoiterWaypoint = true, IsBranchVertex = true, BranchId = 0, WaypointIndex = index,
            DistM = distM, LoiterArcLengthM = arcM, AltRelM = alt,
        };

        static List<Carbonix.Planning.Polyline> Edge(int nVertices)
        {
            var pts = new List<PointLatLngAlt>();
            for (int i = 0; i < nVertices; i++) pts.Add(new PointLatLngAlt(0, i * 1000 / 111319.5, 0));
            return new List<Carbonix.Planning.Polyline> { new Carbonix.Planning.Polyline { Id = 0, Points = pts } };
        }

        // V0 ── leadIn ── spiral ────────────── V1     (spiral inserted 300 m along a 1 km segment)
        static List<ElevationPoint> LegWithSpiral() => new List<ElevationPoint>
        {
            Vertex(0, 0),
            LeadIn(100001, 300),
            Spiral(100002, 300),
            Vertex(1, 1000 + SpiralArcM),
        };

        [TestMethod]
        public void Bracket_BeforeLeadIn_MapsOntoSegment()
        {
            Assert.IsTrue(CorridorPlanForm.TryBracketLeg(LegWithSpiral(), Edge(2), 150,
                out int edge, out int seg, out double t, out var a, out var b, out _));
            Assert.AreEqual(0, edge);
            Assert.AreEqual(0, seg);
            Assert.AreEqual(0.15, t, 1e-9);
            Assert.AreEqual(0, a.WaypointIndex);
            Assert.AreEqual(1, b.WaypointIndex);
        }

        [TestMethod]
        public void Bracket_AfterSpiral_DiscountsArcLength()
        {
            // 2500 m on the axis = 300 m to the spiral + 1885 m of arc + 315 m on along the line.
            Assert.IsTrue(CorridorPlanForm.TryBracketLeg(LegWithSpiral(), Edge(2), 2500,
                out _, out int seg, out double t, out _, out _, out _));
            Assert.AreEqual(0, seg);
            Assert.AreEqual(0.615, t, 1e-9);
        }

        [TestMethod]
        public void Bracket_InsideSpiral_Refused()
        {
            Assert.IsFalse(CorridorPlanForm.TryBracketLeg(LegWithSpiral(), Edge(2), 1000,
                out _, out _, out _, out _, out _, out _));
        }

        [TestMethod]
        public void Bracket_BetweenCornerCutExitAndLeadIn()
        {
            // V0 ── cut entry(1) ── cut exit(1) ── leadIn ── spiral ── V2: the cut's exit and the
            // next vertex bracket the click; the lead-in in between is not a vertex.
            var samples = new List<ElevationPoint>
            {
                Vertex(0, 0),
                Vertex(1, 900), Vertex(1, 1100),
                LeadIn(100001, 1400),
                Spiral(100002, 1400),
                Vertex(2, 2000 + SpiralArcM),
            };
            samples[1].IsCornerCutEndpoint = samples[2].IsCornerCutEndpoint = true;

            Assert.IsTrue(CorridorPlanForm.TryBracketLeg(samples, Edge(3), 1250,
                out _, out int seg, out double t, out var a, out var b, out _));
            Assert.AreEqual(1, seg);
            Assert.AreEqual(150.0 / 900.0, t, 1e-9);
            Assert.AreEqual(1100, a.DistM);
            Assert.AreEqual(2, b.WaypointIndex);
        }

        [TestMethod]
        public void Bracket_OnCornerCutChord_Refused()
        {
            var samples = new List<ElevationPoint> { Vertex(0, 0), Vertex(1, 900), Vertex(1, 1100), Vertex(2, 2000) };
            Assert.IsFalse(CorridorPlanForm.TryBracketLeg(samples, Edge(3), 1000,
                out _, out _, out _, out _, out _, out _));
        }

        [TestMethod]
        public void Bracket_LoiterAnchor_LegStartsAtExit()
        {
            var samples = new List<ElevationPoint> { Loiter(0, 0, 500), Vertex(1, 1500) };
            Assert.IsTrue(CorridorPlanForm.TryBracketLeg(samples, Edge(2), 1000,
                out _, out _, out double t, out _, out _, out _));
            Assert.AreEqual(0.5, t, 1e-9);

            Assert.IsFalse(CorridorPlanForm.TryBracketLeg(samples, Edge(2), 200,
                out _, out _, out _, out _, out _, out _));
        }

        [TestMethod]
        public void Bracket_ReverseIndexOrder_FlipsT()
        {
            var samples = new List<ElevationPoint> { Vertex(1, 0), Vertex(0, 1000) };
            Assert.IsTrue(CorridorPlanForm.TryBracketLeg(samples, Edge(2), 250,
                out _, out int seg, out double t, out _, out _, out _));
            Assert.AreEqual(0, seg);
            Assert.AreEqual(0.75, t, 1e-9);
        }

        [TestMethod]
        public void Gradient_LegOutOfSpiral_IsClassified()
        {
            var ctl = new ElevationProfileControl { GradYellowPct = 4, GradRedPct = 5 };
            var lastArc = ArcSample(100002, 2185, 500);
            var next = Vertex(1, 2285, 490);   // 10 m over 100 m = 10 %

            var (col, _, warn) = ctl.ClassifySegment(lastArc, next);
            Assert.IsTrue(warn);
            Assert.AreEqual(Color.Red, col);
        }

        [TestMethod]
        public void Gradient_InsideSpiral_NotFlagged()
        {
            var ctl = new ElevationProfileControl { GradYellowPct = 4, GradRedPct = 5 };
            var leadIn = LeadIn(100001, 300, 400);
            var anchor = Spiral(100002, 300, 400, 500);
            var arc1 = ArcSample(100002, 325, 401);

            Assert.IsFalse(ctl.ClassifySegment(leadIn, anchor).warn);
            Assert.IsFalse(ctl.ClassifySegment(anchor, arc1).warn);
        }

        [TestMethod]
        public void Gradient_LegIntoLeadIn_IsClassified()
        {
            var ctl = new ElevationProfileControl { GradYellowPct = 4, GradRedPct = 5 };
            Assert.IsTrue(ctl.ClassifySegment(Vertex(0, 0, 400), LeadIn(100001, 300, 415)).warn);
        }
    }
}
