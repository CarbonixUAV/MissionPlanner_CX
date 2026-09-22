using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Utilities;
using Newtonsoft.Json;

namespace Carbonix.Planning
{
    /// <summary>
    /// Everything the corridor planner form holds, as a plain JSON document, so a plan can
    /// be saved and reopened later to replan. Geometry is embedded (not referenced by path)
    /// because the Edit Path tab mutates it in place; profile edits are keyed by the same
    /// vertex identities the tour builder assigns from the features, so they line up again
    /// on reload as long as the features do. All values are internal units: metres, m/s,
    /// degrees.
    /// </summary>
    public class CorridorPlanFile
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string SavedUtc { get; set; }

        /// <summary>Flight Planner home at save time — informational; the tour roots at it.</summary>
        public double[] Home { get; set; }

        public List<FeatureFile> Features { get; set; } = new List<FeatureFile>();
        public List<Zone> Zones { get; set; } = new List<Zone>();
        public Parameters Params { get; set; } = new Parameters();

        public List<int> LegOrder { get; set; } = new List<int>();
        public List<VertexAlt> AltOverrides { get; set; } = new List<VertexAlt>();
        public List<VertexAlt> CornerCutAlts { get; set; } = new List<VertexAlt>();
        public List<Checkpoint> Checkpoints { get; set; } = new List<Checkpoint>();
        public List<LoiterToAlt> LoiterToAlts { get; set; } = new List<LoiterToAlt>();
        public int NextCheckpointId { get; set; } = 100000;

        public class FeatureFile
        {
            public string Name { get; set; }
            /// <summary>Each polyline as [lat, lng, alt] triples.</summary>
            public List<List<double[]>> Polylines { get; set; } = new List<List<double[]>>();
        }

        public class Zone
        {
            public string Name { get; set; }
            public double CeilingAglM { get; set; }
            public List<double[]> Ring { get; set; } = new List<double[]>();
        }

        public class Parameters
        {
            public double MinAGL { get; set; }
            public double MaxAGL { get; set; }
            public double DefaultAGL { get; set; }
            public double SpeedMs { get; set; }
            public int NumberOfPasses { get; set; }
            public double PassOffsetM { get; set; }
            public bool Reverse { get; set; }
            public double CornerCutThresholdDeg { get; set; }
            public double FullOrbitThresholdDeg { get; set; }
            public double OverflyDistM { get; set; }
            public double TurnRadiusM { get; set; }
            public double CornerCutRadiusM { get; set; }
            public double GradWarnPct { get; set; }
            public double GradMaxPct { get; set; }
        }

        public class VertexAlt
        {
            public int PolylineId { get; set; }
            public int Index { get; set; }
            public double AltM { get; set; }
        }

        // ── Conversions ─────────────────────────────────────────────────────────

        public static double[] ToArray(PointLatLngAlt p) => new[] { p.Lat, p.Lng, p.Alt };

        public static PointLatLngAlt ToPoint(double[] a) =>
            new PointLatLngAlt(a[0], a[1], a.Length > 2 ? a[2] : 0);

        public static List<VertexAlt> FromVertexAlts(Dictionary<VertexId, double> d) =>
            d.Select(kv => new VertexAlt { PolylineId = kv.Key.PolylineId, Index = kv.Key.Index, AltM = kv.Value })
             .ToList();

        public static Dictionary<VertexId, double> ToVertexAlts(List<VertexAlt> list)
        {
            var d = new Dictionary<VertexId, double>();
            if (list == null) return d;
            foreach (var v in list) d[new VertexId(v.PolylineId, v.Index)] = v.AltM;
            return d;
        }

        // ── JSON ────────────────────────────────────────────────────────────────

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>
        /// Parse a plan. Sections missing from the file come back empty rather than null, so
        /// an older or hand-trimmed file still loads.
        /// </summary>
        public static CorridorPlanFile FromJson(string json)
        {
            var plan = JsonConvert.DeserializeObject<CorridorPlanFile>(json);
            if (plan == null) throw new FormatException("Not a corridor plan file.");
            if (plan.Version > CurrentVersion)
                throw new FormatException(
                    $"Plan file version {plan.Version} is newer than this Mission Planner supports ({CurrentVersion}).");

            plan.Features = plan.Features ?? new List<FeatureFile>();
            plan.Zones = plan.Zones ?? new List<Zone>();
            plan.Params = plan.Params ?? new Parameters();
            plan.LegOrder = plan.LegOrder ?? new List<int>();
            plan.AltOverrides = plan.AltOverrides ?? new List<VertexAlt>();
            plan.CornerCutAlts = plan.CornerCutAlts ?? new List<VertexAlt>();
            plan.Checkpoints = plan.Checkpoints ?? new List<Checkpoint>();
            plan.LoiterToAlts = plan.LoiterToAlts ?? new List<LoiterToAlt>();
            foreach (var f in plan.Features) f.Polylines = f.Polylines ?? new List<List<double[]>>();
            foreach (var z in plan.Zones) z.Ring = z.Ring ?? new List<double[]>();
            return plan;
        }
    }
}
