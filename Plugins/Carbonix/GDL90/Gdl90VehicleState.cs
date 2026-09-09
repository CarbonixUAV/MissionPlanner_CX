using System;
using System.Collections.Generic;
using MissionPlanner;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents the estimator's ground velocity in meters per second, north-east-down.
    /// </summary>
    public sealed class Gdl90GroundVector
    {
        /// <summary>Initializes a new instance of the <see cref="Gdl90GroundVector"/> class.</summary>
        /// <param name="northMetersPerSecond">The northward component, in meters per second.</param>
        /// <param name="eastMetersPerSecond">The eastward component, in meters per second.</param>
        /// <param name="downMetersPerSecond">The downward component, in meters per second.</param>
        public Gdl90GroundVector(double northMetersPerSecond, double eastMetersPerSecond,
            double downMetersPerSecond)
        {
            North = northMetersPerSecond;
            East = eastMetersPerSecond;
            Down = downMetersPerSecond;
        }

        /// <summary>Gets the northward component, in meters per second.</summary>
        public double North { get; }

        /// <summary>Gets the eastward component, in meters per second.</summary>
        public double East { get; }

        /// <summary>Gets the downward component, in meters per second; positive when descending.</summary>
        public double Down { get; }

        /// <summary>Gets the speed over the ground, in meters per second.</summary>
        public double HorizontalSpeed
        {
            get { return Math.Sqrt(North * North + East * East); }
        }
    }

    /// <summary>
    /// Represents a position in degrees times 1e7, as MAVLink carries it.
    /// </summary>
    public sealed class Gdl90Position
    {
        /// <summary>Initializes a new instance of the <see cref="Gdl90Position"/> class.</summary>
        /// <param name="latitudeE7">The latitude, in degrees times 1e7.</param>
        /// <param name="longitudeE7">The longitude, in degrees times 1e7.</param>
        public Gdl90Position(long latitudeE7, long longitudeE7)
        {
            LatitudeE7 = latitudeE7;
            LongitudeE7 = longitudeE7;
        }

        /// <summary>Gets the latitude, in degrees times 1e7.</summary>
        public long LatitudeE7 { get; }

        /// <summary>Gets the longitude, in degrees times 1e7.</summary>
        public long LongitudeE7 { get; }

        /// <summary>Creates a position from a message's coordinate pair.</summary>
        /// <param name="latitudeE7">The latitude, in degrees times 1e7.</param>
        /// <param name="longitudeE7">The longitude, in degrees times 1e7.</param>
        /// <returns>The position, or null if the pair is a "no position" sentinel.</returns>
        public static Gdl90Position From(int latitudeE7, int longitudeE7)
        {
            // ArduPilot sends exactly 0,0 when the estimator has no position. Other
            // messages use int.MaxValue for the same thing.
            if (latitudeE7 == 0 && longitudeE7 == 0) return null;
            if (latitudeE7 == int.MaxValue || longitudeE7 == int.MaxValue) return null;

            return new Gdl90Position(latitudeE7, longitudeE7);
        }
    }

    /// <summary>
    /// Represents one GPS receiver's last reported fix type and horizontal accuracy.
    /// </summary>
    public sealed class Gdl90GpsSample
    {
        /// <summary>Initializes a new instance of the <see cref="Gdl90GpsSample"/> class.</summary>
        /// <param name="fixType">The receiver's MAV_GPS_FIX_TYPE.</param>
        /// <param name="horizontalAccuracyMeters">The claimed horizontal accuracy in meters, or NaN when there is none.</param>
        public Gdl90GpsSample(int fixType, double horizontalAccuracyMeters)
        {
            FixType = fixType;
            HorizontalAccuracyMeters = horizontalAccuracyMeters;
        }

        /// <summary>Gets the MAV_GPS_FIX_TYPE the receiver last reported.</summary>
        public int FixType { get; }

        /// <summary>
        /// Gets the claimed horizontal accuracy in meters, or NaN when the receiver's
        /// message is not current or carries none.
        /// </summary>
        public double HorizontalAccuracyMeters { get; }

        /// <summary>Gets the sample for a receiver nothing has been heard from.</summary>
        public static Gdl90GpsSample Absent { get; } = new Gdl90GpsSample(0, double.NaN);
    }

    /// <summary>
    /// Represents what the aircraft was last observed doing, as read off one link.
    /// </summary>
    /// <remarks>
    /// A value is present only while the message that carries it is current; otherwise
    /// it is null or NaN. The landed state is the exception: once received, it is held
    /// for the life of the connection.
    /// </remarks>
    public sealed class Gdl90VehicleState
    {
        /// <summary>Gets or sets the position, or null when there is none.</summary>
        public Gdl90Position Position { get; set; }

        /// <summary>Gets or sets the altitude above mean sea level in meters, or NaN when there is none.</summary>
        public double AltitudeMslMeters { get; set; } = double.NaN;

        /// <summary>Gets or sets the static pressure in hPa, or NaN when there is none.</summary>
        public double StaticPressureHpa { get; set; } = double.NaN;

        /// <summary>Gets or sets the estimator's ground velocity, or null when there is none.</summary>
        public Gdl90GroundVector GroundVector { get; set; }

        /// <summary>Gets or sets the yaw as a bearing from 0 to 360 degrees true, or NaN when there is none.</summary>
        public double YawDegrees { get; set; } = double.NaN;

        /// <summary>Gets or sets the primary receiver's last sample.</summary>
        public Gdl90GpsSample Gps { get; set; } = Gdl90GpsSample.Absent;

        /// <summary>Gets or sets the secondary receiver's last sample.</summary>
        public Gdl90GpsSample Gps2 { get; set; } = Gdl90GpsSample.Absent;

        /// <summary>Gets or sets the last landed state received on this connection, or null when none has been.</summary>
        public MAVLink.MAV_LANDED_STATE? LandedState { get; set; }

        /// <summary>Gets or sets the names of the messages the ownship report needs that are not current.</summary>
        public IReadOnlyList<string> MissingMessages { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the quantity the ownship report's track field carries:
        /// <see cref="Gdl90TrackType.TrueTrack"/> or <see cref="Gdl90TrackType.TrueHeading"/>.
        /// </summary>
        public Gdl90TrackType TrackSource { get; set; } = Gdl90TrackType.TrueTrack;
    }
}
