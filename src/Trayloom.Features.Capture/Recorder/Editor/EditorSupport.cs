// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>
/// Editor preview sound: the two sidecar tracks played with their keep flags and gains, following the video clock (the take's own
/// MP4 has no audio). The preview always runs at 1x; export speed is applied only on export.
/// </summary>
internal sealed class PreviewAudio : IDisposable
{
    private readonly MediaPlayer? _system;
    private readonly MediaPlayer? _mic;
    private bool _playing;

    public PreviewAudio(Take take)
    {
        if (take.HasSystemAudio)
        {
            _system = Open(take.SystemWav);
        }

        if (take.HasMicrophone)
        {
            _mic = Open(take.MicWav);
        }
    }

    public bool HasAudio => _system is not null || _mic is not null;

    private static MediaPlayer? Open(string path)
    {
        try
        {
            var p = new MediaPlayer { ScrubbingEnabled = true };
            p.MediaFailed += (_, e) => CaptureHost.Log("preview audio failed: " + e.ErrorException?.Message);
            p.Open(new Uri(path));
            p.Pause();
            return p;
        }
        catch
        {
            return null;
        }
    }

    public void Apply(EditDocument doc)
    {
        if (_system is not null)
        {
            _system.Volume = doc.KeepsSystemAudio ? Math.Clamp(doc.SystemAudioGain, 0, 1) : 0;
        }

        if (_mic is not null)
        {
            _mic.Volume = doc.KeepsMicrophone ? Math.Clamp(doc.MicrophoneGain, 0, 1) : 0;
        }
    }

    public void Seek(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (_system is not null)
        {
            _system.Position = t;
        }

        if (_mic is not null)
        {
            _mic.Position = t;
        }
    }

    public void Play()
    {
        _playing = true;
        _lastSync = Environment.TickCount64;
        _system?.Play();
        _mic?.Play();
    }

    public void Pause()
    {
        _playing = false;
        _system?.Pause();
        _mic?.Pause();
    }

    private long _lastSync;

    /// <summary>Re-aligns the players when they drift from the video clock by more than 0.25 s (at most twice a second, so a seek can settle).</summary>
    public void Follow(double videoSeconds)
    {
        if (!_playing || Environment.TickCount64 - _lastSync < 500)
        {
            return;
        }

        foreach (var p in new[] { _system, _mic })
        {
            if (p is not null && Math.Abs(p.Position.TotalSeconds - videoSeconds) > 0.25)
            {
                p.Position = TimeSpan.FromSeconds(videoSeconds);
                _lastSync = Environment.TickCount64;
            }
        }
    }

    public void Dispose()
    {
        _system?.Close();
        _mic?.Close();
    }
}

/// <summary>A named visual starting point: look values plus the pictures (copied into a private store). Cuts, captions, blurs and sound stay with the recording.</summary>
internal sealed class EditPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Backdrop { get; set; } = string.Empty;

    public string Aspect { get; set; } = "original";

    public bool ShowsPointer { get; set; } = true;

    public string PointerSmoothing { get; set; } = "smooth";

    public double PointerSize { get; set; } = 1;

    public bool ShowsClickRing { get; set; } = true;

    public bool ZoomEnabled { get; set; } = true;

    public double ZoomAmount { get; set; } = 1.8;

    public List<ImageOverlay>? Images { get; set; }

    public static EditPreset From(string name, EditDocument d) => new()
    {
        Name = name, Backdrop = d.Backdrop, Aspect = d.Aspect, ShowsPointer = d.ShowsPointer, PointerSmoothing = d.PointerSmoothing, PointerSize = d.PointerSize,
        ShowsClickRing = d.ShowsClickRing, ZoomEnabled = d.ZoomEnabled, ZoomAmount = d.ZoomAmount, Images = d.Images.Select(i => i.Copy()).ToList(),
    };

    /// <summary>Applies the preset to a document; pictures are restored as fresh copies running for the whole recording.</summary>
    public void ApplyTo(EditDocument d, Take take)
    {
        d.Backdrop = Backdrop;
        d.Aspect = Aspect;
        d.ShowsPointer = ShowsPointer;
        d.PointerSmoothing = PointerSmoothing;
        d.PointerSize = PointerSize;
        d.ShowsClickRing = ShowsClickRing;
        d.ZoomEnabled = ZoomEnabled;
        d.ZoomAmount = ZoomAmount;
        if (Images is not null)
        {
            var restored = new List<ImageOverlay>();
            foreach (var img in Images)
            {
                if (take.ImportImage(img.Path) is { } path)
                {
                    var copy = img.Copy();
                    copy.Id = Guid.NewGuid();
                    copy.Path = path;
                    copy.Start = 0;
                    copy.End = take.Duration;
                    restored.Add(copy);
                }
            }

            d.Images = restored;
        }
    }
}

internal static class PresetStore
{
    public const int Max = 12;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string ImageRoot => Path.Combine(Output.DataRoot(), "Preset Images");

    public static List<EditPreset> Load(IAppSettings app)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EditPreset>>(app.Get<string>(K.RecPresets) ?? "[]", Json)?.Where(p => !string.IsNullOrWhiteSpace(p.Name)).TakeLast(Max).ToList() ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Store(IAppSettings app, List<EditPreset> list) => app.Set(K.RecPresets, JsonSerializer.Serialize(list.TakeLast(Max).ToList(), Json));

    private static bool SameName(string a, string b) => string.Compare(a.Trim(), b.Trim(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    /// <summary>Saves (or replaces by name) a preset; its pictures move to the private store so deleting the take cannot lose them.</summary>
    public static void Save(IAppSettings app, string name, EditDocument doc)
    {
        var list = Load(app);
        var preset = EditPreset.From(name.Trim(), doc);
        var dir = Path.Combine(ImageRoot, preset.Id.ToString("N"));
        var kept = new List<ImageOverlay>();
        foreach (var img in preset.Images ?? new())
        {
            try
            {
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, kept.Count + "-" + Path.GetFileName(img.Path));
                File.Copy(img.Path, dest, true);
                img.Path = dest;
                kept.Add(img);
            }
            catch
            {
                // a missing picture is simply not kept
            }
        }

        preset.Images = kept;
        foreach (var old in list.Where(p => SameName(p.Name, name)).ToList())
        {
            Delete(old);
            list.Remove(old);
        }

        list.Add(preset);
        while (list.Count > Max)
        {
            Delete(list[0]);
            list.RemoveAt(0);
        }

        Store(app, list);
    }

    public static void Remove(IAppSettings app, Guid id)
    {
        var list = Load(app);
        foreach (var p in list.Where(p => p.Id == id).ToList())
        {
            Delete(p);
            list.Remove(p);
        }

        Store(app, list);
    }

    private static void Delete(EditPreset p)
    {
        try
        {
            Directory.Delete(Path.Combine(ImageRoot, p.Id.ToString("N")), true);
        }
        catch
        {
            // not there
        }
    }
}

/// <summary>A small dark modal asking for one line of text (the preset name).</summary>
internal static class TextPrompt
{
    public static string? Ask(Window owner, string title, string placeholder)
    {
        var win = Ed.DarkWindow(title, 340, 150);
        win.Owner = owner;
        win.ResizeMode = ResizeMode.NoResize;
        win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var box = new TextBox { Width = 260, Height = 24, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, Background = Ed.Plate, Foreground = Ed.Fg, BorderBrush = Ed.PlateStroke, CaretBrush = Ed.Fg };
        var hint = Ed.Label(placeholder, 12, FontWeights.Normal, Ed.FgFaint);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(6, 0, 0, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        var field = new Grid { Width = 260 };
        field.Children.Add(box);
        field.Children.Add(hint);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string? result = null;
        var save = new EdButton(null, RS.Save, null, () =>
        {
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                result = box.Text.Trim();
                win.Close();
            }
        }, prominent: true, height: 26);
        var cancel = new EdButton(null, RS.Cancel, null, () => win.Close(), height: 26);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(cancel);
        row.Children.Add(new Border { Width = 8 });
        row.Children.Add(save);
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(field);
        stack.Children.Add(row);
        win.Content = stack;
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(box.Text))
            {
                result = box.Text.Trim();
                win.Close();
            }
            else if (e.Key == Key.Escape)
            {
                win.Close();
            }
        };
        win.Loaded += (_, _) => box.Focus();
        win.ShowDialog();
        return result;
    }
}

/// <summary>An iOS-style switch used by the inspector rows.</summary>
internal sealed class EdSwitch : Border
{
    private bool _on;
    private readonly Border _knob = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };

    public EdSwitch(bool on, Action<bool> changed)
    {
        Width = 32;
        Height = 18;
        CornerRadius = new CornerRadius(9);
        Cursor = Cursors.Hand;
        Child = _knob;
        _on = on;
        Paint();
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _on = !_on;
            Paint();
            changed(_on);
        };
    }

    private void Paint()
    {
        Background = _on ? Ed.Accent : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255));
        _knob.Margin = new Thickness(_on ? 16 : 2, 0, 0, 0);
    }
}

internal static class EdIcons
{
    /// <summary>A 3x3 mosaic (the blur tool's glyph; Segoe Fluent has no matching symbol).</summary>
    public static UIElement Mosaic(double size, Brush brush)
    {
        var canvas = new Canvas { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var cell = size / 3;
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var rect = new System.Windows.Shapes.Rectangle { Width = cell - 1, Height = cell - 1, Fill = brush, Opacity = (r + c) % 2 == 0 ? 1 : 0.4 };
                Canvas.SetLeft(rect, c * cell);
                Canvas.SetTop(rect, r * cell);
                canvas.Children.Add(rect);
            }
        }

        return canvas;
    }

    public static void Prepend(EdButton button, UIElement icon)
    {
        if (button.Child is StackPanel row)
        {
            row.Children.Insert(0, icon);
            if (button.Label is { } l)
            {
                l.Margin = new Thickness(6, 0, 0, 0);
            }
        }
    }
}
