using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.GDL90
{
    [TestClass]
    public class Gdl90MessageTests
    {
        [TestMethod]
        public void TrafficReport_ReproducesIcdWorkedExample()
        {
            // ICD 3.5.2: target airborne over Salem OR. One assertion pins the
            // semicircle packing, altitude offset, misc nibble, NIC/NACp, the 12/12
            // velocity split, track scaling and callsign padding simultaneously.
            var report = new Gdl90Report
            {
                TrafficAlert = false,
                AddressType = Gdl90AddressType.AdsbIcao,
                Address = 0xAB4549, // octal 52642511
                LatitudeE7 = 449070800,    // 44.90708 N
                LongitudeE7 = -1229948800, // 122.99488 W
                PressureAltitudeFeet = 5000,
                Airborne = true,
                TrackType = Gdl90TrackType.TrueTrack,
                Nic = 10,
                Nacp = 9,
                HorizontalVelocityKnots = 123,
                VerticalVelocityFpm = 64,
                TrackDegrees = 45,
                EmitterCategory = 1, // Light
                Callsign = "N825V",
                EmergencyPriorityCode = 0,
            };

            byte[] expected = Gdl90TestUtil.FromHex(
                "1400AB45491FEF15A889780F09A907B00120014E3832355620202000");
            CollectionAssert.AreEqual(expected, Gdl90Messages.TrafficReport(report));
        }

        [TestMethod]
        public void OwnshipReport_DiffersFromTrafficOnlyInMessageId()
        {
            var report = new Gdl90Report
            {
                Address = 0x7C1234,
                LatitudeE7 = -336671870,
                LongitudeE7 = 1508544000,
                PressureAltitudeFeet = 3500,
                Nic = 11,
                Nacp = 11,
                HorizontalVelocityKnots = 65,
                TrackDegrees = 90,
                EmitterCategory = 14,
                Callsign = "VHABC123",
            };
            byte[] traffic = Gdl90Messages.TrafficReport(report);
            byte[] ownship = Gdl90Messages.OwnshipReport(report);

            Assert.AreEqual(0x14, traffic[0]);
            Assert.AreEqual(0x0A, ownship[0]);
            for (int i = 1; i < 28; i++)
            {
                Assert.AreEqual(traffic[i], ownship[i], $"body byte {i}");
            }
        }

        [TestMethod]
        public void Report_InvalidPositionZeroesLatLngAndNic()
        {
            // ICD 3.4 / 3.5.1.3: a target with no valid position has latitude,
            // longitude and NIC all set to zero.
            var report = new Gdl90Report
            {
                PositionValid = false,
                LatitudeE7 = 449070800,
                LongitudeE7 = -1229948800,
                Nic = 11,
                Nacp = 11,
                PressureAltitudeFeet = 0,
            };
            byte[] msg = Gdl90Messages.OwnshipReport(report);
            for (int i = 5; i <= 10; i++)
            {
                Assert.AreEqual(0x00, msg[i], $"lat/lng byte {i}");
            }
            Assert.AreEqual(0x0B, msg[13]); // NIC nibble zeroed, NACp kept
        }

        // --- Semicircle conversion (ICD 3.5.1.3) ---

        [DataTestMethod]
        [DataRow(0L, 0x000000)]                    // 00.000 N or E
        [DataRow(215L, 0x000001)]                  // 00.000 + LSB
        [DataRow(-215L, 0xFFFFFF)]                 // 00.000 - LSB
        [DataRow(450000000L, 0x200000)]            // 45.000 N or E
        [DataRow(-450000000L, 0xE00000)]           // 45.000 S or W
        [DataRow(900000000L, 0x400000)]            // 90.000 N or E
        [DataRow(-1800000000L, 0x800000)]          // -180.000
        [DataRow(449070800L, 0x1FEF15)]            // ICD worked example latitude (ICD 3.5.2)
        [DataRow(-1229948800L, 0xA88978)]          // ICD worked example longitude (ICD 3.5.2)
        public void DegreesE7ToSemicircles_MatchesIcdExamples(long degreesE7, int expected)
        {
            Assert.AreEqual(expected, Gdl90Messages.DegreesE7ToSemicircles(degreesE7));
        }

        // --- Altitude (ICD 3.5.1.4) ---

        [DataTestMethod]
        [DataRow(-1000, 0x000)]
        [DataRow(0, 0x028)]
        [DataRow(1000, 0x050)]
        [DataRow(101350, 0xFFE)]
        [DataRow(12, 0x028)]   // truncates, never rounds
        [DataRow(13, 0x028)]   // 1013/25 = 40.52 still truncates to 40
        [DataRow(-13, 0x027)]  // 987/25 = 39.48: offset-first keeps truncation == floor
        [DataRow(-1001, 0xFFF)]   // out of range is invalid, never clamped
        [DataRow(101351, 0xFFF)]
        public void AltitudeToOffsetInteger_MatchesIcd(int feet, int expected)
        {
            Assert.AreEqual(expected, Gdl90Messages.AltitudeToOffsetInteger(feet, true));
        }

        [TestMethod]
        public void AltitudeToOffsetInteger_InvalidGivesSentinel()
        {
            Assert.AreEqual(0xFFF, Gdl90Messages.AltitudeToOffsetInteger(5000, false));
        }

        // --- Horizontal velocity (ICD 3.5.1.7) ---

        [DataTestMethod]
        [DataRow(0, 0x000)]
        [DataRow(123, 0x07B)]
        [DataRow(4093, 0xFFD)]
        [DataRow(4094, 0xFFE)]  // holds at 0xFFE from 4,094 kt
        [DataRow(4095, 0xFFE)]  // 0xFFF is reserved for "no data", never a speed
        [DataRow(40000, 0xFFE)]
        [DataRow(-1, 0xFFF)]    // domain error reports no data
        public void HorizontalVelocityToField_SaturatesPerIcd(int knots, int expected)
        {
            Assert.AreEqual(expected, Gdl90Messages.HorizontalVelocityToField(knots, true));
        }

        [TestMethod]
        public void HorizontalVelocityToField_InvalidGivesSentinel()
        {
            Assert.AreEqual(0xFFF, Gdl90Messages.HorizontalVelocityToField(100, false));
        }

        // --- Vertical velocity (ICD 3.5.1.8) ---

        [DataTestMethod]
        [DataRow(0, 0x000)]
        [DataRow(64, 0x001)]
        [DataRow(-64, 0xFFF)]
        [DataRow(63, 0x000)]    // truncates toward zero...
        [DataRow(-63, 0x000)]   // ...on both sides of zero (no flooring)
        [DataRow(32576, 0x1FD)]
        [DataRow(32577, 0x1FE)]  // beyond the range holds at +/-32,640 fpm
        [DataRow(99999, 0x1FE)]
        [DataRow(-32576, 0xE03)]
        [DataRow(-32577, 0xE02)]
        [DataRow(-99999, 0xE02)]
        public void VerticalVelocityToField_MatchesIcdExamples(int fpm, int expected)
        {
            Assert.AreEqual(expected, Gdl90Messages.VerticalVelocityToField(fpm, true));
        }

        [TestMethod]
        public void VerticalVelocityToField_InvalidGivesSentinel()
        {
            Assert.AreEqual(0x800, Gdl90Messages.VerticalVelocityToField(0, false));
        }

        // --- Track (ICD 3.5.1.9) ---

        [DataTestMethod]
        [DataRow(0.0, 0x00)]
        [DataRow(45.0, 0x20)]
        [DataRow(90.0, 0x40)]
        [DataRow(180.0, 0x80)]
        [DataRow(359.0, 0xFF)]
        [DataRow(360.0, 0x00)]
        [DataRow(-90.0, 0xC0)]   // normalized into [0, 360)
        [DataRow(1.0, 0x00)]     // truncates, never rounds
        [DataRow(105.0, 0x4A)]   // 74.67 units truncates to 74
        public void TrackToField_TruncatesPerIcd(double degrees, int expected)
        {
            Assert.AreEqual((byte)expected, Gdl90Messages.TrackToField(degrees));
        }

        // --- Callsign (ICD 3.5.1.11) ---

        [DataTestMethod]
        [DataRow("N825V", "N825V   ")]
        [DataRow("VH-ABC", "VHABC   ")]   // registration loses its hyphen, matching the transponder's flight ID
        [DataRow("vhabc", "VHABC   ")]
        [DataRow("AB CD", "ABCD    ")]    // space is only ever a trailing pad
        [DataRow("ABCDEFGHIJ", "ABCDEFGH")]
        [DataRow("", "        ")]
        [DataRow(null, "        ")]
        public void CallsignToBytes_SanitizesAndPads(string callsign, string expected)
        {
            var bytes = Gdl90Messages.CallsignToBytes(callsign);
            Assert.AreEqual(8, bytes.Length);
            Assert.AreEqual(expected, System.Text.Encoding.ASCII.GetString(bytes));
        }

        // --- Ownship geometric altitude (ICD 3.8) ---

        [DataTestMethod]
        [DataRow(-1000, 0xFF, 0x38)]
        [DataRow(0, 0x00, 0x00)]
        [DataRow(1000, 0x00, 0xC8)]
        public void OwnshipGeometricAltitude_MatchesIcdAltitudeExamples(int feet, int hi, int lo)
        {
            byte[] msg = Gdl90Messages.OwnshipGeometricAltitude(
                new Gdl90GeometricAltitude { GeoAltitudeFeet = feet });
            Assert.AreEqual(5, msg.Length);
            Assert.AreEqual(0x0B, msg[0]);
            Assert.AreEqual((byte)hi, msg[1]);
            Assert.AreEqual((byte)lo, msg[2]);
        }

        [TestMethod]
        public void OwnshipGeometricAltitude_VerticalMetricsMatchIcdExamples()
        {
            // Vertical warning, VFOM not available: 0xFFFF
            byte[] msg = Metrics(warning: true, vfom: null);
            Assert.AreEqual(0xFF, msg[3]);
            Assert.AreEqual(0xFF, msg[4]);

            // No warning, VFOM = 40,000 m: 0x7FFE
            msg = Metrics(warning: false, vfom: 40000);
            Assert.AreEqual(0x7F, msg[3]);
            Assert.AreEqual(0xFE, msg[4]);

            // No warning, VFOM = 10 m: 0x000A
            msg = Metrics(warning: false, vfom: 10);
            Assert.AreEqual(0x00, msg[3]);
            Assert.AreEqual(0x0A, msg[4]);

            // Warning, VFOM = 50 m: 0x8032
            msg = Metrics(warning: true, vfom: 50);
            Assert.AreEqual(0x80, msg[3]);
            Assert.AreEqual(0x32, msg[4]);
        }

        static byte[] Metrics(bool warning, int? vfom)
        {
            return Gdl90Messages.OwnshipGeometricAltitude(new Gdl90GeometricAltitude
            {
                VerticalWarning = warning,
                VerticalFigureOfMeritMeters = vfom,
            });
        }

        // --- ForeFlight ID (0x65 sub-ID 0) ---

        [TestMethod]
        public void ForeFlightIdMessage_LayoutMatchesForeFlightSpec()
        {
            byte[] msg = Gdl90Messages.ForeFlightIdMessage(new Gdl90ForeFlightId
            {
                SerialNumber = 0x0102030405060708UL,
                DeviceName = "CX-GCS",
                DeviceLongName = "Carbonix GCS",
                Capabilities = Gdl90ForeFlightId.Capability.GeoAltitudeIsMsl,
            });

            Assert.AreEqual(39, msg.Length);
            Assert.AreEqual(0x65, msg[0]);
            Assert.AreEqual(0x00, msg[1]); // sub-ID 0
            Assert.AreEqual(0x01, msg[2]); // version 1

            // Serial number, big-endian.
            CollectionAssert.AreEqual(
                Gdl90TestUtil.FromHex("0102030405060708"),
                Gdl90TestUtil.Slice(msg, 3, 8));

            // Device name, space-padded to 8.
            Assert.AreEqual("CX-GCS  ", System.Text.Encoding.UTF8.GetString(msg, 11, 8));
            // Device long name, space-padded to 16.
            Assert.AreEqual("Carbonix GCS    ", System.Text.Encoding.UTF8.GetString(msg, 19, 16));

            // Capabilities mask, big-endian: MSL bit lands in the final byte.
            Assert.AreEqual(0x00, msg[35]);
            Assert.AreEqual(0x00, msg[36]);
            Assert.AreEqual(0x00, msg[37]);
            Assert.AreEqual(0x01, msg[38]);
        }

        [TestMethod]
        public void ForeFlightIdMessage_InvalidSerialIsAllOnes()
        {
            byte[] msg = Gdl90Messages.ForeFlightIdMessage(new Gdl90ForeFlightId
            {
                SerialNumber = 0xFFFFFFFFFFFFFFFFUL,
                DeviceName = null,
                DeviceLongName = null,
                Capabilities = Gdl90ForeFlightId.Capability.None,
            });
            for (int i = 3; i < 11; i++)
            {
                Assert.AreEqual(0xFF, msg[i], $"serial byte {i}");
            }
        }

        /// <summary>
        /// ICD 2.2.4: the message the worked example frames. One assertion pins both
        /// status bytes, the timestamp's byte order and the message counts together.
        /// </summary>
        [TestMethod]
        public void Heartbeat_ReproducesIcdWorkedExample()
        {
            byte[] expected = Gdl90TestUtil.FromHex("008141DBD00802");

            CollectionAssert.AreEqual(expected, Gdl90Messages.Heartbeat(new Gdl90Heartbeat
            {
                GpsPositionValid = true,
                UtcTimingValid = true,
                SecondsSinceUtcMidnight = 0xD0DB,
                CsaRequested = true,
                UplinkMessageCount = 1,
                BasicLongMessageCount = 2,
            }));
        }

        [TestMethod]
        public void Heartbeat_MessageCountsMatchIcdExample()
        {
            // ICD 3.1.4: 4 uplinks and 567 basic/long gives bytes 0x22 0x37.
            byte[] msg = Gdl90Messages.Heartbeat(new Gdl90Heartbeat
            {
                GpsPositionValid = true,
                UtcTimingValid = true,
                SecondsSinceUtcMidnight = 0,
                UplinkMessageCount = 4,
                BasicLongMessageCount = 567,
            });
            Assert.AreEqual(0x22, msg[5]);
            Assert.AreEqual(0x37, msg[6]);
        }

        [TestMethod]
        public void Heartbeat_TimestampBit16LivesInStatusByte2()
        {
            // 0x10000 seconds: bit 16 set, low 16 bits zero.
            byte[] msg = Gdl90Messages.Heartbeat(new Gdl90Heartbeat
            {
                SecondsSinceUtcMidnight = 0x10000,
            });
            Assert.AreEqual(0x80, msg[2] & 0x80);
            Assert.AreEqual(0x00, msg[3]);
            Assert.AreEqual(0x00, msg[4]);
        }
    }
}
