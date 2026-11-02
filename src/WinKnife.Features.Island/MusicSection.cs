// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinKnife.Features.Island;

/// <summary>The "Now playing" panel: artwork, title, artist, timeline and transport buttons.</summary>
internal sealed class MusicSection
{
    private readonly MediaService _media;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _progressTimer;
    private Grid? _root;
    private Border? _artwork;
    private TextBlock? _artGlyph;
    private TextBlock? _title;
    private TextBlock? _artist;
    private TextBlock? _source;
    private TextBlock? _elapsed;
    private TextBlock? _remaining;
    private Border? _trackFill;
    private Border? _track;
    private IslandButton? _prev;
    private IslandButton? _next;
    private Border? _playButton;
    private TextBlock? _playGlyph;
    private TextBlock? _empty;
    private Grid? _content;
    private bool _visible;
    private bool _scrubbing;

    public MusicSection(MediaService media, Dispatcher ui)
    {
        _media = media;
        _ui = ui;
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _media.Changed += OnMediaChanged;
    }

    public IslandSection ToSection() => new()
    {
        Id = "music",
        Title = "Now playing",
        Glyph = "",
        Order = 0,
        Create = Build,
        VisibilityChanged = OnVisibility,
    };

    public void Dispose()
    {
        _media.Changed -= OnMediaChanged;
        _progressTimer.Stop();
    }

    private void OnVisibility(bool visible)
    {
        _visible = visible;
        if (visible)
        {
            Update();
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    private void OnMediaChanged()
    {
        if (_visible)
        {
            Update();
        }
    }

    private FrameworkElement Build()
    {
        _root = new Grid { Margin = new Thickness(4, 4, 4, 0) };

        _artGlyph = IslandTheme.Glyph("", 34, IslandTheme.Faint);
        _artwork = new Border
        {
            Width = 120,
            Height = 120,
            CornerRadius = new CornerRadius(14),
            Background = IslandTheme.Surface,
            Child = _artGlyph,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _title = IslandTheme.Label(string.Empty, 18, IslandTheme.Text, FontWeights.SemiBold);
        _artist = IslandTheme.Label(string.Empty, 14, IslandTheme.Muted);
        _source = IslandTheme.Label(string.Empty, 11, IslandTheme.Faint);
        _elapsed = IslandTheme.Digits(11, IslandTheme.Muted, FontWeights.Normal);
        _remaining = IslandTheme.Digits(11, IslandTheme.Muted, FontWeights.Normal);
        _remaining.TextAlignment = TextAlignment.Right;

        _trackFill = new Border { Background = IslandTheme.Text, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _track = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = IslandTheme.SurfacePressed,
            Child = _trackFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var trackHit = new Grid { Background = Brushes.Transparent, Height = 18, Cursor = Cursors.Hand };
        trackHit.Children.Add(_track);
        trackHit.MouseLeftButtonDown += OnTrackDown;
        trackHit.MouseMove += OnTrackMove;
        trackHit.MouseLeftButtonUp += OnTrackUp;

        var timeline = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        Grid.SetColumn(trackHit, 1);
        Grid.SetColumn(_remaining, 2);
        timeline.Children.Add(_elapsed);
        timeline.Children.Add(trackHit);
        timeline.Children.Add(_remaining);

        _prev = new IslandButton("", size: 40);
        _prev.Clicked += _media.Previous;
        _next = new IslandButton("", size: 40);
        _next.Clicked += _media.Next;
        _playGlyph = IslandTheme.Glyph("", 20, IslandTheme.Black);
        _playButton = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = IslandTheme.Text,
            Cursor = Cursors.Hand,
            Child = _playGlyph,
            Margin = new Thickness(10, 0, 10, 0),
        };
        _playButton.MouseLeftButtonUp += (_, e) =>
        {
            _media.TogglePlayPause();
            e.Handled = true;
        };

        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        transport.Children.Add(_prev);
        transport.Children.Add(_playButton);
        transport.Children.Add(_next);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_source);
        info.Children.Add(timeline);
        info.Children.Add(transport);

        _content = new Grid();
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(info, 1);
        _content.Children.Add(_artwork);
        _content.Children.Add(info);

        _empty = IslandTheme.Label("Nothing is playing", 15, IslandTheme.Muted);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;

        _root.Children.Add(_content);
        _root.Children.Add(_empty);
        Update();
        return _root;
    }

    private void Update()
    {
        if (_root is null)
        {
            return;
        }

        var info = _media.Current;
        _content!.Visibility = info.HasSession ? Visibility.Visible : Visibility.Collapsed;
        _empty!.Visibility = info.HasSession ? Visibility.Collapsed : Visibility.Visible;
        if (!info.HasSession)
        {
            _progressTimer.Stop();
            return;
        }

        _title!.Text = string.IsNullOrEmpty(info.Title) ? "Unknown title" : info.Title;
        _artist!.Text = info.Artist;
        _artist.Visibility = string.IsNullOrEmpty(info.Artist) ? Visibility.Collapsed : Visibility.Visible;
        _source!.Text = info.Source;
        _source.Visibility = string.IsNullOrEmpty(info.Source) ? Visibility.Collapsed : Visibility.Visible;
        if (info.Artwork is not null)
        {
            _artwork!.Background = new ImageBrush(info.Artwork) { Stretch = Stretch.UniformToFill };
            _artGlyph!.Visibility = Visibility.Collapsed;
        }
        else
        {
            _artwork!.Background = IslandTheme.Surface;
            _artGlyph!.Visibility = Visibility.Visible;
        }

        _playGlyph!.Text = info.IsPlaying ? "" : "";
        _playButton!.Opacity = info.CanPlayPause ? 1 : 0.4;
        _prev!.Opacity = info.CanPrevious ? 1 : 0.35;
        _next!.Opacity = info.CanNext ? 1 : 0.35;
        UpdateProgress();

        // Only tick while something is actually moving.
        if (info.IsPlaying && info.Duration > TimeSpan.Zero && _visible)
        {
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    private void UpdateProgress()
    {
        if (_track is null || _scrubbing)
        {
            return;
        }

        var info = _media.Current;
        var position = info.Position;
        _elapsed!.Text = Format(position);
        if (info.Duration > TimeSpan.Zero)
        {
            _remaining!.Text = "-" + Format(info.Duration - position);
            var fraction = Math.Clamp(position.TotalSeconds / info.Duration.TotalSeconds, 0, 1);
            _trackFill!.Width = Math.Max(0, _track.ActualWidth * fraction);
        }
        else
        {
            _remaining!.Text = string.Empty;
            _trackFill!.Width = 0;
        }
    }

    private void OnTrackDown(object sender, MouseButtonEventArgs e)
    {
        var info = _media.Current;
        if (!info.CanSeek)
        {
            return;
        }

        _scrubbing = true;
        ((UIElement)sender).CaptureMouse();
        ScrubTo(e.GetPosition(_track));
        e.Handled = true;
    }

    private void OnTrackMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing)
        {
            ScrubTo(e.GetPosition(_track));
        }
    }

    private void OnTrackUp(object sender, MouseButtonEventArgs e)
    {
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        var info = _media.Current;
        var fraction = Math.Clamp(e.GetPosition(_track).X / Math.Max(1, _track!.ActualWidth), 0, 1);
        _media.Seek(TimeSpan.FromSeconds(info.Duration.TotalSeconds * fraction));
        e.Handled = true;
    }

    private void ScrubTo(Point point)
    {
        var info = _media.Current;
        var fraction = Math.Clamp(point.X / Math.Max(1, _track!.ActualWidth), 0, 1);
        _trackFill!.Width = _track.ActualWidth * fraction;
        _elapsed!.Text = Format(TimeSpan.FromSeconds(info.Duration.TotalSeconds * fraction));
    }

    internal static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}
