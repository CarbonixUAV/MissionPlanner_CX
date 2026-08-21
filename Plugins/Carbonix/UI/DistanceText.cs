using System;
using MissionPlanner;

namespace Carbonix
{
    /// <summary>
    /// Formats distances in the operator's distance unit family, reading in
    /// the small unit (m or ft) below one of the large (km or mi) and in the
    /// large unit above.
    /// </summary>
    public static class DistanceText
    {
        const double MetresPerKilometre = 1000.0;
        const double MetresPerStatuteMile = 1609.344;
        const double MetresPerNauticalMile = 1852.0;

        /// <summary>
        /// Formats a distance in the operator's unit family.
        /// </summary>
        /// <param name="metres">The distance in meters, whatever the display unit.</param>
        public static string Format(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres))
                return "--";

            return FormatPrimary(Math.Abs(metres));
        }

        /// <summary>
        /// Formats a distance in nautical miles, independent of the operator's
        /// unit family.
        /// </summary>
        /// <param name="suppressBelowOne">Return an empty string under 1 NM.</param>
        public static string NauticalMiles(double metres, bool suppressBelowOne = true)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres))
                return "";

            metres = Math.Abs(metres);

            return suppressBelowOne && metres < MetresPerNauticalMile
                ? ""
                : Large(metres / MetresPerNauticalMile) + " NM";
        }

        /// <summary>
        /// Formats in the operator's unit family.
        /// </summary>
        static string FormatPrimary(double metres)
        {
            // MainV2.ChangeUnits sets DistanceUnit and multiplierdist together
            // from the "distunits" setting. Reading the pair, rather than the
            // setting and a conversion factor of its own, keeps this in step
            // with the HUD and the rest of the telemetry.
            if (CurrentState.DistanceUnit == "ft")
                return Changeover(metres, MetresPerStatuteMile, "ft", "mi");

            return Changeover(metres, MetresPerKilometre, "m", "km");
        }

        /// <summary>
        /// Formats in the small unit below one large unit, and in the large
        /// unit above.
        /// </summary>
        static string Changeover(double metres, double metresPerLarge, string small, string large)
        {
            return metres < metresPerLarge
                ? Small(metres * CurrentState.multiplierdist) + " " + small
                : Large(metres / metresPerLarge) + " " + large;
        }

        /// <summary>
        /// Formats whole units.
        /// </summary>
        static string Small(double value)
        {
            return value.ToString("#,##0");
        }

        /// <summary>
        /// Formats to roughly three significant figures.
        /// </summary>
        static string Large(double value)
        {
            if (value < 10)
                return value.ToString("0.00");

            if (value < 100)
                return value.ToString("0.0");

            return value.ToString("#,##0");
        }
    }
}
