using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Specifies why the stream is, or is not, on the wire.
    /// </summary>
    public enum Gdl90TabState
    {
        /// <summary>Nothing is wrong and nothing has been started.</summary>
        Idle,

        /// <summary>The destination address or the port will not parse.</summary>
        DestinationInvalid,

        /// <summary>No usable transponder address is selected.</summary>
        NoIcao,

        /// <summary>The source is a tlog being replayed, which never transmits.</summary>
        Playback,

        /// <summary>The source is a simulator that has not been unlocked and attested to.</summary>
        SitlNotEnabled,

        /// <summary>No link to the aircraft is open.</summary>
        NoLink,

        /// <summary>Connected, but not every message the ownship report needs is arriving.</summary>
        TelemetryIncomplete,

        /// <summary>Started, and the last send failed.</summary>
        SendFailed,

        /// <summary>Started, and nothing has reached the socket yet.</summary>
        Starting,

        /// <summary>Started, frames were reaching the socket, and have stopped.</summary>
        Stalled,

        /// <summary>Started, and frames are going out.</summary>
        Transmitting,
    }

    /// <summary>
    /// Specifies what the last probe says about the configured address.
    /// </summary>
    public enum Gdl90Liveness
    {
        /// <summary>Nothing has been asked yet, or there is no address to ask about.</summary>
        Unknown,

        /// <summary>Something at that address answered recently enough to still count.</summary>
        Answered,

        /// <summary>That address has been asked and is not answering.</summary>
        Silent,
    }

    /// <summary>
    /// Specifies what pressing Start should do.
    /// </summary>
    public enum Gdl90StartDecision
    {
        /// <summary>Something the operator can fix, or wait for, is missing.</summary>
        Blocked,

        /// <summary>Start the stream.</summary>
        Ready,

        /// <summary>The source is a simulator that has not been unlocked and attested to.</summary>
        SitlRefused,
    }

    /// <summary>
    /// Defines the settings that outlive a Mission Planner session.
    /// </summary>
    public interface IGdl90Store
    {
        /// <summary>Gets or sets the destination as typed, <c>address[:port]</c>.</summary>
        string Destination { get; set; }

        /// <summary>Gets a value indicating whether the simulator attestation may be shown.</summary>
        /// <remarks>Set by hand in the configuration file, never by the tab.</remarks>
        bool SitlUnlocked { get; }
    }

    /// <summary>
    /// Provides the settings store backed by Mission Planner's config.xml.
    /// </summary>
    public sealed class Gdl90HostStore : IGdl90Store
    {
        const string DestinationKey = "cbx_gdl90_destination";

        const string SitlUnlockKey = "IUnderstandTheRisksOfEFBWithSITL";

        readonly MissionPlanner.Utilities.Settings _config;

        /// <summary>Initializes a new instance of the <see cref="Gdl90HostStore"/> class.</summary>
        /// <param name="config">Mission Planner's settings, or null for a store that remembers nothing.</param>
        public Gdl90HostStore(MissionPlanner.Utilities.Settings config)
        {
            _config = config;
        }

        /// <inheritdoc/>
        public string Destination
        {
            get { return _config == null ? "" : _config[DestinationKey, ""]; }
            set { if (_config != null) _config[DestinationKey] = value; }
        }

        /// <inheritdoc/>
        public bool SitlUnlocked
        {
            get { return Unlocked(_config?[SitlUnlockKey]); }
        }

        // Present means on, since the key's name says what writing it means. It can be
        // turned off again without deleting the line, but only by an explicit value.
        internal static bool Unlocked(string value)
        {
            if (value == null) return false;

            value = value.Trim();
            return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                && value != "0";
        }
    }

    /// <summary>
    /// Represents everything the EFB tab paints, from one call.
    /// </summary>
    public sealed class Gdl90TabView
    {
        /// <summary>Gets or sets the one state the tab reports.</summary>
        public Gdl90TabState State { get; set; }

        /// <summary>Gets or sets the status line.</summary>
        public string StatusText { get; set; }

        /// <summary>Gets or sets a value indicating whether the stream is running, so the inputs are locked.</summary>
        public bool Running { get; set; }

        /// <summary>Gets or sets the Start button's caption.</summary>
        public string ButtonText { get; set; }

        /// <summary>Gets or sets a value indicating whether the Start button may be pressed.</summary>
        public bool ButtonEnabled { get; set; }

        /// <summary>Gets or sets the callsign exactly as it will be transmitted.</summary>
        public string NormalizedCallsign { get; set; }

        /// <summary>Gets or sets what the last probe says about the destination.</summary>
        public Gdl90Liveness Liveness { get; set; }

        /// <summary>Gets or sets the line under the address box.</summary>
        public string LivenessText { get; set; }

        /// <summary>Gets or sets a value indicating whether the address box is tinted as wrong.</summary>
        public bool DestinationInError { get; set; }

        /// <summary>Gets or sets a value indicating whether the simulator attestation belongs on screen.</summary>
        public bool ShowSitlAttestation { get; set; }
    }

    /// <summary>
    /// Provides every decision the EFB tab makes: which state applies, whether Start
    /// may be pressed, what the inputs mean, and what a typed callsign goes out as.
    /// </summary>
    public sealed class Gdl90TabPresenter
    {
        /// <summary>Represents the refusal shown on a simulator that has not been unlocked.</summary>
        public const string SitlRefusedMessage = "SITL detected: GDL90 stream disallowed.";

        /// <summary>Represents the refusal shown on an unlocked simulator that has not been attested to.</summary>
        public const string SitlUnattestedMessage =
            "SITL detected: DO NOT TRANSMIT SITL FLIGHTS TO AvPlan Live.";

        /// <summary>Represents how stale the last transmit may be before the stream stops counting as transmitting.</summary>
        internal static readonly TimeSpan TransmitStale = TimeSpan.FromSeconds(3);

        /// <summary>Represents how long to leave an address alone before asking its name again.</summary>
        internal static readonly TimeSpan NameRetry = TimeSpan.FromSeconds(60);

        readonly IGdl90Store _store;

        string _destination;
        string _icao;
        string _callsign = "";

        // Every address asked about this session, what it said and when.
        readonly Dictionary<IPAddress, SeenDevice> _seen = new Dictionary<IPAddress, SeenDevice>();

        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

        /// <summary>Initializes a new instance of the <see cref="Gdl90TabPresenter"/> class.</summary>
        /// <param name="settings">The settings that supply the ICAO and callsign lists, or null for empty lists.</param>
        /// <param name="store">Where the destination persists, or null to persist nothing.</param>
        public Gdl90TabPresenter(GeneralSettings settings, IGdl90Store store)
        {
            _store = store;

            IcaoOptions = Clean(settings == null ? null : settings.gdl90_icaos);
            CallsignOptions = Clean(settings == null ? null : settings.gdl90_callsigns);

            _destination = _store == null ? "" : _store.Destination ?? "";
        }

        /// <summary>Gets or sets Mission Planner's version, for the frame log headers.</summary>
        public string MissionPlannerVersion { get; set; }

        /// <summary>Gets or sets the plugin's version, for the frame log headers.</summary>
        public string PluginVersion { get; set; }

        /// <summary>Gets the transponder addresses on offer.</summary>
        public IReadOnlyList<string> IcaoOptions { get; }

        /// <summary>Gets the callsigns on offer.</summary>
        public IReadOnlyList<string> CallsignOptions { get; }

        /// <summary>Gets or sets the destination as typed: an IPv4 address, optionally followed by <c>:port</c>.</summary>
        public string Destination
        {
            get { return _destination; }
            set { _destination = value; }
        }

        /// <summary>Gets or sets the selected transponder address, or null when none is selected.</summary>
        public string SelectedIcao
        {
            get { return _icao; }
            set { _icao = value; }
        }

        /// <summary>Gets or sets the callsign as typed.</summary>
        public string Callsign
        {
            get { return _callsign; }
            set { _callsign = value; }
        }

        /// <summary>Gets the callsign as the encoder will transmit it, without the padding.</summary>
        public string NormalizedCallsign
        {
            get { return NormalizeCallsign(_callsign); }
        }

        /// <summary>Normalizes a callsign the way the encoder does.</summary>
        /// <param name="text">The callsign as typed.</param>
        /// <returns>What the Flight ID field will carry, without the padding.</returns>
        public static string NormalizeCallsign(string text)
        {
            return Encoding.ASCII.GetString(Gdl90Messages.CallsignToBytes(text)).TrimEnd(' ');
        }

        /// <summary>Parses a 24-bit ICAO address written in hex, as printed on a transponder.</summary>
        /// <param name="text">The address, with or without a "0x" prefix.</param>
        /// <param name="address">When this method returns, the address, or zero if the text is not one.</param>
        /// <returns>true if the text is a non-zero 24-bit address; otherwise, false.</returns>
        // Zero is rejected because address type 0 claims a registered identity, and no
        // aircraft is registered as zero.
        public static bool TryParseIcao(string text, out int address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            if (text.Length == 0 || text.Length > 6) return false;

            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }

            int parsed;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                return false;

            if (parsed <= 0 || parsed > Gdl90OwnshipIdentity.Icao24BitMax) return false;

            address = parsed;
            return true;
        }

        /// <summary>Gets the endpoint the destination names, or null when the box does not hold one.</summary>
        public IPEndPoint Endpoint
        {
            get { return Parse().Endpoint; }
        }

        /// <summary>Gets the address to probe, or null when the box does not hold one.</summary>
        public IPAddress ProbeAddress
        {
            get { return Parse().Address; }
        }

        bool DestinationValid
        {
            get { return Parse().IsValid; }
        }

        bool IcaoValid
        {
            get { return TryParseIcao(_icao, out _); }
        }

        // Decided after normalization, so punctuation alone is nothing entered.
        bool CallsignValid
        {
            get { return NormalizedCallsign.Length > 0; }
        }

        /// <summary>Gets a value indicating whether the selection is complete enough to start.</summary>
        public bool CanStart
        {
            get { return DestinationValid && IcaoValid && CallsignValid; }
        }

        /// <summary>Records what one address said when it was probed.</summary>
        /// <param name="address">The address probed.</param>
        /// <param name="result">What it said, or null for nothing.</param>
        /// <param name="whenUtc">When it was probed.</param>
        public void ApplyProbe(IPAddress address, Gdl90ProbeResult result, DateTime whenUtc)
        {
            if (address == null) return;

            SeenDevice device;
            if (!_seen.TryGetValue(address, out device))
            {
                device = new SeenDevice();
                _seen[address] = device;
            }

            if (result != null && result.NameQueried) device.NameAskedUtc = whenUtc;

            if (result == null || !result.Answered) return;

            device.Answered = true;
            device.LastSeenUtc = whenUtc;

            // An answer carrying no name is still an answer, and must not erase a name
            // already known.
            if (!string.IsNullOrEmpty(result.Name)) device.Name = result.Name;
        }

        /// <summary>Determines whether the next probe of an address should ask for its name.</summary>
        /// <param name="address">The address about to be probed.</param>
        /// <returns>false once a name is known, or for a while after asking and getting none; otherwise, true.</returns>
        public bool NeedsName(IPAddress address)
        {
            if (address == null) return false;

            SeenDevice device;
            if (!_seen.TryGetValue(address, out device)) return true;
            if (!string.IsNullOrEmpty(device.Name)) return false;

            return UtcNow() - device.NameAskedUtc >= NameRetry;
        }

        /// <summary>Gets what the last probe says about the address in the box.</summary>
        public Gdl90Liveness Liveness
        {
            get { return Resolve(out _, out _); }
        }

        Gdl90Liveness Resolve(out SeenDevice device, out IPAddress address)
        {
            device = null;
            address = Parse().Address;

            if (address == null) return Gdl90Liveness.Unknown;

            // Never asked, which is not the same as asked and unanswered.
            if (!_seen.TryGetValue(address, out device)) return Gdl90Liveness.Unknown;

            if (!device.Answered) return Gdl90Liveness.Silent;

            return UtcNow() - device.LastSeenUtc <= Gdl90DeviceProbe.FreshFor
                ? Gdl90Liveness.Answered
                : Gdl90Liveness.Silent;
        }

        string DescribeAddress()
        {
            // Nothing has been asked of an address that is not one; the status line
            // carries that complaint.
            var parsed = Parse();
            if (parsed.Address == null) return "";

            SeenDevice device;
            IPAddress address;
            switch (Resolve(out device, out address))
            {
                case Gdl90Liveness.Answered:
                    return "Detected: " + Identify(device, address);

                case Gdl90Liveness.Silent:
                    return device.Answered
                        ? "Last detected " + Ago(UtcNow() - device.LastSeenUtc) + " ago: "
                            + Identify(device, address)
                        : "Nothing detected at " + address;

                default:
                    return "Not checked";
            }
        }

        static string Identify(SeenDevice device, IPAddress address)
        {
            return string.IsNullOrEmpty(device.Name) ? address.ToString() : device.Name;
        }

        static string Ago(TimeSpan age)
        {
            int minutes = (int)age.TotalMinutes;
            return minutes < 60
                ? Math.Max(minutes, 1).ToString(CultureInfo.InvariantCulture) + " min"
                : ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + " h";
        }

        /// <summary>Describes what the tab should paint, given what the service is doing.</summary>
        /// <param name="status">The service's own account of itself.</param>
        /// <returns>Everything the tab paints.</returns>
        public Gdl90TabView Describe(IGdl90Status status)
        {
            var view = new Gdl90TabView
            {
                Running = status.Enabled,

                ButtonText = status.Enabled ? "Stop" : "Start",
                ButtonEnabled = status.Enabled || MayStart(status),
                NormalizedCallsign = NormalizedCallsign,

                Liveness = Liveness,
                LivenessText = DescribeAddress(),

                // An empty box has not been filled in yet, which is not the same as
                // being wrong.
                DestinationInError = !string.IsNullOrWhiteSpace(_destination) && !Parse().IsValid,

                ShowSitlAttestation = SitlUnlocked
                    && status.LinkOpen
                    && status.SourceKind == Gdl90SourceKind.Sitl,
            };

            AssignState(view, status);
            return view;
        }

        void AssignState(Gdl90TabView view, IGdl90Status status)
        {
            if (!status.Enabled)
            {
                var parsed = Parse();
                if (!parsed.IsValid)
                {
                    Set(view, Gdl90TabState.DestinationInvalid, parsed.Problem);
                    return;
                }

                if (!IcaoValid)
                {
                    Set(view, Gdl90TabState.NoIcao, IcaoComplaint());
                    return;
                }

                if (!CallsignValid)
                {
                    Set(view, Gdl90TabState.Idle, "Idle - no callsign entered");
                    return;
                }
            }

            if (status.SourceKind == Gdl90SourceKind.Playback)
            {
                Set(view, Gdl90TabState.Playback, "Cannot transmit during tlog playback");
                return;
            }

            if (!status.LinkOpen)
            {
                Set(view, Gdl90TabState.NoLink, "Not connected to the aircraft");
                return;
            }

            if (status.SourceKind == Gdl90SourceKind.Sitl
                && !status.SitlStreamingEnabled && !SitlPermitted)
            {
                Set(view, Gdl90TabState.SitlNotEnabled,
                    SitlUnlocked ? SitlUnattestedMessage : SitlRefusedMessage);
                return;
            }

            if (!status.Enabled)
            {
                if (!SourceConfirmed(status))
                {
                    Set(view, Gdl90TabState.Idle, "Idle - initializing...");
                    return;
                }

                // Only once the source has settled: a connection is a few seconds old
                // before every stream is flowing, and that is not worth a complaint.
                if (!status.TelemetryComplete)
                {
                    Set(view, Gdl90TabState.TelemetryIncomplete, "Idle - not receiving all telemetry");
                    return;
                }

                Set(view, Gdl90TabState.Idle, "Idle - ready");
                return;
            }

            if (!string.IsNullOrEmpty(status.LastError))
            {
                Set(view, Gdl90TabState.SendFailed, "Send failed: " + status.LastError);
                return;
            }

            // By frame age rather than by count: a loop that has died, or a source
            // that has stopped qualifying, stops sending without reporting an error,
            // and a count that has stopped rising still reads as frames sent.
            if (UtcNow() - status.LastTransmitUtc > TransmitStale)
            {
                bool sentSomething = status.FramesSent > 0;
                Set(view, sentSomething ? Gdl90TabState.Stalled : Gdl90TabState.Starting,
                    sentSomething
                        ? "Started - transmission has stopped"
                        : "Started - no frames sent yet");
                return;
            }

            Set(view, Gdl90TabState.Transmitting, "Transmitting to " + status.Destination
                + " - " + status.FramesSent.ToString("N0", CultureInfo.InvariantCulture)
                + " frames sent");
        }

        /// <summary>Decides what pressing Start should do.</summary>
        /// <param name="status">The service's own account of itself.</param>
        /// <param name="message">When the decision is not <see cref="Gdl90StartDecision.Ready"/>, why; otherwise null.</param>
        /// <returns>The decision.</returns>
        public Gdl90StartDecision DecideStart(IGdl90Status status, out string message)
        {
            message = null;

            if (!MayStart(status))
            {
                // The same sentence the status line is already showing.
                message = Describe(status).StatusText;

                return status.SourceKind == Gdl90SourceKind.Sitl && !SitlPermitted
                    ? Gdl90StartDecision.SitlRefused
                    : Gdl90StartDecision.Blocked;
            }

            return Gdl90StartDecision.Ready;
        }

        // Whether the button and the decision behind it agree the stream may start.
        bool MayStart(IGdl90Status status)
        {
            return CanStart
                && status.LinkOpen
                && SourceConfirmed(status)
                && SourceAllowed(status)
                && status.TelemetryComplete;
        }

        bool SourceAllowed(IGdl90Status status)
        {
            return status.SourceKind != Gdl90SourceKind.Playback
                && (status.SourceKind != Gdl90SourceKind.Sitl || SitlPermitted);
        }

        static bool SourceConfirmed(IGdl90Status status)
        {
            return status.SourceKind != Gdl90SourceKind.Unknown;
        }

        /// <summary>Gets a value indicating whether the simulator attestation may be shown at all.</summary>
        public bool SitlUnlocked
        {
            get { return _store != null && _store.SitlUnlocked; }
        }

        /// <summary>Gets or sets a value indicating whether the operator has ticked the attestation.</summary>
        /// <remarks>Never persisted: the attestation covers one session.</remarks>
        public bool SitlAttested { get; set; }

        bool SitlPermitted
        {
            get { return SitlUnlocked && SitlAttested; }
        }

        /// <summary>Builds the configuration to start the stream with.</summary>
        /// <returns>The identity and versions; the identity is null when no usable address is selected.</returns>
        public Gdl90Configuration BuildConfiguration()
        {
            var config = new Gdl90Configuration
            {
                MissionPlannerVersion = MissionPlannerVersion,
                PluginVersion = PluginVersion,
            };

            int address;
            if (!TryParseIcao(_icao, out address)) return config;

            // An address with no callsign is still an identity: the report can state a
            // missing flight ID, but not a missing address. The tab is stricter and
            // will not start without one.
            string callsign = NormalizedCallsign;
            config.Identity = new Gdl90OwnshipIdentity
            {
                IcaoAddress = address,
                Callsign = callsign.Length > 0 ? callsign : null,
            };
            return config;
        }

        /// <summary>Writes the destination back to the store.</summary>
        public void Save()
        {
            if (_store == null) return;

            _store.Destination = (_destination ?? "").Trim();
        }

        string IcaoComplaint()
        {
            if (IcaoOptions.Count == 0)
                return "No transponder addresses configured";

            if (string.IsNullOrWhiteSpace(_icao))
                return "No transponder selected";

            return "'" + _icao.Trim() + "' is not a 24-bit ICAO address";
        }

        static void Set(Gdl90TabView view, Gdl90TabState state, string text)
        {
            view.State = state;
            view.StatusText = text;
        }

        Gdl90Destination Parse()
        {
            return Gdl90Destination.Parse(_destination);
        }

        static IReadOnlyList<string> Clean(List<string> options)
        {
            if (options == null) return new string[0];

            return options
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Select(option => option.Trim())
                .ToList();
        }
    }

    /// <summary>What one address has said, across every time it was asked.</summary>
    sealed class SeenDevice
    {
        /// <summary>Kept after it ages out, so a device that has gone reads differently from a typo.</summary>
        public bool Answered;

        public string Name;

        public DateTime LastSeenUtc;

        /// <summary>When its name was last asked for, whether or not one came back.</summary>
        public DateTime NameAskedUtc;
    }

    /// <summary>What an <c>address[:port]</c> string means.</summary>
    sealed class Gdl90Destination
    {
        /// <summary>The address, or null when the text does not give one.</summary>
        public IPAddress Address { get; private set; }

        /// <summary>The port: the default when the text gives none, zero when it gives an unusable one.</summary>
        public int Port { get; private set; }

        /// <summary>What is wrong with the text, or null when nothing is.</summary>
        public string Problem { get; private set; }

        public bool IsValid
        {
            get { return Problem == null; }
        }

        /// <summary>The endpoint, or null when the text is not valid.</summary>
        public IPEndPoint Endpoint
        {
            get { return IsValid ? new IPEndPoint(Address, Port) : null; }
        }

        public static Gdl90Destination Parse(string text)
        {
            var result = new Gdl90Destination { Port = Gdl90Service.DefaultPort };

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Problem = "No destination - enter the address of the electronic flight bag";
                return result;
            }

            text = text.Trim();
            string host = text;

            // One colon separates a port. More than one is IPv6-shaped and goes to the
            // address check whole, to be rejected there for the right reason.
            int colon = text.IndexOf(':');
            if (colon >= 0 && colon == text.LastIndexOf(':'))
            {
                host = text.Substring(0, colon);
                string portText = text.Substring(colon + 1);

                int port;
                if (!int.TryParse(portText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                        out port) || port <= 0 || port > 65535)
                {
                    result.Port = 0;
                    result.Problem = "Port must be between 1 and 65535";
                    return result;
                }

                result.Port = port;
            }

            IPAddress address;
            if (!TryParseIPv4(host.Trim(), out address))
            {
                result.Port = 0;
                result.Problem = "Destination is not an IPv4 address";
                return result;
            }

            // Refused rather than enabled on the socket: the stream is for one device,
            // and the probe can only vouch for one. Without this the address would
            // start and then fail every send with a permissions error naming nothing.
            if (address.GetAddressBytes()[3] == 255)
            {
                result.Port = 0;
                result.Problem = "Destination is a broadcast address";
                return result;
            }

            result.Address = address;
            return result;
        }

        // Four dotted decimal octets, and nothing else. Do not swap this for
        // IPAddress.TryParse: that accepts the inet_aton shorthands, so "192.16" parses
        // as 192.0.0.16 and "3232235826" as 192.168.1.50, and a half-typed address
        // would be a valid address for a different, real host. Assembling the octets
        // here also stops a leading zero being read as octal.
        static bool TryParseIPv4(string text, out IPAddress address)
        {
            address = null;

            string[] parts = (text ?? "").Split('.');
            if (parts.Length != 4) return false;

            var octets = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                int octet;
                if (parts[i].Length == 0 || parts[i].Length > 3) return false;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out octet))
                    return false;
                if (octet > 255) return false;

                octets[i] = (byte)octet;
            }

            address = new IPAddress(octets);
            return true;
        }
    }
}
