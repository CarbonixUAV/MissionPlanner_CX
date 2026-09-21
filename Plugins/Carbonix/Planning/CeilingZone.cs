using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MissionPlanner.Utilities;

namespace Carbonix.Planning
{
    /// <summary>
    /// One area of an operating approval: a closed polygon with the AGL ceiling approved
    /// inside it. The ceiling is an offset over the corridor planner's ceiling surface, the
    /// same quantity as the form's global Ceiling field — zones just let it vary by position
    /// (a blanket area at 120 m with islands approved to 300 m).
    /// </summary>
    public class CeilingZone
    {
        public string Name { get; set; }

        /// <summary>Outer boundary. Closure is optional (a repeated first point is harmless).</summary>
        public List<PointLatLngAlt> Ring { get; }

        /// <summary>Approved ceiling, metres AGL over the ceiling surface.</summary>
        public double CeilingAglM { get; set; }

        public CeilingZone(string name, IEnumerable<PointLatLngAlt> ring, double ceilingAglM)
        {
            Name = name;
            Ring = ring.ToList();
            CeilingAglM = ceilingAglM;
        }

        /// <summary>
        /// Even-odd ray cast in the lat/lng plane. Approval areas are a few km across at most,
        /// so treating degrees as planar puts the boundary out by centimetres, not metres.
        /// </summary>
        public bool Contains(double lat, double lng)
        {
            var r = Ring;
            int n = r.Count;
            if (n < 3) return false;

            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = r[i].Lat, xi = r[i].Lng;
                double yj = r[j].Lat, xj = r[j].Lng;
                if ((yi > lat) != (yj > lat) &&
                    lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// The ceiling that applies at a point: the highest among the zones containing it, so
        /// an island of higher approval inside a blanket area wins. Null when no zone contains
        /// the point — there is no approval there at all.
        /// </summary>
        public static double? CeilingAt(IEnumerable<CeilingZone> zones, double lat, double lng)
        {
            double? best = null;
            foreach (var z in zones)
            {
                if (!z.Contains(lat, lng)) continue;
                if (best == null || z.CeilingAglM > best.Value) best = z.CeilingAglM;
            }
            return best;
        }

        // An altitude with a unit somewhere in the name: "Area B 300m", "North (1000 ft)",
        // "Site 120 m AGL". A bare number ("Zone 3") is not an altitude.
        private static readonly Regex NameAltitude = new Regex(
            @"(?<![A-Za-z0-9.])(\d+(?:\.\d+)?)\s*(m|metres?|meters?|ft|feet)(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Ceiling in metres carried by a feature name, or null when it has none.</summary>
        public static double? ParseCeilingFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = NameAltitude.Match(name);
            if (!m.Success) return null;

            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string unit = m.Groups[2].Value.ToLowerInvariant();
            return unit.StartsWith("f") ? v * 0.3048 : v;
        }
    }
}
