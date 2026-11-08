using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Telecommande
{
    /// <summary>
    /// Finds Android TV / Google TV / SHIELD on the network the way the Google TV app does:
    /// an mDNS query for "_androidtvremote2._tcp.local", asking for direct (unicast) answers.
    /// Works for TVs that do not have this app installed.
    /// </summary>
    static class Mdns
    {
        public class Found
        {
            public string Name, Host;
            public int Port;
            public DateTime At;
        }

        const string Service = "_androidtvremote2._tcp.local";
        static readonly Dictionary<string, Found> found = new Dictionary<string, Found>();

        public static void Start()
        {
            new Thread(() =>
            {
                while (true)
                {
                    try { Browse(); } catch { }
                    Thread.Sleep(15000);
                }
            }) { IsBackground = true, Name = "mdns" }.Start();
        }

        /// <summary>TVs answering in the last minute.</summary>
        public static List<Found> Tvs()
        {
            lock (found) return found.Values.Where(f => (DateTime.UtcNow - f.At).TotalSeconds < 60).ToList();
        }

        static void Browse()
        {
            byte[] query = Query();
            var sockets = new List<UdpClient>();
            foreach (var ip in LocalIps())
            {
                try
                {
                    var u = new UdpClient(new IPEndPoint(ip, 0));
                    u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
                    u.Send(query, query.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                    sockets.Add(u);
                }
                catch { }
            }
            bool any = false;
            var until = DateTime.UtcNow.AddSeconds(2.5);
            while (DateTime.UtcNow < until && sockets.Count > 0)
            {
                foreach (var u in sockets)
                {
                    while (u.Available > 0)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        try { if (Parse(u.Receive(ref from), from.Address.ToString())) any = true; } catch { }
                    }
                }
                Thread.Sleep(100);
            }
            foreach (var u in sockets) u.Close();
            if (any) Hub.Fire();
        }

        static IEnumerable<IPAddress> LocalIps()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) yield return ua.Address;
            }
        }

        static byte[] Query()
        {
            var b = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }; // id 0, 1 question
            foreach (string label in Service.Split('.'))
            {
                b.Add((byte)label.Length);
                b.AddRange(Encoding.ASCII.GetBytes(label));
            }
            b.Add(0);
            b.AddRange(new byte[] { 0, 12, 0x80, 1 }); // PTR, class IN + "answer me directly" bit
            return b.ToArray();
        }

        /// <summary>Reads PTR/SRV/A records; returns true when a TV was added or refreshed.</summary>
        static bool Parse(byte[] p, string sender)
        {
            if (p.Length < 12) return false;
            int counts = (p[6] << 8 | p[7]) + (p[8] << 8 | p[9]) + (p[10] << 8 | p[11]);
            int qd = p[4] << 8 | p[5];
            int pos = 12;
            for (int i = 0; i < qd; i++) { ReadName(p, ref pos); pos += 4; }
            var instances = new List<string>();
            var srv = new Dictionary<string, Tuple<int, string>>();
            var addr = new Dictionary<string, string>();
            for (int i = 0; i < counts && pos + 10 <= p.Length; i++)
            {
                string name = ReadName(p, ref pos);
                int type = p[pos] << 8 | p[pos + 1];
                int len = p[pos + 8] << 8 | p[pos + 9];
                int data = pos + 10;
                if (type == 12 && name.Equals(Service, StringComparison.OrdinalIgnoreCase))
                {
                    int q = data;
                    instances.Add(ReadName(p, ref q));
                }
                else if (type == 33 && len >= 6)
                {
                    int port = p[data + 4] << 8 | p[data + 5];
                    int q = data + 6;
                    srv[name.ToLowerInvariant()] = Tuple.Create(port, ReadName(p, ref q).ToLowerInvariant());
                }
                else if (type == 1 && len == 4)
                {
                    addr[name.ToLowerInvariant()] = new IPAddress(new[] { p[data], p[data + 1], p[data + 2], p[data + 3] }).ToString();
                }
                pos = data + len;
            }
            bool changed = false;
            foreach (string inst in instances)
            {
                Tuple<int, string> s;
                int port = 6466;
                string host = sender;
                if (srv.TryGetValue(inst.ToLowerInvariant(), out s))
                {
                    port = s.Item1;
                    string a;
                    if (addr.TryGetValue(s.Item2, out a)) host = a;
                }
                string label = inst.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase)
                    ? inst.Substring(0, inst.Length - Service.Length - 1) : inst;
                lock (found) found[host] = new Found { Name = label, Host = host, Port = port, At = DateTime.UtcNow };
                changed = true;
            }
            return changed;
        }

        static string ReadName(byte[] p, ref int pos)
        {
            var parts = new List<string>();
            int jumps = 0, cur = pos;
            bool jumped = false;
            while (cur < p.Length)
            {
                int len = p[cur];
                if (len == 0) { cur++; break; }
                if ((len & 0xC0) == 0xC0)
                {
                    int ptr = (len & 0x3F) << 8 | p[cur + 1];
                    if (!jumped) pos = cur + 2;
                    jumped = true;
                    cur = ptr;
                    if (++jumps > 20) break;
                    continue;
                }
                parts.Add(Encoding.UTF8.GetString(p, cur + 1, len));
                cur += len + 1;
            }
            if (!jumped) pos = cur;
            return string.Join(".", parts);
        }
    }
}
