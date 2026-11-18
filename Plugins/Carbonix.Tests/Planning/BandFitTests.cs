using System;
using System.Collections.Generic;
using System.Linq;
using Carbonix.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Planning
{
    [TestClass]
    public class BandFitTests
    {
        // Band 70-90 m above terrain throughout.
        const double Lo = 70, Hi = 90;

        static FitNode Wp(int id, double x, Func<double, double> terr) =>
            new FitNode { WpId = id, X0 = x, X1 = x, Lo = terr(x) + Lo, Hi = terr(x) + Hi, H = terr(x) + 80 };

        static FitNode Sample(double x, Func<double, double> terr) =>
            new FitNode { Sample = new ElevationPoint(), X0 = x, X1 = x, Lo = terr(x) + Lo, Hi = terr(x) + Hi, H = terr(x) + 80 };

        static List<FitNode> Track(Func<double, double> terr, double len, double step = 50)
        {
            var track = new List<FitNode>();
            for (double x = step; x < len; x += step) track.Add(Sample(x, terr));
            return track;
        }

        class Result
        {
            public List<FitNode> All;
            public double[] H;
            public double MaxGrad;
            public int OutLegs;
            public List<double> InsertXs => All.Where(n => n.Sample != null).Select(n => n.X0).ToList();
        }

        static Result Fit(List<FitNode> nodes, List<FitNode> track, bool addWps = true, double gradMax = 1000)
        {
            var all = BandFit.Fit(nodes, track, addWps, gradMax, out var h);
            var (maxGrad, outLegs) = BandFit.Stats(all, h, track);
            Assert.IsFalse(h.Any(double.IsNaN));
            return new Result { All = all, H = h, MaxGrad = maxGrad, OutLegs = outLegs };
        }

        // Waypoints at both ends of a terrain profile, samples every 50 m between.
        static Result FitEnds(Func<double, double> terr, double len, double gradMax = 1000) =>
            Fit(new List<FitNode> { Wp(0, 0, terr), Wp(1, len, terr) }, Track(terr, len), true, gradMax);

        static double Ridge(double x) => 100 - Math.Abs(x - 2000) / 20;

        [TestMethod]
        public void Ridge_OneInsertAtThePeak()
        {
            var r = FitEnds(Ridge, 4000);
            Assert.AreEqual(1, r.InsertXs.Count);
            Assert.AreEqual(2000, r.InsertXs[0], 1);
            Assert.AreEqual(4, r.MaxGrad, 0.05);   // (170 - 90) / 2000
            Assert.AreEqual(0, r.OutLegs);
        }

        [TestMethod]
        public void Flat_NoInserts()
        {
            var r = FitEnds(x => 0, 4000);
            Assert.AreEqual(0, r.InsertXs.Count);
            Assert.AreEqual(0, r.OutLegs);
        }

        [TestMethod]
        public void RollingHills_StaysInBand()
        {
            var r = FitEnds(x => 150 * Math.Sin(x / 1500) + 60 * Math.Sin(x / 400), 20000);
            Assert.AreEqual(0, r.OutLegs);
        }

        [TestMethod]
        public void Tabletop_StaysInBandWithFewInserts()
        {
            Func<double, double> t = x => x < 3000 ? 0 : x < 3600 ? (x - 3000) / 4 : x < 5600 ? 150 : x < 6200 ? 150 - (x - 5600) / 4 : 0;
            var r = FitEnds(t, 9000);
            Assert.AreEqual(0, r.OutLegs);
            Assert.IsTrue(r.InsertXs.Count <= 4, $"{r.InsertXs.Count} inserts");
        }

        [TestMethod]
        public void ExistingWaypointOnPeak_NoInserts()
        {
            var nodes = new List<FitNode> { FitNode.Fixed(0, 0, 80), Wp(5, 2000, Ridge), Wp(6, 4000, Ridge) };
            var track = Track(Ridge, 4000).Where(n => n.X0 != 2000).ToList();
            var r = Fit(nodes, track);
            Assert.AreEqual(0, r.InsertXs.Count);
        }

        [TestMethod]
        public void RandomTerrain_NeverMoreLegsThanBruteForce()
        {
            // Brute force over 1 m height levels is a restriction of the real problem, so the
            // fit must never need more legs.
            var rng = new Random(1);
            for (int trial = 0; trial < 40; trial++)
            {
                int n = 16;
                var terr = new double[n + 1];
                for (int i = 1; i <= n; i++) terr[i] = terr[i - 1] + rng.Next(-30, 31);
                var r = FitEnds(x => terr[(int)Math.Round(x / 50)], n * 50);
                Assert.AreEqual(0, r.OutLegs);

                int levels = (int)(Hi - Lo) + 1;
                var dist = new int[n + 1, levels];
                for (int i = 0; i <= n; i++) for (int l = 0; l < levels; l++) dist[i, l] = i == 0 ? 0 : int.MaxValue;
                for (int i = 0; i < n; i++) for (int a = 0; a < levels; a++)
                {
                    if (dist[i, a] == int.MaxValue) continue;
                    double ha = terr[i] + Lo + a;
                    for (int j = i + 1; j <= n; j++) for (int b = 0; b < levels; b++)
                    {
                        double hb = terr[j] + Lo + b;
                        bool ok = true;
                        for (int k = i + 1; k < j && ok; k++)
                        {
                            double alt = ha + (hb - ha) * (k - i) / (double)(j - i);
                            ok = alt >= terr[k] + Lo - 0.01 && alt <= terr[k] + Hi + 0.01;
                        }
                        if (ok && dist[i, a] + 1 < dist[j, b]) dist[j, b] = dist[i, a] + 1;
                    }
                }
                int best = Enumerable.Range(0, levels).Min(l => dist[n, l]);
                Assert.IsTrue(r.InsertXs.Count + 1 <= best, $"trial {trial}: {r.InsertXs.Count + 1} legs vs {best}");
            }
        }

        [TestMethod]
        public void GradientLimit_FliesOverSpikeAboveBandNotBelow()
        {
            // A 60 m spike, 200 m wide: following the band needs ~24%. At 5% the fit holds the
            // limit and goes over it above the band, never below the floor.
            Func<double, double> t = x => Math.Abs(x - 2000) < 100 ? 60 : 0;
            var r = Fit(new List<FitNode> { Wp(0, 0, t), Wp(1, 4000, t) }, Track(t, 4000, 25), true, 5);
            var xs = r.All.Select(n => n.X0).ToList();
            for (int i = 0; i + 1 < xs.Count; i++)
                Assert.IsTrue(Math.Abs(r.H[i + 1] - r.H[i]) / (xs[i + 1] - xs[i]) * 100 <= 5.01);
            for (double x = 25; x < 4000; x += 25)
            {
                int i = xs.FindLastIndex(v => v <= x);
                if (i >= xs.Count - 1) continue;
                double alt = r.H[i] + (r.H[i + 1] - r.H[i]) * (x - xs[i]) / (xs[i + 1] - xs[i]);
                Assert.IsTrue(alt >= t(x) + Lo - 0.5, $"below the floor at {x}");
            }
        }

        [TestMethod]
        public void GradientLimit_ClearanceWinsWhenBothCantHold()
        {
            // The ridge needs 4%: fine at 5%; at 3% the band floor wins and the gradient goes over.
            var r = FitEnds(Ridge, 4000, 5);
            Assert.AreEqual(1, r.InsertXs.Count);
            Assert.AreEqual(0, r.OutLegs);
            Assert.IsTrue(r.MaxGrad <= 5.01);

            r = FitEnds(Ridge, 4000, 3);
            Assert.AreEqual(0, r.OutLegs);
        }

        [TestMethod]
        public void NoInserts_LiftsLegOverHump()
        {
            // A flat 80 m leg would dip out of band at the 15 m hump; the fit lifts the ends.
            Func<double, double> t = x => 15 * Math.Sin(Math.PI * x / 4000);
            var nodes = new List<FitNode> { Wp(0, 0, t), Wp(1, 4000, t) };
            var r = Fit(nodes, Track(t, 4000, 25), addWps: false);
            Assert.AreEqual(2, r.All.Count);
            Assert.AreEqual(0, r.OutLegs);
        }

        [TestMethod]
        public void NoInserts_ImpossibleSpikeReportedNotThrown()
        {
            Func<double, double> t = x => Math.Abs(x - 2000) < 100 ? 60 : 0;
            var r = Fit(new List<FitNode> { Wp(0, 0, t), Wp(1, 4000, t) }, Track(t, 4000, 25), addWps: false);
            Assert.AreEqual(2, r.All.Count);
            Assert.AreEqual(1, r.OutLegs);
        }

        [TestMethod]
        public void Loiter_FlatAcrossItsSpan()
        {
            Func<double, double> t = x => x / 100;   // 1% slope
            var loiter = new FitNode { WpId = 1, X0 = 2000, X1 = 3000, Lo = t(3000) + Lo, Hi = t(2000) + Hi };
            var nodes = new List<FitNode> { Wp(0, 0, t), loiter, Wp(2, 5000, t) };
            var r = Fit(nodes, Track(t, 5000, 25), addWps: false);   // samples inside the span are dropped
            Assert.AreEqual(3, r.All.Count);
            Assert.AreEqual(0, r.OutLegs);
        }
    }
}
