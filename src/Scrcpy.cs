using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Telecommande
{
    /// <summary>
    /// scrcpy (github.com/Genymobile/scrcpy, Apache 2.0): the best picture and full control, through ADB.
    /// Downloaded once into %APPDATA%\Nexio\scrcpy; uses the adb that comes with it.
    /// </summary>
    static class Scrcpy
    {
        public static string Dir { get { return Path.Combine(Store.Dir, "scrcpy"); } }

        public static string Exe
        {
            get
            {
                if (!Directory.Exists(Dir)) return null;
                return Directory.GetFiles(Dir, "scrcpy.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
        }

        static string Adb { get { string e = Exe; return e == null ? null : Path.Combine(Path.GetDirectoryName(e), "adb.exe"); } }

        public static string Version
        {
            get
            {
                string e = Exe;
                if (e == null) return null;
                var m = Regex.Match(e, @"v(\d+(\.\d+)+)");
                return m.Success ? m.Groups[1].Value : "installé";
            }
        }

        /// <summary>Downloads the latest Windows release from GitHub. Blocking.</summary>
        public static void Install(Action<string> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            progress("Recherche de la dernière version…");
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Nexio-Windows";
                json = wc.DownloadString("https://api.github.com/repos/Genymobile/scrcpy/releases/latest");
            }
            var root = Json.Obj(json);
            var asset = Json.List(root, "assets").FirstOrDefault(a => Regex.IsMatch(Json.S(a, "name", ""), @"^scrcpy-win64-v[\d.]+\.zip$"));
            if (asset == null) throw new IOException("pas de version Windows 64 bits dans la dernière publication de scrcpy");
            string url = Json.S(asset, "browser_download_url"), name = Json.S(asset, "name");
            if (!url.StartsWith("https://github.com/Genymobile/scrcpy/releases/download/")) throw new IOException("adresse de téléchargement inattendue");
            string zip = Path.Combine(Path.GetTempPath(), name);
            progress("Téléchargement de " + name + "…");
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Nexio-Windows";
                wc.DownloadFile(url, zip);
            }
            progress("Installation…");
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.CreateDirectory(Dir);
            ZipFile.ExtractToDirectory(zip, Dir);
            File.Delete(zip);
            if (Exe == null) throw new IOException("scrcpy.exe introuvable dans l'archive");
        }

        static string Run(string args, int ms = 15000)
        {
            var psi = new ProcessStartInfo(Adb, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(ms);
                return o;
            }
        }

        /// <summary>
        /// The ADB address of a device: already connected, its network ADB on 5555 (TVs), or its
        /// Wireless debugging port as announced on the network (phones; it changes at each start).
        /// </summary>
        public static string FindSerial(string host)
        {
            Run("start-server", 20000);
            string devices = Run("devices");
            var m = Regex.Match(devices, "^(" + Regex.Escape(host) + @":\d+)\s+device", RegexOptions.Multiline);
            if (m.Success) return m.Groups[1].Value;
            var mdns = Regex.Match(Run("mdns services"), Regex.Escape(host) + @":(\d+)", RegexOptions.Multiline);
            foreach (string serial in new[] { mdns.Success ? host + ":" + mdns.Groups[1].Value : null, host + ":5555" })
            {
                if (serial == null) continue;
                string r = Run("connect " + serial);
                if (r.Contains("connected to")) return serial;
            }
            throw new IOException("ADB ne répond pas sur " + host + ".\n\n• TV / SHIELD : active « Débogage réseau » (Options pour les développeurs).\n" +
                "• Téléphone : active « Débogage sans fil » et associe ce PC une fois (code d'association).\n\n" +
                "Sans ADB, utilise le bouton « Voir et contrôler » (notre mode), qui n'en a pas besoin.");
        }

        /// <summary>Sends an Android key through ADB (slower than the remote protocol; used when the device is not paired as a TV).</summary>
        public static void Key(string serial, int code, bool longPress = false)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Run("-s " + serial + " shell input keyevent " + (longPress ? "--longpress " : "") + code, 5000); } catch { }
            });
        }

        /// <summary>
        /// Starts scrcpy. AAC sound (some boxes, like the SHIELD, have no Opus encoder); if it still
        /// stops at once because of the sound, starts again without sound. Blocking for a few seconds.
        /// </summary>
        public static Process Launch(string serial, string title, bool tv)
        {
            string common = "-s " + serial + " --window-title \"" + title.Replace("\"", "") + "\"" + (tv ? " --max-size 1920" : "");
            Process p;
            string err = Start(common + " --audio-codec=aac", out p);
            if (err == null) return p;
            if (err.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 && Start(common + " --no-audio", out p) == null) return p;
            var lines = err.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ERROR") || l.StartsWith("[server] ERROR")).Take(3);
            throw new IOException("scrcpy s'est arrêté :\n" + string.Join("\n", lines));
        }

        /// <summary>Returns null when scrcpy is still running after 6 seconds, else what it printed.</summary>
        static string Start(string args, out Process started)
        {
            started = null;
            var psi = new ProcessStartInfo(Exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Exe),
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            var p = Process.Start(psi);
            var output = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(6000)) { started = p; return null; }
            p.WaitForExit();
            lock (output) return output.Length == 0 ? "scrcpy s'est fermé" : output.ToString();
        }
    }
}
