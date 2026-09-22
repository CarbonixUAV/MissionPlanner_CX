using Carbonix.Weather;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Carbonix.Tests.Weather
{
    /// <summary>
    /// Sample datagrams are taken verbatim from a Wireshark capture of a
    /// Tempest hub (firmware 309, sensor firmware 185).
    /// </summary>
    [TestClass]
    public class TempestParserTests
    {
        const string RapidWindJson =
            "{\"serial_number\":\"ST-00192841\",\"type\":\"rapid_wind\",\"hub_sn\":\"HB-00194898\",\"ob\":[1789610559,2.66,70]}";

        const string ObsJson =
            "{\"serial_number\":\"ST-00192841\",\"type\":\"obs_st\",\"hub_sn\":\"HB-00194898\",\"obs\":[[1789610582,1.03,2.20,3.85,79,3,1028.11,17.67,45.52,3522748,11.83,29356,0.154409,2,0,0,2.706,1]],\"firmware_revision\":185}";

        const string DeviceStatusJson =
            "{\"serial_number\":\"ST-00192841\",\"type\":\"device_status\",\"hub_sn\":\"HB-00194898\",\"timestamp\":1789610582,\"uptime\":66,\"voltage\":2.706,\"firmware_revision\":185,\"rssi\":-68,\"hub_rssi\":-68,\"sensor_status\":655360,\"debug\":1}";

        const string HubStatusJson =
            "{\"serial_number\":\"HB-00194898\",\"type\":\"hub_status\",\"firmware_revision\":\"309\",\"uptime\":2170,\"rssi\":-26,\"timestamp\":1789610562,\"reset_flags\":\"POR\",\"seq\":216,\"radio_stats\":[28,1,0,2,30323],\"mqtt_stats\":[1,5],\"freq\":906000000,\"hw_version\":2,\"hardware_id\":0}";

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void RapidWind()
        {
            var msg = (RapidWind)TempestParser.Parse(RapidWindJson).Single();

            Assert.AreEqual("ST-00192841", msg.SerialNumber);
            Assert.AreEqual("HB-00194898", msg.HubSerial);
            Assert.AreEqual(Epoch.AddSeconds(1789610559), msg.TimeUtc);
            Assert.AreEqual(DateTimeKind.Utc, msg.TimeUtc.Kind);
            Assert.AreEqual(2.66, msg.SpeedMs, 1e-9);
            Assert.AreEqual(70, msg.DirectionDeg, 1e-9);
        }

        [TestMethod]
        public void StationObservation()
        {
            var msg = (StationObservation)TempestParser.Parse(ObsJson).Single();

            Assert.AreEqual(Epoch.AddSeconds(1789610582), msg.TimeUtc);
            Assert.AreEqual(1.03, msg.WindLullMs, 1e-9);
            Assert.AreEqual(2.20, msg.WindAvgMs, 1e-9);
            Assert.AreEqual(3.85, msg.WindGustMs, 1e-9);
            Assert.AreEqual(79, msg.WindDirectionDeg, 1e-9);
            Assert.AreEqual(3, msg.WindSampleIntervalS, 1e-9);
            Assert.AreEqual(1028.11, msg.StationPressureHpa, 1e-9);
            Assert.AreEqual(17.67, msg.AirTemperatureC, 1e-9);
            Assert.AreEqual(45.52, msg.RelativeHumidityPct, 1e-9);
            Assert.AreEqual(3522748, msg.IlluminanceLux, 1e-9);
            Assert.AreEqual(11.83, msg.UvIndex, 1e-9);
            Assert.AreEqual(29356, msg.SolarRadiationWm2, 1e-9);
            Assert.AreEqual(0.154409, msg.RainMm, 1e-9);
            Assert.AreEqual(PrecipType.Hail, msg.PrecipType);
            Assert.AreEqual(0, msg.LightningAvgDistanceKm, 1e-9);
            Assert.AreEqual(0, msg.LightningStrikeCount);
            Assert.AreEqual(2.706, msg.BatteryV, 1e-9);
            Assert.AreEqual(1, msg.ReportIntervalMin, 1e-9);
            Assert.AreEqual(185, msg.FirmwareRevision);
        }

        [TestMethod]
        public void DewPoint()
        {
            var msg = (StationObservation)TempestParser.Parse(ObsJson).Single();

            // 17.67 C at 45.52 % RH
            Assert.AreEqual(5.75, msg.DewPointC, 0.01);
        }

        [TestMethod]
        public void ObservationWithSeveralRows()
        {
            var json = ObsJson.Replace("]],", "],[1789610642,1.03,2.20,3.85,79,3,1028.11,17.67,45.52,3522748,11.83,29356,0,0,0,0,2.706,1]],");

            var msgs = TempestParser.Parse(json);

            Assert.AreEqual(2, msgs.Count);
            Assert.AreEqual(Epoch.AddSeconds(1789610642), msgs[1].TimeUtc);
        }

        [TestMethod]
        public void DeviceStatus()
        {
            var msg = (DeviceStatus)TempestParser.Parse(DeviceStatusJson).Single();

            Assert.AreEqual(Epoch.AddSeconds(1789610582), msg.TimeUtc);
            Assert.AreEqual(66, msg.UptimeS);
            Assert.AreEqual(2.706, msg.VoltageV, 1e-9);
            Assert.AreEqual(185, msg.FirmwareRevision);
            Assert.AreEqual(-68, msg.RssiDbm);
            Assert.AreEqual(-68, msg.HubRssiDbm);
            Assert.AreEqual(655360u, msg.SensorStatus);
            // Only the undocumented high bits are set
            Assert.IsTrue(msg.SensorsOk);
            Assert.AreEqual(0, msg.SensorFaults.Count());
        }

        [TestMethod]
        public void DeviceStatusFaults()
        {
            var msg = (DeviceStatus)TempestParser.Parse(DeviceStatusJson.Replace("655360", "72")).Single();

            Assert.IsFalse(msg.SensorsOk);
            CollectionAssert.AreEqual(new[] { "pressure failed", "wind failed" }, msg.SensorFaults.ToList());
        }

        [TestMethod]
        public void HubStatus()
        {
            var msg = (HubStatus)TempestParser.Parse(HubStatusJson).Single();

            Assert.AreEqual("HB-00194898", msg.SerialNumber);
            Assert.AreEqual("HB-00194898", msg.HubSerial);
            Assert.AreEqual("309", msg.FirmwareRevision);
            Assert.AreEqual(2170, msg.UptimeS);
            Assert.AreEqual(-26, msg.RssiDbm);
            Assert.AreEqual("POR", msg.ResetFlags);
        }

        [TestMethod]
        public void Events()
        {
            var strike = (LightningStrikeEvent)TempestParser.Parse(
                "{\"serial_number\":\"ST-00192841\",\"type\":\"evt_strike\",\"hub_sn\":\"HB-00194898\",\"evt\":[1789610600,27,3848]}").Single();
            Assert.AreEqual(27, strike.DistanceKm, 1e-9);
            Assert.AreEqual(3848, strike.Energy, 1e-9);

            var rain = (PrecipEvent)TempestParser.Parse(
                "{\"serial_number\":\"ST-00192841\",\"type\":\"evt_precip\",\"hub_sn\":\"HB-00194898\",\"evt\":[1789610601]}").Single();
            Assert.AreEqual(Epoch.AddSeconds(1789610601), rain.TimeUtc);
        }

        [TestMethod]
        public void IgnoresWhatItDoesNotUnderstand()
        {
            Assert.AreEqual(0, TempestParser.Parse(null).Count);
            Assert.AreEqual(0, TempestParser.Parse("").Count);
            Assert.AreEqual(0, TempestParser.Parse("not json").Count);
            Assert.AreEqual(0, TempestParser.Parse("[1,2,3]").Count);
            Assert.AreEqual(0, TempestParser.Parse("{\"type\":\"obs_air\",\"obs\":[[1,2,3]]}").Count);
            Assert.AreEqual(0, TempestParser.Parse("{\"no\":\"type\"}").Count);
        }

        [TestMethod]
        public void NullReadingIsNaNNotADroppedMessage()
        {
            // A failed temperature sensor nulls its slot; the rest of the
            // observation is still good
            var json = ObsJson.Replace("1028.11,17.67,45.52", "1028.11,null,null");

            var msg = (StationObservation)TempestParser.Parse(json).Single();

            Assert.AreEqual(1028.11, msg.StationPressureHpa, 1e-9);
            Assert.IsTrue(double.IsNaN(msg.AirTemperatureC));
            Assert.IsTrue(double.IsNaN(msg.RelativeHumidityPct));
            Assert.IsTrue(double.IsNaN(msg.DewPointC));

            var wind = (RapidWind)TempestParser.Parse(RapidWindJson.Replace("2.66,70", "null,null")).Single();
            Assert.IsTrue(double.IsNaN(wind.SpeedMs));
            Assert.IsTrue(double.IsNaN(wind.DirectionDeg));
        }

        [TestMethod]
        public void DropsTruncatedMessage()
        {
            // obs row cut short: recognized type, but not a whole message
            var json = "{\"serial_number\":\"ST-1\",\"type\":\"obs_st\",\"hub_sn\":\"HB-1\",\"obs\":[[1789610582,1.03,2.20]]}";

            Assert.AreEqual(0, TempestParser.Parse(json).Count);
        }
    }
}
