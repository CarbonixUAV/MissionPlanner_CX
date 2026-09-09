using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.GDL90
{
    [TestClass]
    public class Gdl90FrameLogTests
    {
        const string MpVersion = "1.3.83";
        const string PluginVersion = "2.4";

        /// <summary>
        /// The clock every log in here is built on, so that what a log writes is a
        /// function of what it was told and not of when the test ran.
        /// </summary>
        static readonly DateTime Stamp =
            new DateTime(2026, 9, 2, 13, 4, 11, DateTimeKind.Utc);

        string _folder;
        string _tlog;

        static Gdl90FrameLog NewLog()
        {
            return new Gdl90FrameLog(() => Stamp);
        }

        [TestInitialize]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CbxGdl90_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
            _tlog = Path.Combine(_folder, "2026-09-02 13-04-11.tlog");
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

        static Gdl90OwnshipReport SampleReport()
        {
            return new Gdl90OwnshipReport
            {
                Address = 0x7C1A2B,
                LatitudeE7 = -338688000,
                LongitudeE7 = 1512093000,
                PressureAltitudeFeet = 1013,
                TrackDegrees = 271.4,
                HorizontalVelocityKnots = 87,
                VerticalVelocityFpm = 640,
                EmitterCategory = 14,
                Callsign = "VHTEST",
                Nacp = 10,
            };
        }

        /// <summary>The log sits beside the tlog so the pair can be read together.</summary>
        [TestMethod]
        public void CompanionPath_ReplacesTheTlogExtensionInPlace()
        {
            Assert.AreEqual(@"C:\logs\2026-09-02 13-04-11.gdl90.jsonl",
                Gdl90FrameLog.CompanionPath(@"C:\logs\2026-09-02 13-04-11.tlog"));
        }

        /// <summary>
        /// Mission Planner lets an operator add a name to the tlog, and that name can
        /// contain a dot; cutting at the last one would eat part of it.
        /// </summary>
        [TestMethod]
        public void CompanionPath_KeepsADotInsideTheLogName()
        {
            Assert.AreEqual(@"C:\logs\2026-09-02 13-04-11 v1.2 ferry.gdl90.jsonl",
                Gdl90FrameLog.CompanionPath(@"C:\logs\2026-09-02 13-04-11 v1.2 ferry.tlog"));
        }

        /// <summary>
        /// The two versions are positional strings, and nothing reads them back: a swap
        /// would mislabel every log from then on and no other test would notice.
        /// </summary>
        [TestMethod]
        public void Open_NamesTheBuildInTheHeader()
        {
            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
            }

            var meta = Gdl90LogReader.Read(path).Meta;

            Assert.AreEqual(MpVersion, (string)meta["mission_planner"]);
            Assert.AreEqual(PluginVersion, (string)meta["plugin"]);
        }

        [TestMethod]
        public void WriteFrame_DeclaresItsColumnsOnceAndThenWritesRows()
        {
            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;

                var report = SampleReport();
                byte[] message = Gdl90Messages.TrafficReport(report);
                for (int i = 1; i <= 3; i++)
                {
                    log.WriteFrame(i, "traffic", message[0], Gdl90Frame.Frame(message),
                        Gdl90LogShape.Describe(report));
                }
            }

            var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();

            Assert.AreEqual(1, lines.Count(l => l.Contains("\"record\":\"cols\"")),
                "the column names are written once, not once per frame");
            Assert.AreEqual(3, lines.Count(l => l[0] == '['), "frames are rows");

            var cols = JObject.Parse(lines.Single(l => l.Contains("\"record\":\"cols\"")));
            Assert.AreEqual("traffic", (string)cols["msg"]);
            CollectionAssert.AreEqual(new[] { "msg", "t", "seq", "msg_id", "hex" },
                cols["cols"].Take(5).Select(c => (string)c).ToArray(),
                "every row starts with the same five, whatever the message");
        }

        [TestMethod]
        public void WriteFrame_RecordsTheFramedBytesBesideTheEncoderInputs()
        {
            var report = SampleReport();
            byte[] message = Gdl90Messages.TrafficReport(report);
            byte[] frame = Gdl90Frame.Frame(message);

            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
                log.WriteFrame(41, "traffic", message[0], frame, Gdl90LogShape.Describe(report));
            }

            var row = Gdl90LogReader.Read(path).Frames.Single();

            Assert.AreEqual(41, (int)row["seq"]);
            Assert.AreEqual("traffic", (string)row["msg"]);
            Assert.AreEqual(Gdl90Messages.TrafficReportId, (int)row["msg_id"]);
            Assert.AreEqual(Gdl90TestUtil.ToHex(frame), (string)row["hex"]);
            Assert.AreEqual("VHTEST", (string)row["callsign"]);
            Assert.AreEqual(0x7C1A2B, (int)row["address"]);
        }

        /// <summary>
        /// The whole point of the log: what the caller asked for sits beside what went
        /// on the wire, so the two can disagree and acceptance can say which is lying.
        /// Quantizing on the way in would make them agree by construction.
        /// </summary>
        [TestMethod]
        public void WriteFrame_LogsTheValueTheCallerAskedFor_NotTheQuantizedOne()
        {
            var report = SampleReport();
            byte[] message = Gdl90Messages.TrafficReport(report);

            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
                log.WriteFrame(1, "traffic", message[0], Gdl90Frame.Frame(message),
                    Gdl90LogShape.Describe(report));
            }

            var row = Gdl90LogReader.Read(path).Frames.Single();

            // ICD 3.5.1.4: 12-bit offset integer, ("ddd" * 25) - 1000 feet.
            int code = (message[11] << 4) | (message[12] >> 4);
            Assert.AreEqual(1000, code * 25 - 1000, "the wire reads back a quantized 1000 ft");
            Assert.AreEqual(1013, (int)row["pressure_altitude_ft"], "the log keeps 1013");
        }

        [TestMethod]
        public void WriteRecord_LogsADroppedTargetWithItsReason()
        {
            var result = new Gdl90TrafficResult
            {
                Vehicle = Gdl90TrafficMappingTests.Target(icao: 0x4B1A2C, tslc: 3),
                Age = TimeSpan.FromSeconds(3.5),
                DropReason = Gdl90TrafficDropReason.Stale,
                DropIsNew = true,
            };

            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
                log.WriteRecord("drop", Gdl90LogShape.Drop(result));
            }

            var record = Gdl90LogReader.Read(path).Single("drop");

            Assert.AreEqual("stale", (string)record["reason"]);
            Assert.AreEqual(0x4B1A2C, (int)record["address"]);
        }

        /// <summary>
        /// Every line is timestamped, and from the clock the log was handed rather than
        /// from the wall clock - which is what lets a converted tlog date its rows to
        /// the flight instead of to the conversion.
        /// </summary>
        [TestMethod]
        public void EveryRecordAndRow_IsStampedFromTheClockTheLogWasGiven()
        {
            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
                log.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E, 0x00, 0x7E },
                    Gdl90LogShape.Describe(new Gdl90Heartbeat
                    {
                        GpsPositionValid = true,
                        UtcTimingValid = true,
                        SecondsSinceUtcMidnight = 3661,
                    }));
                log.WriteRecord("drop", new JObject());
            }

            var read = Gdl90LogReader.Read(path);
            var stamped = read.Records.Concat(read.Frames).ToList();

            Assert.IsTrue(stamped.Count >= 4, "meta, cols, drop, close and the frame");
            foreach (var line in stamped)
            {
                Assert.AreEqual(Stamp, line["t"].Value<DateTime>().ToUniversalTime(),
                    line.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        [TestMethod]
        public void Close_RecordsWhatTheSessionWroteAndStopsAcceptingRecords()
        {
            string path;
            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                path = log.Path;
                log.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());
                log.WriteFrame(2, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());
                log.Close();

                Assert.IsFalse(log.IsOpen);
                log.WriteFrame(3, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());
            }

            var read = Gdl90LogReader.Read(path);
            var close = read.Single("close");

            Assert.AreEqual(2, (int)close["frames"]);
            Assert.AreEqual(0, (int)close["discarded"]);
            Assert.AreEqual(2, read.Frames.Count, "nothing is accepted after the close");
        }

        /// <summary>
        /// Mission Planner sorts finished logs by moving every file that starts with
        /// the tlog's stem. Our open handle must not block that, or the tlog and rlog
        /// are abandoned along with it.
        /// </summary>
        [TestMethod]
        public void Open_LeavesTheFileMovableWhileItIsStillBeingWritten()
        {
            string sorted = Path.Combine(_folder, "FIXED_WING");
            Directory.CreateDirectory(sorted);

            using (var log = NewLog())
            {
                log.Open(_tlog, MpVersion, PluginVersion);
                log.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());

                string moved = Path.Combine(sorted, Path.GetFileName(log.Path));
                File.Move(log.Path, moved);

                Assert.IsTrue(File.Exists(moved));
            }
        }

        /// <summary>A failing log must be silent, not fatal.</summary>
        [TestMethod]
        public void Open_IntoAMissingDirectory_ReportsFailureWithoutThrowing()
        {
            using (var log = NewLog())
            {
                Assert.IsFalse(log.Open(Path.Combine(_folder, "nope", "x.tlog"),
                    MpVersion, PluginVersion));
                Assert.IsFalse(log.IsOpen);

                log.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());
                log.WriteRecord("drop", new JObject());
                log.Close();
            }
        }

        [TestMethod]
        public void Open_WithNoTlog_DoesNothing()
        {
            using (var log = NewLog())
            {
                Assert.IsFalse(log.Open(null, MpVersion, PluginVersion));
                Assert.IsFalse(log.Open("", MpVersion, PluginVersion));
                Assert.IsFalse(log.IsOpen);
            }
        }

        /// <summary>
        /// A file that stops taking records must stop claiming to be open, or the rest of
        /// the flight goes unlogged behind a log that says it is being written.
        /// </summary>
        [TestMethod]
        public void Open_WhenTheFileFailsUnderIt_ClosesItself()
        {
            using (var log = NewLog())
            {
                log.OpenWriter = _ => new FailingWriter();
                Assert.IsTrue(log.Open(_tlog, MpVersion, PluginVersion),
                    "the failure is in the writing, not the opening");

                Assert.IsTrue(SpinWait.SpinUntil(() => !log.IsOpen, TimeSpan.FromSeconds(5)),
                    "the log closes itself once the writer fails");
                Assert.IsNull(log.Path);

                log.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E }, new JObject());
                log.Close();
            }
        }
    }

    /// <summary>A file that fails on the first write, as a full disk does.</summary>
    sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }

        public override void Write(char value)
        {
            throw new IOException("There is not enough space on the disk.");
        }

        public override void Write(string value)
        {
            throw new IOException("There is not enough space on the disk.");
        }
    }

    /// <summary>
    /// One frame log beside every open tlog, following the links as they come and go.
    /// </summary>
    [TestClass]
    public class Gdl90FrameLogSetTests
    {
        static readonly DateTime Stamp =
            new DateTime(2026, 9, 2, 13, 4, 11, DateTimeKind.Utc);

        string _folder;
        string _first;
        string _second;

        [TestInitialize]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CbxGdl90Set_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
            _first = Path.Combine(_folder, "2026-09-02 13-04-11.tlog");
            _second = Path.Combine(_folder, "2026-09-02 13-05-22.tlog");
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
        /// Mission Planner's tlog stream, so the reflection that finds the file name
        /// behind it is exercised rather than assumed.
        /// </summary>
        static BufferedStream OpenTlog(string path)
        {
            return new BufferedStream(
                File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None));
        }

        static Gdl90FrameLogSet NewSet()
        {
            return new Gdl90FrameLogSet(() => Stamp)
            {
                MissionPlannerVersion = "1.3.83",
                PluginVersion = "2.4",
            };
        }

        /// <summary>
        /// Every link writes its own tlog, so the stream is recorded beside all of
        /// them. Nothing has to decide which tlog is the one.
        /// </summary>
        [TestMethod]
        public void Update_OpensOneLogBesideEveryOpenTlog()
        {
            using (var a = OpenTlog(_first))
            using (var b = OpenTlog(_second))
            using (var set = NewSet())
            {
                set.Update(true, new Stream[] { a, b });

                CollectionAssert.AreEquivalent(
                    new[] { Gdl90FrameLog.CompanionPath(_first), Gdl90FrameLog.CompanionPath(_second) },
                    set.Paths.ToList());
                Assert.AreEqual(Path.GetDirectoryName(_first),
                    Path.GetDirectoryName(set.Paths.First()));
            }
        }

        /// <summary>Each log ends with its own tlog, so the last link out closes the last log.</summary>
        [TestMethod]
        public void Update_ClosesEachLogWithItsOwnTlog()
        {
            var a = OpenTlog(_first);
            var b = OpenTlog(_second);

            using (var set = NewSet())
            {
                set.Update(true, new Stream[] { a, b });
                Assert.AreEqual(2, set.Count);

                a.Close();
                set.Update(true, new Stream[] { a, b });
                Assert.AreEqual(Gdl90FrameLog.CompanionPath(_second), set.Paths.Single(),
                    "the surviving link keeps recording");

                b.Close();
                set.Update(true, new Stream[] { a, b });
                Assert.AreEqual(0, set.Count);
            }
        }

        [TestMethod]
        public void Update_WhenDisabled_OpensNothingAndClosesEverything()
        {
            using (var a = OpenTlog(_first))
            using (var set = NewSet())
            {
                set.Update(false, new Stream[] { a });
                Assert.AreEqual(0, set.Count);
                Assert.IsFalse(File.Exists(Gdl90FrameLog.CompanionPath(_first)));

                set.Update(true, new Stream[] { a });
                Assert.AreEqual(1, set.Count);

                set.Update(false, new Stream[] { a });
                Assert.AreEqual(0, set.Count);
            }
        }

        [TestMethod]
        public void Update_IgnoresALinkWithNoTlog()
        {
            using (var set = NewSet())
            {
                set.Update(true, new Stream[] { null, new MemoryStream() });

                Assert.AreEqual(0, set.Count);
            }
        }

        /// <summary>
        /// One frame carries one number in every copy of the log, so the copies can be
        /// compared and a log that started late visibly starts partway.
        /// </summary>
        [TestMethod]
        public void WriteFrame_WritesTheSameRowToEveryOpenLog()
        {
            using (var a = OpenTlog(_first))
            using (var b = OpenTlog(_second))
            using (var set = NewSet())
            {
                set.Update(true, new Stream[] { a });
                set.WriteFrame(1, "heartbeat", 0x00, new byte[] { 0x7E }, () => new JObject());

                set.Update(true, new Stream[] { a, b });
                set.WriteFrame(2, "heartbeat", 0x00, new byte[] { 0x7E }, () => new JObject());
            }

            var early = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(_first));
            var late = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(_second));

            CollectionAssert.AreEqual(new[] { 1, 2 }, early.Frames.Select(f => (int)f["seq"]).ToList());
            CollectionAssert.AreEqual(new[] { 2 }, late.Frames.Select(f => (int)f["seq"]).ToList(),
                "a log opened mid-session says so rather than restarting the count");
        }

        /// <summary>
        /// A link that comes up mid-flight gets a log that still says where its frames
        /// are going and what produced them. These records change only when the thing
        /// they describe does, so a late log would otherwise wait for an event that may
        /// never come.
        /// </summary>
        [TestMethod]
        public void WriteRecord_ReplaysStickyRecordsIntoALogOpenedLater()
        {
            using (var a = OpenTlog(_first))
            using (var b = OpenTlog(_second))
            using (var set = NewSet())
            {
                set.WriteRecord("destination", () => new JObject { ["destination"] = "10.0.0.1:4000" },
                    sticky: true);
                set.WriteRecord("source", () => new JObject { ["kind"] = "live" }, sticky: true);

                set.Update(true, new Stream[] { a });
                set.WriteRecord("destination", () => new JObject { ["destination"] = "10.0.0.2:4000" },
                    sticky: true);
                set.WriteRecord("drop", () => new JObject { ["reason"] = "stale" });

                set.Update(true, new Stream[] { a, b });
            }

            var late = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(_second));
            var replayed = late.Records.Where(r => (string)r["record"] != "meta").ToList();

            CollectionAssert.AreEqual(new[] { "destination", "source", "close" },
                replayed.Select(r => (string)r["record"]).ToList(),
                "the latest of each sticky kind, in the order first established; no events");
            Assert.AreEqual("10.0.0.2:4000", (string)replayed[0]["destination"]);
        }

        /// <summary>
        /// A log whose file failed under it is dropped, and not reopened while its tlog
        /// lives: the next write would fail the same way, once a second.
        /// </summary>
        [TestMethod]
        public void Update_DropsALogThatClosedItselfAndDoesNotReopenIt()
        {
            using (var tlog = OpenTlog(_first))
            using (var set = NewSet())
            {
                int opened = 0;
                set.OpenWriter = _ =>
                {
                    opened++;
                    return new FailingWriter();
                };

                set.Update(true, new[] { tlog });
                Assert.AreEqual(1, set.Count);
                Assert.IsTrue(SpinWait.SpinUntil(() => !set.Paths.Any(), TimeSpan.FromSeconds(5)),
                    "the log closes itself once the writer fails");

                set.Update(true, new[] { tlog });
                Assert.AreEqual(0, set.Count, "the failed log is dropped");
                Assert.AreEqual(1, opened, "and not reopened while its tlog lives");

                set.Update(true, new Stream[0]);
                set.Update(true, new[] { tlog });
                Assert.AreEqual(2, opened, "a tlog that comes back is tried again");
            }
        }

        [TestMethod]
        public void TlogNameOf_FindsTheFileBehindABufferedStream()
        {
            using (var a = OpenTlog(_first))
            using (var set = NewSet())
            {
                Assert.AreEqual(_first, set.TlogNameOf(a));
                Assert.IsNull(set.TlogNameOf(null));
                Assert.IsNull(set.TlogNameOf(new MemoryStream()));
            }
        }
    }
}
