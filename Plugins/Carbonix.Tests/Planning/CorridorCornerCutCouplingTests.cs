using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using Carbonix.Planning;

namespace Carbonix.Tests.Planning
{
    /// <summary>
    /// Corner cuts are chamfers of the fit-point polyline: straight lines station → fit point →
    /// fit point → station, with each chord endpoint on that polyline at its own x. Neighbouring
    /// cuts see each other's fit points, never each other's chords, so a run of consecutive cuts
    /// is not order-dependent and needs no iteration.
    /// </summary>
    [TestClass]
    public class CorridorCornerCutCouplingTests
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
            CorridorPlanner.TerrainProvider = (lat, lng) => 0.0;
        }

        [TestCleanup]
        public void Teardown() => CorridorPlanner.TerrainProvider = _origTerrain;

        // A zigzag heading east, bending ±40° at every vertex: every interior vertex is a corner
        // cut (15° ≤ turn < 90°), so each cut neighbours other cuts on both sides.
        static Polyline Zigzag(int vertices, double legM = 500)
        {
            var pts = new List<PointLatLngAlt>();
            double lat = BaseLat, lng = BaseLng;
            pts.Add(new PointLatLngAlt(lat, lng, 0));
            for (int i = 1; i < vertices; i++)
            {
                double headingDeg = (i % 2 == 1) ? 70 : 110;   // alternate ±20° about east → 40° bends
                double h = headingDeg * Math.PI / 180.0;
                lat += legM * Math.Cos(h) / MetresPerDegLat;
                lng += legM * Math.Sin(h) / MetresPerDegLng;
                pts.Add(new PointLatLngAlt(lat, lng, 0));
            }
            return new Polyline { Id = VertexId.MainLine, Points = pts };
        }

        static CorridorParameters Params() => new CorridorParameters
        {
            MinAGL = 50, MaxAGL = 120, DefaultAGL = 80, SpeedMs = 25, PassOffsetM = 0,
            CornerCutThresholdDeg = 15, FullOrbitThresholdDeg = 90, CornerCutRadiusM = 150, TurnRadiusM = 220,
        };

        static List<TourStep> OneWay() => new List<TourStep>
        {
            new TourStep { PolylineId = VertexId.MainLine, Direction = TraverseDir.Forward, OneWay = true },
        };

        static double Dist(CorridorWaypoint a, CorridorWaypoint b) =>
            new PointLatLngAlt(a.Lat, a.Lng, 0).GetDistance(new PointLatLngAlt(b.Lat, b.Lng, 0));

        // The control nodes along a gentle zigzag (no loiters, no helpers): stations at their x,
        // cuts as their fit point (chord midpoint, control altitude).
        static List<(double x, double alt, CorridorWaypoint e, CorridorWaypoint x2)> Nodes(
            List<CorridorWaypoint> wps, Dictionary<VertexId, double> controls)
        {
            var nodes = new List<(double, double, CorridorWaypoint, CorridorWaypoint)>();
            double x = 0; CorridorWaypoint last = null;
            for (int i = 0; i < wps.Count; i++)
            {
                var w = wps[i];
                if (!w.IsLineWaypoint) continue;
                if (last != null) x += Dist(last, w);
                last = w;
                if (i + 1 < wps.Count && wps[i + 1].IsLineWaypoint && wps[i + 1].Vertex == w.Vertex
                    && controls.TryGetValue(w.Vertex, out var ySet))
                {
                    double chord = Dist(w, wps[i + 1]);
                    nodes.Add((x + chord * 0.5, ySet, w, wps[i + 1]));
                    x += chord; last = wps[i + 1]; i++;
                }
                else
                {
                    nodes.Add((x, w.AltRelM, null, null));
                }
            }
            return nodes;
        }

        [TestMethod]
        public void ConsecutiveCuts_ChordsChamferTheFitPointPolyline()
        {
            var poly = Zigzag(8);
            // Strongly varying control altitudes so a chord read off the wrong line is unmistakable.
            var controls = new Dictionary<VertexId, double>();
            for (int v = 1; v < 7; v++) controls[new VertexId(VertexId.MainLine, v)] = (v % 2 == 1) ? 900 : 500;

            var wps = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, OneWay(), Params(), poly.Points[0], null, null, controls);
            var nodes = Nodes(wps, controls);

            int cuts = 0;
            for (int k = 1; k + 1 < nodes.Count; k++)
            {
                var n = nodes[k];
                if (n.e == null) continue;
                cuts++;
                var p = nodes[k - 1]; var q = nodes[k + 1];
                double half = Dist(n.e, n.x2) * 0.5;

                double expEntry = p.alt + (n.alt - p.alt) * (n.x - half - p.x) / (n.x - p.x);
                double expExit = n.alt + (q.alt - n.alt) * half / (q.x - n.x);
                Assert.AreEqual(expEntry, n.e.AltRelM, 0.01, $"entry of cut at vertex {n.e.Vertex.Index}");
                Assert.AreEqual(expExit, n.x2.AltRelM, 0.01, $"exit of cut at vertex {n.e.Vertex.Index}");

                // And therefore each endpoint lies between its two defining node altitudes.
                Assert.IsTrue(n.e.AltRelM >= Math.Min(p.alt, n.alt) - 1e-6 && n.e.AltRelM <= Math.Max(p.alt, n.alt) + 1e-6);
                Assert.IsTrue(n.x2.AltRelM >= Math.Min(n.alt, q.alt) - 1e-6 && n.x2.AltRelM <= Math.Max(n.alt, q.alt) + 1e-6);
            }
            Assert.AreEqual(6, cuts);
        }

        [TestMethod]
        public void OverriddenNeighbour_IsWhatTheChordIsDrawnAgainst()
        {
            var poly = Zigzag(4);   // one cut at vertex 1 and one at vertex 2, end station at 3
            var controls = new Dictionary<VertexId, double>
            {
                [new VertexId(VertexId.MainLine, 1)] = 600,
                [new VertexId(VertexId.MainLine, 2)] = 600,
            };
            var overrides = new Dictionary<VertexId, double> { [new VertexId(VertexId.MainLine, 3)] = 1000 };

            var wps = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, OneWay(), Params(), poly.Points[0], null, null, controls, overrides);
            var nodes = Nodes(wps, controls);

            var end = wps.Last(w => w.IsLineWaypoint);
            Assert.AreEqual(1000, end.AltRelM, 1e-6);

            // Cut 2's exit lies on the line from its fit point (600) to the overridden end (1000).
            var n = nodes[2]; var q = nodes[3];
            double half = Dist(n.e, n.x2) * 0.5;
            double expExit = n.alt + (q.alt - n.alt) * half / (q.x - n.x);
            Assert.AreEqual(1000, q.alt, 1e-6);
            Assert.AreEqual(expExit, n.x2.AltRelM, 0.01);
            Assert.IsTrue(n.x2.AltRelM > 600);
        }

        [TestMethod]
        public void ConsecutiveCuts_IndependentOfOtherCuts()
        {
            // Changing one cut's control must move only the chords adjacent to it.
            var poly = Zigzag(8);
            var a = new Dictionary<VertexId, double>();
            for (int v = 1; v < 7; v++) a[new VertexId(VertexId.MainLine, v)] = 700;
            var b = new Dictionary<VertexId, double>(a) { [new VertexId(VertexId.MainLine, 5)] = 300 };

            var wa = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, OneWay(), Params(), poly.Points[0], null, null, a);
            var wb = CorridorPlanner.GenerateMissionFromTour(new List<Polyline> { poly }, OneWay(), Params(), poly.Points[0], null, null, b);

            Assert.AreEqual(wa.Count, wb.Count);
            for (int i = 0; i < wa.Count; i++)
            {
                int v = wa[i].Vertex.Index;
                bool touched = v == 4 || v == 5 || v == 6;   // cut 5 itself and the chords facing it
                if (touched) continue;
                Assert.AreEqual(wa[i].AltRelM, wb[i].AltRelM, 1e-6, $"vertex {v} at {i}");
            }
        }
    }
}
