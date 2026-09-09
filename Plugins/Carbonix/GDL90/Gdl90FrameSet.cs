using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner;
using MissionPlanner.Utilities;
using Newtonsoft.Json.Linq;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents one item of a second's output: a message to transmit, or a record
    /// about one that was not built.
    /// </summary>
    public sealed class Gdl90Emission
    {
        Gdl90Emission(string name, byte[] message, Func<JObject> describe)
        {
            Name = name;
            Message = message;
            Describe = describe;
        }

        /// <summary>Gets the message or record name the frame log files this under.</summary>
        public string Name { get; }

        /// <summary>Gets the clear GDL 90 message, ID first, or null for a record.</summary>
        public byte[] Message { get; }

        /// <summary>Gets the function that describes the encoder's inputs for the frame log.</summary>
        public Func<JObject> Describe { get; }

        /// <summary>Gets a value indicating whether this emission goes on the wire.</summary>
        public bool IsFrame
        {
            get { return Message != null; }
        }

        /// <summary>Creates the emission for one message.</summary>
        /// <param name="message">The message, which supplies its own name, bytes and fields.</param>
        /// <returns>The emission.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
        public static Gdl90Emission Frame(IGdl90Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            return new Gdl90Emission(message.Name, message.Encode(),
                () => Gdl90LogShape.Describe(message));
        }

        /// <summary>Creates the emission for a record that is logged but not transmitted.</summary>
        /// <param name="name">The record name.</param>
        /// <param name="describe">The function that builds the record's fields.</param>
        /// <returns>The emission.</returns>
        public static Gdl90Emission Record(string name, Func<JObject> describe)
        {
            return new Gdl90Emission(name, null, describe);
        }
    }

    /// <summary>
    /// Represents one second of GDL 90 output: every message the bridge emits for an
    /// instant, in the order it emits them.
    /// </summary>
    public sealed class Gdl90FrameSet
    {
        // ForeFlight ID names, space-padded by the encoder to 8 and 16 bytes.
        const string DeviceName = "CxPlan";
        const string DeviceLongName = "Carbonix Planner";

        // The ForeFlight specification reserves all ones for "not available"; this bridge is
        // software on a laptop and has no hardware serial that we care about.
        const ulong DeviceSerialNumber = ulong.MaxValue;

        Gdl90FrameSet(List<Gdl90Emission> emissions)
        {
            Emissions = emissions;
        }

        /// <summary>Gets what to send and log, in order.</summary>
        public IReadOnlyList<Gdl90Emission> Emissions { get; }

        /// <summary>Builds the second.</summary>
        /// <param name="vehicle">What the aircraft was last observed doing.</param>
        /// <param name="identity">The transponder identity to report under.</param>
        /// <param name="nowUtc">The instant the set is dated to.</param>
        /// <param name="traffic">This second's view of every tracked target.</param>
        /// <returns>The set.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public static Gdl90FrameSet Build(Gdl90VehicleState vehicle, Gdl90OwnshipIdentity identity,
            DateTime nowUtc, IEnumerable<Gdl90TrafficResult> traffic)
        {
            if (traffic == null) throw new ArgumentNullException(nameof(traffic));

            var ownship = Gdl90Ownship.Build(vehicle, identity);

            var emissions = new List<Gdl90Emission>
            {
                // Heartbeat sent every second
                Gdl90Emission.Frame(new Gdl90Heartbeat
                {
                    GpsPositionValid = ownship.Report.PositionValid,
                    UtcTimingValid = true,
                    SecondsSinceUtcMidnight = SecondsSinceUtcMidnight(nowUtc),
                }),

                // Ownship output every second whether or not the fix is valid.
                Gdl90Emission.Frame(ownship.Report)
            };

            // Geometric altitude sent only when it is available.
            if (ownship.GeometricAltitudeValid)
            {
                emissions.Add(Gdl90Emission.Frame(new Gdl90GeometricAltitude
                {
                    GeoAltitudeFeet = ownship.GeometricAltitudeFeet,
                }));
            }

            // ForeFlight ID sent every second.
            // (could probably be slower, but this is simpler and matches other implementations)
            emissions.Add(Gdl90Emission.Frame(new Gdl90ForeFlightId
            {
                SerialNumber = DeviceSerialNumber,
                DeviceName = DeviceName,
                DeviceLongName = DeviceLongName,
                Capabilities = Gdl90ForeFlightId.Capability.GeoAltitudeIsMsl,
            }));

            // ICD 2.3: alert reports go out first, then the rest nearest the ownship
            // first. Drop records are not frames and follow them all.
            var proximate = new List<KeyValuePair<double, Gdl90Emission>>();
            var drops = new List<Gdl90Emission>();
            foreach (var target in traffic)
            {
                if (target.DropReason == null)
                {
                    var trafficReport = Gdl90Traffic.BuildReport(
                            target.Vehicle, target.Age, target.ThreatLevel);
                    if (trafficReport.TrafficAlert)
                    {
                        emissions.Add(Gdl90Emission.Frame(trafficReport));
                    }
                    else
                    {
                        proximate.Add(new KeyValuePair<double, Gdl90Emission>(
                            RangeMeters(ownship.Report, target.Vehicle),
                            Gdl90Emission.Frame(trafficReport)));
                    }
                }
                else if (target.DropIsNew)
                {
                    var dropped = target;
                    drops.Add(Gdl90Emission.Record("drop", () => Gdl90LogShape.Drop(dropped)));
                }
            }
            emissions.AddRange(proximate.OrderBy(p => p.Key).Select(p => p.Value));
            emissions.AddRange(drops);

            return new Gdl90FrameSet(emissions);
        }

        // Measured on the received position: proximity is coarse, and propagation moves
        // a target a few hundred meters at most. No range sorts after every range, in
        // arrival order.
        static double RangeMeters(Gdl90OwnshipReport ownship, MAVLink.mavlink_adsb_vehicle_t target)
        {
            bool coords = ((MAVLink.ADSB_FLAGS)target.flags & MAVLink.ADSB_FLAGS.VALID_COORDS) != 0;
            if (!ownship.PositionValid || !coords) return double.PositiveInfinity;

            return new PointLatLngAlt(ownship.LatitudeE7 / 1e7, ownship.LongitudeE7 / 1e7)
                .GetDistance(new PointLatLngAlt(target.lat / 1e7, target.lon / 1e7));
        }

        /// <summary>Calculates the heartbeat's time of day (ICD 3.1.3).</summary>
        /// <param name="utcNow">The instant.</param>
        /// <returns>Whole seconds elapsed since UTC midnight.</returns>
        public static int SecondsSinceUtcMidnight(DateTime utcNow)
        {
            return (int)utcNow.TimeOfDay.TotalSeconds;
        }
    }
}
