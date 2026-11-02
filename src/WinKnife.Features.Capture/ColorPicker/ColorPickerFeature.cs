// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal enum ColorFormat
{
    Hex,
    Rgb,
    Hsl,
}

internal static class ColorFormats
{
    public static string Format(Color c, ColorFormat format)
    {
        switch (format)
        {
            case ColorFormat.Rgb:
                return $"rgb({c.R}, {c.G}, {c.B})";
            case ColorFormat.Hsl:
            {
                var (h, s, l) = ToHsl(c);
                return string.Create(CultureInfo.InvariantCulture, $"hsl({h:0}, {s * 100:0}%, {l * 100:0}%)");
            }

            default:
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        return (h * 60, s, l);
    }

    public static ColorFormat Parse(string? text) => text?.ToUpperInvariant() switch
    {
        "RGB" => ColorFormat.Rgb,
        "HSL" => ColorFormat.Hsl,
        _ => ColorFormat.Hex,
    };

    public static Color? FromHex(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Freezes the screen, shows a magnifier loupe and copies the colour under the pointer.</summary>
public sealed class ColorPickerFeature : ToolFeatureBase
{
    private const int HistoryLimit = 12;
    private bool _picking;
    private StackPanel? _historyPanel;

    public override string Id => "color-picker";

    public override string Name => "Color picker";

    public override string Description => "Pick any colour on your screen with a magnifier and copy it as HEX, RGB or HSL.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("pick", "Pick a colour from the screen", () => Safe(() => _ = PickAsync()), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", Hotkey.Parse("Ctrl+Alt+Shift+C"), () => _ = PickAsync());

    protected override void OnStop()
    {
        _historyPanel = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Pick colour shortcut", Hotkey.Parse("Ctrl+Alt+Shift+C"), "Freezes the screen; click to copy, Esc to cancel, arrow keys nudge by one pixel.")
            .AddChoice(
                "format",
                "Copy format",
                new Choice<string>[] { new("HEX", "HEX  #RRGGBB"), new("RGB", "RGB  rgb(r, g, b)"), new("HSL", "HSL  hsl(h, s%, l%)") },
                "HEX");

        _historyPanel = new StackPanel();
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var title = UiKit.Label("Recent colours", null, 13, FontWeights.SemiBold);
        var clear = UiKit.Btn("Clear", () =>
        {
            Ctx.Settings.Set("history", new List<string>());
            RefreshHistory();
        });
        clear.Margin = new Thickness(0, 8, 0, 0);
        _historyPanel.Children.Add(title);
        _historyPanel.Children.Add(wrap);
        _historyPanel.Children.Add(clear);
        RefreshHistory();
        return UiKit.WithExtras(builder.Build(), UiKit.Card(_historyPanel));
    }

    private void RefreshHistory()
    {
        if (_historyPanel is null || Ctx is null)
        {
            return;
        }

        var wrap = _historyPanel.Children.OfType<WrapPanel>().First();
        wrap.Children.Clear();
        var history = Ctx.Settings.Get("history", new List<string>());
        if (history.Count == 0)
        {
            wrap.Children.Add(UiKit.Muted("Nothing picked yet."));
            return;
        }

        foreach (var hex in history)
        {
            if (ColorFormats.FromHex(hex) is not { } color)
            {
                continue;
            }

            var swatch = new Border
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{hex} - click to copy",
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
            swatch.MouseLeftButtonUp += (_, _) => CopyColor(color, addToHistory: false);
            wrap.Children.Add(swatch);
        }
    }

    private async Task PickAsync()
    {
        if (_picking)
        {
            return;
        }

        _picking = true;
        try
        {
            var frame = ScreenGrab.CaptureVirtualScreen();
            var window = new ColorPickerWindow(frame);
            var color = await window.PickAsync();
            if (color is { } picked && IsRunning)
            {
                CopyColor(picked, addToHistory: true);
            }
        }
        catch (Exception ex)
        {
            Ctx.Log.Error("Colour picking failed", ex);
            Ctx.Notifier.Show(Name, ex.Message, NotificationLevel.Error);
        }
        finally
        {
            _picking = false;
        }
    }

    private void CopyColor(Color color, bool addToHistory)
    {
        var format = ColorFormats.Parse(Ctx.Settings.Get("format", "HEX"));
        var text = ColorFormats.Format(color, format);
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn("Clipboard unavailable: " + ex.Message);
        }

        if (addToHistory)
        {
            var hex = ColorFormats.Format(color, ColorFormat.Hex);
            var history = Ctx.Settings.Get("history", new List<string>());
            history.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, hex);
            if (history.Count > HistoryLimit)
            {
                history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);
            }

            Ctx.Settings.Set("history", history);
            RefreshHistory();
        }

        Ctx.Notifier.Show(Name, $"Copied {text}");
    }
}
