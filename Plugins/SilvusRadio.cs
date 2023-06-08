using System;
using System.Collections.Generic;
using System.IdentityModel.Protocols.WSTrust;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.plugins
{
    public class SilvusRadio : MissionPlanner.Plugin.Plugin
    {
        enum silvus_state
        {
            silvus_wait_for_connection,
            silvus_connecting,
            silvus_check_for_message,
            silvus_error
        }

        silvus_state state = silvus_state.silvus_wait_for_connection;

        int silvus_connection_retries = 0;


        // private HttpClient client;
        // Socket winSocket;
        // EndPoint Remote;

        string SilvusIP = "192.168.0.7";
        string LocalIP  = "192.168.0.22";
        // string SilvusIP = "172.20.172.84";
        // string LocalIP = "172.20.172.81";
        int SilvusPort = 8000;

        // Create UDP client to read incoming packets
        UdpClient client; // = new UdpClient(SilvusPort);

        // Create IP endpoint 
        IPEndPoint RemoteIpEndPoint; // = new IPEndPoint(IPAddress.Parse(SilvusIP), SilvusPort);

        public override string Name
        {
            get { return "Silvus Radio"; }
        }

        public override string Version
        {
            get { return "0.10"; }
        }

        public override string Author
        {
            get { return "Carbonix"; }
        }

        

        public override bool Exit()
        {
            return true;
        }

        public override bool Init()
        {
            return true;
        }

        public override bool Loaded()
        {

            // Create UDP client to read incoming packets
            client = new UdpClient(SilvusPort);

            // Create IP endpoint 
            RemoteIpEndPoint = new IPEndPoint(IPAddress.Parse(SilvusIP), SilvusPort);

            loopratehz = 1f;
            return true;
        }


        public override bool Loop()
        {
            silvus_process();

            // silvus_testing();

            return true;
        }


        private void silvus_testing()
        {
            // Create UDP client to read incoming packets
            UdpClient client = new UdpClient(SilvusPort);

            // Create IP endpoint 
            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Parse(SilvusIP), SilvusPort);

            try
            {
                client.Connect(RemoteIpEndPoint);
                Byte[] receiveBytes = client.Receive(ref RemoteIpEndPoint);

                string returnData = Encoding.ASCII.GetString(receiveBytes);

                Console.WriteLine("This is the message you received " + returnData.ToString());
                //Console.WriteLine("This message was sent from " + RemoteIpEndPoint.Address.ToString() + " on their port number " + RemoteIpEndPoint.Port.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }


        private void silvus_process()
        {
            switch (state)
            {
                case silvus_state.silvus_wait_for_connection:
                    if (wait_for_ping()) 
                        state = silvus_state.silvus_connecting;
                    break;
                case silvus_state.silvus_connecting:
                    if (silvus_connect())
                    {
                        silvus_connection_retries = 0;
                        state = silvus_state.silvus_check_for_message;
                    }
                    else 
                    {
                        if (silvus_connection_retries++ > 3)
                        {
                            silvus_connection_retries = 0;
                            state = silvus_state.silvus_wait_for_connection;
                        }
                    }
                    break;
                case silvus_state.silvus_check_for_message:
                    if (silvus_fetch_message())
                    {
                        silvus_display_RSSI();
                    }
                    else
                    {
                        if (silvus_connection_retries++ > 10)
                        {
                            silvus_connection_retries = 0;
                            state = silvus_state.silvus_connecting;
                        }
                    }
                    break;
                case silvus_state.silvus_error: 
                    // TBD
                    break;
            }
        }

        private void silvus_display_RSSI()
        {
            // throw new NotImplementedException();
        }

        private bool silvus_fetch_message()
        {
            short data_length = 0;
            int rssi_send = 0;
            int noise_send = 0;
            float snr_send = 0;
            int msg5000 = 0;
            int msg5001 = 0;
            int noise = 0;
            int yval = 0;
            int xval = 0;
            int nodeidmsg = 0;

            // Create UDP client to read incoming packets
            // UdpClient client = new UdpClient(SilvusPort);

            // Create IP endpoint 
            // IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Parse(SilvusIP), SilvusPort);

            try
            {
                // client.Connect(RemoteIpEndPoint);
                Byte[] receiveBytes = client.Receive(ref RemoteIpEndPoint);

                for (int i = 0; i < receiveBytes.Length - 4; i+=2)
                {
                    short report_type = (short)(receiveBytes[i] << 0x08 | receiveBytes[i + 1]);
                    data_length = (short)(receiveBytes[i + 2] << 0x08 | receiveBytes[i + 3]);


                    char[] ch = new char[4];
                    byte one = 0, two = 0, thr = 0, fou = 0;
                    int val = 0;

                    switch (report_type)
                    {
                        case 5000:
                            one = receiveBytes[i + 4];
                            ch[0] = (char)one;
                            two = receiveBytes[i + 5];
                            ch[1] = (char)two;
                            thr = receiveBytes[i + 6];
                            ch[2] = (char)thr;
                            fou = receiveBytes[i + 7];
                            ch[3] = (char)fou;
                            val = int.Parse(new string(ch));
                            msg5000 = (int)val;
                            //msg5000 = (int)BitConverter.ToInt32(receiveBytes, i + 4);
                            break;
                        case 5001:
                            one = receiveBytes[i + 4];
                            ch[0] = (char)one;
                            two = receiveBytes[i + 5];
                            ch[1] = (char)two;
                            thr = receiveBytes[i + 6];
                            ch[2] = (char)thr;
                            fou = receiveBytes[i + 7];
                            ch[3] = (char)fou;
                            val = int.Parse(new string(ch));
                            msg5001 = (int)val;
                            //msg5001 = (int)BitConverter.ToInt32(receiveBytes, i + 4);
                            break;
                        case 5004:
                            noise = (int)BitConverter.ToInt32(receiveBytes, i + 4);
                            break;
                        case 5005:
                            xval = (int)BitConverter.ToInt32(receiveBytes, i + 4);
                            break;
                        case 5006:
                            yval = (int)BitConverter.ToInt32(receiveBytes, i + 4);
                            break;
                        case 5007:
                            nodeidmsg = (int)BitConverter.ToInt16(receiveBytes, i + 4);
                            break;
                    }
                }

                // rssi_send = (Math.Abs(msg5000) + Math.Abs(msg5000)) / 2;
                
                rssi_send = (msg5000 + msg5000) / 2;
                noise_send = (Math.Abs(noise));

                if (xval != yval && xval != 999)
                {
                    var zval = (yval - xval) / 51;
                    var snrmw = (xval - 12 * zval) / (64 * zval);

                    snr_send = (float)(10 * Math.Log10(snrmw) / Math.Log10(10));
                }

                if (msg5000 < 999 || msg5001 < 999)
                {
                    string mytxt = "RSSI1:" + msg5000 + "|RSSI2:" + msg5001 + "|SNR:" + snr_send;
                    Console.WriteLine(mytxt);
                }
                    
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            return true;
            }

        private bool silvus_connect()
        {
            bool status = true;

            /*
            if ((client = new HttpClient()) != null)
                status = true;

            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(LocalIP), Int32.Parse(SilvusPort));
            winSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            winSocket.Bind(localEndPoint);

            IPEndPoint silvusEndPoint = new IPEndPoint(IPAddress.Parse(SilvusIP), Int32.Parse(SilvusPort));
            Remote = (EndPoint)(silvusEndPoint);
            */

            // var header = { };
            /*
            var headers = '{ 'Content-Type': 'application/x-www-form-urlencoded',}';
            var data_portip = '{"jsonrpc": "2.0","method":"rssi_report_address","params":[' + '"' + localIP  + '"' + ', ' + '"' + localPort + '"' + '], "id":"sbkb5u0a"}'
            var data_period = '{"jsonrpc": "2.0","method":"rssi_report_period","params":["1000"], "id":"sbkb5u0b"}'
            var values = new Dictionary<string, string>
            {
                { "thing1", "hello" },
                { "thing2", "world" }
            };
            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync("http://www.example.com/recepticle.aspx", content);
            var responseString = await response.Content.ReadAsStringAsync();
            */

            return status;
        }

        private bool wait_for_ping()
        {
            bool status = false;
            int timeout = 120;

            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();

            try
            {
                PingReply reply = pingSender.Send(SilvusIP, timeout);
                status = (reply.Status == IPStatus.Success) ? true : false;
            }
            catch (Exception e)
            {
                status = false;
            }

            return status;
        }
    }
}
