using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Telecommande
{
    /// <summary>
    /// "Écran": see and control a paired phone or TV from this PC with scrcpy (through ADB).
    /// </summary>
    class ScreenPage : ScrollViewer
    {
        readonly Window owner;
        readonly StackPanel list = new StackPanel();
        readonly TextBlock scrcpyState;
        readonly Button scrcpyInstall;

        public ScreenPage(Window owner)
        {
            this.owner = owner;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

            root.Children.Add(Ui.Text("Voir l'écran d'un téléphone ou d'une TV appairé, et le contrôler à la souris et au clavier.", 14, Theme.Muted));
            root.Children.Add(Header("Appareils"));
            root.Children.Add(Ui.Card(list, 12));

            root.Children.Add(Header("scrcpy"));
            var how = new StackPanel();
            how.Children.Add(Ui.Text("Logiciel libre (github.com/Genymobile/scrcpy) : image fluide (vidéo H.264), son, clavier et souris complets. " +
                "Il faut le débogage ADB sur l'appareil : « Débogage réseau » sur la TV, « Débogage sans fil » sur le téléphone (déjà associé à ce PC).", 13, Theme.Muted));
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            scrcpyState = Ui.Text("", 13, Theme.Muted);
            scrcpyState.VerticalAlignment = VerticalAlignment.Center;
            scrcpyState.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(scrcpyState);
            scrcpyInstall = Ui.Btn(Ui.IconText("", "Installer scrcpy", 13), (s, e) => InstallScrcpy());
            scrcpyInstall.Padding = new Thickness(12, 7, 12, 7);
            row.Children.Add(scrcpyInstall);
            how.Children.Add(row);
            root.Children.Add(Ui.Card(how, 18));

            Content = root;
            Hub.Changed += () => Dispatcher.BeginInvoke(new Action(Build));
            Build();
            ShowScrcpy();
        }

        static TextBlock Header(string s)
        {
            var t = Ui.Text(s.ToUpperInvariant(), 12, Theme.Muted, true);
            t.Margin = new Thickness(4, 22, 0, 8);
            return t;
        }

        void ShowScrcpy()
        {
            string v = Scrcpy.Version;
            scrcpyState.Text = v == null ? "scrcpy n'est pas encore installé." : "scrcpy " + v + " installé.";
            ((TextBlock)((StackPanel)scrcpyInstall.Content).Children[1]).Text = v == null ? "Installer scrcpy" : "Mettre à jour";
        }

        void Build()
        {
            list.Children.Clear();
            var peers = Store.Peers();
            if (peers.Count == 0)
            {
                list.Children.Add(Ui.Text("Aucun appareil appairé. Appaire le téléphone ou la TV dans Fichiers (puce « + »).", 14, Theme.Muted));
                return;
            }
            foreach (var p in peers)
            {
                var peer = p;
                var r = new DockPanel { Margin = new Thickness(6, 6, 6, 6) };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                var sc = Ui.Btn(Ui.IconText("", "Voir et contrôler", 13), (s, e) => StartScrcpy(peer), "AccentPill");
                sc.Padding = new Thickness(14, 7, 14, 7);
                buttons.Children.Add(sc);
                DockPanel.SetDock(buttons, Dock.Right);
                r.Children.Add(buttons);
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(Ui.IconText(peer.Tv ? "" : "", (Hub.Online(peer) ? "● " : "○ ") + peer.Name, 15));
                var sub = Ui.Text(Hub.HostOf(peer) + (Hub.Online(peer) ? "" : "  ·  hors ligne : l'appli Nexio doit être lancée dessus"), 12, Theme.Muted);
                sub.Margin = new Thickness(24, 2, 0, 0);
                names.Children.Add(sub);
                r.Children.Add(names);
                list.Children.Add(r);
            }
        }

        void InstallScrcpy()
        {
            scrcpyInstall.IsEnabled = false;
            new Thread(() =>
            {
                try
                {
                    Scrcpy.Install(m => Dispatcher.BeginInvoke(new Action(() => scrcpyState.Text = m)));
                    Dispatcher.BeginInvoke(new Action(() => { scrcpyInstall.IsEnabled = true; ShowScrcpy(); }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        scrcpyInstall.IsEnabled = true;
                        ShowScrcpy();
                        Ui.Info(owner, "scrcpy", "Installation impossible : " + ex.Message);
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        void StartScrcpy(Peer peer)
        {
            if (Scrcpy.Exe == null)
            {
                if (Ui.Confirm(owner, "scrcpy", "scrcpy n'est pas encore installé. Le télécharger maintenant (environ 6 Mo, depuis github.com/Genymobile/scrcpy) ?", "Installer")) InstallScrcpy();
                return;
            }
            string host = Hub.HostOf(peer);
            scrcpyState.Text = "Connexion ADB à " + peer.Name + "…";
            new Thread(() =>
            {
                try
                {
                    string serial = Scrcpy.FindSerial(host);
                    var proc = Scrcpy.Launch(serial, peer.Name + " (scrcpy)", peer.Tv);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        scrcpyState.Text = "scrcpy lancé pour " + peer.Name + " (" + serial + ").";
                        if (proc != null) new ScrcpyBar(proc, serial, host, peer.Name, peer.Tv).Start();
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => { ShowScrcpy(); Ui.Info(owner, "scrcpy", ex.Message); }));
                }
            }) { IsBackground = true }.Start();
        }
    }
}
