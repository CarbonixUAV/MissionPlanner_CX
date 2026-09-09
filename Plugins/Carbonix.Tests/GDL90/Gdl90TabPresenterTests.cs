using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Carbonix;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.GDL90
{
    [TestClass]
    public class Gdl90IcaoParsingTests
    {
        [TestMethod]
        public void TryParseIcao_AcceptsTheFormsPrintedOnATransponder()
        {
            int address;

            Assert.IsTrue(Gdl90TabPresenter.TryParseIcao("7C1A2B", out address));
            Assert.AreEqual(0x7C1A2B, address);

            Assert.IsTrue(Gdl90TabPresenter.TryParseIcao("7c1a2b", out address));
            Assert.AreEqual(0x7C1A2B, address);

            Assert.IsTrue(Gdl90TabPresenter.TryParseIcao("0x7C1A2B", out address));
            Assert.AreEqual(0x7C1A2B, address);

            Assert.IsTrue(Gdl90TabPresenter.TryParseIcao("  7C1A2B  ", out address));
            Assert.AreEqual(0x7C1A2B, address);

            Assert.IsTrue(Gdl90TabPresenter.TryParseIcao("FFFFFF", out address));
            Assert.AreEqual(0xFFFFFF, address);
        }

        [TestMethod]
        public void TryParseIcao_RejectsAnythingThatIsNotA24BitAddress()
        {
            int address;

            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("000000", out address), "zero is not an aircraft");
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("0", out address));
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("1000000", out address), "25 bits");
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("7C1A2G", out address), "not hex");
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("7C 1A 2B", out address));
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("-1", out address));
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao("", out address));
            Assert.IsFalse(Gdl90TabPresenter.TryParseIcao(null, out address));
        }
    }

    /// <summary>
    /// The decisions behind the EFB tab.
    /// </summary>
    /// <remarks>
    /// Nothing here pins what the tab says, only what it decides. Wording is changed
    /// often and by eye; what the words have to do is covered by
    /// <see cref="Describe_GivesEveryStateWordsOfItsOwn"/>, which holds the states apart
    /// without quoting any of them.
    /// </remarks>
    [TestClass]
    public class Gdl90TabPresenterTests
    {
        static readonly DateTime Now = new DateTime(2026, 9, 4, 4, 5, 6, DateTimeKind.Utc);

        static readonly IPEndPoint Efb = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 4000);

        sealed class FakeStore : IGdl90Store
        {
            public string Destination { get; set; } = "";

            public bool SitlUnlocked { get; set; }
        }

        static GeneralSettings Lists()
        {
            return new GeneralSettings
            {
                gdl90_icaos = new List<string> { "7C1A2B", "7C4D5E" },
                gdl90_callsigns = new List<string> { "VHXYZ" },
            };
        }

        /// <summary>A presenter with everything filled in, i.e. one that may be started.</summary>
        static Gdl90TabPresenter Ready(IGdl90Store store = null)
        {
            var presenter = new Gdl90TabPresenter(Lists(), store ?? new FakeStore())
            {
                Destination = "192.168.1.50",
                SelectedIcao = "7C1A2B",
                Callsign = "VHXYZ",
            };
            presenter.UtcNow = () => Now;
            return presenter;
        }

        sealed class FakeStatus : IGdl90Status
        {
            public bool Enabled { get; set; }

            public IPEndPoint Destination { get; set; }

            public Gdl90SourceKind SourceKind { get; set; }

            public bool SitlStreamingEnabled { get; set; }

            /// <summary>True unless a test says otherwise: most of them are not about the link.</summary>
            public bool LinkOpen { get; set; } = true;

            public bool TelemetryComplete { get; set; } = true;

            public string LastError { get; set; }

            public long FramesSent { get; set; }

            public DateTime LastTransmitUtc { get; set; }
        }

        static FakeStore Unlocked()
        {
            return new FakeStore { SitlUnlocked = true };
        }

        /// <summary>Stopped, live aircraft, link up: nothing wrong and nothing running.</summary>
        static FakeStatus Stopped()
        {
            return new FakeStatus();
        }

        static FakeStatus Simulated()
        {
            var status = Stopped();
            status.SourceKind = Gdl90SourceKind.Sitl;
            return status;
        }

        static FakeStatus Running(DateTime lastTransmitUtc)
        {
            return new FakeStatus
            {
                Enabled = true,
                Destination = Efb,
                FramesSent = 12345,
                LastTransmitUtc = lastTransmitUtc,
            };
        }

        /// <summary>Switched on a moment ago: counters clear, nothing gone out yet.</summary>
        static FakeStatus JustArmed()
        {
            var status = Running(default(DateTime));
            status.FramesSent = 0;
            return status;
        }

        static Gdl90ProbeResult Answered(string name = "Ops iPad")
        {
            return new Gdl90ProbeResult { Answered = true, Name = name };
        }

        static readonly Gdl90ProbeResult NoAnswer = new Gdl90ProbeResult();

        static IPAddress At(string address)
        {
            return IPAddress.Parse(address);
        }

        // ---- the states, and telling them apart ----

        /// <summary>Every situation the tab can be in, and the state it is expected to report.</summary>
        static IEnumerable<KeyValuePair<Gdl90TabState, Gdl90TabView>> EveryState()
        {
            var cases = new List<KeyValuePair<Gdl90TabState, Gdl90TabView>>();

            Action<Gdl90TabState, Gdl90TabPresenter, IGdl90Status> add =
                (expected, presenter, status) => cases.Add(
                    new KeyValuePair<Gdl90TabState, Gdl90TabView>(expected, presenter.Describe(status)));

            add(Gdl90TabState.Idle, Ready(), Stopped());

            var noDestination = Ready();
            noDestination.Destination = "";
            add(Gdl90TabState.DestinationInvalid, noDestination, Stopped());

            var badDestination = Ready();
            badDestination.Destination = "192.168.1.999";
            add(Gdl90TabState.DestinationInvalid, badDestination, Stopped());

            var badPort = Ready();
            badPort.Destination = "192.168.1.50:70000";
            add(Gdl90TabState.DestinationInvalid, badPort, Stopped());

            var noIcao = Ready();
            noIcao.SelectedIcao = null;
            add(Gdl90TabState.NoIcao, noIcao, Stopped());

            var unusableIcao = Ready();
            unusableIcao.SelectedIcao = "not-an-address";
            add(Gdl90TabState.NoIcao, unusableIcao, Stopped());

            // An empty list is a different problem from an empty selection: the fix is
            // somewhere the tab cannot reach, so it has to be said differently.
            var noIcaosAtAll = new Gdl90TabPresenter(
                new GeneralSettings { gdl90_icaos = new List<string>(), gdl90_callsigns = new List<string>() },
                new FakeStore()) { Destination = "192.168.1.50", Callsign = "VHXYZ" };
            add(Gdl90TabState.NoIcao, noIcaosAtAll, Stopped());

            var noCallsign = Ready();
            noCallsign.Callsign = "";
            add(Gdl90TabState.Idle, noCallsign, Stopped());

            var settling = Stopped();
            settling.SourceKind = Gdl90SourceKind.Unknown;
            add(Gdl90TabState.Idle, Ready(), settling);

            var replay = Stopped();
            replay.SourceKind = Gdl90SourceKind.Playback;
            add(Gdl90TabState.Playback, Ready(), replay);

            add(Gdl90TabState.SitlNotEnabled, Ready(), Simulated());

            // Unlocked but not attested: the same state, and it has to point at the
            // checkbox rather than refuse flatly.
            add(Gdl90TabState.SitlNotEnabled, Ready(Unlocked()), Simulated());

            var disconnected = Stopped();
            disconnected.LinkOpen = false;
            add(Gdl90TabState.NoLink, Ready(), disconnected);

            var incomplete = Stopped();
            incomplete.TelemetryComplete = false;
            add(Gdl90TabState.TelemetryIncomplete, Ready(), incomplete);

            var failed = Running(Now);
            failed.LastError = "No route to host";
            add(Gdl90TabState.SendFailed, Ready(), failed);

            add(Gdl90TabState.Starting, Ready(), JustArmed());

            add(Gdl90TabState.Stalled, Ready(),
                Running(Now - Gdl90TabPresenter.TransmitStale - TimeSpan.FromSeconds(1)));

            add(Gdl90TabState.Transmitting, Ready(), Running(Now));

            return cases;
        }

        [TestMethod]
        public void Describe_ReportsTheStateForEachSituation()
        {
            foreach (var expected in EveryState())
                Assert.AreEqual(expected.Key, expected.Value.State, expected.Value.StatusText);
        }

        /// <summary>
        /// The exit criterion for the whole tab: silence has several causes and the
        /// operator has to tell them apart without walking to the iPad.
        /// </summary>
        /// <remarks>
        /// Asserts that the situations are distinguishable, not what distinguishes them,
        /// so rewording is free but collapsing two of them together is not.
        /// </remarks>
        [TestMethod]
        public void Describe_GivesEveryStateWordsOfItsOwn()
        {
            var seen = new Dictionary<string, Gdl90TabState>();

            foreach (var expected in EveryState())
            {
                string text = expected.Value.StatusText;

                Assert.IsFalse(string.IsNullOrWhiteSpace(text),
                    "a silent stream that says nothing about why is the whole problem: "
                    + expected.Key);

                Assert.IsFalse(seen.ContainsKey(text),
                    "two situations cannot share one sentence: " + expected.Key
                    + " and " + (seen.ContainsKey(text) ? seen[text].ToString() : "") + " - " + text);

                seen[text] = expected.Key;
            }
        }

        // ---- which state wins, where it is not obvious ----

        /// <summary>
        /// After disconnecting there is nothing on the other end to detect, and
        /// reporting a simulator would describe a link that is not there.
        /// </summary>
        [TestMethod]
        public void Describe_AfterDisconnectingFromASimulator_ReportsTheDisconnection()
        {
            var status = Simulated();
            status.LinkOpen = false;

            Assert.AreEqual(Gdl90TabState.NoLink, Ready().Describe(status).State);
        }

        /// <summary>
        /// Transmitting needs an aircraft, and a replay is not one. A Start that got
        /// through would arm a stream that never sends and leave the button reading
        /// Stop over it.
        /// </summary>
        [TestMethod]
        public void DecideStart_WhileReplaying_IsBlocked()
        {
            var status = Stopped();
            status.SourceKind = Gdl90SourceKind.Playback;

            Assert.IsFalse(Ready().Describe(status).ButtonEnabled);

            string message;
            Assert.AreEqual(Gdl90StartDecision.Blocked, Ready().DecideStart(status, out message));
        }

        [TestMethod]
        public void Describe_WhileReplaying_OutranksTheClosedLink()
        {
            var status = Stopped();
            status.SourceKind = Gdl90SourceKind.Playback;
            status.LinkOpen = false;

            Assert.AreEqual(Gdl90TabState.Playback, Ready().Describe(status).State);
        }

        [TestMethod]
        public void Describe_WithNoLinkOpen_OutranksAStaleSendError()
        {
            var status = Running(Now);
            status.LinkOpen = false;
            status.LastError = "No route to host";

            Assert.AreEqual(Gdl90TabState.NoLink, Ready().Describe(status).State);
        }

        [TestMethod]
        public void Describe_WhileRunning_IgnoresTheInputBoxes()
        {
            var presenter = Ready();
            presenter.Destination = "192.168.1.";

            var view = presenter.Describe(Running(Now));

            Assert.AreEqual(Gdl90TabState.Transmitting, view.State);
            Assert.IsTrue(view.Running, "so the tab knows to lock the inputs");
        }

        /// <summary>
        /// A count that has stopped rising still reads as frames sent, so only the age
        /// of the last frame says whether anything is going out now.
        /// </summary>
        [TestMethod]
        public void Describe_JudgesTransmittingByFrameAgeAndNotByTheCount()
        {
            var stale = Running(Now - Gdl90TabPresenter.TransmitStale - TimeSpan.FromSeconds(1));
            Assert.AreNotEqual(0, stale.FramesSent, "frames had been going out");

            Assert.AreEqual(Gdl90TabState.Transmitting, Ready().Describe(Running(Now)).State);
            Assert.AreEqual(Gdl90TabState.Stalled, Ready().Describe(stale).State);
            Assert.AreEqual(Gdl90TabState.Starting, Ready().Describe(JustArmed()).State);
        }

        // ---- when Start may be pressed ----

        [TestMethod]
        public void CanStart_NeedsADestinationAnAddressAndACallsign()
        {
            Assert.IsTrue(Ready().CanStart);

            var noDestination = Ready();
            noDestination.Destination = "";
            Assert.IsFalse(noDestination.CanStart);

            var noAddress = Ready();
            noAddress.SelectedIcao = null;
            Assert.IsFalse(noAddress.CanStart);

            var noCallsign = Ready();
            noCallsign.Callsign = "  ";
            Assert.IsFalse(noCallsign.CanStart);
        }

        [TestMethod]
        public void Describe_WithNoLinkOpen_WillNotLetTheStreamBeStarted()
        {
            var presenter = Ready();
            var status = Stopped();
            status.LinkOpen = false;

            Assert.IsTrue(presenter.CanStart, "the selection itself is complete");
            Assert.IsFalse(presenter.Describe(status).ButtonEnabled);
        }

        /// <summary>
        /// The stream may not start until every message the ownship report needs is
        /// arriving; a settled connection that lacks one says so and stays dark.
        /// </summary>
        [TestMethod]
        public void Describe_WhileTelemetryIsIncomplete_WillNotLetTheStreamBeStarted()
        {
            var status = Stopped();
            status.TelemetryComplete = false;

            var view = Ready().Describe(status);

            Assert.AreEqual(Gdl90TabState.TelemetryIncomplete, view.State);
            Assert.IsFalse(view.ButtonEnabled);
        }

        /// <summary>The first seconds of a connection read as settling, not as a fault.</summary>
        [TestMethod]
        public void Describe_WhileSettling_DoesNotReportIncompleteTelemetry()
        {
            var status = Stopped();
            status.SourceKind = Gdl90SourceKind.Unknown;
            status.TelemetryComplete = false;

            Assert.AreEqual(Gdl90TabState.Idle, Ready().Describe(status).State);
        }

        /// <summary>A dropout after Start is the stream's business, not a reason to stop it.</summary>
        [TestMethod]
        public void Describe_WhileRunning_IgnoresIncompleteTelemetry()
        {
            var status = Running(Now);
            status.TelemetryComplete = false;

            Assert.AreEqual(Gdl90TabState.Transmitting, Ready().Describe(status).State);
        }

        /// <summary>
        /// For the first seconds of a connection a simulator looks exactly like an
        /// aircraft, so Start waits until the absence of evidence is worth something.
        /// </summary>
        [TestMethod]
        public void Describe_WhileTheSourceIsStillSettling_WillNotLetTheStreamBeStarted()
        {
            var status = Stopped();
            status.SourceKind = Gdl90SourceKind.Unknown;

            Assert.IsFalse(Ready().Describe(status).ButtonEnabled);
        }

        [TestMethod]
        public void Describe_WhileSettling_DoesNotHoldUpAnAlreadyIdentifiedSimulator()
        {
            var presenter = Ready(Unlocked());
            presenter.SitlAttested = true;

            Assert.IsTrue(presenter.Describe(Simulated()).ButtonEnabled);
        }

        [TestMethod]
        public void Describe_ButtonIsStartWhenStoppedAndStopWhenRunning()
        {
            var presenter = Ready();

            Assert.AreEqual("Start", presenter.Describe(Stopped()).ButtonText);
            Assert.AreEqual("Stop", presenter.Describe(Running(Now)).ButtonText);
        }

        [TestMethod]
        public void Describe_StopStaysPressableWhateverElseIsWrong()
        {
            var presenter = Ready();
            presenter.SelectedIcao = null;

            var status = Running(Now);
            status.LinkOpen = false;

            Assert.IsTrue(presenter.Describe(status).ButtonEnabled);
        }

        /// <summary>
        /// The button and the decision behind it cannot disagree: a dark button that
        /// would have started, or a lit one that refuses, is the tab lying about itself.
        /// </summary>
        [TestMethod]
        public void DecideStart_AgreesWithTheButtonInEverySituation()
        {
            AssertDecisionMatchesButton(Ready(), Stopped());
            AssertDecisionMatchesButton(Ready(), Simulated());
            AssertDecisionMatchesButton(Ready(Unlocked()), Simulated());

            var disconnected = Stopped();
            disconnected.LinkOpen = false;
            AssertDecisionMatchesButton(Ready(), disconnected);

            var missingTelemetry = Stopped();
            missingTelemetry.TelemetryComplete = false;
            AssertDecisionMatchesButton(Ready(), missingTelemetry);

            var settling = Stopped();
            settling.SourceKind = Gdl90SourceKind.Unknown;
            AssertDecisionMatchesButton(Ready(), settling);

            var replay = Stopped();
            replay.SourceKind = Gdl90SourceKind.Playback;
            AssertDecisionMatchesButton(Ready(), replay);

            var incomplete = Ready();
            incomplete.SelectedIcao = null;
            AssertDecisionMatchesButton(incomplete, Stopped());
        }

        static void AssertDecisionMatchesButton(Gdl90TabPresenter presenter,
            IGdl90Status status)
        {
            string message;
            bool ready = presenter.DecideStart(status, out message) == Gdl90StartDecision.Ready;

            Assert.AreEqual(presenter.Describe(status).ButtonEnabled, ready,
                "the button and the decision disagree");

            // Whatever it refuses, it says why - and says the same thing the tab is
            // already showing, so pressing Start never introduces a new complaint.
            if (!ready) Assert.AreEqual(presenter.Describe(status).StatusText, message);
        }

        // ---- the simulator gate ----

        /// <summary>
        /// Two separate things: the key is a decision about this install, the tick is a
        /// claim about this session. Neither is sufficient alone.
        /// </summary>
        [TestMethod]
        public void DecideStart_OnASimulatorNeedsBothTheKeyAndTheTick()
        {
            string message;

            var locked = Ready();
            locked.SitlAttested = true;
            Assert.AreEqual(Gdl90StartDecision.SitlRefused, locked.DecideStart(Simulated(), out message),
                "ticking a box that was never unlocked does nothing");

            var unattested = Ready(Unlocked());
            Assert.AreEqual(Gdl90StartDecision.SitlRefused,
                unattested.DecideStart(Simulated(), out message),
                "unlocking without ticking transmits nothing");

            var both = Ready(Unlocked());
            both.SitlAttested = true;
            Assert.AreEqual(Gdl90StartDecision.Ready, both.DecideStart(Simulated(), out message));
        }

        [TestMethod]
        public void HostStore_UnlocksOnTheKeysPresenceAndLocksOnlyForAnExplicitValue()
        {
            Assert.IsFalse(Gdl90HostStore.Unlocked(null), "absent");

            foreach (var value in new[] { "true", "yes", "1", "", "  " })
                Assert.IsTrue(Gdl90HostStore.Unlocked(value), "'" + value + "' is present");

            foreach (var value in new[] { "false", "False", " 0 " })
                Assert.IsFalse(Gdl90HostStore.Unlocked(value), "'" + value + "' turns it off");
        }

        [TestMethod]
        public void Describe_ShowsTheAttestationOnlyOnAnUnlockedConnectedSimulator()
        {
            Assert.IsTrue(Ready(Unlocked()).Describe(Simulated()).ShowSitlAttestation);

            Assert.IsFalse(Ready().Describe(Simulated()).ShowSitlAttestation, "not unlocked");
            Assert.IsFalse(Ready(Unlocked()).Describe(Stopped()).ShowSitlAttestation,
                "not a simulator");

            var disconnected = Simulated();
            disconnected.LinkOpen = false;
            Assert.IsFalse(Ready(Unlocked()).Describe(disconnected).ShowSitlAttestation,
                "not connected");
        }

        [TestMethod]
        public void Describe_KeepsTheAttestationOnScreenWhileTheSimulatorIsTransmitting()
        {
            var presenter = Ready(Unlocked());
            presenter.SitlAttested = true;

            var status = Running(Now);
            status.SourceKind = Gdl90SourceKind.Sitl;
            status.SitlStreamingEnabled = true;

            Assert.IsTrue(presenter.Describe(status).ShowSitlAttestation);
        }

        // ---- the callsign the operator sees is the callsign that goes out ----

        [TestMethod]
        public void NormalizedCallsign_ShowsWhatTheEncoderWillActuallySend()
        {
            Assert.AreEqual("VHABC", Gdl90TabPresenter.NormalizeCallsign("VH-ABC"));
            Assert.AreEqual("VHABC", Gdl90TabPresenter.NormalizeCallsign("vh-abc"));
            Assert.AreEqual("VHABC", Gdl90TabPresenter.NormalizeCallsign("  vh abc  "));

            // Eight characters is the whole field (ICD 3.5.1.11); the rest is dropped
            // silently on the wire, so it has to be dropped visibly here.
            Assert.AreEqual("VHABCDEF", Gdl90TabPresenter.NormalizeCallsign("VH-ABCDEFGH"));

            Assert.AreEqual("", Gdl90TabPresenter.NormalizeCallsign("---"));
            Assert.AreEqual("", Gdl90TabPresenter.NormalizeCallsign(null));
        }

        [TestMethod]
        public void Describe_CarriesTheNormalizedCallsignForTheBoxToShow()
        {
            var presenter = Ready();
            presenter.Callsign = "vh-abc";

            Assert.AreEqual("VHABC", presenter.Describe(Stopped()).NormalizedCallsign);
        }

        [TestMethod]
        public void CanStart_TreatsACallsignThatNormalizesToNothingAsAbsent()
        {
            var presenter = Ready();
            presenter.Callsign = "-----";

            Assert.IsFalse(presenter.CanStart);
        }

        // ---- the address box carries an optional port ----

        [TestMethod]
        public void Endpoint_WithNoPort_UsesTheOneEveryElectronicFlightBagListensOn()
        {
            Assert.AreEqual(Gdl90Service.DefaultPort, Ready().Endpoint.Port);
        }

        [TestMethod]
        public void Endpoint_WithAPort_SplitsItFromTheAddress()
        {
            var presenter = Ready();
            presenter.Destination = "192.168.1.50:4001";

            var endpoint = presenter.Endpoint;

            Assert.AreEqual(At("192.168.1.50"), endpoint.Address);
            Assert.AreEqual(4001, endpoint.Port);
        }

        /// <summary>
        /// An absent port is not the same as a wrong one. Silence means the default; a
        /// number out of range means the operator meant something we cannot do, and
        /// quietly substituting the default would send the stream somewhere nobody asked
        /// for.
        /// </summary>
        [TestMethod]
        public void Endpoint_WithAnUnusablePort_IsNothingRatherThanAGuess()
        {
            var presenter = Ready();
            presenter.Destination = "192.168.1.50:banana";

            Assert.IsFalse(presenter.CanStart);
            Assert.IsNull(presenter.Endpoint);
        }

        /// <summary>
        /// IPAddress.TryParse accepts the old inet_aton shorthands, so half of a typed
        /// address is itself a valid address pointing somewhere else entirely - "192.16"
        /// is 192.0.0.16. Taking that would send the stream to a real host nobody chose,
        /// with nothing on the tab looking wrong, so the box insists on four octets.
        /// </summary>
        [TestMethod]
        public void Endpoint_RejectsTheShorthandFormsThatWouldMeanAnotherHost()
        {
            var presenter = Ready();

            foreach (var shorthand in new[] { "192.16", "192.168.1", "3232235826", "0x C0A80132" })
            {
                presenter.Destination = shorthand;
                Assert.IsFalse(presenter.CanStart, shorthand + " must not be an address");
                Assert.IsNull(presenter.Endpoint);
            }

            // A leading zero is decimal here, not octal, so what is typed is what is
            // meant - or it is refused, never quietly reinterpreted.
            presenter.Destination = "192.168.1.010";
            Assert.IsTrue(presenter.CanStart);
            Assert.AreEqual(At("192.168.1.10"), presenter.Endpoint.Address);
        }

        /// <summary>
        /// The socket is not a broadcast socket, so a .255 address would parse, start,
        /// and then fail every send with a permissions error that names nothing. The
        /// box refuses it instead.
        /// </summary>
        [TestMethod]
        public void Endpoint_RejectsABroadcastAddress()
        {
            var presenter = Ready();

            foreach (var broadcast in new[] { "192.168.1.255", "255.255.255.255", "10.0.0.255:4000" })
            {
                presenter.Destination = broadcast;
                Assert.IsFalse(presenter.CanStart, broadcast + " must not be a destination");
                Assert.IsNull(presenter.Endpoint);
            }

            presenter.Destination = "192.168.255.1";
            Assert.IsTrue(presenter.CanStart, "only the host octet marks a broadcast");
        }

        [TestMethod]
        public void Describe_WithAnAddressThatWillNotParse_TintsTheBox()
        {
            var presenter = Ready();
            presenter.Destination = "192.16";

            var view = presenter.Describe(Stopped());

            Assert.AreEqual(Gdl90TabState.DestinationInvalid, view.State);
            Assert.IsTrue(view.DestinationInError);

            // Nothing was asked of it, so the line under the box says nothing and the
            // status line carries the complaint alone.
            Assert.AreEqual("", view.LivenessText);
        }

        [TestMethod]
        public void Describe_WithAnEmptyAddress_DoesNotTint()
        {
            var presenter = Ready();
            presenter.Destination = "";

            var view = presenter.Describe(Stopped());

            Assert.IsFalse(view.DestinationInError);
            Assert.AreEqual("", view.LivenessText);
            Assert.AreEqual(Gdl90TabState.DestinationInvalid, view.State);
        }

        // ---- what persists, and what does not ----

        [TestMethod]
        public void Save_KeepsTheDestinationAndNothingElse()
        {
            var store = new FakeStore();
            var presenter = Ready(store);
            presenter.Destination = "192.168.1.50:4001";

            presenter.Save();

            // Nothing else can be written - the store has no other member - so the
            // selection cannot survive the session even by accident.
            Assert.AreEqual("192.168.1.50:4001", store.Destination);
        }

        [TestMethod]
        public void Construction_ReadsBackTheStoredDestination()
        {
            var store = new FakeStore();
            store.Destination = "10.0.0.7:4001";

            Assert.AreEqual("10.0.0.7:4001", new Gdl90TabPresenter(Lists(), store).Destination);
        }

        [TestMethod]
        public void Construction_OffersTheListsButSelectsNothing()
        {
            var presenter = new Gdl90TabPresenter(Lists(), new FakeStore());

            CollectionAssert.AreEqual(new[] { "7C1A2B", "7C4D5E" }, presenter.IcaoOptions.ToArray());
            CollectionAssert.AreEqual(new[] { "VHXYZ" }, presenter.CallsignOptions.ToArray());
            Assert.IsNull(presenter.SelectedIcao);
            Assert.AreEqual("", presenter.Callsign);
            Assert.IsFalse(presenter.CanStart);
        }

        // ---- the configuration handed to the service ----

        [TestMethod]
        public void BuildConfiguration_CarriesTheSelectionAndTheNormalizedCallsign()
        {
            var presenter = Ready();
            presenter.Callsign = "vh-xyz";
            presenter.MissionPlannerVersion = "1.3.83";
            presenter.PluginVersion = "2.4";

            var config = presenter.BuildConfiguration();

            Assert.AreEqual(0x7C1A2B, config.Identity.IcaoAddress);
            Assert.AreEqual("VHXYZ", config.Identity.Callsign,
                "the wire carries what the tab showed, not what was typed");
            Assert.AreEqual("1.3.83", config.MissionPlannerVersion);
            Assert.AreEqual("2.4", config.PluginVersion);
        }

        [TestMethod]
        public void BuildConfiguration_WithoutACallsign_StillCarriesTheAddress()
        {
            var presenter = Ready();
            presenter.Callsign = "";

            var config = presenter.BuildConfiguration();

            Assert.AreEqual(0x7C1A2B, config.Identity.IcaoAddress);
            Assert.IsNull(config.Identity.Callsign);
        }

        [TestMethod]
        public void BuildConfiguration_WithoutAnAddress_YieldsNoIdentityAtAll()
        {
            var presenter = Ready();
            presenter.SelectedIcao = null;

            Assert.IsNull(presenter.BuildConfiguration().Identity);
        }

        /// <summary>
        /// The service is what the tab actually reads, so what it reports has to line up
        /// with what it was switched on with.
        /// </summary>
        [TestMethod]
        public void Service_ReportsTheStatusItWasEnabledWith()
        {
            var presenter = Ready();

            using (var service = new Gdl90Service())
            {
                service.Enable(presenter.Endpoint, presenter.BuildConfiguration());

                IGdl90Status status = service;

                Assert.IsTrue(status.Enabled);
                Assert.AreEqual(Efb, status.Destination);
                Assert.AreEqual(Gdl90SourceKind.Live, status.SourceKind);
                Assert.IsTrue(status.LinkOpen);
                Assert.AreEqual(0, status.FramesSent);
            }
        }

        // ---- liveness ----

        [TestMethod]
        public void ApplyProbe_ReportsAnAnswerAndThenAgesItOut()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), Answered(), Now);

            presenter.UtcNow = () => Now + Gdl90DeviceProbe.FreshFor;
            Assert.AreEqual(Gdl90Liveness.Answered, presenter.Liveness);

            presenter.UtcNow = () => Now + Gdl90DeviceProbe.FreshFor + TimeSpan.FromSeconds(1);
            Assert.AreEqual(Gdl90Liveness.Silent, presenter.Liveness);
        }

        [TestMethod]
        public void ApplyProbe_WithNothingAtTheAddress_IsSilent()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), NoAnswer, Now);

            Assert.AreEqual(Gdl90Liveness.Silent, presenter.Liveness);
        }

        /// <summary>
        /// Not having asked about an address is a different thing from having asked and
        /// heard nothing. Only the second is a typo.
        /// </summary>
        [TestMethod]
        public void Liveness_ForAnAddressNeverAskedAbout_IsUnknownRatherThanSilent()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.40"), NoAnswer, Now);

            Assert.AreEqual(Gdl90Liveness.Unknown, presenter.Liveness,
                "the probe was about some other address");
        }

        [TestMethod]
        public void Describe_TellsADeviceThatLeftFromAnAddressThatWasNeverRight()
        {
            var left = Ready();
            left.ApplyProbe(At("192.168.1.50"), Answered(), Now);
            left.UtcNow = () => Now + TimeSpan.FromMinutes(4);

            var never = Ready();
            never.ApplyProbe(At("192.168.1.50"), NoAnswer, Now);

            Assert.AreEqual(Gdl90Liveness.Silent, left.Liveness);
            Assert.AreEqual(Gdl90Liveness.Silent, never.Liveness);
            Assert.AreNotEqual(never.Describe(Stopped()).LivenessText,
                left.Describe(Stopped()).LivenessText);
        }

        [TestMethod]
        public void Liveness_RemembersEveryAddressItHasAskedAbout()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), NoAnswer, Now);
            presenter.ApplyProbe(At("192.168.1.60"), Answered(), Now);

            Assert.AreEqual(Gdl90Liveness.Silent, presenter.Liveness);

            presenter.Destination = "192.168.1.60";
            Assert.AreEqual(Gdl90Liveness.Answered, presenter.Liveness);
        }

        [TestMethod]
        public void Liveness_WithAPortOnTheAddress_StillMatches()
        {
            var presenter = Ready();
            presenter.Destination = "192.168.1.50:4001";
            presenter.ApplyProbe(At("192.168.1.50"), Answered(), Now);

            Assert.AreEqual(Gdl90Liveness.Answered, presenter.Liveness);
        }

        [TestMethod]
        public void Liveness_SurvivesALostProbeOrTwo()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), Answered(), Now);

            presenter.ApplyProbe(At("192.168.1.50"), NoAnswer, Now + Gdl90DeviceProbe.Interval);
            presenter.UtcNow = () => Now + Gdl90DeviceProbe.Interval + Gdl90DeviceProbe.Interval;

            Assert.AreEqual(Gdl90Liveness.Answered, presenter.Liveness);
        }

        [TestMethod]
        public void Liveness_WithAnUnusableAddress_IsUnknown()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), Answered(), Now);
            presenter.Destination = "banana";

            Assert.AreEqual(Gdl90Liveness.Unknown, presenter.Liveness);
        }

        /// <summary>
        /// An answer that carries no name - a ping, or a reply too mangled to read one
        /// out of - must not erase a name already known. The name is the caller's, not
        /// ours, so looking for it is not a wording assertion.
        /// </summary>
        [TestMethod]
        public void ApplyProbe_KeepsTheLastNameItWasGiven()
        {
            var presenter = Ready();

            presenter.ApplyProbe(At("192.168.1.50"), Answered("Ops iPad"), Now);
            presenter.ApplyProbe(At("192.168.1.50"), Answered(null), Now);

            StringAssert.Contains(presenter.Describe(Stopped()).LivenessText, "Ops iPad");
        }

        /// <summary>The answer turns on the address and never the name.</summary>
        [TestMethod]
        public void Liveness_KeysOnTheAddressAndNotTheName()
        {
            var presenter = Ready();
            presenter.ApplyProbe(At("192.168.1.50"), Answered("Somebody's iPhone"), Now);

            Assert.AreEqual(Gdl90Liveness.Answered, presenter.Liveness);
            StringAssert.Contains(presenter.Describe(Stopped()).LivenessText, "Somebody's iPhone");
        }

        [TestMethod]
        public void ApplyProbe_SurvivesBeingToldNothingUseful()
        {
            var presenter = Ready();

            presenter.ApplyProbe(null, Answered(), Now);
            Assert.AreEqual(Gdl90Liveness.Unknown, presenter.Liveness);

            presenter.ApplyProbe(At("192.168.1.50"), null, Now);
            Assert.AreEqual(Gdl90Liveness.Silent, presenter.Liveness,
                "asked and told nothing is still asked");
        }

        [TestMethod]
        public void NeedsName_UntilOneIsGiven()
        {
            var presenter = Ready();
            var address = At("192.168.1.50");

            Assert.IsTrue(presenter.NeedsName(address), "never asked");

            presenter.ApplyProbe(address, Answered("Ops iPad"), Now);
            Assert.IsFalse(presenter.NeedsName(address),
                "a device does not rename itself mid-session");
        }

        /// <summary>
        /// A device that answers no mDNS - anything that is not an Apple device - would
        /// otherwise cost the full query timeout on every probe, forever, for a reply
        /// that is never coming.
        /// </summary>
        [TestMethod]
        public void NeedsName_BacksOffAfterAskingAndGettingNothing()
        {
            var presenter = Ready();
            var address = At("192.168.1.50");

            presenter.ApplyProbe(address,
                new Gdl90ProbeResult { Answered = true, NameQueried = true }, Now);

            Assert.IsFalse(presenter.NeedsName(address), "just asked");

            presenter.UtcNow = () => Now + Gdl90TabPresenter.NameRetry;
            Assert.IsTrue(presenter.NeedsName(address),
                "but it is worth another go later - it may only have been asleep");
        }

        /// <summary>
        /// A ping-only answer does not count as having asked, or an address whose name
        /// query never ran would be treated as one that ran and found nothing.
        /// </summary>
        [TestMethod]
        public void NeedsName_IsNotSatisfiedByAProbeThatNeverAsked()
        {
            var presenter = Ready();
            var address = At("192.168.1.50");

            presenter.ApplyProbe(address, new Gdl90ProbeResult { Answered = true }, Now);

            Assert.IsTrue(presenter.NeedsName(address));
        }
    }
}
