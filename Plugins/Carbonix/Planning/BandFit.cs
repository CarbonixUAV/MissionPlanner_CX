using System;
using System.Collections.Generic;
using System.Linq;

namespace Carbonix.Planning
{
    // A fit node: a waypoint (WpId), a track sample that may become an insert (Sample), or a
    // fixed point (neither). Spans X0..X1 along track (a loiter's orbit), height in Lo..Hi.
    internal class FitNode
    {
        public int WpId = -1;
        public ElevationPoint Sample;
        public double X0, X1, Lo, Hi, H;
        public double Cap = double.PositiveInfinity;   // highest allowed when the band gives way
        public bool Free => WpId >= 0 || Sample != null;

        public static FitNode Fixed(double x0, double x1, double h) =>
            new FitNode { X0 = x0, X1 = x1, Lo = h, Hi = h, H = h };
    }

    /// <summary>
    /// Fits waypoint heights into a target band along a profile: fewest added waypoints first,
    /// then the gentlest gradients. Heights are AMSL metres, positions metres along track.
    /// </summary>
    internal static class BandFit
    {
        // Walks the track samples strictly inside legs of the fitted path, giving each its leg
        // index and how far (m) the leg's straight line is outside the sample's box.
        private static IEnumerable<(int k, int leg, double outside)> LegChecks(List<FitNode> all, double[] h, List<FitNode> track)
        {
            int j = 0;
            for (int k = 0; k < track.Count; k++)
            {
                double x = track[k].X0;
                while (j + 1 < all.Count && all[j + 1].X0 <= x) j++;
                if (j + 1 >= all.Count) yield break;
                double a = all[j].X1, b = all[j + 1].X0;
                if (x <= a || x >= b) continue;   // on a node or inside a loiter
                double alt = h[j] + (h[j + 1] - h[j]) * (x - a) / (b - a);
                yield return (k, j, Math.Max(track[k].Lo - alt, alt - track[k].Hi));
            }
        }

        private const double BandTolM = 0.5;

        // Steepest leg touching a free node, and how many legs leave the band.
        public static (double maxGrad, int outLegs) Stats(List<FitNode> all, double[] h, List<FitNode> track)
        {
            double maxGrad = 0;
            for (int i = 0; i + 1 < all.Count; i++)
                if (all[i].Free || all[i + 1].Free)
                    maxGrad = Math.Max(maxGrad, Math.Abs(h[i + 1] - h[i]) / Math.Max(1, all[i + 1].X0 - all[i].X1) * 100);
            int outLegs = LegChecks(all, h, track).Where(c => c.outside > BandTolM).Select(c => c.leg).Distinct().Count();
            return (maxGrad, outLegs);
        }

        // Heights reachable at full[At] with a given leg count: the union of overlapping legs
        // landing there (From), each remembering the Seg it left from.
        private class Seg
        {
            public int At;
            public double Lo, Hi;
            public readonly List<Leg> From = new List<Leg>();
        }

        // One leg into a Seg: its landing interval, where it left from, and its feasible lines as
        // (start height, slope). Poly null = a jump over a stretch whose band was given up;
        // Prev null = the mission start.
        private class Leg
        {
            public double Lo, Hi;
            public Seg Prev;
            public List<(double h, double s)> Poly;
        }

        // Overlapping landing intervals at one sample merged into disjoint Segs.
        private static IEnumerable<Seg> MergeLegs(int at, List<Leg> legs)
        {
            Seg cur = null;
            foreach (var l in legs.OrderBy(l => l.Lo))
            {
                if (cur == null || l.Lo > cur.Hi + 1e-6)
                {
                    if (cur != null) yield return cur;
                    cur = new Seg { At = at, Lo = l.Lo, Hi = l.Hi };
                }
                cur.Hi = Math.Max(cur.Hi, l.Hi);
                cur.From.Add(l);
            }
            if (cur != null) yield return cur;
        }

        // Heights for the waypoints (and, with addWps, the fewest inserts) that keep every leg
        // inside every sample's box, then the gentlest gradients that still do. A node with a
        // span (a loiter) is a flat stretch: its entry and exit share one height and the span
        // itself is no leg. Where a stretch between waypoints can't stay in the band (only
        // possible without addWps, or under the gradient limit) the fallbacks go safest first:
        // climb above the band up to each point's Cap; then keep the band but drop the
        // gradient limit for that stretch; then ignore its samples. So clearance beats the
        // limit, which beats the band top. Stats reports what's left out of band.
        //  1. Fewest legs, one stretch between waypoints at a time (waypoints are forced
        //     vertices): breadth-first by leg count over (sample, height interval). From each
        //     interval a leg stretches along the track window by window while some straight line
        //     still fits (the polygon of feasible lines, clipped per window); every sample it
        //     reaches gets the heights those lines land on. Overlapping intervals at a sample
        //     merge; ones already covered with fewer legs are dropped. The first layer to reach
        //     the stretch's end waypoint is its minimum, and every height reachable there at that
        //     count carries into the next stretch. Earlier inserts can sit anywhere, not just as
        //     far along as they reach.
        //     Without addWps a leg may only land on the stretch's end waypoint.
        //  2. Backward: pick heights from the end, each leg taking its flattest feasible line.
        //  3. Smooth + slide (below) for the gradient, without changing the count.
        // Returns the node list (waypoints + samples to insert) and their heights.
        // ponytail: minimum per stretch, not jointly (a stretch never spends an extra insert to
        // widen the next one's start); step 3 is local search, so the gradient is low but not
        // guaranteed optimal among all minimum-count layouts.
        public static List<FitNode> Fit(List<FitNode> nodes, List<FitNode> track, bool addWps,
            double maxGradPct, out double[] h)
        {
            const double eps = 0.01;      // window slack so fixed (zero-width) windows clip cleanly
            // Hard gradient limit: bounds every leg's slope in the search, the backward pass and the
            // smoothing. Takes priority over the band: a stretch that can't hold both drops the band.
            double maxSlope = maxGradPct > 0 ? maxGradPct / 100 : 10;
            // Drop samples on a waypoint or inside a loiter's span: each profile leg's last sample
            // lands exactly on the next station, and a zero/negative gap breaks the geometry.
            track = track.Where(t => !nodes.Any(n => t.X0 >= n.X0 - 1 && t.X0 <= n.X1 + 1)).ToList();
            var full = nodes.Concat(track).OrderBy(n => n.X0).ToList();
            int endAt = full.Count - 1;
            var ignore = new bool[full.Count];   // samples on stretches that can't stay in band
            var slopeOf = new double[full.Count];   // slope limit of the stretch ending at/after k

            // Breadth-first by leg count from the Segs at s0 to the waypoint at s1;
            // the Segs reachable there at the minimum count, or null if none within the limits.
            List<Seg> Stretch(List<Seg> from0, int s0, int s1, double slope)
            {
                var seen = new Dictionary<int, List<Seg>> { [s0] = from0 };
                var frontier = from0;
                while (frontier.Count > 0)
                {
                    var found = new Dictionary<int, List<Leg>>();
                    foreach (var from in frontier)
                    {
                        int i = from.At;
                        var poly = new List<(double h, double s)>
                            { (from.Lo, -slope), (from.Hi, -slope), (from.Hi, slope), (from.Lo, slope) };
                        for (int k = i + 1; k <= s1; k++)
                        {
                            double d = full[k].X0 - full[i].X1;
                            poly = Clip(Clip(poly, 1, d, full[k].Hi + eps), -1, -d, -(full[k].Lo - eps));
                            if (poly.Count == 0) break;
                            if (!addWps && k != s1) continue;   // may only land on the end waypoint
                            if (!found.TryGetValue(k, out var list)) found[k] = list = new List<Leg>();
                            list.Add(new Leg
                            {
                                Prev = from, Poly = poly,
                                Lo = poly.Min(v => v.h + v.s * d), Hi = poly.Max(v => v.h + v.s * d),
                            });
                        }
                    }

                    if (found.TryGetValue(s1, out var ends)) return MergeLegs(s1, ends).ToList();
                    var next = new List<Seg>();
                    foreach (var kv in found)
                    {
                        if (!seen.TryGetValue(kv.Key, out var old)) seen[kv.Key] = old = new List<Seg>();
                        foreach (var seg in MergeLegs(kv.Key, kv.Value))
                        {
                            if (old.Any(o => o.Lo <= seg.Lo + 1e-6 && o.Hi >= seg.Hi - 1e-6)) continue;
                            old.Add(seg);
                            next.Add(seg);
                        }
                    }
                    frontier = next;
                }
                return null;
            }

            // 1. Fewest legs, stretch by stretch.
            var start = new Seg { At = 0, Lo = full[0].Lo - eps, Hi = full[0].Hi + eps };
            start.From.Add(new Leg { Lo = start.Lo, Hi = start.Hi });
            var cur = new List<Seg> { start };
            int runStart = 0;
            while (runStart < endAt)
            {
                int runEnd = runStart + 1;
                while (full[runEnd].Sample != null) runEnd++;   // next waypoint (the last node is one)

                // Fallbacks, safest first: clearance (band floor) beats the gradient limit, which
                // beats the band top.
                double slope = maxSlope;
                var atEnd = Stretch(cur, runStart, runEnd, slope);
                if (atEnd == null)
                {
                    // Band and limit can't both hold: fly above the band, up to the ceiling.
                    // Waypoints keep their band when inserts can do the climbing.
                    var orig = full.GetRange(runStart + 1, runEnd - runStart);
                    for (int k = runStart + 1; k <= runEnd; k++)
                    {
                        var n = full[k];
                        if (n.Lo < n.Hi && n.Cap > n.Hi && (n.Sample != null || !addWps))
                            full[k] = new FitNode { WpId = n.WpId, Sample = n.Sample, X0 = n.X0, X1 = n.X1, Lo = n.Lo, Hi = n.Cap, H = n.H };
                    }
                    atEnd = Stretch(cur, runStart, runEnd, slope);
                    if (atEnd == null)
                    {
                        // Still not: keep the band, give up the limit here (Stats shows it).
                        for (int k = runStart + 1; k <= runEnd; k++) full[k] = orig[k - runStart - 1];
                        slope = 10;
                        atEnd = Stretch(cur, runStart, runEnd, slope);
                    }
                }
                for (int k = runStart + 1; k <= runEnd; k++) slopeOf[k] = slope;
                if (atEnd == null)
                {
                    // Still nothing: ignore this stretch's samples and jump to its end.
                    for (int k = runStart + 1; k < runEnd; k++) ignore[k] = true;
                    var jump = new Seg { At = runEnd, Lo = full[runEnd].Lo - eps, Hi = full[runEnd].Hi + eps };
                    jump.From.Add(new Leg { Lo = jump.Lo, Hi = jump.Hi, Prev = cur.OrderByDescending(g => g.Hi - g.Lo).First() });
                    atEnd = new List<Seg> { jump };
                }
                cur = atEnd;
                runStart = runEnd;
            }

            // 2. Backward.
            var last = cur.OrderByDescending(g => g.Hi - g.Lo).First();
            double y = (Math.Max(last.Lo, full[endAt].Lo) + Math.Min(last.Hi, full[endAt].Hi)) / 2;
            var verts = new List<int> { endAt };
            var hs = new List<double> { y };
            for (var seg = last; ; )
            {
                double yy = y;
                var leg = seg.From.FirstOrDefault(l => l.Lo - 1e-6 <= yy && yy <= l.Hi + 1e-6)
                          ?? seg.From.OrderBy(l => Math.Min(Math.Abs(l.Lo - yy), Math.Abs(l.Hi - yy))).First();
                if (leg.Prev == null) break;
                double d = full[seg.At].X0 - full[leg.Prev.At].X1;
                y = leg.Poly == null ? (leg.Prev.Lo + leg.Prev.Hi) / 2 : FlattestStart(leg.Poly, d, y);
                seg = leg.Prev;
                verts.Add(seg.At);
                hs.Add(y);
            }
            verts.Reverse();
            hs.Reverse();
            var hv = hs.Select((v, k) => Math.Max(full[verts[k]].Lo, Math.Min(full[verts[k]].Hi, v))).ToArray();

            // Height interval at full[c] that keeps its legs (from full[pa] at ha, to full[pb]
            // at hb; -1 = none) inside every window they cross, and the neighbour-weighted
            // (minimum-gradient) target height.
            (double lo, double hi, double target) Range(int pa, double ha, int c, int pb, double hb)
            {
                double lo = full[c].Lo - eps, hi = full[c].Hi + eps, w = 0, sum = 0;
                if (pa >= 0)
                {
                    double a = full[pa].X1, span = full[c].X0 - a;
                    for (int k = pa + 1; k < c; k++)   // (1-t)ha + t hc within the window
                    {
                        if (ignore[k]) continue;
                        double t = (full[k].X0 - a) / span;
                        lo = Math.Max(lo, (full[k].Lo - eps - (1 - t) * ha) / t);
                        hi = Math.Min(hi, (full[k].Hi + eps - (1 - t) * ha) / t);
                    }
                    lo = Math.Max(lo, ha - slopeOf[c] * span);
                    hi = Math.Min(hi, ha + slopeOf[c] * span);
                    w += 1 / Math.Max(1, span); sum += ha / Math.Max(1, span);
                }
                if (pb >= 0)
                {
                    double a = full[c].X1, span = full[pb].X0 - a;
                    for (int k = c + 1; k < pb; k++)   // (1-t)hc + t hb within the window
                    {
                        if (ignore[k]) continue;
                        double t = (full[k].X0 - a) / span;
                        lo = Math.Max(lo, (full[k].Lo - eps - t * hb) / (1 - t));
                        hi = Math.Min(hi, (full[k].Hi + eps - t * hb) / (1 - t));
                    }
                    lo = Math.Max(lo, hb - slopeOf[pb] * span);
                    hi = Math.Min(hi, hb + slopeOf[pb] * span);
                    w += 1 / Math.Max(1, span); sum += hb / Math.Max(1, span);
                }
                return (lo, hi, w > 0 ? sum / w : (lo + hi) / 2);
            }

            // 3. Alternate: smooth heights toward neighbours; then slide each insert along its
            //    leg to the sample where its steeper leg is gentlest. Both stay in the band.
            int lastV = verts.Count - 1;
            for (int round = 0; round < 50; round++)
            {
                for (int sweep = 0; sweep < 5000; sweep++)
                {
                    double moved = 0;
                    for (int v = 0; v <= lastV; v++)
                    {
                        var n = full[verts[v]];
                        if (n.Lo == n.Hi) continue;
                        var (lo, hi, tg) = Range(v > 0 ? verts[v - 1] : -1, v > 0 ? hv[v - 1] : 0,
                                                 verts[v], v < lastV ? verts[v + 1] : -1, v < lastV ? hv[v + 1] : 0);
                        if (lo > hi) continue;
                        double nv = Math.Max(lo, Math.Min(hi, tg));
                        moved = Math.Max(moved, Math.Abs(nv - hv[v]));
                        hv[v] = nv;
                    }
                    if (moved < 1e-3) break;
                }

                bool slid = false;
                for (int v = 1; v < lastV; v++)
                {
                    if (full[verts[v]].Sample == null) continue;
                    int pa = verts[v - 1], pb = verts[v + 1];
                    double Steeper(int c, double hc) => Math.Max(
                        Math.Abs(hc - hv[v - 1]) / Math.Max(1, full[c].X0 - full[pa].X1),
                        Math.Abs(hv[v + 1] - hc) / Math.Max(1, full[pb].X0 - full[c].X1));
                    int bestC = verts[v];
                    double bestH = hv[v], bestG = Steeper(bestC, bestH);
                    for (int c = pa + 1; c < pb; c++)
                    {
                        var (lo, hi, tg) = Range(pa, hv[v - 1], c, pb, hv[v + 1]);
                        if (lo > hi) continue;
                        double hc = Math.Max(lo, Math.Min(hi, tg)), g = Steeper(c, hc);
                        if (g < bestG - 1e-6) { bestC = c; bestH = hc; bestG = g; }
                    }
                    if (bestC != verts[v]) { verts[v] = bestC; hv[v] = bestH; slid = true; }
                }
                if (!slid) break;
            }

            h = hv;
            return verts.Select(v => full[v]).ToList();
        }

        // Clip a convex polygon in (h, s) to a*h + b*s <= c (Sutherland-Hodgman).
        private static List<(double h, double s)> Clip(List<(double h, double s)> poly, double a, double b, double c)
        {
            var res = new List<(double h, double s)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                double fp = a * p.h + b * p.s - c, fq = a * q.h + b * q.s - c;
                if (fp <= 0) res.Add(p);
                if ((fp < 0 && fq > 0) || (fp > 0 && fq < 0))
                {
                    double t = fp / (fp - fq);
                    res.Add((p.h + t * (q.h - p.h), p.s + t * (q.s - p.s)));
                }
            }
            return res;
        }

        // Of the lines in poly ending at height y after distance d (h + s*d = y), the start
        // height of the flattest one.
        private static double FlattestStart(List<(double h, double s)> poly, double d, double y)
        {
            double sMin = double.MaxValue, sMax = double.MinValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                double fp = p.h + p.s * d - y, fq = q.h + q.s * d - y;
                if (Math.Abs(fp) < 1e-9) { sMin = Math.Min(sMin, p.s); sMax = Math.Max(sMax, p.s); }
                if ((fp < 0 && fq > 0) || (fp > 0 && fq < 0))
                {
                    double sc = p.s + fp / (fp - fq) * (q.s - p.s);
                    sMin = Math.Min(sMin, sc); sMax = Math.Max(sMax, sc);
                }
            }
            if (sMin > sMax)
            {
                // Numerically just outside: the vertex nearest the line.
                var p = poly.OrderBy(v => Math.Abs(v.h + v.s * d - y)).First();
                return p.h;
            }
            double sBest = Math.Max(sMin, Math.Min(sMax, 0));
            return y - sBest * d;
        }
    }
}
