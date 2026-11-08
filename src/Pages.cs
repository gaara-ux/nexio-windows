using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Telecommande
{
    /// <summary>Every copy/move and received file, with progress.</summary>
    class TransfersPage : DockPanel
    {
        readonly StackPanel list = new StackPanel();

        public TransfersPage()
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var clear = Ui.Btn(Ui.IconText("", "Effacer les terminés", 13), (s, e) => { Transfers.ClearFinished(); Build(); });
            DockPanel.SetDock(clear, Dock.Right);
            head.Children.Add(clear);
            head.Children.Add(Ui.Text("Les copies en cours et terminées, et les fichiers reçus du téléphone ou de la TV.", 14, Theme.Muted));
            DockPanel.SetDock(head, Dock.Top);
            Children.Add(head);
            Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) => { if (Transfers.Changed) { Transfers.Changed = false; Build(); } };
            timer.Start();
            Build();
        }

        void Build()
        {
            list.Children.Clear();
            var all = Transfers.Snapshot();
            if (all.Count == 0)
            {
                list.Children.Add(Ui.Card(Ui.Text("Rien pour l'instant. Dans Fichiers, choisis des fichiers puis une flèche Copier, ou glisse-les depuis l'Explorateur.", 14, Theme.Muted)));
                return;
            }
            foreach (var t in all) list.Children.Add(Item(t));
        }

        UIElement Item(Transfer t)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var color = t.State == "failed" ? Theme.Danger : t.State == "done" ? Theme.Ok : Theme.Accent;
            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Theme.B(color), VerticalAlignment = VerticalAlignment.Top };
            badge.Child = Ui.Icon(t.Sending ? "" : "", 17, System.Windows.Media.Colors.White);
            g.Children.Add(badge);

            var texts = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
            texts.Children.Add(Ui.Text((t.Sending ? "Copie vers " : "Reçu de ") + t.Peer, 15, Theme.Text, true));
            string files = t.Done + "/" + t.Count + " fichier" + (t.Count > 1 ? "s" : "");
            string state = t.State == "done" ? "Terminé  ·  " + files + "  ·  " + Ui.Size(t.Total)
                : t.State == "failed" ? "Échec : " + t.Error
                : t.State == "canceled" ? "Annulé  ·  " + files
                : t.Percent + " %  ·  " + files + "  ·  " + Ui.Size(t.Speed) + "/s";
            texts.Children.Add(Ui.Text(state, 13, t.State == "failed" ? Theme.Danger : Theme.Muted));
            if (t.State == "running")
            {
                var bar = new ProgressBar { Maximum = 100, Value = t.Percent, Margin = new Thickness(0, 10, 0, 0) };
                texts.Children.Add(bar);
            }
            if (!string.IsNullOrEmpty(t.Current))
            {
                var cur = Ui.Text(t.Current, 12, Theme.Muted);
                cur.Margin = new Thickness(0, 6, 0, 0);
                texts.Children.Add(cur);
            }
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            if (t.State == "running" && t.Sending)
            {
                var cancel = Ui.Btn("Annuler", (s, e) => t.Cancel = true);
                cancel.Foreground = Theme.B(Theme.Danger);
                cancel.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(cancel, 2);
                g.Children.Add(cancel);
            }
            var card = Ui.Card(g, 16);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }
    }

    /// <summary>Checks the app's needs on this PC and repairs them.</summary>
    class SystemPage : StackPanel
    {
        readonly Window owner;
        readonly StackPanel rows = new StackPanel();
        readonly TextBlock summary;
        readonly Button fix;
        List<Dep> deps;

        public event Action<bool> Checked; // all required OK?

        public SystemPage(Window owner)
        {
            this.owner = owner;
            summary = Ui.Text("Vérification…", 15, Theme.Muted);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var recheck = Ui.Btn(Ui.IconText("", "Revérifier", 13), (s, e) => Run());
            recheck.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(recheck);
            fix = Ui.Btn(Ui.IconText("", "Tout réparer", 13), (s, e) => FixAll(), "AccentPill");
            buttons.Children.Add(fix);
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            head.Children.Add(summary);
            Children.Add(head);
            Children.Add(rows);
            var note = Ui.Text("« Tout réparer » demande une seule fois l'autorisation administrateur de Windows (pare-feu et réseau). Rien n'est installé en dehors de ce qui est listé ici.", 12, Theme.Muted);
            note.Margin = new Thickness(4, 14, 0, 0);
            Children.Add(note);
        }

        public void Run()
        {
            summary.Text = "Vérification…";
            fix.IsEnabled = false;
            new Thread(() =>
            {
                Deps.Forget();
                var d = Deps.All();
                bool ok = Deps.CheckAll(d);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    deps = d;
                    Build();
                    bool optionalMissing = d.Any(x => x.Optional && !x.Ok);
                    summary.Text = ok ? (optionalMissing ? "Tout le nécessaire est en place. Une option conseillée est désactivée." : "Tout est en place ✓")
                                      : "Il manque des éléments : touche « Tout réparer ».";
                    summary.Foreground = Theme.B(ok ? Theme.Ok : Theme.Warn);
                    fix.IsEnabled = d.Any(x => !x.Ok);
                    var c = Checked;
                    if (c != null) c(ok);
                }));
            }) { IsBackground = true }.Start();
        }

        void Build()
        {
            rows.Children.Clear();
            foreach (var d in deps)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var color = d.Ok ? Theme.Ok : d.Optional ? Theme.Muted : Theme.Warn;
                var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.B(color), VerticalAlignment = VerticalAlignment.Top };
                badge.Child = Ui.Icon(d.Ok ? "" : d.Optional ? "" : "", 15, System.Windows.Media.Colors.White);
                g.Children.Add(badge);
                var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                t.Children.Add(Ui.Text(d.Title + (d.Optional ? "  (option)" : ""), 15, Theme.Text, true));
                t.Children.Add(Ui.Text(d.Detail ?? "", 13, Theme.Muted));
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                var card = Ui.Card(g, 14);
                card.Margin = new Thickness(0, 0, 0, 8);
                rows.Children.Add(card);
            }
        }

        void FixAll()
        {
            if (deps == null) return;
            bool optional = deps.Any(d => d.Optional && !d.Ok) &&
                Ui.Confirm(owner, "Démarrer avec Windows ?", "Conseillé : le PC reste visible pour le téléphone et la TV sans ouvrir l'appli à la main.", "Oui, activer");
            fix.IsEnabled = false;
            summary.Text = "Réparation… (accepte la demande d'autorisation de Windows)";
            var list = deps;
            new Thread(() =>
            {
                string err = Deps.FixAll(list, optional);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err != null) Ui.Info(owner, "Réparation incomplète", err);
                    Run();
                }));
            }) { IsBackground = true }.Start();
        }
    }
}
