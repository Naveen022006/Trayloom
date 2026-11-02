// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out WinKnife.Core.Native.RECT rect);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(WallpaperFit position);

    WallpaperFit GetPosition();
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass
{
}

internal static class WallpaperApi
{
    public static IReadOnlyList<string> MonitorIds()
    {
        var ids = new List<string>();
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            var count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var id = wallpaper.GetMonitorDevicePathAt(i);
                if (!string.IsNullOrEmpty(id))
                {
                    ids.Add(id);
                }
            }
        }
        catch
        {
            // COM interface unavailable: fall back to the global API only
        }

        return ids;
    }

    /// <summary>Sets the wallpaper for one monitor id, or for all monitors when <paramref name="monitorId"/> is null.</summary>
    public static void Apply(string path, string? monitorId, WallpaperFit fit)
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.SetPosition(fit);
            wallpaper.SetWallpaper(monitorId, path);
            return;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            // fall back below
        }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            if (key is not null)
            {
                var (style, tile) = fit switch
                {
                    WallpaperFit.Center => ("0", "0"),
                    WallpaperFit.Tile => ("0", "1"),
                    WallpaperFit.Stretch => ("2", "0"),
                    WallpaperFit.Fit => ("6", "0"),
                    WallpaperFit.Span => ("22", "0"),
                    _ => ("10", "0"),
                };
                key.SetValue("WallpaperStyle", style);
                key.SetValue("TileWallpaper", tile);
            }
        }

        if (!N.SystemParametersInfoW(N.SPI_SETDESKWALLPAPER, 0, path, N.SPIF_UPDATEINIFILE | N.SPIF_SENDCHANGE))
        {
            throw new InvalidOperationException("Windows refused to change the wallpaper.");
        }
    }
}

/// <summary>Pick an image and set it as wallpaper (per monitor), with a list of recent choices.</summary>
public sealed class WallpaperFeature : ToolFeatureBase
{
    private const int RecentLimit = 12;
    private WallpaperWindow? _window;

    public override string Id => "wallpaper";

    public override string Name => "Wallpaper";

    public override string Description => "Change your desktop wallpaper from any image, per monitor, and jump back to recent ones.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Change wallpaper...", () => Safe(Open), ""),
        new FeatureAction("random", "Random recent wallpaper", () => Safe(ApplyRandom), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Open);

    protected override void OnStop()
    {
        _window?.Close();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var open = UiKit.Btn("Change wallpaper", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open wallpaper picker", null, "Optional global shortcut.")
            .AddChoice(
                "fit",
                "Picture position",
                Enum.GetValues<WallpaperFit>().Select(f => new Choice<string>(f.ToString(), f.ToString())).ToArray(),
                nameof(WallpaperFit.Fill));
        return UiKit.WithExtras(builder.Build(), open);
    }

    internal WallpaperFit Fit() => Enum.TryParse<WallpaperFit>(Ctx.Settings.Get("fit", nameof(WallpaperFit.Fill)), out var f) ? f : WallpaperFit.Fill;

    internal List<string> Recent() => Ctx.Settings.Get("recent", new List<string>()).Where(File.Exists).ToList();

    internal void SetWallpaper(string path, string? monitorId)
    {
        WallpaperApi.Apply(path, monitorId, Fit());
        var recent = Recent();
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        if (recent.Count > RecentLimit)
        {
            recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
        }

        Ctx.Settings.Set("recent", recent);
    }

    private void ApplyRandom()
    {
        var recent = Recent();
        if (recent.Count == 0)
        {
            Ctx.Notifier.Show(Name, "No recent wallpapers yet. Pick one first.", NotificationLevel.Info);
            return;
        }

        var path = recent[Random.Shared.Next(recent.Count)];
        SetWallpaper(path, null);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new WallpaperWindow(this, Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class WallpaperWindow : Window
{
    private readonly WallpaperFeature _feature;
    private readonly IFeatureContext _ctx;
    private readonly WrapPanel _recent = new();
    private readonly ComboBox _monitor = new() { MinWidth = 180 };
    private readonly IReadOnlyList<string> _monitorIds;
    private readonly TextBlock _status = UiKit.Muted(string.Empty);

    public WallpaperWindow(WallpaperFeature feature, IFeatureContext ctx)
    {
        _feature = feature;
        _ctx = ctx;
        Title = "Wallpaper";
        Width = 720;
        Height = 560;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _monitorIds = WallpaperApi.MonitorIds();
        _monitor.Items.Add("All monitors");
        for (var i = 0; i < _monitorIds.Count; i++)
        {
            _monitor.Items.Add($"Monitor {i + 1}");
        }

        _monitor.SelectedIndex = 0;
        _monitor.IsEnabled = _monitorIds.Count > 1;

        var pick = UiKit.Btn("Choose image...", Choose, true, "");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        bar.Children.Add(pick);
        var target = UiKit.Label("Apply to", ThemeKeys.TextMuted);
        target.Margin = new Thickness(8, 0, 8, 0);
        bar.Children.Add(target);
        bar.Children.Add(_monitor);
        bar.Children.Add(new Border { Width = 12 });
        bar.Children.Add(_status);

        var header = new StackPanel();
        header.Children.Add(UiKit.Label("Recent wallpapers", null, 16, FontWeights.SemiBold));
        header.Children.Add(UiKit.Muted("Click a picture to apply it. Drop an image file on this window to use it."));
        header.Margin = new Thickness(0, 0, 0, 8);

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = _recent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;

        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(IsImage) is { } file)
            {
                Apply(file);
            }
        };
        RefreshRecent();
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";

    private void Choose()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) == true)
        {
            Apply(dialog.FileName);
        }
    }

    private void Apply(string path)
    {
        try
        {
            var index = _monitor.SelectedIndex;
            string? id = index > 0 && index - 1 < _monitorIds.Count ? _monitorIds[index - 1] : null;
            _feature.SetWallpaper(path, id);
            _status.Text = "Applied " + Path.GetFileName(path);
            RefreshRecent();
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("Wallpaper failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Wallpaper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshRecent()
    {
        _recent.Children.Clear();
        var recent = _feature.Recent();
        if (recent.Count == 0)
        {
            _recent.Children.Add(UiKit.Muted("Nothing yet."));
            return;
        }

        foreach (var path in recent)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 320;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                var thumb = new Image { Source = image, Width = 200, Height = 120, Stretch = Stretch.UniformToFill };
                var frame = new Border
                {
                    Child = thumb,
                    ClipToBounds = true,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                    ToolTip = path,
                };
                frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
                var captured = path;
                frame.MouseLeftButtonUp += (_, _) => Apply(captured);
                _recent.Children.Add(frame);
            }
            catch
            {
                // unreadable image: skip it
            }
        }
    }
}
