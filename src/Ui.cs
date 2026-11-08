using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Telecommande
{
    /// <summary>Helpers to build the UI in code, plus dark dialogs.</summary>
    static class Ui
    {
        public static TextBlock Text(string s, double size = 14, Color? color = null, bool bold = false)
        {
            return new TextBlock
            {
                Text = s, FontSize = size, Foreground = Theme.B(color ?? Theme.Text), FontFamily = Theme.Font,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock Icon(string glyph, double size = 16, Color? color = null)
        {
            return new TextBlock
            {
                Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = Theme.B(color ?? Theme.Text),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        public static StackPanel IconText(string glyph, string text, double size = 14, Color? color = null)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(Icon(glyph, size + 1, color));
            var t = Text(text, size, color);
            t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextWrapping = TextWrapping.NoWrap;
            p.Children.Add(t);
            return p;
        }

        public static Border Card(UIElement child, double pad = 18)
        {
            return new Border { Background = Theme.B(Theme.Surface), CornerRadius = new CornerRadius(18), Padding = new Thickness(pad), Child = child };
        }

        public static Button Btn(object content, RoutedEventHandler click, string style = "Pill", string tip = null)
        {
            var b = new Button { Content = content, Style = (Style)Application.Current.FindResource(style) };
            if (click != null) b.Click += click;
            if (tip != null) b.ToolTip = tip;
            return b;
        }

        public static string Size(long b)
        {
            if (b < 1024) return b + " o";
            double v = b;
            string[] u = { "Ko", "Mo", "Go", "To" };
            int i = -1;
            do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
            return v.ToString(v < 10 ? "0.0" : "0", CultureInfo.GetCultureInfo("fr-FR")) + " " + u[i];
        }

        // ---- Dark title bar (Windows 10 2004+ / 11) ----

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitle(Window w)
        {
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                int on = 1;
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                int caption = 0x14100E; // BGR of #0E1014
                DwmSetWindowAttribute(h, 35, ref caption, 4);
            };
        }

        // ---- Dialogs ----

        static Window Dialog(Window owner, string title, out StackPanel body, out StackPanel buttons)
        {
            var w = new Window
            {
                Title = title, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.Height, Width = 440, ResizeMode = ResizeMode.NoResize,
                Background = Theme.B(Theme.Surface), ShowInTaskbar = false, FontFamily = Theme.Font
            };
            DarkTitle(w);
            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var t = Text(title, 19, Theme.Text, true);
            t.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(t);
            body = new StackPanel();
            root.Children.Add(body);
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            root.Children.Add(buttons);
            w.Content = root;
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            return w;
        }

        /// <summary>Asks for text; returns null when cancelled.</summary>
        public static string Ask(Window owner, string title, string message, string initial = "", string ok = "OK", bool big = false)
        {
            StackPanel body, buttons;
            var w = Dialog(owner, title, out body, out buttons);
            if (!string.IsNullOrEmpty(message))
            {
                var m = Text(message, 14, Theme.Muted);
                m.Margin = new Thickness(0, 0, 0, 12);
                body.Children.Add(m);
            }
            var box = new TextBox { Text = initial ?? "" };
            if (big) { box.FontSize = 28; box.TextAlignment = TextAlignment.Center; }
            body.Children.Add(box);
            string result = null;
            Action accept = () => { result = box.Text; w.Close(); };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) accept(); };
            var cancel = Btn("Annuler", (s, e) => w.Close());
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            buttons.Children.Add(Btn(ok, (s, e) => accept(), "AccentPill"));
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            w.ShowDialog();
            return result;
        }

        public static bool Confirm(Window owner, string title, string message, string ok, bool danger = false)
        {
            StackPanel body, buttons;
            var w = Dialog(owner, title, out body, out buttons);
            body.Children.Add(Text(message, 14, Theme.Muted));
            bool result = false;
            var cancel = Btn("Annuler", (s, e) => w.Close());
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            var go = Btn(ok, (s, e) => { result = true; w.Close(); }, "AccentPill");
            if (danger) go.Style = (Style)Application.Current.FindResource("Pill");
            if (danger) go.Foreground = Theme.B(Theme.Danger);
            buttons.Children.Add(go);
            w.ShowDialog();
            return result;
        }

        public static void Info(Window owner, string title, string message)
        {
            StackPanel body, buttons;
            var w = Dialog(owner, title, out body, out buttons);
            body.Children.Add(Text(message, 14, Theme.Muted));
            buttons.Children.Add(Btn("OK", (s, e) => w.Close(), "AccentPill"));
            w.ShowDialog();
        }

        /// <summary>Choose one option; returns its index or -1.</summary>
        public static int Pick(Window owner, string title, IList<string> options, string message = null)
        {
            StackPanel body, buttons;
            var w = Dialog(owner, title, out body, out buttons);
            if (message != null)
            {
                var m = Text(message, 14, Theme.Muted);
                m.Margin = new Thickness(0, 0, 0, 10);
                body.Children.Add(m);
            }
            int result = -1;
            for (int i = 0; i < options.Count; i++)
            {
                int k = i;
                var b = Btn(new TextBlock { Text = options[i], FontSize = 15, TextWrapping = TextWrapping.Wrap }, (s, e) => { result = k; w.Close(); });
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(0, 0, 0, 6);
                b.Padding = new Thickness(16, 11, 16, 11);
                body.Children.Add(b);
            }
            buttons.Children.Add(Btn("Annuler", (s, e) => w.Close()));
            w.ShowDialog();
            return result;
        }
    }
}
