// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Capture;

/// <summary>Tool order and per-tool shortcuts: used by the Settings page ("Editor tools") and the editor's gear popover.</summary>
internal sealed class ToolOrderPanel : StackPanel
{
    private readonly IAppSettings _app;
    private readonly bool _dark;
    private readonly bool _headline;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private string? _recordingTool;
    private string _message = string.Empty;
    private bool _error;

    public ToolOrderPanel(IAppSettings app, bool dark, bool headline)
    {
        _app = app;
        _dark = dark;
        _headline = headline;
        Rebuild();
    }

    public event Action? Changed;

    private void Themed(TextBlock t, string key, Brush darkBrush)
    {
        if (_dark)
        {
            t.Foreground = darkBrush;
        }
        else
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, key);
        }
    }

    private TextBlock Text(string text, double size, FontWeight weight, bool secondary = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center, FontFamily = UiKit.TextFont };
        Themed(t, secondary ? ThemeKeys.TextSecondary : ThemeKeys.TextPrimary, secondary ? Ed.FgDim : Ed.Fg);
        return t;
    }

    private void Rebuild()
    {
        Children.Clear();
        if (_headline)
        {
            Children.Add(Text(Strings.Screenshot.EditorTools, 15, FontWeights.SemiBold));
            Children[^1].SetValue(MarginProperty, new Thickness(0, 0, 0, 10));
        }

        var toggleRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toggleRow.Children.Add(Text(Strings.Screenshot.ToolShortcuts, 13, FontWeights.Normal));
        var sw = new MacSwitch(_app.Get<bool>(K.ToolShortcutsEnabled), SwitchSize.Small, on =>
        {
            _app.Set(K.ToolShortcutsEnabled, on);
            Rebuild();
            Changed?.Invoke();
        });
        Grid.SetColumn(sw, 1);
        toggleRow.Children.Add(sw);
        Children.Add(toggleRow);
        var cap = Text(Strings.Screenshot.ToolShortcutsCaption, 11, FontWeights.Normal, secondary: true);
        cap.TextWrapping = TextWrapping.Wrap;
        cap.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(cap);

        var order = ToolCatalog.Order(_app);
        var custom = ToolCatalog.CustomKeys(_app);
        var enabled = _app.Get<bool>(K.ToolShortcutsEnabled);
        for (var i = 0; i < order.Count; i++)
        {
            var tool = order[i];
            var index = i;
            var row = new Grid { MinHeight = 26 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = tool.Glyph, FontFamily = UiKit.IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Themed(icon, ThemeKeys.TextSecondary, Ed.FgDim);
            row.Children.Add(icon);
            var title = Text(tool.Title, 12.5, FontWeights.Normal);
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            var col = 2;
            if (custom.ContainsKey(tool.RawId))
            {
                var reset = SmallButton(G.Undo, Strings.Reset, () =>
                {
                    custom.Remove(tool.RawId);
                    ToolCatalog.SaveCustomKeys(_app, custom);
                    Rebuild();
                    Changed?.Invoke();
                });
                Grid.SetColumn(reset, col);
                row.Children.Add(reset);
            }

            col = 3;
            var recorder = KeyBox(tool, index, custom, enabled);
            Grid.SetColumn(recorder, col);
            row.Children.Add(recorder);
            var up = SmallButton(G.ChevronUp, Strings.Screenshot.MoveUp, () => Move(index, -1));
            up.IsEnabled = index > 0;
            Grid.SetColumn(up, 4);
            row.Children.Add(up);
            var down = SmallButton(G.ChevronDown, Strings.Screenshot.MoveDown, () => Move(index, 1));
            down.IsEnabled = index < order.Count - 1;
            Grid.SetColumn(down, 5);
            row.Children.Add(down);
            Children.Add(row);
            if (i < order.Count - 1)
            {
                Children.Add(UiKitDivider());
            }
        }

        _status.Height = 32;
        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.Text = _message;
        if (_error)
        {
            if (_dark)
            {
                _status.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10));
            }
            else
            {
                _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Orange);
            }
        }
        else
        {
            Themed(_status, ThemeKeys.TextSecondary, Ed.FgDim);
        }

        Children.Add(_status);
        var resetAll = _dark
            ? (FrameworkElement)new EdButton(null, Strings.Reset, null, ResetAll, height: 24) { HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = !ToolCatalog.IsDefaultOrder(_app) }
            : UiKit.Button(Strings.Reset, ButtonKind.Bordered, 12, null, ResetAll);
        resetAll.HorizontalAlignment = HorizontalAlignment.Right;
        resetAll.IsEnabled = !ToolCatalog.IsDefaultOrder(_app);
        Children.Add(resetAll);
    }

    private UIElement UiKitDivider()
    {
        var d = new Border { Height = 1, Margin = new Thickness(28, 0, 0, 0) };
        if (_dark)
        {
            d.Background = Ed.PlateStroke;
        }
        else
        {
            d.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
        }

        return d;
    }

    private FrameworkElement SmallButton(string glyph, string tooltip, Action click)
    {
        var b = new EdButton(glyph, null, tooltip, click, iconSize: 10, width: 20, height: 22);
        if (!_dark && b.Glyph is { } g)
        {
            g.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        }

        return b;
    }

    private void ResetAll()
    {
        _app.Set(K.ToolOrder, K.DefaultToolOrder);
        _app.Set(K.ToolShortcuts, string.Empty);
        _message = string.Empty;
        _error = false;
        Rebuild();
        Changed?.Invoke();
    }

    private void Move(int index, int dir)
    {
        var order = ToolCatalog.Order(_app);
        var j = index + dir;
        if (j < 0 || j >= order.Count)
        {
            return;
        }

        (order[index], order[j]) = (order[j], order[index]);
        ToolCatalog.SaveOrder(_app, order);
        Rebuild();
        Changed?.Invoke();
    }

    private FrameworkElement KeyBox(ToolInfo tool, int index, Dictionary<string, Key> custom, bool enabled)
    {
        var recording = _recordingTool == tool.RawId;
        string text;
        if (recording)
        {
            text = "Press keys";
        }
        else if (custom.TryGetValue(tool.RawId, out var k))
        {
            text = ToolCatalog.KeyName(k);
        }
        else
        {
            text = index < 9 ? (index + 1).ToString() : Strings.None;
        }

        var label = Text(text, 11.5, FontWeights.Medium, secondary: !recording);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Width = 86, Height = 22, CornerRadius = new CornerRadius(6), Child = label, Margin = new Thickness(6, 0, 6, 0), Focusable = true, Cursor = Cursors.Hand, Opacity = enabled ? 1 : 0.48 };
        if (_dark)
        {
            box.Background = recording ? Ed.AccentFill : Ed.Faint;
        }
        else
        {
            box.SetResourceReference(Border.BackgroundProperty, recording ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
        }

        box.MouseLeftButtonUp += (_, _) =>
        {
            _recordingTool = tool.RawId;
            _message = Strings.Screenshot.ToolRecording;
            _error = false;
            Rebuild();
            Dispatcher.BeginInvoke(() =>
            {
                foreach (var child in Children.OfType<Grid>())
                {
                    foreach (var b in child.Children.OfType<Border>().Where(x => x.Focusable))
                    {
                        if (Grid.GetColumn(b) == 3 && child.Children.OfType<TextBlock>().Skip(1).FirstOrDefault()?.Text == tool.Title)
                        {
                            b.Focus();
                        }
                    }
                }
            });
        };
        if (recording)
        {
            box.Loaded += (_, _) => box.Focus();
        }

        box.PreviewKeyDown += (_, e) =>
        {
            if (_recordingTool != tool.RawId)
            {
                return;
            }

            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                return;
            }

            Record(tool, index, key, Keyboard.Modifiers);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (_recordingTool == tool.RawId)
            {
                _recordingTool = null;
                _message = string.Empty;
                Dispatcher.BeginInvoke(Rebuild);
            }
        };
        return box;
    }

    private void Record(ToolInfo tool, int index, Key key, ModifierKeys mods)
    {
        var order = ToolCatalog.Order(_app);
        var custom = ToolCatalog.CustomKeys(_app);
        _error = false;
        _recordingTool = null;
        if (key == Key.Escape)
        {
            _message = string.Empty;
        }
        else if (key is Key.Delete or Key.Back)
        {
            custom.Remove(tool.RawId);
            ToolCatalog.SaveCustomKeys(_app, custom);
            if (index < 9)
            {
                order.RemoveAt(index);
                order.Insert(Math.Min(9, order.Count), tool);
                ToolCatalog.SaveOrder(_app, order);
            }

            _message = string.Empty;
        }
        else if (ToolCatalog.DigitOf(key) is var digit && digit > 0)
        {
            custom.Remove(tool.RawId);
            ToolCatalog.SaveCustomKeys(_app, custom);
            order.RemoveAt(index);
            order.Insert(Math.Min(digit - 1, order.Count), tool);
            ToolCatalog.SaveOrder(_app, order);
            _message = string.Empty;
        }
        else
        {
            var name = ToolCatalog.KeyName(key);
            var primary = mods.HasFlag(ModifierKeys.Control);
            string? reason = null;
            if (ToolCatalog.IsReserved(key) || primary && ToolCatalog.IsCtrlReserved(key) || primary)
            {
                reason = Strings.Screenshot.ToolReserved;
            }
            else if (custom.FirstOrDefault(kv => kv.Value == key && kv.Key != tool.RawId) is { Key: not null } other)
            {
                reason = $"This shortcut is already used by {ToolCatalog.ByRaw(other.Key)?.Title}.";
            }

            if (reason is not null)
            {
                _message = $"{tool.Title} · {name}\n{reason}";
                _error = true;
            }
            else
            {
                custom[tool.RawId] = key;
                ToolCatalog.SaveCustomKeys(_app, custom);
                _message = string.Empty;
            }
        }

        Rebuild();
        Changed?.Invoke();
    }
}
