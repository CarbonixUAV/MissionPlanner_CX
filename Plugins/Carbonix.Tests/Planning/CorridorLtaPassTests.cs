using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// A loiter-to-alt climbs on the pass it was planned on — the edge's first traversal in the
    /// tour, which is what the profile shows — regardless of which way that runs along the
    /// polyline's own vertex order. The return pass descends.
    /// </summary>
    [TestClass]
    public class CorridorLtaPassTests
    {
        const double BaseLat = -33.0;
        const double BaseLng = 151.0;

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

        static Polyline Line() => new Polyline
        {
            Id = VertexId.MainLine,
            Points = new List<PointLatLngAlt> { P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02), P(BaseLat, BaseLng + 0.04) },
        };

        static TourStep Step(TraverseDir d, bool oneWay = false) =>
            new TourStep { PolylineId = VertexId.MainLine, Direction = d, OneWay = oneWay };

        static List<LoiterToAlt> Lta() => new List<LoiterToAlt>
        {
            new LoiterToAlt { PolylineId = VertexId.MainLine, SegmentIndex = 0, T = 0.5,
                              Id = 100000, LoiterId = 100001, Side = 1, LtaAltRelM = 900, LeadInAltRelM = 400 },
        };

        static CorridorParameters Params() => new CorridorParameters
        {
            MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 100,
        };

        // (lead-in alt, spiral target alt) per traversal, in mission order.
        static List<(double leadIn, double target)> Passes(List<CorridorWaypoint> wps)
        {
            var r = new List<(double, double)>();
            for (int i = 0; i + 1 < wps.Count; i++)
                if (wps[i].CorridorVertexIndex == 100000 && wps[i + 1].Command == MAVLink.MAV_CMD.LOITER_TO_ALT)
                    r.Add((wps[i].AltRelM, wps[i + 1].AltRelM));
            return r;
        }

        [TestMethod]
        public void ForwardFirst_ClimbsOnFirstPass_DescendsOnReturn()
        {
            var poly = Line();
            var tour = new List<TourStep> { Step(TraverseDir.Forward), Step(TraverseDir.Reverse) };
            var passes = Passes(CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, Params(), poly.Points[0], null, Lta()));

            Assert.AreEqual(2, passes.Count);
            Assert.AreEqual((400.0, 900.0), passes[0]);
            Assert.AreEqual((900.0, 400.0), passes[1]);
        }

        [TestMethod]
        public void ReverseFirst_StillClimbsOnFirstPass()
        {
            // Tour rooted at the polyline's far end: the first pass runs against the vertex order.
            var poly = Line();
            var tour = new List<TourStep> { Step(TraverseDir.Reverse), Step(TraverseDir.Forward) };
            var passes = Passes(CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, Params(), poly.Points[2], null, Lta()));

            Assert.AreEqual(2, passes.Count);
            Assert.AreEqual((400.0, 900.0), passes[0]);
            Assert.AreEqual((900.0, 400.0), passes[1]);
        }

        [TestMethod]
        public void OneWayReverse_ClimbsOnItsOnlyPass()
        {
            var poly = Line();
            var tour = new List<TourStep> { Step(TraverseDir.Reverse, oneWay: true) };
            var passes = Passes(CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, Params(), poly.Points[2], null, Lta()));

            Assert.AreEqual(1, passes.Count);
            Assert.AreEqual((400.0, 900.0), passes[0]);
        }

        [TestMethod]
        public void ReverseFirst_AddingAnotherLta_KeepsFirstPassClimb()
        {
            var poly = Line();
            var tour = new List<TourStep> { Step(TraverseDir.Reverse, oneWay: true) };
            var two = Lta();
            two.Add(new LoiterToAlt
            {
                PolylineId = VertexId.MainLine, SegmentIndex = 1, T = 0.5,
                Id = 100002, LoiterId = 100003, Side = -1, LtaAltRelM = 300, LeadInAltRelM = 700,
            });
            var passes = Passes(CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, tour, Params(), poly.Points[2], null, two));
            Assert.AreEqual((400.0, 900.0), passes[0]);
        }
    }
}
