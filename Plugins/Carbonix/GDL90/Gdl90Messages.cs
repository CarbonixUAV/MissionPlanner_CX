using System;
using System.Text;
using Newtonsoft.Json;

namespace Carbonix.GDL90
{
    /// <summary>Specifies the type of participant address a report carries (ICD section 3.5.1.2).</summary>
    public enum Gdl90AddressType
    {
        AdsbIcao = 0,
        AdsbSelfAssigned = 1,
        TisbIcao = 2,
        TisbTrackFile = 3,
        SurfaceVehicle = 4,
        GroundStationBeacon = 5,
    }

    /// <summary>Specifies which quantity a report's track field carries (ICD section 3.5.1.5).</summary>
    public enum Gdl90TrackType
    {
        Invalid = 0,
        TrueTrack = 1,
        MagneticHeading = 2,
        TrueHeading = 3,
    }

    /// <summary>
    /// Defines a GDL 90 message's name and how to encode it.
    /// </summary>
    public interface IGdl90Message
    {
        /// <summary>The name the frame log files this message under.</summary>
        string Name { get; }

        /// <summary>Builds the clear message.</summary>
        /// <returns>The Message ID and data, without a frame check sequence.</returns>
        byte[] Encode();
    }

    /// <summary>Represents the body shared by the Ownship (ID 10) and Traffic (ID 20) reports.</summary>
    public class Gdl90Report
    {
        /// <summary>24-bit participant address.</summary>
        [JsonProperty("address")] public int Address;
        [JsonProperty("address_type")] public Gdl90AddressType AddressType = Gdl90AddressType.AdsbIcao;
        [JsonProperty("traffic_alert")] public bool TrafficAlert;
        /// <summary>
        /// A value indicating whether the report carries a position
        /// </summary>
        /// <remarks>
        /// If true, zeroes the latitude, longitude and NIC fields during encoding
        /// (ICD sections 3.4 and 3.5.1.3).
        /// </remarks>
        [JsonProperty("position_valid")] public bool PositionValid = true;
        /// <summary>Latitude in degrees times 1e7, as MAVLink carries it.</summary>
        [JsonProperty("latitude_dege7")] public long LatitudeE7;
        /// <summary>Longitude in degrees times 1e7, as MAVLink carries it.</summary>
        [JsonProperty("longitude_dege7")] public long LongitudeE7;
        [JsonProperty("altitude_valid")] public bool AltitudeValid = true;
        /// <summary>Pressure altitude in feet (i.e. non-QNH-corrected baro).</summary>
        [JsonProperty("pressure_altitude_ft")] public int PressureAltitudeFeet;
        [JsonProperty("airborne")] public bool Airborne = true;
        [JsonProperty("extrapolated")] public bool Extrapolated;
        [JsonProperty("track_type")] public Gdl90TrackType TrackType = Gdl90TrackType.TrueTrack;
        [JsonProperty("nic")] public int Nic;
        [JsonProperty("nacp")] public int Nacp;
        [JsonProperty("horizontal_velocity_valid")] public bool HorizontalVelocityValid = true;
        [JsonProperty("horizontal_velocity_kt")] public int HorizontalVelocityKnots;
        [JsonProperty("vertical_velocity_valid")] public bool VerticalVelocityValid = true;
        /// <summary>Climb rate in feet per minute.</summary>
        [JsonProperty("vertical_velocity_fpm")] public int VerticalVelocityFpm;
        [JsonProperty("track_deg")] public double TrackDegrees;
        /// <summary>Emitter category per ICD Table 11</summary>
        /// <remarks> Identical to MAVLink ADSB_EMITTER_TYPE over 0-19.</remarks>
        [JsonProperty("emitter_category")] public int EmitterCategory;
        [JsonProperty("callsign")] public string Callsign;
        /// <summary>Emergency/priority code per ICD section 3.5.1.12.</summary>
        [JsonProperty("emergency_priority_code")] public int EmergencyPriorityCode;
    }

    /// <summary>Represents an Ownship report (ID 10, ICD section 3.4).</summary>
    public class Gdl90OwnshipReport : Gdl90Report, IGdl90Message
    {
        /// <inheritdoc/>
        [JsonIgnore] public string Name { get { return "ownship"; } }

        /// <inheritdoc/>
        public byte[] Encode()
        {
            return Gdl90Messages.OwnshipReport(this);
        }
    }

    /// <summary>Represents a Traffic report (ID 20, ICD section 3.5).</summary>
    public class Gdl90TrafficReport : Gdl90Report, IGdl90Message
    {
        /// <inheritdoc/>
        [JsonIgnore] public string Name { get { return "traffic"; } }

        /// <summary>
        /// How far the position was carried forward to reach this report; logged
        /// rather than transmitted.
        /// </summary>
        [JsonProperty("age_s")] public double AgeSeconds;

        /// <summary>
        /// The aircraft's own collision assessment, from COLLISION; logged rather than
        /// transmitted.
        /// </summary>
        [JsonProperty("threat_level")] public int ThreatLevel;

        /// <inheritdoc/>
        public byte[] Encode()
        {
            return Gdl90Messages.TrafficReport(this);
        }
    }

    /// <summary>Represents a Heartbeat message (ID 0).</summary>
    public class Gdl90Heartbeat : IGdl90Message
    {
        /// <inheritdoc/>
        [JsonIgnore] public string Name { get { return "heartbeat"; } }

        /// <summary>Whether a valid ownship position is available.</summary>
        [JsonProperty("gps_position_valid")] public bool GpsPositionValid { get; set; }

        /// <summary>Whether the timestamp is referenced to UTC.</summary>
        [JsonProperty("utc_timing_valid")] public bool UtcTimingValid { get; set; }

        /// <summary>Time of day in whole seconds; 17 bits.</summary>
        [JsonProperty("seconds_since_utc_midnight")] public int SecondsSinceUtcMidnight { get; set; }

        /// <summary>Always set, per ICD section 3.1.1.h.</summary>
        [JsonProperty("uat_initialized")] public bool UatInitialized { get { return true; } }

        /// <summary>Whether conflict situational awareness has been requested.</summary>
        [JsonProperty("csa_requested")] public bool CsaRequested { get; set; }

        /// <summary>Uplink messages received in the last second, 0 to 31.</summary>
        [JsonProperty("uplink_message_count")] public int UplinkMessageCount { get; set; }

        /// <summary>Basic and Long messages received in the last second, 0 to 1023.</summary>
        [JsonProperty("basic_long_message_count")] public int BasicLongMessageCount { get; set; }

        /// <inheritdoc/>
        public byte[] Encode()
        {
            return Gdl90Messages.Heartbeat(this);
        }
    }

    /// <summary>Represents an Ownship Geometric Altitude message (ID 11).</summary>
    /// <remarks>
    /// ICD section 3.8 defines this field as height above the WGS-84 ellipsoid. However, you can
    /// instead supply height above mean sea level if you also send the ForeFlight ID message
    /// (ID 0x65, sub-ID 0) with the GeoAltitudeIsMsl capability bit set.
    /// </remarks>
    public class Gdl90GeometricAltitude : IGdl90Message
    {
        /// <inheritdoc/>
        [JsonIgnore] public string Name { get { return "ownship_geo_altitude"; } }

        /// <summary>Altitude above mean sea level in feet, encoded at 5 ft resolution.</summary>
        [JsonProperty("geometric_altitude_ft")] public int GeoAltitudeFeet { get; set; }

        /// <summary>Whether a position alarm is present or fault detection is unavailable.</summary>
        [JsonProperty("vertical_warning")] public bool VerticalWarning { get; set; }

        /// <summary>Vertical figure of merit in meters, or null when unavailable.</summary>
        [JsonProperty("vertical_figure_of_merit_m")] public int? VerticalFigureOfMeritMeters { get; set; }

        /// <inheritdoc/>
        public byte[] Encode()
        {
            return Gdl90Messages.OwnshipGeometricAltitude(this);
        }
    }

    /// <summary>Represents a ForeFlight ID message (ID 0x65, sub-ID 0).</summary>
    public class Gdl90ForeFlightId : IGdl90Message
    {
        /// <summary>Bits of the ForeFlight capabilities mask.</summary>
        [Flags]
        public enum Capability : uint
        {
            /// <summary>Nothing claimed.</summary>
            None = 0,

            /// <summary>
            /// Bit 0: the ownship geometric altitude is MSL rather than WGS-84 ellipsoid height.
            /// </summary>
            GeoAltitudeIsMsl = 1u << 0,
        }

        /// <inheritdoc/>
        [JsonIgnore] public string Name { get { return "foreflight_id"; } }

        /// <summary>Device serial number, or all ones when unavailable.</summary>
        [JsonProperty("serial_number")] public ulong SerialNumber { get; set; }

        /// <summary>Short device name, truncated or space-padded to 8 bytes.</summary>
        [JsonProperty("device_name")] public string DeviceName { get; set; }

        /// <summary>Long device name, truncated or space-padded to 16 bytes.</summary>
        [JsonProperty("device_long_name")] public string DeviceLongName { get; set; }

        /// <summary>What the device tells the receiver it does.</summary>
        [JsonProperty("capabilities")] public Capability Capabilities { get; set; }

        /// <inheritdoc/>
        public byte[] Encode()
        {
            return Gdl90Messages.ForeFlightIdMessage(this);
        }
    }

    /// <summary>Provides builders for the GDL 90 messages this plugin transmits.</summary>
    /// <remarks>
    /// Each builder returns the clear message (Message ID and data), ready for
    /// <see cref="Gdl90Frame.Frame"/>. Multi-byte fields are big-endian, except the
    /// heartbeat timestamp (ICD section 3.1.3).
    /// </remarks>
    public static class Gdl90Messages
    {
        public const byte HeartbeatId = 0x00;
        public const byte OwnshipReportId = 0x0A;
        public const byte OwnshipGeometricAltitudeId = 0x0B;
        public const byte TrafficReportId = 0x14;
        public const byte ForeFlightId = 0x65;


        /// <summary>Builds the Heartbeat message (ID 0) per ICD section 3.1.</summary>
        /// <param name="h">The values to encode.</param>
        /// <returns>The clear message.</returns>
        internal static byte[] Heartbeat(Gdl90Heartbeat h)
        {
            if (h == null) throw new ArgumentNullException(nameof(h));

            int timestamp = h.SecondsSinceUtcMidnight & 0x1FFFF;
            int uplink = Clamp(h.UplinkMessageCount, 0, 31);
            int basicLong = Clamp(h.BasicLongMessageCount, 0, 1023);

            var msg = new byte[7];
            msg[0] = HeartbeatId;

            // Status byte 1
            msg[1] = (byte)(
                (h.GpsPositionValid ? 1 << 7 : 0) |
                // Bits 6..2: not implemented by us
                // Bit 1: reserved (0)
                (1 << 0)); // UAT Initialized, always set (ICD section 3.1.1.h).

            // Status byte 2
            msg[2] = (byte)(
                ((timestamp >> 16) << 7) | // Bit 7: UAT timestamp's 17th bit
                (h.CsaRequested ? 1 << 6 : 0) | // Bit 6: CSA requested
                // Bit 5 not implemented by us
                // Bit 4..1 reserved (0)
                (h.UtcTimingValid ? 1 << 0 : 0)); // Bit 0: UTC timing valid
            // Timestamp bits 15..0, least significant byte first (ICD 3.1.3).
            msg[3] = (byte)(timestamp & 0xFF);
            msg[4] = (byte)((timestamp >> 8) & 0xFF);
            // Message counts (ICD section 3.1.4)
            msg[5] = (byte)((uplink << 3) | (basicLong >> 8));
            msg[6] = (byte)(basicLong & 0xFF);
            return msg;
        }

        /// <summary>Builds the Ownship report (ID 10) per ICD section 3.4.</summary>
        /// <param name="report">The values to encode.</param>
        /// <returns>The clear message.</returns>
        /// <remarks>Uses the Traffic report body; only the Message ID differs.</remarks>
        internal static byte[] OwnshipReport(Gdl90Report report)
        {
            return BuildReport(OwnshipReportId, report);
        }

        /// <summary>Builds the Traffic report (ID 20) per ICD section 3.5.</summary>
        /// <param name="report">The values to encode.</param>
        /// <returns>The clear message.</returns>
        internal static byte[] TrafficReport(Gdl90Report report)
        {
            return BuildReport(TrafficReportId, report);
        }

        private static byte[] BuildReport(byte messageId, Gdl90Report r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            // ICD 3.4 and 3.5.1.3: no valid position is reported as zeroed latitude,
            // longitude and NIC.
            int lat = r.PositionValid ? DegreesE7ToSemicircles(r.LatitudeE7) : 0;
            int lng = r.PositionValid ? DegreesE7ToSemicircles(r.LongitudeE7) : 0;
            int nic = r.PositionValid ? r.Nic & 0xF : 0;
            int altitude = AltitudeToOffsetInteger(r.PressureAltitudeFeet, r.AltitudeValid);
            int hVelocity = HorizontalVelocityToField(r.HorizontalVelocityKnots, r.HorizontalVelocityValid);
            int vVelocity = VerticalVelocityToField(r.VerticalVelocityFpm, r.VerticalVelocityValid);
            // Miscellaneous flags (ICD section 3.5.1.5)
            int misc =
                (r.Airborne ? 1 << 3 : 0) | // Bit 3: Airborne
                (r.Extrapolated ? 1 << 2 : 0) | // Bit 2: Extrapolated
                ((int)r.TrackType & 0x3); // Bit 1..0: Track type

            var msg = new byte[28];
            msg[0] = messageId;
            msg[1] = (byte)(((r.TrafficAlert ? 1 : 0) << 4) | ((int)r.AddressType & 0xF));
            msg[2] = (byte)((r.Address >> 16) & 0xFF);
            msg[3] = (byte)((r.Address >> 8) & 0xFF);
            msg[4] = (byte)(r.Address & 0xFF);
            msg[5] = (byte)((lat >> 16) & 0xFF);
            msg[6] = (byte)((lat >> 8) & 0xFF);
            msg[7] = (byte)(lat & 0xFF);
            msg[8] = (byte)((lng >> 16) & 0xFF);
            msg[9] = (byte)((lng >> 8) & 0xFF);
            msg[10] = (byte)(lng & 0xFF);
            msg[11] = (byte)((altitude >> 4) & 0xFF);
            msg[12] = (byte)(((altitude & 0xF) << 4) | misc);
            msg[13] = (byte)((nic << 4) | (r.Nacp & 0xF));
            msg[14] = (byte)((hVelocity >> 4) & 0xFF);
            msg[15] = (byte)(((hVelocity & 0xF) << 4) | ((vVelocity >> 8) & 0xF));
            msg[16] = (byte)(vVelocity & 0xFF);
            msg[17] = TrackToField(r.TrackDegrees);
            msg[18] = (byte)r.EmitterCategory;
            byte[] callsign = CallsignToBytes(r.Callsign);
            Array.Copy(callsign, 0, msg, 19, 8);
            msg[27] = (byte)((r.EmergencyPriorityCode & 0xF) << 4); // Last nibble reserved (0)
            return msg;
        }

        /// <summary>Builds the Ownship Geometric Altitude message (ID 11) per ICD section 3.8.</summary>
        /// <param name="a">The values to encode.</param>
        /// <returns>The clear message.</returns>
        internal static byte[] OwnshipGeometricAltitude(Gdl90GeometricAltitude a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));

            int altitude = Clamp(a.GeoAltitudeFeet / 5, short.MinValue, short.MaxValue);

            int vfom;
            if (a.VerticalFigureOfMeritMeters == null)
                vfom = 0x7FFF;
            else if (a.VerticalFigureOfMeritMeters.Value >= 32766)
                vfom = 0x7FFE;
            else
                vfom = Clamp(a.VerticalFigureOfMeritMeters.Value, 0, 0x7FFD);
            int metrics = (a.VerticalWarning ? 0x8000 : 0) | vfom;

            return new byte[]
            {
                OwnshipGeometricAltitudeId,
                (byte)((altitude >> 8) & 0xFF),
                (byte)(altitude & 0xFF),
                (byte)(metrics >> 8),
                (byte)(metrics & 0xFF),
            };
        }

        /// <summary>Builds the ForeFlight ID message (ID 0x65, sub-ID 0).</summary>
        /// <param name="id">The values to encode.</param>
        /// <returns>The clear message.</returns>
        /// <remarks>
        /// Defined by the ForeFlight GDL 90 extended specification rather than by the
        /// ICD. Names are encoded as UTF-8.
        /// </remarks>
        internal static byte[] ForeFlightIdMessage(Gdl90ForeFlightId id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));

            var msg = new byte[39];
            msg[0] = ForeFlightId;
            msg[1] = 0x00; // Sub-ID 0
            msg[2] = 0x01; // Version 1
            for (int i = 0; i < 8; i++)
            {
                msg[3 + i] = (byte)(id.SerialNumber >> (8 * (7 - i)));
            }
            WritePaddedUtf8(id.DeviceName, msg, 11, 8);
            WritePaddedUtf8(id.DeviceLongName, msg, 19, 16);
            for (int i = 0; i < 4; i++)
            {
                msg[35 + i] = (byte)((uint)id.Capabilities >> (8 * (3 - i)));
            }
            return msg;
        }

        private static void WritePaddedUtf8(string value, byte[] dest, int offset, int width)
        {
            for (int i = 0; i < width; i++) dest[offset + i] = 0x20;
            if (string.IsNullOrEmpty(value)) return;
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            Array.Copy(encoded, 0, dest, offset, Math.Min(encoded.Length, width));
        }

        /// <summary>
        /// Converts degrees times 1e7 to the 24-bit semicircle field of ICD section
        /// 3.5.1.3, truncated toward zero.
        /// </summary>
        internal static int DegreesE7ToSemicircles(long degreesE7)
        {
            // Int64 throughout. The quotient is exact, where a float32 intermediate
            // loses the last bit and a rounded double shifts every value that lands
            // near an integer boundary.
            return (int)(degreesE7 * 8388608L / 1800000000L) & 0xFFFFFF;
        }

        /// <summary>Feet per count of the 12-bit altitude field (ICD 3.5.1.4).</summary>
        private const int AltitudeQuantumFeet = 25;

        /// <summary>0xFFF is the "no data" sentinel, so 0xFFE is the largest encodable code.</summary>
        private const int MaximumAltitudeCode = 0xFFE;

        /// <summary>Lowest altitude the field encodes, at code zero (ICD 3.5.1.4).</summary>
        public const int MinimumAltitudeFeet = -1000;

        /// <summary>Highest altitude the field encodes, at the largest code: 101,350 ft.</summary>
        public const int MaximumAltitudeFeet =
            MaximumAltitudeCode * AltitudeQuantumFeet + MinimumAltitudeFeet;

        /// <summary>
        /// Converts pressure altitude to the 12-bit offset integer of ICD section
        /// 3.5.1.4.
        /// </summary>
        /// <remarks>An altitude outside the encodable range emits the 0xFFF sentinel.</remarks>
        internal static int AltitudeToOffsetInteger(int feet, bool valid)
        {
            // The sentinel rather than a clamp: a clamp reports a confidently wrong
            // altitude, where the sentinel reports no altitude at all.
            if (!valid || feet < MinimumAltitudeFeet || feet > MaximumAltitudeFeet) return 0xFFF;

            // Offset before dividing, so the dividend is non-negative for every valid
            // altitude and truncation cannot depend on integer-division semantics.
            return (feet - MinimumAltitudeFeet) / AltitudeQuantumFeet;
        }

        /// <summary>
        /// Converts horizontal velocity to the 12-bit unsigned field of ICD section
        /// 3.5.1.7.
        /// </summary>
        /// <remarks>Holds at 0xFFE from 4,094 kt; 0xFFF means no data.</remarks>
        internal static int HorizontalVelocityToField(int knots, bool valid)
        {
            if (!valid || knots < 0) return 0xFFF;
            if (knots >= 4094) return 0xFFE;
            return knots;
        }

        /// <summary>
        /// Converts vertical velocity to the 12-bit signed field of ICD section 3.5.1.8,
        /// in 64 fpm units truncated toward zero.
        /// </summary>
        /// <remarks>
        /// Beyond +/-32,576 fpm holds at the value representing +/-32,640 fpm; 0x800
        /// means no data.
        /// </remarks>
        internal static int VerticalVelocityToField(int fpm, bool valid)
        {
            if (!valid) return 0x800;
            int units;
            if (fpm > 32576) units = 510;
            else if (fpm < -32576) units = -510;
            else units = fpm / 64;
            return units & 0xFFF;
        }

        /// <summary>
        /// Converts a track or heading to the 8-bit angular field of ICD section
        /// 3.5.1.9, in 360/256 degree units truncated toward zero.
        /// </summary>
        internal static byte TrackToField(double degrees)
        {
            double normalized = degrees % 360.0;
            if (normalized < 0) normalized += 360.0;
            return (byte)((int)(normalized * 256.0 / 360.0) & 0xFF);
        }

        /// <summary>Converts a callsign to the 8-byte field of ICD section 3.5.1.11.</summary>
        /// <remarks>
        /// Characters outside A-Z and 0-9 are dropped after upper-casing, so a VH-ABC
        /// registration becomes VHABC, and the result is padded with trailing spaces.
        /// </remarks>
        internal static byte[] CallsignToBytes(string callsign)
        {
            var result = new byte[8];
            int n = 0;
            if (callsign != null)
            {
                foreach (char raw in callsign)
                {
                    char c = char.ToUpperInvariant(raw);
                    if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    {
                        result[n++] = (byte)c;
                        if (n == 8) break;
                    }
                }
            }
            for (; n < 8; n++) result[n] = 0x20;
            return result;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
