using System;

namespace Carbonix
{
    /// <summary>
    /// Provides bearing arithmetic shared across the plugin.
    /// </summary>
    static class Angles
    {
        /// <summary>
        /// Wraps an angle to (0, 360], so north reads as 360 rather than 000.
        /// </summary>
        /// <remarks>
        /// Round before wrapping a value headed for display, or a bearing just
        /// under north rounds up to 000. Use <see cref="Wrap180"/> for signed
        /// differences, where zero is a valid answer.
        /// </remarks>
        public static double Wrap360(double degrees)
        {
            degrees %= 360;
            return degrees <= 0 ? degrees + 360 : degrees;
        }

        /// <summary>
        /// Wraps an angle to a signed difference in (-180, 180].
        /// </summary>
        public static double Wrap180(double degrees)
        {
            degrees = Wrap360(degrees);
            return degrees > 180 ? degrees - 360 : degrees;
        }

        static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Returns the nearest eight-point compass direction for a bearing in degrees.</summary>
        public static string Compass(double bearing)
        {
            return CompassPoints[(int)Math.Round(Wrap360(bearing) / 45.0) % 8];
        }
    }
}
