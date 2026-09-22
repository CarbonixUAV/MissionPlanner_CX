using Carbonix.Weather;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Carbonix.Tests.Weather
{
    /// <summary>
    /// Replays a real Tempest capture (about eight minutes: 165 rapid_wind,
    /// 8 obs_st, plus hub and device status) into a station with the packet
    /// timing from the capture.
    /// </summary>
    [TestClass]
    public class WeatherStationTests
    {
        static readonly DateTime Start = new DateTime(2026, 9, 17, 2, 0, 0, DateTimeKind.Utc);

        struct Packet
        {
            public DateTime ReceivedUtc;
            public string Json;
        }

        static List<Packet> Capture()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "tempest_capture.txt");
            return File.ReadAllLines(path)
                .Where(l => l.Contains("\t"))
                .Select(l =>
                {
                    var parts = l.Split(new[] { '\t' }, 2);
                    return new Packet
                    {
                        ReceivedUtc = Start.AddSeconds(double.Parse(parts[0], CultureInfo.InvariantCulture)),
                        Json = parts[1],
                    };
                })
                .ToList();
        }

        static WeatherStation Replay(WeatherStation station = null)
        {
            station = station ?? new WeatherStation();
            foreach (var p in Capture())
                station.Apply(p.Json, p.ReceivedUtc);
            return station;
        }

        static RapidWind Sample(double speedMs, double directionDeg)
        {
            return new RapidWind { SerialNumber = "ST-1", HubSerial = "HB-1", SpeedMs = speedMs, DirectionDeg = directionDeg };
        }

        static SmoothedWind Wind(WeatherStation station)
        {
            return station.Snapshot(Start).Wind;
        }

        [TestMethod]
        public void LatestValuesAreTheLastHeard()
        {
            var station = Replay();
            var last = Capture().Last().ReceivedUtc;
            var snap = station.Snapshot(last);

            Assert.AreEqual(1.49, snap.LatestWind.SpeedMs, 1e-9);
            Assert.AreEqual(59, snap.LatestWind.DirectionDeg, 1e-9);
            Assert.AreEqual(last, snap.LastWindReceivedUtc);
            Assert.AreEqual(last, snap.LastReceivedUtc);

            Assert.AreEqual(1028.06, snap.LatestObservation.StationPressureHpa, 1e-9);
            Assert.AreEqual(17.64, snap.LatestObservation.AirTemperatureC, 1e-9);
            Assert.AreEqual(Start.AddSeconds(445.060293), snap.LastObservationReceivedUtc);

            Assert.AreEqual(2.772, snap.LatestDeviceStatus.VoltageV, 1e-9);
            Assert.AreEqual("309", snap.LatestHubStatus.FirmwareRevision);
        }

        [TestMethod]
        public void WindGoesStaleWhenTheHubStopsTalking()
        {
            var station = new WeatherStation();
            Assert.IsTrue(station.Snapshot(Start).WindStale);

            Replay(station);
            var last = Capture().Last().ReceivedUtc;

            Assert.IsFalse(station.Snapshot(last).WindStale);
            Assert.IsFalse(station.Snapshot(last.AddSeconds(29)).WindStale);
            Assert.IsTrue(station.Snapshot(last.AddSeconds(31)).WindStale);
        }

        [TestMethod]
        public void ObservationGoesStaleWhenTheHubStopsTalking()
        {
            var station = new WeatherStation();
            Assert.IsTrue(station.Snapshot(Start).ObservationStale);

            Replay(station);
            var last = station.Snapshot(Start).LastObservationReceivedUtc;

            Assert.IsFalse(station.Snapshot(last.AddMinutes(4)).ObservationStale);
            Assert.IsTrue(station.Snapshot(last.AddMinutes(6)).ObservationStale);
        }

        [TestMethod]
        public void NullWindSampleLeavesTheWindToGoStale()
        {
            var station = new WeatherStation();
            station.Apply(Sample(4.2, 135), Start);
            station.Apply(Sample(double.NaN, double.NaN), Start.AddSeconds(3));

            var snap = station.Snapshot(Start.AddSeconds(3));
            Assert.AreEqual(4.2, snap.Wind.SpeedMs, 1e-9);
            Assert.AreEqual(4.2, snap.LatestWind.SpeedMs, 1e-9);
            Assert.AreEqual(Start, snap.LastWindReceivedUtc);
            Assert.AreEqual(Start.AddSeconds(3), snap.LastReceivedUtc);
            Assert.IsTrue(station.Snapshot(Start.AddSeconds(40)).WindStale);
        }

        [TestMethod]
        public void ClockStepBackHoldsTheFilter()
        {
            var station = new WeatherStation();
            for (int i = 0; i < 100; i++)
                station.Apply(Sample(2, 90), Start.AddSeconds(3 * i));
            var before = Wind(station).SpeedMs;

            station.Apply(Sample(8, 270), Start.AddSeconds(3 * 99 - 5));

            Assert.AreEqual(before, Wind(station).SpeedMs, 1e-9);
        }

        [TestMethod]
        public void FirstSampleSetsTheSmoothedWind()
        {
            var station = new WeatherStation();
            station.Apply(Sample(4.2, 135), Start);

            Assert.AreEqual(4.2, Wind(station).SpeedMs, 1e-9);
            Assert.AreEqual(135, Wind(station).DirectionDeg, 1e-9);
        }

        [TestMethod]
        public void StepReachesTwoThirdsAfterOneTimeConstant()
        {
            var station = new WeatherStation { WindSmoothing = TimeSpan.FromSeconds(60) };
            station.Apply(Sample(0, 90), Start);

            // 3 s samples for one time constant
            for (int i = 1; i <= 20; i++)
                station.Apply(Sample(10, 90), Start.AddSeconds(3 * i));

            Assert.AreEqual(10 * (1 - Math.Exp(-1)), Wind(station).SpeedMs, 1e-6);

            for (int i = 21; i <= 200; i++)
                station.Apply(Sample(10, 90), Start.AddSeconds(3 * i));

            Assert.AreEqual(10, Wind(station).SpeedMs, 1e-3);
        }

        [TestMethod]
        public void StepIsIndependentOfSampleSpacing()
        {
            var every3s = new WeatherStation();
            var every6s = new WeatherStation();
            every3s.Apply(Sample(0, 0), Start);
            every6s.Apply(Sample(0, 0), Start);

            for (int i = 1; i <= 40; i++)
                every3s.Apply(Sample(10, 0), Start.AddSeconds(3 * i));
            for (int i = 1; i <= 20; i++)
                every6s.Apply(Sample(10, 0), Start.AddSeconds(6 * i));

            Assert.AreEqual(Wind(every3s).SpeedMs, Wind(every6s).SpeedMs, 1e-9);
        }

        [TestMethod]
        public void DirectionAveragesAcrossNorth()
        {
            var station = new WeatherStation();
            for (int i = 0; i < 100; i++)
                station.Apply(Sample(5, i % 2 == 0 ? 350 : 10), Start.AddSeconds(3 * i));

            var deg = Wind(station).DirectionDeg;
            Assert.AreEqual(0, Math.Min(deg, 360 - deg), 2);
        }

        [TestMethod]
        public void LongOutageRestartsTheFilter()
        {
            var station = new WeatherStation();
            for (int i = 0; i < 100; i++)
                station.Apply(Sample(2, 90), Start.AddSeconds(3 * i));

            station.Apply(Sample(8, 270), Start.AddMinutes(30));

            Assert.AreEqual(8, Wind(station).SpeedMs, 1e-6);
            Assert.AreEqual(270, Wind(station).DirectionDeg, 1e-6);
        }

        [TestMethod]
        public void SmoothedWindIsSteadierThanTheSamples()
        {
            var station = new WeatherStation();
            var raw = new List<double>();
            var smoothed = new List<double>();
            foreach (var p in Capture())
            {
                station.Apply(p.Json, p.ReceivedUtc);
                if (p.Json.Contains("\"rapid_wind\""))
                {
                    var snap = station.Snapshot(p.ReceivedUtc);
                    raw.Add(snap.LatestWind.SpeedMs);
                    smoothed.Add(snap.Wind.SpeedMs);
                }
            }

            double Jitter(List<double> v) => v.Zip(v.Skip(1), (a, b) => Math.Abs(b - a)).Max();

            Assert.IsTrue(Jitter(smoothed) < Jitter(raw) / 4, $"smoothed {Jitter(smoothed)} raw {Jitter(raw)}");
            Assert.IsTrue(smoothed.Last() >= raw.Min() && smoothed.Last() <= raw.Max());
        }
    }
}
