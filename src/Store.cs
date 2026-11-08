using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Telecommande
{
    /// <summary>A device paired for file sharing (phone, TV, other PC). Same token on both sides.</summary>
    class Peer
    {
        public string Uuid, Name, Host, Token;
        public int Port = Store.SharePort;
        public bool Tv;
    }

    /// <summary>A TV paired for the remote (Android TV Remote protocol).</summary>
    class TvEntry
    {
        public string Name, Host, Fingerprint;
    }

    /// <summary>Settings and paired devices, in %APPDATA%\Nexio.</summary>
    static class Store
    {
        public const int SharePort = 40819, BeaconPort = 40820;
        static readonly object gate = new object();

        public static string Dir
        {
            get
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string d = Path.Combine(app, "Nexio"), old = Path.Combine(app, "Telecommande");
                // The app used to be called Télécommande: keep its paired devices and settings.
                if (!Directory.Exists(d) && Directory.Exists(old)) { try { Directory.Move(old, d); } catch { } }
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static Dictionary<string, object> settings;

        static Dictionary<string, object> Settings
        {
            get
            {
                if (settings == null)
                {
                    string f = Path.Combine(Dir, "settings.json");
                    try { settings = File.Exists(f) ? Json.Obj(File.ReadAllText(f)) : null; } catch { settings = null; }
                    if (settings == null) settings = new Dictionary<string, object>();
                }
                return settings;
            }
        }

        static void SaveSettings()
        {
            File.WriteAllText(Path.Combine(Dir, "settings.json"), Json.Write(Settings));
        }

        public static string Uuid
        {
            get
            {
                lock (gate)
                {
                    string id = Json.S(Settings, "uuid");
                    if (id == null)
                    {
                        id = Guid.NewGuid().ToString();
                        Settings["uuid"] = id;
                        SaveSettings();
                    }
                    return id;
                }
            }
        }

        public static string Name { get { return Environment.MachineName; } }

        // ---- Paired sharing devices ----

        public static List<Peer> Peers()
        {
            lock (gate)
            {
                var list = new List<Peer>();
                string f = Path.Combine(Dir, "peers.json");
                if (!File.Exists(f)) return list;
                try
                {
                    var root = Json.Obj("{\"p\":" + File.ReadAllText(f) + "}");
                    foreach (var o in Json.List(root, "p"))
                        list.Add(new Peer
                        {
                            Uuid = Json.S(o, "uuid"), Name = Json.S(o, "name"), Host = Json.S(o, "host"),
                            Port = (int)Json.L(o, "port", SharePort), Token = Json.S(o, "token"), Tv = Json.B(o, "tv")
                        });
                }
                catch { }
                return list;
            }
        }

        public static Peer PeerByToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            return Peers().FirstOrDefault(p => p.Token == token);
        }

        public static void SavePeer(Peer peer)
        {
            lock (gate)
            {
                var list = Peers().Where(p => p.Uuid != peer.Uuid).ToList();
                list.Add(peer);
                WritePeers(list);
            }
        }

        public static void RemovePeer(string uuid)
        {
            lock (gate) { WritePeers(Peers().Where(p => p.Uuid != uuid).ToList()); }
        }

        static void WritePeers(List<Peer> list)
        {
            var arr = list.Select(p => new Dictionary<string, object> {
                {"uuid", p.Uuid}, {"name", p.Name}, {"host", p.Host}, {"port", p.Port}, {"token", p.Token}, {"tv", p.Tv}
            }).ToList();
            File.WriteAllText(Path.Combine(Dir, "peers.json"), Json.Write(arr));
        }

        // ---- Remote-control TVs ----

        public static List<TvEntry> Tvs()
        {
            lock (gate)
            {
                var list = new List<TvEntry>();
                string f = Path.Combine(Dir, "tvs.json");
                if (!File.Exists(f)) return list;
                try
                {
                    foreach (var o in Json.List(Json.Obj("{\"t\":" + File.ReadAllText(f) + "}"), "t"))
                        list.Add(new TvEntry { Name = Json.S(o, "name"), Host = Json.S(o, "host"), Fingerprint = Json.S(o, "fp") });
                }
                catch { }
                return list;
            }
        }

        public static void SaveTv(TvEntry tv)
        {
            lock (gate)
            {
                var list = Tvs().Where(t => t.Fingerprint != tv.Fingerprint).ToList();
                list.Add(tv);
                File.WriteAllText(Path.Combine(Dir, "tvs.json"), Json.Write(list.Select(t => new Dictionary<string, object> {
                    {"name", t.Name}, {"host", t.Host}, {"fp", t.Fingerprint}
                }).ToList()));
            }
        }

        public static void RemoveTv(string fingerprint)
        {
            lock (gate)
            {
                var list = Tvs().Where(t => t.Fingerprint != fingerprint).ToList();
                File.WriteAllText(Path.Combine(Dir, "tvs.json"), Json.Write(list.Select(t => new Dictionary<string, object> {
                    {"name", t.Name}, {"host", t.Host}, {"fp", t.Fingerprint}
                }).ToList()));
            }
        }

        public static string LastTv
        {
            get { lock (gate) return Json.S(Settings, "lastTv"); }
            set { lock (gate) { Settings["lastTv"] = value; SaveSettings(); } }
        }

        public static string NewToken()
        {
            var b = new byte[24];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            var sb = new StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        // ---- What this PC shows to the other devices ----

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string Downloads
        {
            get
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        /// <summary>Top-level names: the same words as on Android, so phone shortcuts work here too.</summary>
        public static readonly string[] Aliases = { "Download", "Documents", "Pictures", "Movies", "Music", "Desktop" };

        public static string AliasLabel(string a)
        {
            switch (a)
            {
                case "Download": return "Téléchargements";
                case "Pictures": return "Images";
                case "Movies": return "Vidéos";
                case "Music": return "Musique";
                case "Desktop": return "Bureau";
                default: return a;
            }
        }

        static string AliasPath(string a)
        {
            switch (a)
            {
                case "Download": return Downloads;
                case "Documents": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "Pictures":
                case "DCIM": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "Movies": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                case "Music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                case "Desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                default: return null;
            }
        }

        public static IEnumerable<string> Drives()
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try { ok = d.IsReady; } catch { ok = false; }
                if (ok) yield return d.Name.Substring(0, 2); // "C:"
            }
        }

        /// <summary>Turns a shared path ("Download/x.mp4", "C:/Films") into a real path. Null = the virtual root.</summary>
        public static string Resolve(string rel)
        {
            var parts = (rel ?? "").Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            foreach (string p in parts)
                if (p == ".." || p == ".") throw new IOException("chemin refusé");
            string baseDir = AliasPath(parts[0]);
            if (baseDir == null)
            {
                if (!Regex.IsMatch(parts[0], "^[A-Za-z]:$")) throw new IOException("chemin inconnu");
                baseDir = parts[0].ToUpperInvariant() + "\\";
            }
            string full = Path.GetFullPath(Path.Combine(baseDir, string.Join("\\", parts.Skip(1))));
            string b = Path.GetFullPath(baseDir).TrimEnd('\\');
            if (!full.Equals(b, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("chemin refusé");
            return full;
        }
    }
}
