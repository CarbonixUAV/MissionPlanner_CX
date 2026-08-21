using System;
using MissionPlanner;

namespace Carbonix
{
    /// <summary>
    /// Renders a distance for display. The unit family follows the operator's
    /// "distunits" setting, and the magnitude picks whether it reads in the
    /// small unit (m/ft) or the large one (km/statute miles), so a readout that
    /// sweeps from a wingspan to a leg length never turns into a wall of
    /// digits.
    ///
    /// Nautical miles come back from <see cref="NauticalMiles"/> as a reading
    /// of their own, independent of the unit-family setting.
    /// </summary>
    public static class DistanceText
    {
        const double MetresPerKilometre = 1000.0;
        const double MetresPerStatuteMile = 1609.344;
        const double MetresPerNauticalMile = 1852.0;

        /// <summary>
        /// Formats a distance held in metres, in the operator's chosen unit
        /// family.
        /// </summary>
        /// <param name="metres">The distance, always in metres regardless of the display unit.</param>
        public static string Format(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres))
                return "--";

            return FormatPrimary(Math.Abs(metres));
        }

        /// <summary>
        /// The nautical-mile reading on its own, for callers laying out their
        /// own columns.
        /// </summary>
        /// <param name="suppressBelowOne">
        /// Return an empty string under 1 NM. A readout that trails the mouse
        /// wants that - a fraction of a mile is noise while you are measuring a
        /// strip - but a panel with a standing slot for the figure would rather
        /// fill it.
        /// </param>
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
        /// Picks the unit family and hands off to <see cref="Changeover"/>.
        /// </summary>
        /// <remarks>
        /// MainV2.ChangeUnits sets DistanceUnit and multiplierdist together
        /// from the "distunits" setting; between them they say which family the
        /// operator is in and how to get there from metres. Reading the pair,
        /// rather than the raw setting and a conversion factor of our own,
        /// keeps this in step with the HUD and the rest of the telemetry.
        /// </remarks>
        static string FormatPrimary(double metres)
        {
            if (CurrentState.DistanceUnit == "ft")
                return Changeover(metres, MetresPerStatuteMile, "ft", "mi");

            return Changeover(metres, MetresPerKilometre, "m", "km");
        }

        /// <summary>
        /// Reads in the small unit below one of the large ones, and in large
        /// units above - so metres give way to kilometres at 1 km, and feet to
        /// statute miles at 1 mi.
        /// </summary>
        static string Changeover(double metres, double metresPerLarge, string small, string large)
        {
            return metres < metresPerLarge
                ? Small(metres * CurrentState.multiplierdist) + " " + small
                : Large(metres / metresPerLarge) + " " + large;
        }

        /// <summary>
        /// Whole units below the changeover - a metre or a foot is already
        /// finer than anything you can point at with a mouse.
        /// </summary>
        static string Small(double value)
        {
            return value.ToString("#,##0");
        }

        /// <summary>
        /// Roughly three significant figures above the changeover, so 1.23 km
        /// and 123 km both read cleanly.
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
