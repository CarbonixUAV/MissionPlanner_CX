using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// A loiter-to-alt's spiral sits on a fixed geographic side of its segment. Adding another
    /// LTA, or flying the tour reversed, must not move an existing spiral to the other side.
    /// </summary>
    [TestClass]
    public class CorridorLtaSideTests
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
            Points = new List<PointLatLngAlt>
            {
                P(BaseLat, BaseLng), P(BaseLat, BaseLng + 0.02), P(BaseLat, BaseLng + 0.04),
            },
        };

        static List<TourStep> Tour(bool reversed) => reversed
            ? new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
            }
            : new List<TourStep>
            {
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward },
                new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Reverse },
            };

        static LoiterToAlt Lta(int seg, int id, int side = 1) => new LoiterToAlt
        {
            PolylineId = VertexId.MainLine, SegmentIndex = seg, T = 0.5,
            Id = id, LoiterId = id + 1, Side = side, LtaAltRelM = 500, LeadInAltRelM = 400,
        };

        static CorridorParameters Params() => new CorridorParameters
        {
            MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 100,
        };

        // Side of the line the spirals with this identity sit on: +1 north, -1 south.
        static List<int> SpiralSides(List<CorridorWaypoint> wps, int loiterId) =>
            wps.Where(w => w.Command == MAVLink.MAV_CMD.LOITER_TO_ALT && w.CorridorVertexIndex == loiterId)
               .Select(w => Math.Sign(w.Lat - BaseLat)).ToList();

        [TestMethod]
        public void ReversedTour_AddingSecondLta_KeepsFirstSpiralSide()
        {
            var poly = Line();
            var one = new List<LoiterToAlt> { Lta(0, 100000) };
            var two = new List<LoiterToAlt> { Lta(0, 100000), Lta(1, 100002) };

            var before = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, Tour(reversed: true), Params(), poly.Points[0], null, one);
            var after = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, Tour(reversed: true), Params(), poly.Points[0], null, two);

            var sidesBefore = SpiralSides(before, 100001);
            var sidesAfter = SpiralSides(after, 100001);
            Assert.AreEqual(2, sidesBefore.Count);
            Assert.AreEqual(2, sidesAfter.Count);
            CollectionAssert.AreEqual(sidesBefore, sidesAfter);
        }

        [TestMethod]
        public void ForwardAndReversedTour_SameSpiralSide()
        {
            var poly = Line();
            var ltas = new List<LoiterToAlt> { Lta(0, 100000) };

            var fwd = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, Tour(reversed: false), Params(), poly.Points[0], null, ltas);
            var rev = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, Tour(reversed: true), Params(), poly.Points[0], null, ltas);

            CollectionAssert.AreEqual(SpiralSides(fwd, 100001).Distinct().ToList(),
                                      SpiralSides(rev, 100001).Distinct().ToList());
        }

        [TestMethod]
        public void SpiralSide_BothPasses_SameGeographicSide()
        {
            var poly = Line();
            var ltas = new List<LoiterToAlt> { Lta(0, 100000, side: 1) };
            var wps = CorridorPlanner.GenerateMissionFromTour(
                new List<Polyline> { poly }, Tour(reversed: true), Params(), poly.Points[0], null, ltas);
            var sides = SpiralSides(wps, 100001);
            Assert.AreEqual(2, sides.Count);
            Assert.AreEqual(sides[0], sides[1]);
        }
    }
}
