using System;
using System.Collections.Generic;
using System.Linq;
using Carbonix.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Utilities;

namespace Carbonix.Tests.Planning
{
    [TestClass]
    public class ProfileBuilderTests
    {
        const double BaseLat = -33.0;
        const double BaseLng = 151.0;
        const double MetresPerDegLat = 111195;   // PointLatLngAlt's spherical earth, roughly

        Func<double, double, double> srtm;

        [TestInitialize]
        public void FlatTerrain()
        {
            srtm = ProfileBuilder.TerrainProvider;
            ProfileBuilder.TerrainProvider = (lat, lng) => 10;
        }

        [TestCleanup]
        public void Restore() => ProfileBuilder.TerrainProvider = srtm;

        static ProfileWaypoint Wp(int id, double northM, double alt, MAVLink.MAV_CMD cmd = MAVLink.MAV_CMD.WAYPOINT,
            float p2 = 0, float p3 = 0) => new ProfileWaypoint
        {
            Id = id, Command = cmd, Lat = BaseLat + northM / MetresPerDegLat, Lng = BaseLng,
            AltM = alt, TerrainAltM = 10, P2 = p2, P3 = p3,
        };

        static List<ElevationPoint> Build(params ProfileWaypoint[] wps) =>
            ProfileBuilder.Build(wps.ToList(), new PointLatLngAlt(BaseLat, BaseLng), 0);

        [TestMethod]
        public void StraightLeg_SamplesEvery25mAndLandsOnNextWaypoint()
        {
            var pts = Build(Wp(5, 0, 100), Wp(6, 1000, 120));

            var dots = pts.Where(p => p.IsLineWaypoint).ToList();
            Assert.AreEqual(2, dots.Count);
            Assert.AreEqual(5, dots[0].WaypointIndex);
            Assert.AreEqual(0, dots[0].DistM, 1e-6);
            Assert.AreEqual(1000, dots[1].DistM, 2);

            var leg = pts.Where(p => p.IsLegTerrainSample).ToList();
            Assert.AreEqual((int)(dots[1].DistM / 25), leg.Count);
            Assert.AreEqual(dots[1].DistM, leg.Last().DistM, 1e-6);
            Assert.IsTrue(leg.All(p => p.WaypointIndex == 5 && p.AltRelM == 100 && p.TerrainAlt == 10));
        }

        [TestMethod]
        public void LoiterToAlt_FullTurnRampingFromPreviousWaypoint()
        {
            var pts = Build(Wp(0, 0, 100), Wp(1, 1000, 100),
                Wp(2, 2000, 200, MAVLink.MAV_CMD.LOITER_TO_ALT, p2: 80), Wp(3, 3000, 200));

            var bar = pts.Single(p => p.IsLoiterWaypoint);
            Assert.IsTrue(bar.IsLoiterToAlt);
            Assert.AreEqual(2 * Math.PI * 80, bar.LoiterArcLengthM, 1e-6);
            Assert.AreEqual(100, bar.LtaStartAltRelM);
            Assert.AreEqual(200, bar.AltRelM);

            var arc = pts.Where(p => p.IsLoiterArcSample).OrderBy(p => p.DistM).ToList();
            Assert.IsTrue(arc.Count >= 2);
            Assert.AreEqual(bar.DistM + bar.LoiterArcLengthM, arc.Last().DistM, 1e-6);
            Assert.AreEqual(200, arc.Last().AltRelM, 1e-6);
            Assert.IsTrue(arc.First().AltRelM > 100 && arc.First().AltRelM < 200);

            // Distance carries on after the turn.
            Assert.IsTrue(pts.Single(p => p.IsLineWaypoint && p.WaypointIndex == 3).DistM > arc.Last().DistM);
        }
    }
}
