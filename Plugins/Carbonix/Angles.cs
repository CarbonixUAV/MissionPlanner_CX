using System;

namespace Carbonix
{
    /// <summary>
    /// Bearing arithmetic shared across the plugin.
    /// </summary>
    static class Angles
    {
        /// <summary>
        /// Wraps to (0, 360] - aviation reads north as 360, not 000, and
        /// that holds for headings, runways and wind alike. Round before
        /// wrapping when the result is headed for a display, or a bearing a
        /// hair under north rounds up into 000 on its way out.
        /// </summary>
        /// <remarks>
        /// Never returns 0. Use <see cref="Wrap180"/> for "how far off is
        /// this" comparisons, where zero is the answer you want.
        /// </remarks>
        public static double Wrap360(double degrees)
        {
            degrees %= 360;
            return degrees <= 0 ? degrees + 360 : degrees;
        }

        static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Nearest compass octant for a bearing in degrees.</summary>
        public static string Compass(double bearing)
        {
            return CompassPoints[(int)Math.Round(Wrap360(bearing) / 45.0) % 8];
        }
    }
}
