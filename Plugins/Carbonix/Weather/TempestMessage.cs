using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Carbonix.Weather
{
    /// <summary>
    /// Represents one datagram from a WeatherFlow Tempest hub.
    /// </summary>
    /// <remarks>
    /// Field order follows the WeatherFlow UDP reference, v171:
    /// https://weatherflow.github.io/Tempest/api/udp/v171/. A reading the
    /// hub sends as null (a failed sensor) is NaN here.
    /// </remarks>
    public abstract class TempestMessage
    {
        public string SerialNumber;
        public string HubSerial;

        /// <summary>Station clock at the observation, UTC.</summary>
        public DateTime TimeUtc;
    }

    /// <summary>Represents a three-second wind sample (rapid_wind).</summary>
    public class RapidWind : TempestMessage
    {
        public double SpeedMs;

        /// <summary>Direction the wind is coming from, degrees true.</summary>
        public double DirectionDeg;
    }

    public enum PrecipType
    {
        None = 0,
        Rain = 1,
        Hail = 2,
        RainAndHail = 3,
    }

    /// <summary>Represents a one-minute summary observation (obs_st).</summary>
    public class StationObservation : TempestMessage
    {
        public double WindLullMs;
        public double WindAvgMs;
        public double WindGustMs;

        /// <summary>Direction the wind is coming from, degrees true.</summary>
        public double WindDirectionDeg;
        public double WindSampleIntervalS;

        /// <summary>Pressure at the station, hPa (not reduced to sea level).</summary>
        public double StationPressureHpa;
        public double AirTemperatureC;
        public double RelativeHumidityPct;
        public double IlluminanceLux;
        public double UvIndex;
        public double SolarRadiationWm2;

        /// <summary>Rain over the previous minute, mm.</summary>
        public double RainMm;
        public PrecipType PrecipType;
        public double LightningAvgDistanceKm;
        public int LightningStrikeCount;
        public double BatteryV;
        public double ReportIntervalMin;
        public int FirmwareRevision;

        /// <summary>Gets the dew point from the Magnus formula, deg C.</summary>
        public double DewPointC
        {
            get
            {
                const double a = 17.62, b = 243.12;
                var rh = Math.Max(1.0, RelativeHumidityPct) / 100.0;
                var gamma = Math.Log(rh) + a * AirTemperatureC / (b + AirTemperatureC);
                return b * gamma / (a - gamma);
            }
        }
    }

    /// <summary>Represents the sensor unit's health report (device_status).</summary>
    public class DeviceStatus : TempestMessage
    {
        public int UptimeS;
        public double VoltageV;
        public int FirmwareRevision;
        public int RssiDbm;
        public int HubRssiDbm;
        public uint SensorStatus;

        /// <summary>Gets a value indicating whether the nine documented fault bits are all clear.</summary>
        public bool SensorsOk => (SensorStatus & 0x1FF) == 0;

        /// <summary>Gets the names of the faults the status word reports.</summary>
        public IEnumerable<string> SensorFaults
        {
            get
            {
                if ((SensorStatus & 0x001) != 0) yield return "lightning failed";
                if ((SensorStatus & 0x002) != 0) yield return "lightning noise";
                if ((SensorStatus & 0x004) != 0) yield return "lightning disturber";
                if ((SensorStatus & 0x008) != 0) yield return "pressure failed";
                if ((SensorStatus & 0x010) != 0) yield return "temperature failed";
                if ((SensorStatus & 0x020) != 0) yield return "humidity failed";
                if ((SensorStatus & 0x040) != 0) yield return "wind failed";
                if ((SensorStatus & 0x080) != 0) yield return "precip failed";
                if ((SensorStatus & 0x100) != 0) yield return "light/UV failed";
            }
        }
    }

    /// <summary>Represents the hub's health report (hub_status).</summary>
    public class HubStatus : TempestMessage
    {
        public string FirmwareRevision;
        public int UptimeS;
        public int RssiDbm;
        public string ResetFlags;
    }

    /// <summary>Represents the start of rain (evt_precip).</summary>
    public class PrecipEvent : TempestMessage
    {
    }

    /// <summary>Represents a lightning strike (evt_strike).</summary>
    public class LightningStrikeEvent : TempestMessage
    {
        public double DistanceKm;
        public double Energy;
    }

    /// <summary>
    /// Decodes Tempest hub datagrams into <see cref="TempestMessage"/> objects.
    /// </summary>
    public static class TempestParser
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Decodes one datagram.
        /// </summary>
        /// <returns>
        /// One message per observation it carries (an obs_st datagram can
        /// hold several), or an empty list for anything that is not a
        /// recognized Tempest message, including malformed JSON.
        /// </returns>
        public static IReadOnlyList<TempestMessage> Parse(string json)
        {
            var result = new List<TempestMessage>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch (Exception)
            {
                return result;
            }

            try
            {
                switch ((string)obj["type"])
                {
                    case "rapid_wind":
                        {
                            var ob = (JArray)obj["ob"];
                            result.Add(Fill(new RapidWind
                            {
                                TimeUtc = Time(ob[0]),
                                SpeedMs = Num(ob[1]),
                                DirectionDeg = Num(ob[2]),
                            }, obj));
                            break;
                        }
                    case "obs_st":
                        {
                            foreach (JArray ob in (JArray)obj["obs"])
                            {
                                result.Add(Fill(new StationObservation
                                {
                                    TimeUtc = Time(ob[0]),
                                    WindLullMs = Num(ob[1]),
                                    WindAvgMs = Num(ob[2]),
                                    WindGustMs = Num(ob[3]),
                                    WindDirectionDeg = Num(ob[4]),
                                    WindSampleIntervalS = Num(ob[5]),
                                    StationPressureHpa = Num(ob[6]),
                                    AirTemperatureC = Num(ob[7]),
                                    RelativeHumidityPct = Num(ob[8]),
                                    IlluminanceLux = Num(ob[9]),
                                    UvIndex = Num(ob[10]),
                                    SolarRadiationWm2 = Num(ob[11]),
                                    RainMm = Num(ob[12]),
                                    PrecipType = (PrecipType)((int?)ob[13] ?? 0),
                                    LightningAvgDistanceKm = Num(ob[14]),
                                    LightningStrikeCount = (int?)ob[15] ?? 0,
                                    BatteryV = Num(ob[16]),
                                    ReportIntervalMin = Num(ob[17]),
                                    FirmwareRevision = (int?)obj["firmware_revision"] ?? 0,
                                }, obj));
                            }
                            break;
                        }
                    case "device_status":
                        result.Add(Fill(new DeviceStatus
                        {
                            TimeUtc = Time(obj["timestamp"]),
                            UptimeS = (int)obj["uptime"],
                            VoltageV = (double)obj["voltage"],
                            FirmwareRevision = (int)obj["firmware_revision"],
                            RssiDbm = (int)obj["rssi"],
                            HubRssiDbm = (int)obj["hub_rssi"],
                            SensorStatus = (uint)obj["sensor_status"],
                        }, obj));
                        break;
                    case "hub_status":
                        result.Add(Fill(new HubStatus
                        {
                            TimeUtc = Time(obj["timestamp"]),
                            FirmwareRevision = (string)obj["firmware_revision"],
                            UptimeS = (int)obj["uptime"],
                            RssiDbm = (int)obj["rssi"],
                            ResetFlags = (string)obj["reset_flags"],
                        }, obj));
                        break;
                    case "evt_precip":
                        {
                            var evt = (JArray)obj["evt"];
                            result.Add(Fill(new PrecipEvent { TimeUtc = Time(evt[0]) }, obj));
                            break;
                        }
                    case "evt_strike":
                        {
                            var evt = (JArray)obj["evt"];
                            result.Add(Fill(new LightningStrikeEvent
                            {
                                TimeUtc = Time(evt[0]),
                                DistanceKm = Num(evt[1]),
                                Energy = Num(evt[2]),
                            }, obj));
                            break;
                        }
                }
            }
            catch (Exception)
            {
                // Recognized type with a field missing or of the wrong
                // shape: drop the whole datagram
                result.Clear();
            }

            return result;
        }

        static T Fill<T>(T msg, JObject obj) where T : TempestMessage
        {
            msg.SerialNumber = (string)obj["serial_number"];
            // Hub messages name themselves in serial_number and have no hub_sn
            msg.HubSerial = (string)obj["hub_sn"] ?? msg.SerialNumber;
            return msg;
        }

        static DateTime Time(JToken epochSeconds)
        {
            return Epoch.AddSeconds((double)epochSeconds);
        }

        /// <summary>Reads a numeric slot, NaN when the hub sent null.</summary>
        static double Num(JToken value)
        {
            return (double?)value ?? double.NaN;
        }
    }
}
