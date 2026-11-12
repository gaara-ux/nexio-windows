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
using System.Windows.Media.Imaging;

namespace Telecommande
{
    /// <summary>
    /// APK extractor: the apps of a paired phone or TV. Take their APK onto this PC, or send it straight to
    /// another device; also send APK files from this PC to a device.
    /// </summary>
    class AppsPage : Grid
    {
        readonly Window owner;
        readonly WrapPanel devices = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBox search = new TextBox();
        readonly ToggleButton systemChip;
        readonly ListBox list = new ListBox { SelectionMode = SelectionMode.Extended };
        readonly TextBlock empty, status;
        readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>();
        readonly HashSet<string> iconLoading = new HashSet<string>();
        readonly SemaphoreSlim iconGate = new SemaphoreSlim(3);
        Peer device;
        List<AppInfo> all = new List<AppInfo>();
        int loadId;
        bool loading;

        public AppsPage(Window owner)
        {
            this.owner = owner;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Children.Add(devices);

            var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            systemChip = new ToggleButton { Style = (Style)Application.Current.FindResource("Chip"), Content = "Applis système", Margin = new Thickness(10, 0, 0, 0) };
            systemChip.Click += (s, e) => Fill();
            DockPanel.SetDock(systemChip, Dock.Right);
            filters.Children.Add(systemChip);
            var hint = Ui.Text("Chercher une appli ou un package", 14, Theme.Muted);
            hint.IsHitTestVisible = false;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(16, 0, 0, 0);
            search.TextChanged += (s, e) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
            var searchBox = new Grid();
            searchBox.Children.Add(search);
            searchBox.Children.Add(hint);
            filters.Children.Add(searchBox);
            SetRow(filters, 1);
            Children.Add(filters);

            var listCard = new Grid();
            list.MouseDoubleClick += (s, e) => { if (list.SelectedItem != null) ToPc(); };
            list.SelectionChanged += (s, e) => UpdateStatus();
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { ToPc(); e.Handled = true; }
                else if (e.Key == Key.F5) { Load(); e.Handled = true; }
                else if (e.Key == Key.Delete) { Uninstall(); e.Handled = true; }
            };
            listCard.Children.Add(list);
            empty = Ui.Text("", 14, Theme.Muted);
            empty.Margin = new Thickness(12);
            empty.IsHitTestVisible = false;
            listCard.Children.Add(empty);
            var card = Ui.Card(listCard, 8);
            SetRow(card, 2);
            Children.Add(card);

            var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            status = Ui.Text("", 12, Theme.Muted);
            status.Margin = new Thickness(4, 0, 0, 8);
            bottom.Children.Add(status);
            var tools = new WrapPanel();
            tools.Children.Add(Tool("", "Récupérer sur ce PC", ToPc, "AccentPill"));
            tools.Children.Add(Tool("", "Envoyer vers un appareil…", ToDevice));
            tools.Children.Add(Tool("", "Envoyer des APK du PC…", FromPc));
            tools.Children.Add(Tool("", "Désinstaller de l'appareil", Uninstall));
            tools.Children.Add(Tool("", "Ouvrir le dossier APK", OpenFolder));
            tools.Children.Add(Tool("", "Actualiser", Load));
            bottom.Children.Add(tools);
            SetRow(bottom, 3);
            Children.Add(bottom);

            Hub.Changed += () => Dispatcher.BeginInvoke(new Action(BuildDevices));
            device = Store.Peers().FirstOrDefault(p => p.Tv) ?? Store.Peers().FirstOrDefault();
            BuildDevices();
            IsVisibleChanged += (s, e) => { if (IsVisible && all.Count == 0 && !loading) Load(); };
        }

        Button Tool(string glyph, string text, Action act, string style = "Pill")
        {
            var b = Ui.Btn(Ui.IconText(glyph, text, 13), (s, e) => act(), style);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Padding = new Thickness(12, 7, 12, 7);
            return b;
        }

        // ---- Devices ----

        void BuildDevices()
        {
            devices.Children.Clear();
            var peers = Store.Peers();
            if (peers.Count == 0)
            {
                devices.Children.Add(Ui.Text("Aucun appareil appairé. Dans Fichiers, appaire le téléphone ou la TV (le code s'affiche dans leur appli Nexio).", 14, Theme.Muted));
                return;
            }
            if (device != null && peers.All(p => p.Uuid != device.Uuid)) device = null;
            foreach (var p in peers)
            {
                var peer = p;
                var chip = new ToggleButton
                {
                    Style = (Style)Application.Current.FindResource("Chip"),
                    IsChecked = device != null && device.Uuid == p.Uuid,
                    Content = Ui.IconText(p.Tv ? "" : "", (Hub.Online(p) ? "● " : "○ ") + p.Name, 13),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                chip.Click += (s, e) => { chip.IsChecked = true; if (!Client.Same(device, peer)) { device = peer; BuildDevices(); Load(); } };
                devices.Children.Add(chip);
            }
        }

        // ---- List ----

        void Load()
        {
            int id = ++loadId;
            var dev = device;
            all = new List<AppInfo>();
            list.Items.Clear();
            if (dev == null) { empty.Text = Store.Peers().Count == 0 ? "" : "Choisis un appareil."; UpdateStatus(); return; }
            loading = true;
            empty.Text = "Lecture des applis de " + dev.Name + "…";
            UpdateStatus();
            new Thread(() =>
            {
                try
                {
                    var got = Client.Apps(dev);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId) return;
                        loading = false;
                        all = got;
                        icons.Clear();
                        Fill();
                    }));
                }
                catch (Exception e)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId) return;
                        loading = false;
                        string m = e.Message;
                        if (m.Contains("inconnu"))
                            m = dev.Name + " ne sait pas encore lister ses applis : installe-lui la version 3.3 (ou plus) de l'appli Nexio. Un PC n'a pas d'applis Android.";
                        empty.Text = m;
                        UpdateStatus();
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        void Fill()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            bool sys = systemChip.IsChecked == true;
            list.Items.Clear();
            foreach (var a in all)
            {
                if (!sys && a.System) continue;
                if (q.Length > 0 && !a.Label.ToLowerInvariant().Contains(q) && !a.Pkg.ToLowerInvariant().Contains(q)) continue;
                list.Items.Add(Row(a));
            }
            if (!loading) empty.Text = all.Count == 0 ? "" : list.Items.Count == 0 ? "Aucune appli ne correspond." : "";
            UpdateStatus();
        }

        ListBoxItem Row(AppInfo a)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var img = new Image { Width = 36, Height = 36 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            ImageSource src;
            if (icons.TryGetValue(a.Pkg, out src)) img.Source = src; else LoadIcon(a.Pkg, img);
            g.Children.Add(img);
            var t = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var n = Ui.Text(a.Label, 14);
            n.TextWrapping = TextWrapping.NoWrap;
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Children.Add(n);
            var d = Ui.Text(a.Pkg + "  ·  " + (string.IsNullOrEmpty(a.Version) ? "" : a.Version + "  ·  ") + Ui.Size(a.Size)
                + (a.Parts > 1 ? "  ·  " + a.Parts + " morceaux (.apks)" : "") + (a.System ? "  ·  système" : ""), 11, Theme.Muted);
            d.TextWrapping = TextWrapping.NoWrap;
            d.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Children.Add(d);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            if (a.Updated > 0)
            {
                var date = Ui.Text(Local.FromMs(a.Updated).ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), 11, Theme.Muted);
                date.VerticalAlignment = VerticalAlignment.Center;
                date.Margin = new Thickness(12, 0, 8, 0);
                Grid.SetColumn(date, 2);
                g.Children.Add(date);
            }
            return new ListBoxItem { Content = g, Tag = a, ToolTip = a.FileName };
        }

        void LoadIcon(string pkg, Image img)
        {
            var dev = device;
            int id = loadId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                iconGate.Wait();
                try
                {
                    byte[] png = Client.AppIcon(dev, pkg);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(png);
                    bmp.EndInit();
                    bmp.Freeze();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId) return;
                        icons[pkg] = bmp;
                        img.Source = bmp;
                    }));
                }
                catch { }
                finally { iconGate.Release(); }
            });
        }

        List<AppInfo> Picked()
        {
            return list.SelectedItems.Cast<ListBoxItem>().Select(i => (AppInfo)i.Tag).ToList();
        }

        void UpdateStatus()
        {
            var p = Picked();
            if (p.Count > 0)
                status.Text = p.Count + " appli" + (p.Count > 1 ? "s choisies" : " choisie") + "  ·  " + Ui.Size(p.Sum(a => a.Size));
            else if (all.Count > 0)
                status.Text = list.Items.Count + " applis sur " + device.Name + "  ·  double-clic pour récupérer, Ctrl/Maj pour en choisir plusieurs";
            else status.Text = "";
        }

        // ---- Actions ----

        bool Ready(List<AppInfo> picked)
        {
            if (device == null) { Ui.Info(owner, "Applis", "Choisis d'abord un appareil en haut."); return false; }
            if (picked.Count == 0) { Ui.Info(owner, "Applis", "Choisis d'abord une ou plusieurs applis dans la liste."); return false; }
            return true;
        }

        void ToPc()
        {
            var picked = Picked();
            if (!Ready(picked)) return;
            Client.ExtractApks(device, picked, null, null);
            ((MainWindow)owner).ShowTransfers();
        }

        void ToDevice()
        {
            var picked = Picked();
            if (!Ready(picked)) return;
            var targets = Store.Peers().Where(p => p.Uuid != device.Uuid).ToList();
            if (targets.Count == 0) { Ui.Info(owner, "Envoyer vers…", "Aucun autre appareil appairé. Appaire-le dans Fichiers."); return; }
            int k = Ui.Pick(owner, "Envoyer vers…", targets.Select(p => (Hub.Online(p) ? "● " : "○ ") + p.Name).ToList(),
                (picked.Count == 1 ? picked[0].Label : picked.Count + " applis") + " de " + device.Name + ", vers le dossier " + Client.ApkFolderAndroid + " de l'appareil choisi.");
            if (k < 0) return;
            Client.ExtractApks(device, picked, targets[k], null);
            ((MainWindow)owner).ShowTransfers();
        }

        void FromPc()
        {
            var peers = Store.Peers();
            if (peers.Count == 0) { Ui.Info(owner, "Envoyer des APK", "Aucun appareil appairé. Appaire-le dans Fichiers."); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "APK à envoyer", Multiselect = true,
                Filter = "Applis Android (*.apk;*.apks;*.xapk;*.apkm)|*.apk;*.apks;*.xapk;*.apkm|Tous les fichiers|*.*"
            };
            if (Directory.Exists(Client.ApkFolderPc)) dlg.InitialDirectory = Client.ApkFolderPc;
            if (dlg.ShowDialog(owner) != true) return;
            int k = Ui.Pick(owner, "Envoyer vers…", peers.Select(p => (Hub.Online(p) ? "● " : "○ ") + p.Name).ToList(),
                dlg.FileNames.Length + " fichier" + (dlg.FileNames.Length > 1 ? "s" : "") + ", vers le dossier " + Client.ApkFolderAndroid + " de l'appareil choisi.");
            if (k < 0) return;
            Uploads.Run(dlg.FileNames, peers[k], Client.ApkFolderAndroid, null);
            ((MainWindow)owner).ShowTransfers();
        }

        /// <summary>Asks the device to uninstall the chosen apps; Android shows its confirmation on the device's screen.</summary>
        void Uninstall()
        {
            var picked = Picked();
            if (!Ready(picked)) return;
            var ok = picked.Where(a => !a.System).ToList();
            int sys = picked.Count - ok.Count;
            if (ok.Count == 0) { Ui.Info(owner, "Désinstaller", "Les applis système ne peuvent pas être désinstallées : Android ne le permet pas."); return; }
            var dev = device;
            string names = ok.Count == 1 ? ok[0].Label : ok.Count + " applis";
            string msg = "Android va demander de confirmer " + (ok.Count == 1 ? "" : "chacune ") + "sur l'écran de " + dev.Name
                + " : prends la télécommande ou le téléphone pour répondre."
                + (sys > 0 ? "\n\n" + sys + " appli" + (sys > 1 ? "s" : "") + " système ignorée" + (sys > 1 ? "s" : "") + " : Android ne permet pas de les désinstaller." : "");
            if (!Ui.Confirm(owner, "Désinstaller " + names + " de " + dev.Name + " ?", msg, "Désinstaller", true)) return;
            status.Text = "Demande envoyée à " + dev.Name + "…";
            new Thread(() =>
            {
                string error = null;
                foreach (var a in ok)
                {
                    try { Client.Uninstall(dev, a.Pkg); }
                    catch (Exception e)
                    {
                        string m = e.Message;
                        if (m.Contains("inconnu")) m = dev.Name + " a une ancienne version de Nexio : installe-lui la 3.9 (ou plus) pour désinstaller à distance.";
                        error = a.Label + " : " + m;
                        break;
                    }
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error != null) Ui.Info(owner, "Désinstaller", error);
                    else status.Text = "Confirme sur " + dev.Name + ", puis clique « Actualiser » (F5).";
                }));
            }) { IsBackground = true }.Start();
        }

        void OpenFolder()
        {
            Directory.CreateDirectory(Client.ApkFolderPc);
            Process.Start(new ProcessStartInfo(Client.ApkFolderPc) { UseShellExecute = true });
        }
    }
}
