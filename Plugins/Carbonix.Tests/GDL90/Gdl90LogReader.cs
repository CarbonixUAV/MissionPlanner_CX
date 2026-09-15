using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// Reads a frame log
    /// </summary>
    internal class Gdl90LogReader
    {
        readonly List<JObject> _records = new List<JObject>();
        readonly List<JObject> _frames = new List<JObject>();

        Gdl90LogReader()
        {
        }

        public static Gdl90LogReader Read(string path)
        {
            var reader = new Gdl90LogReader();
            var columns = new Dictionary<string, List<string>>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0) continue;

                if (line[0] == '[')
                {
                    var row = JArray.Parse(line);
                    string message = (string)row[0];
                    List<string> cols;
                    if (!columns.TryGetValue(message, out cols))
                        throw new InvalidDataException("row before its cols record: " + message);

                    var frame = new JObject();
                    for (int i = 0; i < cols.Count; i++) frame[cols[i]] = row[i];
                    reader._frames.Add(frame);
                    continue;
                }

                var record = JObject.Parse(line);
                if ((string)record["record"] == "cols")
                {
                    columns[(string)record["msg"]] =
                        record["cols"].Select(c => (string)c).ToList();
                }
                reader._records.Add(record);
            }

            return reader;
        }

        /// <summary>Every object record, in file order.</summary>
        public IReadOnlyList<JObject> Records => _records;

        /// <summary>Every frame, flattened back into named fields.</summary>
        public IReadOnlyList<JObject> Frames => _frames;

        public JObject Meta => Single("meta");

        public JObject Single(string kind)
        {
            return _records.Single(r => (string)r["record"] == kind);
        }

        public IEnumerable<JObject> All(string kind)
        {
            return _records.Where(r => (string)r["record"] == kind);
        }

        public IEnumerable<JObject> FramesOf(string message)
        {
            return _frames.Where(f => (string)f["msg"] == message);
        }
    }
}
