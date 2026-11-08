using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Telecommande
{
    /// <summary>Two panes side by side (this PC, phone, TV...): copy, move, rename, delete, new folder, drag and drop.</summary>
    class FilesPage : Grid
    {
        readonly Window owner;
        readonly FilePane left, right;

        public FilesPage(Window owner)
        {
            this.owner = owner;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var peers = Store.Peers();
            left = new FilePane(this, null);
            right = new FilePane(this, peers.FirstOrDefault());
            Children.Add(left);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            mid.Children.Add(Arrow("Copier", "", (s, e) => Transfer(left, right, false), "Copier la sélection de gauche vers le dossier de droite"));
            mid.Children.Add(Arrow("Copier", "", (s, e) => Transfer(right, left, false), "Copier la sélection de droite vers le dossier de gauche"));
            var gap = new Border { Height = 18 };
            mid.Children.Add(gap);
            mid.Children.Add(Arrow("Déplacer", "", (s, e) => Transfer(left, right, true), "Déplacer de gauche à droite"));
            mid.Children.Add(Arrow("Déplacer", "", (s, e) => Transfer(right, left, true), "Déplacer de droite à gauche"));
            Grid.SetColumn(mid, 1);
            Children.Add(mid);

            Grid.SetColumn(right, 2);
            Children.Add(right);

            Hub.Changed += () => Dispatcher.BeginInvoke(new Action(() => { left.BuildDevices(); right.BuildDevices(); }));
        }

        static Button Arrow(string text, string glyph, RoutedEventHandler click, string tip)
        {
            var content = new StackPanel { Orientation = Orientation.Vertical };
            content.Children.Add(Ui.Icon(glyph, 18));
            var t = Ui.Text(text, 12, Theme.Muted);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(t);
            var b = Ui.Btn(content, click, "Pill", tip);
            b.Margin = new Thickness(0, 4, 0, 4);
            b.Padding = new Thickness(12, 10, 12, 10);
            return b;
        }

        public Window Owner { get { return owner; } }
        public FilePane Other(FilePane p) { return p == left ? right : left; }
        public void ReloadBoth() { left.Reload(); right.Reload(); }

        void Transfer(FilePane src, FilePane dst, bool move)
        {
            var picked = src.Picked();
            if (picked.Count == 0) { Ui.Info(owner, move ? "Déplacer" : "Copier", "Choisis d'abord des fichiers dans le panneau de " + (src == left ? "gauche" : "droite") + "."); return; }
            if (dst.Dir == "" && dst.Device == null) { Ui.Info(owner, "Choisis un dossier", "Ouvre un dossier de destination dans l'autre panneau (Téléchargements, Documents, C:…)."); return; }
            if (Client.Same(src.Device, dst.Device) && src.Dir == dst.Dir) { Ui.Info(owner, "Même dossier", "Les deux panneaux montrent le même dossier."); return; }
            string what = picked.Count == 1 ? "« " + picked[0].Name + " »" : picked.Count + " éléments";
            if (!Ui.Confirm(owner, move ? "Déplacer" : "Copier", what + "\n\nvers  " + dst.Place, move ? "Déplacer" : "Copier")) return;
            Client.Run(src.Device, src.Dir, picked, dst.Device, dst.Dir, move, () => Dispatcher.BeginInvoke(new Action(ReloadBoth)));
            ((MainWindow)owner).ShowTransfers();
        }

        /// <summary>Files dropped from the Explorer: copy them into the pane's folder.</summary>
        public void DropFiles(FilePane pane, string[] paths)
        {
            if (pane.Device == null && pane.Dir == "") { Ui.Info(owner, "Choisis un dossier", "Ouvre d'abord un dossier dans ce panneau."); return; }
            Uploads.Run(paths, pane.Device, pane.Dir, () => Dispatcher.BeginInvoke(new Action(ReloadBoth)));
            ((MainWindow)owner).ShowTransfers();
        }
    }

    /// <summary>One side: a device, a folder and its content.</summary>
    class FilePane : Grid
    {
        static readonly string[][] Quick = {
            new[] {"Téléchargements", "Download"}, new[] {"Documents", "Documents"}, new[] {"Images", "Pictures"},
            new[] {"Vidéos", "Movies"}, new[] {"Musique", "Music"}, new[] {"Appareil photo", "DCIM"} };

        readonly FilesPage page;
        readonly WrapPanel devices = new WrapPanel();
        readonly TextBlock title, sub, empty;
        readonly ListBox list = new ListBox { SelectionMode = SelectionMode.Extended };
        public Peer Device;
        public string Dir = "";
        List<Entry> items = new List<Entry>();
        int loadId;

        public FilePane(FilesPage page, Peer device)
        {
            this.page = page;
            Device = device;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            devices.Margin = new Thickness(0, 0, 0, 10);
            Children.Add(devices);

            var path = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var up = Ui.Btn(Ui.Icon("", 14), (s, e) => Up(), "Round", "Dossier parent (Retour arrière)");
            up.Width = up.Height = 38;
            path.Children.Add(up);
            var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title = Ui.Text("", 18, Theme.Text, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap;
            sub = Ui.Text("", 12, Theme.Muted);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.TextWrapping = TextWrapping.NoWrap;
            texts.Children.Add(title);
            texts.Children.Add(sub);
            path.Children.Add(texts);
            SetRow(path, 1);
            Children.Add(path);

            var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var root = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = "Racine" };
            root.Click += (s, e) => { root.IsChecked = false; Open(""); };
            root.Margin = new Thickness(0, 0, 6, 6);
            quick.Children.Add(root);
            foreach (var q in Quick)
            {
                var b = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = q[0], Margin = new Thickness(0, 0, 6, 6) };
                string target = q[1];
                b.Click += (s, e) => { b.IsChecked = false; Open(target); };
                quick.Children.Add(b);
            }
            SetRow(quick, 2);
            Children.Add(quick);

            var listCard = new Grid();
            list.ItemTemplate = null;
            list.MouseDoubleClick += (s, e) => OpenSelected();
            list.KeyDown += OnListKey;
            list.AllowDrop = true;
            list.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) page.DropFiles(this, (string[])e.Data.GetData(DataFormats.FileDrop));
            };
            list.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            listCard.Children.Add(list);
            empty = Ui.Text("", 14, Theme.Muted);
            empty.Margin = new Thickness(12);
            empty.IsHitTestVisible = false;
            listCard.Children.Add(empty);
            var card = Ui.Card(listCard, 8);
            SetRow(card, 3);
            Children.Add(card);

            var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            tools.Children.Add(Tool("", "Nouveau dossier", NewFolder));
            tools.Children.Add(Tool("", "Renommer", Rename));
            tools.Children.Add(Tool("", "Supprimer", Delete));
            tools.Children.Add(Tool("", "Actualiser", Reload));
            SetRow(tools, 4);
            Children.Add(tools);

            BuildDevices();
            Open("");
        }

        Button Tool(string glyph, string text, Action act)
        {
            var b = Ui.Btn(Ui.IconText(glyph, text, 13), (s, e) => act());
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Padding = new Thickness(12, 7, 12, 7);
            return b;
        }

        public string DeviceName { get { return Device == null ? "Ce PC" : Device.Name; } }

        public string Place
        {
            get
            {
                if (Dir == "") return DeviceName;
                var parts = Dir.Split('/');
                if (Device == null) parts[0] = Store.AliasLabel(parts[0]);
                return DeviceName + " › " + string.Join(" › ", parts);
            }
        }

        public List<Entry> Picked()
        {
            return list.SelectedItems.Cast<ListBoxItem>().Select(i => (Entry)i.Tag).ToList();
        }

        // ---- Devices ----

        public void BuildDevices()
        {
            devices.Children.Clear();
            devices.Children.Add(DeviceChip("", "Ce PC", Device == null, () => SwitchTo(null)));
            foreach (var p in Store.Peers())
            {
                var peer = p;
                string dot = Hub.Online(p) ? "● " : "○ ";
                var chip = DeviceChip(p.Tv ? "" : "", dot + p.Name, Device != null && Device.Uuid == p.Uuid, () => SwitchTo(peer));
                var menu = new ContextMenu();
                var forget = new MenuItem { Header = "Oublier cet appareil" };
                forget.Click += (s, e) =>
                {
                    if (!Ui.Confirm(page.Owner, "Oublier " + peer.Name + " ?", "Il faudra refaire l'appairage avec un code.", "Oublier", true)) return;
                    Store.RemovePeer(peer.Uuid);
                    if (Device != null && Device.Uuid == peer.Uuid) SwitchTo(null);
                    page.Other(this).BuildDevices();
                    BuildDevices();
                };
                menu.Items.Add(forget);
                chip.ContextMenu = menu;
                devices.Children.Add(chip);
            }
            // Found automatically on the network, not paired yet: one click to pair.
            foreach (var s in Hub.SeenNow().Where(x => Store.Peers().All(p => p.Uuid != x.Uuid)))
            {
                var seen = s;
                var chip = DeviceChip(seen.Tv ? "" : "", "+ " + seen.Name, false, () => PairWith(seen.Host, seen.Name, seen.Port));
                chip.Opacity = 0.75;
                chip.ToolTip = "Trouvé sur le réseau : clic pour appairer";
                devices.Children.Add(chip);
            }
            devices.Children.Add(DeviceChip("", "Appareil", false, AddDevice));
        }

        ToggleButton DeviceChip(string glyph, string text, bool on, Action click)
        {
            var b = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), IsChecked = on, Content = Ui.IconText(glyph, text, 13) };
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Click += (s, e) => { b.IsChecked = on; click(); };
            return b;
        }

        void SwitchTo(Peer p)
        {
            Device = p;
            BuildDevices();
            Open("");
        }

        void AddDevice()
        {
            var found = Hub.SeenNow().Where(s => Store.Peers().All(p => p.Uuid != s.Uuid)).ToList();
            var options = found.Select(s => (s.Tv ? "TV : " : "") + s.Name + "  (" + s.Host + ")").ToList();
            options.Add("Par adresse IP…");
            int k = Ui.Pick(page.Owner, "Appairer un appareil", options, "L'appli Nexio doit être ouverte dessus. Elle affiche un code à 6 chiffres.");
            if (k < 0) return;
            string host, name;
            int port = Store.SharePort;
            if (k < found.Count) { host = found[k].Host; name = found[k].Name; port = found[k].Port; }
            else
            {
                host = Ui.Ask(page.Owner, "Adresse IP de l'appareil", null, "", "Suivant");
                if (string.IsNullOrWhiteSpace(host)) return;
                host = host.Trim();
                name = host;
            }
            PairWith(host, name, port);
        }

        void PairWith(string host, string name, int port)
        {
            string code = Ui.Ask(page.Owner, "Code de " + name, "Tape le code à 6 chiffres affiché sur l'appareil (téléphone : onglet Transferts, TV : appli Nexio).", "", "Appairer", true);
            if (string.IsNullOrWhiteSpace(code)) return;
            new Thread(() =>
            {
                try
                {
                    var p = Client.Pair(host, port, code);
                    Dispatcher.BeginInvoke(new Action(() => { SwitchTo(p); page.Other(this).BuildDevices(); }));
                }
                catch (Exception e)
                {
                    Dispatcher.BeginInvoke(new Action(() => Ui.Info(page.Owner, "Appairage refusé", e.Message)));
                }
            }) { IsBackground = true }.Start();
        }

        // ---- Browsing ----

        public void Reload() { Open(Dir); }

        void Open(string path)
        {
            Dir = path ?? "";
            int id = ++loadId;
            var dev = Device;
            empty.Text = "Chargement…";
            list.Items.Clear();
            Refresh();
            new Thread(() =>
            {
                try
                {
                    var got = Client.List(dev, Dir);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId) return;
                        items = got;
                        Fill();
                        empty.Text = got.Count == 0 ? "Dossier vide. Glisse des fichiers ici depuis l'Explorateur." : "";
                        Refresh();
                    }));
                }
                catch (Exception e)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId) return;
                        empty.Text = e.Message;
                        Refresh();
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        void Refresh()
        {
            string name = Dir == "" ? DeviceName : Dir.Substring(Dir.LastIndexOf('/') + 1);
            if (Device == null && Dir.IndexOf('/') < 0 && Dir != "") name = Store.AliasLabel(Dir);
            title.Text = name;
            sub.Text = Place + "  ·  " + items.Count + " élément" + (items.Count > 1 ? "s" : "");
        }

        void Fill()
        {
            list.Items.Clear();
            foreach (var e in items) list.Items.Add(Row(e));
        }

        ListBoxItem Row(Entry e)
        {
            string glyph; Color color;
            Kind(e, out glyph, out color);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.B(color) };
            badge.Child = Ui.Icon(glyph, 15, Colors.White);
            g.Children.Add(badge);
            var t = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            string label = Device == null && Dir == "" ? Store.AliasLabel(e.Name) : e.Name;
            var n = Ui.Text(label, 14);
            n.TextWrapping = TextWrapping.NoWrap;
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            if (e.Name.StartsWith(".")) n.Opacity = 0.6;
            t.Children.Add(n);
            string s = e.Dir ? (e.Count >= 0 ? e.Count + " élément" + (e.Count > 1 ? "s" : "") : "")
                             : Ui.Size(e.Size) + "  ·  " + Local.FromMs(e.Mtime).ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            t.Children.Add(Ui.Text(s, 11, Theme.Muted));
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return new ListBoxItem { Content = g, Tag = e };
        }

        static void Kind(Entry e, out string glyph, out Color color)
        {
            if (e.Dir) { glyph = e.Name.Length == 2 && e.Name[1] == ':' ? "" : ""; color = Theme.Accent; return; }
            string x = Path.GetExtension(e.Name).ToLowerInvariant();
            Func<string, bool> any = list => list.Split(' ').Contains(x);
            if (any(".jpg .jpeg .png .gif .webp .heic .heif .bmp .svg .tif .tiff")) { glyph = ""; color = Theme.Ok; }
            else if (any(".mp4 .mkv .avi .mov .webm .m4v .ts .wmv .flv .3gp .mpg .mpeg")) { glyph = ""; color = Theme.Danger; }
            else if (any(".mp3 .flac .wav .ogg .m4a .aac .opus .wma .amr")) { glyph = ""; color = Theme.Rgb(0xB06AF0); }
            else if (any(".apk .apks .xapk .apkm .exe .msi")) { glyph = ""; color = Theme.Rgb(0x7CB342); }
            else if (any(".pdf .txt .doc .docx .xls .xlsx .ppt .pptx .odt .ods .csv .md .rtf .json .xml .html")) { glyph = ""; color = Theme.Warn; }
            else if (any(".zip .rar .7z .tar .gz")) { glyph = ""; color = Theme.Rgb(0x6B7280); }
            else { glyph = ""; color = Theme.Rgb(0x6B7280); }
        }

        void Up()
        {
            if (Dir == "") return;
            int i = Dir.LastIndexOf('/');
            Open(i < 0 ? "" : Dir.Substring(0, i));
        }

        void OpenSelected()
        {
            var item = list.SelectedItem as ListBoxItem;
            if (item == null) return;
            var e = (Entry)item.Tag;
            if (e.Dir) { Open(Client.Join(Dir, e.Name)); return; }
            if (Client.IsPackage(e.Name)) { InstallApk(e); return; }
            if (Device == null)
            {
                try { Process.Start(new ProcessStartInfo(Store.Resolve(Client.Join(Dir, e.Name))) { UseShellExecute = true }); }
                catch (Exception ex) { Ui.Info(page.Owner, "Ouverture impossible", ex.Message); }
            }
            else Ui.Info(page.Owner, e.Name, "Ce fichier est sur " + DeviceName + ". Copie-le vers ce PC pour l'ouvrir.");
        }

        /// <summary>An app file: install it on the phone/TV it is on, or (from this PC) send it to one and install it there.</summary>
        void InstallApk(Entry e)
        {
            Window owner = page.Owner;
            string path = Client.Join(Dir, e.Name);
            Peer target;
            string local = null;
            if (Device != null)
            {
                if (!Ui.Confirm(owner, "Installer " + e.Name, "Installer sur " + Device.Name + " ?\n\nAndroid demande de confirmer sur l'écran de " + Device.Name + ".", "Installer")) return;
                target = Device;
            }
            else
            {
                var peers = Store.Peers();
                if (peers.Count == 0) { Ui.Info(owner, e.Name, "Un APK s'installe sur un téléphone ou une TV : appaire d'abord l'appareil (puce « + » en haut)."); return; }
                int k = Ui.Pick(owner, "Installer " + e.Name, peers.Select(p => (Hub.Online(p) ? "● " : "○ ") + p.Name).ToList(),
                    "Sur quel appareil ? Le fichier y est copié (Download/Nexio/APK), puis Android demande de confirmer sur son écran.");
                if (k < 0) return;
                target = peers[k];
                local = Store.Resolve(path);
            }
            var t = Transfers.Start(true, target.Name + " (installation)");
            t.Count = 1;
            t.Total = local == null ? 0 : e.Size;
            t.Current = e.Name;
            new Thread(() =>
            {
                string error = null;
                try
                {
                    if (local == null) Client.Install(target, path); else Client.InstallFromPc(local, target, t);
                    t.Done = 1;
                    t.State = "done";
                }
                catch (Exception ex) { t.State = "failed"; t.Error = ex.Message; error = ex.Message; }
                t.Current = "";
                Transfers.Changed = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error == null) Ui.Info(owner, "Installation lancée", "Confirme l'installation sur l'écran de " + target.Name + ".");
                    else
                    {
                        if (error.Contains("inconnu")) error = target.Name + " ne sait pas encore installer : mets à jour son appli Nexio (3.4 ou plus).";
                        Ui.Info(owner, "Installation impossible", error);
                    }
                    page.ReloadBoth();
                }));
            }) { IsBackground = true }.Start();
        }

        void OnListKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Key == Key.Back) { Up(); e.Handled = true; }
            else if (e.Key == Key.Delete) { Delete(); e.Handled = true; }
            else if (e.Key == Key.F2) { Rename(); e.Handled = true; }
            else if (e.Key == Key.F5) { Reload(); e.Handled = true; }
        }

        // ---- Actions ----

        void RunJob(Action job, string error)
        {
            new Thread(() =>
            {
                try { job(); Dispatcher.BeginInvoke(new Action(page.ReloadBoth)); }
                catch (Exception e) { Dispatcher.BeginInvoke(new Action(() => { Ui.Info(page.Owner, error, e.Message); Reload(); })); }
            }) { IsBackground = true }.Start();
        }

        void NewFolder()
        {
            if (Device == null && Dir == "") { Ui.Info(page.Owner, "Nouveau dossier", "Ouvre d'abord un dossier (Documents, C:…)."); return; }
            string n = Ui.Ask(page.Owner, "Nouveau dossier", "Dans " + Place, "Nouveau dossier", "Créer");
            if (string.IsNullOrWhiteSpace(n) || n.Contains("/") || n.Contains("\\")) return;
            var dev = Device; string path = Client.Join(Dir, n.Trim());
            RunJob(() => Client.Mkdir(dev, path), "Création impossible");
        }

        void Rename()
        {
            var p = Picked();
            if (p.Count != 1) { Ui.Info(page.Owner, "Renommer", "Choisis un seul élément."); return; }
            string n = Ui.Ask(page.Owner, "Renommer", null, p[0].Name, "Renommer");
            if (string.IsNullOrWhiteSpace(n) || n == p[0].Name || n.Contains("/") || n.Contains("\\")) return;
            var dev = Device; string from = Client.Join(Dir, p[0].Name), to = Client.Join(Dir, n.Trim());
            RunJob(() => Client.Rename(dev, from, to), "Renommage impossible");
        }

        void Delete()
        {
            var p = Picked();
            if (p.Count == 0) return;
            string what = p.Count == 1 ? "« " + p[0].Name + " »" : p.Count + " éléments";
            if (!Ui.Confirm(page.Owner, "Supprimer ?", what + " sur " + DeviceName + ".\nC'est définitif (pas de corbeille).", "Supprimer", true)) return;
            var dev = Device; string dir = Dir;
            RunJob(() => { foreach (var e in p) Client.Delete(dev, Client.Join(dir, e.Name)); }, "Suppression impossible");
        }
    }

    /// <summary>Files dropped from the Explorer (absolute paths) sent to a device folder.</summary>
    static class Uploads
    {
        public static void Run(string[] paths, Peer to, string toDir, Action done)
        {
            var t = Transfers.Start(true, to == null ? "ce PC" : to.Name);
            new Thread(() =>
            {
                try
                {
                    var jobs = new List<Tuple<string, string>>();
                    foreach (var p in paths) Expand(p, Client.Join(toDir, Path.GetFileName(p.TrimEnd('\\'))), jobs);
                    t.Count = jobs.Count;
                    t.Total = jobs.Sum(j => new FileInfo(j.Item1).Length);
                    foreach (var j in jobs)
                    {
                        if (t.Cancel) throw new IOException("annulé");
                        t.Current = Path.GetFileName(j.Item1);
                        Transfers.Changed = true;
                        var fi = new FileInfo(j.Item1);
                        using (var input = new FileStream(j.Item1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                            Send(input, fi.Length, Local.ToMs(fi.LastWriteTimeUtc), to, j.Item2, t);
                        t.Done++;
                    }
                    t.State = "done";
                }
                catch (Exception e) { t.State = t.Cancel ? "canceled" : "failed"; t.Error = e.Message; }
                t.Current = "";
                Transfers.Changed = true;
                if (done != null) done();
            }) { IsBackground = true }.Start();
        }

        static void Expand(string abs, string dest, List<Tuple<string, string>> jobs)
        {
            if (Directory.Exists(abs))
            {
                foreach (var f in Directory.GetFileSystemEntries(abs)) Expand(f, Client.Join(dest, Path.GetFileName(f)), jobs);
            }
            else if (File.Exists(abs)) jobs.Add(Tuple.Create(abs, dest));
        }

        static void Send(Stream input, long size, long mtime, Peer to, string dst, Transfer t)
        {
            if (to == null)
            {
                string target = Store.Resolve(dst);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string dest = Local.Unique(target);
                using (var fout = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536)) Hub.Pump(input, fout, size, t);
                File.SetLastWriteTimeUtc(dest, Local.FromMs(mtime));
                return;
            }
            var put = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://" + Hub.HostOf(to) + ":" + to.Port +
                "/api/file?path=" + Uri.EscapeDataString(dst) + "&mtime=" + mtime);
            put.Method = "PUT";
            put.Proxy = null;
            put.KeepAlive = false;
            put.Headers["X-Token"] = to.Token;
            put.AllowWriteStreamBuffering = false;
            put.ContentLength = size;
            put.Timeout = 60000;
            put.ReadWriteTimeout = 60000;
            using (var o = put.GetRequestStream()) Hub.Pump(input, o, size, t);
            using (put.GetResponse()) { }
        }
    }
}
