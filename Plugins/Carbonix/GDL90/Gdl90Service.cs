using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MissionPlanner;
using MissionPlanner.ArduPilot.Mavlink;
using Newtonsoft.Json.Linq;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Defines what the transmit service reports about itself.
    /// </summary>
    public interface IGdl90Status
    {
        /// <summary>Gets a value indicating whether the operator has started the stream.</summary>
        bool Enabled { get; }

        /// <summary>Gets where the stream is pointed, or null before it has ever been started.</summary>
        IPEndPoint Destination { get; }

        /// <summary>Gets what the selected link was carrying as of the last tick.</summary>
        Gdl90SourceKind SourceKind { get; }

        /// <summary>Gets a value indicating whether a simulator's stream has been switched on for this session.</summary>
        bool SitlStreamingEnabled { get; }

        /// <summary>Gets a value indicating whether any link to the aircraft is open.</summary>
        bool LinkOpen { get; }

        /// <summary>Gets a value indicating whether every message the ownship report needs was current as of the last tick.</summary>
        bool TelemetryComplete { get; }

        /// <summary>Gets the most recent send failure, or null once a send succeeds.</summary>
        string LastError { get; }

        /// <summary>Gets the number of frames handed to the socket since the stream was started.</summary>
        long FramesSent { get; }

        /// <summary>Gets the UTC time of the last frame handed to the socket, or default if none has been.</summary>
        DateTime LastTransmitUtc { get; }
    }

    /// <summary>
    /// Transmits the GDL 90 stream to an electronic flight bag over UDP, once a second,
    /// from live telemetry.
    /// </summary>
    /// <remarks>
    /// The loop runs on its own task. Only an aircraft is transmitted: a simulator
    /// sends nothing unless <see cref="SitlStreamingEnabled"/> is set for the session,
    /// and a replay sends nothing and writes no frame log. The creating plugin owns
    /// <see cref="Dispose"/>.
    /// </remarks>
    public sealed class Gdl90Service : IGdl90Status, IDisposable
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Represents the port electronic flight bags listen on for a GDL 90 stream.</summary>
        public const int DefaultPort = 4000;

        internal const int TransmitIntervalMs = 1000;

        const byte VehicleSysId = 1;
        const byte VehicleCompId = 1;

        // Twice the transmit rate, so a sample is never a whole second old by the time
        // it goes out. The rate manager takes the fastest active lease, so this never
        // slows anyone else's subscription down.
        const int LeasedMessageRateHz = 2;

        // Everything that stamps a record takes a clock rather than reading one, so the
        // converter can hand them the recording's time instead.
        static readonly Func<DateTime> WallClock = () => DateTime.UtcNow;

        readonly Gdl90VehicleReader _reader = new Gdl90VehicleReader();
        readonly Gdl90TrafficTracker _traffic = new Gdl90TrafficTracker(WallClock);
        readonly Gdl90FrameLogSet _frameLogs = new Gdl90FrameLogSet(WallClock);
        readonly Gdl90SourceDetector _detector = new Gdl90SourceDetector();

        volatile MAVLinkInterface _port;
        IReadOnlyList<MAVLinkInterface> _links;
        MAVLinkInterface _boundPort;
        readonly List<MessageRateLease> _leases = new List<MessageRateLease>();

        readonly object _socketLock = new object();
        UdpClient _socket;
        IPEndPoint _endpoint;

        volatile Gdl90Configuration _config = new Gdl90Configuration();
        volatile bool _enabled;
        volatile bool _sitlStreaming;
        volatile string _lastError;

        // What the logs were last told, so a change is recorded once rather than every
        // second.
        string _loggedDestination;
        string _loggedSource;
        string _selectedTlogPath;
        string _loggedMissing;

        // Frame number across the whole service, so the same frame carries the same
        // number in every copy of the log.
        long _sequence;

        CancellationTokenSource _cts;
        Task _loop;
        DateTime _lastTickUtc;
        DateTime _lastTransmitUtc;
        long _framesSent;

        /// <summary>Gets the UTC time of the most recent loop iteration, whether or not it transmitted.</summary>
        /// <remarks>The plugin loop reads this to report a stall. The loop catches everything, so a stall is a blocked call rather than a dead task.</remarks>
        public DateTime LastTickUtc
        {
            get { return _lastTickUtc; }
        }

        /// <inheritdoc/>
        public DateTime LastTransmitUtc
        {
            get { return _lastTransmitUtc; }
        }

        /// <inheritdoc/>
        public long FramesSent
        {
            get { return Interlocked.Read(ref _framesSent); }
        }

        /// <inheritdoc/>
        public string LastError
        {
            get { return _lastError; }
        }

        /// <inheritdoc/>
        public bool Enabled
        {
            get { return _enabled; }
        }

        /// <inheritdoc/>
        public IPEndPoint Destination
        {
            get { lock (_socketLock) return _endpoint; }
        }

        /// <inheritdoc/>
        public Gdl90SourceKind SourceKind { get; private set; }

        /// <inheritdoc/>
        public bool TelemetryComplete { get; private set; }

        /// <summary>Gets the paths of the frame logs being written, one per open tlog.</summary>
        public IEnumerable<string> FrameLogPaths
        {
            get { return _frameLogs.Paths; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether a simulator's stream may go on the
        /// wire, ownship and traffic together.
        /// </summary>
        /// <remarks>Cleared by <see cref="Disable"/>: the attestation covers one run.</remarks>
        public bool SitlStreamingEnabled
        {
            get { return _sitlStreaming; }
            set { _sitlStreaming = value; }
        }

        /// <inheritdoc/>
        public bool LinkOpen
        {
            get { return (LinksOpen ?? AnyLinkOpen)(); }
        }

        /// <summary>The ADS-B targets being forwarded, for tests to feed directly.</summary>
        internal Gdl90TrafficTracker Traffic
        {
            get { return _traffic; }
        }

        // Replaceable by tests, which have no link to hand the service.
        internal Func<Gdl90SourceKind> ClassifySource;
        internal Func<IEnumerable<Stream>> TlogStreams;
        internal Func<bool> LinksOpen;

        /// <summary>Switches the stream on, taking effect on the next tick.</summary>
        /// <param name="destination">Where to send the stream.</param>
        /// <param name="config">The identity to report under and the versions for the frame log headers.</param>
        /// <remarks>
        /// One call is one run. To change the destination or identity, call
        /// <see cref="Disable"/> first.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="config"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="config"/> carries no transponder ICAO address.</exception>
        public void Enable(IPEndPoint destination, Gdl90Configuration config)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (config == null) throw new ArgumentNullException(nameof(config));

            // Address type 0 asserts a registered identity. Sending it with no address
            // would claim to be an aircraft that is not this one; sending zero would
            // claim to be no aircraft at all.
            if (config.Identity == null || config.Identity.IcaoAddress == 0)
                throw new ArgumentException("no transponder ICAO address to report as", nameof(config));

            _config = config;
            ResetStatistics();

            lock (_socketLock)
            {
                // Both under the lock, so no tick can find the stream switched on while
                // the endpoint is still the last run's.
                _endpoint = destination;
                _enabled = true;
            }
        }

        /// <summary>Takes the stream off the wire, leaving the loop running so that <see cref="Enable"/> puts it back.</summary>
        public void Disable()
        {
            _sitlStreaming = false;
            _enabled = false;
            ResetStatistics();
        }

        // The counters describe the current run, not the service's whole life.
        void ResetStatistics()
        {
            Interlocked.Exchange(ref _framesSent, 0);
            _lastTransmitUtc = default(DateTime);
            _lastError = null;
        }

        /// <summary>Follows the selected link, and notes every open link for the frame logs.</summary>
        /// <param name="port">The selected link, or null when there is none.</param>
        /// <param name="allLinks">Every link Mission Planner has open, or null if <paramref name="port"/> is the only one.</param>
        /// <remarks>Call once per plugin tick.</remarks>
        // The redundant link switches occur by assigning a different MAVLinkInterface to
        // MainV2.comPort, so the selected port changes identity at runtime and anything still bound
        // to the old object stops receiving without saying so. Comparing by reference each tick is
        // what catches that.
        public void UpdatePort(MAVLinkInterface port, IEnumerable<MAVLinkInterface> allLinks = null)
        {
            _port = port;
            try
            {
                _links = (allLinks ?? (port == null
                    ? Enumerable.Empty<MAVLinkInterface>()
                    : new[] { port })).ToList();
            }
            catch (Exception ex)
            {
                // MainV2.Comports is a plain list the UI thread adds to on connect.
                // Last tick's copy stands until the next.
                _log.Debug("GDL90 could not copy the link list: " + ex.Message);
            }

            if (port == null || port == _boundPort) return;

            ReleaseLeases();
            _traffic.Unbind();
            _boundPort = port;

            // Leased because the default stream rates do not carry it and the Airborne
            // bit needs it. Everything else the report reads rides on streams Mission
            // Planner always requests.
            Lease(port, MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);

            // ADSB_VEHICLE takes a subscription and no lease: the receiver emits one
            // per target as it hears them, and getPacketLast is keyed by message id
            // alone, so polling it would keep whichever aircraft arrived most recently
            // and discard the rest of the sky.
            _traffic.Bind(port);
        }

        void Lease(MAVLinkInterface port, MAVLink.MAVLINK_MSG_ID id)
        {
            try
            {
                var lease = port.RateManager?.Subscribe(
                    VehicleSysId, VehicleCompId, id, LeasedMessageRateHz, "GDL90");
                if (lease != null) _leases.Add(lease);
            }
            catch (Exception ex)
            {
                _log.Warn($"GDL90 could not lease {id}: {ex.Message}");
            }
        }

        void ReleaseLeases()
        {
            foreach (var lease in _leases) lease.Dispose();
            _leases.Clear();
        }

        /// <summary>Starts the transmit loop, if it is not already running.</summary>
        public void Start()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
                return;

            _cts = new CancellationTokenSource();
            _lastTickUtc = DateTime.UtcNow;

            _loop = RunLoop(_cts.Token);
        }

        /// <summary>Signals the transmit loop to stop.</summary>
        /// <remarks>A subsequent <see cref="Start"/> may launch a new loop immediately; the old one exits without transmitting again.</remarks>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>Stops the loop, releases the link, closes the frame logs and the socket.</summary>
        public void Dispose()
        {
            _cts?.Cancel();
            WaitForLoop();
            ReleaseLeases();
            _traffic.Unbind();
            _boundPort = null;
            _frameLogs.Dispose();

            lock (_socketLock)
            {
                _socket?.Close();
                _socket = null;
            }
        }

        // A tick in flight finishes logging its last frame before the logs close, so
        // every log holds every frame that went on the wire.
        void WaitForLoop()
        {
            var loop = _loop;
            _loop = null;
            if (loop == null) return;

            try
            {
                loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
        }

        async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TransmitOnce();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _log.Error("GDL90 transmit loop error: " + ex.Message);
                }

                _lastTickUtc = DateTime.UtcNow;

                try
                {
                    await Task.Delay(TransmitIntervalMs, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        void TransmitOnce()
        {
            var config = _config;
            var now = DateTime.UtcNow;

            // Read whether or not the stream is on: the tab refuses to start it until
            // every message the ownship report needs is arriving.
            var vehicle = _reader.Read(_port?.MAV, now);
            NoteMissing(vehicle.MissingMessages);

            var kind = (ClassifySource ?? DetectSourceKind)();
            SourceKind = kind;

            DisarmIfRefused(kind);

            // Live may transmit, and a simulator only once it has been switched on for
            // this session. Unknown is the settling state before Live, and the tab does
            // not start a stream until it clears.
            bool active = kind == Gdl90SourceKind.Live
                || (kind == Gdl90SourceKind.Sitl && _sitlStreaming);

            _frameLogs.MissionPlannerVersion = config.MissionPlannerVersion;
            _frameLogs.PluginVersion = config.PluginVersion;

            // The log follows the send. A replay writes no frame log at all; producing
            // one from a recording is the converter's job.
            UpdateFrameLogs(_enabled && active);

            if (!_enabled || !active)
            {
                // Targets keep arriving while nothing collects them.
                _traffic.Prune(now);
                return;
            }

            // A telemetry dropout keeps transmitting: the report says the position is
            // unknown, and that is worth more to a receiver than silence, because an
            // EFB with no ownship source falls back to its own GPS, which is wherever
            // the tablet is. Once every link is closed there is no aircraft to be the
            // ownship for, and that fallback becomes the right answer.
            if (!LinkOpen) return;

            var identity = config.Identity;
            if (identity == null) return;

            var set = Gdl90FrameSet.Build(vehicle, identity, now,
                _traffic.Collect(now, identity.IcaoAddress));

            foreach (var emission in set.Emissions)
            {
                if (emission.IsFrame)
                    Send(emission.Message, emission.Name, emission.Describe);
                else
                    _frameLogs.WriteRecord(emission.Name, emission.Describe);
            }
        }

        // Names the missing messages in the Mission Planner log when the set changes;
        // the tab only says that something is missing.
        void NoteMissing(IReadOnlyList<string> missing)
        {
            TelemetryComplete = missing.Count == 0;

            string names = string.Join(", ", missing);
            if (names == _loggedMissing) return;

            _loggedMissing = names;
            _log.Info(missing.Count == 0
                ? "GDL90 receiving all telemetry"
                : "GDL90 not receiving " + names);
        }

        Gdl90SourceKind DetectSourceKind()
        {
            return _detector.Classify(_port);
        }

        // Ends the run on a refusal the operator has to answer, rather than leaving the
        // stream switched on and quietly silent.
        void DisarmIfRefused(Gdl90SourceKind kind)
        {
            // A ground station sitting idle would otherwise log one of these a second.
            if (!_enabled) return;

            if (!LinkOpen)
            {
                _log.Info("GDL90 stream stopped: no link to the aircraft");
                Disable();
                return;
            }

            if (kind == Gdl90SourceKind.Sitl && !_sitlStreaming)
            {
                _log.Info("GDL90 stream stopped: SITL detected");
                Disable();
                return;
            }

            if (kind == Gdl90SourceKind.Playback)
            {
                _log.Info("GDL90 stream stopped: replaying a log");
                Disable();
            }
        }

        // The list always holds at least the primary interface, so the empty and
        // absent cases are test conveniences rather than flight states. Both read as
        // connected: going dark on an absence of information is not a safe default
        // for a stream a pilot may be flying on.
        bool AnyLinkOpen()
        {
            var links = _links;
            if (links == null) return true;

            foreach (var link in links)
            {
                try
                {
                    if (link?.BaseStream?.IsOpen == true) return true;
                }
                catch (Exception ex)
                {
                    _log.Debug("GDL90 could not read a link's state: " + ex.Message);
                }
            }

            return false;
        }

        // One framed message per datagram.
        //
        // The log write stays below the socket write, and the early return stays above
        // it. Every row in a frame log is a promise that those bytes were handed to
        // the network stack.
        void Send(byte[] message, string name, Func<JObject> describe)
        {
            byte[] frame = Gdl90Frame.Frame(message);

            lock (_socketLock)
            {
                var endpoint = _endpoint;
                if (endpoint == null) return;

                try
                {
                    if (_socket == null) _socket = new UdpClient();
                    _socket.Send(frame, frame.Length, endpoint);
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    return;
                }
            }

            Interlocked.Increment(ref _framesSent);
            _lastTransmitUtc = DateTime.UtcNow;
            _lastError = null;

            _frameLogs.WriteFrame(Interlocked.Increment(ref _sequence), name, message[0],
                frame, describe);
        }

        IEnumerable<Stream> LinkTlogStreams()
        {
            var links = _links;
            return links == null
                ? Enumerable.Empty<Stream>()
                : links.Select(link => (Stream)link?.logfile);
        }

        internal void UpdateFrameLogs(bool enabled)
        {
            UpdateFrameLogs(enabled, (TlogStreams ?? LinkTlogStreams)(), _port?.logfile);
        }

        // Keeps a frame log open beside every link's tlog, and notes in them which
        // link is selected. No file has to be rolled on a link switch, and the logs
        // stop when the links do because each ends with its own tlog.
        internal void UpdateFrameLogs(bool enabled, IEnumerable<Stream> tlogStreams,
            Stream selectedTlog)
        {
            _frameLogs.Update(enabled, tlogStreams);

            if (!_frameLogs.IsOpen) return;

            RecordDestination();
            RecordSource();

            string selected = _frameLogs.TlogNameOf(selectedTlog);
            if (selected == _selectedTlogPath) return;

            string from = _selectedTlogPath;
            _selectedTlogPath = selected;
            _frameLogs.WriteRecord("link", () => new JObject
            {
                ["from_tlog"] = from,
                ["to_tlog"] = selected,
            }, sticky: true);
        }

        // Where the frames in this log went. A record rather than a header field
        // because a link switch opens further logs that need it replayed into them.
        void RecordDestination()
        {
            string destination = Destination?.ToString();
            if (destination == null || destination == _loggedDestination) return;

            _loggedDestination = destination;
            _frameLogs.WriteRecord("destination", () => new JObject
            {
                ["destination"] = destination,
            }, sticky: true);
        }

        // What produced these frames, and that they went somewhere. Without it a
        // simulator's log and a flight's log are the same file.
        void RecordSource()
        {
            var kind = SourceKind;
            string signal = kind == Gdl90SourceKind.Sitl
                ? Gdl90SourceDetector.SimstateSignal
                : null;
            bool streaming = _sitlStreaming;

            string state = string.Join("|", kind.ToString(), signal, streaming);
            if (state == _loggedSource) return;

            _loggedSource = state;
            _frameLogs.WriteRecord("source", () => new JObject
            {
                ["kind"] = kind.ToString().ToLowerInvariant(),
                ["signal"] = signal,

                // What the hex column promises. The converter writes the same record
                // with false, and that is the only difference between its output and
                // this one.
                ["transmitted"] = true,
                ["sitl_streaming"] = streaming,
            }, sticky: true);
        }
    }
}
