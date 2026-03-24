using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner;
using MissionPlanner.Utilities;

namespace Carbonix.Planning
{
    public class CorridorParameters
    {
        // Altitude (always stored in metres internally)
        public double MinAGL { get; set; } = 50;
        public double MaxAGL { get; set; } = 120;
        public double DefaultAGL { get; set; } = 80;

        // Flight parameters
        public double SpeedMs { get; set; } = 25;

        // Pass layout
        // Odd NumberOfPasses → one pass on the centreline; even → no centreline pass.
        // Adjacent passes are separated by PassOffsetM.
        public int NumberOfPasses { get; set; } = 3;
        public double PassOffsetM { get; set; } = 100;

        // Mission options
        public bool ReverseDirection { get; set; } = false;

        // Turn thresholds (unsigned heading-change in degrees)
        //   [0,              CornerCutThresholdDeg) : gentle — plain waypoint
        //   [CornerCut,      FullOrbitThresholdDeg) : corner cut — inscribed loiter circle
        //   [FullOrbit, ∞)                          : Dubins S-turn — two tangent circles
        public double CornerCutThresholdDeg { get; set; } = 15;
        public double FullOrbitThresholdDeg { get; set; } = 60;

        // How far past the vertex to overfly before entering the S-turn (metres).
        // Automatically clamped by the solver to the geometrically valid maximum.
        public double OverflyDistM { get; set; } = 100;

        // If the Dubins entry-circle sweep is below this, replace it with a straight leg.
        public double MinDubinsArcDeg { get; set; } = 30;

        // Loiter radius for Dubins S-turns (sharp turns, two tangent circles).
        public double TurnRadiusM { get; set; } = 300;

        // Loiter radius for corner-cut inscribed circles (medium turns).
        public double CornerCutRadiusM { get; set; } = 150;
    }

    public class CorridorWaypoint
    {
        public MAVLink.MAV_CMD Command { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }

        // AltAGL: target altitude above ground at this point (metres, user-editable)
        public double AltAGL { get; set; }

        // AltRelM: altitude relative to home (metres) — written to mission
        public double AltRelM { get; set; }

        // Terrain altitude at this point (absolute, metres)
        public double TerrainAltM { get; set; }

        public float P1 { get; set; }
        public float P2 { get; set; }
        public float P3 { get; set; }
        public float P4 { get; set; }

        // True for data-collection waypoints (user can drag on elevation profile)
        public bool IsLineWaypoint { get; set; }
        public int LineIndex { get; set; }

        // Index of the corresponding corridor vertex (0..M-1, where M = corridorLine.Count).
        // Set to -1 for non-line waypoints (loiters between lines, etc.).
        public int CorridorVertexIndex { get; set; } = -1;

        // For LOITER_TURNS waypoints — used in elevation profile visualisation
        public double LoiterRadiusM { get; set; }
        public double LoiterTurns { get; set; }
    }

    /// <summary>
    /// A single point on the elevation profile chart.  Four kinds exist:
    ///   IsLineWaypoint    — draggable blue/green dot; represents a corridor vertex.
    ///   IsLoiterWaypoint  — draggable orange bar;     represents a loiter circle.
    ///   IsLoiterArcSample — terrain fill point around a loiter arc (not draggable).
    ///   IsLegTerrainSample — terrain fill point along a straight leg (not draggable).
    /// Exactly one of the four flags is set on any given instance.
    /// IsInserted may additionally be set on IsLineWaypoint points added interactively.
    /// </summary>
    public class ElevationPoint
    {
        public double DistM { get; set; }           // cumulative distance along path (m)
        /// <summary>
        /// Aircraft altitude relative to home (metres).  Authoritative Y-axis value
        /// used for display and export.  Updated directly by user altitude drags.
        /// </summary>
        public double AltRelM { get; set; }
        public double TerrainAlt { get; set; }       // absolute terrain alt (metres)
        public double HomeTerrainAlt { get; set; }
        public bool IsLineWaypoint { get; set; }
        public int WaypointIndex { get; set; }       // corridor vertex index

        // Geographic position — used for map-viewport sync
        public double Lat { get; set; }
        public double Lng { get; set; }

        // Loiter arc visualisation
        public bool IsLoiterWaypoint { get; set; }
        public double LoiterArcLengthM { get; set; } // 2π × r × turns
        public double LoiterRadiusM { get; set; }

        // Terrain fill points distributed around the loiter circumference.
        // NOT draggable; provide terrain variation across the arc for rendering.
        public bool IsLoiterArcSample { get; set; }

        // Terrain fill points inserted between waypoints along straight legs.
        // Used ONLY for terrain fill and min/max band rendering; excluded from the
        // planned flight path (aircraft flies at constant AltRelM, not terrain-following).
        public bool IsLegTerrainSample { get; set; }

        // True for corridor vertices added interactively by the user (right-click on the
        // elevation profile).  These dots support X+Y dragging and show a remove option.
        public bool IsInserted { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Main planner
    // ─────────────────────────────────────────────────────────────────────────────

    public static class CorridorPlanner
    {
        private const double Gravity = 9.81;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        // ════════════════════════════════════════════════════════════════════════
        // LAYER 1: Cartesian geometry — pure Vec2 math, no geo, no side-effects
        // ════════════════════════════════════════════════════════════════════════

        private readonly struct Vec2
        {
            public readonly double X;
            public readonly double Y;
            public Vec2(double x, double y) { X = x; Y = y; }
        }

        private static class Geom
        {
            public static Vec2 Normalize(Vec2 v)
            {
                double l = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                return l < 1e-12 ? new Vec2(0, 0) : new Vec2(v.X / l, v.Y / l);
            }

            public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

            /// <summary>2-D cross product (scalar z-component).</summary>
            public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

            public static Vec2 Add(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
            public static Vec2 Sub(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
            public static Vec2 Scale(Vec2 v, double s) => new Vec2(v.X * s, v.Y * s);
            public static Vec2 Midpoint(Vec2 a, Vec2 b) => new Vec2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            public static Vec2 Direction(Vec2 a, Vec2 b) => Normalize(Sub(b, a));

            /// <summary>Perpendicular to a unit direction. side &gt; 0 = left of travel, side &lt; 0 = right.</summary>
            public static Vec2 Perp(Vec2 dir, int side)
                => side > 0 ? new Vec2(-dir.Y, dir.X) : new Vec2(dir.Y, -dir.X);

            /// <summary>Unsigned angle between two unit vectors [0, π].</summary>
            public static double AngleBetween(Vec2 a, Vec2 b)
                => Math.Acos(Math.Max(-1.0, Math.Min(1.0, Dot(a, b))));

            /// <summary>Signed distance: project point onto line(origin, direction).</summary>
            public static double ProjectOntoLine(Vec2 point, Vec2 origin, Vec2 direction)
                => Dot(Sub(point, origin), direction);

            /// <summary>Signed arc sweep from startAngle to endAngle in the given rotation direction.</summary>
            public static double ArcSweep(double startAngle, double endAngle, bool clockwise)
            {
                double s = endAngle - startAngle;
                if (clockwise) { if (s > 0) s -= 2 * Math.PI; }
                else           { if (s < 0) s += 2 * Math.PI; }
                return s;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // LAYER 2: Turn solvers — pure geometry, no geo, no side-effects
        // Port of the JS solveTangentCircle / solveCornerCut / solveDubinsSTurn.
        // ════════════════════════════════════════════════════════════════════════

        private enum TurnKind { CornerCut, DubinsSTurn, DubinsStraightToOrbit }

        private struct LoiterInfo
        {
            public Vec2 Center;
            public double Radius;
            public bool Clockwise;
        }

        private class TurnResult
        {
            public TurnKind Kind;
            public Vec2 EntryPoint;   // WP to emit before the first loiter
            public Vec2 ExitPoint;    // where the aircraft leaves the last loiter
            public Vec2? TransferPoint; // midpoint between c1 and c2 (Dubins only)
            public List<LoiterInfo> Loiters; // 1 or 2 circles
        }

        // Candidate passed to the pick function inside SolveTangentCircle.
        private struct TCSol
        {
            public double D;       // offset along dirOut from vertex to exit tangent point
            public double EntryT;  // signed distance along dirIn from vertex to entry tangent
            public Vec2 Center;
        }

        // Full result of SolveTangentCircle (after pick + derivation of entry/exit points).
        private struct TCResult
        {
            public Vec2 Center;
            public Vec2 EntryPoint;  // vertex + dirIn  * EntryT
            public Vec2 ExitPoint;   // vertex + dirOut * D
            public double D;
            public double EntryT;
        }

        /// <summary>
        /// Find a circle of radius R tangent to the outgoing leg (on perpSide side),
        /// that is also tangent to the incoming leg (extended through the vertex).
        ///
        /// perpSide: +1 = left of dirOut, -1 = right of dirOut.
        /// pickFn receives the two quadratic solutions and returns the preferred one.
        /// Returns null when lines are (near-)parallel.
        /// </summary>
        private static TCResult? SolveTangentCircle(
            Vec2 vertex, Vec2 dirIn, Vec2 dirOut,
            double radius, int perpSide,
            Func<TCSol, TCSol, TCSol> pickFn)
        {
            var perpOut = Geom.Perp(dirOut, perpSide);
            double crossDO = Geom.Cross(dirOut, dirIn);
            double crossPO = Geom.Cross(perpOut, dirIn);

            if (Math.Abs(crossDO) < 1e-10) return null;

            double d1 = ( radius - radius * crossPO) / crossDO;
            double d2 = (-radius - radius * crossPO) / crossDO;

            TCSol MakeSol(double d)
            {
                var center = Geom.Add(vertex,
                    Geom.Add(Geom.Scale(dirOut, d), Geom.Scale(perpOut, radius)));
                return new TCSol
                {
                    D = d,
                    EntryT = Geom.ProjectOntoLine(center, vertex, dirIn),
                    Center = center,
                };
            }

            var s = pickFn(MakeSol(d1), MakeSol(d2));
            return new TCResult
            {
                Center = s.Center,
                EntryPoint = Geom.Add(vertex, Geom.Scale(dirIn, s.EntryT)),
                ExitPoint = Geom.Add(vertex, Geom.Scale(dirOut, s.D)),
                D = s.D,
                EntryT = s.EntryT,
            };
        }

        /// <summary>
        /// CORNER CUT: a single circle inscribed on the inside of the turn,
        /// tangent to both the incoming and outgoing legs.
        /// Trims the vertex — entry is behind vertex on incoming, exit is ahead on outgoing.
        /// </summary>
        private static TurnResult SolveCornerCut(
            Vec2 vertex, Vec2 dirIn, Vec2 dirOut, bool turnsLeft, double radius)
        {
            int insideSide = turnsLeft ? 1 : -1;
            bool clockwise = !turnsLeft;

            var sol = SolveTangentCircle(vertex, dirIn, dirOut, radius, insideSide,
                (a, b) =>
                {
                    // Want d > 0 (exit ahead on outgoing) and entryT < 0 (entry behind vertex).
                    if (a.D > b.D && a.EntryT < 0) return a;
                    if (b.D > a.D && b.EntryT < 0) return b;
                    return a.D > b.D ? a : b;
                });

            if (sol == null) return null;

            return new TurnResult
            {
                Kind = TurnKind.CornerCut,
                EntryPoint = sol.Value.EntryPoint,
                ExitPoint = sol.Value.ExitPoint,
                Loiters = new List<LoiterInfo>
                {
                    new LoiterInfo { Center = sol.Value.Center, Radius = radius, Clockwise = clockwise },
                },
            };
        }

        /// <summary>
        /// DUBINS S-TURN: two externally-tangent circles for sharp turns.
        ///   Circle 1 (entry): tangent to incoming leg at the overfly point, on inside of turn.
        ///   Circle 2 (exit):  tangent to outgoing leg, on outside of turn.
        ///   |c1 – c2| = 2R (externally tangent).
        ///
        /// If circle 1's sweep &lt; minArcDeg the entry arc is replaced with a straight leg.
        /// overflyDist is automatically clamped to the geometrically valid maximum.
        /// </summary>
        private static TurnResult SolveDubinsSTurn(
            Vec2 vertex, Vec2 dirIn, Vec2 dirOut,
            bool turnsLeft, double radius, double overflyDist, double minArcDeg)
        {
            bool c1CW = !turnsLeft;
            bool c2CW = turnsLeft;
            double minArcRad = minArcDeg * Deg2Rad;

            // Perpendiculars: circle 1 on inside of incoming, circle 2 on outside of outgoing.
            var perpIn  = Geom.Perp(dirIn,  turnsLeft ? 1 : -1);
            var perpOut = Geom.Perp(dirOut, turnsLeft ? -1 : 1);

            // Solve the dual-circle geometry for a given overfly distance.
            (Vec2 c1, Vec2 c2, Vec2 transfer, Vec2 entry, Vec2 exit)? Solve(double ofDist)
            {
                var ofPt = Geom.Add(vertex, Geom.Scale(dirIn, ofDist));
                var c1   = Geom.Add(ofPt, Geom.Scale(perpIn, radius));

                // c2 = vertex + dirOut*d + perpOut*R
                // |c1 – c2|² = (2R)²  →  quadratic in d
                var P  = Geom.Sub(c1, Geom.Add(vertex, Geom.Scale(perpOut, radius)));
                var B  = Geom.Scale(dirOut, -1);
                double qa = Geom.Dot(B, B);           // = 1 (B is unit length)
                double qb = 2 * Geom.Dot(P, B);
                double qc = Geom.Dot(P, P) - 4 * radius * radius;
                double disc = qb * qb - 4 * qa * qc;
                if (disc < 0) return null;

                double sq = Math.Sqrt(disc);
                // Pick the more-negative d to place c2 behind the vertex.
                double d = Math.Min((-qb + sq) / (2 * qa), (-qb - sq) / (2 * qa));
                var c2 = Geom.Add(vertex, Geom.Add(Geom.Scale(dirOut, d), Geom.Scale(perpOut, radius)));
                return (c1, c2,
                        Geom.Midpoint(c1, c2),
                        ofPt,
                        Geom.Add(vertex, Geom.Scale(dirOut, d)));
            }

            // Compute max overfly = the single-circle tangent entry distance on the
            // OUTSIDE of the turn.  The entryT of this solution is the maximum useful overfly.
            int outsideSide = turnsLeft ? -1 : 1;
            var singleSol = SolveTangentCircle(vertex, dirIn, dirOut, radius, outsideSide,
                (a, b) =>
                {
                    if (a.D < b.D && a.EntryT > 0) return a;
                    if (b.D < a.D && b.EntryT > 0) return b;
                    return a.EntryT > b.EntryT ? a : b;
                });

            double maxOverfly = singleSol.HasValue ? Math.Max(0, singleSol.Value.EntryT - 1) : 0;
            double clampedOverfly = Math.Max(0, Math.Min(overflyDist, maxOverfly));

            var res = Solve(clampedOverfly) ?? Solve(0);
            if (res == null) return null;

            var (c1r, c2r, transferPoint, entryPoint, exitPoint) = res.Value;

            // Check circle 1 sweep — skip it if too small.
            double a1Start = Math.Atan2(entryPoint.Y - c1r.Y, entryPoint.X - c1r.X);
            double a1End   = Math.Atan2(transferPoint.Y - c1r.Y, transferPoint.X - c1r.X);
            bool skipC1    = Math.Abs(Geom.ArcSweep(a1Start, a1End, c1CW)) < minArcRad;

            if (skipC1)
            {
                return new TurnResult
                {
                    Kind = TurnKind.DubinsStraightToOrbit,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    TransferPoint = transferPoint,
                    Loiters = new List<LoiterInfo>
                    {
                        new LoiterInfo { Center = c2r, Radius = radius, Clockwise = c2CW },
                    },
                };
            }

            return new TurnResult
            {
                Kind = TurnKind.DubinsSTurn,
                EntryPoint = entryPoint,
                ExitPoint = exitPoint,
                TransferPoint = transferPoint,
                Loiters = new List<LoiterInfo>
                {
                    new LoiterInfo { Center = c1r, Radius = radius, Clockwise = c1CW },
                    new LoiterInfo { Center = c2r, Radius = radius, Clockwise = c2CW },
                },
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        // LAYER 3: Cartesian mission generator
        // Port of JS generateMission(). Operates entirely in metres.
        // Input:  Vec2[] polyline + per-vertex metadata
        // Output: CartCommand[] (geo-independent mission commands)
        // ════════════════════════════════════════════════════════════════════════

        private class CartCommand
        {
            public MAVLink.MAV_CMD Cmd;
            public Vec2 Position;
            public float P1, P2, P3, P4;
            public bool IsLineWaypoint;
            public int LineIndex;
            public int CorridorVertexIndex = -1;
            public double LoiterRadiusM;
            public double LoiterTurns;
        }

        private static List<CartCommand> GenerateMissionCartesian(
            Vec2[] poly,
            (int lineIdx, bool isLineVertex, int corridorVtxIdx)[] meta,
            CorridorParameters p,
            double turnRadius,
            double cornerCutRadius)
        {
            var commands = new List<CartCommand>();
            if (poly.Length < 2) return commands;

            double cornerCutRad = p.CornerCutThresholdDeg * Deg2Rad;
            double fullOrbitRad = p.FullOrbitThresholdDeg * Deg2Rad;

            void AddWP(Vec2 pt, int srcIdx, bool isLineWp)
            {
                commands.Add(new CartCommand
                {
                    Cmd = MAVLink.MAV_CMD.WAYPOINT,
                    Position = pt,
                    P1 = 0, P2 = 0, P3 = 0, P4 = 0,
                    IsLineWaypoint = isLineWp,
                    LineIndex = meta[srcIdx].lineIdx,
                    CorridorVertexIndex = isLineWp ? meta[srcIdx].corridorVtxIdx : -1,
                });
            }

            void AddLoiter(Vec2 center, double radius, bool clockwise, int srcIdx, double turns)
            {
                float radiusSigned = clockwise ? (float)radius : -(float)radius;
                commands.Add(new CartCommand
                {
                    Cmd = MAVLink.MAV_CMD.LOITER_TURNS,
                    Position = center,
                    P1 = (float)turns,
                    P2 = 0,
                    P3 = radiusSigned,
                    P4 = 1,   // exit_tangent = 1: depart aligned with next WP heading
                    IsLineWaypoint = false,
                    LineIndex = meta[srcIdx].lineIdx,
                    CorridorVertexIndex = meta[srcIdx].corridorVtxIdx,
                    LoiterRadiusM = radius,
                    LoiterTurns = turns,
                });
            }

            for (int i = 0; i < poly.Length; i++)
            {
                // First and last points are always plain waypoints.
                if (i == 0 || i == poly.Length - 1)
                {
                    AddWP(poly[i], i, true);
                    continue;
                }

                var dirIn    = Geom.Direction(poly[i - 1], poly[i]);
                var dirOut   = Geom.Direction(poly[i], poly[i + 1]);
                double angle = Geom.AngleBetween(dirIn, dirOut);
                bool turnsLeft = Geom.Cross(dirIn, dirOut) > 0;

                // ── GENTLE: below cornerCutThreshold ──────────────────────────
                if (angle < cornerCutRad)
                {
                    AddWP(poly[i], i, true);
                    continue;
                }

                // ── CORNER CUT: between the two thresholds ────────────────────
                if (angle < fullOrbitRad)
                {
                    var turn = SolveCornerCut(poly[i], dirIn, dirOut, turnsLeft, cornerCutRadius);
                    if (turn == null) { AddWP(poly[i], i, true); continue; }

                    AddWP(turn.EntryPoint, i, true);
                    var cc = turn.Loiters[0];
                    AddLoiter(cc.Center, cc.Radius, cc.Clockwise, i,
                        ArcTurns(turn.EntryPoint, turn.ExitPoint, cc.Center, cc.Clockwise));
                    continue;
                }

                // ── SHARP: Dubins S-turn ──────────────────────────────────────
                {
                    var turn = SolveDubinsSTurn(poly[i], dirIn, dirOut, turnsLeft,
                        turnRadius, p.OverflyDistM, p.MinDubinsArcDeg);

                    if (turn == null) { AddWP(poly[i], i, true); continue; }

                    // Entry/overfly waypoint (plain WP on or near the flight line).
                    AddWP(turn.EntryPoint, i, true);

                    if (turn.Kind == TurnKind.DubinsStraightToOrbit)
                    {
                        // Straight leg to transfer point, then single exit orbit.
                        AddWP(turn.TransferPoint.Value, i, false);
                        var c2 = turn.Loiters[0];
                        AddLoiter(c2.Center, c2.Radius, c2.Clockwise, i,
                            ArcTurns(turn.TransferPoint.Value, turn.ExitPoint, c2.Center, c2.Clockwise));
                    }
                    else
                    {
                        // Full S-turn: entry orbit → exit orbit.
                        var c1 = turn.Loiters[0];
                        var c2 = turn.Loiters[1];
                        AddLoiter(c1.Center, c1.Radius, c1.Clockwise, i,
                            ArcTurns(turn.EntryPoint, turn.TransferPoint.Value, c1.Center, c1.Clockwise));
                        AddLoiter(c2.Center, c2.Radius, c2.Clockwise, i,
                            ArcTurns(turn.TransferPoint.Value, turn.ExitPoint, c2.Center, c2.Clockwise));
                    }
                }
            }

            return commands;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Public helpers
        // ════════════════════════════════════════════════════════════════════════

        public static (double totalDistM, double timeSec)
            CalculateStats(List<CorridorWaypoint> waypoints, CorridorParameters p)
        {
            double dist = 0;
            PointLatLngAlt prev = null;
            foreach (var wp in waypoints)
            {
                var pt = new PointLatLngAlt(wp.Lat, wp.Lng, 0);
                if (prev != null)
                {
                    if (wp.Command == MAVLink.MAV_CMD.LOITER_TURNS && wp.LoiterRadiusM > 0)
                        dist += 2 * Math.PI * wp.LoiterRadiusM * wp.LoiterTurns;
                    else
                        dist += prev.GetDistance(pt);
                }
                prev = pt;
            }
            double timeS = p.SpeedMs > 0 ? dist / p.SpeedMs : 0;
            return (dist, timeS);
        }

        // ════════════════════════════════════════════════════════════════════════
        // Main entry point
        // ════════════════════════════════════════════════════════════════════════

        public static List<CorridorWaypoint> GenerateMission(
            List<PointLatLngAlt> centerLine,
            CorridorParameters p,
            PointLatLngAlt homePoint)
        {
            var result = new List<CorridorWaypoint>();
            if (centerLine == null || centerLine.Count < 2) return result;

            if (homePoint == null) homePoint = centerLine.First();

            double homeTerrainAlt = GetTerrainAlt(homePoint.Lat, homePoint.Lng);

            // 1. Generate offset flight lines.
            var lines = GenerateFlightLines(centerLine, p);

            // 2. Order lines (nearest-to-home end starts first, snake pattern).
            lines = OrderLines(lines, homePoint, p.ReverseDirection);

            // 3. Build a combined polyline by concatenating all flight lines.
            //    The turn at each inter-line junction is handled automatically
            //    by the same Dubins / corner-cut solver used for intra-line turns.
            //
            //    Track which corridor vertex (0..M-1) each polyline point came from,
            //    accounting for snake-ordering that may have reversed individual lines.
            var combinedPts  = new List<PointLatLngAlt>();
            var combinedMeta = new List<(int lineIdx, bool isLineVertex, int corridorVtxIdx)>();

            int M = centerLine.Count;
            var corrFirst = new PointLatLngAlt(centerLine[0].Lat, centerLine[0].Lng, 0);
            var corrLast  = new PointLatLngAlt(centerLine[M - 1].Lat, centerLine[M - 1].Lng, 0);

            for (int li = 0; li < lines.Count; li++)
            {
                // Determine if this ordered line runs in the same direction as corridorLine
                // by comparing its first point's distance to each corridor endpoint.
                var lineStart = lines[li][0];
                bool lineReversed = lineStart.GetDistance(corrLast) < lineStart.GetDistance(corrFirst);

                for (int vi = 0; vi < lines[li].Count; vi++)
                {
                    int corridorVtx = lineReversed ? M - 1 - vi : vi;
                    combinedPts.Add(lines[li][vi]);
                    combinedMeta.Add((li, true, corridorVtx));
                }
            }

            // 5. Flat-earth projection: lon/lat → local cartesian metres.
            //    Reference = first point of the combined polyline.
            var refPt   = combinedPts[0];
            double cosLat = Math.Cos(refPt.Lat * Deg2Rad);

            Vec2 ToCart(PointLatLngAlt geo) => new Vec2(
                (geo.Lng - refPt.Lng) * cosLat * 111319.5,
                (geo.Lat - refPt.Lat) * 111319.5);

            PointLatLngAlt FromCart(Vec2 v) => new PointLatLngAlt(
                refPt.Lat + v.Y / 111319.5,
                refPt.Lng + v.X / (cosLat * 111319.5),
                0);

            Vec2[] cartPts = combinedPts.Select(ToCart).ToArray();
            var    meta    = combinedMeta.ToArray();  // (lineIdx, isLineVertex, corridorVtxIdx)

            // 6. Generate the mission command sequence in cartesian space.
            var commands = GenerateMissionCartesian(cartPts, meta, p, p.TurnRadiusM, p.CornerCutRadiusM);

            // 7. Project back to geo coordinates, look up terrain, compute altitudes.
            double agl = Clamp(p.DefaultAGL, p.MinAGL, p.MaxAGL);
            foreach (var cmd in commands)
            {
                var geo     = FromCart(cmd.Position);
                double terr = GetTerrainAlt(geo.Lat, geo.Lng);

                result.Add(new CorridorWaypoint
                {
                    Command             = cmd.Cmd,
                    Lat                 = geo.Lat,
                    Lng                 = geo.Lng,
                    AltAGL              = agl,
                    AltRelM             = terr - homeTerrainAlt + agl,
                    TerrainAltM         = terr,
                    P1 = cmd.P1,
                    P2 = cmd.P2,
                    P3 = cmd.P3,
                    P4 = cmd.P4,
                    IsLineWaypoint      = cmd.IsLineWaypoint,
                    LineIndex           = cmd.LineIndex,
                    CorridorVertexIndex = cmd.CorridorVertexIndex,
                    LoiterRadiusM       = cmd.LoiterRadiusM,
                    LoiterTurns         = cmd.LoiterTurns,
                });
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Flight line generation
        // ════════════════════════════════════════════════════════════════════════

        private static List<List<PointLatLngAlt>> GenerateFlightLines(
            List<PointLatLngAlt> center, CorridorParameters p)
        {
            int n = Math.Max(1, p.NumberOfPasses);
            bool hasCentre = (n % 2) == 1;
            int passesPerSide = n / 2;

            var lines = new List<List<PointLatLngAlt>>();

            if (hasCentre)
                lines.Add(center.Select(pt => new PointLatLngAlt(pt.Lat, pt.Lng, pt.Alt)).ToList());

            // Lateral passes at ±offset, ±2*offset, … (inner to outer, interleaved left/right)
            for (int i = 1; i <= passesPerSide; i++)
            {
                double offset = p.PassOffsetM * i;
                lines.Add(OffsetPolyline(center, -offset));
                lines.Add(OffsetPolyline(center, +offset));
            }

            return lines;
        }

        private static List<PointLatLngAlt> OffsetPolyline(
            List<PointLatLngAlt> pts, double offsetM)
        {
            var result = new List<PointLatLngAlt>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                double bearing;
                if (i == 0)
                    bearing = pts[0].GetBearing(pts[1]);
                else if (i == pts.Count - 1)
                    bearing = pts[i - 1].GetBearing(pts[i]);
                else
                    bearing = AverageBearing(pts[i - 1].GetBearing(pts[i]),
                                            pts[i].GetBearing(pts[i + 1]));

                double perpBearing = WrapBearing(bearing + 90.0);
                var newPt = pts[i].newpos(perpBearing, offsetM);
                newPt.Alt = pts[i].Alt;
                result.Add(newPt);
            }
            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Line ordering
        // ════════════════════════════════════════════════════════════════════════

        private static List<List<PointLatLngAlt>> OrderLines(
            List<List<PointLatLngAlt>> lines, PointLatLngAlt home, bool reverse)
        {
            if (lines.Count == 0) return lines;

            // Orient the first line so the closer end is the start.
            var firstLine = lines[0];
            if (home.GetDistance(firstLine.Last()) < home.GetDistance(firstLine.First()))
                firstLine.Reverse();

            // Snake: orient each subsequent line so its start is near the previous end.
            for (int i = 1; i < lines.Count; i++)
            {
                var prevEnd = lines[i - 1].Last();
                var cur     = lines[i];
                if (prevEnd.GetDistance(cur.Last()) < prevEnd.GetDistance(cur.First()))
                    cur.Reverse();
            }

            if (reverse)
            {
                foreach (var line in lines) line.Reverse();
                lines.Reverse();
            }

            return lines;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Terrain profile sampling
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build the elevation-profile point list for the centreline flight path.
        ///
        /// Leg terrain is sampled directly along the raw <paramref name="corridorLine"/>
        /// (straight lines between corridor vertices) rather than along the Dubins mission
        /// path, so the terrain profile always reflects ground under the planning centreline
        /// rather than ground under approach/exit tangent legs or loiter-centre offsets.
        ///
        /// Loiter arc data (anchor + circumference sub-points) still comes from the
        /// single-pass GenerateMission result so turn geometry is accurate.
        ///
        /// Every sample interval is 25 m — legs and arcs alike — with no sample cap.
        ///
        /// Must be called on a background thread (calls GetTerrainAlt which sleeps
        /// while SRTM tiles load).
        /// </summary>
        public static List<ElevationPoint> BuildElevationProfile(
            List<PointLatLngAlt> corridorLine, PointLatLngAlt homePoint,
            CorridorParameters p)
        {
            if (corridorLine == null || corridorLine.Count < 1)
                return new List<ElevationPoint>();

            // Generate a centreline-only mission to obtain loiter geometry at each corner.
            // GenerateMission also warms the SRTM cache for this area, making subsequent
            // GetTerrainAlt calls near-instant.
            var centrelineParams = new CorridorParameters
            {
                MinAGL                = p.MinAGL,
                MaxAGL                = p.MaxAGL,
                DefaultAGL            = p.DefaultAGL,
                SpeedMs               = p.SpeedMs,
                NumberOfPasses        = 1,
                PassOffsetM           = 0,
                ReverseDirection      = p.ReverseDirection,
                CornerCutThresholdDeg = p.CornerCutThresholdDeg,
                FullOrbitThresholdDeg = p.FullOrbitThresholdDeg,
                OverflyDistM          = p.OverflyDistM,
                MinDubinsArcDeg       = p.MinDubinsArcDeg,
                TurnRadiusM           = p.TurnRadiusM,
                CornerCutRadiusM      = p.CornerCutRadiusM,
            };

            var wps = GenerateMission(corridorLine, centrelineParams, homePoint);
            double homeTerrAlt = GetTerrainAlt(homePoint.Lat, homePoint.Lng);
            double agl = Clamp(p.DefaultAGL, p.MinAGL, p.MaxAGL);

            // Build a lookup of loiter WPs keyed by corridor vertex index so we can
            // attach arc sub-samples to the correct corridor vertex below.
            var loitersByVertex = new Dictionary<int, CorridorWaypoint>();
            foreach (var wp in wps)
                if (wp.Command == MAVLink.MAV_CMD.LOITER_TURNS
                    && wp.CorridorVertexIndex >= 0
                    && wp.LoiterRadiusM > 0)
                    loitersByVertex[wp.CorridorVertexIndex] = wp;

            const double SampleSpacingM = 25.0;

            var samples = new List<ElevationPoint>(corridorLine.Count * 20);
            double cumDist = 0;
            PointLatLngAlt prevGeo = null;

            foreach (var wp in wps)
            {
                var geoNow = new PointLatLngAlt(wp.Lat, wp.Lng, 0);
                bool isLoiter = wp.Command == MAVLink.MAV_CMD.LOITER_TURNS && wp.LoiterRadiusM > 0;

                if (prevGeo != null && !isLoiter)
                {
                    // Leg between two non-loiter WPs: the aircraft actually flies this path.
                    // Legs TO a loiter center are excluded (isLoiter check above) because the
                    // aircraft enters the loiter tangentially, not by flying to the center.
                    double legDist = prevGeo.GetDistance(geoNow);
                    int nInterp = Math.Max(0, (int)(legDist / SampleSpacingM) - 1);
                    for (int k = 1; k <= nInterp; k++)
                    {
                        double t         = (double)k / (nInterp + 1);
                        double interpLat = prevGeo.Lat + t * (geoNow.Lat - prevGeo.Lat);
                        double interpLng = prevGeo.Lng + t * (geoNow.Lng - prevGeo.Lng);
                        double terrInter = GetTerrainAlt(interpLat, interpLng);
                        samples.Add(new ElevationPoint
                        {
                            DistM              = cumDist + legDist * t,
                            AltRelM            = terrInter - homeTerrAlt + agl,
                            TerrainAlt         = terrInter,
                            HomeTerrainAlt     = homeTerrAlt,
                            WaypointIndex      = -1,
                            IsLegTerrainSample = true,
                            Lat                = interpLat,
                            Lng                = interpLng,
                        });
                    }
                    cumDist += legDist;
                }

                if (isLoiter)
                {
                    double arcLen     = 2.0 * Math.PI * wp.LoiterRadiusM * wp.LoiterTurns;
                    double terrCenter = wp.TerrainAltM;
                    double aircraftAltRelHome = terrCenter - homeTerrAlt + agl;

                    // Loiter anchor (the draggable bar).
                    samples.Add(new ElevationPoint
                    {
                        DistM            = cumDist,
                        AltRelM          = aircraftAltRelHome,
                        TerrainAlt       = terrCenter,
                        HomeTerrainAlt   = homeTerrAlt,
                        IsLoiterWaypoint = true,
                        LoiterArcLengthM = arcLen,
                        LoiterRadiusM    = wp.LoiterRadiusM,
                        WaypointIndex    = wp.CorridorVertexIndex,
                        Lat              = wp.Lat,
                        Lng              = wp.Lng,
                    });

                    // Arc sub-samples at 25 m spacing around the fraction actually flown.
                    var loiterGeo  = new PointLatLngAlt(wp.Lat, wp.Lng, 0);
                    int nSub       = Math.Max(4, (int)(arcLen / SampleSpacingM));
                    double arcSpan = 360.0 * wp.LoiterTurns;
                    for (int k = 1; k <= nSub; k++)
                    {
                        double bearing = k * arcSpan / nSub;
                        var circumPt   = loiterGeo.newpos(bearing, wp.LoiterRadiusM);
                        samples.Add(new ElevationPoint
                        {
                            DistM             = cumDist + arcLen * k / nSub,
                            AltRelM           = aircraftAltRelHome,
                            TerrainAlt        = GetTerrainAlt(circumPt.Lat, circumPt.Lng),
                            HomeTerrainAlt    = homeTerrAlt,
                            IsLoiterArcSample = true,
                            WaypointIndex     = wp.CorridorVertexIndex,
                            Lat               = circumPt.Lat,
                            Lng               = circumPt.Lng,
                        });
                    }
                    cumDist += arcLen;
                }
                else if (wp.IsLineWaypoint && wp.CorridorVertexIndex >= 0
                         && !loitersByVertex.ContainsKey(wp.CorridorVertexIndex))
                {
                    // Straight-through corridor vertex — draggable dot.
                    // Entry WPs immediately before a loiter share the same CorridorVertexIndex
                    // as the loiter and are intentionally skipped here; the loiter bar
                    // represents that vertex in the profile.
                    samples.Add(new ElevationPoint
                    {
                        DistM          = cumDist,
                        AltRelM        = wp.TerrainAltM - homeTerrAlt + agl,
                        TerrainAlt     = wp.TerrainAltM,
                        HomeTerrainAlt = homeTerrAlt,
                        IsLineWaypoint = true,
                        WaypointIndex  = wp.CorridorVertexIndex,
                        Lat            = wp.Lat,
                        Lng            = wp.Lng,
                    });
                }

                prevGeo = geoNow;
            }

            return samples;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Utility
        // ════════════════════════════════════════════════════════════════════════

        public static double GetTerrainAlt(double lat, double lng)
        {
            var r = srtm.getAltitude(lat, lng);
            if (r.currenttype == srtm.tiletype.valid) return r.alt;
            if (r.currenttype == srtm.tiletype.ocean) return 0.0;

            for (int i = 0; i < 15; i++)
            {
                System.Threading.Thread.Sleep(200);
                r = srtm.getAltitude(lat, lng);
                if (r.currenttype == srtm.tiletype.valid) return r.alt;
                if (r.currenttype == srtm.tiletype.ocean) return 0.0;
            }

            return 0.0;
        }

        /// <summary>
        /// Signed arc from <paramref name="from"/> to <paramref name="to"/> around
        /// <paramref name="center"/>, expressed as a fraction of a full circle.
        /// Always positive (absolute value of the sweep).
        /// </summary>
        private static double ArcTurns(Vec2 from, Vec2 to, Vec2 center, bool clockwise)
        {
            double a0 = Math.Atan2(from.Y - center.Y, from.X - center.X);
            double a1 = Math.Atan2(to.Y   - center.Y, to.X   - center.X);
            return Math.Abs(Geom.ArcSweep(a0, a1, clockwise)) / (2 * Math.PI);
        }

        public static double NormalizeHeadingChange(double delta)
        {
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;
            return delta;
        }

        public static double WrapBearing(double b)
        {
            while (b >= 360) b -= 360;
            while (b < 0) b += 360;
            return b;
        }

        private static double AverageBearing(double b1, double b2)
        {
            double diff = NormalizeHeadingChange(b2 - b1);
            return WrapBearing(b1 + diff / 2.0);
        }

        public static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));

    }
}
