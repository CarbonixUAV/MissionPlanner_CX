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
using System.Xml;
using GMap.NET.WindowsForms;
using MissionPlanner.Maps;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;
using System.Web.Script.Serialization;
using OpenTK.Audio.OpenAL;

namespace MissionPlanner.plugins
{
    public class rssi_node
    {
        private bool _node_enabled;
        public bool node_enabled
        {
            get { return _node_enabled; }
            set { _node_enabled = value; }
        }

        private string _node_name;
        public string node_name
        {
            get { return _node_name; }
            set { _node_name = value; }
        }

        private string _remote_ip;
        public string remote_ip
        {
            get { return _remote_ip; }
            set { _remote_ip = value; }
        }

        private string _local_ip;
        public string local_ip
        {
            get { return _local_ip; }
            set { _local_ip = value; }
        }

        private int _node_port;
        public int node_port
        {
            get { return _node_port; }
            set { _node_port = value; }
        }

        private UdpClient udp_server;

        public int rssi1 { get; set; }
        public int rssi2 { get; set; }
        public float snr { get; set; }

        public enum node_state
        {
            node_ping_test,
            node_connect,
            node_check_for_message,
            node_error
        }
        public node_state node_current_state;

        int retry_count;


        private void init_node()
        {
            retry_count = 0;
            node_current_state = node_state.node_ping_test;
        }

        private void close_node()
        {
            node_enabled = false;
            node_current_state = node_state.node_ping_test;
            udp_server.Close();
        }

        public rssi_node(bool enabled, string name, string remote, string local, int port)
        {
            node_enabled = enabled;
            node_name = name;
            remote_ip = remote;
            local_ip = local;
            node_port = port;

            init_node();
        }

        public void node_process()
        {
            // Start the node process
            switch (node_current_state)
            {
                case node_state.node_ping_test:
                    if (ping_test())
                    {
                        node_current_state = node_state.node_connect;
                    }
                    else
                    {
                        if (retry_count++ > 100)
                        {
                            retry_count = 0;
                            Console.WriteLine("RSSI comm: unable to ping - " + remote_ip);
                        }
                    }
                    break;

                case node_state.node_connect:
                    if (connect())
                    {
                        node_current_state = node_state.node_check_for_message;
                    }
                    else
                    {
                        if (retry_count++ > 10)
                        {
                            retry_count = 0;
                            Console.WriteLine("RSSI comm: unable to connect - " + remote_ip);
                            node_current_state = node_state.node_ping_test;
                        }
                    }
                    break;

                case node_state.node_check_for_message:
                    if (!fetch_data())
                    {
                        if (retry_count++ > 10)
                        {
                            retry_count = 0;
                            Console.WriteLine("RSSI comm: unable to connect - " + remote_ip);
                            node_current_state = node_state.node_ping_test;
                        }
                    }
                    break;
            }
        }


        private bool ping_test()
        {
            bool status = false;
            int timeout = 120;

            // Ping the remote IP
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();

            try
            {
                PingReply reply = pingSender.Send(remote_ip, timeout);
                status = (reply.Status == IPStatus.Success) ? true : false;
            }
            catch
            {
                status = false;
            }

            return status;
        }


        private bool connect()
        {
            bool status = true;
            try
            {
                udp_server = new UdpClient(node_port);
            }
            catch
            {
                status = false;
            }

            return status;
        }


        private bool fetch_data()
        {
            bool status = true;

            try
            {
                IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 8000);
                byte[] data = udp_server.Receive(ref RemoteIpEndPoint);
                // string receive_bytes = Encoding.UTF8.GetString(data);

                if (RemoteIpEndPoint.Address.ToString() == remote_ip)
                {
                    status = parse_data(data);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                status = false;
            }

            return status;
        }


        // private bool parse_data(string receive_bytes)
        private bool parse_data(byte[] data)
        {
            bool status = true;

            short data_length = 0;
            string xval = "";
            string yval = "";

            try
            {
                for (int i = 0; i < data.Length - 4; i += 1)
                {
                    char[] ch = new char[4];

                    short report_type = (short)(data[i] << 0x08 | data[i + 1]);
                    data_length = (short)(data[i + 2] << 0x08 | data[i + 3]);

                    switch (report_type)
                    {
                        case 5000:
                            ch[0] = (char)data[i + data_length];
                            ch[1] = (char)data[i + data_length + 1];
                            ch[2] = (char)data[i + data_length + 2];
                            ch[3] = (char)data[i + data_length + 3];

                            rssi1 = Int32.Parse(new string(ch));
                            break;
                        case 5001:
                            ch[0] = (char)data[i + data_length];
                            ch[1] = (char)data[i + data_length + 1];
                            ch[2] = (char)data[i + data_length + 2];
                            ch[3] = (char)data[i + data_length + 3];
                            rssi2 = Int32.Parse(new string(ch));
                            break;
                        //case 5004:
                        //    noise = int.Parse(receive_bytes.Substring(i + 4, data_length));
                        //    //noise = (int)BitConverter.ToInt32(receive_bytes, i + 4);
                        //    break;
                        case 5005:
                            ch[0] = (char)data[i + data_length];
                            ch[1] = (char)data[i + data_length + 1];
                            ch[2] = (char)data[i + data_length + 2];
                            ch[3] = (char)data[i + data_length + 3];
                            xval = new string(ch);

                            // xval = int.Parse(receive_bytes.Substring(i + 4, data_length));
                            // xval = (int)BitConverter.ToInt32(receive_bytes, i + 4);
                            break;
                        case 5006:
                            ch[0] = (char)data[i + data_length];
                            ch[1] = (char)data[i + data_length + 1];
                            ch[2] = (char)data[i + data_length + 2];
                            ch[3] = (char)data[i + data_length + 3];
                            yval = new string(ch);

                            // yval = int.Parse(receive_bytes.Substring(i + 4, data_length));
                            // yval = (int)BitConverter.ToInt32(receive_bytes, i + 4);
                            break;
                        //case 5007:
                        //    nodeidmsg = int.Parse(receive_bytes.Substring(i + 4, data_length));
                        //    //nodeidmsg = (int)BitConverter.ToInt16(receiveBytes, i + 4);
                        //    break;

                        default:
                            break;
                    }
                }

                if (xval != "" && yval != "")
                {
                    int x = int.Parse(xval);
                    int y = int.Parse(yval);

                    if (x != y && x != 999)
                    {
                        float zval = (float)((y - x) / 51.0);
                        var snrmw = (x - 12 * zval) / (64 * zval);

                        this.snr = (int)(10 * Math.Log10(snrmw) / Math.Log10(10));
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                status = false;
            }

            return status;
        }
    }


    public class SilvusRadio : MissionPlanner.Plugin.Plugin
    {
        rssi_node first;
        rssi_node second;

        string comms_xml_file = "Comms_RSSI.xml";

        public override string Name
        {
            get { return "Silvus Radio RSSI"; }
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
            read_xml(comms_xml_file);
            loopratehz = 100;

            return true;
        }


        public override bool Loop()
        {
            if (first != null && first.node_enabled)
            {
                first.node_process();
                if (first.node_enabled && first.node_current_state == rssi_node.node_state.node_check_for_message)
                {
                    silvus_display_RSSI(1);
                }
            }

            if (second != null && second.node_enabled)
            {
                second.node_process();
                if (second.node_enabled && second.node_current_state == rssi_node.node_state.node_check_for_message)
                {
                    silvus_display_RSSI(2);
                }
            }

            return true;
        }

        private void silvus_display_RSSI(int v)
        {
            if (v == 1)
            {
                FlightData.instance.update_snr_data(v, first.node_name, first.rssi1, first.rssi2, first.snr);
            }
            else
            {
                FlightData.instance.update_snr_data(v, first.node_name, second.rssi1, second.rssi2, second.snr);
            }
        }

        private void read_xml(string filename)
        {
            int index = 0;

            try
            {
                using (XmlTextReader xmlreader = new XmlTextReader(Settings.GetUserDataDirectory() + filename))
                {
                    while (xmlreader.Read())
                    {
                        bool node_enabled = false;
                        string node_name = "";
                        string remote_ip = "";
                        string local_ip = "";
                        int port = 0;

                        xmlreader.MoveToElement();
                        try
                        {
                            switch (xmlreader.Name)
                            {
                                case "RSSI_node":
                                    while (xmlreader.Read())
                                    {
                                        bool dobreak = false;
                                        xmlreader.MoveToElement();
                                        switch (xmlreader.Name)
                                        {
                                            case "node_enabled":
                                                node_enabled = (xmlreader.ReadString() == "true") ? true : false;
                                                break;
                                            case "node_name":
                                                node_name = (string)xmlreader.ReadString();
                                                break;
                                            case "remote_ip":
                                                remote_ip = (string)xmlreader.ReadString();
                                                break;
                                            case "local_IP":
                                                local_ip = (string)xmlreader.ReadString();
                                                break;
                                            case "port":
                                                port = Int32.Parse(xmlreader.ReadString());
                                                break;
                                            case "RSSI_node":
                                                if (index++ == 0)
                                                {
                                                    first = new rssi_node(node_enabled, node_name, remote_ip, local_ip, port);
                                                }
                                                else
                                                {
                                                    second = new rssi_node(node_enabled, node_name, remote_ip, local_ip, port);
                                                }
                                                dobreak = true;
                                                break;
                                        }
                                        if (dobreak) break;
                                    }
                                    break;

                                default: break;
                            }
                        }
                        catch (Exception ee) { Console.WriteLine("RSSI comm: " + ee.Message); } // silent fail on bad entry
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("RSSI comm: Bad config file: " + ex.ToString()); } // bad config file
        }
    }
}
