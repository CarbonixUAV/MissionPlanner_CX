using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MissionPlanner.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Writes a JSON Lines record of every frame handed to the socket: the framed bytes
    /// beside the values that produced them, plus a record for every ADS-B target that
    /// was received and not forwarded.
    /// </summary>
    public sealed class Gdl90FrameLog : IDisposable
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Represents the extension a frame log carries in place of ".tlog".</summary>
        public const string Extension = ".gdl90.jsonl";

        /// <summary>Represents the record shape readers parse.</summary>
        /// <remarks>
        /// Readers zip rows against the "cols" header, so a field can be added, removed
        /// or reordered without a bump. Bump it when a field keeps its name and changes
        /// its meaning.
        /// </remarks>
        public const int SchemaVersion = 1;

        // Several seconds of a busy sky at one frame per target per second, so only a
        // writer that has genuinely stopped can fill it.
        const int QueueLimit = 4096;

        static readonly string[] FixedColumns = { "msg", "t", "seq", "msg_id", "hex" };

        readonly object _lifecycle = new object();

        // Field order per message type, as already declared to this file. Written only
        // from the thread that calls WriteFrame.
        readonly Dictionary<string, List<string>> _columns = new Dictionary<string, List<string>>();

        readonly Func<DateTime> _clock;

        BlockingCollection<JToken> _queue;
        Task _writer;
        string _path;
        long _frames;
        long _discarded;

        // Replaceable by tests, which need a file that fails.
        internal Func<string, TextWriter> OpenWriter = OpenFile;

        /// <summary>Initializes a new instance of the <see cref="Gdl90FrameLog"/> class.</summary>
        /// <param name="clock">The clock every record's "t" is read from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        public Gdl90FrameLog(Func<DateTime> clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Gets a value indicating whether records are being accepted.</summary>
        public bool IsOpen
        {
            get { return _queue != null; }
        }

        /// <summary>Gets the path of the open log file, or null.</summary>
        public string Path
        {
            get { return _path; }
        }

        /// <summary>Gets the number of frame rows written to this file.</summary>
        public long Frames
        {
            get { return Interlocked.Read(ref _frames); }
        }

        /// <summary>Opens a log beside a tlog, closing any log already open.</summary>
        /// <param name="tlogPath">The tlog to sit beside.</param>
        /// <param name="missionPlannerVersion">Mission Planner's version, for the header.</param>
        /// <param name="pluginVersion">The plugin's version, for the header.</param>
        /// <returns>true if the file was opened; otherwise, false.</returns>
        public bool Open(string tlogPath, string missionPlannerVersion, string pluginVersion)
        {
            if (string.IsNullOrEmpty(tlogPath)) return false;

            lock (_lifecycle)
            {
                Close();

                string path = CompanionPath(tlogPath);
                TextWriter writer;
                try
                {
                    writer = OpenWriter(path);
                }
                catch (Exception ex)
                {
                    _log.Warn($"GDL90 frame log could not open {path}: {ex.Message}");
                    return false;
                }

                _path = path;
                _columns.Clear();
                Interlocked.Exchange(ref _frames, 0);
                Interlocked.Exchange(ref _discarded, 0);
                _queue = new BlockingCollection<JToken>(QueueLimit);

                var queue = _queue;
                _writer = Task.Factory.StartNew(() => Drain(queue, writer),
                    TaskCreationOptions.LongRunning);

                WriteRecord("meta", Meta(tlogPath, missionPlannerVersion, pluginVersion));
                return true;
            }
        }

        static TextWriter OpenFile(string path)
        {
            // FileShare.Delete matters: Mission Planner sorts finished logs by moving
            // every file whose name starts with the tlog's stem
            // (LogSort.MoveFileUsingMask), and without it the move throws and the bare
            // catch around the sort abandons the tlog and rlog too.
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return new StreamWriter(stream, new UTF8Encoding(false));
        }

        /// <summary>Stops accepting records and lets the writer finish what is queued, without waiting.</summary>
        public void Close()
        {
            lock (_lifecycle)
            {
                var queue = _queue;
                if (queue == null) return;

                WriteRecord("close", new JObject
                {
                    ["frames"] = Interlocked.Read(ref _frames),
                    ["discarded"] = Interlocked.Read(ref _discarded),
                });

                _queue = null;
                _path = null;

                try
                {
                    queue.CompleteAdding();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        // The writer's side of Close: a file that stopped taking records stops claiming
        // to be open, so the set drops it rather than counting frames into nothing.
        // Only while the log is still this queue's; a later Open may have replaced it.
        void Fail(BlockingCollection<JToken> queue)
        {
            lock (_lifecycle)
            {
                if (_queue != queue) return;

                _queue = null;
                _path = null;
            }
        }

        /// <summary>Closes the log and waits briefly for the writer to flush.</summary>
        public void Dispose()
        {
            Task writer;
            lock (_lifecycle)
            {
                Close();
                writer = _writer;
                _writer = null;
            }

            try
            {
                writer?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _log.Warn("GDL90 frame log did not flush cleanly: " + ex.Message);
            }
        }

        /// <summary>Records one frame handed to the socket.</summary>
        /// <param name="sequence">The frame's number within the session, the same in every log written for it.</param>
        /// <param name="message">The message name the row is filed under.</param>
        /// <param name="messageId">The GDL 90 message ID.</param>
        /// <param name="frame">The bytes handed to the socket, flags and FCS included.</param>
        /// <param name="values">The encoder's inputs, before quantization.</param>
        /// <remarks>Declares the message's columns first if this is its first row, or its fields have changed.</remarks>
        public void WriteFrame(long sequence, string message, byte messageId, byte[] frame,
            JObject values)
        {
            if (_queue == null) return;

            var columns = DeclareColumns(message, values);

            var row = new JArray { message, UtcNow(), sequence, (int)messageId, frame.ToHexString() };
            for (int i = FixedColumns.Length; i < columns.Count; i++)
            {
                JToken value;
                row.Add(values != null && values.TryGetValue(columns[i], out value)
                    ? value
                    : JValue.CreateNull());
            }

            Interlocked.Increment(ref _frames);
            Enqueue(row);
        }

        // Rows are built by looking each column up by name, so a caller that reorders
        // its fields cannot silently misalign a row.
        List<string> DeclareColumns(string message, JObject values)
        {
            var columns = new List<string>(FixedColumns);
            if (values != null)
            {
                foreach (var property in values.Properties()) columns.Add(property.Name);
            }

            List<string> declared;
            if (_columns.TryGetValue(message, out declared) && declared.SequenceEqual(columns))
                return declared;

            _columns[message] = columns;
            WriteRecord("cols", new JObject
            {
                ["msg"] = message,
                ["cols"] = new JArray(columns),
            });
            return columns;
        }

        /// <summary>Records one object record, stamped and queued without blocking.</summary>
        /// <param name="kind">The record name.</param>
        /// <param name="fields">The record's fields, or null for none.</param>
        public void WriteRecord(string kind, JObject fields)
        {
            if (_queue == null) return;

            var record = new JObject
            {
                ["record"] = kind,
                ["t"] = UtcNow(),
            };
            if (fields != null)
            {
                foreach (var property in fields.Properties())
                {
                    record[property.Name] = property.Value;
                }
            }

            Enqueue(record);
        }

        void Enqueue(JToken record)
        {
            var queue = _queue;
            if (queue == null) return;

            try
            {
                if (!queue.TryAdd(record)) Interlocked.Increment(ref _discarded);
            }
            catch (Exception ex)
            {
                // Includes the ObjectDisposedException from a queue completed on
                // another thread between the null check and the add.
                _log.Debug("GDL90 frame log dropped a record: " + ex.Message);
            }
        }

        string UtcNow()
        {
            return _clock().ToString("o", CultureInfo.InvariantCulture);
        }

        void Drain(BlockingCollection<JToken> queue, TextWriter writer)
        {
            try
            {
                foreach (var record in queue.GetConsumingEnumerable())
                {
                    writer.Write(record.ToString(Formatting.None));
                    writer.Write('\n');

                    // Flush whenever the queue has caught up, so the file is readable
                    // live and a crash costs at most the records still in flight.
                    if (queue.Count == 0) writer.Flush();
                }
            }
            catch (Exception ex)
            {
                _log.Warn("GDL90 frame log writer stopped: " + ex.Message);
                Fail(queue);
            }
            finally
            {
                try
                {
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Debug("GDL90 frame log did not close cleanly: " + ex.Message);
                }
                queue.Dispose();
            }
        }

        // Which fields the wire quantizes, and by how much, is recorded nowhere on
        // purpose: acceptance reads both off the ICD, and that independent reading is
        // what stands between an off-by-one-quantum encoder bug and a log that agrees
        // with it.
        static JObject Meta(string tlogPath, string missionPlannerVersion, string pluginVersion)
        {
            return new JObject
            {
                ["schema"] = SchemaVersion,
                ["mission_planner"] = missionPlannerVersion,
                ["plugin"] = pluginVersion,
                ["values"] = "encoder inputs, before GDL 90 field quantization; "
                    + "the hex is what was transmitted",
                ["tlog"] = tlogPath,
            };
        }

        /// <summary>Calculates the path of the frame log that sits beside a tlog.</summary>
        /// <param name="tlogPath">The tlog's path.</param>
        /// <returns>The same directory and name, with <see cref="Extension"/> in place of ".tlog".</returns>
        // Stripped by hand rather than with Path.ChangeExtension, which cuts at the
        // last dot and would eat part of an operator-supplied name containing one.
        public static string CompanionPath(string tlogPath)
        {
            if (tlogPath == null) return null;

            string stem = tlogPath.EndsWith(".tlog", StringComparison.OrdinalIgnoreCase)
                ? tlogPath.Substring(0, tlogPath.Length - ".tlog".Length)
                : tlogPath;
            return stem + Extension;
        }
    }

    /// <summary>
    /// Keeps one frame log open beside every open tlog, all carrying the same records.
    /// </summary>
    /// <remarks>
    /// Records that describe the session rather than a moment in it are marked sticky,
    /// and are replayed into every log opened afterwards.
    /// </remarks>
    // The aircraft flies over several links at once and each writes its own tlog, so
    // there is no single tlog for the stream to belong to. Writing beside all of them
    // makes every tlog self-describing, and each pair travels together when Mission
    // Planner sorts the logs.
    public sealed class Gdl90FrameLogSet : IDisposable
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly Dictionary<string, Gdl90FrameLog> _logs = new Dictionary<string, Gdl90FrameLog>();

        // Paths that failed to open, so a broken one is not retried every second.
        readonly HashSet<string> _failed = new HashSet<string>();

        // In the order first written, holding the latest of each kind.
        readonly List<KeyValuePair<string, JObject>> _sticky =
            new List<KeyValuePair<string, JObject>>();

        readonly Func<DateTime> _clock;

        bool _tlogNameWarned;

        /// <summary>Initializes a new instance of the <see cref="Gdl90FrameLogSet"/> class.</summary>
        /// <param name="clock">The clock every log it opens stamps records from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        public Gdl90FrameLogSet(Func<DateTime> clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Gets or sets Mission Planner's version, for the log headers.</summary>
        public string MissionPlannerVersion { get; set; }

        /// <summary>Gets or sets the plugin's version, for the log headers.</summary>
        public string PluginVersion { get; set; }

        // Handed to every log opened, so tests can give them a file that fails.
        internal Func<string, TextWriter> OpenWriter;

        /// <summary>Gets a value indicating whether at least one log is accepting records.</summary>
        public bool IsOpen
        {
            get { return _logs.Count > 0; }
        }

        /// <summary>Gets the number of open logs.</summary>
        public int Count
        {
            get { return _logs.Count; }
        }

        /// <summary>Gets the paths of the open logs.</summary>
        public IEnumerable<string> Paths
        {
            get { return _logs.Values.Select(l => l.Path).Where(p => p != null).ToList(); }
        }

        /// <summary>Opens a log beside every tlog that lacks one, and closes any whose tlog has closed.</summary>
        /// <param name="enabled">false closes every log.</param>
        /// <param name="tlogStreams">Every link's tlog stream, open or not.</param>
        public void Update(bool enabled, IEnumerable<Stream> tlogStreams)
        {
            try
            {
                var live = new Dictionary<string, Stream>();
                if (enabled && tlogStreams != null)
                {
                    foreach (var stream in tlogStreams)
                    {
                        string path = TlogNameOf(stream);
                        if (path != null && !live.ContainsKey(path)) live[path] = stream;
                    }
                }

                foreach (var pair in _logs.ToList())
                {
                    bool wanted = live.ContainsKey(pair.Key);
                    if (wanted && pair.Value.IsOpen) continue;

                    pair.Value.Close();
                    _logs.Remove(pair.Key);

                    // A log that closed itself had its file fail under it. Not reopened
                    // while that tlog lives: the next write would fail the same way.
                    if (wanted) _failed.Add(pair.Key);
                }

                // A path that stopped being live is worth retrying if it comes back:
                // the failure may have been the directory, not the file.
                _failed.IntersectWith(live.Keys);

                foreach (var pair in live)
                {
                    if (_logs.ContainsKey(pair.Key) || _failed.Contains(pair.Key)) continue;

                    var log = new Gdl90FrameLog(_clock);
                    if (OpenWriter != null) log.OpenWriter = OpenWriter;
                    if (log.Open(pair.Key, MissionPlannerVersion, PluginVersion))
                    {
                        foreach (var record in _sticky)
                            log.WriteRecord(record.Key, record.Value);
                        _logs[pair.Key] = log;
                    }
                    else
                    {
                        _failed.Add(pair.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("GDL90 frame log lifecycle error: " + ex.Message);
            }
        }

        /// <summary>Records one transmitted frame in every open log.</summary>
        /// <param name="sequence">The frame's number within the session.</param>
        /// <param name="message">The message name the row is filed under.</param>
        /// <param name="messageId">The GDL 90 message ID.</param>
        /// <param name="frame">The bytes handed to the socket.</param>
        /// <param name="describe">The function that builds the encoder's inputs, called once and only if a log is open.</param>
        public void WriteFrame(long sequence, string message, byte messageId, byte[] frame,
            Func<JObject> describe)
        {
            if (_logs.Count == 0) return;

            try
            {
                var values = describe();
                foreach (var log in _logs.Values)
                {
                    log.WriteFrame(sequence, message, messageId, frame, values);
                }
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 frame log could not record a frame: " + ex.Message);
            }
        }

        /// <summary>Records one object record in every open log.</summary>
        /// <param name="kind">The record name.</param>
        /// <param name="describe">The function that builds the record's fields.</param>
        /// <param name="sticky">
        /// true for a record that states the session's current state rather than an
        /// event, so a log opened later is given it too. Remembered even when no log is
        /// open yet.
        /// </param>
        public void WriteRecord(string kind, Func<JObject> describe, bool sticky = false)
        {
            if (_logs.Count == 0 && !sticky) return;

            try
            {
                var fields = describe();
                if (sticky) Remember(kind, fields);

                foreach (var log in _logs.Values)
                {
                    log.WriteRecord(kind, fields);
                }
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 frame log could not record a " + kind + ": " + ex.Message);
            }
        }

        void Remember(string kind, JObject fields)
        {
            var entry = new KeyValuePair<string, JObject>(kind, fields ?? new JObject());

            for (int i = 0; i < _sticky.Count; i++)
            {
                if (_sticky[i].Key != kind) continue;
                _sticky[i] = entry;
                return;
            }

            _sticky.Add(entry);
        }

        /// <summary>Closes every log and waits briefly for each to flush.</summary>
        public void Dispose()
        {
            foreach (var log in _logs.Values) log.Dispose();
            _logs.Clear();
        }

        /// <summary>Finds the path of the tlog behind a link's log stream.</summary>
        /// <param name="logfile">The link's log stream.</param>
        /// <returns>The tlog's path, or null if the link has none, it has closed, or its name cannot be reached.</returns>
        // Taken from the live stream rather than rebuilt from the clock and the log
        // directory: Mission Planner appends a "-1" style index when a name already
        // exists, so a reconstruction can silently name a different file. The stream
        // is a BufferedStream, which does not expose what it wraps, so the FileStream
        // is reached by reflection.
        public string TlogNameOf(Stream logfile)
        {
            if (!IsWritable(logfile)) return null;

            var file = UnderlyingFileStream(logfile, 4);
            if (file == null)
            {
                if (!_tlogNameWarned)
                {
                    _tlogNameWarned = true;
                    _log.Warn("GDL90 could not find the tlog behind " + logfile.GetType().Name
                        + "; no frame log will be written");
                }
                return null;
            }

            try
            {
                return file.Name;
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 could not read the tlog name: " + ex.Message);
                return null;
            }
        }

        static bool IsWritable(Stream stream)
        {
            if (stream == null) return false;

            try
            {
                return stream.CanWrite;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        static FieldInfo _wrappedStreamField;

        static FileStream UnderlyingFileStream(Stream stream, int depth)
        {
            var file = stream as FileStream;
            if (file != null) return file;
            if (depth <= 0) return null;

            var field = _wrappedStreamField;
            if (field == null || !field.DeclaringType.IsInstanceOfType(stream))
            {
                field = WrappedStreamField(stream.GetType());
                if (field == null) return null;
                _wrappedStreamField = field;
            }

            var inner = field.GetValue(stream) as Stream;
            return inner == null ? null : UnderlyingFileStream(inner, depth - 1);
        }

        static FieldInfo WrappedStreamField(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (typeof(Stream).IsAssignableFrom(field.FieldType)) return field;
                }
            }

            return null;
        }
    }
}
