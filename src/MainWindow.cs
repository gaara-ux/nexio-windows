using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Telecommande
{
    class MainWindow : Window
    {
        readonly Grid content = new Grid();
        readonly TextBlock pageTitle, codeText, ipText, warnText;
        readonly Border banner;
        readonly RadioButton[] nav = new RadioButton[6];
        readonly UIElement[] pages = new UIElement[6];
        readonly RemotePage remote;
        readonly SystemPage system;

        public MainWindow(bool minimized)
        {
            Title = "Nexio";
            Width = 1280; Height = 820; MinWidth = 1060; MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.B(Theme.Bg);
            FontFamily = Theme.Font;
            Icon = AppIcon();
            if (minimized) WindowState = WindowState.Minimized;
            Ui.DarkTitle(this);

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- Sidebar ----
            var side = new DockPanel { Background = Theme.B(Theme.Side) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 26, 18, 26) };
            brand.Children.Add(new Image { Source = Icon, Width = 40, Height = 40 });
            var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.Text("Nexio", 18, Theme.Text, true));
            names.Children.Add(Ui.Text("pour Windows", 12, Theme.Muted));
            brand.Children.Add(names);
            DockPanel.SetDock(brand, Dock.Top);
            side.Children.Add(brand);

            // Bottom: this PC's pairing code.
            var me = new StackPanel();
            me.Children.Add(Ui.Text("CE PC", 11, Theme.Muted, true));
            me.Children.Add(Ui.Text(Store.Name, 15, Theme.Text, true));
            ipText = Ui.Text("", 12, Theme.Muted);
            me.Children.Add(ipText);
            var codeLabel = Ui.Text("Code d'appairage", 11, Theme.Muted);
            codeLabel.Margin = new Thickness(0, 12, 0, 0);
            me.Children.Add(codeLabel);
            codeText = Ui.Text("", 28, Theme.Text, true);
            codeText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            me.Children.Add(codeText);
            me.Children.Add(Ui.Text("À taper sur le téléphone : Fichiers › Appareil.", 11, Theme.Muted));
            warnText = Ui.Text("", 11, Theme.Danger);
            me.Children.Add(warnText);
            var meCard = Ui.Card(me, 16);
            meCard.Margin = new Thickness(14, 0, 14, 18);
            DockPanel.SetDock(meCard, Dock.Bottom);
            side.Children.Add(meCard);

            var navPanel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
            string[][] items = { new[] { "", "Télécommande" }, new[] { "", "Fichiers" }, new[] { "", "Applis" }, new[] { "\uE7F4", "Écran" }, new[] { "", "Transferts" }, new[] { "", "Système" } };
            for (int i = 0; i < items.Length; i++)
            {
                int k = i;
                nav[i] = new RadioButton { Style = (Style)FindResource("Nav"), GroupName = "nav", Content = Ui.IconText(items[i][0], items[i][1], 15, null) };
                nav[i].Checked += (s, e) => Show(k);
                navPanel.Children.Add(nav[i]);
            }
            side.Children.Add(navPanel);
            root.Children.Add(side);

            // ---- Content ----
            var right = new DockPanel { Margin = new Thickness(36, 28, 36, 28) };
            pageTitle = Ui.Text("", 30, Theme.Text, true);
            pageTitle.Margin = new Thickness(0, 0, 0, 14);
            DockPanel.SetDock(pageTitle, Dock.Top);
            right.Children.Add(pageTitle);

            var bannerRow = new DockPanel();
            var bannerFix = Ui.Btn("Voir et réparer", (s, e) => nav[5].IsChecked = true, "AccentPill");
            DockPanel.SetDock(bannerFix, Dock.Right);
            bannerRow.Children.Add(bannerFix);
            bannerRow.Children.Add(Ui.IconText("", "Il manque des réglages sur ce PC (pare-feu, réseau…) : le téléphone et la TV pourraient ne pas le voir.", 13, Theme.Warn));
            banner = new Border { Background = Theme.B(Theme.Rgb(0x2A2414)), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 10, 10, 10), Child = bannerRow, Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed };
            DockPanel.SetDock(banner, Dock.Top);
            right.Children.Add(banner);
            right.Children.Add(content);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            Content = root;

            remote = new RemotePage(this);
            system = new SystemPage(this);
            system.Checked += ok => banner.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            pages[0] = remote;
            pages[1] = new FilesPage(this);
            pages[2] = new AppsPage(this);
            pages[3] = new ScreenPage(this);
            pages[4] = new TransfersPage();
            pages[5] = new ScrollViewer { Content = system, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            foreach (var p in pages) { p.Visibility = Visibility.Collapsed; content.Children.Add(p); }

            Hub.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateMe));
            UpdateMe();
            PreviewKeyDown += (s, e) => { if (pages[0].Visibility == Visibility.Visible && remote.OnKey(e)) e.Handled = true; };
            nav[0].IsChecked = true;
            system.Run();
        }

        void UpdateMe()
        {
            codeText.Text = Hub.Code;
            ipText.Text = Hub.LocalIp();
            warnText.Text = Hub.ServerError == null ? "" : "Partage indisponible : " + Hub.ServerError;
        }

        void Show(int k)
        {
            string[] titles = { "Télécommande", "Fichiers", "Applis", "Écran", "Transferts", "Système" };
            pageTitle.Text = titles[k];
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == k ? Visibility.Visible : Visibility.Collapsed;
            if (k == 5) system.Run();
            Keyboard.ClearFocus();
        }

        public void ShowTransfers() { nav[4].IsChecked = true; }

        /// <summary>The remote icon, drawn (same as the phone app's).</summary>
        static ImageSource AppIcon()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Theme.B(Theme.Surface), null, new RectangleGeometry(new Rect(0, 0, 108, 108), 24, 24)));
            g.Children.Add(new GeometryDrawing(Theme.B(Theme.Text), null, new RectangleGeometry(new Rect(34, 22, 40, 66), 9, 9)));
            g.Children.Add(new GeometryDrawing(Theme.B(Theme.Accent), null, new EllipseGeometry(new Point(54, 44), 13, 13)));
            g.Children.Add(new GeometryDrawing(Theme.B(Theme.Text), null, new EllipseGeometry(new Point(54, 44), 5, 5)));
            foreach (var p in new[] { new Point(45, 68), new Point(63, 68), new Point(45, 79), new Point(63, 79) })
                g.Children.Add(new GeometryDrawing(Theme.B(Theme.Surface), null, new EllipseGeometry(p, 3.5, 3.5)));
            // Window icons must be bitmaps: render the drawing.
            var img = new DrawingImage(g);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawImage(img, new Rect(0, 0, 128, 128));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return System.Windows.Media.Imaging.BitmapFrame.Create(bmp);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var single = new Mutex(true, "Nexio-Windows-" + Environment.UserName, out created))
            {
                if (!created) { MessageBox.Show("Nexio est déjà ouverte.", "Nexio"); return; }
                Deps.MigrateAutostart();
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    try { File.AppendAllText(Path.Combine(Store.Dir, "erreurs.log"), DateTime.Now + "  " + e.Exception + "\r\n\r\n"); } catch { }
                    MessageBox.Show(e.Exception.Message, "Nexio — erreur");
                    e.Handled = true;
                };
                Theme.Apply(app);
                Hub.Start();
                Mdns.Start();
                app.Run(new MainWindow(args.Contains("--minimized")));
            }
        }
    }
}
