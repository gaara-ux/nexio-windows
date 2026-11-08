using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telecommande
{
    /// <summary>
    /// A remote bar glued to the side of a scrcpy window: it follows it, minimizes with it and closes
    /// when scrcpy closes. Keys go through the TV remote protocol when the device is a paired TV (it
    /// keeps working when a protected app makes the picture black), otherwise through ADB.
    /// </summary>
    class ScrcpyBar : Window
    {
        const double BarWidth = 196;

        readonly Process proc;
        readonly string serial;
        readonly bool tv;
        readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        readonly TextBlock state;
        readonly DPad pad = new DPad();
        readonly StackPanel body;
        readonly Button fold, dock;
        AtvRemote remote;
        IntPtr target;
        bool folded, moved, dragging;
        Point offset, lastTl;

        public ScrcpyBar(Process proc, string serial, string host, string name, bool tv)
        {
            this.proc = proc;
            this.serial = serial;
            this.tv = tv;
            Title = "Nexio — " + name;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = BarWidth;
            Background = Theme.B(Theme.Bg);
            FontFamily = Theme.Font;
            Visibility = Visibility.Hidden;

            var root = new StackPanel { Margin = new Thickness(10, 10, 10, 12) };

            // Header: drag it to move the bar (it then keeps that place beside scrcpy), fold, re-dock, close.
            var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Glisser pour déplacer · double-clic : recoller à scrcpy" };
            var close = Small("", "Fermer la barre (scrcpy reste ouvert)", (s, e) => Close());
            fold = Small("", "Réduire la barre", (s, e) => Fold(!folded));
            dock = Small("", "Recoller à côté de scrcpy", (s, e) => Redock());
            dock.Visibility = Visibility.Collapsed;
            foreach (var x in new[] { close, fold, dock }) { DockPanel.SetDock(x, Dock.Right); head.Children.Add(x); }
            var grip = Ui.Icon("", 13, Theme.Muted);
            grip.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(grip, Dock.Left);
            head.Children.Add(grip);
            var title = Ui.Text(name, 13, Theme.Text, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap;
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            head.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Redock(); return; }
                dragging = true;
                try { DragMove(); } catch (InvalidOperationException) { return; } finally { dragging = false; }
                offset = new Point(Left - lastTl.X, Top - lastTl.Y);
                moved = true;
                dock.Visibility = Visibility.Visible;
            };
            root.Children.Add(head);

            body = new StackPanel();
            state = Ui.Text("", 11, Theme.Muted);
            state.Margin = new Thickness(0, 2, 0, 8);
            body.Children.Add(state);

            pad.Width = pad.Height = 168;
            pad.Key += Key;
            body.Children.Add(pad);

            if (tv)
            {
                body.Children.Add(Row(B("", AtvRemote.BACK, "Retour"), B("", AtvRemote.HOME, "Accueil"), B("", AtvRemote.MENU, "Menu")));
                body.Children.Add(Row(B("", AtvRemote.REWIND, "Reculer"), B("", AtvRemote.PLAY_PAUSE, "Lecture / pause"), B("", AtvRemote.FORWARD, "Avancer")));
                body.Children.Add(Row(B("", AtvRemote.VOL_DOWN, "Volume −"), B("", AtvRemote.MUTE, "Muet"), B("", AtvRemote.VOL_UP, "Volume +")));
                body.Children.Add(Row(B("", CH_DOWN, "Chaîne −"), B("", TV_INPUT, "Source (HDMI)"), B("", CH_UP, "Chaîne +")));
            }
            else
            {
                // The phone's own three buttons, in Android's order.
                body.Children.Add(Row(B("", AtvRemote.BACK, "Retour"), B("", AtvRemote.HOME, "Accueil"), B("", APP_SWITCH, "Applis récentes")));
                body.Children.Add(Row(B("", AtvRemote.REWIND, "Reculer"), B("", AtvRemote.PLAY_PAUSE, "Lecture / pause"), B("", AtvRemote.FORWARD, "Avancer")));
                body.Children.Add(Row(B("", AtvRemote.VOL_DOWN, "Volume −"), B("", AtvRemote.MUTE, "Muet"), B("", AtvRemote.VOL_UP, "Volume +")));
                body.Children.Add(Row(B("", NOTIFICATIONS, "Notifications"), B("", SCREENSHOT, "Capture d'écran (sur l'appareil)"), B("", AtvRemote.MENU, "Menu")));
            }
            var power = B("", AtvRemote.POWER, "Marche / veille");
            ((TextBlock)power.Content).Foreground = Theme.B(Theme.Danger);
            body.Children.Add(Row(power));
            root.Children.Add(body);
            Content = root;
            SizeToContent = SizeToContent.Height;

            // A paired TV at this address: use the remote protocol (instant, works on protected content).
            var entry = tv ? Store.Tvs().FirstOrDefault(t => t.Host == host) : null;
            if (entry != null)
            {
                remote = new AtvRemote(entry);
                remote.Connected += i => Dispatcher.BeginInvoke(new Action(() => state.Text = "● Télécommande TV"));
                remote.Disconnected += why => Dispatcher.BeginInvoke(new Action(() => state.Text = "○ ADB (télécommande TV : " + why + ")"));
                remote.Connect();
                state.Text = "Connexion…";
            }
            else state.Text = "● ADB" + (tv ? " — appaire la TV dans Nexio pour des touches instantanées" : "");

            follow.Tick += (s, e) => Follow();
            Closed += (s, e) => { follow.Stop(); if (remote != null) remote.Close(); };
        }

        public void Start()
        {
            follow.Start();
        }

        Button Small(string glyph, string tip, RoutedEventHandler click)
        {
            var b = Ui.Btn(Ui.Icon(glyph, 11), click, "Round", tip);
            b.Width = b.Height = 26;
            b.Margin = new Thickness(4, 0, 0, 0);
            return b;
        }

        /// <summary>Folded: only the header stays, so the bar no longer covers the scrcpy window.</summary>
        void Fold(bool on)
        {
            folded = on;
            body.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            ((TextBlock)fold.Content).Text = on ? "" : "";
            fold.ToolTip = on ? "Ouvrir la barre" : "Réduire la barre";
        }

        void Redock()
        {
            moved = false;
            dock.Visibility = Visibility.Collapsed;
            Follow();
        }

        // Keys that are not on the main remote page
        const int CH_UP = 166, CH_DOWN = 167, TV_INPUT = 178, APP_SWITCH = 187, SCREENSHOT = 120, NOTIFICATIONS = 83;

        Button B(string glyph, int key, string tip)
        {
            var b = Ui.Btn(Ui.Icon(glyph, 17), (s, e) => Key(key), "Round", tip);
            b.Width = b.Height = 48;
            b.Margin = new Thickness(4, 0, 4, 0);
            return b;
        }

        static StackPanel Row(params Button[] buttons)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var b in buttons) p.Children.Add(b);
            return p;
        }

        void Key(int code)
        {
            if (remote != null && remote.IsOpen) remote.Key(code);
            else Scrcpy.Key(serial, code);
            if (code == AtvRemote.UP || code == AtvRemote.DOWN || code == AtvRemote.LEFT || code == AtvRemote.RIGHT || code == AtvRemote.CENTER)
                pad.Flash(code);
            // Give the keyboard back to scrcpy, so typing keeps going to the device.
            if (target != IntPtr.Zero) SetForegroundWindow(target);
        }

        // ---- Following the scrcpy window ----

        void Follow()
        {
            if (proc.HasExited) { Close(); return; }
            if (target == IntPtr.Zero || !IsWindow(target))
            {
                proc.Refresh();
                target = proc.MainWindowHandle;
                if (target == IntPtr.Zero) return;
                // Owned by scrcpy: stays just above it, minimizes and restores with it.
                if (!IsVisible) { new WindowInteropHelper(this).Owner = target; Show(); }
            }
            if (IsIconic(target)) { if (IsVisible) Hide(); return; }
            if (!IsVisible) Show();

            if (dragging) return;
            RECT r;
            if (!GetWindowRect(target, out r)) return;
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return;
            var m = src.CompositionTarget.TransformFromDevice;
            Point tl = m.Transform(new Point(r.Left, r.Top)), br = m.Transform(new Point(r.Right, r.Bottom));
            lastTl = tl;
            var work = SystemParameters.WorkArea;
            double left;
            // Moved by hand: keep that place relative to the scrcpy window, on screen.
            if (moved) left = Math.Max(work.Left, Math.Min(tl.X + offset.X, work.Right - BarWidth));
            // Beside the window when there is room (right first), else over its right edge.
            else if (work.Right - br.X >= BarWidth) left = br.X;
            else if (tl.X - work.Left >= BarWidth) left = tl.X - BarWidth;
            else left = Math.Min(br.X, work.Right) - BarWidth;
            double top = Math.Max(work.Top, Math.Min(moved ? tl.Y + offset.Y : tl.Y, work.Bottom - ActualHeight));
            if (Math.Abs(Left - left) > 0.5) Left = left;
            if (Math.Abs(Top - top) > 0.5) Top = top;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // No focus steal: clicking the bar leaves the keyboard with scrcpy.
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }
}
