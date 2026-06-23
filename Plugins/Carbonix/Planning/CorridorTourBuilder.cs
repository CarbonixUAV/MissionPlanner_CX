using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Utilities;

namespace Carbonix.Planning
{
    /// <summary>
    /// Builds a polyline set + Euler tour from snapped polyline features (e.g. a
    /// multi-feature shapefile where each branch meets its parent at an exact shared
    /// vertex). Junctions are detected by exact vertex match; each feature is split at
    /// its junctions into edges; the resulting tree is walked as a DFS Euler tour where
    /// every edge is flown out (+offset) and back (−offset) — the two passes.
    ///
    /// The tour is rooted at the feature endpoint nearest <c>home</c> (the FlightPlanner
    /// home point). Branches are detoured before continuing the parent, so the tour reads
    /// "out along the trunk, out-and-back into each spur as it's reached, back along the
    /// trunk". See .claude/corridor-tree-design.md.
    /// </summary>
    public static class CorridorTourBuilder
    {
        // Snapped vertices are bit-identical doubles; round to ~0.1 mm so a shared vertex
        // keys identically while distinct vertices never collide.
        private const double KeyScale = 1e9;

        private static (long, long) Key(PointLatLngAlt p)
            => ((long)Math.Round(p.Lng * KeyScale), (long)Math.Round(p.Lat * KeyScale));

        public static (List<Polyline> polylines, List<TourStep> tour) Build(
            List<List<PointLatLngAlt>> features, PointLatLngAlt home,
            double passOffsetM, int numberOfPasses)
        {
            var polylines = new List<Polyline>();
            var tour = new List<TourStep>();

            var feats = (features ?? new List<List<PointLatLngAlt>>())
                .Where(f => f != null && f.Count >= 2).ToList();
            if (feats.Count == 0) return (polylines, tour);

            // 1. Index every vertex; a vertex touched by >1 feature is a junction node.
            var byKey = new Dictionary<(long, long), HashSet<int>>();
            for (int fi = 0; fi < feats.Count; fi++)
                foreach (var p in feats[fi])
                {
                    var k = Key(p);
                    if (!byKey.TryGetValue(k, out var set)) byKey[k] = set = new HashSet<int>();
                    set.Add(fi);
                }
            bool IsJunction((long, long) k) => byKey[k].Count > 1;

            // 2. Split each feature at its interior junction vertices into edges, and
            //    record adjacency (which edges touch each node, and at which end).
            int nextId = 0;
            var edgeFeature = new Dictionary<int, int>();   // edge id -> source feature
            var adjacency = new Dictionary<(long, long), List<(Polyline edge, bool atStart)>>();
            void AddAdj((long, long) node, Polyline edge, bool atStart)
            {
                if (!adjacency.TryGetValue(node, out var lst)) adjacency[node] = lst = new List<(Polyline, bool)>();
                lst.Add((edge, atStart));
            }

            for (int fi = 0; fi < feats.Count; fi++)
            {
                var f = feats[fi];
                var cut = new List<int> { 0 };
                for (int i = 1; i < f.Count - 1; i++)
                    if (IsJunction(Key(f[i]))) cut.Add(i);
                cut.Add(f.Count - 1);

                for (int c = 0; c + 1 < cut.Count; c++)
                {
                    int a = cut[c], b = cut[c + 1];
                    var seg = f.GetRange(a, b - a + 1)
                               .Select(p => new PointLatLngAlt(p.Lat, p.Lng, p.Alt)).ToList();
                    var edge = new Polyline { Id = nextId++, Points = seg };
                    polylines.Add(edge);
                    edgeFeature[edge.Id] = fi;
                    AddAdj(Key(seg[0]), edge, true);
                    AddAdj(Key(seg[seg.Count - 1]), edge, false);
                }
            }

            // 3. Start at the feature endpoint nearest home.
            var startNode = ChooseStart(feats, home);

            // 4. DFS Euler tour: out at +offset, back at −offset. At each node, detour
            //    into other features (branches) before continuing the arriving feature.
            double off = numberOfPasses >= 2 ? passOffsetM / 2.0 : 0.0;
            var visited = new HashSet<int>();

            void Dfs((long, long) node, int arrivingFeature)
            {
                if (!adjacency.TryGetValue(node, out var incident)) return;

                var ordered = incident
                    .Where(e => !visited.Contains(e.edge.Id))
                    .OrderBy(e => edgeFeature[e.edge.Id] == arrivingFeature ? 1 : 0)   // branches first
                    .ToList();

                foreach (var (edge, atStart) in ordered)
                {
                    if (visited.Contains(edge.Id)) continue;
                    visited.Add(edge.Id);

                    var far = atStart ? Key(edge.Points[edge.Points.Count - 1]) : Key(edge.Points[0]);

                    tour.Add(new TourStep
                    {
                        PolylineId = edge.Id,
                        Direction = atStart ? TraverseDir.Forward : TraverseDir.Reverse,
                        LaneOffsetM = +off,
                    });
                    Dfs(far, edgeFeature[edge.Id]);
                    tour.Add(new TourStep
                    {
                        PolylineId = edge.Id,
                        Direction = atStart ? TraverseDir.Reverse : TraverseDir.Forward,
                        LaneOffsetM = -off,
                    });
                }
            }

            Dfs(startNode, -1);
            return (polylines, tour);
        }

        private static (long, long) ChooseStart(List<List<PointLatLngAlt>> feats, PointLatLngAlt home)
        {
            PointLatLngAlt best = null;
            double bestD = double.MaxValue;
            foreach (var f in feats)
                foreach (var ep in new[] { f[0], f[f.Count - 1] })
                {
                    double d = home != null ? home.GetDistance(ep) : 0;
                    if (best == null || d < bestD) { best = ep; bestD = d; }
                }
            return Key(best);
        }
    }
}
