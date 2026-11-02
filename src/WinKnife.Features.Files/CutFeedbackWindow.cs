// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;
using Orientation = System.Windows.Controls.Orientation;

namespace WinKnife.Features.Files;

/// <summary>One file or folder held for a move.</summary>
internal sealed record CutItem(string Path, string Name, ImageSource? Icon);

/// <summary>
/// The floating cut panel (<c>CutFeedbackView</c>): 286 wide, anchored top-centre of the display with the pointer, 14 px below
/// the work area top. Shows what is cut, the progress of a move that leaves its volume, and the "Moved!" result.
/// </summary>
internal sealed class CutFeedbackWindow : OverlayWindow
{
    private const double PanelWidth = 286;
    private const int MaxRows = 5;
    private readonly Action _cancel;
    private readonly Border _frame;
    private bool _hiding;

    public CutFeedbackWindow(Action cancel)
    {
        _cancel = cancel;
        Width = PanelWidth + 40;
        SizeToContent = SizeToContent.Height;
        _frame = new Border
        {
            Width = PanelWidth,
            Margin = new Thickness(20),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.32, Direction = 270 },
        };
        _frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        _frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        Content = _frame;
    }

    public void ShowMarked(IReadOnlyList<CutItem> items)
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var scissors = UiKit.Icon(G.Scissors, 13, ThemeKeys.Orange, FontWeights.SemiBold);
        scissors.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(scissors);
        var title = UiKit.Text(Strings.CutPaste.ReadyTitle, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var count = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(7, 2, 7, 2), Child = UiKit.Text(items.Count.ToString(), 12, FontWeights.Bold, ThemeKeys.Orange), Margin = new Thickness(0, 0, 8, 0) };
        count.SetResourceReference(Border.BackgroundProperty, ThemeKeys.OrangeFill);
        Grid.SetColumn(count, 2);
        header.Children.Add(count);
        var cancel = Ui.IconButton(G.CloseCircle, Strings.CutPaste.Cancel, () => _cancel(), 20, 14, 10, filled: false);
        Grid.SetColumn(cancel, 3);
        header.Children.Add(cancel);
        stack.Children.Add(header);

        var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var item in items.Take(MaxRows))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(new Image { Source = item.Icon, Width = 17, Height = 17 });
            var name = UiKit.Text(item.Name, 12, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.MaxWidth = 220;
            name.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(name);
            rows.Children.Add(row);
        }

        if (items.Count > MaxRows)
        {
            var more = UiKit.Text("+" + (items.Count - MaxRows), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            more.Margin = new Thickness(24, 0, 0, 5);
            rows.Children.Add(more);
        }

        stack.Children.Add(rows);
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hint.Children.Add(SettingsKit.KeyCap("Ctrl", 10.5));
        var vCap = SettingsKit.KeyCap("V", 10.5);
        vCap.Margin = new Thickness(0, 0, 6, 0);
        hint.Children.Add(vCap);
        var hintText = UiKit.Text(Strings.CutPaste.ReadyHint, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        hintText.VerticalAlignment = VerticalAlignment.Center;
        hint.Children.Add(hintText);
        stack.Children.Add(hint);
        SetBody(stack);
    }

    public void ShowMoving(string currentName, int completed, int total, double? fraction)
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var arrow = UiKit.Icon(G.Forward, 13, ThemeKeys.Accent, FontWeights.SemiBold);
        arrow.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(arrow);
        var title = UiKit.Text(Strings.CutPaste.MovingTitle, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        if (total > 1)
        {
            var position = Math.Min(Math.Max(completed, 0) + 1, Math.Max(total, 1));
            var counter = UiKit.Text(string.Format(Strings.CutPaste.MovingCountFormat, position, total), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            Grid.SetColumn(counter, 2);
            header.Children.Add(counter);
        }

        stack.Children.Add(header);
        FrameworkElement bar;
        if (fraction is { } value)
        {
            bar = new UsageBar(Math.Clamp(value, 0, 1), ThemeKeys.Accent, 6);
        }
        else
        {
            var indeterminate = new ProgressBar { IsIndeterminate = true, Height = 6 };
            bar = indeterminate;
        }

        bar.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(bar);
        var name = UiKit.Text(currentName, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(name);
        SetBody(stack);
    }

    public void ShowResult(int moved, int failed)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ok = failed == 0;
        var icon = UiKit.Icon(ok ? G.CheckCircle : G.Warning, 20, ok ? ThemeKeys.Green : ThemeKeys.Orange);
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(icon);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(Strings.CutPaste.DoneTitle, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var summary = UiKit.Text(moved == 1 ? Strings.CutPaste.MovedSingular : string.Format(Strings.CutPaste.MovedPluralFormat, moved), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        summary.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(summary);
        if (failed > 0)
        {
            var some = UiKit.Text(Strings.CutPaste.SomeFailed, 11, FontWeights.Normal, ThemeKeys.Orange);
            some.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(some);
        }

        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        SetBody(row);
    }

    private void SetBody(UIElement body)
    {
        _frame.Child = body;
        Reveal();
    }

    private void Reveal()
    {
        _hiding = false;
        var wasVisible = IsVisible;
        var cursor = Screens.CursorPosition();
        var area = Screens.FromPoint(cursor);
        var scale = WinUtil.PrepareOnMonitorAt(this, (int)cursor.X, (int)cursor.Y);
        if (!wasVisible)
        {
            Opacity = 0;
            ShowNoActivate();
        }

        var width = WinUtil.DesiredSize(this).Width * scale;
        var x = (int)(area.WorkArea.Left + (area.WorkArea.Width - width) / 2);
        var y = (int)(area.WorkArea.Top + 14 * scale - 20 * scale);
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        if (!wasVisible || Opacity < 1)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.18)));
        }
    }

    /// <summary>Fades out and hides.</summary>
    public void HideHud()
    {
        if (!IsVisible || _hiding)
        {
            return;
        }

        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.18));
        fade.Completed += (_, _) =>
        {
            if (_hiding)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
