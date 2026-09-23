using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    [TestClass]
    public class CorridorPlanFileTests
    {
        static CorridorPlanFile Sample()
        {
            var plan = new CorridorPlanFile
            {
                Home = new[] { -33.0, 151.0 },
                Params = new CorridorPlanFile.Parameters
                {
                    MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25,
                    PassOffsetM = 100, OneWay = true, OneWayEnd = new[] { -33.1, 151.2 },
                    TourStart = new[] { -33.0, 151.0 }, Reverse = true,
                    CornerCutThresholdDeg = 15, FullOrbitThresholdDeg = 60,
                    OverflyDistM = 100, TurnRadiusM = 300, CornerCutRadiusM = 150,
                    GradWarnPct = 4, GradMaxPct = 5,
                },
                LegOrder = new List<int> { 2, 0, 1 },
                NextCheckpointId = 100004,
            };
            plan.Features.Add(new CorridorPlanFile.FeatureFile
            {
                Name = "trunk.kml",
                Polylines = new List<List<double[]>>
                {
                    new List<double[]> { new[] { -33.0, 151.0, 0.0 }, new[] { -33.0, 151.01, 0.0 } },
                },
            });
            plan.Zones.Add(new CorridorPlanFile.Zone
            {
                Name = "island 300m", CeilingAglM = 300,
                Ring = new List<double[]> { new[] { -33.1, 150.9, 0.0 }, new[] { -33.1, 151.1, 0.0 }, new[] { -32.9, 151.0, 0.0 } },
            });
            plan.AltOverrides.Add(new CorridorPlanFile.VertexAlt { PolylineId = 0, Index = 1, AltM = 412.5 });
            plan.CornerCutAlts.Add(new CorridorPlanFile.VertexAlt { PolylineId = 1, Index = 2, AltM = 390 });
            plan.Checkpoints.Add(new Checkpoint { PolylineId = 0, SegmentIndex = 0, T = 0.25, Id = 100000 });
            plan.LoiterToAlts.Add(new LoiterToAlt
            {
                PolylineId = 0, SegmentIndex = 0, T = 0.6, Id = 100001, LoiterId = 100002, Side = -1,
                LtaAltRelM = 500, LeadInAltRelM = 420,
            });
            return plan;
        }

        [TestMethod]
        public void RoundTrip_PreservesEverything()
        {
            var a = Sample();
            var b = CorridorPlanFile.FromJson(a.ToJson());

            Assert.AreEqual(CorridorPlanFile.CurrentVersion, b.Version);
            CollectionAssert.AreEqual(a.Home, b.Home);

            Assert.AreEqual(a.Params.MinAGL, b.Params.MinAGL);
            Assert.AreEqual(a.Params.Reverse, b.Params.Reverse);
            Assert.AreEqual(a.Params.OneWay, b.Params.OneWay);
            CollectionAssert.AreEqual(a.Params.OneWayEnd, b.Params.OneWayEnd);
            CollectionAssert.AreEqual(a.Params.TourStart, b.Params.TourStart);
            Assert.AreEqual(a.Params.GradMaxPct, b.Params.GradMaxPct);

            CollectionAssert.AreEqual(a.LegOrder, b.LegOrder);
            Assert.AreEqual(a.NextCheckpointId, b.NextCheckpointId);

            Assert.AreEqual(1, b.Features.Count);
            Assert.AreEqual("trunk.kml", b.Features[0].Name);
            CollectionAssert.AreEqual(a.Features[0].Polylines[0][1], b.Features[0].Polylines[0][1]);

            Assert.AreEqual(1, b.Zones.Count);
            Assert.AreEqual(300, b.Zones[0].CeilingAglM);
            Assert.AreEqual(3, b.Zones[0].Ring.Count);

            Assert.AreEqual(412.5, b.AltOverrides[0].AltM);
            Assert.AreEqual(2, b.CornerCutAlts[0].Index);

            Assert.AreEqual(0.25, b.Checkpoints[0].T);
            Assert.AreEqual(100000, b.Checkpoints[0].Id);

            var lta = b.LoiterToAlts[0];
            Assert.AreEqual(100002, lta.LoiterId);
            Assert.AreEqual(-1, lta.Side);
            Assert.AreEqual(500, lta.LtaAltRelM);
            Assert.AreEqual(420, lta.LeadInAltRelM);
        }

        [TestMethod]
        public void VertexAlts_RoundTripThroughDictionary()
        {
            var d = new Dictionary<VertexId, double>
            {
                [new VertexId(0, 3)] = 410,
                [new VertexId(2, 0)] = 455.5,
            };
            var back = CorridorPlanFile.ToVertexAlts(CorridorPlanFile.FromVertexAlts(d));
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(410, back[new VertexId(0, 3)]);
            Assert.AreEqual(455.5, back[new VertexId(2, 0)]);
        }

        [TestMethod]
        public void FromJson_MissingSectionsComeBackEmpty()
        {
            var plan = CorridorPlanFile.FromJson("{\"Version\":1}");
            Assert.AreEqual(0, plan.Features.Count);
            Assert.AreEqual(0, plan.Zones.Count);
            Assert.AreEqual(0, plan.LegOrder.Count);
            Assert.AreEqual(0, plan.AltOverrides.Count);
            Assert.AreEqual(0, plan.Checkpoints.Count);
            Assert.AreEqual(0, plan.LoiterToAlts.Count);
            Assert.IsNotNull(plan.Params);
        }

        [TestMethod]
        public void FromJson_NewerVersionRejected()
        {
            Assert.ThrowsException<System.FormatException>(
                () => CorridorPlanFile.FromJson("{\"Version\":99}"));
        }

        [TestMethod]
        public void ToPoint_TwoElementArrayHasZeroAlt()
        {
            var p = CorridorPlanFile.ToPoint(new[] { -33.5, 151.2 });
            Assert.AreEqual(-33.5, p.Lat);
            Assert.AreEqual(151.2, p.Lng);
            Assert.AreEqual(0, p.Alt);
        }
    }
}
