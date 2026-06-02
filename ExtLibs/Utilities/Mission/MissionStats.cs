using System;
using System.Collections.Generic;
using static MissionPlanner.Utilities.Mission.CommandUtils;

namespace MissionPlanner.Utilities.Mission
{
    /// <summary>
    /// Represents distance statistics computed from a mission's <see cref="MissionGraph"/>.
    /// </summary>
    /// <remarks>
    /// DO_JUMP loops are counted, with nested loops multiplying. ArduPilot only
    /// multiplies nested loops from 4.7 on; this assumes that behavior for all versions.
    /// </remarks>
    public sealed class MissionStats
    {
        /// <summary>
        /// Gets the total horizontal distance flown, in meters.
        /// </summary>
        public double TotalDistance { get; }

        /// <summary>
        /// Gets a value indicating whether the mission never terminates.
        /// </summary>
        /// <remarks>
        /// True for an infinite jump loop (negative repeat) or a LOITER_UNLIM;
        /// <see cref="TotalDistance"/> then reflects a single pass rather than an
        /// unbounded total.
        /// </remarks>
        public bool HasInfiniteLoop { get; }

        private MissionStats(double totalDistance, bool hasInfiniteLoop)
        {
            TotalDistance = totalDistance;
            HasInfiniteLoop = hasInfiniteLoop;
        }

        /// <summary>
        /// Gets the statistics of an empty mission (zero distance).
        /// </summary>
        public static MissionStats Empty { get; } = new MissionStats(0, false);

        /// <summary>
        /// Computes statistics from a mission graph and its rendered segments.
        /// </summary>
        /// <param name="graph">The mission graph.</param>
        /// <param name="segments">The rendered segments built for <paramref name="graph"/>.</param>
        /// <param name="loiterRadius">The default loiter radius, in meters, applied when a loiter command does not specify one.</param>
        /// <returns>The computed statistics.</returns>
        public static MissionStats Compute(MissionGraph graph, List<MissionSegmentizer.Segment> segments,
            double loiterRadius)
        {
            // Ordinal of each node along the sequential chain (Nodes is in mission order)
            var ordinal = new Dictionary<MissionNode, int>();
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                ordinal[graph.Nodes[i]] = i;
            }

            // Length of each leg, keyed by endpoints, from the first segment drawn
            // between those nodes (the segmentizer emits the primary shape first).
            // Jump legs are included: they are drawn as alternates and can be
            // splines or arcs, not just straight chords.
            var legLength = new Dictionary<(MissionNode, MissionNode), double>();
            var arcLoiterNodes = new HashSet<MissionNode>();
            var loiterTransitArc = new Dictionary<MissionNode, double>();
            foreach (var seg in segments)
            {
                if (seg.Kind == SegmentKind.LoiterArc && seg.StartNode != null)
                {
                    arcLoiterNodes.Add(seg.StartNode);
                    // The primary loiter arc is the entry->exit transit around the circle
                    if (!seg.Flags.HasFlag(SegmentFlags.Alternate) && !loiterTransitArc.ContainsKey(seg.StartNode))
                    {
                        loiterTransitArc[seg.StartNode] = PathLength(seg.Path);
                    }
                }
                if (seg.StartNode == null || seg.EndNode == null)
                {
                    continue;
                }
                var key = (seg.StartNode, seg.EndNode);
                if (!legLength.ContainsKey(key))
                {
                    legLength[key] = PathLength(seg.Path);
                }
            }

            // Collect backward jumps as node-ordinal loops [Lo, Hi] with their
            // repeat count. Only backward jumps re-fly a section of the path.
            var loops = new List<Loop>();
            bool hasInfinite = false;
            foreach (var edge in graph.Edges)
            {
                if (!edge.IsJump)
                {
                    continue;
                }
                int from = ordinal[edge.FromNode];
                int to = ordinal[edge.ToNode];
                if (to >= from)
                {
                    continue; // forward jump: skips ahead, does not form a loop
                }
                int repeat = edge.JumpRepeat ?? 0;
                if (repeat < 0)
                {
                    hasInfinite = true;
                    repeat = 0; // count a single pass for an infinite loop
                }
                loops.Add(new Loop { Lo = to, Hi = from, Repeat = repeat });
            }

            double total = 0;

            // Leg from home to each path start. That is the mission's first node,
            // plus any node nothing leads into (e.g. a region orphaned after a
            // terminal): the operator presumably reaches it from home, like a start.
            if (graph.Home != null && graph.Home != PointLatLngAlt.Zero && graph.Nodes.Count > 0)
            {
                var start = graph.Nodes[0];
                foreach (var node in graph.Nodes)
                {
                    if (node != start && node.IncomingEdges.Count != 0)
                    {
                        continue;
                    }
                    var pos = ResolvePosition(node, graph.Home);
                    if (pos != null)
                    {
                        total += graph.Home.GetDistance(pos);
                    }
                }
            }

            // Transit legs. Every edge is summed with its loop multiplier. There
            // is no reachability analysis: unreachable regions (skipped by a
            // forward jump, or left after a terminal) are counted on purpose, on
            // the assumption the operator means to reach them (e.g. by jumping there).
            foreach (var edge in graph.Edges)
            {
                double length;
                if (legLength.TryGetValue((edge.FromNode, edge.ToNode), out var segLen))
                {
                    length = segLen;
                }
                else
                {
                    // Edge the segmentizer didn't draw (e.g. a jump out of a
                    // terminal node, or an endpoint with no usable location):
                    // best-effort straight chord, zero when a position is missing.
                    length = EdgeChord(edge, graph.Home);
                }
                if (length <= 0)
                {
                    continue;
                }

                double multiplier;
                if (edge.IsJump)
                {
                    int from = ordinal[edge.FromNode];
                    int to = ordinal[edge.ToNode];
                    if (to >= from)
                    {
                        multiplier = 1; // forward jump: leg flown once
                    }
                    else
                    {
                        int repeat = edge.JumpRepeat ?? 0;
                        if (repeat < 0)
                        {
                            repeat = 0;
                        }
                        // The jump leg is flown 'repeat' times for each entry into
                        // the loops that strictly enclose it.
                        multiplier = repeat * EnclosingProduct(loops, to, from);
                    }
                }
                else
                {
                    // Sequential edge between consecutive nodes (k, k+1).
                    multiplier = ContainingProduct(loops, ordinal[edge.FromNode]);
                }

                total += length * multiplier;
            }

            // Loiter dwell: distance spent circling, added per visit. Only loiters
            // the segmentizer drew as an arc count. Each visit flies the transit
            // arc plus any whole turns; a sub-1-turn LOITER_TURNS is just the arc.
            foreach (var node in graph.Nodes)
            {
                var cmd = node.Command;
                if (!IsLoiter(cmd.id))
                {
                    continue;
                }
                if (cmd.id == (ushort)MAVLink.MAV_CMD.LOITER_UNLIM)
                {
                    hasInfinite = true; // never terminates, regardless of vehicle class
                }
                if (!arcLoiterNodes.Contains(node))
                {
                    continue; // not flown as a circle on this vehicle
                }
                double radius = Math.Abs(LoiterRadius(cmd, loiterRadius));
                double transitArc = loiterTransitArc.TryGetValue(node, out var arc) ? arc : 0;
                double dwellPerVisit = LoiterFullTurns(cmd) * 2 * Math.PI * radius + transitArc;
                if (dwellPerVisit <= 0)
                {
                    continue;
                }
                double visits = ContainingProduct(loops, ordinal[node], inclusiveEnd: true);
                total += dwellPerVisit * visits;
            }

            return new MissionStats(total, hasInfinite);
        }

        struct Loop
        {
            public int Lo;     // jump target ordinal (loop start)
            public int Hi;     // jump source ordinal (loop end)
            public int Repeat; // number of times the jump is taken
        }

        /// <summary>
        /// Whole circles charged as loiter dwell, on top of the entry->exit
        /// transit arc. LOITER_TURNS uses its turn count, but only when it is a
        /// full turn or more (a sub-1-turn loiter is just the arc); the other
        /// loiter types are treated as a single turn.
        /// </summary>
        static double LoiterFullTurns(Locationwp cmd)
        {
            switch (cmd.id)
            {
                case (ushort)MAVLink.MAV_CMD.LOITER_TURNS:
                    return cmd.p1 >= 1.0 ? cmd.p1 : 0.0;
                case (ushort)MAVLink.MAV_CMD.LOITER_TIME:
                case (ushort)MAVLink.MAV_CMD.LOITER_TO_ALT:
                case (ushort)MAVLink.MAV_CMD.LOITER_UNLIM:
                    return 1.0;
                default:
                    return 0.0;
            }
        }

        /// <summary>
        /// Product of (Repeat + 1) over every loop whose body contains ordinal
        /// <paramref name="k"/>. With <paramref name="inclusiveEnd"/> false this
        /// counts the sequential edge at k (covering k..k+1); with it true it
        /// counts node k itself (how many times the node is visited).
        /// </summary>
        static double ContainingProduct(List<Loop> loops, int k, bool inclusiveEnd = false)
        {
            double product = 1;
            foreach (var loop in loops)
            {
                bool inside = inclusiveEnd ? (loop.Lo <= k && loop.Hi >= k) : (loop.Lo <= k && loop.Hi > k);
                if (inside)
                {
                    product *= loop.Repeat + 1;
                }
            }
            return product;
        }

        /// <summary>
        /// Product of (Repeat + 1) over every loop that strictly encloses the
        /// interval [lo, hi] (a wider loop), counting how many times a nested
        /// jump leg is reached.
        /// </summary>
        static double EnclosingProduct(List<Loop> loops, int lo, int hi)
        {
            double product = 1;
            foreach (var loop in loops)
            {
                if (loop.Lo <= lo && loop.Hi >= hi && (loop.Lo < lo || loop.Hi > hi))
                {
                    product *= loop.Repeat + 1;
                }
            }
            return product;
        }

        static double PathLength(List<PointLatLngAlt> path)
        {
            if (path == null || path.Count < 2)
            {
                return 0;
            }
            double d = 0;
            for (int i = 1; i < path.Count; i++)
            {
                d += path[i - 1].GetDistance(path[i]);
            }
            return d;
        }

        static double EdgeChord(MissionEdge edge, PointLatLngAlt home)
        {
            var a = ResolvePosition(edge.FromNode, home);
            var b = ResolvePosition(edge.ToNode, home);
            if (a == null || b == null)
            {
                return 0;
            }
            return a.GetDistance(b);
        }

        /// <summary>
        /// Resolves a node's physical position: takeoffs use their launch point
        /// (home or a preceding land), positional commands use their lat/lng, and
        /// nodes without a usable location (RTL, loiter-at-current) return null.
        /// </summary>
        static PointLatLngAlt ResolvePosition(MissionNode node, PointLatLngAlt home)
        {
            var cmd = node.Command;
            if (IsTakeoff(cmd.id))
            {
                return GetTakeoffLocation(node, home);
            }
            if (HasLocation(cmd))
            {
                return new PointLatLngAlt(cmd);
            }
            return null;
        }
    }
}
