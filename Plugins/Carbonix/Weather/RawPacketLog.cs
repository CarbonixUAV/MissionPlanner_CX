using log4net;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Carbonix.Weather
{
    /// <summary>
    /// Writes every datagram heard from the weather station to a debug log,
    /// one per line.
    /// </summary>
    public class RawPacketLog : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly string _folder;
        readonly object _lock = new object();
        StreamWriter _writer;
        bool _failed;

        public RawPacketLog(string folder)
        {
            _folder = folder;
        }

        /// <summary>Gets the path of the open file, or null before the first packet.</summary>
        public string Path { get; private set; }

        /// <summary>Appends one datagram, opening the file on the first call.</summary>
        public void Write(DateTime receivedUtc, string datagram)
        {
            lock (_lock)
            {
                if (_writer == null && !Open(receivedUtc))
                    return;
                try
                {
                    _writer.Write(receivedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    _writer.Write('\t');
                    _writer.WriteLine(datagram.Replace('\r', ' ').Replace('\n', ' '));
                }
                catch (Exception ex)
                {
                    log.Error("weather station raw log write failed, stopping the log", ex);
                    Close();
                    _failed = true;
                }
            }
        }

        bool Open(DateTime firstUtc)
        {
            if (_failed)
                return false;
            try
            {
                Directory.CreateDirectory(_folder);
                var stem = System.IO.Path.Combine(_folder, "tempest-" + firstUtc.ToString("yyyyMMdd-HHmmss"));
                var path = stem + ".log";
                for (int n = 1; File.Exists(path); n++)
                    path = stem + "-" + n + ".log";
                // No BOM: the first line must split on its tab like the rest
                _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
                {
                    AutoFlush = true,
                };
                Path = path;
                log.Info("weather station raw log: " + path);
                return true;
            }
            catch (Exception ex)
            {
                log.Error("weather station raw log could not be created in " + _folder, ex);
                _failed = true;
                return false;
            }
        }

        void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Close();
                // A datagram already in flight must not reopen the log
                _failed = true;
            }
        }
    }
}
