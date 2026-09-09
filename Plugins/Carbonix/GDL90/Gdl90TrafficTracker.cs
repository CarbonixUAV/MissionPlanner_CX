using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using MissionPlanner;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Provides the reasons an ADS-B target is not forwarded.
    /// </summary>
    public static class Gdl90TrafficDropReason
    {
        /// <summary>The target carries the ownship's own address.</summary>
        public const string Ownship = "ownship";

        /// <summary>Nothing has been heard of the target for <see cref="Gdl90TrafficTracker.DropAfter"/>.</summary>
        public const string Stale = "stale";

        /// <summary>The address is zero, or wider than 24 bits.</summary>
        public const string InvalidAddress = "invalid_address";
    }

    /// <summary>
    /// Represents one tracked target at one instant: what the receiver last said about
    /// it, how old that is, and whether it is forwarded.
    /// </summary>
    public sealed class Gdl90TrafficResult
    {
        /// <summary>Gets or sets the target as the receiver last described it.</summary>
        public MAVLink.mavlink_adsb_vehicle_t Vehicle { get; set; }

        /// <summary>
        /// Gets or sets the age of the target's data: the receiver's own
        /// <c>tslc</c> plus the time since its message arrived.
        /// </summary>
        public TimeSpan Age { get; set; }

        /// <summary>Gets or sets the aircraft's last collision assessment of the target, or NONE.</summary>
        public MAVLink.MAV_COLLISION_THREAT_LEVEL ThreatLevel { get; set; }

        /// <summary>Gets or sets why the target is not forwarded, or null when it is.</summary>
        public string DropReason { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is the first second the target
        /// has been dropped for <see cref="DropReason"/>.
        /// </summary>
        public bool DropIsNew { get; set; }
    }

    /// <summary>
    /// Accumulates the ADS-B targets the aircraft's receiver reports, and decides once
    /// a second which of them are forwarded.
    /// </summary>
    /// <remarks>
    /// Targets arrive on the MAVLink receive thread and are collected on the transmit
    /// loop; the table is locked.
    /// </remarks>
    public sealed class Gdl90TrafficTracker
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Represents how old a target's data may be before it is dropped instead of extrapolated and reported.</summary>
        public static readonly TimeSpan DropAfter = TimeSpan.FromSeconds(5);

        sealed class Entry
        {
            public MAVLink.mavlink_adsb_vehicle_t Vehicle;
            public DateTime ReceivedUtc;
            public MAVLink.MAV_COLLISION_THREAT_LEVEL Threat;

            /// <summary>The reason last reported for this target, so it is not repeatedly logged.</summary>
            public string LoggedDropReason;
        }

        readonly object _lock = new object();
        readonly Dictionary<uint, Entry> _targets = new Dictionary<uint, Entry>();
        readonly Func<DateTime> _receivedUtc;

        MAVLinkInterface _port;
        int? _adsbSubscription;
        int? _collisionSubscription;

        /// <summary>Initializes a new instance of the <see cref="Gdl90TrafficTracker"/> class.</summary>
        /// <param name="receivedUtc">
        /// The clock a subscribed message is stamped with on arrival: the wall clock
        /// live, the recording's own clock during a conversion.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="receivedUtc"/> is null.</exception>
        public Gdl90TrafficTracker(Func<DateTime> receivedUtc)
        {
            _receivedUtc = receivedUtc ?? throw new ArgumentNullException(nameof(receivedUtc));
        }

        /// <summary>Gets the number of targets held, forwarded or not.</summary>
        public int Count
        {
            get { lock (_lock) return _targets.Count; }
        }

        /// <summary>Subscribes to a link's ADSB_VEHICLE and COLLISION messages, releasing any earlier link first.</summary>
        /// <param name="port">The link to subscribe to, or null to subscribe to nothing.</param>
        /// <remarks>Targets already held are kept: every link carries the same aircraft's messages.</remarks>
        public void Bind(MAVLinkInterface port)
        {
            Unbind();
            if (port == null) return;

            _port = port;

            // Subscribed to every system and filtered in the handler: sysidcurrent may
            // not be set yet when the plugin loads, and a specific subscription would
            // then receive nothing.
            _adsbSubscription = port.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.ADSB_VEHICLE, OnAdsbVehicle, 0, 0);
            _collisionSubscription = port.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.COLLISION, OnCollision, 0, 0);
        }

        /// <summary>Releases the subscriptions taken by <see cref="Bind"/>.</summary>
        public void Unbind()
        {
            var port = _port;
            if (port == null) return;

            if (_adsbSubscription.HasValue) port.UnSubscribeToPacketType(_adsbSubscription.Value);
            if (_collisionSubscription.HasValue) port.UnSubscribeToPacketType(_collisionSubscription.Value);
            _adsbSubscription = null;
            _collisionSubscription = null;
            _port = null;
        }

        /// <summary>Records the receiver's latest word on one target.</summary>
        /// <param name="vehicle">The target as received.</param>
        /// <param name="receivedUtc">When the message arrived.</param>
        public void Accept(MAVLink.mavlink_adsb_vehicle_t vehicle, DateTime receivedUtc)
        {
            lock (_lock)
            {
                Entry entry;
                if (!_targets.TryGetValue(vehicle.ICAO_address, out entry))
                {
                    entry = new Entry();
                    _targets[vehicle.ICAO_address] = entry;
                }

                entry.Vehicle = vehicle;
                entry.ReceivedUtc = receivedUtc;
            }
        }

        /// <summary>Records the aircraft's collision assessment of a target it has already reported.</summary>
        /// <param name="icaoAddress">The target's ICAO address.</param>
        /// <param name="threat">The assessment.</param>
        /// <remarks>An assessment of a target never heard is discarded.</remarks>
        // No expiry of its own: ArduPilot clears one with NONE, and otherwise it lives
        // as long as the target. Holding an alert too long is the safe direction.
        public void AcceptCollision(uint icaoAddress, MAVLink.MAV_COLLISION_THREAT_LEVEL threat)
        {
            lock (_lock)
            {
                Entry entry;
                if (_targets.TryGetValue(icaoAddress, out entry)) entry.Threat = threat;
            }
        }

        /// <summary>Takes one second's view of every target, forgetting those that have stopped arriving.</summary>
        /// <param name="nowUtc">The instant the view is for.</param>
        /// <param name="ownshipIcao">The ownship's own address, which is not forwarded as traffic; zero matches nothing.</param>
        /// <returns>One result per target held at the start of the call.</returns>
        public List<Gdl90TrafficResult> Collect(DateTime nowUtc, int ownshipIcao)
        {
            var results = new List<Gdl90TrafficResult>();

            lock (_lock)
            {
                List<uint> forget = null;

                foreach (var pair in _targets)
                {
                    var entry = pair.Value;
                    var age = EffectiveAge(entry.Vehicle, entry.ReceivedUtc, nowUtc);
                    string reason = ClassifyDrop(entry.Vehicle, age, ownshipIcao);

                    var result = new Gdl90TrafficResult
                    {
                        Vehicle = entry.Vehicle,
                        Age = age,
                        DropReason = reason,
                        ThreatLevel = entry.Threat,
                    };

                    if (reason == null)
                    {
                        entry.LoggedDropReason = null;
                    }
                    else
                    {
                        result.DropIsNew = entry.LoggedDropReason != reason;
                        entry.LoggedDropReason = reason;
                    }

                    // Forgotten on our own silence, not on the effective age: a target
                    // whose receiver keeps saying it heard it long ago is still arriving,
                    // and forgetting it would re-add it on the next message and report
                    // the same drop as new every second.
                    if (Silent(entry, nowUtc))
                        (forget ?? (forget = new List<uint>())).Add(pair.Key);

                    results.Add(result);
                }

                if (forget != null)
                {
                    foreach (uint key in forget) _targets.Remove(key);
                }
            }

            return results;
        }

        /// <summary>Forgets every target that has stopped arriving, without taking a view of the rest.</summary>
        /// <param name="nowUtc">The instant to measure silence against.</param>
        /// <remarks>
        /// Targets arrive whether or not anything is transmitting, and only
        /// <see cref="Collect"/> forgets them. This is for the seconds nothing collects.
        /// </remarks>
        public void Prune(DateTime nowUtc)
        {
            lock (_lock)
            {
                List<uint> forget = null;

                foreach (var pair in _targets)
                {
                    if (Silent(pair.Value, nowUtc))
                        (forget ?? (forget = new List<uint>())).Add(pair.Key);
                }

                if (forget != null)
                {
                    foreach (uint key in forget) _targets.Remove(key);
                }
            }
        }

        static bool Silent(Entry entry, DateTime nowUtc)
        {
            return nowUtc - entry.ReceivedUtc > DropAfter;
        }

        /// <summary>Calculates how old a target's data is.</summary>
        /// <param name="vehicle">The target as received.</param>
        /// <param name="receivedUtc">When its message arrived.</param>
        /// <param name="nowUtc">The instant to measure against.</param>
        /// <returns>The receiver's own <c>tslc</c> plus the time since the message arrived.</returns>
        public static TimeSpan EffectiveAge(MAVLink.mavlink_adsb_vehicle_t vehicle,
            DateTime receivedUtc, DateTime nowUtc)
        {
            var transport = nowUtc - receivedUtc;
            if (transport < TimeSpan.Zero) transport = TimeSpan.Zero;

            return TimeSpan.FromSeconds(vehicle.tslc) + transport;
        }

        /// <summary>Determines why a target must not be forwarded.</summary>
        /// <param name="vehicle">The target as received.</param>
        /// <param name="age">How old its data is.</param>
        /// <param name="ownshipIcao">The ownship's own address; zero matches nothing.</param>
        /// <returns>A <see cref="Gdl90TrafficDropReason"/>, or null if the target is forwarded.</returns>
        public static string ClassifyDrop(MAVLink.mavlink_adsb_vehicle_t vehicle,
            TimeSpan age, int ownshipIcao)
        {
            uint address = vehicle.ICAO_address;
            if (address == 0 || address > Gdl90OwnshipIdentity.Icao24BitMax)
                return Gdl90TrafficDropReason.InvalidAddress;

            if (ownshipIcao > 0 && address == (uint)(ownshipIcao & Gdl90OwnshipIdentity.Icao24BitMax))
                return Gdl90TrafficDropReason.Ownship;

            if (age > DropAfter)
                return Gdl90TrafficDropReason.Stale;

            return null;
        }

        internal bool OnAdsbVehicle(MAVLink.MAVLinkMessage message)
        {
            if (!FromSelectedVehicle(message)) return true;

            try
            {
                Accept((MAVLink.mavlink_adsb_vehicle_t)message.data, _receivedUtc());
            }
            catch (Exception ex)
            {
                _log.Warn("GDL90 could not read an ADSB_VEHICLE: " + ex.Message);
            }

            return true;
        }

        internal bool OnCollision(MAVLink.MAVLinkMessage message)
        {
            if (!FromSelectedVehicle(message)) return true;

            try
            {
                var collision = (MAVLink.mavlink_collision_t)message.data;
                // COLLISION.id is only an ICAO address when src says ADSB
                if ((MAVLink.MAV_COLLISION_SRC)collision.src != MAVLink.MAV_COLLISION_SRC.ADSB)
                    return true;

                AcceptCollision(collision.id, (MAVLink.MAV_COLLISION_THREAT_LEVEL)collision.threat_level);
            }
            catch (Exception ex)
            {
                _log.Warn("GDL90 could not read a COLLISION: " + ex.Message);
            }

            return true;
        }

        bool FromSelectedVehicle(MAVLink.MAVLinkMessage message)
        {
            var port = _port;
            return port != null
                && message.sysid == port.sysidcurrent
                && message.compid == port.compidcurrent;
        }
    }
}
