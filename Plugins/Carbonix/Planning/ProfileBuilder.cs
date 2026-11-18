using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.Mission;

namespace Carbonix.Planning
{
    /// <summary>A positioned mission item as the elevation profile sees it.</summary>
    public class ProfileWaypoint
    {
        // Caller's identity for the item (e.g. its grid row), carried onto its samples.
        public int Id { get; set; }
        public MAVLink.MAV_CMD Command { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public double AltM { get; set; }           // AMSL
        public double TerrainAltM { get; set; }    // AMSL
        public float P1 { get; set; }
        public float P2 { get; set; }
        public float P3 { get; set; }
        public float P4 { get; set; }
    }

    /// <summary>
    /// A single point on the elevation profile chart. Four kinds exist:
    ///   IsLineWaypoint     — draggable dot; a waypoint.
    ///   IsLoiterWaypoint   — draggable bar; a loiter circle (its flown arc).
    ///   IsLoiterArcSample  — terrain point around a loiter arc (not draggable).
    ///   IsLegTerrainSample — terrain point along a straight leg (not draggable).
    /// Exactly one of the four flags is set on any given instance.
    /// IsInserted may additionally be set on IsLineWaypoint points added interactively.
    /// </summary>
    public class ElevationPoint
    {
        public double DistM { get; set; }            // cumulative distance along path (m)
        // Aircraft altitude (metres) on the chart's datum (HomeTerrainAlt = 0 makes it AMSL).
        // Updated directly by altitude drags. (The "Rel" is historical.)
        public double AltRelM { get; set; }
        public double TerrainAlt { get; set; }       // absolute terrain alt (metres)
        public double HomeTerrainAlt { get; set; }

        // Absolute-AMSL samples from the floor/ceiling surface COGs at this point (NaN when
        // no surface is loaded or the point is outside coverage). The profile draws reference
        // lines at (surface + live MSA/ceiling offset).
        public double FloorSurfaceAmsl { get; set; } = double.NaN;
        public double CeilingSurfaceAmsl { get; set; } = double.NaN;

        public bool IsLineWaypoint { get; set; }
        public int WaypointIndex { get; set; }       // owning ProfileWaypoint.Id

        public double Lat { get; set; }
        public double Lng { get; set; }

        public bool IsLoiterWaypoint { get; set; }
        public double LoiterArcLengthM { get; set; } // flown arc length
        public double LoiterRadiusM { get; set; }

        // Loiter-to-alt: a NON-flat spiral. AltRelM is the target (end) altitude; the spiral
        // ramps from LtaStartAltRelM (the lead-in altitude) to it across the arc.
        public bool IsLoiterToAlt { get; set; }
        public double LtaStartAltRelM { get; set; }

        public bool IsLoiterArcSample { get; set; }

        // Along straight legs, for terrain and band checks only; the planned path is straight
        // between waypoints.
        public bool IsLegTerrainSample { get; set; }

        // Waypoints added interactively: X+Y draggable, with a remove option.
        public bool IsInserted { get; set; }
    }

    public static class ProfileBuilder
    {
        public const double SampleSpacingM = 25.0;

        // Terrain source: absolute terrain altitude (metres) at lat/lng. SRTM by default;
        // tests swap it for a deterministic one.
        public static Func<double, double, double> TerrainProvider { get; set; } = SrtmTerrainAlt;

        public static double GetTerrainAlt(double lat, double lng) => TerrainProvider(lat, lng);

        private static double SrtmTerrainAlt(double lat, double lng)
        {
            // Tiles load in the background; wait up to 3 s for one.
            for (int i = 0; ; i++)
            {
                var r = srtm.getAltitude(lat, lng);
                if (r.currenttype == srtm.tiletype.valid) return r.alt;
                if (r.currenttype == srtm.tiletype.ocean || i == 15) return 0.0;
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Samples the flown ground track of <paramref name="wps"/> every 25 m: a dot per
        /// waypoint, a bar + arc samples per loiter (its flown arc; a full turn for a
        /// loiter-to-alt, ramping from the previous waypoint's altitude), and terrain samples
        /// along each straight leg, the last landing on the next station. Uses the map's
        /// segmentizer so the track matches what's drawn. Calls SRTM: run off the UI thread.
        /// </summary>
        public static List<ElevationPoint> Build(List<ProfileWaypoint> wps, PointLatLngAlt home, double homeTerrainAlt)
        {
            var samples = new List<ElevationPoint>();
            if (wps == null || wps.Count == 0) return samples;

            var locationWps = wps.Select(wp => new Locationwp
            {
                id = (ushort)wp.Command, p1 = wp.P1, p2 = wp.P2, p3 = wp.P3, p4 = wp.P4,
                lat = wp.Lat, lng = wp.Lng, alt = (float)wp.AltM,
            }).ToList();
            var graph = MissionGraph.Create(new PointLatLngAlt(home.Lat, home.Lng, 0), locationWps);
            var segments = MissionSegmentizer.BuildSegments(graph, VehicleClass.Plane, 0);

            // Primary (flown) segments by their start node (= index into wps).
            var straightByFrom = new Dictionary<int, MissionSegmentizer.Segment>();
            var loiterByNode = new Dictionary<int, MissionSegmentizer.Segment>();
            foreach (var s in segments)
            {
                if (s.StartNode == null || (s.Flags & SegmentFlags.Alternate) != 0) continue;
                if (s.Kind == SegmentKind.LoiterArc)
                {
                    if (!loiterByNode.ContainsKey(s.StartNode.MissionIndex))
                        loiterByNode[s.StartNode.MissionIndex] = s;
                }
                else if (s.EndNode != null && !straightByFrom.ContainsKey(s.StartNode.MissionIndex))
                {
                    straightByFrom[s.StartNode.MissionIndex] = s;
                }
            }

            double cum = 0;

            ElevationPoint Sample(double lat, double lng, double alt, ProfileWaypoint owner) => new ElevationPoint
            {
                DistM = cum,
                AltRelM = alt,
                TerrainAlt = GetTerrainAlt(lat, lng),
                HomeTerrainAlt = homeTerrainAlt,
                WaypointIndex = owner.Id,
                Lat = lat,
                Lng = lng,
            };

            foreach (var node in graph.Nodes)
            {
                int idx = node.MissionIndex;
                if (idx < 0 || idx >= wps.Count) continue;
                var wp = wps[idx];

                if (loiterByNode.TryGetValue(idx, out var arc) && arc.Path != null && arc.Path.Count >= 2)
                {
                    bool isLta = wp.Command == MAVLink.MAV_CMD.LOITER_TO_ALT;
                    double radiusParam = isLta ? wp.P2 : wp.P3;   // signed: negative = CCW
                    double radius = Math.Abs(radiusParam);
                    double sign = radiusParam >= 0 ? 1.0 : -1.0;
                    var center = new PointLatLngAlt(wp.Lat, wp.Lng, 0);
                    double entry = center.GetBearing(arc.Path[0]);
                    double exit = center.GetBearing(arc.Path[arc.Path.Count - 1]);
                    // Flown arc, [0, 360). The segmentizer only renders a capture arc for a
                    // loiter-to-alt, so it gets a full turn to show the spiral.
                    double sweep = isLta ? 360.0 : ((sign * (exit - entry)) % 360 + 360) % 360;
                    double arcLen = 2.0 * Math.PI * radius * sweep / 360.0;
                    double altEnd = wp.AltM;
                    double altStart = isLta && idx > 0 ? wps[idx - 1].AltM : altEnd;

                    var bar = Sample(wp.Lat, wp.Lng, altEnd, wp);
                    bar.TerrainAlt = wp.TerrainAltM;
                    bar.IsLoiterWaypoint = true;
                    bar.IsLoiterToAlt = isLta;
                    bar.LtaStartAltRelM = altStart;
                    bar.LoiterRadiusM = radius;
                    bar.LoiterArcLengthM = arcLen;
                    samples.Add(bar);

                    double cum0 = cum;
                    int n = Math.Max(2, (int)(arcLen / SampleSpacingM));
                    for (int k = 1; k <= n; k++)
                    {
                        double f = (double)k / n;
                        var pt = center.newpos(entry + sign * sweep * f, radius);
                        cum = cum0 + arcLen * f;
                        var s = Sample(pt.Lat, pt.Lng, altStart + (altEnd - altStart) * f, wp);
                        s.IsLoiterArcSample = true;
                        s.IsLoiterToAlt = isLta;
                        s.LtaStartAltRelM = altStart;
                        samples.Add(s);
                    }
                    cum = cum0 + arcLen;
                }
                else
                {
                    var dot = Sample(wp.Lat, wp.Lng, wp.AltM, wp);
                    dot.TerrainAlt = wp.TerrainAltM;
                    dot.IsLineWaypoint = true;
                    samples.Add(dot);
                }

                if (straightByFrom.TryGetValue(idx, out var seg) && seg.Path != null && seg.Path.Count >= 2)
                {
                    var a = seg.Path[0];
                    var b = seg.Path[seg.Path.Count - 1];
                    double dist = a.GetDistance(b), cum0 = cum;
                    int n = Math.Max(1, (int)(dist / SampleSpacingM));
                    for (int k = 1; k <= n; k++)
                    {
                        double t = (double)k / n;
                        cum = cum0 + dist * t;
                        var s = Sample(a.Lat + t * (b.Lat - a.Lat), a.Lng + t * (b.Lng - a.Lng), wp.AltM, wp);
                        s.IsLegTerrainSample = true;
                        samples.Add(s);
                    }
                    cum = cum0 + dist;
                }
            }
            return samples;
        }
    }
}
