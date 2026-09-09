using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The transmit loop over loopback: what reaches the socket, when the run ends,
    /// and what reaches the frame logs beside the tlogs.
    /// </summary>
    /// <remarks>
    /// Every service here reads a vehicle state that holds no telemetry at all, so
    /// what goes on the socket is a stream of explicit "no data" encodings and never
    /// anything resembling a flight.
    /// </remarks>
    [TestClass]
    public class Gdl90ServiceTests
    {
        static readonly Gdl90OwnshipIdentity Identity =
            new Gdl90OwnshipIdentity { IcaoAddress = 0x7C1A2B, Callsign = "VHTEST" };

        string _folder;
        string _tlog;

        [TestInitialize]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CbxGdl90Svc_" + Path.GetRandomFileName());
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

        static Gdl90Configuration Configured()
        {
            return new Gdl90Configuration
            {
                Identity = Identity,
                MissionPlannerVersion = "1.3.83",
                PluginVersion = "2.4",
            };
        }

        static IPEndPoint Loopback(UdpClient receiver)
        {
            return new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)receiver.Client.LocalEndPoint).Port);
        }

        /// <summary>A service pointed at the receiver, on an aircraft, with the link up.</summary>
        static Gdl90Service Armed(UdpClient receiver)
        {
            var service = new Gdl90Service
            {
                ClassifySource = () => Gdl90SourceKind.Live,
                LinksOpen = () => true,
            };
            service.Enable(Loopback(receiver), Configured());
            return service;
        }

        static UdpClient Receiver()
        {
            var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.Client.ReceiveTimeout = 5000;
            return receiver;
        }

        /// <summary>Mission Planner's tlog stream, so the reflection behind the log path is exercised.</summary>
        static BufferedStream OpenTlog(string path)
        {
            return new BufferedStream(
                File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None));
        }

        /// <summary>Points the service's frame logs at streams instead of live links.</summary>
        static void LogBeside(Gdl90Service service, params Stream[] tlogs)
        {
            service.TlogStreams = () => tlogs;
            service.UpdateFrameLogs(true);
        }

        /// <summary>Clear messages received, each from one datagram, until enough or time is up.</summary>
        static List<byte[]> Receive(UdpClient receiver, int expected, double seconds = 5.0)
        {
            var messages = new List<byte[]>();
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            var elapsed = Stopwatch.StartNew();

            while (messages.Count < expected && elapsed.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                byte[] datagram;
                try
                {
                    datagram = receiver.Receive(ref endpoint);
                }
                catch (SocketException)
                {
                    break;
                }

                byte[] message;
                Assert.IsTrue(Gdl90Frame.TryUnframe(datagram, out message),
                    "each datagram is one well-framed message");
                messages.Add(message);
            }

            return messages;
        }

        static List<byte> Ids(IEnumerable<byte[]> messages)
        {
            return messages.Select(m => m[0]).ToList();
        }

        // ---- switching on ----

        [TestMethod]
        public void Enable_WithoutADestination_Throws()
        {
            using (var service = new Gdl90Service())
            {
                Assert.ThrowsException<ArgumentNullException>(
                    () => service.Enable(null, Configured()));
            }
        }

        /// <summary>
        /// Address type 0 asserts a registered identity, so a run with no address is
        /// refused outright rather than transmitting as nobody.
        /// </summary>
        [TestMethod]
        public void Enable_WithoutAnIdentity_Throws()
        {
            using (var service = new Gdl90Service())
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, 4000);

                Assert.ThrowsException<ArgumentException>(
                    () => service.Enable(endpoint, new Gdl90Configuration()));
                Assert.ThrowsException<ArgumentException>(
                    () => service.Enable(endpoint, new Gdl90Configuration
                    {
                        Identity = new Gdl90OwnshipIdentity { IcaoAddress = 0 },
                    }));
                Assert.IsFalse(service.Enabled);
            }
        }

        [TestMethod]
        public void Enable_ReportsWhereItIsPointed()
        {
            using (var service = new Gdl90Service())
            {
                var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 4001);
                service.Enable(endpoint, Configured());

                IGdl90Status status = service;
                Assert.IsTrue(status.Enabled);
                Assert.AreEqual(endpoint, status.Destination);
                Assert.AreEqual(0, status.FramesSent);
                Assert.IsNull(status.LastError);
            }
        }

        [TestMethod]
        public void Disable_TakesTheStreamOffTheWireAndDropsTheSimulatorAttestation()
        {
            using (var service = new Gdl90Service())
            {
                service.Enable(new IPEndPoint(IPAddress.Loopback, 4000), Configured());
                service.SitlStreamingEnabled = true;

                service.Disable();

                Assert.IsFalse(service.Enabled);
                Assert.IsFalse(service.SitlStreamingEnabled,
                    "the attestation covered one run, and the next has to claim it again");
            }
        }

        [TestMethod]
        public void SitlStreamingEnabled_IsOffOnAFreshService()
        {
            using (var service = new Gdl90Service())
            {
                Assert.IsFalse(service.SitlStreamingEnabled);
            }
        }

        // ---- the loop ----

        [TestMethod]
        public void Start_TransmitsTheOwnshipStreamAndStopsCleanly()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.Start();
                var ids = Ids(Receive(receiver, 3));
                service.Stop();

                CollectionAssert.Contains(ids, Gdl90Messages.HeartbeatId);
                CollectionAssert.Contains(ids, Gdl90Messages.OwnshipReportId);
                CollectionAssert.Contains(ids, Gdl90Messages.ForeFlightId);
                CollectionAssert.DoesNotContain(ids, Gdl90Messages.OwnshipGeometricAltitudeId,
                    "ICD 3.8: not sent without a GPS altitude");

                Assert.AreNotEqual(DateTime.MinValue, service.LastTransmitUtc);
                Assert.IsNull(service.LastError);
            }
        }

        /// <summary>
        /// The counters describe the current run, not the service's whole life, so the
        /// tab cannot report the previous run's frames against this one.
        /// </summary>
        [TestMethod]
        public void Disable_ForgetsWhatTheRunDid()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.Start();
                Receive(receiver, 3);
                service.Stop();

                Assert.AreNotEqual(0, service.FramesSent);

                service.Disable();

                Assert.AreEqual(0, service.FramesSent);
                Assert.AreEqual(default(DateTime), service.LastTransmitUtc);
            }
        }

        /// <summary>
        /// Fully configured and switched off, rather than never switched on: the
        /// silence has to come from the flag and not from a gap in the configuration.
        /// </summary>
        [TestMethod]
        public void Start_WhenDisabled_TransmitsNothing()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.Disable();
                service.Start();

                Thread.Sleep(300);
                service.Stop();

                Assert.AreEqual(0, receiver.Available, "nothing should have been sent");
                Assert.AreEqual(0L, service.FramesSent);
            }
        }

        /// <summary>A dropout leaves no port to read; the loop must survive it.</summary>
        [TestMethod]
        public void Start_WithNoPortAttached_KeepsTicking()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.UpdatePort(null);
                service.Start();

                Thread.Sleep(200);
                var tick = service.LastTickUtc;
                service.Stop();

                Assert.AreNotEqual(DateTime.MinValue, tick);
                Assert.IsNull(service.LastError);
            }
        }

        /// <summary>
        /// MainV2.Comports is a plain list the UI thread adds to on connect, and the
        /// plugin thread copies it once a second.
        /// </summary>
        [TestMethod]
        public void UpdatePort_WhenTheLinkListChangesUnderneathIt_KeepsTheLastCopy()
        {
            using (var service = new Gdl90Service())
            {
                service.UpdatePort(null, new MAVLinkInterface[0]);
                Assert.IsFalse(service.LinkOpen);

                service.UpdatePort(null, Changing());

                Assert.IsFalse(service.LinkOpen, "the copy that failed is not the one in use");
            }
        }

        static IEnumerable<MAVLinkInterface> Changing()
        {
            yield return null;
            throw new InvalidOperationException("Collection was modified");
        }

        /// <summary>
        /// The receiver keeps reporting the sky while the stream is off, and nothing
        /// collects what it reports.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WhileOff_ForgetsTargetsThatStoppedArriving()
        {
            using (var service = new Gdl90Service())
            {
                service.Traffic.Accept(Gdl90TrafficMappingTests.Target(),
                    DateTime.UtcNow - Gdl90TrafficTracker.DropAfter - TimeSpan.FromSeconds(1));

                service.Start();
                Thread.Sleep(200);
                service.Stop();

                Assert.AreEqual(0, service.Traffic.Count);
            }
        }

        // ---- telemetry readiness ----

        [TestMethod]
        public void TransmitOnce_ReportsIncompleteTelemetryWithNoVehicle()
        {
            using (var service = new Gdl90Service())
            {
                service.Start();
                Thread.Sleep(200);
                service.Stop();

                Assert.IsFalse(service.TelemetryComplete);
            }
        }

        /// <summary>
        /// Read whether or not the stream is on, so the tab can refuse to start it
        /// until every message the ownship report needs is arriving.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_ReportsCompleteTelemetryWhileIdle()
        {
            using (var port = new MAVLinkInterface())
            using (var service = new Gdl90Service())
            {
                var parse = new MAVLink.MavlinkParse();
                var now = DateTime.UtcNow;
                Action<MAVLink.MAVLINK_MSG_ID, object> receive = (id, data) =>
                    port.MAV.addPacket(new MAVLink.MAVLinkMessage(
                        parse.GenerateMAVLinkPacket20(id, data, false, 1, 1), now));

                receive(MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT, new MAVLink.mavlink_global_position_int_t());
                receive(MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT, new MAVLink.mavlink_gps_raw_int_t());
                receive(MAVLink.MAVLINK_MSG_ID.SCALED_PRESSURE, new MAVLink.mavlink_scaled_pressure_t());
                receive(MAVLink.MAVLINK_MSG_ID.ATTITUDE, new MAVLink.mavlink_attitude_t());
                receive(MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE, new MAVLink.mavlink_extended_sys_state_t());

                service.UpdatePort(port);
                service.Start();
                Thread.Sleep(200);
                service.Stop();

                Assert.IsTrue(service.TelemetryComplete);
                Assert.IsFalse(service.Enabled, "nothing was started");
            }
        }

        // ---- which sources transmit ----

        /// <summary>
        /// Runs the service for long enough to take a tick, and reports whether it was
        /// still switched on at the end. The disarm rules live in the transmit loop, so
        /// they need a tick to have happened.
        /// </summary>
        static bool StillEnabledAfterATick(Action<Gdl90Service> arrange)
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                arrange(service);

                service.Start();
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline && service.Enabled)
                    Thread.Sleep(50);

                bool enabled = service.Enabled;
                service.Stop();
                return enabled;
            }
        }

        /// <summary>
        /// Every link closed is a disconnect, not a telemetry dropout, and it ends the
        /// run rather than leaving the stream switched on and silent.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WithEveryLinkClosed_EndsTheRun()
        {
            Assert.IsFalse(StillEnabledAfterATick(s => s.LinksOpen = () => false));
        }

        /// <summary>
        /// A simulator found after the stream was started disarms it, rather than going
        /// quietly silent for the rest of the session.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WhenASimulatorAppearsMidRun_EndsTheRun()
        {
            Assert.IsFalse(StillEnabledAfterATick(s => s.ClassifySource = () => Gdl90SourceKind.Sitl));
        }

        [TestMethod]
        public void TransmitOnce_FromAnEnabledSimulator_KeepsRunning()
        {
            Assert.IsTrue(StillEnabledAfterATick(s =>
            {
                s.ClassifySource = () => Gdl90SourceKind.Sitl;
                s.SitlStreamingEnabled = true;
            }));
        }

        /// <summary>The frames of a replay would carry the positions of an old flight, dated to today.</summary>
        [TestMethod]
        public void TransmitOnce_DuringAReplay_EndsTheRun()
        {
            Assert.IsFalse(StillEnabledAfterATick(s => s.ClassifySource = () => Gdl90SourceKind.Playback));
        }

        /// <summary>One tick of the transmit loop against a loopback socket.</summary>
        List<byte[]> TickOnce(Action<Gdl90Service> arrange, Stream tlog = null)
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                if (tlog != null) service.TlogStreams = () => new[] { tlog };
                arrange(service);

                receiver.Client.ReceiveTimeout = 1500;
                service.Start();

                // Long enough for one tick's worth of frames and then some, so a
                // stream that should be silent has had every chance to speak.
                var messages = Receive(receiver, int.MaxValue, 2.0);

                service.Stop();
                return messages;
            }
        }

        [TestMethod]
        public void TransmitOnce_FromALiveAircraft_SendsEverything()
        {
            var ids = Ids(TickOnce(s => { }));

            CollectionAssert.Contains(ids, Gdl90Messages.HeartbeatId);
            CollectionAssert.Contains(ids, Gdl90Messages.OwnshipReportId);
        }

        /// <summary>
        /// Unknown is the settling state before Live. The tab does not start a stream
        /// until it clears, so nothing assumes one may run through it.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WhileTheSourceIsStillSettling_SendsNothing()
        {
            Assert.AreEqual(0, TickOnce(s => s.ClassifySource = () => Gdl90SourceKind.Unknown).Count);
        }

        [TestMethod]
        public void TransmitOnce_WhileReplayingATlog_SendsNothingAndWritesNoFrameLog()
        {
            using (var tlog = OpenTlog(_tlog))
            {
                var messages = TickOnce(s => s.ClassifySource = () => Gdl90SourceKind.Playback, tlog);
                Assert.AreEqual(0, messages.Count);
            }

            Assert.IsFalse(File.Exists(Gdl90FrameLog.CompanionPath(_tlog)));
        }

        [TestMethod]
        public void TransmitOnce_FromASimulatorWithNothingEnabled_SendsNothingAndWritesNoFrameLog()
        {
            using (var tlog = OpenTlog(_tlog))
            {
                var messages = TickOnce(s => s.ClassifySource = () => Gdl90SourceKind.Sitl, tlog);
                Assert.AreEqual(0, messages.Count);
            }

            Assert.IsFalse(File.Exists(Gdl90FrameLog.CompanionPath(_tlog)));
        }

        [TestMethod]
        public void TransmitOnce_FromASimulatorWithStreamingEnabled_SendsEverything()
        {
            var ids = Ids(TickOnce(s =>
            {
                s.ClassifySource = () => Gdl90SourceKind.Sitl;
                s.SitlStreamingEnabled = true;
                s.Traffic.Accept(Gdl90TrafficMappingTests.Target(icao: 0x111111), DateTime.UtcNow);
            }));

            CollectionAssert.Contains(ids, Gdl90Messages.HeartbeatId);
            CollectionAssert.Contains(ids, Gdl90Messages.OwnshipReportId);
            CollectionAssert.Contains(ids, Gdl90Messages.TrafficReportId);
        }

        /// <summary>
        /// A deliberate disconnect is not a telemetry dropout. With every link closed
        /// there is no aircraft to be the ownship for, so the receiver is better served
        /// by our absence than by a stream of "position unknown".
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WithEveryLinkClosed_TransmitsNothing()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.LinksOpen = () => false;

                service.Start();
                Thread.Sleep(300);
                service.Stop();

                Assert.AreEqual(0, receiver.Available, "nothing should have been sent");
                Assert.AreEqual(0L, service.FramesSent);
                Assert.IsFalse(service.Enabled, "and the run ended rather than idling");
                Assert.AreNotEqual(DateTime.MinValue, service.LastTickUtc,
                    "the loop itself keeps running, ready for the next Start");
            }
        }

        /// <summary>
        /// A link that comes back does not restart the stream. Reconnecting is a
        /// deliberate act and so is transmitting, and the operator has to look at the
        /// callsign and the address again before either resumes.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_WhenALinkComesBack_DoesNotResumeOnItsOwn()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                bool connected = false;
                service.LinksOpen = () => connected;

                service.Start();
                Thread.Sleep(300);
                Assert.IsFalse(service.Enabled, "the disconnect ended the run");

                connected = true;
                Thread.Sleep(1500);
                service.Stop();

                Assert.AreEqual(0L, service.FramesSent, "still silent until Start again");
                Assert.AreEqual(0, receiver.Available);
            }
        }

        // ---- traffic ----

        [TestMethod]
        public void TransmitOnce_ForwardsEveryTrackedTargetEachSecond()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                var now = DateTime.UtcNow;
                service.Traffic.Accept(Gdl90TrafficMappingTests.Target(icao: 0x111111), now);
                service.Traffic.Accept(Gdl90TrafficMappingTests.Target(icao: 0x222222), now);

                service.Start();
                var messages = Receive(receiver, 6);
                service.Stop();

                var addresses = messages
                    .Where(m => m[0] == Gdl90Messages.TrafficReportId)
                    .Select(m => (m[2] << 16) | (m[3] << 8) | m[4])
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList();

                CollectionAssert.AreEqual(new[] { 0x111111, 0x222222 }, addresses,
                    "one message id multiplexes many aircraft; all of them go out");
            }
        }

        [TestMethod]
        public void TransmitOnce_DoesNotForwardOurselfAsTraffic()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.Traffic.Accept(
                    Gdl90TrafficMappingTests.Target(icao: (uint)Identity.IcaoAddress), DateTime.UtcNow);

                service.Start();
                var messages = Receive(receiver, 4);
                service.Stop();

                Assert.IsFalse(messages.Any(m => m[0] == Gdl90Messages.TrafficReportId),
                    "our own transponder comes back over the link and must not appear twice");
                Assert.IsTrue(messages.Any(m => m[0] == Gdl90Messages.OwnshipReportId));
            }
        }

        // ---- the frame logs ----

        [TestMethod]
        public void TransmitOnce_LogsEveryFrameItSendsWithTheValuesBehindIt()
        {
            string logPath;

            using (var receiver = Receiver())
            using (var tlog = OpenTlog(_tlog))
            using (var service = Armed(receiver))
            {
                LogBeside(service, tlog);
                logPath = service.FrameLogPaths.Single();
                service.Traffic.Accept(
                    Gdl90TrafficMappingTests.Target(icao: 0x333333), DateTime.UtcNow);

                service.Start();
                Receive(receiver, 4);
                service.Stop();
                service.Dispose();
            }

            var read = Gdl90LogReader.Read(logPath);
            var names = read.Frames.Select(f => (string)f["msg"]).Distinct().ToList();

            CollectionAssert.Contains(names, "heartbeat");
            CollectionAssert.Contains(names, "ownship");
            CollectionAssert.Contains(names, "foreflight_id");
            CollectionAssert.Contains(names, "traffic");

            var traffic = read.FramesOf("traffic").First();
            byte[] frame = Gdl90TestUtil.FromHex((string)traffic["hex"]);

            byte[] message;
            Assert.IsTrue(Gdl90Frame.TryUnframe(frame, out message),
                "the hex is the bytes that went on the wire, framing and all");
            Assert.AreEqual(Gdl90Messages.TrafficReportId, message[0]);
            Assert.AreEqual(0x333333, (int)traffic["address"]);
        }

        [TestMethod]
        public void TransmitOnce_RecordsDroppedTargetsInTheLog()
        {
            string logPath;

            using (var receiver = Receiver())
            using (var tlog = OpenTlog(_tlog))
            using (var service = Armed(receiver))
            {
                LogBeside(service, tlog);
                logPath = service.FrameLogPaths.Single();

                var now = DateTime.UtcNow;
                service.Traffic.Accept(Gdl90TrafficMappingTests.Target(icao: 0), now);
                service.Traffic.Accept(Gdl90TrafficMappingTests.Target(icao: 0x333333), now);

                service.Start();
                Receive(receiver, 5);
                service.Stop();
                service.Dispose();
            }

            var drops = Gdl90LogReader.Read(logPath).All("drop").ToList();

            CollectionAssert.AreEquivalent(
                new[] { Gdl90TrafficDropReason.InvalidAddress },
                drops.Select(d => (string)d["reason"]).ToList());
        }

        /// <summary>Nothing about logging may reach the stream.</summary>
        [TestMethod]
        public void TransmitOnce_KeepsTransmittingWithNoTlogToLogBeside()
        {
            using (var receiver = Receiver())
            using (var service = Armed(receiver))
            {
                service.Traffic.Accept(
                    Gdl90TrafficMappingTests.Target(icao: 0x333333), DateTime.UtcNow);

                service.Start();
                var messages = Receive(receiver, 4);
                service.Stop();

                Assert.AreEqual(0, service.FrameLogPaths.Count());
                Assert.IsTrue(messages.Any(m => m[0] == Gdl90Messages.TrafficReportId));
                Assert.IsNull(service.LastError);
            }
        }

        /// <summary>
        /// One frame carries one number in every copy of the log, so the copies can be
        /// compared and a log that started late visibly starts partway.
        /// </summary>
        [TestMethod]
        public void TransmitOnce_NumbersFramesAcrossTheServiceRatherThanPerFile()
        {
            string first = Path.Combine(_folder, "link-a.tlog");
            string second = Path.Combine(_folder, "link-b.tlog");

            using (var receiver = Receiver())
            using (var a = OpenTlog(first))
            using (var b = OpenTlog(second))
            using (var service = Armed(receiver))
            {
                LogBeside(service, a);
                service.Start();
                Receive(receiver, 4);

                // The second link starts recording mid-session. Only the transmit loop
                // opens and closes logs, so this hands it the streams and waits, rather
                // than opening the log from this thread and racing the loop's writes.
                service.TlogStreams = () => new Stream[] { a, b };
                Receive(receiver, 8);

                service.Stop();
                service.Dispose();
            }

            var late = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(second));
            var early = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(first));

            Assert.AreEqual(1, (int)early.Frames.First()["seq"]);
            Assert.IsTrue((int)late.Frames.First()["seq"] > 1,
                "a log opened mid-session says so rather than restarting the count");

            foreach (int seq in late.Frames.Select(f => (int)f["seq"]))
            {
                var inEarly = early.Frames.Single(f => (int)f["seq"] == seq);
                var inLate = late.Frames.Single(f => (int)f["seq"] == seq);
                Assert.AreEqual((string)inEarly["hex"], (string)inLate["hex"]);
                Assert.AreEqual((string)inEarly["msg"], (string)inLate["msg"]);
            }
        }

        /// <summary>
        /// A link switch is noted in every log, so any single one of them reconstructs
        /// which telemetry stream was feeding the bridge at each moment.
        /// </summary>
        [TestMethod]
        public void UpdateFrameLogs_RecordsALinkSwitchInEveryLog()
        {
            string second = Path.Combine(_folder, "2026-09-02 13-05-22.tlog");

            using (var a = OpenTlog(_tlog))
            using (var b = OpenTlog(second))
            using (var service = new Gdl90Service())
            {
                var both = new Stream[] { a, b };
                service.UpdateFrameLogs(true, both, a);
                service.UpdateFrameLogs(true, both, a);
                service.UpdateFrameLogs(true, both, b);

                service.Dispose();
            }

            foreach (string tlog in new[] { _tlog, second })
            {
                var links = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(tlog))
                    .All("link").ToList();

                Assert.AreEqual(2, links.Count, tlog + ": the first selection and the switch");
                Assert.IsNull((string)links[0]["from_tlog"]);
                Assert.AreEqual(_tlog, (string)links[0]["to_tlog"]);
                Assert.AreEqual(_tlog, (string)links[1]["from_tlog"]);
                Assert.AreEqual(second, (string)links[1]["to_tlog"],
                    "a steady selection is not re-recorded; only the change is");
            }
        }

        /// <summary>
        /// Where the frames went, and what produced them. A log that does not say is a
        /// list of bytes with no claim attached to it.
        /// </summary>
        [TestMethod]
        public void UpdateFrameLogs_RecordsTheDestinationAndTheSourceOnce()
        {
            using (var tlog = OpenTlog(_tlog))
            using (var service = new Gdl90Service())
            {
                service.Enable(new IPEndPoint(IPAddress.Loopback, 4000), Configured());
                service.ClassifySource = () => Gdl90SourceKind.Live;
                LogBeside(service, tlog);

                // Single below is the assertion that a steady state is written once
                // rather than every tick.
                service.UpdateFrameLogs(true);
                service.Dispose();
            }

            var read = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(_tlog));

            Assert.AreEqual("127.0.0.1:4000", (string)read.Single("destination")["destination"]);

            var source = read.Single("source");
            Assert.AreEqual("live", (string)source["kind"]);
            Assert.IsTrue((bool)source["transmitted"]);
        }

        /// <summary>
        /// A link that comes up mid-flight gets a log that still says where its frames
        /// are going and what produced them.
        /// </summary>
        [TestMethod]
        public void UpdateFrameLogs_GivesALogOpenedLaterTheRecordsItMissed()
        {
            string second = Path.Combine(_folder, "2026-09-02 13-05-22.tlog");

            using (var a = OpenTlog(_tlog))
            using (var service = new Gdl90Service())
            {
                service.Enable(new IPEndPoint(IPAddress.Loopback, 4000), Configured());
                service.UpdateFrameLogs(true, new Stream[] { a }, a);

                using (var b = OpenTlog(second))
                {
                    service.UpdateFrameLogs(true, new Stream[] { a, b }, a);
                    service.Dispose();
                }
            }

            var late = Gdl90LogReader.Read(Gdl90FrameLog.CompanionPath(second));

            Assert.AreEqual("127.0.0.1:4000", (string)late.Single("destination")["destination"]);
            Assert.IsNotNull(late.Single("source"));
            Assert.AreEqual(_tlog, (string)late.Single("link")["to_tlog"]);
        }

        /// <summary>The log is a debugging aid for a stream that is on; off means off.</summary>
        [TestMethod]
        public void UpdateFrameLogs_WritesNothingWhileTheServiceIsOff()
        {
            using (var tlog = OpenTlog(_tlog))
            using (var service = new Gdl90Service())
            {
                service.TlogStreams = () => new Stream[] { tlog };
                service.UpdateFrameLogs(false);

                Assert.AreEqual(0, service.FrameLogPaths.Count());
                Assert.IsFalse(File.Exists(Gdl90FrameLog.CompanionPath(_tlog)));
            }
        }
    }
}
