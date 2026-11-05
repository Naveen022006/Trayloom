// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>A small dark switch for the island pages.</summary>
internal sealed class IslandToggle : Border
{
    private bool _on;
    private readonly Border _knob = new() { Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };

    public IslandToggle(bool on, Action<bool> changed)
    {
        Width = 34;
        Height = 19;
        CornerRadius = new CornerRadius(9.5);
        Child = _knob;
        _on = on;
        Apply();
        MouseLeftButtonUp += (_, e) =>
        {
            _on = !_on;
            Apply();
            changed(_on);
            e.Handled = true;
        };
    }

    private void Apply()
    {
        Background = _on ? D.Green : D.White(0.2);
        _knob.Margin = new Thickness(_on ? 17 : 2, 0, 0, 0);
    }
}

internal sealed class DownloadsPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };

    public DownloadsPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "downloads";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override bool ScrollsContent => true;

    private DownloadsService S => Core.Downloads;

    private bool NeedsSetup => !S.Enabled || string.IsNullOrEmpty(S.Folder) || S.FolderUnavailable;

    public override double Height(double width, double budget)
    {
        Build();
        return budget;
    }

    public override void Shown()
    {
        S.Changed += OnChanged;
    }

    public override void Hidden() => S.Changed -= OnChanged;

    private void OnChanged() => _view.Dispatcher.BeginInvoke(DispatcherPriority.Background, Build);

    private void Build()
    {
        _view.Children.Clear();
        _leading.Children.Clear();
        if (NeedsSetup)
        {
            _view.Children.Add(SetupView());
            return;
        }

        var menu = PageUi.BandMenu(S.FolderName, G.Folder, () =>
        [
            new IslandMenu.Item(Loc.T("Choose Folder…"), Choose),
            new IslandMenu.Item(Loc.T("Forget Folder"), () => S.Forget()),
        ], () => HasWindow ? Window.HoldOpen() : null, S.FolderName);
        _leading.Children.Add(menu);
        if (S.Items.Count == 0)
        {
            _view.Children.Add(D.EmptyView(G.Download, Loc.T("No files in this folder")));
            return;
        }

        var stack = new StackPanel();
        foreach (var item in S.Items.Take(60))
        {
            stack.Children.Add(Card(item));
        }

        _view.Children.Add(PageUi.VScroll(stack));
    }

    private void Choose()
    {
        using var hold = HasWindow ? Window.HoldOpen() : null;
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.T("Choose the folder where your browser saves downloads. Only that folder is watched."), SelectedPath = Directory.Exists(S.Folder) ? S.Folder : downloads, UseDescriptionForTitle = true };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            S.Choose(dlg.SelectedPath);
        }
    }

    private FrameworkElement SetupView()
    {
        var unavailable = S.FolderUnavailable;
        var installed = Core.Ctx.Runtime.IsAvailable("notch-downloads");
        var enabled = Core.App.Get<bool>(K.DownloadsEnabled);
        var tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(18), Background = D.White(0.05), Child = D.I(unavailable ? G.Warning : G.Download, 26, D.White(0.65)) };
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        string text = unavailable ? Loc.T("This folder is unavailable. Choose it again to restore access.")
            : (!installed && !enabled ? Loc.T("Downloads")
            : (enabled ? Loc.T("Choose the folder where your browser saves downloads. Only that folder is watched.")
            : Loc.T("See files arriving in a folder you choose, directly in the Dynamic Island.")));
        var copy = D.T(text, 12, null, unavailable ? D.Orange : D.White(0.65), true);
        copy.TextAlignment = TextAlignment.Center;
        copy.MaxWidth = 300;
        copy.Margin = new Thickness(0, 12, 0, 12);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360 };
        stack.Children.Add(tile);
        stack.Children.Add(copy);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (!enabled && installed)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(D.T(Loc.T("Downloads"), 12, FontWeights.Medium, D.Text));
            var tg = new IslandToggle(false, on => { Core.App.Set(K.DownloadsEnabled, on); Build(); });
            tg.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(tg);
            actions.Children.Add(row);
        }
        else if (enabled || installed)
        {
            actions.Children.Add(D.PillButton(Loc.T("Choose Folder…"), Choose, true));
            if (unavailable)
            {
                var forget = D.PillButton(Loc.T("Forget Folder"), () => S.Forget());
                forget.Margin = new Thickness(6, 0, 0, 0);
                actions.Children.Add(forget);
            }
        }

        stack.Children.Add(actions);
        return stack;
    }

    private FrameworkElement Card(DownloadItem item)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Background = D.White(0.07), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true };
        var col = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(D.I(item.Completed ? "" : G.Download, 13, item.Completed ? D.Green : D.Text));
        var name = D.T(item.Name, 12, FontWeights.Medium, D.Text, false, TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(8, 0, 0, 0);
        name.MaxWidth = 260;
        nameRow.Children.Add(name);
        if (item.Completed)
        {
            var b = new HoverButton(() => OpenPath(item.Path)) { MinHeight = 28, CornerRadius = new CornerRadius(6), Child = nameRow, Padding = new Thickness(4, 0, 4, 0), Lifts = false, HorizontalAlignment = HorizontalAlignment.Left };
            D.Tip(b, Loc.T("Open"));
            var itemPath = item.Path;
            IslandFileDrag.Attach(b, () => [itemPath], () => HasWindow ? Window.HoldOpen() : null);
            card.MouseRightButtonUp += (_, e) =>
            {
                IslandMenu.Show(card,
                [
                    new IslandMenu.Item(Loc.T("Open"), () => OpenPath(itemPath)),
                    new IslandMenu.Item(Loc.T("Show in folder"), () => Reveal(itemPath)),
                    new IslandMenu.Item(Loc.T("Copy"), () => IslandFileDrag.CopyToClipboard([itemPath])),
                ], () => HasWindow ? Window.HoldOpen() : null);
                e.Handled = true;
            };
            row.Children.Add(b);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(D.IconButton(G.Folder, Loc.T("Show in folder"), () => Reveal(item.Path), size: 26));
            if (Core.Ctx.Runtime.IsAvailable("shelf"))
            {
                var add = D.IconButton("", Loc.T("Files"), () => IslandShelf.Add([item.Path]), size: 26);
                add.Margin = new Thickness(4, 0, 0, 0);
                actions.Children.Add(add);
            }

            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
        }
        else
        {
            row.Children.Add(nameRow);
            if (item.Fraction is { } f)
            {
                var pct = D.Digits($"{(int)Math.Round(f * 100)}%", 11, null, D.Secondary);
                Grid.SetColumn(pct, 1);
                row.Children.Add(pct);
            }
        }

        col.Children.Add(row);
        if (item.Completed)
        {
            col.Children.Add(D.T(Loc.T("Saved"), 11, null, D.Secondary));
        }
        else
        {
            if (item.Fraction is { } f)
            {
                var m = D.MeterBar(f, null, 5);
                m.Margin = new Thickness(0, 6, 0, 4);
                col.Children.Add(m);
            }
            else if (item.Active)
            {
                var sp = PageUi.Spinner(12);
                sp.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Margin = new Thickness(0, 6, 0, 4);
                col.Children.Add(sp);
            }

            var line = new Grid();
            line.Children.Add(D.T(item.Active ? Loc.T("Downloading") : "", 10, null, D.Secondary));
            var size = D.T(item.Bytes > 0 ? FormatBytes(item.Bytes) : Loc.T("Total size unavailable"), 10, null, D.Secondary);
            size.HorizontalAlignment = HorizontalAlignment.Right;
            line.Children.Add(size);
            col.Children.Add(line);
        }

        card.Child = col;
        return card;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] u = ["bytes", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1000 && i < u.Length - 1)
        {
            v /= 1000;
            i++;
        }

        return i == 0 ? $"{bytes} bytes" : $"{v:0.#} {u[i]}";
    }

    internal static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    internal static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
