using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The regression net: converts a folder of recorded flights and compares the frame
    /// logs, line for line, against the baseline kept beside them. Opt-in, because it
    /// takes minutes: run <c>validate-tlogs.ps1</c>, or set the environment variable
    /// yourself and filter on the Validation category.
    /// </summary>
    /// <remarks>
    /// The folder holds the tlogs and a <c>baseline</c> subfolder of frame logs with a
    /// <c>report.txt</c> naming the commit that produced them. A difference is either a
    /// regression or an intended change; for the latter, review the report and refresh
    /// the baseline. Nothing here classifies differences as acceptable.
    /// </remarks>
    [TestClass]
    public class Gdl90TlogValidationTests
    {
        /// <summary>Names the environment variable holding the validation folder.</summary>
        public const string RootVariable = "CBX_GDL90_VALIDATION";

        /// <summary>Names the environment variable that, set to 1, rewrites the baseline instead of comparing.</summary>
        public const string RefreshVariable = "CBX_GDL90_VALIDATION_REFRESH";

        const string BaselineFolder = "baseline";
        const int ExamplesPerKind = 3;
        const int ExampleWidth = 160;

        // Fixed so the header never differs between runs; a recording does not carry
        // the ownship identity, since an aircraft's receiver does not report the aircraft.
        static readonly Gdl90Configuration Config = new Gdl90Configuration
        {
            Identity = new Gdl90OwnshipIdentity { IcaoAddress = 0x7C1A2B, Callsign = "CXRPAS1" },
            MissionPlannerVersion = "validation",
            PluginVersion = "validation",
        };

        [TestMethod]
        [TestCategory("Validation")]
        public void Converter_ReproducesTheBaselineFrameLogs()
        {
            string root = Environment.GetEnvironmentVariable(RootVariable);
            if (string.IsNullOrWhiteSpace(root))
            {
                Assert.Inconclusive("Set " + RootVariable
                    + " to the folder holding the validation tlogs, or run validate-tlogs.ps1.");
            }

            Assert.IsTrue(Directory.Exists(root), "no such folder: " + root);
            var tlogs = Directory.GetFiles(root, "*.tlog").OrderBy(p => p, StringComparer.Ordinal).ToList();
            Assert.AreNotEqual(0, tlogs.Count, "no tlogs in " + root);

            bool refresh = Environment.GetEnvironmentVariable(RefreshVariable) == "1";
            string baseline = Path.Combine(root, BaselineFolder);
            string work = Path.Combine(Path.GetTempPath(), "CbxGdl90Validation_" + Path.GetRandomFileName());
            Directory.CreateDirectory(work);

            var report = new StringBuilder();
            bool failed = false;
            try
            {
                foreach (string tlog in tlogs)
                {
                    // Converted from a copy: the converter writes beside its input, and
                    // the tlogs live somewhere that should not see the output.
                    string copy = Path.Combine(work, Path.GetFileName(tlog));
                    File.Copy(tlog, copy);

                    var stopwatch = Stopwatch.StartNew();
                    var result = Quietly(() => Gdl90TlogConverter.Convert(copy, Config, overwrite: true));
                    stopwatch.Stop();

                    string name = Path.GetFileName(tlog);
                    if (!result.Succeeded)
                    {
                        failed = true;
                        report.AppendLine(name + ": conversion FAILED - " + result.Error);
                        continue;
                    }

                    report.AppendLine(string.Format("{0}: {1:N0} frames, {2:N0} traffic, {3} gaps, {4:F0} s",
                        name, result.Frames, result.TrafficFrames, result.Gaps,
                        stopwatch.Elapsed.TotalSeconds));

                    string expected = Path.Combine(baseline, Path.GetFileName(result.OutputPath));
                    if (refresh)
                    {
                        Directory.CreateDirectory(baseline);
                        File.Copy(result.OutputPath, expected, true);
                        report.AppendLine("  baseline written");
                        continue;
                    }

                    if (!File.Exists(expected))
                    {
                        failed = true;
                        report.AppendLine("  no baseline at " + expected + "; run with -Refresh");
                        continue;
                    }

                    var differences = Compare(File.ReadAllLines(expected), File.ReadAllLines(result.OutputPath));
                    if (differences.Count == 0)
                    {
                        report.AppendLine("  identical");
                    }
                    else
                    {
                        failed = true;
                        foreach (string line in differences) report.AppendLine("  " + line);
                    }
                }

                if (refresh) WriteBaselineReport(baseline, report.ToString());
            }
            finally
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch (IOException)
                {
                }
            }

            Console.WriteLine(report.ToString());
            Assert.IsFalse(failed, Environment.NewLine + report);
        }

        // CurrentState traces a line for every message it has no handler for, tens of
        // thousands per flight, and the test runner would print every one of them.
        static T Quietly<T>(Func<T> action)
        {
            var listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
            Trace.Listeners.Clear();
            try
            {
                return action();
            }
            finally
            {
                Trace.Listeners.AddRange(listeners);
            }
        }

        // ---- the comparison itself ----

        static string Row(int seq, string message = "ownship", int messageId = 10, string hex = "7e0a7e")
        {
            return "[\"" + message + "\",\"2026-09-02T13:04:11.0000000Z\"," + seq + ","
                + messageId + ",\"" + hex + "\"]";
        }

        const string Drop = "{\"record\":\"drop\",\"t\":\"2026-09-02T13:04:12.0000000Z\",\"icao\":\"7C4B1A\"}";

        [TestMethod]
        public void Compare_IdenticalLogs_ReportsNothing()
        {
            var log = new[] { Row(1), Drop, Row(2) };

            Assert.AreEqual(0, Compare(log, log).Count);
        }

        /// <summary>
        /// One extra record must read as one extra record. Compared by position it would
        /// shift every later line, and the one real difference would be lost among
        /// thousands.
        /// </summary>
        [TestMethod]
        public void Compare_ReportsAnInsertedRecordAsItselfAndNotEveryLineAfterIt()
        {
            var baseline = new[] { Row(1), Row(2), Row(3) };
            var produced = new[] { Row(1), Drop, Row(2), Row(3) };

            var report = Compare(baseline, produced);

            Assert.AreEqual(1, report.Count(l => l.StartsWith("1 drop record lines only produced")),
                string.Join(Environment.NewLine, report));
            Assert.IsFalse(report.Any(l => l.Contains("differ")), string.Join(Environment.NewLine, report));
        }

        [TestMethod]
        public void Compare_ReportsAChangedRowUnderItsMessage()
        {
            var baseline = new[] { Row(1), Row(2, "traffic", 20, "7e14aa7e") };
            var produced = new[] { Row(1), Row(2, "traffic", 20, "7e14bb7e") };

            var report = Compare(baseline, produced);

            Assert.AreEqual(1, report.Count(l => l.StartsWith("1 traffic lines differ")),
                string.Join(Environment.NewLine, report));
        }

        /// <summary>
        /// Lines that differ, grouped by the message or record they belong to, with a
        /// few examples of each. Rows are matched by frame number and records by kind
        /// and stamp, so one inserted or missing line is reported as itself rather than
        /// as every line after it. The converter's one wall-clock value is ignored.
        /// </summary>
        internal static List<string> Compare(string[] expected, string[] actual)
        {
            var before = Keyed(expected);
            var after = Keyed(actual);

            var counts = new Dictionary<string, int>();
            var examples = new Dictionary<string, List<string>>();

            foreach (var pair in before)
            {
                string produced;
                if (!after.TryGetValue(pair.Key, out produced))
                {
                    Note(counts, examples, KindOf(pair.Value) + " lines only in the baseline",
                        "    - " + Clip(pair.Value));
                }
                else if (produced != pair.Value)
                {
                    Note(counts, examples, KindOf(pair.Value) + " lines differ",
                        "    - " + Clip(pair.Value) + Environment.NewLine + "    + " + Clip(produced));
                }
            }

            foreach (var pair in after)
            {
                if (!before.ContainsKey(pair.Key))
                {
                    Note(counts, examples, KindOf(pair.Value) + " lines only produced",
                        "    + " + Clip(pair.Value));
                }
            }

            var lines = new List<string>();
            if (before.Count != after.Count)
                lines.Add(string.Format("{0:N0} lines in the baseline, {1:N0} produced", before.Count, after.Count));
            foreach (var pair in counts.OrderByDescending(p => p.Value))
            {
                lines.Add(string.Format("{0:N0} {1}", pair.Value, pair.Key));
                lines.AddRange(examples[pair.Key]);
            }
            return lines;
        }

        static void Note(Dictionary<string, int> counts, Dictionary<string, List<string>> examples,
            string kind, string example)
        {
            int count;
            counts.TryGetValue(kind, out count);
            counts[kind] = count + 1;

            List<string> shown;
            if (!examples.TryGetValue(kind, out shown)) examples[kind] = shown = new List<string>();
            if (shown.Count < ExamplesPerKind) shown.Add(example);
        }

        // Rows by frame number, which runs across the whole log; records by kind and
        // stamp, numbered within the stamp, since one second can hold several of a kind.
        static Dictionary<string, string> Keyed(string[] lines)
        {
            var keyed = new Dictionary<string, string>();
            var seen = new Dictionary<string, int>();

            foreach (string raw in lines)
            {
                if (raw.Length == 0 || raw.Contains("\"converted_utc\"")) continue;

                string line = Normalize(raw);
                string key = KeyOf(line);

                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                keyed[n == 0 ? key : key + " #" + n] = line;
            }

            return keyed;
        }

        static readonly Regex RowSeq = new Regex("^\\[\"[a-z_]+\",\"[^\"]*\",(\\d+),", RegexOptions.Compiled);
        static readonly Regex RecordStamp = new Regex("\"t\":\"([^\"]*)\"", RegexOptions.Compiled);

        static string KeyOf(string line)
        {
            var row = RowSeq.Match(line);
            if (row.Success) return "seq " + row.Groups[1].Value;

            var record = RecordKind.Match(line);
            if (!record.Success) return "unrecognised";

            var stamp = RecordStamp.Match(line);
            return record.Groups[1].Value + " @ " + (stamp.Success ? stamp.Groups[1].Value : "?");
        }

        // The header names the tlog it was converted from, which is the working copy:
        // a different path on every run and every machine.
        static readonly Regex TlogPath = new Regex("\"tlog\":\"[^\"]*\"", RegexOptions.Compiled);

        static string Normalize(string line)
        {
            return line.StartsWith("{\"record\":\"meta\"", StringComparison.Ordinal)
                ? TlogPath.Replace(line, "\"tlog\":\"...\"")
                : line;
        }

        static readonly Regex RowKind = new Regex("^\\[\"([a-z_]+)\"", RegexOptions.Compiled);
        static readonly Regex RecordKind = new Regex("\"record\":\"([a-z_]+)\"", RegexOptions.Compiled);

        static string KindOf(string line)
        {
            var row = RowKind.Match(line);
            if (row.Success) return row.Groups[1].Value;
            var record = RecordKind.Match(line);
            return record.Success ? record.Groups[1].Value + " record" : "unrecognised";
        }

        static string Clip(string line)
        {
            return line.Length <= ExampleWidth ? line : line.Substring(0, ExampleWidth) + "...";
        }

        // The commit is the one fact a reviewer needs to reproduce the baseline; the
        // rest is convenience.
        static void WriteBaselineReport(string baseline, string conversions)
        {
            var text = new StringBuilder();
            text.AppendLine("commit     " + GitHead());
            text.AppendLine("written    " + DateTime.UtcNow.ToString("u"));
            text.AppendLine("identity   7C1A2B CXRPAS1");
            text.AppendLine();
            text.Append(conversions);
            File.WriteAllText(Path.Combine(baseline, "report.txt"), text.ToString());
        }

        static string GitHead()
        {
            try
            {
                var git = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory = Path.GetDirectoryName(typeof(Gdl90TlogValidationTests).Assembly.Location),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(git))
                {
                    string head = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return process.ExitCode == 0 && head.Length > 0 ? head : "unknown";
                }
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
