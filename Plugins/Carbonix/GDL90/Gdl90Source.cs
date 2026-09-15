using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using log4net;
using MissionPlanner;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Specifies what is behind the telemetry on a link.
    /// </summary>
    public enum Gdl90SourceKind
    {
        /// <summary>An aircraft, as far as anything can tell.</summary>
        Live,

        /// <summary>A simulator.</summary>
        Sitl,

        /// <summary>A tlog being replayed.</summary>
        Playback,

        /// <summary>Too early in the connection to rule out a simulator.</summary>
        // Declared last so that default stays Live.
        Unknown,
    }

    /// <summary>
    /// Classifies what a link is carrying: an aircraft, a simulator or a replay.
    /// </summary>
    /// <remarks>
    /// A simulator is recognized by SIMSTATE, which stays in the link's packet list for
    /// the life of the connection, so the verdict is sticky without being held here.
    /// </remarks>
    public sealed class Gdl90SourceDetector
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Represents the evidence recorded in a frame log marked as simulated.</summary>
        public const string SimstateSignal = "SIMSTATE received";

        /// <summary>
        /// Represents how long a link must have been open before the absence of
        /// simulator evidence counts as evidence of an aircraft.
        /// </summary>
        // A speed bump against Start pressed in the same movement as Connect, not a
        // promise that SIMSTATE has arrived. If the tab ever reads Idle and only then
        // flips to SITL detected, raise this.
        public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

        readonly ConditionalWeakTable<MAVLinkInterface, LinkState> _links =
            new ConditionalWeakTable<MAVLinkInterface, LinkState>();

        /// <summary>Replaceable so the settling period can be crossed without waiting it out.</summary>
        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

        sealed class LinkState
        {
            /// <summary>When the current open period began, or default while closed.</summary>
            public DateTime OpenedUtc;
        }

        /// <summary>Classifies a link.</summary>
        /// <param name="port">The link, or null for no link.</param>
        /// <returns>
        /// <see cref="Gdl90SourceKind.Playback"/> for a replay, even of a simulator;
        /// <see cref="Gdl90SourceKind.Sitl"/> once SIMSTATE has arrived;
        /// <see cref="Gdl90SourceKind.Unknown"/> until the link has been open for
        /// <see cref="SettleTime"/>; otherwise <see cref="Gdl90SourceKind.Live"/>.
        /// </returns>
        public Gdl90SourceKind Classify(MAVLinkInterface port)
        {
            if (port == null) return Gdl90SourceKind.Live;

            // Mission Planner's own flag for this, and the one it uses to refuse to
            // write a tlog while replaying one. Not logplaybackfile, which stays
            // non-null after a replay ends.
            if (port.logreadmode) return Gdl90SourceKind.Playback;
            if (IsSimulated(port)) return Gdl90SourceKind.Sitl;

            return Settled(port) ? Gdl90SourceKind.Live : Gdl90SourceKind.Unknown;
        }

        /// <summary>Determines whether a link has ever carried a simulator's SIMSTATE.</summary>
        /// <param name="port">The link, or null for no link.</param>
        /// <returns>true if the link is open, or a replay, and has received SIMSTATE; otherwise, false.</returns>
        public bool IsSimulated(MAVLinkInterface port)
        {
            if (port == null) return false;

            // Never read a closed link. MAVLinkInterface.Close leaves the packet list
            // populated and Open is what clears it, so the packets still there belong
            // to the connection that just ended, and the next connection would inherit
            // a simulator it never was. A replay is exempt: its packets come from the
            // file, and it has no stream to be open.
            if (!port.logreadmode && !IsOpen(port)) return false;

            return HasSimstate(port);
        }

        // Unreadable counts as open: a verdict must not be discarded on the strength
        // of an exception.
        static bool IsOpen(MAVLinkInterface port)
        {
            try
            {
                return port.BaseStream?.IsOpen == true;
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 could not read the link's state: " + ex.Message);
                return true;
            }
        }

        // Live is an absence of evidence, and none of the evidence exists at the
        // instant a link opens.
        bool Settled(MAVLinkInterface port)
        {
            var link = _links.GetValue(port, _ => new LinkState());

            if (!IsOpen(port))
            {
                link.OpenedUtc = default(DateTime);
                return true;
            }

            if (link.OpenedUtc == default(DateTime)) link.OpenedUtc = UtcNow();

            return UtcNow() - link.OpenedUtc >= SettleTime;
        }

        // SIMSTATE is ArduPilot's simulator state, and no autopilot emits it off a
        // simulated build.
        static bool HasSimstate(MAVLinkInterface port)
        {
            try
            {
                return port.MAV?.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.SIMSTATE) != null;
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 could not read SIMSTATE: " + ex.Message);
                return false;
            }
        }
    }
}
