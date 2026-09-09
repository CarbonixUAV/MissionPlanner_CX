using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents what one probe found at one address.
    /// </summary>
    public sealed class Gdl90ProbeResult
    {
        /// <summary>Gets or sets a value indicating whether something at the address replied.</summary>
        public bool Answered { get; set; }

        /// <summary>Gets or sets the name the device gave, or null if it gave none.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets a value indicating whether the name query was sent.</summary>
        public bool NameQueried { get; set; }
    }

    /// <summary>
    /// Provides a probe that asks one address whether anything is there and, for an
    /// Apple device, what it calls itself.
    /// </summary>
    /// <remarks>
    /// Liveness is an ICMP ping. The name is a unicast DNS-SD query to port 5353,
    /// which only Apple devices answer usefully.
    /// </remarks>
    public static class Gdl90DeviceProbe
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Represents the service iOS devices advertise.</summary>
        public const string CompanionLink = "_companion-link._tcp.local";

        /// <summary>Represents the multicast DNS port.</summary>
        public const int MdnsPort = 5353;

        // DNS PTR record (RFC 1035), which is what a service query is answered with.
        const int PtrType = 12;

        static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

        static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);

        /// <summary>Represents how often to probe: one small packet to one host.</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Represents how recently a device must have answered to still count as present: four
        /// probes' worth, so a couple of lost packets cannot blink the light.
        /// </summary>
        public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(Interval.TotalSeconds * 4);

        static readonly Random _ids = new Random();

        /// <summary>Asks an address whether it is there and, optionally, what it is called.</summary>
        /// <param name="address">The address to ask.</param>
        /// <param name="wantName">false once the caller has a name it is happy with.</param>
        /// <param name="ct">Cancels the name query.</param>
        /// <returns>What the address said.</returns>
        // Ping first: it is the cheap answer and the one that works on anything. The
        // more expensive name query only runs while a name is still wanted.
        public static async Task<Gdl90ProbeResult> ProbeAsync(IPAddress address,
            bool wantName = true, CancellationToken ct = default(CancellationToken))
        {
            if (address == null) return new Gdl90ProbeResult();

            bool alive = await PingAsync(address).ConfigureAwait(false);
            if (alive && !wantName) return new Gdl90ProbeResult { Answered = true };

            var named = await QueryAsync(address, ct).ConfigureAwait(false);
            named.NameQueried = true;
            if (named.Answered) return named;

            return new Gdl90ProbeResult { Answered = alive, NameQueried = true };
        }

        static async Task<Gdl90ProbeResult> QueryAsync(IPAddress address, CancellationToken ct)
        {
            ushort id;
            lock (_ids) id = (ushort)_ids.Next(1, ushort.MaxValue);

            byte[] query = BuildQuery(CompanionLink, id);

            try
            {
                using (var udp = new UdpClient(address.AddressFamily))
                {
                    await udp.SendAsync(query, query.Length, new IPEndPoint(address, MdnsPort))
                        .ConfigureAwait(false);

                    var receive = udp.ReceiveAsync();

                    var finished = await Task.WhenAny(receive, Task.Delay(QueryTimeout, ct))
                        .ConfigureAwait(false);

                    if (finished != receive) return new Gdl90ProbeResult();

                    var datagram = await receive.ConfigureAwait(false);

                    // Only the host we asked.
                    if (!datagram.RemoteEndPoint.Address.Equals(address))
                        return new Gdl90ProbeResult();

                    return ReadReply(datagram.Buffer, id);
                }
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 device query failed: " + ex.Message);
                return new Gdl90ProbeResult();
            }
        }

        static async Task<bool> PingAsync(IPAddress address)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping
                        .SendPingAsync(address, (int)PingTimeout.TotalMilliseconds)
                        .ConfigureAwait(false);

                    return reply != null && reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                _log.Debug("GDL90 device ping failed: " + ex.Message);
                return false;
            }
        }

        // A standard DNS question for one service name (RFC 1035 section 4.1).
        internal static byte[] BuildQuery(string service, ushort id)
        {
            var query = new List<byte>
            {
                (byte)(id >> 8), (byte)id,
                0, 0,               // standard query, no flags
                0, 1,               // one question
                0, 0, 0, 0, 0, 0,   // no answers, authorities or additional records
            };

            foreach (var label in (service ?? "").Split('.'))
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) continue;

                query.Add((byte)bytes.Length);
                query.AddRange(bytes);
            }

            query.Add(0);           // end of name
            query.Add(0);
            query.Add(PtrType);
            query.Add(0);
            query.Add(1);           // class IN

            return query.ToArray();
        }

        // Anything that replied to our question is alive; the rest only looks for a
        // name, and gives up quietly rather than throwing.
        internal static Gdl90ProbeResult ReadReply(byte[] reply, ushort expectedId)
        {
            var result = new Gdl90ProbeResult();

            if (reply == null || reply.Length < 12) return result;
            if (((reply[0] << 8) | reply[1]) != expectedId) return result;

            // QR bit: a response, not somebody else's question.
            if ((reply[2] & 0x80) == 0) return result;

            result.Answered = true;

            int questions = (reply[4] << 8) | reply[5];
            int answers = (reply[6] << 8) | reply[7];

            int offset = 12;
            for (int i = 0; i < questions; i++)
            {
                if (!SkipName(reply, ref offset)) return result;
                offset += 4;
            }

            for (int i = 0; i < answers; i++)
            {
                if (!SkipName(reply, ref offset)) return result;
                if (offset + 10 > reply.Length) return result;

                int type = (reply[offset] << 8) | reply[offset + 1];
                int rdLength = (reply[offset + 8] << 8) | reply[offset + 9];
                offset += 10;

                if (offset + rdLength > reply.Length) return result;

                if (type == PtrType)
                {
                    // The instance name is the first label of the PTR target and is
                    // always literal; only the suffix after it is ever a compression
                    // pointer, so no decompression is needed.
                    result.Name = FirstLabel(reply, offset, rdLength);
                    return result;
                }

                offset += rdLength;
            }

            return result;
        }

        // Steps over a name, whether it is spelled out or a compression pointer.
        static bool SkipName(byte[] data, ref int offset)
        {
            while (true)
            {
                if (offset >= data.Length) return false;

                int length = data[offset];
                if (length == 0)
                {
                    offset++;
                    return true;
                }

                // Top two bits set marks a compression pointer: two bytes, ends the name.
                if ((length & 0xC0) == 0xC0)
                {
                    offset += 2;
                    return offset <= data.Length;
                }

                offset += 1 + length;
            }
        }

        static string FirstLabel(byte[] data, int offset, int rdLength)
        {
            if (rdLength < 2) return null;

            int length = data[offset];
            if (length == 0 || (length & 0xC0) != 0) return null;
            if (length + 1 > rdLength || offset + 1 + length > data.Length) return null;

            return Encoding.UTF8.GetString(data, offset + 1, length);
        }
    }
}
