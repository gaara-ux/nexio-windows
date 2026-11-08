using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace Telecommande
{
    /// <summary>One thing the app needs on this PC, with how to check and fix it.</summary>
    class Dep
    {
        public string Title, Detail;
        public bool Ok, Optional, NeedsAdmin;
        public Func<bool> Check;
        public Func<string> Describe;   // detail text after a check
        public string AdminScript;      // PowerShell run elevated to fix
        public Action Fix;              // fix without admin
    }

    /// <summary>Checks what the app needs on Windows and installs/repairs it.</summary>
    static class Deps
    {
        const string RuleTcp = "Nexio - partage (TCP 40819)";
        const string RuleUdp = "Nexio - annonces (UDP 40820)";
        const string RuleApp = "Nexio - application (detection des TV)";
        /// <summary>The app used to be called Télécommande: move its start-with-Windows entry to this exe.</summary>
        public static void MigrateAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue("Telecommande") == null) return;
                    k.DeleteValue("Telecommande", false);
                    k.SetValue("Nexio", "\"" + Assembly.GetExecutingAssembly().Location + "\" --minimized");
                }
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string ReceiveDir { get { return Path.Combine(Store.Downloads, "Nexio"); } }

        public static List<Dep> All()
        {
            var list = new List<Dep>();

            list.Add(new Dep
            {
                Title = ".NET Framework 4.7.2 ou plus récent",
                Check = () => NetRelease() >= 461808,
                Describe = () => "Version installée : " + NetVersion() + ". Le lanceur Nexio.cmd l'installe s'il manque.",
            });

            list.Add(new Dep
            {
                Title = "Pare-feu Windows : autoriser le partage",
                NeedsAdmin = true,
                Check = () => RuleExists(RuleTcp) && RuleExists(RuleUdp) && RuleExists(RuleApp),
                Describe = () => "Ports 40819 (fichiers) et 40820 (annonces) ouverts sur les réseaux privés, pour que le téléphone et la TV voient ce PC.",
                AdminScript =
                    "netsh advfirewall firewall delete rule name=\"" + RuleTcp + "\" | Out-Null;" +
                    "netsh advfirewall firewall delete rule name=\"" + RuleUdp + "\" | Out-Null;" +
                    "netsh advfirewall firewall add rule name=\"" + RuleTcp + "\" dir=in action=allow protocol=TCP localport=40819 profile=private,domain | Out-Null;" +
                    "netsh advfirewall firewall add rule name=\"" + RuleUdp + "\" dir=in action=allow protocol=UDP localport=40820 profile=private,domain | Out-Null;" +
                    // The app itself: answers to TV detection (mDNS) come back on other ports.
                    "netsh advfirewall firewall delete rule name=\"" + RuleApp + "\" | Out-Null;" +
                    "netsh advfirewall firewall add rule name=\"" + RuleApp + "\" dir=in action=allow program=\"" +
                    Assembly.GetExecutingAssembly().Location + "\" profile=private,domain | Out-Null;",
            });

            list.Add(new Dep
            {
                Title = "Réseau Wi-Fi/Ethernet en mode « privé »",
                NeedsAdmin = true,
                Check = () => !PublicNetwork(),
                Describe = () => PublicNetwork()
                    ? "Ton réseau est en mode « public » : Windows bloque les appareils de la maison. À passer en « privé »."
                    : "Le réseau de la maison est bien en mode privé.",
                AdminScript = "Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' } | Set-NetConnectionProfile -NetworkCategory Private;",
            });

            list.Add(new Dep
            {
                Title = "Dossier de réception",
                Check = () => Directory.Exists(ReceiveDir),
                Describe = () => ReceiveDir,
                Fix = () => Directory.CreateDirectory(ReceiveDir),
            });

            list.Add(new Dep
            {
                Title = "Démarrer avec Windows (conseillé)",
                Optional = true,
                Check = () => AutoStart,
                Describe = () => "Le PC reste visible pour le téléphone et la TV sans ouvrir l'appli à la main.",
                Fix = () => AutoStart = true,
            });

            return list;
        }

        /// <summary>Runs every check; returns true when all required ones pass.</summary>
        public static bool CheckAll(List<Dep> deps)
        {
            bool ok = true;
            foreach (var d in deps)
            {
                try { d.Ok = d.Check(); } catch { d.Ok = false; }
                try { d.Detail = d.Describe(); } catch { d.Detail = ""; }
                if (!d.Ok && !d.Optional) ok = false;
            }
            return ok;
        }

        /// <summary>Fixes everything broken: one UAC prompt for all admin fixes. Returns an error or null.</summary>
        public static string FixAll(List<Dep> deps, bool includeOptional)
        {
            var script = new StringBuilder();
            foreach (var d in deps)
            {
                if (d.Ok || (d.Optional && !includeOptional)) continue;
                if (d.Fix != null) { try { d.Fix(); } catch (Exception e) { return d.Title + " : " + e.Message; } }
                if (d.AdminScript != null) script.Append(d.AdminScript);
            }
            if (script.Length == 0) return null;
            try
            {
                string cmd = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Continue';" + script));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + cmd)
                {
                    Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi)) p.WaitForExit(60000);
                return null;
            }
            catch (System.ComponentModel.Win32Exception) { return "Autorisation administrateur refusée."; }
            catch (Exception e) { return e.Message; }
        }

        // ---- Checks ----

        static int NetRelease()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                return k == null ? 0 : Convert.ToInt32(k.GetValue("Release", 0));
        }

        static string NetVersion()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                return k == null ? "aucune" : Convert.ToString(k.GetValue("Version", "?"));
        }

        static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode == 0 ? o : null;
            }
        }

        static bool RuleExists(string name)
        {
            return Run("netsh", "advfirewall firewall show rule name=\"" + name + "\"") != null;
        }

        static bool? publicCache;

        static bool PublicNetwork()
        {
            if (publicCache.HasValue) return publicCache.Value;
            string o = Run("powershell.exe", "-NoProfile -Command \"(Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' }).Count\"");
            int n;
            publicCache = o != null && int.TryParse(o.Trim(), out n) && n > 0;
            return publicCache.Value;
        }

        public static void Forget() { publicCache = null; }

        public static bool AutoStart
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("Nexio") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("Nexio", "\"" + Assembly.GetExecutingAssembly().Location + "\" --minimized");
                    else k.DeleteValue("Nexio", false);
                }
            }
        }
    }
}
