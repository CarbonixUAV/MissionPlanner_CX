using System;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Provides conversion from static pressure to pressure altitude on the
    /// 1013.25 hPa datum.
    /// </summary>
    /// <remarks>
    /// Implements the ICAO standard atmosphere (ISO 2533) / US Standard
    /// Atmosphere 1976. Results are geopotential altitude, not geometric.
    /// </remarks>
    public static class PressureAltitude
    {
        // Every altitude here is geopotential, which is what pressure altitude means:
        // the ISA's geopotential altitude for a given pressure. Most sources assume
        // that rather than state it. The firmest freely-available citation is 14 CFR
        // part 43 appendix E table I, whose nominal pressure/altitude pairs match the
        // ISA geopotential column exactly, despite tolerances wide enough to admit
        // either geometric or geopotential.

        /// <summary>
        /// Represents the standard sea-level pressure, in hPa, that pressure altitude
        /// is referenced to.
        /// </summary>
        public const double SeaLevelPressureHpa = 1013.25;

        /// <summary>Specific gas constant for dry air, R, in J/(kg K); ICAO / ISO 2533.</summary>
        private const double SpecificGasConstant = 287.05287;

        /// <summary>Standard gravitational acceleration, g0, in m/s^2.</summary>
        private const double StandardGravity = 9.80665;

        private const double MetersPerFoot = 0.3048;

        // Three layers cover the whole encodable range. Layer 1 is isothermal and takes
        // the other functional form: at a lapse rate of zero the gradient expression
        // divides by zero.

        // Layer 0: troposphere, 0 to 11 km, gradient.
        private const double Layer0BaseAltitudeM = 0.0;
        private const double Layer0BaseTemperatureK = 288.15;
        private const double Layer0LapseRateKPerM = -0.0065;
        private const double Layer0BasePressureHpa = SeaLevelPressureHpa;

        // Layer 1: tropopause, 11 to 20 km, isothermal.
        private const double Layer1BaseAltitudeM = 11000.0;
        private const double Layer1BaseTemperatureK = 216.65;

        // Layer 2: stratosphere, 20 to 32 km, gradient.
        private const double Layer2BaseAltitudeM = 20000.0;
        private const double Layer2BaseTemperatureK = Layer1BaseTemperatureK;
        private const double Layer2LapseRateKPerM = 0.001;

        // Each layer's base pressure, derived by evaluating the layer below at the
        // boundary instead of taking the published figure. That puts the switch in
        // TryFromStaticPressure at exactly the pressure the next layer is based on, so
        // the conversion stays continuous across it.
        internal static readonly double Layer1BasePressureHpa = GradientLayerPressure(
            Layer1BaseAltitudeM,
            Layer0BaseAltitudeM, Layer0BaseTemperatureK, Layer0LapseRateKPerM, Layer0BasePressureHpa);

        internal static readonly double Layer2BasePressureHpa = IsothermalLayerPressure(
            Layer2BaseAltitudeM,
            Layer1BaseAltitudeM, Layer1BaseTemperatureK, Layer1BasePressureHpa);

        /// <summary>
        /// Converts static pressure in hPa to pressure altitude in geopotential feet.
        /// </summary>
        /// <param name="staticPressureHpa">Absolute static pressure in hPa, as read by the barometer.</param>
        /// <param name="altitudeFeet">Pressure altitude in feet on the 1013.25 hPa datum; zero when this returns false.</param>
        /// <returns>
        /// true if the pressure converts to an altitude within the range the GDL 90
        /// altitude field encodes; otherwise, false.
        /// </returns>
        public static bool TryFromStaticPressure(double staticPressureHpa, out double altitudeFeet)
        {
            altitudeFeet = 0.0;

            if (double.IsNaN(staticPressureHpa) || double.IsInfinity(staticPressureHpa)) return false;
            if (staticPressureHpa <= 0.0) return false;

            double altitudeM;
            if (staticPressureHpa >= Layer1BasePressureHpa)
            {
                altitudeM = GradientLayerAltitude(staticPressureHpa,
                    Layer0BaseAltitudeM, Layer0BaseTemperatureK, Layer0LapseRateKPerM, Layer0BasePressureHpa);
            }
            else if (staticPressureHpa >= Layer2BasePressureHpa)
            {
                altitudeM = IsothermalLayerAltitude(staticPressureHpa,
                    Layer1BaseAltitudeM, Layer1BaseTemperatureK, Layer1BasePressureHpa);
            }
            else
            {
                altitudeM = GradientLayerAltitude(staticPressureHpa,
                    Layer2BaseAltitudeM, Layer2BaseTemperatureK, Layer2LapseRateKPerM, Layer2BasePressureHpa);
            }

            double feet = altitudeM / MetersPerFoot;

            if (double.IsNaN(feet)) return false;
            if (feet < Gdl90Messages.MinimumAltitudeFeet
                || feet > Gdl90Messages.MaximumAltitudeFeet) return false;

            altitudeFeet = feet;
            return true;
        }

        /// <summary>
        /// Geopotential altitude within a layer of non-zero lapse rate:
        /// h = hb + (Tb / L) * ((p / pb) ^ (-R L / g0) - 1).
        /// </summary>
        private static double GradientLayerAltitude(
            double pressureHpa,
            double baseAltitudeM, double baseTemperatureK, double lapseRateKPerM, double basePressureHpa)
        {
            double exponent = -(SpecificGasConstant * lapseRateKPerM) / StandardGravity;
            return baseAltitudeM
                + (baseTemperatureK / lapseRateKPerM)
                * (Math.Pow(pressureHpa / basePressureHpa, exponent) - 1.0);
        }

        /// <summary>
        /// Geopotential altitude within an isothermal layer:
        /// h = hb - (R Tb / g0) * ln(p / pb).
        /// </summary>
        private static double IsothermalLayerAltitude(
            double pressureHpa,
            double baseAltitudeM, double baseTemperatureK, double basePressureHpa)
        {
            return baseAltitudeM
                - ((SpecificGasConstant * baseTemperatureK) / StandardGravity)
                * Math.Log(pressureHpa / basePressureHpa);
        }

        /// <summary>
        /// The gradient relation in the forward direction, used only to carry a base
        /// pressure up to the next layer boundary.
        /// </summary>
        private static double GradientLayerPressure(
            double altitudeM,
            double baseAltitudeM, double baseTemperatureK, double lapseRateKPerM, double basePressureHpa)
        {
            double exponent = StandardGravity / (SpecificGasConstant * lapseRateKPerM);
            return basePressureHpa
                * Math.Pow(
                    baseTemperatureK / (baseTemperatureK + lapseRateKPerM * (altitudeM - baseAltitudeM)),
                    exponent);
        }

        /// <summary>The isothermal relation in the forward direction. See <see cref="GradientLayerPressure"/>.</summary>
        private static double IsothermalLayerPressure(
            double altitudeM,
            double baseAltitudeM, double baseTemperatureK, double basePressureHpa)
        {
            return basePressureHpa
                * Math.Exp(-(StandardGravity * (altitudeM - baseAltitudeM))
                           / (SpecificGasConstant * baseTemperatureK));
        }
    }
}
