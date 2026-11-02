// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal enum RadialNowPlayingState
{
    Loading,
    NothingPlaying,
    Playing,
}

/// <summary>Sanitised snapshot of the playing media session (spec 3.7).</summary>
internal sealed record RadialNowPlayingSnapshot(string? Title, string? Artist, string? Album, string AppId, BitmapSource? Artwork)
{
    /// <summary>Title and artist joined by a newline; "Now Playing" when both are absent.</summary>
    public string Label => string.Join("\n", new[] { Title, Artist }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } s ? s : "Now Playing";
}

/// <summary>Reads the current media session (GSMTC) and shows the Now Playing card. Session scoped; the wheel works without it.</summary>
internal sealed class RadialNowPlayingService
{
    private readonly IContributionServices _ctx;
    private int _generation;
    private NowPlayingCard? _card;
    private Point? _pendingAnchor;

    public RadialNowPlayingService(IContributionServices ctx) => _ctx = ctx;

    public RadialNowPlayingState State { get; private set; } = RadialNowPlayingState.NothingPlaying;

    public RadialNowPlayingSnapshot? Snapshot { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="State"/> changed.</summary>
    public event Action? Changed;

    /// <summary>The wheel label of the Now Playing slice.</summary>
    public string Label => State switch
    {
        RadialNowPlayingState.Loading => "Now Playing",
        RadialNowPlayingState.NothingPlaying => "Nothing playing",
        _ => Snapshot?.Label ?? "Now Playing",
    };

    public void Reset()
    {
        _generation++;
        State = RadialNowPlayingState.NothingPlaying;
        Snapshot = null;
    }

    public void Refresh()
    {
        var generation = ++_generation;
        State = RadialNowPlayingState.Loading;
        Snapshot = null;
        Changed?.Invoke();
        _ = FetchAsync(generation);
    }

    private async Task FetchAsync(int generation)
    {
        RadialNowPlayingSnapshot? snapshot = null;
        try
        {
            var work = ReadAsync();
            var done = await Task.WhenAny(work, Task.Delay(2000));
            snapshot = done == work ? await work : null;
        }
        catch
        {
            snapshot = null;
        }

        await _ctx.Ui.InvokeAsync(() =>
        {
            if (generation != _generation)
            {
                return;
            }

            Snapshot = snapshot;
            State = snapshot is null ? RadialNowPlayingState.NothingPlaying : RadialNowPlayingState.Playing;
            Changed?.Invoke();
            if (snapshot is not null && _pendingAnchor is { } anchor)
            {
                _pendingAnchor = null;
                ShowCard(anchor);
            }
        });
    }

    private async Task<RadialNowPlayingSnapshot?> ReadAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        GlobalSystemMediaTransportControlsSession? session = null;
        var current = manager.GetCurrentSession();
        if (current?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            session = current;
        }
        else
        {
            session = manager.GetSessions().FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }

        if (session is null)
        {
            return null;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        byte[]? art = null;
        if (props.Thumbnail is { } thumb)
        {
            try
            {
                using var stream = await thumb.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    if (ms.Length > 12 * 1024 * 1024)
                    {
                        ms.SetLength(0);
                        break;
                    }
                }

                art = ms.Length > 0 ? ms.ToArray() : null;
            }
            catch
            {
                art = null;
            }
        }

        var title = Clean(props.Title);
        var artist = Clean(props.Artist);
        var album = Clean(props.AlbumTitle);
        var appId = (session.SourceAppUserModelId ?? "").Trim();
        if (title is null && appId.Length == 0)
        {
            return null;
        }

        BitmapSource? image = null;
        if (art is not null)
        {
            image = await _ctx.Ui.InvokeAsync(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(art);
                    bmp.EndInit();
                    bmp.Freeze();
                    return (BitmapSource?)bmp;
                }
                catch
                {
                    return null;
                }
            });
        }

        return new RadialNowPlayingSnapshot(title, artist, album, appId, image);
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        var text = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length > 300)
        {
            text = text[..300];
        }

        return text.Length == 0 ? null : text;
    }

    public void Dismiss()
    {
        _pendingAnchor = null;
        _card?.Close();
        _card = null;
    }

    /// <summary>Selecting the slice (after the wheel closed): shows the card at the anchor (physical px) or waits for the fetch.</summary>
    public void Present(Point anchorPhysical)
    {
        switch (State)
        {
            case RadialNowPlayingState.Playing:
                ShowCard(anchorPhysical);
                break;
            case RadialNowPlayingState.Loading:
                _pendingAnchor = anchorPhysical;
                break;
        }
    }

    private void ShowCard(Point anchor)
    {
        if (Snapshot is null)
        {
            return;
        }

        _card?.Close();
        _card = new NowPlayingCard(_ctx, Snapshot, anchor);
        _card.Closed += (_, _) => _card = null;
        _card.Open();
    }

    // ---------------------------------------------------------------- app helpers

    internal static string AppName(string appId)
    {
        if (appId.Length == 0)
        {
            return "";
        }

        try
        {
            var info = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appId);
            if (info is not null && info.DisplayInfo.DisplayName is { Length: > 0 } name)
            {
                return name;
            }
        }
        catch
        {
            // desktop app id
        }

        var core = appId.Contains('!') ? appId[..appId.IndexOf('!')] : appId;
        core = Path.GetFileNameWithoutExtension(core);
        var dot = core.LastIndexOf('.');
        return dot >= 0 && dot < core.Length - 1 ? core[(dot + 1)..] : core;
    }

    internal static void OpenApp(string appId)
    {
        if (appId.Length == 0)
        {
            return;
        }

        try
        {
            var proc = Path.GetFileNameWithoutExtension(appId);
            foreach (var p in Process.GetProcessesByName(proc))
            {
                if (p.MainWindowHandle != 0 && Plat.Activate(p.MainWindowHandle))
                {
                    return;
                }
            }
        }
        catch
        {
            // fall through to the shell
        }

        Plat.Open(@"shell:AppsFolder\" + appId);
    }

    private sealed class NowPlayingCard : GlassWindow
    {
        private readonly RadialNowPlayingSnapshot _snapshot;
        private readonly Point _anchor;
        private readonly OutsideClickMonitor _outside;
        private readonly ForegroundWatcher _foreground;
        private readonly string _appId;

        public NowPlayingCard(IContributionServices ctx, RadialNowPlayingSnapshot snapshot, Point anchor)
            : base(16)
        {
            _snapshot = snapshot;
            _anchor = anchor;
            _appId = snapshot.AppId;
            ShowActivated = false;
            Width = 320;
            SizeToContent = SizeToContent.Height;
            var appName = AppName(snapshot.AppId);
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var art = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(10), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
            if (snapshot.Artwork is { } bmp)
            {
                art.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                art.Background = new LinearGradientBrush(
                    [new GradientStop(Color.FromRgb(0x1A, 0x1A, 0x1A), 0), new GradientStop(Color.FromRgb(0x0A, 0x0A, 0x0A), 0.5), new GradientStop(Colors.Black, 1)],
                    new Point(0, 0),
                    new Point(1, 1));
                if (Plat.ShellImage(@"shell:AppsFolder\" + snapshot.AppId, 42) is { } icon)
                {
                    art.Child = new Image { Source = icon, Width = 42, Height = 42 };
                }
                else
                {
                    art.Child = new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 24, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
            }

            grid.Children.Add(art);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(UiKit.Text(snapshot.Title ?? appName, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
            if (snapshot.Album is not null)
            {
                var album = UiKit.Text(snapshot.Album, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                album.Margin = new Thickness(0, 3, 0, 0);
                texts.Children.Add(album);
            }

            if (snapshot.Artist is not null)
            {
                var artist = UiKit.Text(snapshot.Artist, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                artist.Margin = new Thickness(0, 3, 0, 0);
                texts.Children.Add(artist);
            }

            var open = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            if (Plat.ShellImage(@"shell:AppsFolder\" + snapshot.AppId, 14) is { } small)
            {
                open.Children.Add(new Image { Source = small, Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0) });
            }

            open.Children.Add(UiKit.Text("Open " + appName, 9.5, FontWeights.Medium, ThemeKeys.TextSecondary));
            var arrow = UiKit.Icon("", 8.5, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            arrow.Margin = new Thickness(4, 0, 0, 0);
            open.Children.Add(arrow);
            texts.Children.Add(open);
            Grid.SetColumn(texts, 2);
            grid.Children.Add(texts);
            Body = grid;
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += (_, _) =>
            {
                OpenApp(_appId);
                Close();
            };
            _outside = new OutsideClickMonitor(ctx.Hooks, ctx.Ui, () => PhysicalFrame(), () => Close());
            _foreground = new ForegroundWatcher(ctx.Ui);
            _foreground.ForegroundChanged += hwnd =>
            {
                if (Plat.ProcessOf(hwnd) != Environment.ProcessId)
                {
                    Close();
                }
            };
            Closed += (_, _) =>
            {
                _outside.Dispose();
                _foreground.Dispose();
            };
        }

        private Rect PhysicalFrame()
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return new Rect(Left * dpi, Top * dpi, ActualWidth * dpi, ActualHeight * dpi);
        }

        public void Open()
        {
            var area = Screens.FromPoint(_anchor).WorkArea;
            Opacity = 0;
            Show();
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var w = ActualWidth * dpi;
            var h = ActualHeight * dpi;
            var margin = 12 * dpi;
            var x = Math.Clamp(_anchor.X - w / 2, area.Left + margin, Math.Max(area.Left + margin, area.Right - w - margin));
            var y = Math.Clamp(_anchor.Y - h / 2, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - h - margin));
            Plat.PlaceWindow(this, x, y, 0, 0);
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }
    }
}
