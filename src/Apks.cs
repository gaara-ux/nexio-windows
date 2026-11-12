using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Telecommande
{
    /// <summary>An app installed on a phone or TV running the app (APK extractor).</summary>
    class AppInfo
    {
        public string Pkg, Label, Version;
        public long Code, Size, Updated;
        public bool System;
        public int Parts;

        /// <summary>Same name as the phone gives: "Label_version.apk", or .apks for an app in several pieces.</summary>
        public string FileName
        {
            get
            {
                string v = string.IsNullOrEmpty(Version) ? Code.ToString() : Version;
                return Safe(Label) + "_" + Safe(v) + (Parts > 1 ? ".apks" : ".apk");
            }
        }

        static string Safe(string s)
        {
            string r = Regex.Replace(s ?? "", "[\\\\/:*?\"<>|\\p{Cc}]", "_").Trim();
            return r.Length == 0 ? "app" : r;
        }
    }

    static partial class Client
    {
        /// <summary>Where the APKs land: on this PC, and on a phone or TV.</summary>
        public static string ApkFolderPc { get { return Path.Combine(Store.Downloads, "Nexio", "APK"); } }
        public const string ApkFolderAndroid = "Download/Nexio/APK";

        public static List<AppInfo> Apps(Peer p)
        {
            var res = ReadJson(Open(p, "/api/apps", "GET"));
            return Json.List(res, "apps").Select(o => new AppInfo
            {
                Pkg = Json.S(o, "pkg"), Label = Json.S(o, "label", Json.S(o, "pkg")), Version = Json.S(o, "ver", ""),
                Code = Json.L(o, "code"), Size = Json.L(o, "size"), Updated = Json.L(o, "upd"),
                System = Json.B(o, "sys"), Parts = Math.Max(1, (int)Json.L(o, "parts", 1))
            }).ToList();
        }

        public static bool IsPackage(string name)
        {
            string x = Path.GetExtension(name ?? "").ToLowerInvariant();
            return x == ".apk" || x == ".apks" || x == ".apkm" || x == ".xapk";
        }

        /// <summary>Installs a file that is on the device, on that device: its screen asks to confirm. Blocking.</summary>
        public static void Install(Peer p, string path) { Post(p, "/api/install?path=" + Enc(path)); }

        /// <summary>Uninstalls an app of the device: its screen asks to confirm. Blocking.</summary>
        public static void Uninstall(Peer p, string pkg) { Post(p, "/api/uninstall?pkg=" + Enc(pkg)); }

        /// <summary>Sends a file of this PC to the device's APK folder, then installs it there. Blocking.</summary>
        public static void InstallFromPc(string file, Peer to, Transfer t)
        {
            var fi = new FileInfo(file);
            var put = Open(to, "/api/file?path=" + Enc(ApkFolderAndroid + "/" + fi.Name) + "&mtime=" + Local.ToMs(fi.LastWriteTimeUtc), "PUT");
            put.AllowWriteStreamBuffering = false;
            put.SendChunked = false;
            put.ContentLength = fi.Length;
            put.Timeout = 60000;
            string remote;
            try
            {
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                using (var o = put.GetRequestStream()) Hub.Pump(input, o, fi.Length, t);
                remote = Json.S(ReadJson(put), "path", ApkFolderAndroid + "/" + fi.Name);
            }
            catch (WebException e) { throw Explain(e); }
            Install(to, remote);
        }

        public static byte[] AppIcon(Peer p, string pkg)
        {
            var r = Open(p, "/api/icon?pkg=" + Enc(pkg), "GET");
            try
            {
                using (var resp = r.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var m = new MemoryStream())
                {
                    s.CopyTo(m);
                    return m.ToArray();
                }
            }
            catch (WebException e) { throw Explain(e); }
        }

        /// <summary>
        /// Takes the apps' APKs from "from" and puts them on this PC (to null) or on another device "to"
        /// (in its Download/Nexio/APK), in the background. Progress on the Transfers page.
        /// </summary>
        public static Transfer ExtractApks(Peer from, List<AppInfo> apps, Peer to, Action done)
        {
            var t = Transfers.Start(true, to == null ? "ce PC (APK)" : to.Name + " (APK)");
            t.Count = apps.Count;
            t.Total = apps.Sum(a => a.Size);
            new Thread(() =>
            {
                try
                {
                    foreach (var a in apps)
                    {
                        if (t.Cancel) throw new IOException("annulé");
                        t.Current = a.Label;
                        Transfers.Changed = true;
                        long before = t.Bytes;
                        WebResponse resp;
                        try { resp = Open(from, "/api/apk?pkg=" + Enc(a.Pkg), "GET").GetResponse(); }
                        catch (WebException e) { throw Explain(e); }
                        using (resp)
                        using (var input = resp.GetResponseStream())
                        {
                            long len = resp.ContentLength;
                            if (to == null)
                            {
                                Directory.CreateDirectory(ApkFolderPc);
                                string dest = Local.Unique(Path.Combine(ApkFolderPc, a.FileName));
                                try
                                {
                                    using (var fout = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                                        Hub.Pump(input, fout, len, t);
                                }
                                catch { try { File.Delete(dest); } catch { } throw; }
                            }
                            else
                            {
                                var put = Open(to, "/api/file?path=" + Enc(ApkFolderAndroid + "/" + a.FileName), "PUT");
                                put.AllowWriteStreamBuffering = false;
                                put.SendChunked = false;
                                put.ContentLength = len;
                                put.Timeout = 60000;
                                try
                                {
                                    using (var o = put.GetRequestStream()) Hub.Pump(input, o, len, t);
                                    ReadJson(put);
                                }
                                catch (WebException e) { throw Explain(e); }
                            }
                        }
                        t.Bytes = before + a.Size; // a .apks is a little bigger than its pieces
                        t.Done++;
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
    }
}
