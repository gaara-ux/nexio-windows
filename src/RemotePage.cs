using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Telecommande
{
    /// <summary>Remote page: TV list, D-pad and buttons, typing on the TV, keyboard shortcuts.</summary>
    class RemotePage : Grid
    {
        readonly Window owner;
        readonly StackPanel tvList = new StackPanel();
        readonly TextBlock status, appText;
        readonly DPad pad = new DPad();
        readonly TextBox input = new TextBox();
        readonly DispatcherTimer retry = new DispatcherTimer();
        AtvRemote remote;
        TvEntry current;
        string lastSent = "";
        int retryMs = 1000;

        static readonly Dictionary<string, string> Apps = new Dictionary<string, string> {
            {"com.google.android.youtube.tv", "YouTube"}, {"com.netflix.ninja", "Netflix"}, {"ca.fizz_app.tv", "Fizz"},
            {"com.amazon.amazonvideo.livingroom", "Prime Video"}, {"com.disney.disneyplus", "Disney+"}, {"ca.bellmedia.cravetv", "Crave"},
            {"tv.tou.android", "ICI TOU.TV"}, {"com.plexapp.android", "Plex"}, {"org.xbmc.kodi", "Kodi"}, {"com.android.vending", "Play Store"},
            {"com.gathin.tvhome", "Accueil"}, {"com.google.android.tvlauncher", "Accueil"}, {"com.gaara.remote", "Nexio"}, {"io.github.gaara_ux.nexio", "Nexio"},
            {"com.android.tv.settings", "Paramètres"}, {"com.spotify.tv.android", "Spotify"}
        };

        public RemotePage(Window owner)
        {
            this.owner = owner;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

            // ---- Left: TVs ----
            var left = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(Header("Mes TV"));
            left.Children.Add(Ui.Card(tvList, 10));
            var add = Ui.Btn(Ui.IconText("", "Ajouter une TV"), (s, e) => AddTv());
            add.Margin = new Thickness(0, 10, 0, 0);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            left.Children.Add(add);
            var stateCard = new StackPanel();
            status = Ui.Text("Aucune TV choisie", 15, Theme.Muted, true);
            appText = Ui.Text("", 13, Theme.Muted);
            stateCard.Children.Add(status);
            stateCard.Children.Add(appText);
            var sc = Ui.Card(stateCard, 14);
            sc.Margin = new Thickness(0, 18, 0, 0);
            left.Children.Add(sc);
            Children.Add(left);

            // ---- Center: pad + buttons ----
            var center = new Grid();
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pad.Key += Key;
            pad.Margin = new Thickness(10);
            pad.MaxWidth = pad.MaxHeight = 380;
            center.Children.Add(pad);
            var rows = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            rows.Children.Add(Row(Round("", AtvRemote.BACK, "Retour (Échap)"), Round("", AtvRemote.HOME, "Accueil (Début)"), Round("", AtvRemote.MENU, "Menu")));
            rows.Children.Add(Row(Round("", AtvRemote.REWIND, "Reculer"), Round("", AtvRemote.PLAY_PAUSE, "Lecture / pause (Espace)"), Round("", AtvRemote.FORWARD, "Avancer")));
            rows.Children.Add(Row(Round("", AtvRemote.VOL_DOWN, "Volume − (touche -)"), Round("", AtvRemote.MUTE, "Muet (M)"), Round("", AtvRemote.VOL_UP, "Volume + (touche +)"), Round("", AtvRemote.POWER, "Marche / arrêt")));
            Grid.SetRow(rows, 1);
            center.Children.Add(rows);
            Grid.SetColumn(center, 1);
            Children.Add(center);

            // ---- Right: typing + shortcuts ----
            var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            right.Children.Add(Header("Écrire sur la TV"));
            var tcard = new StackPanel();
            tcard.Children.Add(Ui.Text("Ouvre une recherche sur la TV, puis tape ici : le texte s'écrit directement dedans.", 13, Theme.Muted));
            input.Margin = new Thickness(0, 12, 0, 10);
            input.TextChanged += (s, e) => PushText();
            input.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Submit(); e.Handled = true; } };
            tcard.Children.Add(input);
            tcard.Children.Add(Ui.Btn(Ui.IconText("", "Valider sur la TV"), (s, e) => Submit(), "AccentPill"));
            right.Children.Add(Ui.Card(tcard));
            right.Children.Add(Header("Raccourcis clavier"));
            var help = new StackPanel();
            foreach (var l in new[] { "Flèches : se déplacer", "Entrée : OK", "Échap ou Retour arrière : Retour", "Début : Accueil",
                                      "Espace : lecture / pause", "+ / − : volume", "M : couper le son" })
            {
                var t = Ui.Text(l, 13, Theme.Muted);
                t.Margin = new Thickness(0, 2, 0, 2);
                help.Children.Add(t);
            }
            right.Children.Add(Ui.Card(help, 14));
            Grid.SetColumn(right, 2);
            Children.Add(right);

            retry.Tick += (s, e) => { retry.Stop(); if (current != null) Connect(current); };
            Hub.Changed += () => Dispatcher.BeginInvoke(new Action(BuildTvList));
            BuildTvList();
            var last = Store.Tvs().FirstOrDefault(t => t.Fingerprint == Store.LastTv) ?? Store.Tvs().FirstOrDefault();
            if (last != null) Connect(last);
        }

        static TextBlock Header(string s)
        {
            var t = Ui.Text(s.ToUpperInvariant(), 12, Theme.Muted, true);
            t.Margin = new Thickness(4, 18, 0, 8);
            return t;
        }

        static StackPanel Row(params Button[] buttons)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
            double gap = buttons.Length > 3 ? 8 : 14; // four buttons: tighter, so the row fits
            foreach (var b in buttons) { b.Margin = new Thickness(gap, 0, gap, 0); p.Children.Add(b); }
            return p;
        }

        Button Round(string glyph, int key, string tip)
        {
            var b = Ui.Btn(Ui.Icon(glyph, 22), (s, e) => Key(key), "Round", tip);
            if (key == AtvRemote.POWER) ((TextBlock)b.Content).Foreground = Theme.B(Theme.Danger);
            return b;
        }

        // ---- TVs ----

        void BuildTvList()
        {
            tvList.Children.Clear();
            var tvs = Store.Tvs();
            if (tvs.Count == 0)
            {
                var t = Ui.Text("Aucune TV. Touche « Ajouter une TV ».", 13, Theme.Muted);
                t.Margin = new Thickness(8);
                tvList.Children.Add(t);
            }
            foreach (var tv in tvs)
            {
                var item = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), IsChecked = current != null && current.Fingerprint == tv.Fingerprint };
                item.Margin = new Thickness(0, 3, 0, 3);
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                item.Content = Ui.IconText("", tv.Name + "   " + tv.Host, 14);
                var entry = tv;
                item.Click += (s, e) => Connect(entry);
                var menu = new ContextMenu();
                var forget = new MenuItem { Header = "Oublier cette TV" };
                forget.Click += (s, e) =>
                {
                    if (!Ui.Confirm(owner, "Oublier " + entry.Name + " ?", "Il faudra refaire l'appairage avec le code affiché sur la TV.", "Oublier", true)) return;
                    Store.RemoveTv(entry.Fingerprint);
                    if (current != null && current.Fingerprint == entry.Fingerprint) { Disconnect(); current = null; status.Text = "Aucune TV choisie"; }
                    BuildTvList();
                };
                menu.Items.Add(forget);
                item.ContextMenu = menu;
                item.ToolTip = "Clic droit : oublier";
                tvList.Children.Add(item);
            }

            // Found automatically on the network (Google TV way, or the TV's own app): one click to pair.
            var paired = tvs.Select(t => t.Host).ToList();
            var detected = new Dictionary<string, string>(); // host -> name
            foreach (var f in Mdns.Tvs()) if (!paired.Contains(f.Host)) detected[f.Host] = f.Name;
            foreach (var s in Hub.SeenNow().Where(s => s.Tv)) if (!paired.Contains(s.Host) && !detected.ContainsKey(s.Host)) detected[s.Host] = s.Name;
            if (detected.Count > 0)
            {
                var h = Ui.Text("TROUVÉES SUR LE RÉSEAU", 11, Theme.Muted, true);
                h.Margin = new Thickness(8, 12, 0, 4);
                tvList.Children.Add(h);
            }
            foreach (var d in detected)
            {
                string host = d.Key, name = d.Value;
                var row = new DockPanel { Margin = new Thickness(4, 3, 0, 3) };
                var pair = Ui.Btn("Appairer", (s, e) => Pair(host, name), "AccentPill");
                pair.Padding = new Thickness(12, 5, 12, 5);
                DockPanel.SetDock(pair, Dock.Right);
                row.Children.Add(pair);
                var label = Ui.IconText("", name + "   " + host, 13, Theme.Muted);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                tvList.Children.Add(row);
            }
        }

        void AddTv()
        {
            // TVs running the app announce themselves; any Android/Google TV works by IP.
            var known = Store.Tvs().Select(t => t.Host).ToList();
            var found = Hub.SeenNow().Where(s => s.Tv && !known.Contains(s.Host)).ToList();
            var options = found.Select(s => "TV : " + s.Name + "  (" + s.Host + ")").ToList();
            options.Add("Par adresse IP…");
            int k = Ui.Pick(owner, "Ajouter une TV", options, "Android TV, Google TV ou SHIELD sur le même réseau.");
            if (k < 0) return;
            string host, name;
            if (k < found.Count) { host = found[k].Host; name = found[k].Name; }
            else
            {
                host = Ui.Ask(owner, "Adresse IP de la TV", "Sur la TV : Paramètres › Réseau.", "", "Suivant");
                if (string.IsNullOrWhiteSpace(host)) return;
                host = host.Trim();
                name = "TV " + host;
            }
            Pair(host, name);
        }

        void Pair(string host, string name)
        {
            status.Text = "Connexion à " + name + "…";
            new Thread(() =>
            {
                AtvPairing pairing = null;
                try
                {
                    pairing = new AtvPairing(host);
                    var p = pairing;
                    Dispatcher.Invoke(new Action(() => AskCode(p, host, name, null)));
                }
                catch (Exception e)
                {
                    if (pairing != null) pairing.Dispose();
                    Dispatcher.BeginInvoke(new Action(() => Ui.Info(owner, "Appairage impossible", e.Message)));
                }
            }) { IsBackground = true }.Start();
        }

        void AskCode(AtvPairing pairing, string host, string name, string error)
        {
            string code = Ui.Ask(owner, "Code de la TV", (error != null ? error + "\n\n" : "") + "Tape le code à 6 caractères affiché sur la TV.", "", "Valider", true);
            if (code == null) { pairing.Dispose(); status.Text = "Appairage annulé"; return; }
            new Thread(() =>
            {
                try
                {
                    string fp = pairing.Finish(code);
                    pairing.Dispose();
                    var tv = new TvEntry { Name = name, Host = host, Fingerprint = fp };
                    Store.SaveTv(tv);
                    Dispatcher.BeginInvoke(new Action(() => { BuildTvList(); Connect(tv); }));
                }
                catch (Exception e)
                {
                    if (e.Message.StartsWith("Code incorrect")) Dispatcher.BeginInvoke(new Action(() => AskCode(pairing, host, name, e.Message)));
                    else { pairing.Dispose(); Dispatcher.BeginInvoke(new Action(() => Ui.Info(owner, "Appairage refusé", e.Message))); }
                }
            }) { IsBackground = true }.Start();
        }

        // ---- Connection ----

        void Connect(TvEntry tv)
        {
            Disconnect();
            current = tv;
            Store.LastTv = tv.Fingerprint;
            BuildTvList();
            status.Text = "Connexion à " + tv.Name + "…";
            status.Foreground = Theme.B(Theme.Warn);
            appText.Text = "";
            // The TV may have a new address: take the one its app announces.
            var seen = Hub.SeenNow().FirstOrDefault(s => s.Tv && s.Name == tv.Name);
            if (seen != null && seen.Host != tv.Host) { tv.Host = seen.Host; Store.SaveTv(tv); }
            var r = new AtvRemote(tv);
            remote = r;
            r.Connected += info => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (remote != r) return;
                retryMs = 1000;
                status.Text = "● " + tv.Name;
                status.Foreground = Theme.B(Theme.Ok);
                appText.Text = info;
            }));
            r.AppChanged += pkg => Dispatcher.BeginInvoke(new Action(() =>
            {
                string label;
                if (remote == r) appText.Text = "Ouvert : " + (Apps.TryGetValue(pkg, out label) ? label : pkg);
            }));
            r.PowerChanged += on => Dispatcher.BeginInvoke(new Action(() => { if (remote == r && !on) appText.Text = "En veille"; }));
            r.Disconnected += why => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (remote != r) return;
                status.Text = "Déconnecté — " + why;
                status.Foreground = Theme.B(Theme.Danger);
                if (why.Contains("appairage")) return;
                retry.Interval = TimeSpan.FromMilliseconds(retryMs);
                retryMs = Math.Min(retryMs * 2, 15000);
                retry.Start();
            }));
            r.Connect();
        }

        void Disconnect()
        {
            retry.Stop();
            if (remote != null) { var r = remote; remote = null; r.Close(); }
        }

        public void Key(int code)
        {
            if (remote == null || !remote.IsOpen) { status.Text = current == null ? "Ajoute d'abord une TV" : "Pas connecté — nouvelle tentative…"; return; }
            remote.Key(code);
            if (code == AtvRemote.UP || code == AtvRemote.DOWN || code == AtvRemote.LEFT || code == AtvRemote.RIGHT || code == AtvRemote.CENTER)
                pad.Flash(code);
        }

        /// <summary>Keyboard shortcuts, while the remote page is shown and no text box has focus.</summary>
        public bool OnKey(KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return false;
            int k = 0;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Up: k = AtvRemote.UP; break;
                case System.Windows.Input.Key.Down: k = AtvRemote.DOWN; break;
                case System.Windows.Input.Key.Left: k = AtvRemote.LEFT; break;
                case System.Windows.Input.Key.Right: k = AtvRemote.RIGHT; break;
                case System.Windows.Input.Key.Enter: k = AtvRemote.CENTER; break;
                case System.Windows.Input.Key.Escape:
                case System.Windows.Input.Key.Back: k = AtvRemote.BACK; break;
                case System.Windows.Input.Key.Home: k = AtvRemote.HOME; break;
                case System.Windows.Input.Key.Space: k = AtvRemote.PLAY_PAUSE; break;
                case System.Windows.Input.Key.Add:
                case System.Windows.Input.Key.OemPlus: k = AtvRemote.VOL_UP; break;
                case System.Windows.Input.Key.Subtract:
                case System.Windows.Input.Key.OemMinus: k = AtvRemote.VOL_DOWN; break;
                case System.Windows.Input.Key.M: k = AtvRemote.MUTE; break;
            }
            if (k == 0) return false;
            Key(k);
            return true;
        }

        // ---- Typing: send only what changed (the TV appends what it receives) ----

        void PushText()
        {
            if (remote == null || !remote.IsOpen) return;
            string now = input.Text;
            int p = 0, max = Math.Min(now.Length, lastSent.Length);
            while (p < max && now[p] == lastSent[p]) p++;
            for (int i = lastSent.Length - p; i > 0; i--) remote.Key(AtvRemote.DEL);
            string added = now.Substring(p);
            if (added.Length > 0) remote.Insert(added);
            lastSent = now;
        }

        void Submit()
        {
            if (remote == null || !remote.IsOpen) return;
            remote.Key(AtvRemote.ENTER);
            lastSent = "";      // the TV keeps its text; clearing our box must not send Deletes
            input.Text = "";
        }
    }
}
