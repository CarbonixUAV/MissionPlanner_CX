using System;
using System.IO;
using System.Reflection;
using log4net;
using MissionPlanner;
using Newtonsoft.Json.Linq;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents what a conversion produced.
    /// </summary>
    public sealed class Gdl90ConversionResult
    {
        /// <summary>Gets the frame log written, or null if nothing was.</summary>
        public string OutputPath { get; internal set; }

        /// <summary>Gets why nothing was written, or null on success.</summary>
        public string Error { get; internal set; }

        /// <summary>Gets a value indicating whether a frame log was written.</summary>
        public bool Succeeded
        {
            get { return Error == null; }
        }

        /// <summary>Gets the number of seconds of recorded time a frame set was emitted for.</summary>
        public long FrameSets { get; internal set; }

        /// <summary>Gets the number of frame rows written.</summary>
        public long Frames { get; internal set; }

        /// <summary>Gets the number of traffic report rows written, of those frames.</summary>
        public long TrafficFrames { get; internal set; }

        /// <summary>Gets the number of jumps in recorded time that were skipped rather than filled.</summary>
        public int Gaps { get; internal set; }

        /// <summary>Gets a value indicating whether the recording was of a simulator.</summary>
        public bool RecordedFromSitl { get; internal set; }
    }

    /// <summary>
    /// Reads a recorded tlog and writes the frame log the bridge would have written
    /// beside it, transmitting nothing.
    /// </summary>
    /// <remarks>
    /// Recorded time is the clock throughout, and the log's "source" record says
    /// <c>transmitted: false</c>.
    /// </remarks>
    public sealed class Gdl90TlogConverter
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Represents the forward jump in recorded time beyond which a gap is skipped
        /// rather than filled with frame sets.
        /// </summary>
        // Mission Planner only writes a tlog while connected, so a gap this long inside
        // one is a corrupt timestamp, not a dropout; filling it would emit millions of
        // frame sets of frozen telemetry. Shorter gaps are filled, because a real
        // dropout is where the staleness handling earns its keep and acceptance
        // should see it.
        public static readonly TimeSpan MaxFilledGap = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Represents how far recorded time may run backwards before the schedule
        /// resynchronizes rather than stalling.
        /// </summary>
        // Sub-second reordering is normal on a UDP link and is absorbed by the
        // schedule itself.
        public static readonly TimeSpan MaxRewind = TimeSpan.FromSeconds(5);

        readonly Gdl90VehicleReader _reader = new Gdl90VehicleReader();
        readonly Gdl90SourceDetector _detector = new Gdl90SourceDetector();
        readonly Gdl90FrameLog _frameLog;
        readonly Gdl90ConversionResult _result = new Gdl90ConversionResult();

        Gdl90TrafficTracker _traffic;
        Gdl90OwnshipIdentity _identity;
        DateTime _clockUtc;
        DateTime _nextEmitUtc;
        bool _scheduled;
        long _sequence;

        Gdl90TlogConverter()
        {
            _frameLog = new Gdl90FrameLog(() => _clockUtc);
        }

        /// <summary>Converts one tlog into the frame log beside it.</summary>
        /// <param name="tlogPath">The recording to read.</param>
        /// <param name="config">
        /// The identity to report under and the versions for the header. The identity
        /// is required: a recording does not carry it, since an aircraft's ADS-B
        /// receiver does not report the aircraft.
        /// </param>
        /// <param name="overwrite">
        /// true to replace an existing frame log. The default refuses, because the
        /// live bridge appends to the same path and converting a tlog that was flown
        /// with the bridge running would splice made-up frames onto a real transmission
        /// record.
        /// </param>
        /// <returns>What was written, or why nothing was.</returns>
        public static Gdl90ConversionResult Convert(string tlogPath, Gdl90Configuration config,
            bool overwrite = false)
        {
            return new Gdl90TlogConverter().Run(tlogPath, config, overwrite);
        }

        Gdl90ConversionResult Run(string tlogPath, Gdl90Configuration config, bool overwrite)
        {
            config = config ?? new Gdl90Configuration();
            _identity = config.Identity;

            if (string.IsNullOrWhiteSpace(tlogPath) || !File.Exists(tlogPath))
                return Failed("no such tlog: " + tlogPath);
            if (_identity == null || _identity.IcaoAddress == 0)
                return Failed("no transponder ICAO address to report the ownship as");

            string outputPath = Gdl90FrameLog.CompanionPath(tlogPath);
            if (File.Exists(outputPath))
            {
                if (!overwrite)
                    return Failed("a frame log already exists at " + outputPath);

                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception ex)
                {
                    return Failed("could not replace " + outputPath + ": " + ex.Message);
                }
            }

            try
            {
                using (var stream = File.Open(tlogPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var port = new MAVLinkInterface(stream))
                {
                    Read(port, stream, tlogPath, config);
                }
            }
            catch (Exception ex)
            {
                _log.Error("GDL90 tlog conversion failed: " + ex);
                return Failed(ex.Message);
            }
            finally
            {
                _frameLog.Dispose();
            }

            _result.OutputPath = File.Exists(outputPath) ? outputPath : null;
            return _result;
        }

        void Read(MAVLinkInterface port, Stream stream, string tlogPath, Gdl90Configuration config)
        {
            // Targets are stamped in recorded time, read off the interface rather than
            // from _clockUtc: the subscription fires inside readPacket, which takes a
            // message's timestamp before its body, so lastlogread is already this
            // target's while _clockUtc is still the previous packet's.
            _traffic = new Gdl90TrafficTracker(() => RecordedTime(port));
            _traffic.Bind(port);

            long length = stream.Length;
            while (stream.Position < length)
            {
                long before = stream.Position;
                var message = port.readPacket();

                if (message == null || message.Length == 0)
                {
                    // A reader that returns nothing and consumes nothing will do it
                    // again forever; anything else is just a frame it could not parse.
                    if (stream.Position == before) break;
                    continue;
                }

                if (port.lastlogread == DateTime.MinValue) continue;

                _clockUtc = RecordedTime(port);

                if (!_frameLog.IsOpen) Open(tlogPath, config);

                Advance(port);
            }

            RecordRecordedSource(port);
            _traffic.Unbind();
        }

        // Opened once recorded time is known, so the header is dated to the recording
        // rather than to the conversion.
        void Open(string tlogPath, Gdl90Configuration config)
        {
            if (!_frameLog.Open(tlogPath, config.MissionPlannerVersion, config.PluginVersion))
                throw new IOException("could not open " + Gdl90FrameLog.CompanionPath(tlogPath));

            _frameLog.WriteRecord("source", new JObject
            {
                ["kind"] = "tlog",

                // The one thing that separates this file from one the live bridge
                // wrote. It leads the file because it is the fact a reader must not
                // get halfway through without.
                ["transmitted"] = false,
                ["converted_utc"] = DateTime.UtcNow.ToString("o"),
            });
        }

        // At the end rather than beside the header: SIMSTATE accumulates on the
        // MAVState as the file is read, and asking after the first packet would call
        // almost every simulated flight live.
        void RecordRecordedSource(MAVLinkInterface port)
        {
            if (!_frameLog.IsOpen) return;

            _result.RecordedFromSitl = _detector.IsSimulated(port);
            _frameLog.WriteRecord("source", new JObject
            {
                ["kind"] = "tlog",
                ["transmitted"] = false,
                ["recorded"] = _result.RecordedFromSitl ? "sitl" : "live",
                ["signal"] = _result.RecordedFromSitl
                    ? Gdl90SourceDetector.SimstateSignal
                    : null,
            });
        }

        // Emits every whole second of recorded time the clock has reached.
        void Advance(MAVLinkInterface port)
        {
            if (!_scheduled)
            {
                _scheduled = true;
                _nextEmitUtc = Floor(_clockUtc);
            }

            if (_clockUtc - _nextEmitUtc > MaxFilledGap
                || _nextEmitUtc - _clockUtc > MaxRewind)
            {
                Resync();
            }

            while (_clockUtc >= _nextEmitUtc)
            {
                Emit(port, _nextEmitUtc);
                _nextEmitUtc += Interval;
            }
        }

        void Resync()
        {
            var from = _nextEmitUtc;
            _nextEmitUtc = Floor(_clockUtc);
            _result.Gaps++;

            _frameLog.WriteRecord("gap", new JObject
            {
                ["from"] = from.ToString("o"),
                ["to"] = _nextEmitUtc.ToString("o"),
                ["seconds"] = Math.Round((_nextEmitUtc - from).TotalSeconds, 3),
            });
        }

        void Emit(MAVLinkInterface port, DateTime nowUtc)
        {
            var mav = port.MAV;
            if (mav == null) return;

            var set = Gdl90FrameSet.Build(_reader.Read(mav, nowUtc), _identity, nowUtc,
                _traffic.Collect(nowUtc, _identity.IcaoAddress));
            _result.FrameSets++;

            foreach (var emission in set.Emissions)
            {
                if (!emission.IsFrame)
                {
                    _frameLog.WriteRecord(emission.Name, emission.Describe());
                    continue;
                }

                _frameLog.WriteFrame(++_sequence, emission.Name, emission.Message[0],
                    Gdl90Frame.Frame(emission.Message), emission.Describe());
                _result.Frames++;
                if (emission.Name == "traffic") _result.TrafficFrames++;
            }
        }

        // The reader hands the recording's clock back localised (MAVLinkInterface.cs:6532).
        // Every age in here is measured against it, and treating a local time as UTC
        // would offset the whole run by the machine's time zone.
        static DateTime RecordedTime(MAVLinkInterface port)
        {
            var stamp = port.lastlogread;
            return stamp == DateTime.MinValue ? stamp : stamp.ToUniversalTime();
        }

        static DateTime Floor(DateTime value)
        {
            return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond,
                value.Kind);
        }

        Gdl90ConversionResult Failed(string error)
        {
            _result.Error = error;
            return _result;
        }
    }
}
