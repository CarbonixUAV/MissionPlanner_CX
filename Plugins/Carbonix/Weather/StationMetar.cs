using System;
using System.Collections.Generic;

namespace Carbonix.Weather
{
    /// <summary>
    /// Formats the ground station's latest observation as a METAR-shaped
    /// line for the flight log.
    /// </summary>
    public static class StationMetar
    {
        public const string StationId = "WXSTN";

        /// <summary>Prefix on each STATUSTEXT line, numbered WS1:, WS2:, ...</summary>
        public const string LinePrefix = "WS";

        /// <summary>STATUSTEXT is 50 bytes; the records tab wraps at 35.</summary>
        public const int LineLength = 35;

        const double KnotsPerMs = 1.94384449;
        const double MmHgPerHpa = 0.750061683;

        /// <summary>
        /// Formats the report. The time group is when the observation was
        /// received on the GCS clock, so it lines up with the rest of the
        /// log even if the hub's clock is off.
        /// </summary>
        /// <returns>The report, or null when the snapshot's observation is missing or stale.</returns>
        public static string Format(WeatherSnapshot snap)
        {
            var obs = snap.LatestObservation;
            if (obs == null || snap.ObservationStale)
                return null;

            var report = string.Join(" ",
                StationId,
                snap.LastObservationReceivedUtc.ToString("ddHHmm") + "Z",
                Wind(obs.WindAvgMs * KnotsPerMs, obs.WindGustMs * KnotsPerMs, obs.WindDirectionDeg),
                Temperature(obs.AirTemperatureC) + "/" + Temperature(obs.DewPointC));
            if (!double.IsNaN(obs.StationPressureHpa))
                report += " RMK " + Qfe(obs.StationPressureHpa);
            return report;
        }

        /// <summary>
        /// Splits the report into STATUSTEXT-sized lines on word boundaries,
        /// each prefixed WS1:, WS2:, ... like the records tab's WXn: lines
        /// for the airfield METAR.
        /// </summary>
        public static List<string> Lines(string report)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(report))
                return lines;

            string line = "";
            foreach (var word in report.Split(' '))
            {
                if (line == "")
                    line = LinePrefix + (lines.Count + 1) + ":" + word;
                else if (line.Length + 1 + word.Length > LineLength)
                {
                    lines.Add(line);
                    line = LinePrefix + (lines.Count + 1) + ":" + word;
                }
                else
                    line += " " + word;
            }
            lines.Add(line);
            return lines;
        }

        /// <summary>Formats the wind group: dddffKT or dddffGggKT, 00000KT below 1 kt, /////KT when the sensor gave nothing.</summary>
        public static string Wind(double meanKt, double gustKt, double directionDeg)
        {
            if (double.IsNaN(meanKt) || double.IsNaN(directionDeg))
                return "/////KT";
            if (meanKt < 1.0)
                return "00000KT";

            // Direction to the nearest 10 degrees, 360 for north
            int dir = (int)Math.Round(directionDeg / 10.0, MidpointRounding.AwayFromZero) * 10;
            if (dir == 0)
                dir = 360;

            int speed = (int)Math.Round(meanKt, MidpointRounding.AwayFromZero);
            int gust = (int)Math.Round(gustKt, MidpointRounding.AwayFromZero);
            var text = dir.ToString("000") + speed.ToString("00");
            if (!double.IsNaN(gustKt) && gust >= speed + 10)
                text += "G" + gust.ToString("00");
            return text + "KT";
        }

        /// <summary>Formats a temperature as whole degrees with M for below zero (M00 just under zero), as in the body of a METAR; // when the sensor gave nothing.</summary>
        public static string Temperature(double celsius)
        {
            if (double.IsNaN(celsius))
                return "//";
            int t = (int)Math.Round(celsius, MidpointRounding.AwayFromZero);
            return (celsius < 0 ? "M" : "") + Math.Abs(t).ToString("00");
        }

        /// <summary>Formats the station pressure as QFEmmm/hhhh: whole mmHg, then whole hPa.</summary>
        public static string Qfe(double hpa)
        {
            int mm = (int)Math.Round(hpa * MmHgPerHpa, MidpointRounding.AwayFromZero);
            int hp = (int)Math.Round(hpa, MidpointRounding.AwayFromZero);
            return "QFE" + mm.ToString("000") + "/" + hp.ToString("0000");
        }
    }
}
