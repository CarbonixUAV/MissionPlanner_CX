using System;
using System.IO;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Comms;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// Which links the bridge is willing to be an aircraft for.
    /// </summary>
    [TestClass]
    public class Gdl90SourceDetectorTests
    {
        /// <summary>An open in-memory link, so no socket and no network are involved.</summary>
        static CommsInjection Connected()
        {
            var stream = new CommsInjection();
            stream.Open();
            return stream;
        }

        static MAVLinkInterface Playback()
        {
            return new MAVLinkInterface(new MemoryStream(new byte[0]));
        }

        [TestMethod]
        public void Classify_WithNoLink_IsLive()
        {
            Assert.AreEqual(Gdl90SourceKind.Live, new Gdl90SourceDetector().Classify(null));
        }

        /// <summary>
        /// The flag Mission Planner itself uses to refuse to write a tlog while reading
        /// one, rather than logplaybackfile, which stays non-null after a replay ends.
        /// </summary>
        [TestMethod]
        public void Classify_WhileReplayingATlog_IsPlayback()
        {
            using (var port = Playback())
            {
                Assert.AreEqual(Gdl90SourceKind.Playback, new Gdl90SourceDetector().Classify(port));
            }
        }

        [TestMethod]
        public void Classify_WhileReplayingASimulatedTlog_IsStillPlayback()
        {
            using (var port = Playback())
            {
                port.MAV.addPacket(Simstate());

                Assert.AreEqual(Gdl90SourceKind.Playback, new Gdl90SourceDetector().Classify(port));
            }
        }

        /// <summary>
        /// A simulator says so itself, so it never waits out the settling period; only
        /// a would-be Live verdict does.
        /// </summary>
        [TestMethod]
        public void Classify_WithSimstate_IsSitlImmediately()
        {
            using (var port = new MAVLinkInterface())
            {
                port.BaseStream = Connected();
                port.MAV.addPacket(Simstate());

                Assert.AreEqual(Gdl90SourceKind.Sitl, new Gdl90SourceDetector().Classify(port));
            }
        }

        /// <summary>
        /// Closing does not clear the evidence, so it must not be read after a close.
        /// </summary>
        [TestMethod]
        public void Classify_AfterAClose_DoesNotReDetectFromTheLastConnectionsPackets()
        {
            using (var port = new MAVLinkInterface())
            {
                var stream = new CommsInjection();
                stream.Open();
                port.BaseStream = stream;

                // Sysid 1, as a real autopilot uses, and placed in the master list -
                // the only list MAVlist.Clear empties. The default sysid 0 would not
                // do: MAVList seeds its hidden list with (0,0) at construction and
                // checks that list before the master one
                port.sysidcurrent = 1;
                port.compidcurrent = 1;
                port.MAVlist[1, 1] = new MAVState(port, 1, 1);
                port.MAV.addPacket(Simstate());

                var detector = new Gdl90SourceDetector();
                Assert.AreEqual(Gdl90SourceKind.Sitl, detector.Classify(port));

                // The packet is still there
                stream.Close();
                Assert.AreEqual(Gdl90SourceKind.Live, detector.Classify(port));
                Assert.AreEqual(Gdl90SourceKind.Live, detector.Classify(port),
                    "and it stays cleared rather than re-arming and re-marking every tick");

                // What MAVLinkInterface.Open does before the stream comes up, so the
                // next connection starts with no evidence at all.
                port.MAVlist.Clear();
                stream.Open();

                Assert.AreNotEqual(Gdl90SourceKind.Sitl, detector.Classify(port),
                    "the real aircraft is judged on its own evidence, first time");
            }
        }

        [TestMethod]
        public void IsSimulated_OnAReplay_ReadsThePacketsDespiteHavingNoOpenStream()
        {
            using (var port = Playback())
            {
                port.MAV.addPacket(Simstate());

                // The path the tlog converter takes: a replay's port has no BaseStream
                // at all, and guarding on "not open now" alone would label every
                // converted SITL tlog as live.
                Assert.IsTrue(new Gdl90SourceDetector().IsSimulated(port));
            }
        }

        /// <summary>
        /// Live is not a finding, it is an absence of findings, and an absence only
        /// means something once there has been time for the evidence to turn up.
        /// </summary>
        [TestMethod]
        public void Classify_IsUnknownUntilTheLinkHasBeenOpenLongEnoughForEvidenceToArrive()
        {
            var t = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);

            using (var port = new MAVLinkInterface())
            {
                var stream = new CommsInjection();
                var detector = new Gdl90SourceDetector { UtcNow = () => t };

                stream.Open();
                port.BaseStream = stream;

                Assert.AreEqual(Gdl90SourceKind.Unknown, detector.Classify(port), "just connected");

                t += Gdl90SourceDetector.SettleTime - TimeSpan.FromSeconds(1);
                Assert.AreEqual(Gdl90SourceKind.Unknown, detector.Classify(port));

                t += TimeSpan.FromSeconds(1);
                Assert.AreEqual(Gdl90SourceKind.Live, detector.Classify(port));

                // Reconnecting starts the wait again: the new connection has had no more
                // chance to declare itself than the first one had.
                stream.Close();
                detector.Classify(port);
                stream.Open();
                Assert.AreEqual(Gdl90SourceKind.Unknown, detector.Classify(port));
            }
        }

        [TestMethod]
        public void Classify_WithNoStreamAtAll_DoesNotWaitToSettle()
        {
            using (var port = new MAVLinkInterface())
            {
                Assert.AreEqual(Gdl90SourceKind.Live, new Gdl90SourceDetector().Classify(port));
            }
        }

        [TestMethod]
        public void Classify_DoesNotSpreadFromOneLinkToAnother()
        {
            using (var simulated = new MAVLinkInterface())
            using (var real = new MAVLinkInterface())
            {
                simulated.BaseStream = Connected();
                real.BaseStream = Connected();

                var detector = new Gdl90SourceDetector();
                simulated.MAV.addPacket(Simstate());

                Assert.AreEqual(Gdl90SourceKind.Sitl, detector.Classify(simulated));
                Assert.AreNotEqual(Gdl90SourceKind.Sitl, detector.Classify(real));
            }
        }

        internal static MAVLink.MAVLinkMessage Simstate()
        {
            var parser = new MAVLink.MavlinkParse();
            byte[] packet = parser.GenerateMAVLinkPacket20(
                MAVLink.MAVLINK_MSG_ID.SIMSTATE, new MAVLink.mavlink_simstate_t(),
                false, 1, 1);
            return new MAVLink.MAVLinkMessage(packet, DateTime.UtcNow);
        }
    }
}
