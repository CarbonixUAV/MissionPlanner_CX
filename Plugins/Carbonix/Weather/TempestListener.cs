using log4net;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Carbonix.Weather
{
    /// <summary>
    /// Receives the Tempest hub's UDP broadcasts and feeds them to a
    /// <see cref="WeatherStation"/>.
    /// </summary>
    public class TempestListener : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const int DefaultPort = 50222;

        readonly WeatherStation _station;
        readonly int _port;
        readonly RawPacketLog _rawLog;
        UdpClient _client;
        Thread _thread;
        volatile bool _running;

        /// <summary>Gets the reason the listener is not running, or null while listening.</summary>
        public string Error { get; private set; }

        public int Port => _port;

        /// <param name="rawLog">Optional debug log that every datagram is written to as it arrives.</param>
        public TempestListener(WeatherStation station, int port = DefaultPort, RawPacketLog rawLog = null)
        {
            _station = station;
            _port = port;
            _rawLog = rawLog;
        }

        public void Start()
        {
            if (_running)
                return;

            try
            {
                _client = new UdpClient();
                // Address reuse lets the Tempest app, or a second Mission
                // Planner, listen on the same port at the same time
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _client.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
                Error = null;
            }
            catch (Exception ex)
            {
                log.Error("weather station listener could not open UDP port " + _port, ex);
                Error = ex.Message;
                _client?.Dispose();
                _client = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop)
            {
                Name = "Tempest UDP listener",
                IsBackground = true,
            };
            _thread.Start();
        }

        void ReceiveLoop()
        {
            // Dispose nulls the field from another thread
            var client = _client;
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var lastError = SocketError.Success;
            while (_running)
            {
                byte[] data;
                try
                {
                    data = client.Receive(ref remote);
                    lastError = SocketError.Success;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    if (!_running)
                        return;
                    // Windows reports ICMP port-unreachable from unrelated
                    // traffic as ConnectionReset on a UDP socket
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                        continue;
                    // A persistent fault would otherwise log once a second
                    if (ex.SocketErrorCode != lastError)
                        log.Warn("weather station receive failed", ex);
                    lastError = ex.SocketErrorCode;
                    Thread.Sleep(1000);
                    continue;
                }
                catch (Exception ex)
                {
                    // Anything unexpected must not escape a background
                    // thread, which would take the whole process down
                    log.Error("weather station listener stopped", ex);
                    Error = ex.Message;
                    return;
                }

                try
                {
                    var text = Encoding.UTF8.GetString(data);
                    var now = DateTime.UtcNow;
                    _rawLog?.Write(now, text);
                    _station.Apply(text, now);
                }
                catch (Exception ex)
                {
                    log.Error("weather station message handling failed", ex);
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _client?.Close();
            _client = null;
        }
    }
}
