using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Utilities;

namespace Carbonix.Planning
{
    /// <summary>
    /// Builds a polyline set + tour from snapped polyline features (e.g. a multi-feature
    /// shapefile where each branch meets its parent at an exact shared vertex). Junctions
    /// are detected by exact vertex match; each feature is split at its junctions into
    /// edges; the resulting tree is walked as a DFS Euler tour where every edge is flown
    /// out (+offset) and back (−offset) — the two passes.
    ///
    /// The tour is rooted at the feature endpoint nearest <c>home</c> (the FlightPlanner
    /// home point). Branches are detoured before continuing the parent, so the tour reads
    /// "out along the trunk, out-and-back into each spur as it's reached, back along the
    /// trunk". See .claude/corridor-tree-design.md.
    ///
    /// A ONE-WAY tour ends at a dead-end instead of returning to the start: the edges on
    /// the start→end path are flown once, on the centreline; every other edge (a spur off
    /// that path) is still flown out and back. A non-branching line is then simply flown
    /// end to end.
    /// </summary>
    public static class CorridorTourBuilder
    {
        // Snapped vertices are bit-identical doubles; round to ~0.1 mm so a shared vertex
        // keys identically while distinct vertices never collide.
        private const double KeyScale = 1e9;

        private static (long, long) Key(PointLatLngAlt p)
            => ((long)Math.Round(p.Lng * KeyScale), (long)Math.Round(p.Lat * KeyScale));

        /// <param name="oneWay">
        /// End the tour at a dead-end rather than back at the start (see class remarks).
        /// The end is <paramref name="oneWayEnd"/>'s nearest dead-end when given, else the
        /// dead-end farthest from the start along the network (the most distance saved).
        /// A network with no dead-end (a loop) falls back to a round trip.
        /// </param>
        /// <param name="legPriority">
        /// Optional edge-id order expressing the preferred branch-visit order. Where the DFS
        /// genuinely has a choice (several unvisited edges at a node), edges earlier in this
        /// list are taken first; unlisted edges fall back to the default "branches first"
        /// heuristic. The tour stays a valid connected walk regardless.
        /// </param>
        /// <param name="startAt">
        /// Start the tour at the network node (feature endpoint or junction) nearest this
        /// point instead of the feature endpoint nearest <paramref name="home"/>.
        /// </param>
        public static (List<Polyline> polylines, List<TourStep> tour) Build(
            List<List<PointLatLngAlt>> features, PointLatLngAlt home,
            double passOffsetM, bool oneWay = false, bool reverse = false,
            IReadOnlyList<int> legPriority = null, PointLatLngAlt oneWayEnd = null,
            PointLatLngAlt startAt = null)
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
            var nodePoint = new Dictionary<(long, long), PointLatLngAlt>();
            void AddAdj((long, long) node, Polyline edge, bool atStart, PointLatLngAlt at)
            {
                if (!adjacency.TryGetValue(node, out var lst)) adjacency[node] = lst = new List<(Polyline, bool)>();
                lst.Add((edge, atStart));
                if (!nodePoint.ContainsKey(node)) nodePoint[node] = at;
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
                    AddAdj(Key(seg[0]), edge, true, seg[0]);
                    AddAdj(Key(seg[seg.Count - 1]), edge, false, seg[seg.Count - 1]);
                }
            }

            // 3. Start at the chosen node, else the feature endpoint nearest home.
            var startNode = startAt != null
                ? nodePoint.OrderBy(kv => kv.Value.GetDistance(startAt)).First().Key
                : ChooseStart(feats, home);

            // One way: choose the end and the edges on the start→end path. Those edges are
            // taken LAST at each node so their back-passes fall at the very end of the walk,
            // where they can simply be dropped.
            var pathEdges = new HashSet<int>();
            (long, long)? endNode = null;
            if (oneWay)
                endNode = ChooseEnd(adjacency, nodePoint, startNode, oneWayEnd, pathEdges);

            // 4. DFS Euler tour: out at +offset, back at −offset. At each node, detour
            //    into other features (branches) before continuing the arriving feature.
            double off = passOffsetM / 2.0;
            var visited = new HashSet<int>();

            // Priority lookup for the branch-visit-order hint (lower rank = visited first).
            Dictionary<int, int> rank = null;
            if (legPriority != null)
            {
                rank = new Dictionary<int, int>();
                for (int i = 0; i < legPriority.Count; i++)
                    if (!rank.ContainsKey(legPriority[i])) rank[legPriority[i]] = i;
            }

            void Dfs((long, long) node, int arrivingFeature)
            {
                if (!adjacency.TryGetValue(node, out var incident)) return;

                var ordered = incident
                    .Where(e => !visited.Contains(e.edge.Id))
                    // The one-way exit path last, then the user's branch-visit order (unlisted
                    // edges after), then branches-first.
                    .OrderBy(e => pathEdges.Contains(e.edge.Id) ? 1 : 0)
                    .ThenBy(e => rank != null && rank.TryGetValue(e.edge.Id, out var r) ? r : int.MaxValue)
                    .ThenBy(e => edgeFeature[e.edge.Id] == arrivingFeature ? 1 : 0)   // branches first
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

            if (endNode != null)
                TruncateAtEnd(tour, polylines, endNode.Value);

            // Reverse option: fly the whole tour in the opposite order/sense — a round trip
            // still starts and ends at the home-nearest endpoint (it's a circuit), just walked
            // the other way; a one-way trip then runs from its end back to the start.
            if (reverse)
            {
                tour.Reverse();
                foreach (var s in tour)
                    s.Direction = s.Direction == TraverseDir.Forward ? TraverseDir.Reverse : TraverseDir.Forward;
            }

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

        // The one-way end: a dead-end (one incident edge) other than the start — the one
        // nearest `wanted` if given, else the farthest from the start along the network.
        // Fills `pathEdges` with the edges of the shortest start→end path. Null if the
        // network has no reachable dead-end (a loop).
        private static (long, long)? ChooseEnd(
            Dictionary<(long, long), List<(Polyline edge, bool atStart)>> adjacency,
            Dictionary<(long, long), PointLatLngAlt> nodePoint,
            (long, long) start, PointLatLngAlt wanted, HashSet<int> pathEdges)
        {
            // Dijkstra over nodes, edge weight = polyline length.
            var dist = new Dictionary<(long, long), double> { [start] = 0 };
            var via = new Dictionary<(long, long), (Polyline edge, (long, long) from)>();
            var open = new List<(long, long)> { start };
            var done = new HashSet<(long, long)>();
            while (open.Count > 0)
            {
                var u = open.OrderBy(n => dist[n]).First();
                open.Remove(u);
                if (!done.Add(u)) continue;
                if (!adjacency.TryGetValue(u, out var incident)) continue;
                foreach (var (edge, atStart) in incident)
                {
                    var v = atStart ? Key(edge.Points[edge.Points.Count - 1]) : Key(edge.Points[0]);
                    double d = dist[u] + Length(edge);
                    if (!dist.TryGetValue(v, out var cur) || d < cur)
                    {
                        dist[v] = d;
                        via[v] = (edge, u);
                        if (!done.Contains(v)) open.Add(v);
                    }
                }
            }

            var leaves = adjacency.Where(kv => kv.Value.Count == 1 && !kv.Key.Equals(start) && dist.ContainsKey(kv.Key))
                                  .Select(kv => kv.Key).ToList();
            if (leaves.Count == 0) return null;

            (long, long) end = wanted != null
                ? leaves.OrderBy(n => nodePoint[n].GetDistance(wanted)).First()
                : leaves.OrderByDescending(n => dist[n]).First();

            for (var n = end; !n.Equals(start) && via.TryGetValue(n, out var step); n = step.from)
                pathEdges.Add(step.edge.Id);
            return end;
        }

        private static double Length(Polyline edge)
        {
            double len = 0;
            for (int i = 0; i + 1 < edge.Points.Count; i++) len += edge.Points[i].GetDistance(edge.Points[i + 1]);
            return len;
        }

        // Cut the walk at its last arrival at `end`, provided everything after that point is a
        // second traversal (the back-passes of the exit path). Edges left with one traversal
        // become one-way: flown once, on the centreline. If the tail isn't purely repeats
        // (only possible with cycles) the circuit is kept as is.
        private static void TruncateAtEnd(List<TourStep> tour, List<Polyline> polylines, (long, long) end)
        {
            var byId = polylines.ToDictionary(p => p.Id);
            (long, long) EndOf(TourStep s)
            {
                var pts = byId[s.PolylineId].Points;
                return Key(s.Direction == TraverseDir.Forward ? pts[pts.Count - 1] : pts[0]);
            }

            int last = -1;
            for (int i = 0; i < tour.Count; i++)
                if (EndOf(tour[i]).Equals(end)) last = i;
            if (last < 0 || last == tour.Count - 1) return;

            var seenBefore = new HashSet<int>(tour.Take(last + 1).Select(s => s.PolylineId));
            for (int i = last + 1; i < tour.Count; i++)
                if (!seenBefore.Contains(tour[i].PolylineId)) return;

            tour.RemoveRange(last + 1, tour.Count - last - 1);

            var count = tour.GroupBy(s => s.PolylineId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var s in tour)
                if (count[s.PolylineId] == 1) { s.OneWay = true; s.LaneOffsetM = 0; }
        }
    }
}
