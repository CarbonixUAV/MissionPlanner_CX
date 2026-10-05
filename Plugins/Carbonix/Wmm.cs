using System;

namespace Carbonix
{
    /// <summary>
    /// Provides the World Magnetic Model (WMM2025) field vector and declination
    /// at a point.
    /// </summary>
    /// <remarks>
    /// Fitted for 2025.0 to 2030.0; later dates extrapolate the model's linear
    /// secular variation with growing error. The coefficient table is the
    /// published WMM2025.COF, which is public domain.
    /// </remarks>
    public static class Wmm
    {
        /// <summary>Model epoch, as a decimal year.</summary>
        public const double Epoch = 2025.0;

        /// <summary>First decimal year beyond the model's fitted span.</summary>
        public const double ValidUntil = 2030.0;

        const int Degree = 12;

        // Geomagnetic reference radius, km.
        const double ReferenceRadius = 6371.2;

        // WGS84.
        const double SemiMajor = 6378.137;
        const double Flattening = 1 / 298.257223563;

        /// <summary>
        /// Represents the field vector in the local geodetic frame, in nT: X
        /// north, Y east, Z down.
        /// </summary>
        internal struct Field
        {
            public double X;
            public double Y;
            public double Z;

            /// <summary>
            /// Gets the declination in degrees, east positive, or NaN at the
            /// geographic poles.
            /// </summary>
            public double Declination
            {
                get { return Math.Atan2(Y, X) * 180 / Math.PI; }
            }
        }

        /// <summary>
        /// Calculates the declination in degrees, east positive, at a geodetic
        /// position and date, with <paramref name="heightKm"/> above the WGS84
        /// ellipsoid.
        /// </summary>
        public static double Declination(double latDeg, double lonDeg, double heightKm, DateTime when)
        {
            return Compute(latDeg, lonDeg, heightKm, DecimalYear(when)).Declination;
        }

        /// <summary>
        /// Converts a date to a decimal year, the model's time axis.
        /// </summary>
        public static double DecimalYear(DateTime when)
        {
            var daysInYear = DateTime.IsLeapYear(when.Year) ? 366.0 : 365.0;
            return when.Year + (when.DayOfYear - 1 + when.TimeOfDay.TotalDays) / daysInYear;
        }

        /// <summary>
        /// Evaluates the field at a geodetic position for a decimal year.
        /// </summary>
        internal static Field Compute(double latDeg, double lonDeg, double heightKm, double decimalYear)
        {
            var lat = latDeg * Math.PI / 180;
            var lon = lonDeg * Math.PI / 180;

            // Geodetic to geocentric: the model is a spherical harmonic
            // expansion about the Earth's center, not about the ellipsoid.
            var e2 = Flattening * (2 - Flattening);
            var sinLat = Math.Sin(lat);
            var cosLat = Math.Cos(lat);
            var rc = SemiMajor / Math.Sqrt(1 - e2 * sinLat * sinLat);
            var p = (rc + heightKm) * cosLat;
            var z = (rc * (1 - e2) + heightKm) * sinLat;
            var r = Math.Sqrt(p * p + z * z);
            var gcLat = Math.Atan2(z, p);
            var sinG = z / r;
            var cosG = p / r;

            // Associated Legendre functions of sin(gcLat), and their
            // derivatives with respect to gcLat, by the standard recursions
            // on the unnormalized functions. The recursion feeds on earlier
            // rows, so the Schmidt normalization is a separate pass after it.
            var P = new double[Degree + 1, Degree + 1];
            var dP = new double[Degree + 1, Degree + 1];
            P[0, 0] = 1;

            for (var n = 1; n <= Degree; n++)
            {
                for (var m = 0; m <= n; m++)
                {
                    if (m == n)
                    {
                        P[n, n] = (2 * n - 1) * cosG * P[n - 1, n - 1];
                        dP[n, n] = (2 * n - 1) * (cosG * dP[n - 1, n - 1] - sinG * P[n - 1, n - 1]);
                    }
                    else
                    {
                        var prev2 = n - 2 >= m ? P[n - 2, m] : 0;
                        var dprev2 = n - 2 >= m ? dP[n - 2, m] : 0;

                        P[n, m] = ((2 * n - 1) * sinG * P[n - 1, m] - (n + m - 1) * prev2) / (n - m);
                        dP[n, m] = ((2 * n - 1) * (cosG * P[n - 1, m] + sinG * dP[n - 1, m]) - (n + m - 1) * dprev2) / (n - m);
                    }
                }
            }

            // Schmidt semi-normalization: sqrt((2 - d_m0) (n - m)! / (n + m)!),
            // built as a running product so nothing large is ever formed.
            for (var n = 1; n <= Degree; n++)
            {
                for (var m = 0; m <= n; m++)
                {
                    var s = m == 0 ? 1.0 : 2.0;
                    for (var k = n - m + 1; k <= n + m; k++)
                        s /= k;
                    s = Math.Sqrt(s);

                    P[n, m] *= s;
                    dP[n, m] *= s;
                }
            }

            var cosLon = new double[Degree + 1];
            var sinLon = new double[Degree + 1];
            for (var m = 0; m <= Degree; m++)
            {
                cosLon[m] = Math.Cos(m * lon);
                sinLon[m] = Math.Sin(m * lon);
            }

            var ratio = ReferenceRadius / r;
            var dt = decimalYear - Epoch;

            double xp = 0, yp = 0, zp = 0;

            for (var row = 0; row < Coefficients.GetLength(0); row++)
            {
                var n = (int)Coefficients[row, 0];
                var m = (int)Coefficients[row, 1];
                var g = Coefficients[row, 2] + dt * Coefficients[row, 4];
                var h = Coefficients[row, 3] + dt * Coefficients[row, 5];

                var rn = Math.Pow(ratio, n + 2);
                var a = g * cosLon[m] + h * sinLon[m];
                var b = g * sinLon[m] - h * cosLon[m];

                xp -= rn * a * dP[n, m];
                yp += rn * m * b * P[n, m];
                zp -= rn * (n + 1) * a * P[n, m];
            }

            // East component carries a 1/cos(lat) that has no finite value
            // at the poles; the declination there is undefined anyway.
            yp = Math.Abs(cosG) < 1e-12 ? double.NaN : yp / cosG;

            // Back from geocentric to geodetic.
            var psi = gcLat - lat;
            var cosPsi = Math.Cos(psi);
            var sinPsi = Math.Sin(psi);

            return new Field
            {
                X = xp * cosPsi - zp * sinPsi,
                Y = yp,
                Z = xp * sinPsi + zp * cosPsi,
            };
        }

        // WMM2025.COF, epoch 2025.0, released 2024-11-13, laid out line for
        // line so it can be diffed against the file:
        // n, m, g (nT), h (nT), g-dot (nT/yr), h-dot (nT/yr)
        static readonly double[,] Coefficients =
        {
            {  1,  0,  -29351.8,       0.0,       12.0,        0.0 },
            {  1,  1,   -1410.8,    4545.4,        9.7,      -21.5 },
            {  2,  0,   -2556.6,       0.0,      -11.6,        0.0 },
            {  2,  1,    2951.1,   -3133.6,       -5.2,      -27.7 },
            {  2,  2,    1649.3,    -815.1,       -8.0,      -12.1 },
            {  3,  0,    1361.0,       0.0,       -1.3,        0.0 },
            {  3,  1,   -2404.1,     -56.6,       -4.2,        4.0 },
            {  3,  2,    1243.8,     237.5,        0.4,       -0.3 },
            {  3,  3,     453.6,    -549.5,      -15.6,       -4.1 },
            {  4,  0,     895.0,       0.0,       -1.6,        0.0 },
            {  4,  1,     799.5,     278.6,       -2.4,       -1.1 },
            {  4,  2,      55.7,    -133.9,       -6.0,        4.1 },
            {  4,  3,    -281.1,     212.0,        5.6,        1.6 },
            {  4,  4,      12.1,    -375.6,       -7.0,       -4.4 },
            {  5,  0,    -233.2,       0.0,        0.6,        0.0 },
            {  5,  1,     368.9,      45.4,        1.4,       -0.5 },
            {  5,  2,     187.2,     220.2,        0.0,        2.2 },
            {  5,  3,    -138.7,    -122.9,        0.6,        0.4 },
            {  5,  4,    -142.0,      43.0,        2.2,        1.7 },
            {  5,  5,      20.9,     106.1,        0.9,        1.9 },
            {  6,  0,      64.4,       0.0,       -0.2,        0.0 },
            {  6,  1,      63.8,     -18.4,       -0.4,        0.3 },
            {  6,  2,      76.9,      16.8,        0.9,       -1.6 },
            {  6,  3,    -115.7,      48.8,        1.2,       -0.4 },
            {  6,  4,     -40.9,     -59.8,       -0.9,        0.9 },
            {  6,  5,      14.9,      10.9,        0.3,        0.7 },
            {  6,  6,     -60.7,      72.7,        0.9,        0.9 },
            {  7,  0,      79.5,       0.0,       -0.0,        0.0 },
            {  7,  1,     -77.0,     -48.9,       -0.1,        0.6 },
            {  7,  2,      -8.8,     -14.4,       -0.1,        0.5 },
            {  7,  3,      59.3,      -1.0,        0.5,       -0.8 },
            {  7,  4,      15.8,      23.4,       -0.1,        0.0 },
            {  7,  5,       2.5,      -7.4,       -0.8,       -1.0 },
            {  7,  6,     -11.1,     -25.1,       -0.8,        0.6 },
            {  7,  7,      14.2,      -2.3,        0.8,       -0.2 },
            {  8,  0,      23.2,       0.0,       -0.1,        0.0 },
            {  8,  1,      10.8,       7.1,        0.2,       -0.2 },
            {  8,  2,     -17.5,     -12.6,        0.0,        0.5 },
            {  8,  3,       2.0,      11.4,        0.5,       -0.4 },
            {  8,  4,     -21.7,      -9.7,       -0.1,        0.4 },
            {  8,  5,      16.9,      12.7,        0.3,       -0.5 },
            {  8,  6,      15.0,       0.7,        0.2,       -0.6 },
            {  8,  7,     -16.8,      -5.2,       -0.0,        0.3 },
            {  8,  8,       0.9,       3.9,        0.2,        0.2 },
            {  9,  0,       4.6,       0.0,       -0.0,        0.0 },
            {  9,  1,       7.8,     -24.8,       -0.1,       -0.3 },
            {  9,  2,       3.0,      12.2,        0.1,        0.3 },
            {  9,  3,      -0.2,       8.3,        0.3,       -0.3 },
            {  9,  4,      -2.5,      -3.3,       -0.3,        0.3 },
            {  9,  5,     -13.1,      -5.2,        0.0,        0.2 },
            {  9,  6,       2.4,       7.2,        0.3,       -0.1 },
            {  9,  7,       8.6,      -0.6,       -0.1,       -0.2 },
            {  9,  8,      -8.7,       0.8,        0.1,        0.4 },
            {  9,  9,     -12.9,      10.0,       -0.1,        0.1 },
            { 10,  0,      -1.3,       0.0,        0.1,        0.0 },
            { 10,  1,      -6.4,       3.3,        0.0,        0.0 },
            { 10,  2,       0.2,       0.0,        0.1,       -0.0 },
            { 10,  3,       2.0,       2.4,        0.1,       -0.2 },
            { 10,  4,      -1.0,       5.3,       -0.0,        0.1 },
            { 10,  5,      -0.6,      -9.1,       -0.3,       -0.1 },
            { 10,  6,      -0.9,       0.4,        0.0,        0.1 },
            { 10,  7,       1.5,      -4.2,       -0.1,        0.0 },
            { 10,  8,       0.9,      -3.8,       -0.1,       -0.1 },
            { 10,  9,      -2.7,       0.9,       -0.0,        0.2 },
            { 10, 10,      -3.9,      -9.1,       -0.0,       -0.0 },
            { 11,  0,       2.9,       0.0,        0.0,        0.0 },
            { 11,  1,      -1.5,       0.0,       -0.0,       -0.0 },
            { 11,  2,      -2.5,       2.9,        0.0,        0.1 },
            { 11,  3,       2.4,      -0.6,        0.0,       -0.0 },
            { 11,  4,      -0.6,       0.2,        0.0,        0.1 },
            { 11,  5,      -0.1,       0.5,       -0.1,       -0.0 },
            { 11,  6,      -0.6,      -0.3,        0.0,       -0.0 },
            { 11,  7,      -0.1,      -1.2,       -0.0,        0.1 },
            { 11,  8,       1.1,      -1.7,       -0.1,       -0.0 },
            { 11,  9,      -1.0,      -2.9,       -0.1,        0.0 },
            { 11, 10,      -0.2,      -1.8,       -0.1,        0.0 },
            { 11, 11,       2.6,      -2.3,       -0.1,        0.0 },
            { 12,  0,      -2.0,       0.0,        0.0,        0.0 },
            { 12,  1,      -0.2,      -1.3,        0.0,       -0.0 },
            { 12,  2,       0.3,       0.7,       -0.0,        0.0 },
            { 12,  3,       1.2,       1.0,       -0.0,       -0.1 },
            { 12,  4,      -1.3,      -1.4,       -0.0,        0.1 },
            { 12,  5,       0.6,      -0.0,       -0.0,       -0.0 },
            { 12,  6,       0.6,       0.6,        0.1,       -0.0 },
            { 12,  7,       0.5,      -0.1,       -0.0,       -0.0 },
            { 12,  8,      -0.1,       0.8,        0.0,        0.0 },
            { 12,  9,      -0.4,       0.1,        0.0,       -0.0 },
            { 12, 10,      -0.2,      -1.0,       -0.1,       -0.0 },
            { 12, 11,      -1.3,       0.1,       -0.0,        0.0 },
            { 12, 12,      -0.7,       0.2,       -0.1,       -0.1 },
        };
    }
}
