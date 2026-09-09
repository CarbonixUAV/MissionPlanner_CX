using System;
using System.IO;
using System.Linq;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// Turning a recorded flight into a frame log, transmitting nothing.
    /// </summary>
    [TestClass]
    public class Gdl90TlogConverterTests
    {
        /// <summary>
        /// Somewhere in the past, so a timestamp taken from the recording is
        /// distinguishable from one taken from the clock.
        /// </summary>
        static readonly DateTime Recorded = new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc);

        const int FixtureLatE7 = -338688000;
        const int FixtureLonE7 = 1512093000;

        string _folder;
        string _tlog;

        [TestInitialize]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CbxGdl90Cnv_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
            _tlog = Path.Combine(_folder, "2026-01-15 02-30-00.tlog");
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Writes a tlog the way Mission Planner does: eight big-endian bytes of
        /// microseconds since the epoch, then the raw packet
        /// (<c>MAVLinkInterface.SaveToTlog</c>).
        /// </summary>
        sealed class TlogWriter : IDisposable
        {
            static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            readonly FileStream _file;
            readonly MAVLink.MavlinkParse _parse = new MAVLink.MavlinkParse();

            public TlogWriter(string path)
            {
                _file = File.Open(path, FileMode.Append, FileAccess.Write);
            }

            public void Write(DateTime utc, MAVLink.MAVLINK_MSG_ID id, object data)
            {
                byte[] packet = _parse.GenerateMAVLinkPacket20(id, data, false, 1, 1);
                byte[] stamp = BitConverter.GetBytes(
                    (ulong)((utc - Epoch).TotalMilliseconds * 1000));
                Array.Reverse(stamp);

                _file.Write(stamp, 0, stamp.Length);
                _file.Write(packet, 0, packet.Length);
            }

            public void Dispose()
            {
                _file.Dispose();
            }
        }

        /// <summary>
        /// A parked aircraft with a fix, reporting once a second for
        /// <paramref name="seconds"/>, optionally with one ADS-B target in view.
        /// </summary>
        void WriteFixture(int seconds, bool withTraffic = false, TimeSpan gapAfter = default(TimeSpan),
            int gapAtSecond = -1)
        {
            using (var tlog = new TlogWriter(_tlog))
            {
                var when = Recorded;
                for (int i = 0; i < seconds; i++)
                {
                    if (i == gapAtSecond) when += gapAfter;

                    tlog.Write(when, MAVLink.MAVLINK_MSG_ID.HEARTBEAT,
                        new MAVLink.mavlink_heartbeat_t
                        {
                            type = (byte)MAVLink.MAV_TYPE.FIXED_WING,
                            autopilot = (byte)MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA,
                            base_mode = (byte)MAVLink.MAV_MODE_FLAG.SAFETY_ARMED,
                            system_status = (byte)MAVLink.MAV_STATE.ACTIVE,
                            mavlink_version = 3,
                        });

                    tlog.Write(when, MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT,
                        new MAVLink.mavlink_gps_raw_int_t
                        {
                            fix_type = 3,
                            satellites_visible = 14,
                            lat = FixtureLatE7,
                            lon = FixtureLonE7,
                            alt = 400000,
                            eph = 80,
                        });

                    tlog.Write(when, MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
                        new MAVLink.mavlink_global_position_int_t
                        {
                            lat = FixtureLatE7,
                            lon = FixtureLonE7,
                            alt = 400000,
                            relative_alt = 0,
                            hdg = 13000,
                        });

                    tlog.Write(when, MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE,
                        new MAVLink.mavlink_scaled_pressure_t { press_abs = 966.5f });

                    if (withTraffic)
                    {
                        tlog.Write(when, MAVLink.MAVLINK_MSG_ID.ADSB_VEHICLE,
                            Gdl90TrafficMappingTests.Target(icao: 0x7C4B1A));
                    }

                    when += TimeSpan.FromSeconds(1);
                }
            }
        }

        static Gdl90Configuration Configured()
        {
            return new Gdl90Configuration
            {
                Identity = new Gdl90OwnshipIdentity { IcaoAddress = 0x7C1A2B, Callsign = "VHTEST" },
                MissionPlannerVersion = "1.3.83",
                PluginVersion = "2.4",
            };
        }

        Gdl90LogReader Convert(out Gdl90ConversionResult result, bool overwrite = false)
        {
            result = Gdl90TlogConverter.Convert(_tlog, Configured(), overwrite);
            if (result.Error != null) Assert.Fail("conversion error: " + result.Error);
            Assert.IsNotNull(result.OutputPath);
            return Gdl90LogReader.Read(result.OutputPath);
        }

        [TestMethod]
        public void Convert_WritesTheFrameLogBesideTheTlog()
        {
            WriteFixture(seconds: 3);

            Gdl90ConversionResult result;
            Convert(out result);

            Assert.AreEqual(Gdl90FrameLog.CompanionPath(_tlog), result.OutputPath);
            Assert.IsTrue(File.Exists(result.OutputPath));
        }

        [TestMethod]
        public void Convert_EmitsTheOwnshipStream()
        {
            WriteFixture(seconds: 3);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.IsTrue(log.FramesOf("heartbeat").Any());
            Assert.IsTrue(log.FramesOf("ownship").Any());
            Assert.IsTrue(log.FramesOf("ownship_geo_altitude").Any());
            Assert.IsTrue(log.FramesOf("foreflight_id").Any());
        }

        /// <summary>
        /// The point of the whole converter: a recorded flight with traffic in it is
        /// what acceptance works against, and a simulator cannot produce that coverage.
        /// </summary>
        [TestMethod]
        public void Convert_ATlogWithTraffic_YieldsTrafficReports()
        {
            WriteFixture(seconds: 4, withTraffic: true);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            var traffic = log.FramesOf("traffic").ToList();
            Assert.IsTrue(traffic.Count > 0, "no traffic reports");
            Assert.AreEqual(0x7C4B1AL, (long)traffic[0]["address"]);
            Assert.IsTrue(result.TrafficFrames > 0);

            // Targets age in recorded time. On the wall clock every one of them would
            // be months stale by the time the conversion reached it, and the output
            // would be ownship only.
            foreach (var report in traffic)
            {
                Assert.IsTrue((double)report["age_s"] < Gdl90TrafficTracker.DropAfter.TotalSeconds,
                    "target aged against the wall clock");
            }
        }

        /// <summary>
        /// One frame set per second of recorded time, so an hour of tlog is an hour of
        /// frame sets however long the conversion itself takes.
        /// </summary>
        [TestMethod]
        public void Convert_EmitsOneFrameSetPerRecordedSecond()
        {
            WriteFixture(seconds: 5);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.AreEqual(5, log.FramesOf("heartbeat").Count());
            Assert.AreEqual(5, result.FrameSets);
        }

        /// <summary>
        /// Rows are dated to the recording, not to the afternoon the conversion ran.
        /// Joining a frame log back to its tlog is the only thing acceptance can do
        /// with it, and wall-clock timestamps would make that impossible.
        /// </summary>
        [TestMethod]
        public void Convert_DatesEveryRowToRecordedTime()
        {
            WriteFixture(seconds: 3);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            foreach (var frame in log.Frames)
            {
                var stamp = frame["t"].Value<DateTime>().ToUniversalTime();
                Assert.AreEqual(Recorded.Date, stamp.Date, "row dated to the wall clock");
            }

            // And the heartbeat's own time of day, which is what a receiver reads.
            var first = log.FramesOf("heartbeat").First();
            Assert.AreEqual(Gdl90FrameSet.SecondsSinceUtcMidnight(Recorded),
                (int)first["seconds_since_utc_midnight"]);

            // In the file, not just after a parser has been generous with it.
            StringAssert.Contains(File.ReadAllLines(result.OutputPath)[0],
                "2026-01-15T02:30:00.0000000Z");
        }

        /// <summary>
        /// The one thing that separates this file from one the live bridge wrote.
        /// Without it a converted log and a transmission record are the same document.
        /// </summary>
        [TestMethod]
        public void Convert_SaysNothingWasTransmitted()
        {
            WriteFixture(seconds: 2);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            var sources = log.All("source").ToList();
            Assert.IsTrue(sources.Count > 0);
            foreach (var source in sources)
            {
                Assert.AreEqual("tlog", (string)source["kind"]);
                Assert.IsFalse((bool)source["transmitted"]);
            }
        }

        /// <summary>
        /// Whether the recording was of a simulator, answered once the whole file has
        /// been read rather than after the first packet.
        /// </summary>
        [TestMethod]
        public void Convert_ARecordingWithNoSimulatorSignals_SaysItWasLive()
        {
            WriteFixture(seconds: 2);

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.IsFalse(result.RecordedFromSitl);
            Assert.AreEqual("live",
                (string)log.All("source").Last(s => s["recorded"] != null)["recorded"]);
        }

        [TestMethod]
        public void Convert_ARecordingOfASimulator_SaysSo()
        {
            WriteFixture(seconds: 2);
            using (var tlog = new TlogWriter(_tlog))
            {
                tlog.Write(Recorded.AddSeconds(2), MAVLink.MAVLINK_MSG_ID.SIMSTATE,
                    new MAVLink.mavlink_simstate_t());
            }

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.IsTrue(result.RecordedFromSitl);
            Assert.AreEqual("sitl",
                (string)log.All("source").Last(s => s["recorded"] != null)["recorded"]);
        }

        /// <summary>
        /// The companion path is the one the live bridge writes to, and the writer
        /// appends. Converting a tlog that was flown with the bridge running would
        /// otherwise splice made-up frames onto the end of a transmission record.
        /// </summary>
        [TestMethod]
        public void Convert_WithAFrameLogAlreadyThere_RefusesRatherThanAppending()
        {
            WriteFixture(seconds: 2);
            string companion = Gdl90FrameLog.CompanionPath(_tlog);
            File.WriteAllText(companion, "{\"record\":\"meta\"}\n");

            var result = Gdl90TlogConverter.Convert(_tlog, Configured());

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error), "and it says why");
            Assert.AreEqual("{\"record\":\"meta\"}\n", File.ReadAllText(companion));
        }

        [TestMethod]
        public void Convert_WithOverwrite_ReplacesTheExistingFrameLog()
        {
            WriteFixture(seconds: 2);
            File.WriteAllText(Gdl90FrameLog.CompanionPath(_tlog), "stale\n");

            Gdl90ConversionResult result;
            var log = Convert(out result, overwrite: true);

            Assert.IsTrue(log.FramesOf("ownship").Any());
        }

        /// <summary>
        /// The identity is not guessable from a recording - an aircraft's ADS-B
        /// receiver does not report the aircraft - and inventing one would put a
        /// made-up registration in the output.
        /// </summary>
        [TestMethod]
        public void Convert_WithoutAnIdentity_RefusesAndWritesNothing()
        {
            WriteFixture(seconds: 2);

            var result = Gdl90TlogConverter.Convert(_tlog, new Gdl90Configuration());

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(File.Exists(Gdl90FrameLog.CompanionPath(_tlog)));
        }

        [TestMethod]
        public void Convert_WithNoSuchTlog_Refuses()
        {
            var result = Gdl90TlogConverter.Convert(
                Path.Combine(_folder, "absent.tlog"), Configured());

            Assert.IsFalse(result.Succeeded);
        }

        /// <summary>
        /// A short dropout is filled, because that is where the ownship report's
        /// staleness handling earns its keep and acceptance should see it.
        /// </summary>
        [TestMethod]
        public void Convert_AcrossAShortGap_FillsIt()
        {
            WriteFixture(seconds: 4, gapAtSecond: 2, gapAfter: TimeSpan.FromSeconds(10));

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.AreEqual(0, result.Gaps);
            Assert.AreEqual(14, log.FramesOf("heartbeat").Count());

            // The filled seconds report the position as stale, which is the point: the
            // last position is held for PositionStaleAfter and then withheld until the
            // next packet arrives. The very first set is built on the recording's first
            // packet, before that second's GLOBAL_POSITION_INT has been read.
            var ownship = log.FramesOf("ownship").Select(f => (bool)f["position_valid"]).ToList();
            CollectionAssert.AreEqual(
                new[]
                {
                    false, true, true, true,
                    false, false, false, false, false, false, false, false, false,
                    true,
                },
                ownship);
        }

        /// <summary>
        /// A jump longer than a tlog can honestly contain is a corrupt timestamp, not a
        /// dropout: filling it would emit frame sets for hours that were never recorded.
        /// </summary>
        [TestMethod]
        public void Convert_AcrossAnImpossibleJump_SkipsItAndSaysSo()
        {
            WriteFixture(seconds: 4, gapAtSecond: 2, gapAfter: TimeSpan.FromHours(3));

            Gdl90ConversionResult result;
            var log = Convert(out result);

            Assert.AreEqual(1, result.Gaps);
            Assert.AreEqual(4, log.FramesOf("heartbeat").Count());

            var gap = log.Single("gap");
            Assert.IsTrue((double)gap["seconds"] > 3600);
        }
    }
}
