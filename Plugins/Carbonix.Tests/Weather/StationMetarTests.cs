using Carbonix.Weather;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Carbonix.Tests.Weather
{
    [TestClass]
    public class StationMetarTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 22, 3, 12, 0, DateTimeKind.Utc);

        static StationObservation Obs(double avgMs = 3.0, double gustMs = 4.0, double dir = 70, double tempC = 17.6, double rh = 45.5, double hpa = 1028.11)
        {
            return new StationObservation
            {
                WindAvgMs = avgMs,
                WindGustMs = gustMs,
                WindDirectionDeg = dir,
                AirTemperatureC = tempC,
                RelativeHumidityPct = rh,
                StationPressureHpa = hpa,
            };
        }

        static WeatherSnapshot Snap(StationObservation obs, DateTime received, bool stale = false)
        {
            return new WeatherSnapshot
            {
                LatestObservation = obs,
                LastObservationReceivedUtc = received,
                ObservationStale = stale,
            };
        }

        [TestMethod]
        public void Report()
        {
            // 3.0 m/s = 5.8 kt from 070, 17.6 C at 45.5 % (dew point 5.7 C), 1028.11 hPa = 771.1 mmHg
            var report = StationMetar.Format(Snap(Obs(), Now.AddSeconds(-30)));

            Assert.AreEqual("WXSTN 220311Z 07006KT 18/06 RMK QFE771/1028", report);
        }

        [TestMethod]
        public void ReportWithGust()
        {
            var report = StationMetar.Format(Snap(Obs(avgMs: 6, gustMs: 12), Now.AddSeconds(-30)));

            Assert.AreEqual("WXSTN 220311Z 07012G23KT 18/06 RMK QFE771/1028", report);
        }

        [TestMethod]
        public void TimeGroupIsWhenTheObservationArrived()
        {
            var report = StationMetar.Format(Snap(Obs(), Now.AddMinutes(-4)));

            Assert.IsTrue(report.StartsWith("WXSTN 220308Z "), report);
        }

        [TestMethod]
        public void ReportWithFailedSensors()
        {
            var obs = Obs(avgMs: double.NaN, dir: double.NaN, tempC: double.NaN, hpa: double.NaN);

            var report = StationMetar.Format(Snap(obs, Now.AddSeconds(-30)));

            Assert.AreEqual("WXSTN 220311Z /////KT /////", report);
        }

        [TestMethod]
        public void NothingWithoutARecentObservation()
        {
            Assert.IsNull(StationMetar.Format(Snap(null, DateTime.MinValue)));
            Assert.IsNull(StationMetar.Format(Snap(Obs(), Now.AddMinutes(-6), stale: true)));
            Assert.IsNotNull(StationMetar.Format(Snap(Obs(), Now.AddMinutes(-4))));
        }

        [TestMethod]
        public void WindGroup()
        {
            Assert.AreEqual("/////KT", StationMetar.Wind(double.NaN, double.NaN, double.NaN));
            Assert.AreEqual("07006KT", StationMetar.Wind(5.8, double.NaN, 70));
            Assert.AreEqual("00000KT", StationMetar.Wind(0.4, 3, 70));
            Assert.AreEqual("07006KT", StationMetar.Wind(5.8, 8, 70));
            // gust only from 10 kt over the mean
            Assert.AreEqual("07006KT", StationMetar.Wind(5.8, 15.4, 70));
            Assert.AreEqual("07006G16KT", StationMetar.Wind(5.8, 15.5, 70));
            // direction to the nearest 10, north is 360
            Assert.AreEqual("36012KT", StationMetar.Wind(12, 12, 2));
            Assert.AreEqual("36012KT", StationMetar.Wind(12, 12, 357));
            Assert.AreEqual("01012KT", StationMetar.Wind(12, 12, 5));
            Assert.AreEqual("35012KT", StationMetar.Wind(12, 12, 354));
            Assert.AreEqual("100105KT", StationMetar.Wind(105, 110, 100));
        }

        [TestMethod]
        public void TemperatureGroup()
        {
            Assert.AreEqual("//", StationMetar.Temperature(double.NaN));
            Assert.AreEqual("18", StationMetar.Temperature(17.6));
            Assert.AreEqual("00", StationMetar.Temperature(0.4));
            Assert.AreEqual("M00", StationMetar.Temperature(-0.4));
            Assert.AreEqual("M03", StationMetar.Temperature(-2.5));
        }

        [TestMethod]
        public void QfeGroup()
        {
            Assert.AreEqual("QFE771/1028", StationMetar.Qfe(1028.11));
            Assert.AreEqual("QFE746/0995", StationMetar.Qfe(994.6));
            Assert.AreEqual("QFE675/0900", StationMetar.Qfe(900));
        }

        [TestMethod]
        public void LinesFitStatusText()
        {
            var lines = StationMetar.Lines("WXSTN 220312Z 07012G23KT 18/06 RMK QFE771/1028");

            CollectionAssert.AreEqual(new[]
            {
                "WS1:WXSTN 220312Z 07012G23KT 18/06",
                "WS2:RMK QFE771/1028",
            }, lines);
            Assert.IsTrue(lines.All(l => l.Length <= StationMetar.LineLength));
            Assert.AreEqual(0, StationMetar.Lines(null).Count);
        }
    }
}
