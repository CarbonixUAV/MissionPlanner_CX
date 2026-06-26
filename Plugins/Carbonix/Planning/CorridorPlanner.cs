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
        //   [CornerCut,      FullOrbitThresholdDeg) : corner cut — straight chord across the corner
        //   [FullOrbit, ∞)                          : sharp turn — overfly + single exit orbit
        public double CornerCutThresholdDeg { get; set; } = 15;
        public double FullOrbitThresholdDeg { get; set; } = 60;

        // How far past the vertex to overfly before entering the sharp turn (metres).
        // Automatically clamped by the solver to the geometrically valid maximum.
        public double OverflyDistM { get; set; } = 100;

        // Orbit radius for sharp turns (overfly + single exit orbit).
        public double TurnRadiusM { get; set; } = 300;

        // Loiter radius for corner-cut inscribed circles (medium turns).
        public double CornerCutRadiusM { get; set; } = 150;

        // All fields are value types, so a shallow copy is a full copy.
        public CorridorParameters Clone() => (CorridorParameters)MemberwiseClone();
    }

    /// <summary>
    /// Stable identity for a planned vertex: which polyline it belongs to, and its
    /// index within that polyline's own vertex space. Replaces the overloaded
    /// CorridorVertexIndex + IsBranchVertex + BranchId trio (see
    /// .claude/corridor-tree-design.md).
    ///
    /// Pre-Tour-refactor, PolylineId is encoded from the legacy fields: the main
    /// line is <see cref="MainLine"/> (-1); each branch uses its BranchId. Each
    /// polyline owns its index space, so (PolylineId, Index) never collides — which
    /// is the whole point (it lets spur vertices be addressed without the
    /// main-line/branch index clash the legacy scheme had).
    /// </summary>
    public readonly struct VertexId : IEquatable<VertexId>
    {
        public const int MainLine = -1;

        public readonly int PolylineId;
        public readonly int Index;

        public VertexId(int polylineId, int index)
        {
            PolylineId = polylineId;
            Index = index;
        }

        public bool Equals(VertexId other) => PolylineId == other.PolylineId && Index == other.Index;
        public override bool Equals(object obj) => obj is VertexId other && Equals(other);
        public override int GetHashCode() => unchecked((PolylineId * 397) ^ Index);
        public static bool operator ==(VertexId a, VertexId b) => a.Equals(b);
        public static bool operator !=(VertexId a, VertexId b) => !a.Equals(b);
        public override string ToString() =>
            $"({(PolylineId == MainLine ? "main" : "branch" + PolylineId)}, {Index})";
    }

    public enum TraverseDir { Forward, Reverse }

    /// <summary>
    /// An independently-planned path. A spur is just a Polyline like any other; the
    /// <see cref="TourStep"/> list decides ordering and direction. See
    /// .claude/corridor-tree-design.md.
    /// </summary>
    public class Polyline
    {
        public int Id { get; set; }
        public List<PointLatLngAlt> Points { get; set; }
    }

    /// <summary>
    /// One step of a tour: traverse a polyline in a direction on a single offset lane.
    /// The out/back traversals of the tour, at opposite offsets, ARE the passes — flying
    /// a polyline Forward at +offset then Reverse at −offset is a 2-pass there-and-back
    /// (see .claude/corridor-tree-design.md).
    /// </summary>
    public class TourStep
    {
        public int PolylineId { get; set; }
        public TraverseDir Direction { get; set; }
        public double LaneOffsetM { get; set; }   // lateral offset of this lane (0 = centreline)
    }

    /// <summary>
    /// A user-inserted altitude checkpoint on a centreline segment (between vertices
    /// SegmentIndex and SegmentIndex+1 of the polyline, at fraction T along it). Spliced
    /// into the centreline at generation so it's flown by both passes; its identity is
    /// <see cref="VertexId"/>(PolylineId, Id), with Id kept clear of real vertex indices so
    /// inserting one never shifts another vertex's identity. Its altitude lives in the
    /// caller's alt-override store, keyed by that VertexId.
    /// </summary>
    public struct Checkpoint
    {
        public int PolylineId;
        public int SegmentIndex;
        public double T;
        public int Id;
    }

    public class CorridorWaypoint
    {
        public MAVLink.MAV_CMD Command { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }

        // AltAGL: target altitude above ground at this point (metres, user-editable)
        public double AltAGL { get; set; }

        // AltRelM: absolute AMSL altitude (metres) — written to mission. (Datum is sea level,
        // not home terrain; the "Rel" is historical.)
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

        // True for waypoints generated from a branch out-and-back detour.
        // When set, CorridorVertexIndex indexes into the branch's own point list
        // (BranchAttachment.Points), not the main line.
        public bool IsBranchVertex { get; set; }
        public int BranchId { get; set; } = -1;

        // True for the sharp-turn lead-in helpers (overfly + transfer). They're flown and
        // exported, but the elevation profile omits them — the turn is represented by the
        // loiter, and the preturn isn't a scan station worth showing.
        public bool IsTurnHelper { get; set; }

        // Stable identity, derived from the legacy fields for now (behaviour-neutral).
        // Later steps make this the stored identity and retire the three fields above.
        public VertexId Vertex =>
            new VertexId(IsBranchVertex ? BranchId : VertexId.MainLine, CorridorVertexIndex);
    }

    /// <summary>
    /// A branch/spur polyline attached to the main line at a junction vertex.
    /// The mission generator flies out to the branch tip (with a turnaround loiter)
    /// and back to the junction before continuing along the main line.
    /// </summary>
    public class BranchAttachment
    {
        // Root-first point list. Points[0] is snapped exactly onto
        // mainLine[MainLineVertexIndex].
        public List<PointLatLngAlt> Points { get; set; }

        // Index into mainLine (after any insertions) where this branch attaches.
        public int MainLineVertexIndex { get; set; }

        // True if attaching this branch required inserting a new mainLine vertex.
        public bool InsertedVertex { get; set; }

        // Stable identifier (load order) carried through to CorridorWaypoint/ElevationPoint.
        public int BranchId { get; set; }

        // Turn direction for the 180° turnaround loiter at the branch tip, chosen so
        // the racetrack bulges away from the main line.
        public bool PreferredTurnLeft { get; set; }
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

        // Absolute-AMSL samples from the floor/ceiling surface COGs at this point (NaN when
        // no surface is loaded or the point is outside coverage). The profile draws reference
        // lines at (surface + live MSA/ceiling offset); generation does not use these.
        public double FloorSurfaceAmsl { get; set; } = double.NaN;
        public double CeilingSurfaceAmsl { get; set; } = double.NaN;
        public bool IsLineWaypoint { get; set; }
        public int WaypointIndex { get; set; }       // corridor vertex index

        // Geographic position — used for map-viewport sync
        public double Lat { get; set; }
        public double Lng { get; set; }

        // Loiter arc visualisation
        public bool IsLoiterWaypoint { get; set; }
        public double LoiterArcLengthM { get; set; } // flown (primary) arc length
        public double LoiterRadiusM { get; set; }

        // Terrain fill points distributed around the loiter arc.
        // NOT draggable; provide terrain variation across the arc for rendering.
        public bool IsLoiterArcSample { get; set; }

        // Terrain fill points inserted between waypoints along straight legs.
        // Used ONLY for terrain fill and min/max band rendering; excluded from the
        // planned flight path (aircraft flies at constant AltRelM, not terrain-following).
        public bool IsLegTerrainSample { get; set; }

        // True for corridor vertices added interactively by the user (right-click on the
        // elevation profile).  These dots support X+Y dragging and show a remove option.
        public bool IsInserted { get; set; }

        // True for samples generated from a branch out-and-back detour. Read-only in
        // the elevation profile: excluded from alt-edit capture/restore and from
        // insert/move/remove handlers.
        public bool IsBranchVertex { get; set; }
        public int BranchId { get; set; } = -1;

        // Stable identity, derived from the legacy fields for now (behaviour-neutral).
        public VertexId Vertex =>
            new VertexId(IsBranchVertex ? BranchId : VertexId.MainLine, WaypointIndex);
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

        private enum TurnKind { CornerCut, DubinsStraightToOrbit }

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
        /// SHARP TURN: overfly past the vertex, fly a straight secant to the tangent point
        /// of a single exit orbit, then orbit out aligned with the outgoing leg.
        ///
        /// This is a Dubins S-turn with its entry circle collapsed to a straight leg. The
        /// full two-arc S reads as an overlapping-circle "flower" — worst at 180°
        /// turnarounds — and the secant's slight entry kink has most of an orbit to wash
        /// out before the leg we actually care about. The geometry is still solved as two
        /// tangent circles to locate the transfer point; only the entry arc is dropped.
        /// overflyDist is clamped to the geometrically valid maximum.
        /// </summary>
        private static TurnResult SolveDubinsTurn(
            Vec2 vertex, Vec2 dirIn, Vec2 dirOut,
            bool turnsLeft, double radius, double overflyDist)
        {
            bool c2CW = turnsLeft;

            // Perpendiculars: entry circle on inside of incoming, exit circle on outside.
            var perpIn  = Geom.Perp(dirIn,  turnsLeft ? 1 : -1);
            var perpOut = Geom.Perp(dirOut, turnsLeft ? -1 : 1);

            // Solve the dual-circle geometry for a given overfly distance; the transfer
            // point is the tangent between them, which the straight secant flies to.
            (Vec2 c2, Vec2 transfer, Vec2 entry, Vec2 exit)? Solve(double ofDist)
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
                return (c2, Geom.Midpoint(c1, c2), ofPt, Geom.Add(vertex, Geom.Scale(dirOut, d)));
            }

            // Max overfly = the single-circle tangent entry distance on the OUTSIDE of the
            // turn; that solution's entryT is the maximum useful overfly.
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

            var (c2r, transferPoint, entryPoint, exitPoint) = res.Value;

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
            public bool IsBranchVertex;
            public int BranchId = -1;
            public bool IsTurnHelper;

            // When set, terrain (hence altitude) is sampled here instead of at Position.
            // Used for loiters so the orbit targets the scan altitude at its EXIT, not at
            // the orbit centre (~the turn vertex, which reads like the entry/exit average).
            public Vec2? TerrainAnchor;
        }

        // Per-source-vertex metadata for the combined polyline.
        //   turnLeftOverride: when set, forces the turn direction at this vertex
        //   instead of deriving it from Cross(dirIn, dirOut) — used for branch-tip
        //   180° turnarounds where the cross product is ~0 and the natural side is
        //   ambiguous.
        private static List<CartCommand> GenerateMissionCartesian(
            Vec2[] poly,
            (int lineIdx, bool isLineVertex, int corridorVtxIdx, bool isBranchVertex, int branchId, bool? turnLeftOverride)[] meta,
            CorridorParameters p,
            double turnRadius,
            double cornerCutRadius)
        {
            var commands = new List<CartCommand>();
            if (poly.Length < 2) return commands;

            double cornerCutRad = p.CornerCutThresholdDeg * Deg2Rad;
            double fullOrbitRad = p.FullOrbitThresholdDeg * Deg2Rad;

            void AddWP(Vec2 pt, int srcIdx, bool isLineWp, Vec2? terrainAt = null, bool turnHelper = false)
            {
                commands.Add(new CartCommand
                {
                    Cmd = MAVLink.MAV_CMD.WAYPOINT,
                    Position = pt,
                    P1 = 0, P2 = 0, P3 = 0, P4 = 0,
                    IsLineWaypoint = isLineWp,
                    LineIndex = meta[srcIdx].lineIdx,
                    CorridorVertexIndex = isLineWp ? meta[srcIdx].corridorVtxIdx : -1,
                    IsBranchVertex = isLineWp && meta[srcIdx].isBranchVertex,
                    BranchId = isLineWp ? meta[srcIdx].branchId : -1,
                    TerrainAnchor = terrainAt,
                    IsTurnHelper = turnHelper,
                });
            }

            void AddLoiter(Vec2 center, double radius, bool clockwise, int srcIdx, double turns, Vec2 terrainAt)
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
                    IsBranchVertex = meta[srcIdx].isBranchVertex,
                    BranchId = meta[srcIdx].branchId,
                    TerrainAnchor = terrainAt,   // target scan altitude at the orbit exit
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
                bool turnsLeft = meta[i].turnLeftOverride ?? (Geom.Cross(dirIn, dirOut) > 0);

                // ── GENTLE: below cornerCutThreshold ──────────────────────────
                if (angle < cornerCutRad)
                {
                    AddWP(poly[i], i, true);
                    continue;
                }

                // ── CORNER CUT: between the two thresholds ────────────────────
                // Straight chord cut: fly to the entry cut point then straight across to the
                // exit cut point (no loiter arc). CornerCutRadius still sizes how far the cut
                // is pulled in from the vertex (via SolveCornerCut's tangent points).
                if (angle < fullOrbitRad)
                {
                    var turn = SolveCornerCut(poly[i], dirIn, dirOut, turnsLeft, cornerCutRadius);
                    if (turn == null) { AddWP(poly[i], i, true); continue; }

                    // Both cut points share the corner vertex's altitude (a flat chord at the
                    // corner's scan alt) — they're one editable station (same VertexId, linked
                    // drag), so anchor both terrain samples to the vertex to match.
                    AddWP(turn.EntryPoint, i, true, poly[i]);
                    AddWP(turn.ExitPoint, i, true, poly[i]);
                    continue;
                }

                // ── SHARP: overfly → straight secant → single exit orbit ──────
                {
                    var turn = SolveDubinsTurn(poly[i], dirIn, dirOut, turnsLeft,
                        turnRadius, p.OverflyDistM);

                    if (turn == null) { AddWP(poly[i], i, true); continue; }

                    // Entry/overfly waypoint, straight leg to the transfer point, then the
                    // single exit orbit. The transfer point sits off the flight line (its own
                    // terrain projection is unreliable), so anchor the whole turn block —
                    // entry, transfer, orbit — to the exit terrain: the turn holds the exit
                    // scan altitude and the climb falls on the approach leg (a ramp, not a
                    // step on the preturn).
                    AddWP(turn.EntryPoint, i, true, turn.ExitPoint, turnHelper: true);
                    AddWP(turn.TransferPoint.Value, i, false, turn.ExitPoint, turnHelper: true);
                    var c2 = turn.Loiters[0];
                    AddLoiter(c2.Center, c2.Radius, c2.Clockwise, i,
                        ArcTurns(turn.TransferPoint.Value, turn.ExitPoint, c2.Center, c2.Clockwise),
                        turn.ExitPoint);
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
            PointLatLngAlt homePoint,
            List<BranchAttachment> branches = null)
        {
            var result = new List<CorridorWaypoint>();
            if (centerLine == null || centerLine.Count < 2) return result;

            if (homePoint == null) homePoint = centerLine.First();

            // Altitudes are absolute AMSL: the vertical datum is sea level, not the home
            // terrain, so the home position never affects the generated altitudes.
            double homeTerrainAlt = 0;

            // 1. Generate offset flight lines (and each line's signed lateral offset).
            var (lines, offsets) = GenerateFlightLines(centerLine, p);

            // 2. Order lines (nearest-to-home end starts first, snake pattern).
            (lines, offsets) = OrderLines(lines, offsets, homePoint, p.ReverseDirection);

            // 3. Build a combined polyline by concatenating all flight lines.
            //    The turn at each inter-line junction is handled automatically
            //    by the same Dubins / corner-cut solver used for intra-line turns.
            //
            //    Track which corridor vertex (0..M-1) each polyline point came from,
            //    accounting for snake-ordering that may have reversed individual lines.
            //
            //    Branch out-and-back detours are spliced into the first ordered line
            //    (li == 0) only — later passes return along the main line and ignore
            //    branches.
            var combinedPts  = new List<PointLatLngAlt>();
            var combinedMeta = new List<(int lineIdx, bool isLineVertex, int corridorVtxIdx, bool isBranchVertex, int branchId, bool? turnLeftOverride)>();

            int M = centerLine.Count;
            var corrFirst = new PointLatLngAlt(centerLine[0].Lat, centerLine[0].Lng, 0);
            var corrLast  = new PointLatLngAlt(centerLine[M - 1].Lat, centerLine[M - 1].Lng, 0);

            Dictionary<int, List<BranchAttachment>> branchesByVertex = null;
            if (branches != null && branches.Count > 0)
                branchesByVertex = branches
                    .OrderBy(b => b.BranchId)
                    .GroupBy(b => b.MainLineVertexIndex)
                    .ToDictionary(g => g.Key, g => g.ToList());

            for (int li = 0; li < lines.Count; li++)
            {
                // Determine if this ordered line runs in the same direction as centerLine
                // by comparing its first point's distance to each corridor endpoint.
                var lineStart = lines[li][0];
                bool lineReversed = lineStart.GetDistance(corrLast) < lineStart.GetDistance(corrFirst);

                for (int vi = 0; vi < lines[li].Count; vi++)
                {
                    int corridorVtx = lineReversed ? M - 1 - vi : vi;
                    var pt = lines[li][vi];
                    combinedPts.Add(pt);
                    combinedMeta.Add((li, true, corridorVtx, false, -1, null));

                    if (li == 0 && branchesByVertex != null
                        && branchesByVertex.TryGetValue(corridorVtx, out var branchList))
                    {
                        foreach (var br in branchList)
                        {
                            var offsetBranch = Math.Abs(offsets[li]) > 1e-9
                                ? OffsetPolyline(br.Points, offsets[li])
                                : br.Points.Select(bp => new PointLatLngAlt(bp.Lat, bp.Lng, bp.Alt)).ToList();

                            // Snap the root exactly onto the just-emitted junction point
                            // so no spurious near-zero leg is introduced.
                            offsetBranch[0] = new PointLatLngAlt(pt.Lat, pt.Lng, pt.Alt);

                            int tipIdx = offsetBranch.Count - 1;

                            // Out: junction -> tip (tip gets the 180° turnaround).
                            for (int bi = 1; bi <= tipIdx; bi++)
                            {
                                bool isTip = bi == tipIdx;
                                combinedPts.Add(offsetBranch[bi]);
                                combinedMeta.Add((li, true, bi, true, br.BranchId,
                                    isTip ? br.PreferredTurnLeft : (bool?)null));
                            }

                            // Back: tip -> junction.
                            for (int bi = tipIdx - 1; bi >= 1; bi--)
                            {
                                combinedPts.Add(offsetBranch[bi]);
                                combinedMeta.Add((li, true, bi, true, br.BranchId, null));
                            }

                            // Re-emit the junction vertex so the branch-in -> mainline-out
                            // turn is computed independently of the mainline-in -> branch-out
                            // turn handled by the copy emitted above.
                            combinedPts.Add(pt);
                            combinedMeta.Add((li, true, corridorVtx, false, -1, null));
                        }
                    }
                }
            }

            // 5-7. Solve turns, project back to geo, sample terrain, build waypoints.
            //      Lanes project onto the centreline; branch detours keep their own terrain.
            return SolveAndBuildWaypoints(combinedPts, combinedMeta,
                (cmd, geo) => cmd.IsBranchVertex ? geo : NearestPointOnPolyline(geo, centerLine).point,
                p, homeTerrainAlt);
        }

        /// <summary>
        /// Shared tail of mission generation: take a combined geo polyline plus per-point
        /// metadata, solve the turns in cartesian space, project back to geo, sample
        /// terrain, and build the waypoint list. <paramref name="terrainQueryPoint"/> maps
        /// each solved command + its geo position to the point whose terrain to sample, so
        /// the caller decides the strategy (centreline projection for lanes;
        /// per-polyline for tours). Used by <see cref="GenerateMission"/> and the tour path
        /// (see .claude/corridor-tree-design.md).
        /// </summary>
        private static List<CorridorWaypoint> SolveAndBuildWaypoints(
            List<PointLatLngAlt> combinedPts,
            List<(int lineIdx, bool isLineVertex, int corridorVtxIdx, bool isBranchVertex, int branchId, bool? turnLeftOverride)> combinedMeta,
            Func<CartCommand, PointLatLngAlt, PointLatLngAlt> terrainQueryPoint,
            CorridorParameters p,
            double homeTerrainAlt)
        {
            var result = new List<CorridorWaypoint>();
            if (combinedPts.Count == 0) return result;

            // Flat-earth projection: lon/lat → local cartesian metres. Ref = first point.
            var refPt = combinedPts[0];
            double cosLat = Math.Cos(refPt.Lat * Deg2Rad);

            Vec2 ToCart(PointLatLngAlt geo) => new Vec2(
                (geo.Lng - refPt.Lng) * cosLat * 111319.5,
                (geo.Lat - refPt.Lat) * 111319.5);

            PointLatLngAlt FromCart(Vec2 v) => new PointLatLngAlt(
                refPt.Lat + v.Y / 111319.5,
                refPt.Lng + v.X / (cosLat * 111319.5),
                0);

            Vec2[] cartPts = combinedPts.Select(ToCart).ToArray();
            var    meta    = combinedMeta.ToArray();

            var commands = GenerateMissionCartesian(cartPts, meta, p, p.TurnRadiusM, p.CornerCutRadiusM);

            // Terrain (hence altitude) is sampled on the centreline so both lanes share one
            // profile. Turn helpers carry a TerrainAnchor (the orbit exit) so the turn block
            // targets the exit station — still projected to the centreline.
            double agl = p.DefaultAGL;   // target AGL over raw SRTM; floor/ceiling are manual, not clamped
            foreach (var cmd in commands)
            {
                var geo       = FromCart(cmd.Position);
                var terrGeo   = cmd.TerrainAnchor.HasValue ? FromCart(cmd.TerrainAnchor.Value) : geo;
                var terrQuery = terrainQueryPoint(cmd, terrGeo);
                double terr   = GetTerrainAlt(terrQuery.Lat, terrQuery.Lng);

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
                    IsBranchVertex      = cmd.IsBranchVertex,
                    BranchId            = cmd.BranchId,
                    IsTurnHelper        = cmd.IsTurnHelper,
                });
            }

            return result;
        }

        /// <summary>
        /// Tour-based generation. Builds the tour as ONE continuous centreline polyline,
        /// offsets the whole thing to a constant side, then runs the shared solve/terrain
        /// tail (<see cref="SolveAndBuildWaypoints"/>). A spur is just a polyline the tour
        /// visits — there is no separate branch concept. See .claude/corridor-tree-design.md.
        ///
        /// Offsetting a continuous centreline (rather than each edge separately) makes every
        /// junction a clean mitered corner automatically, and the out/back passes fall out
        /// of the constant-side offset: the path reverses at each dead-end, flipping the
        /// offset side. Dead-ends are capped as a single U-turn vertex for the solver.
        /// Terrain is sampled on each waypoint's own polyline centreline, so both passes
        /// share one altitude profile.
        /// </summary>
        public static List<CorridorWaypoint> GenerateMissionFromTour(
            List<Polyline> polylines, List<TourStep> tour, CorridorParameters p, PointLatLngAlt homePoint,
            IReadOnlyList<Checkpoint> checkpoints = null)
        {
            var empty = new List<CorridorWaypoint>();
            if (polylines == null || polylines.Count == 0 || tour == null || tour.Count == 0)
                return empty;

            var byId = polylines.ToDictionary(pl => pl.Id);
            if (homePoint == null) homePoint = byId[tour[0].PolylineId].Points.First();

            // Altitudes are absolute AMSL: the vertical datum is sea level, not the home
            // terrain, so the home position never affects the generated altitudes.
            double homeTerrainAlt = 0;

            // 1. The tour as one continuous centreline polyline (junctions shared, doubles
            //    back at dead-ends). 2. Offset the whole path at once.
            var centre = BuildCenterlineTour(byId, tour, checkpoints);
            if (centre.Count < 2) return empty;

            double off = (p.NumberOfPasses >= 2) ? p.PassOffsetM / 2.0 : 0.0;
            var (combinedPts, combinedMeta) = OffsetTourPath(centre, off);
            if (combinedPts.Count == 0) return empty;

            // 3. Terrain on each waypoint's own polyline centreline.
            PointLatLngAlt TerrainPoint(CartCommand cmd, PointLatLngAlt geo)
            {
                int plId = cmd.IsBranchVertex ? cmd.BranchId : VertexId.MainLine;
                var cl = byId.TryGetValue(plId, out var pl) ? pl.Points : byId[tour[0].PolylineId].Points;
                return NearestPointOnPolyline(geo, cl).point;
            }

            return SolveAndBuildWaypoints(combinedPts, combinedMeta, TerrainPoint, p, homeTerrainAlt);
        }

        // Walk the tour into one continuous centreline polyline. Each point carries its
        // VertexId (polyline + vertex index) and the tour-step it came from; a vertex is
        // flagged a dead-end when the next step reverses back along the same edge (a leaf
        // tip). Coincident junction points between steps are de-duplicated to one vertex.
        private static List<(PointLatLngAlt pt, VertexId vid, bool deadEnd, int step)> BuildCenterlineTour(
            Dictionary<int, Polyline> byId, List<TourStep> tour, IReadOnlyList<Checkpoint> checkpoints)
        {
            const double JoinDedupM = 0.5;
            var path = new List<(PointLatLngAlt pt, VertexId vid, bool deadEnd, int step)>();

            // Group inserted checkpoints by (edge, segment) for splicing during the walk.
            var bySeg = new Dictionary<(int edge, int seg), List<Checkpoint>>();
            if (checkpoints != null)
                foreach (var cp in checkpoints)
                {
                    var key = (cp.PolylineId, cp.SegmentIndex);
                    if (!bySeg.TryGetValue(key, out var lst)) bySeg[key] = lst = new List<Checkpoint>();
                    lst.Add(cp);
                }

            for (int s = 0; s < tour.Count; s++)
            {
                var step = tour[s];
                if (!byId.TryGetValue(step.PolylineId, out var poly) || poly.Points == null || poly.Points.Count < 2)
                    continue;

                int m = poly.Points.Count;
                bool fwd = step.Direction == TraverseDir.Forward;
                bool nextReversesSameEdge = s + 1 < tour.Count
                    && tour[s + 1].PolylineId == step.PolylineId
                    && tour[s + 1].Direction != step.Direction;

                for (int k = 0; k < m; k++)
                {
                    int vi = fwd ? k : m - 1 - k;
                    var src = poly.Points[vi];
                    bool dedup = k == 0 && path.Count > 0 && path[path.Count - 1].pt.GetDistance(src) < JoinDedupM;
                    if (!dedup)   // else: shared junction with the previous step
                    {
                        bool deadEnd = (k == m - 1) && nextReversesSameEdge;   // leaf tip
                        path.Add((new PointLatLngAlt(src.Lat, src.Lng, src.Alt),
                                  new VertexId(poly.Id, vi), deadEnd, s));
                    }

                    // Splice any checkpoints on the segment leaving this vertex toward the next
                    // walked vertex (mid-segment, so they never affect dead-end / junction logic).
                    if (k < m - 1 && bySeg.Count > 0)
                    {
                        int viNext = fwd ? vi + 1 : vi - 1;
                        if (bySeg.TryGetValue((poly.Id, Math.Min(vi, viNext)), out var segCps))
                        {
                            var a = poly.Points[vi];
                            var b = poly.Points[viNext];
                            foreach (var cp in segCps.OrderBy(c => fwd ? c.T : 1.0 - c.T))
                            {
                                double tw = fwd ? cp.T : 1.0 - cp.T;
                                var cpt = new PointLatLngAlt(
                                    a.Lat + tw * (b.Lat - a.Lat),
                                    a.Lng + tw * (b.Lng - a.Lng),
                                    0);
                                path.Add((cpt, new VertexId(poly.Id, cp.Id), false, s));
                            }
                        }
                    }
                }
            }
            return path;
        }

        // Offset the continuous centreline to one constant side (right of travel) by `off`,
        // mitering every corner. The path reverses at each dead-end, so out and back land on
        // opposite sides. A dead-end can't be mitered (180° reversal): it's emitted at the
        // centreline as a single U-turn vertex with a direction override, and the solver
        // makes the racetrack at the real turn radius.
        private static (List<PointLatLngAlt> pts, List<(int lineIdx, bool isLineVertex, int corridorVtxIdx, bool isBranchVertex, int branchId, bool? turnLeftOverride)> meta)
            OffsetTourPath(List<(PointLatLngAlt pt, VertexId vid, bool deadEnd, int step)> path, double off)
        {
            const double MaxMiterScale = 4.0;
            var pts  = new List<PointLatLngAlt>();
            var meta = new List<(int lineIdx, bool isLineVertex, int corridorVtxIdx, bool isBranchVertex, int branchId, bool? turnLeftOverride)>();

            int n = path.Count;
            for (int i = 0; i < n; i++)
            {
                var node = path[i];
                bool isBranch = node.vid.PolylineId != VertexId.MainLine;
                int branchId = isBranch ? node.vid.PolylineId : -1;

                if (node.deadEnd)
                {
                    // U-turn cap: tip at the centreline as a single ~180° turn. The out lane
                    // is on the right of the approach and the back lane on the left, so the
                    // racetrack is a left turn bulging past the tip.
                    pts.Add(new PointLatLngAlt(node.pt.Lat, node.pt.Lng, node.pt.Alt));
                    meta.Add((node.step, true, node.vid.Index, isBranch, branchId, true));
                    continue;
                }

                double offDir, scale = 1.0;
                if (i == 0)
                {
                    offDir = WrapBearing(node.pt.GetBearing(path[i + 1].pt) + 90.0);
                }
                else if (i == n - 1)
                {
                    offDir = WrapBearing(path[i - 1].pt.GetBearing(node.pt) + 90.0);
                }
                else
                {
                    double b1 = path[i - 1].pt.GetBearing(node.pt);
                    double b2 = node.pt.GetBearing(path[i + 1].pt);
                    offDir = WrapBearing(AverageBearing(b1, b2) + 90.0);
                    double cosHalf = Math.Cos(Math.Abs(NormalizeHeadingChange(b2 - b1)) * 0.5 * Deg2Rad);
                    scale = cosHalf > 1e-6 ? Math.Min(1.0 / cosHalf, MaxMiterScale) : MaxMiterScale;
                }

                var op = off > 1e-9 ? node.pt.newpos(offDir, off * scale)
                                    : new PointLatLngAlt(node.pt.Lat, node.pt.Lng, node.pt.Alt);
                op.Alt = node.pt.Alt;
                pts.Add(op);
                meta.Add((node.step, true, node.vid.Index, isBranch, branchId, null));
            }

            return (pts, meta);
        }

        // ════════════════════════════════════════════════════════════════════════
        // Flight line generation
        // ════════════════════════════════════════════════════════════════════════

        private static (List<List<PointLatLngAlt>> lines, List<double> offsets) GenerateFlightLines(
            List<PointLatLngAlt> center, CorridorParameters p)
        {
            int n = Math.Max(1, p.NumberOfPasses);
            bool hasCentre = (n % 2) == 1;
            int passesPerSide = n / 2;

            var lines = new List<List<PointLatLngAlt>>();
            var offsets = new List<double>();

            if (hasCentre)
            {
                lines.Add(center.Select(pt => new PointLatLngAlt(pt.Lat, pt.Lng, pt.Alt)).ToList());
                offsets.Add(0.0);
            }

            // Lateral passes at ±offset, ±2*offset, … (inner to outer, interleaved left/right)
            for (int i = 1; i <= passesPerSide; i++)
            {
                double offset = p.PassOffsetM * i;
                lines.Add(OffsetPolyline(center, -offset));
                offsets.Add(-offset);
                lines.Add(OffsetPolyline(center, +offset));
                offsets.Add(+offset);
            }

            return (lines, offsets);
        }

        // Parallel (miter) offset. The offset direction at each vertex is perpendicular to
        // the angle bisector; the distance is scaled by 1/cos(δ/2) (δ = heading change) so
        // BOTH adjacent segments end up exactly offsetM away — i.e. a true parallel offset
        // that preserves every corner angle. That keeps +offset and -offset lanes
        // classifying turns identically (a plain perpendicular shift distorts corners
        // asymmetrically and can flip the cut↔Dubins decision between lanes). Very sharp
        // corners are capped by a miter limit so the vertex doesn't shoot to infinity.
        internal static List<PointLatLngAlt> OffsetPolyline(
            List<PointLatLngAlt> pts, double offsetM)
        {
            const double MaxMiterScale = 4.0;

            var result = new List<PointLatLngAlt>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                double bearing;
                double scale = 1.0;
                if (i == 0)
                {
                    bearing = pts[0].GetBearing(pts[1]);
                }
                else if (i == pts.Count - 1)
                {
                    bearing = pts[i - 1].GetBearing(pts[i]);
                }
                else
                {
                    double b1 = pts[i - 1].GetBearing(pts[i]);
                    double b2 = pts[i].GetBearing(pts[i + 1]);
                    bearing = AverageBearing(b1, b2);
                    double cosHalf = Math.Cos(Math.Abs(NormalizeHeadingChange(b2 - b1)) * 0.5 * Deg2Rad);
                    scale = cosHalf > 1e-6 ? Math.Min(1.0 / cosHalf, MaxMiterScale) : MaxMiterScale;
                }

                double perpBearing = WrapBearing(bearing + 90.0);
                var newPt = pts[i].newpos(perpBearing, offsetM * scale);
                newPt.Alt = pts[i].Alt;
                result.Add(newPt);
            }
            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Line ordering
        // ════════════════════════════════════════════════════════════════════════

        private static (List<List<PointLatLngAlt>> lines, List<double> offsets) OrderLines(
            List<List<PointLatLngAlt>> lines, List<double> offsets, PointLatLngAlt home, bool reverse)
        {
            if (lines.Count == 0) return (lines, offsets);

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
                offsets.Reverse();
            }

            return (lines, offsets);
        }

        // ════════════════════════════════════════════════════════════════════════
        // Main line construction & branch attachment
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Nearest point on a polyline to <paramref name="query"/>, found by
        /// point-to-segment projection in a local flat-earth frame centred on
        /// <paramref name="query"/>. <paramref name="t"/> is the projection
        /// fraction [0,1] along segment <paramref name="segIndex"/>..segIndex+1.
        /// </summary>
        private static (PointLatLngAlt point, int segIndex, double t, double distM) NearestPointOnPolyline(
            PointLatLngAlt query, List<PointLatLngAlt> poly)
        {
            double cosLat = Math.Cos(query.Lat * Deg2Rad);

            Vec2 ToCart(PointLatLngAlt geo) => new Vec2(
                (geo.Lng - query.Lng) * cosLat * 111319.5,
                (geo.Lat - query.Lat) * 111319.5);

            PointLatLngAlt FromCart(Vec2 v) => new PointLatLngAlt(
                query.Lat + v.Y / 111319.5,
                query.Lng + v.X / (cosLat * 111319.5),
                0);

            var q = new Vec2(0, 0);

            double bestDistSq = double.MaxValue;
            int bestSeg = 0;
            double bestT = 0;
            Vec2 bestPt = ToCart(poly[0]);

            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vec2 a = ToCart(poly[i]);
                Vec2 b = ToCart(poly[i + 1]);
                Vec2 ab = Geom.Sub(b, a);
                double lenSq = Geom.Dot(ab, ab);
                double t = lenSq > 1e-9 ? Geom.Dot(Geom.Sub(q, a), ab) / lenSq : 0;
                t = Math.Max(0, Math.Min(1, t));
                Vec2 proj = Geom.Add(a, Geom.Scale(ab, t));
                double distSq = Geom.Dot(Geom.Sub(q, proj), Geom.Sub(q, proj));

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestSeg = i;
                    bestT = t;
                    bestPt = proj;
                }
            }

            return (FromCart(bestPt), bestSeg, bestT, Math.Sqrt(bestDistSq));
        }

        /// <summary>
        /// Chain main-line segment polylines into a single continuous main line by
        /// repeatedly joining whichever remaining segment has an endpoint within
        /// <paramref name="snapToleranceM"/> of either end of the growing chain.
        /// Segment order and orientation in <paramref name="segments"/> don't matter.
        /// Any segments that can't be connected are reported via
        /// <paramref name="unconnectedIndices"/> (indices into <paramref name="segments"/>)
        /// rather than throwing.
        /// </summary>
        public static List<PointLatLngAlt> BuildMainLine(
            List<List<PointLatLngAlt>> segments, out List<int> unconnectedIndices,
            double snapToleranceM = 20.0)
        {
            unconnectedIndices = new List<int>();
            if (segments == null || segments.Count == 0)
                return new List<PointLatLngAlt>();

            PointLatLngAlt Copy(PointLatLngAlt pt) => new PointLatLngAlt(pt.Lat, pt.Lng, pt.Alt);

            if (segments.Count == 1)
                return segments[0].Select(Copy).ToList();

            var chain = segments[0].Select(Copy).ToList();
            var remaining = Enumerable.Range(1, segments.Count - 1).ToList();

            bool progress = true;
            while (progress && remaining.Count > 0)
            {
                progress = false;
                var chainStart = chain.First();
                var chainEnd   = chain.Last();

                for (int ri = 0; ri < remaining.Count; ri++)
                {
                    var seg = segments[remaining[ri]];
                    var segStart = seg.First();
                    var segEnd   = seg.Last();

                    if (chainEnd.GetDistance(segStart) <= snapToleranceM)
                        chain.AddRange(seg.Skip(1).Select(Copy));
                    else if (chainEnd.GetDistance(segEnd) <= snapToleranceM)
                        chain.AddRange(seg.Take(seg.Count - 1).Reverse().Select(Copy));
                    else if (chainStart.GetDistance(segEnd) <= snapToleranceM)
                        chain.InsertRange(0, seg.Take(seg.Count - 1).Select(Copy));
                    else if (chainStart.GetDistance(segStart) <= snapToleranceM)
                        chain.InsertRange(0, seg.Skip(1).Reverse().Select(Copy));
                    else
                        continue;

                    remaining.RemoveAt(ri);
                    progress = true;
                    break;
                }
            }

            unconnectedIndices = remaining;
            return chain;
        }

        /// <summary>
        /// Snap each branch polyline onto <paramref name="mainLine"/> at the junction
        /// vertex nearest its root end (the end closer to the main line), inserting a
        /// new main-line vertex if the nearest point doesn't already coincide with one.
        /// <paramref name="mainLine"/> is mutated in place; branches that resolve to
        /// the same junction (within <paramref name="snapToleranceM"/>) naturally share
        /// that vertex since later branches are resolved against the already-updated
        /// main line.
        /// </summary>
        public static List<BranchAttachment> AttachBranches(
            ref List<PointLatLngAlt> mainLine,
            List<List<PointLatLngAlt>> branchSegments,
            double snapToleranceM = 10.0)
        {
            var result = new List<BranchAttachment>();
            if (mainLine == null || mainLine.Count < 2 || branchSegments == null)
                return result;

            PointLatLngAlt Copy(PointLatLngAlt pt) => new PointLatLngAlt(pt.Lat, pt.Lng, pt.Alt);

            for (int bId = 0; bId < branchSegments.Count; bId++)
            {
                var raw = branchSegments[bId];
                if (raw == null || raw.Count < 2) continue;

                var pts = raw.Select(Copy).ToList();

                // The endpoint nearer to mainLine is the root; reverse so Points[0]
                // is always the root.
                var nearStart = NearestPointOnPolyline(pts.First(), mainLine);
                var nearEnd   = NearestPointOnPolyline(pts.Last(), mainLine);
                var near = nearStart;
                if (nearEnd.distM < nearStart.distM)
                {
                    pts.Reverse();
                    near = nearEnd;
                }

                // Snap to an existing mainLine vertex if the projected point already
                // coincides with one; otherwise insert a new vertex for the junction.
                double distToA = mainLine[near.segIndex].GetDistance(near.point);
                double distToB = mainLine[near.segIndex + 1].GetDistance(near.point);

                int vertexIndex;
                bool insertedVertex = distToA > snapToleranceM && distToB > snapToleranceM;
                if (!insertedVertex)
                {
                    vertexIndex = distToA <= distToB ? near.segIndex : near.segIndex + 1;
                }
                else
                {
                    vertexIndex = near.segIndex + 1;
                    mainLine.Insert(vertexIndex, near.point);

                    // Shift previously-resolved attachments whose vertex index is at or
                    // after the insertion point.
                    foreach (var prior in result)
                        if (prior.MainLineVertexIndex >= vertexIndex)
                            prior.MainLineVertexIndex++;
                }

                // Snap the branch root exactly onto the junction coordinate.
                pts[0] = Copy(mainLine[vertexIndex]);

                result.Add(new BranchAttachment
                {
                    Points = pts,
                    MainLineVertexIndex = vertexIndex,
                    InsertedVertex = insertedVertex,
                    BranchId = bId,
                    PreferredTurnLeft = ComputePreferredTurnLeft(mainLine, vertexIndex, pts),
                });
            }

            return result;
        }

        /// <summary>
        /// Turn direction for the 180° turnaround loiter at a branch tip, chosen so the
        /// racetrack bulges away from <paramref name="mainLine"/> at the junction.
        /// </summary>
        private static bool ComputePreferredTurnLeft(
            List<PointLatLngAlt> mainLine, int vertexIndex, List<PointLatLngAlt> branchPts)
        {
            var junction = mainLine[vertexIndex];
            var mainNeighbor = vertexIndex + 1 < mainLine.Count
                ? mainLine[vertexIndex + 1]
                : mainLine[vertexIndex - 1];

            double cosLat = Math.Cos(junction.Lat * Deg2Rad);
            Vec2 ToCart(PointLatLngAlt geo) => new Vec2(
                (geo.Lng - junction.Lng) * cosLat * 111319.5,
                (geo.Lat - junction.Lat) * 111319.5);

            Vec2 branchDir = Geom.Direction(ToCart(branchPts[0]), ToCart(branchPts[1]));
            Vec2 toMain    = Geom.Direction(ToCart(junction), ToCart(mainNeighbor));

            return Geom.Cross(branchDir, toMain) < 0;
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
            CorridorParameters p, List<BranchAttachment> branches = null)
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
                TurnRadiusM           = p.TurnRadiusM,
                CornerCutRadiusM      = p.CornerCutRadiusM,
            };

            var wps = GenerateMission(corridorLine, centrelineParams, homePoint, branches);
            double homeTerrAlt = GetTerrainAlt(homePoint.Lat, homePoint.Lng);
            double agl = p.DefaultAGL;   // target AGL over raw SRTM; floor/ceiling are manual, not clamped

            // Build a lookup of loiter WPs keyed by (corridor vertex index, is-branch,
            // branch id) so we can attach arc sub-samples to the correct vertex below.
            // The tuple key avoids collisions between mainline vertex indices and
            // branch-internal point indices, which both start from 0.
            var loitersByVertex = new Dictionary<(int idx, bool isBranch, int branchId), CorridorWaypoint>();
            foreach (var wp in wps)
                if (wp.Command == MAVLink.MAV_CMD.LOITER_TURNS
                    && wp.CorridorVertexIndex >= 0
                    && wp.LoiterRadiusM > 0)
                    loitersByVertex[(wp.CorridorVertexIndex, wp.IsBranchVertex, wp.BranchId)] = wp;

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
                        IsBranchVertex   = wp.IsBranchVertex,
                        BranchId         = wp.BranchId,
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
                            IsBranchVertex    = wp.IsBranchVertex,
                            BranchId          = wp.BranchId,
                            Lat               = circumPt.Lat,
                            Lng               = circumPt.Lng,
                        });
                    }
                    cumDist += arcLen;
                }
                else if (wp.IsLineWaypoint && wp.CorridorVertexIndex >= 0 && !wp.IsBranchVertex
                         && !loitersByVertex.ContainsKey((wp.CorridorVertexIndex, false, -1)))
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
                else if (wp.IsLineWaypoint && wp.IsBranchVertex
                         && !loitersByVertex.ContainsKey((wp.CorridorVertexIndex, true, wp.BranchId)))
                {
                    // Branch out-and-back vertex — non-draggable dot, recomputed on
                    // every regeneration from terrain (never alt-edited in v1).
                    samples.Add(new ElevationPoint
                    {
                        DistM          = cumDist,
                        AltRelM        = wp.TerrainAltM - homeTerrAlt + agl,
                        TerrainAlt     = wp.TerrainAltM,
                        HomeTerrainAlt = homeTerrAlt,
                        IsLineWaypoint = true,
                        IsBranchVertex = true,
                        BranchId       = wp.BranchId,
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

        // Terrain source: absolute terrain altitude (metres) at lat/lng. Defaults to
        // SRTM; overridable so generation can be driven deterministically (tests, or
        // an alternate terrain source). Production behaviour is unchanged.
        public static Func<double, double, double> TerrainProvider { get; set; } = SrtmTerrainAlt;

        public static double GetTerrainAlt(double lat, double lng) => TerrainProvider(lat, lng);

        private static double SrtmTerrainAlt(double lat, double lng)
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
