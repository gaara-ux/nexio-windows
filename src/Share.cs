using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Telecommande
{
    /// <summary>A file or folder on some device.</summary>
    class Entry
    {
        public string Name;
        public bool Dir;
        public long Size, Mtime; // mtime in ms since 1970
        public int Count;
    }

    /// <summary>One copy/move (or a file received), shown on the Transfers page.</summary>
    class Transfer
    {
        public bool Sending;
        public string Peer = "", Current = "", Error;
        public int Count, Done;
        public long Total, Bytes;
        public string State = "running"; // running, done, failed, canceled
        public volatile bool Cancel;
        public readonly Stopwatch Clock = Stopwatch.StartNew();

        public int Percent { get { return Total <= 0 ? (State == "done" ? 100 : 0) : (int)Math.Min(100, Bytes * 100 / Total); } }
        public long Speed { get { long ms = Clock.ElapsedMilliseconds; return ms <= 0 ? 0 : Bytes * 1000 / ms; } }
    }

    static class Transfers
    {
        public static readonly List<Transfer> All = new List<Transfer>();
        public static volatile bool Changed;

        public static Transfer Start(bool sending, string peer)
        {
            var t = new Transfer { Sending = sending, Peer = peer };
            lock (All) All.Insert(0, t);
            Changed = true;
            return t;
        }

        public static List<Transfer> Snapshot() { lock (All) return All.ToList(); }

        public static void ClearFinished() { lock (All) All.RemoveAll(t => t.State != "running"); Changed = true; }
    }

    /// <summary>Another copy of the app seen on the network.</summary>
    class Seen
    {
        public string Uuid, Name, Host;
        public int Port;
        public bool Tv;
        public DateTime At;
    }

    /// <summary>
    /// This PC's file sharing: HTTP API on 40819 (same as the phone and TV app),
    /// UDP beacons on 40820, pairing code. Paired devices only.
    /// </summary>
    static class Hub
    {
        public static string Code { get; private set; }
        public static event Action Changed;
        static int wrong;
        static readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
        static TcpListener listener;
        public static string ServerError;

        public static void Start()
        {
            NewCode();
            try
            {
                listener = new TcpListener(IPAddress.Any, Store.SharePort);
                listener.Start();
                new Thread(AcceptLoop) { IsBackground = true, Name = "http" }.Start();
            }
            catch (Exception e) { ServerError = e.Message; }
            new Thread(BeaconLoop) { IsBackground = true, Name = "beacon" }.Start();
            new Thread(ListenLoop) { IsBackground = true, Name = "listen" }.Start();
        }

        static void NewCode()
        {
            var b = new byte[4];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            Code = (BitConverter.ToUInt32(b, 0) % 1000000).ToString("000000");
            Fire();
        }

        public static void Fire() { var c = Changed; if (c != null) c(); }

        public static Dictionary<string, object> Info()
        {
            return new Dictionary<string, object> {
                {"app", "telecommande"}, {"uuid", Store.Uuid}, {"name", Store.Name}, {"port", Store.SharePort}, {"tv", false}
            };
        }

        static readonly object pairGate = new object();

        static Peer TryPair(string code, Dictionary<string, object> who, string host)
        {
            lock (pairGate)
            {
                if (code == null || code != Code)
                {
                    if (++wrong >= 5) { wrong = 0; NewCode(); }
                    return null;
                }
                wrong = 0;
                NewCode();
                var p = new Peer
                {
                    Uuid = Json.S(who, "uuid"), Name = Json.S(who, "name", host), Host = host,
                    Port = (int)Json.L(who, "port", Store.SharePort), Token = Store.NewToken(), Tv = Json.B(who, "tv")
                };
                Store.SavePeer(p);
                Fire();
                return p;
            }
        }

        // ---- Discovery ----

        public static List<Seen> SeenNow()
        {
            lock (seen) return seen.Values.Where(s => (DateTime.UtcNow - s.At).TotalSeconds < 12).ToList();
        }

        public static bool Online(Peer p)
        {
            if (p.Uuid == null) return false; // not paired yet (pairing request)
            lock (seen) { Seen s; return seen.TryGetValue(p.Uuid, out s) && (DateTime.UtcNow - s.At).TotalSeconds < 12; }
        }

        public static string HostOf(Peer p)
        {
            if (p.Uuid == null) return p.Host; // not paired yet: use the address given
            lock (seen) { Seen s; return seen.TryGetValue(p.Uuid, out s) && (DateTime.UtcNow - s.At).TotalSeconds < 12 ? s.Host : p.Host; }
        }

        static void BeaconLoop()
        {
            byte[] msg = Encoding.UTF8.GetBytes(Json.Write(Info()));
            while (true)
            {
                try
                {
                    using (var u = new UdpClient())
                    {
                        u.EnableBroadcast = true;
                        foreach (var b in Broadcasts()) u.Send(msg, msg.Length, new IPEndPoint(b, Store.BeaconPort));
                    }
                }
                catch { }
                Thread.Sleep(3000);
                Fire(); // refresh online dots
            }
        }

        static IEnumerable<IPAddress> Broadcasts()
        {
            var list = new List<IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    byte[] ip = ua.Address.GetAddressBytes(), mask = ua.IPv4Mask.GetAddressBytes(), bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                    list.Add(new IPAddress(bc));
                }
            }
            list.Add(IPAddress.Broadcast);
            return list.Distinct();
        }

        public static string LocalIp()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) return ua.Address.ToString();
            }
            return "";
        }

        static void ListenLoop()
        {
            try
            {
                using (var u = new UdpClient())
                {
                    u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    u.Client.Bind(new IPEndPoint(IPAddress.Any, Store.BeaconPort));
                    while (true)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = u.Receive(ref from);
                        try
                        {
                            var o = Json.Obj(Encoding.UTF8.GetString(data));
                            if (Json.S(o, "app") != "telecommande" || Json.S(o, "uuid") == Store.Uuid) continue;
                            var s = new Seen
                            {
                                Uuid = Json.S(o, "uuid"), Name = Json.S(o, "name"), Host = from.Address.ToString(),
                                Port = (int)Json.L(o, "port", Store.SharePort), Tv = Json.B(o, "tv"), At = DateTime.UtcNow
                            };
                            bool isNew;
                            lock (seen) { isNew = !seen.ContainsKey(s.Uuid); seen[s.Uuid] = s; }
                            if (isNew) Fire();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e) { ServerError = (ServerError == null ? "" : ServerError + " ; ") + "annonces : " + e.Message; }
        }

        // ---- HTTP server ----

        static void AcceptLoop()
        {
            while (true)
            {
                try
                {
                    var c = listener.AcceptTcpClient();
                    new Thread(() => Handle(c)) { IsBackground = true }.Start();
                }
                catch { Thread.Sleep(200); }
            }
        }

        class Denied : Exception { public Denied(string m) : base(m) { } }

        static void Handle(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = c.SendTimeout = 60000;
                    var stream = new BufferedStream(c.GetStream(), 65536);
                    string line = ReadLine(stream);
                    if (line == null) return;
                    var parts = line.Split(' ');
                    if (parts.Length < 2) return;
                    string method = parts[0], target = parts[1];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (string h; (h = ReadLine(stream)) != null && h.Length > 0;)
                    {
                        int i = h.IndexOf(':');
                        if (i > 0) headers[h.Substring(0, i).Trim()] = h.Substring(i + 1).Trim();
                    }
                    string path = target, query = "";
                    int q = target.IndexOf('?');
                    if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
                    var prm = Params(query);
                    long length = 0;
                    string cl;
                    if (headers.TryGetValue("Content-Length", out cl)) long.TryParse(cl, out length);
                    string host = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
                    try { Route(method, path, prm, headers, length, stream, host); }
                    catch (Denied e) { Send(stream, 403, new Dictionary<string, object> { { "error", e.Message } }); }
                    catch (Exception e) { Send(stream, 400, new Dictionary<string, object> { { "error", e.Message } }); }
                    stream.Flush();
                }
                catch { }
            }
        }

        static void Route(string method, string path, Dictionary<string, string> p, Dictionary<string, string> h, long length,
                          Stream s, string host)
        {
            if (path == "/api/info") { Send(s, 200, Info()); return; }
            if (path == "/api/pair" && method == "POST")
            {
                var who = Json.Obj(Encoding.UTF8.GetString(ReadBody(s, length)));
                var peer = TryPair(Json.S(who, "code"), who, host);
                if (peer == null) throw new Denied("Code incorrect.");
                var me = Info();
                me["token"] = peer.Token;
                Send(s, 200, me);
                return;
            }
            string token;
            h.TryGetValue("X-Token", out token);
            var who2 = Store.PeerByToken(token);
            if (who2 == null) throw new Denied("Appareil non appairé.");
            if (who2.Host != host) { who2.Host = host; Store.SavePeer(who2); }
            string arg;
            p.TryGetValue("path", out arg);

            switch (method + " " + path)
            {
                case "GET /api/list":
                    {
                        var items = Local.List(arg).Select(e =>
                        {
                            var o = new Dictionary<string, object> { { "n", e.Name }, { "d", e.Dir }, { "m", e.Mtime } };
                            if (e.Dir) o["c"] = e.Count; else o["s"] = e.Size;
                            return o;
                        }).ToList();
                        Send(s, 200, new Dictionary<string, object> { { "path", arg ?? "" }, { "items", items } });
                        return;
                    }
                case "GET /api/file":
                    {
                        string f = Store.Resolve(arg);
                        if (f == null || !File.Exists(f)) throw new IOException("fichier introuvable");
                        using (var fin = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                        {
                            Head(s, 200, "application/octet-stream", fin.Length);
                            fin.CopyTo(s, 65536);
                        }
                        return;
                    }
                case "PUT /api/file":
                    {
                        string target = Store.Resolve(arg);
                        if (target == null) throw new IOException("chemin refusé");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        string dest = Local.Unique(target);
                        var t = Transfers.Start(false, who2.Name);
                        t.Count = 1; t.Total = length; t.Current = Path.GetFileName(dest);
                        try
                        {
                            using (var fout = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                                Pump(s, fout, length, t);
                        }
                        catch (Exception e)
                        {
                            try { File.Delete(dest); } catch { }
                            t.State = "failed"; t.Error = e.Message; Transfers.Changed = true;
                            throw;
                        }
                        string mt;
                        long ms;
                        if (p.TryGetValue("mtime", out mt) && long.TryParse(mt, out ms) && ms > 0)
                            File.SetLastWriteTimeUtc(dest, Local.FromMs(ms));
                        t.Done = 1; t.State = "done"; Transfers.Changed = true;
                        Send(s, 200, new Dictionary<string, object> { { "path", arg } });
                        return;
                    }
                case "POST /api/mkdir": Local.Mkdir(arg); Send(s, 200, new Dictionary<string, object> { { "ok", true } }); return;
                case "POST /api/delete": Local.Delete(arg); Send(s, 200, new Dictionary<string, object> { { "ok", true } }); return;
                case "POST /api/rename":
                    {
                        string to;
                        p.TryGetValue("to", out to);
                        Local.Rename(arg, to);
                        Send(s, 200, new Dictionary<string, object> { { "ok", true } });
                        return;
                    }
                default: Send(s, 404, new Dictionary<string, object> { { "error", "inconnu" } }); return;
            }
        }

        public static void Pump(Stream from, Stream to, long size, Transfer t)
        {
            var buf = new byte[65536];
            long left = size;
            while (left > 0)
            {
                if (t.Cancel) throw new IOException("annulé");
                int n = from.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) throw new IOException("connexion coupée");
                to.Write(buf, 0, n);
                left -= n;
                t.Bytes += n;
                Transfers.Changed = true;
            }
        }

        static void Send(Stream s, int code, object body)
        {
            byte[] b = Encoding.UTF8.GetBytes(Json.Write(body));
            Head(s, code, "application/json; charset=utf-8", b.Length);
            s.Write(b, 0, b.Length);
        }

        static void Head(Stream s, int code, string type, long length)
        {
            string reason = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Bad Request";
            byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: " + type +
                "\r\nContent-Length: " + length + "\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
        }

        static string ReadLine(Stream s)
        {
            var b = new List<byte>();
            int c;
            while ((c = s.ReadByte()) >= 0)
            {
                if (c == '\n') return Encoding.UTF8.GetString(b.ToArray()).TrimEnd('\r');
                if (b.Count > 8192) throw new IOException("ligne trop longue");
                b.Add((byte)c);
            }
            return b.Count == 0 ? null : Encoding.UTF8.GetString(b.ToArray());
        }

        static byte[] ReadBody(Stream s, long length)
        {
            if (length < 0 || length > 65536) throw new IOException("requête trop grosse");
            var b = new byte[length];
            int off = 0;
            while (off < b.Length)
            {
                int n = s.Read(b, off, b.Length - off);
                if (n <= 0) throw new IOException("requête coupée");
                off += n;
            }
            return b;
        }

        static Dictionary<string, string> Params(string q)
        {
            var d = new Dictionary<string, string>();
            foreach (string kv in q.Split('&'))
            {
                if (kv.Length == 0) continue;
                int i = kv.IndexOf('=');
                string k = Uri.UnescapeDataString((i < 0 ? kv : kv.Substring(0, i)).Replace('+', ' '));
                string v = i < 0 ? "" : Uri.UnescapeDataString(kv.Substring(i + 1).Replace('+', ' '));
                d[k] = v;
            }
            return d;
        }
    }

    /// <summary>This PC's files, by shared path.</summary>
    static class Local
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long ToMs(DateTime utc) { return (long)(utc - Epoch).TotalMilliseconds; }
        public static DateTime FromMs(long ms) { return Epoch.AddMilliseconds(ms); }

        public static List<Entry> List(string rel)
        {
            var list = new List<Entry>();
            string dir = Store.Resolve(rel);
            if (dir == null)
            {
                foreach (string a in Store.Aliases) list.Add(new Entry { Name = a, Dir = true, Count = -1 });
                foreach (string d in Store.Drives()) list.Add(new Entry { Name = d, Dir = true, Count = -1 });
                return list;
            }
            var di = new DirectoryInfo(dir);
            foreach (var d in di.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                int count = -1;
                try { count = d.EnumerateFileSystemInfos().Count(); } catch { }
                list.Add(new Entry { Name = d.Name, Dir = true, Count = count, Mtime = ToMs(d.LastWriteTimeUtc) });
            }
            foreach (var f in di.EnumerateFiles().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                list.Add(new Entry { Name = f.Name, Size = f.Length, Mtime = ToMs(f.LastWriteTimeUtc) });
            return list;
        }

        static string Need(string rel)
        {
            string p = Store.Resolve(rel);
            if (p == null) throw new IOException("impossible ici");
            return p;
        }

        public static void Mkdir(string rel) { Directory.CreateDirectory(Need(rel)); }

        public static void Delete(string rel)
        {
            string p = Need(rel);
            if (Path.GetPathRoot(p) == p || Store.Aliases.Contains(rel)) throw new IOException("refusé");
            if (Directory.Exists(p)) Directory.Delete(p, true);
            else if (File.Exists(p)) File.Delete(p);
        }

        public static void Rename(string rel, string toRel)
        {
            string from = Need(rel), to = Need(toRel);
            if (File.Exists(to) || Directory.Exists(to)) throw new IOException("« " + Path.GetFileName(to) + " » existe déjà");
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
        }

        public static string Unique(string f)
        {
            if (!File.Exists(f) && !Directory.Exists(f)) return f;
            string dir = Path.GetDirectoryName(f), name = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
            for (int i = 1; ; i++)
            {
                string c = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(c) && !Directory.Exists(c)) return c;
            }
        }
    }

    /// <summary>Talks to the other devices. A null peer means this PC.</summary>
    static partial class Client
    {
        public static string Join(string dir, string name) { return string.IsNullOrEmpty(dir) ? name : dir + "/" + name; }
        public static bool Same(Peer a, Peer b) { return a == null ? b == null : b != null && a.Uuid == b.Uuid; }

        static HttpWebRequest Open(Peer p, string path, string method)
        {
            var r = (HttpWebRequest)WebRequest.Create("http://" + Hub.HostOf(p) + ":" + p.Port + path);
            r.Method = method;
            r.Timeout = 8000;
            r.ReadWriteTimeout = 60000;
            r.KeepAlive = false;
            r.Proxy = null;
            if (p.Token != null) r.Headers["X-Token"] = p.Token;
            return r;
        }

        static Dictionary<string, object> ReadJson(HttpWebRequest r)
        {
            try
            {
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.Obj(sr.ReadToEnd());
            }
            catch (WebException e) { throw Explain(e); }
        }

        static Exception Explain(WebException e)
        {
            if (e.Response != null)
            {
                try
                {
                    using (var sr = new StreamReader(e.Response.GetResponseStream()))
                        return new IOException(Json.S(Json.Obj(sr.ReadToEnd()), "error", e.Message));
                }
                catch { }
            }
            if (e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.Timeout)
                return new IOException("L'appareil ne répond pas. L'appli Nexio doit être lancée dessus.");
            return new IOException(e.Message);
        }

        static string Enc(string s) { return Uri.EscapeDataString(s ?? ""); }

        public static Peer Pair(string host, int port, string code)
        {
            var body = Hub.Info();
            body["code"] = code.Trim();
            var r = Open(new Peer { Host = host, Port = port }, "/api/pair", "POST");
            byte[] b = Encoding.UTF8.GetBytes(Json.Write(body));
            r.ContentType = "application/json";
            r.ContentLength = b.Length;
            try { using (var o = r.GetRequestStream()) o.Write(b, 0, b.Length); }
            catch (WebException e) { throw Explain(e); }
            var res = ReadJson(r);
            var p = new Peer
            {
                Uuid = Json.S(res, "uuid"), Name = Json.S(res, "name", host), Host = host,
                Port = (int)Json.L(res, "port", port), Token = Json.S(res, "token"), Tv = Json.B(res, "tv")
            };
            Store.SavePeer(p);
            Hub.Fire();
            return p;
        }

        public static List<Entry> List(Peer p, string path)
        {
            if (p == null) return Local.List(path);
            var res = ReadJson(Open(p, "/api/list?path=" + Enc(path), "GET"));
            return Json.List(res, "items").Select(o => new Entry
            {
                Name = Json.S(o, "n"), Dir = Json.B(o, "d"), Size = Json.L(o, "s"), Mtime = Json.L(o, "m"), Count = (int)Json.L(o, "c")
            }).ToList();
        }

        static void Post(Peer p, string path)
        {
            var r = Open(p, path, "POST");
            r.ContentLength = 0;
            ReadJson(r);
        }

        public static void Mkdir(Peer p, string path) { if (p == null) Local.Mkdir(path); else Post(p, "/api/mkdir?path=" + Enc(path)); }
        public static void Delete(Peer p, string path) { if (p == null) Local.Delete(path); else Post(p, "/api/delete?path=" + Enc(path)); }

        public static void Rename(Peer p, string path, string to)
        {
            if (p == null) Local.Rename(path, to); else Post(p, "/api/rename?path=" + Enc(path) + "&to=" + Enc(to));
        }

        /// <summary>Copies (or moves) entries of fromDir on "from" into toDir on "to", in the background.</summary>
        public static Transfer Run(Peer from, string fromDir, List<Entry> picked, Peer to, string toDir, bool move, Action done)
        {
            var t = Transfers.Start(true, to == null ? "ce PC" : to.Name);
            new Thread(() =>
            {
                try
                {
                    if (move && Same(from, to))
                    {
                        t.Count = picked.Count;
                        foreach (var e in picked) { t.Current = e.Name; Rename(from, Join(fromDir, e.Name), Join(toDir, e.Name)); t.Done++; }
                    }
                    else
                    {
                        var jobs = new List<Tuple<string, string, long, long>>();
                        foreach (var e in picked) Expand(from, Join(fromDir, e.Name), Join(toDir, e.Name), e, jobs);
                        t.Count = jobs.Count;
                        t.Total = jobs.Sum(j => j.Item3);
                        foreach (var j in jobs)
                        {
                            if (t.Cancel) throw new IOException("annulé");
                            t.Current = j.Item1.Substring(j.Item1.LastIndexOf('/') + 1);
                            Transfers.Changed = true;
                            CopyOne(from, j.Item1, to, j.Item2, j.Item3, j.Item4, t);
                            t.Done++;
                        }
                        if (move) foreach (var e in picked) Delete(from, Join(fromDir, e.Name));
                    }
                    t.State = "done";
                }
                catch (Exception ex)
                {
                    t.State = t.Cancel ? "canceled" : "failed";
                    t.Error = ex.Message;
                }
                t.Current = "";
                Transfers.Changed = true;
                if (done != null) done();
            }) { IsBackground = true }.Start();
            return t;
        }

        static void Expand(Peer from, string src, string dst, Entry e, List<Tuple<string, string, long, long>> jobs)
        {
            if (!e.Dir) { jobs.Add(Tuple.Create(src, dst, e.Size, e.Mtime)); return; }
            foreach (var k in List(from, src)) Expand(from, Join(src, k.Name), Join(dst, k.Name), k, jobs);
        }

        static void CopyOne(Peer from, string src, Peer to, string dst, long size, long mtime, Transfer t)
        {
            Stream input;
            WebResponse resp = null;
            if (from == null)
            {
                input = new FileStream(Store.Resolve(src), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
            }
            else
            {
                try { resp = Open(from, "/api/file?path=" + Enc(src), "GET").GetResponse(); }
                catch (WebException e) { throw Explain(e); }
                input = resp.GetResponseStream();
            }
            try
            {
                if (to == null)
                {
                    string target = Store.Resolve(dst);
                    if (target == null) throw new IOException("choisis un dossier de destination");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string dest = Local.Unique(target);
                    try
                    {
                        using (var fout = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                            Hub.Pump(input, fout, size, t);
                    }
                    catch { try { File.Delete(dest); } catch { } throw; }
                    if (mtime > 0) File.SetLastWriteTimeUtc(dest, Local.FromMs(mtime));
                }
                else
                {
                    var put = Open(to, "/api/file?path=" + Enc(dst) + "&mtime=" + mtime, "PUT");
                    put.AllowWriteStreamBuffering = false;
                    put.SendChunked = false;
                    put.ContentLength = size;
                    put.Timeout = 60000;
                    try
                    {
                        using (var o = put.GetRequestStream()) Hub.Pump(input, o, size, t);
                        ReadJson(put);
                    }
                    catch (WebException e) { throw Explain(e); }
                }
            }
            finally
            {
                input.Dispose();
                if (resp != null) resp.Dispose();
            }
        }
    }
}
