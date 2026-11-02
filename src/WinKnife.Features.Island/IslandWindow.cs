// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>
/// The black pill at the top of the screen. The window is only ever as big as the pill needs to be (it grows before an
/// expand animation and shrinks after a collapse), so transparent areas never block clicks on other windows.
/// </summary>
internal sealed class IslandWindow : OverlayWindow, IIslandController
{
    internal const double CompactHeight = 34;
    internal const double IdleWidth = 116;
    internal const double ExpandedWidth = 480;
    internal const double ExpandedHeight = 292;
    private const double HeaderHeight = 46;

    public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(
        nameof(BottomRadius), typeof(double), typeof(IslandWindow), new PropertyMetadata(17.0, OnBottomRadiusChanged));

    private readonly MediaService _media;
    private readonly Border _pill = new();
    private readonly Grid _compactLayer = new();
    private readonly Grid _expandedLayer = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _contentHost = new();
    private readonly Border _leftArt = new();
    private readonly TextBlock _leftGlyph;
    private readonly TextBlock _centerText;
    private readonly TextBlock _rightText;
    private readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScaleTransform[] _barScales = new ScaleTransform[4];
    private readonly Border _grabber = new();
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _alertTimer;
    private readonly DispatcherTimer _watchTimer;
    private DateTime? _outsideSince;
    private readonly Dictionary<string, FrameworkElement> _contentCache = [];
    private readonly Dictionary<string, Border> _tabButtons = [];

    private Storyboard? _barsStoryboard;
    private Storyboard? _sizeStoryboard;
    private int _animToken;
    private bool _expanded;
    private bool _suppressed;
    private bool _mouseInside;
    private int _holdCount;
    private string? _currentSection;
    private bool _userPicked;
    private IslandAlert? _alert;
    private MonitorArea _area;
    private double _scale = 1;
    private double _winW;
    private double _winH;
    private double _pillW;
    private double _pillH;

    public IslandWindow(MediaService media)
    {
        _media = media;
        Title = "WinKnife Island";
        Width = IdleWidth;
        Height = CompactHeight;
        SizeToContent = SizeToContent.Manual;
        FontFamily = IslandTheme.TextFont;
        _winW = IdleWidth;
        _winH = CompactHeight;
        _pillW = IdleWidth;
        _pillH = CompactHeight;

        _leftGlyph = IslandTheme.Glyph("", 14);
        _centerText = IslandTheme.Label(string.Empty, 12.5, IslandTheme.Text, FontWeights.SemiBold);
        _rightText = IslandTheme.Digits(13);

        BuildCompact();
        BuildExpanded();

        var stack = new Grid();
        stack.Children.Add(_compactLayer);
        stack.Children.Add(_expandedLayer);
        _pill.Background = IslandTheme.Black;
        _pill.HorizontalAlignment = HorizontalAlignment.Center;
        _pill.VerticalAlignment = VerticalAlignment.Top;
        _pill.Width = IdleWidth;
        _pill.Height = CompactHeight;
        _pill.CornerRadius = new CornerRadius(0, 0, 17, 17);
        _pill.ClipToBounds = true;
        _pill.Child = stack;
        _pill.MouseEnter += OnPillEnter;
        _pill.MouseLeave += OnPillLeave;
        _pill.MouseLeftButtonUp += OnPillClick;

        Content = new Grid { Children = { _pill } };

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(130) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_mouseInside && !_expanded)
            {
                Expand();
            }
        };
        _collapseTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_expanded && _holdCount == 0 && !CursorInsidePill())
            {
                Collapse();
            }
        };
        // Only runs while expanded: pointer tracking by polling is more reliable than leave events for a no-activate window.
        _watchTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _watchTimer.Tick += (_, _) =>
        {
            if (!_expanded || _holdCount > 0 || CursorInsidePill())
            {
                _outsideSince = null;
                return;
            }

            _outsideSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _outsideSince.Value >= _collapseTimer.Interval)
            {
                Collapse();
            }
        };
        _alertTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _alertTimer.Tick += (_, _) =>
        {
            _alertTimer.Stop();
            _alert = null;
            RefreshCompact();
        };

        IslandHub.SectionsChanged += OnSectionsChanged;
        IslandHub.CompactChanged += RefreshCompact;
        _media.Changed += RefreshCompact;
        IslandHub.Controller = this;
        RebuildTabs();
        RefreshCompact();
    }

    /// <summary>True: hover expands the pill. False: only a click does.</summary>
    public bool ExpandOnHover { get; set; } = true;

    public int CollapseDelayMs
    {
        get => (int)_collapseTimer.Interval.TotalMilliseconds;
        set => _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(value, 100, 3000));
    }

    /// <summary>Distance between the top screen edge and the pill, in DIPs.</summary>
    public double TopGap { get; set; }

    public bool ShowTitleInCompact { get; set; } = true;

    public bool IsSuppressed => _suppressed;

    public bool IsExpanded => _expanded;

    public double BottomRadius
    {
        get => (double)GetValue(BottomRadiusProperty);
        set => SetValue(BottomRadiusProperty, value);
    }

    /// <summary>The monitor the pill currently sits on (physical pixels).</summary>
    public MonitorArea Area => _area;

    private static void OnBottomRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (IslandWindow)d;
        var r = (double)e.NewValue;
        window._pill.CornerRadius = new CornerRadius(0, 0, r, r);
    }

    // ---------------------------------------------------------------- compact content

    private void BuildCompact()
    {
        _compactLayer.Height = CompactHeight;
        _compactLayer.VerticalAlignment = VerticalAlignment.Top;
        _compactLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _compactLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _compactLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _leftArt.Width = 22;
        _leftArt.Height = 22;
        _leftArt.CornerRadius = new CornerRadius(6);
        _leftArt.Margin = new Thickness(12, 0, 8, 2);
        _leftArt.VerticalAlignment = VerticalAlignment.Center;
        _leftArt.Child = _leftGlyph;
        _leftArt.Background = IslandTheme.Surface;
        Grid.SetColumn(_leftArt, 0);

        _centerText.Margin = new Thickness(0, 0, 8, 2);
        Grid.SetColumn(_centerText, 1);

        _rightText.Margin = new Thickness(0, 0, 14, 2);
        Grid.SetColumn(_rightText, 2);

        for (var i = 0; i < _barScales.Length; i++)
        {
            _barScales[i] = new ScaleTransform(1, 0.4);
            _bars.Children.Add(new Border
            {
                Width = 3,
                Height = 14,
                Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
                CornerRadius = new CornerRadius(1.5),
                Background = IslandTheme.Green,
                RenderTransform = _barScales[i],
                RenderTransformOrigin = new Point(0.5, 0.5),
            });
        }

        _bars.Margin = new Thickness(0, 0, 14, 2);
        Grid.SetColumn(_bars, 2);

        _grabber.Width = 30;
        _grabber.Height = 4;
        _grabber.CornerRadius = new CornerRadius(2);
        _grabber.Background = IslandTheme.Faint;
        _grabber.HorizontalAlignment = HorizontalAlignment.Center;
        _grabber.VerticalAlignment = VerticalAlignment.Center;
        _grabber.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetColumnSpan(_grabber, 3);

        _compactLayer.Children.Add(_grabber);
        _compactLayer.Children.Add(_leftArt);
        _compactLayer.Children.Add(_centerText);
        _compactLayer.Children.Add(_rightText);
        _compactLayer.Children.Add(_bars);
    }

    /// <summary>Recomputes what the collapsed pill shows and animates it to the matching width.</summary>
    private void RefreshCompact()
    {
        var media = _media.Current;
        var hasMedia = media.HasSession && (media.IsPlaying || !string.IsNullOrEmpty(media.Title));
        var info = IslandHub.CurrentCompact;
        double width;

        if (_alert is { } alert)
        {
            SetLeft(alert.Glyph, null, alert.Accent);
            SetCenter(alert.Text, IslandTheme.Text);
            _rightText.Visibility = Visibility.Collapsed;
            _bars.Visibility = Visibility.Collapsed;
            _grabber.Visibility = Visibility.Collapsed;
            width = Math.Clamp(70 + alert.Text.Length * 7.2, 170, 340);
        }
        else if (hasMedia || info is not null)
        {
            _grabber.Visibility = Visibility.Collapsed;
            if (hasMedia)
            {
                SetLeft("", media.Artwork, IslandTheme.Muted);
            }
            else
            {
                SetLeft(info!.Glyph, null, info.Accent);
            }

            if (info is not null)
            {
                _rightText.Text = info.Text;
                _rightText.Foreground = info.Dimmed ? IslandTheme.Muted : info.Accent;
                _rightText.Visibility = Visibility.Visible;
                _bars.Visibility = Visibility.Collapsed;
            }
            else
            {
                _rightText.Visibility = Visibility.Collapsed;
                _bars.Visibility = Visibility.Visible;
                _bars.Opacity = media.IsPlaying ? 1 : 0.4;
            }

            if (hasMedia && ShowTitleInCompact)
            {
                var text = string.IsNullOrEmpty(media.Artist) ? media.Title : $"{media.Title} · {media.Artist}";
                SetCenter(text, IslandTheme.Text);
                width = info is null ? 236 : 276;
            }
            else
            {
                SetCenter(string.Empty, IslandTheme.Text);
                width = info is null ? 112 : 56 + 12 + info.Text.Length * 8.5 + 26;
            }
        }
        else
        {
            _leftArt.Visibility = Visibility.Collapsed;
            SetCenter(string.Empty, IslandTheme.Text);
            _rightText.Visibility = Visibility.Collapsed;
            _bars.Visibility = Visibility.Collapsed;
            _grabber.Visibility = Visibility.Visible;
            width = IdleWidth;
        }

        UpdateBars(!_expanded && !_suppressed && _bars.Visibility == Visibility.Visible && media.IsPlaying);
        if (!_expanded)
        {
            AnimateTo(width, CompactHeight, CompactHeight / 2, expanded: false, duration: 240);
        }
    }

    private void SetLeft(string glyph, ImageSource? art, Brush glyphBrush)
    {
        _leftArt.Visibility = Visibility.Visible;
        if (art is not null)
        {
            _leftArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            _leftGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            _leftArt.Background = Brushes.Transparent;
            _leftGlyph.Text = glyph;
            _leftGlyph.Foreground = glyphBrush;
            _leftGlyph.Visibility = Visibility.Visible;
        }
    }

    private void SetCenter(string text, Brush brush)
    {
        _centerText.Text = text;
        _centerText.Foreground = brush;
        _centerText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateBars(bool run)
    {
        if (run)
        {
            if (_barsStoryboard is not null)
            {
                return;
            }

            var sb = new Storyboard();
            Timeline.SetDesiredFrameRate(sb, 20);
            double[] seconds = [0.46, 0.62, 0.52, 0.7];
            for (var i = 0; i < _barScales.Length; i++)
            {
                var animation = new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(seconds[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromMilliseconds(i * 90),
                };
                Storyboard.SetTarget(animation, _bars.Children[i]);
                Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                sb.Children.Add(animation);
            }

            _barsStoryboard = sb;
            sb.Begin(this, true);
        }
        else if (_barsStoryboard is not null)
        {
            _barsStoryboard.Remove(this);
            _barsStoryboard = null;
            foreach (var scale in _barScales)
            {
                scale.ScaleY = 0.4;
            }
        }
    }

    // ---------------------------------------------------------------- expanded content

    private void BuildExpanded()
    {
        _expandedLayer.Width = ExpandedWidth;
        _expandedLayer.Height = ExpandedHeight;
        _expandedLayer.HorizontalAlignment = HorizontalAlignment.Center;
        _expandedLayer.VerticalAlignment = VerticalAlignment.Top;
        _expandedLayer.Opacity = 0;
        _expandedLayer.Visibility = Visibility.Collapsed;
        _expandedLayer.IsHitTestVisible = false;
        _expandedLayer.Margin = new Thickness(0);
        _expandedLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        _expandedLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(16, 6, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tabs.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_tabs);
        var collapse = new IslandButton("", size: 30, foreground: IslandTheme.Muted);
        collapse.Clicked += Collapse;
        Grid.SetColumn(collapse, 1);
        header.Children.Add(collapse);

        _contentHost.Margin = new Thickness(20, 4, 20, 18);
        Grid.SetRow(_contentHost, 1);
        _expandedLayer.Children.Add(header);
        _expandedLayer.Children.Add(_contentHost);
    }

    private void OnSectionsChanged()
    {
        RebuildTabs();
        if (_expanded)
        {
            SelectSection(_currentSection, animate: false);
        }
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        _tabButtons.Clear();
        var sections = IslandHub.Sections;
        foreach (var section in sections)
        {
            var glyph = IslandTheme.Glyph(section.Glyph, 15, IslandTheme.Muted);
            var label = IslandTheme.Label(section.Title, 12.5, IslandTheme.Text, FontWeights.SemiBold);
            label.Margin = new Thickness(7, 0, 0, 0);
            label.Visibility = Visibility.Collapsed;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(glyph);
            row.Children.Add(label);
            var tab = new Border
            {
                Child = row,
                Padding = new Thickness(11, 6, 11, 6),
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = section.Id,
            };
            var id = section.Id;
            tab.MouseLeftButtonUp += (_, e) =>
            {
                _userPicked = true;
                SelectSection(id, animate: true);
                e.Handled = true;
            };
            _tabs.Children.Add(tab);
            _tabButtons[id] = tab;
        }

        // A single tab is not worth showing.
        _tabs.Visibility = sections.Count > 1 ? Visibility.Visible : Visibility.Hidden;
        HighlightTabs();
    }

    private void HighlightTabs()
    {
        foreach (var (id, tab) in _tabButtons)
        {
            var selected = id == _currentSection;
            tab.Background = selected ? IslandTheme.SurfaceHover : Brushes.Transparent;
            var row = (StackPanel)tab.Child;
            ((TextBlock)row.Children[0]).Foreground = selected ? IslandTheme.Text : IslandTheme.Muted;
            row.Children[1].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SelectSection(string? id, bool animate)
    {
        var sections = IslandHub.Sections;
        if (sections.Count == 0)
        {
            return;
        }

        var target = sections.FirstOrDefault(s => s.Id == id) ?? sections[0];
        var previous = sections.FirstOrDefault(s => s.Id == _currentSection);
        if (previous is not null && previous.Id != target.Id)
        {
            previous.VisibilityChanged?.Invoke(false);
        }

        _currentSection = target.Id;
        if (!_contentCache.TryGetValue(target.Id, out var content))
        {
            try
            {
                content = target.Create();
            }
            catch (Exception)
            {
                content = IslandTheme.Label("This section could not be loaded.", 13, IslandTheme.Muted);
            }

            _contentCache[target.Id] = content;
        }

        if (!ReferenceEquals(_contentHost.Children.Count > 0 ? _contentHost.Children[0] : null, content))
        {
            _contentHost.Children.Clear();
            if (content.Parent is Panel oldParent)
            {
                oldParent.Children.Remove(content);
            }

            _contentHost.Children.Add(content);
            if (animate && SystemParameters.ClientAreaAnimation)
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                content.BeginAnimation(OpacityProperty, fade);
            }
        }

        target.VisibilityChanged?.Invoke(_expanded);
        HighlightTabs();
        PublishPill();
    }

    private string PickInitialSection()
    {
        var sections = IslandHub.Sections;
        if (_currentSection is not null && (_userPicked || sections.All(s => s.Id != "music")) && sections.Any(s => s.Id == _currentSection))
        {
            return _currentSection;
        }

        var media = _media.Current;
        if (media.HasSession && sections.Any(s => s.Id == "music"))
        {
            return "music";
        }

        var info = IslandHub.CurrentCompact;
        if (info is not null && sections.Any(s => s.Id == info.SectionId))
        {
            return info.SectionId;
        }

        return _currentSection ?? sections.FirstOrDefault()?.Id ?? "music";
    }

    // ---------------------------------------------------------------- expand / collapse

    public void Expand(string? sectionId = null)
    {
        if (_suppressed)
        {
            return;
        }

        _collapseTimer.Stop();
        _hoverTimer.Stop();
        _alertTimer.Stop();
        _alert = null;
        var target = sectionId ?? PickInitialSection();
        if (_expanded)
        {
            if (sectionId is not null)
            {
                _userPicked = true;
                SelectSection(sectionId, animate: true);
            }

            return;
        }

        _expanded = true;
        _outsideSince = null;
        _watchTimer.Start();
        UpdateBars(false);
        _expandedLayer.Visibility = Visibility.Visible;
        _expandedLayer.IsHitTestVisible = true;
        _currentSection = null;
        SelectSection(target, animate: false);
        AnimateTo(ExpandedWidth, ExpandedHeight, 30, expanded: true, duration: 380);
    }

    public void Collapse()
    {
        if (!_expanded)
        {
            return;
        }

        _collapseTimer.Stop();
        _hoverTimer.Stop();
        _watchTimer.Stop();
        _expanded = false;
        _expandedLayer.IsHitTestVisible = false;
        var current = IslandHub.Sections.FirstOrDefault(s => s.Id == _currentSection);
        current?.VisibilityChanged?.Invoke(false);
        RefreshCompact();
        if (_sizeStoryboard is null)
        {
            // RefreshCompact did not animate (same size); still need the layers swapped.
            AnimateTo(_pillW, CompactHeight, CompactHeight / 2, expanded: false, duration: 280);
        }
    }

    public void Toggle()
    {
        if (_expanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    public void NextTrack() => _media.Next();

    public void PreviousTrack() => _media.Previous();

    public void ShowAlert(IslandAlert alert)
    {
        if (_suppressed)
        {
            return;
        }

        _alertTimer.Stop();
        _alert = alert;
        _alertTimer.Interval = alert.Duration;
        if (_expanded)
        {
            // The user is looking at the island already; keep the alert for when it collapses.
            _alertTimer.Start();
            return;
        }

        RefreshCompact();
        _alertTimer.Start();
    }

    public IDisposable HoldOpen()
    {
        _holdCount++;
        _collapseTimer.Stop();
        return new HoldToken(this);
    }

    private sealed class HoldToken(IslandWindow owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner._holdCount = Math.Max(0, owner._holdCount - 1);
            if (owner._holdCount == 0 && owner._expanded && !owner._mouseInside)
            {
                owner._collapseTimer.Start();
            }
        }
    }

    private void OnPillEnter(object sender, MouseEventArgs e)
    {
        _mouseInside = true;
        _collapseTimer.Stop();
        if (!_expanded && ExpandOnHover && !_suppressed)
        {
            _hoverTimer.Start();
        }
    }

    private void OnPillLeave(object sender, MouseEventArgs e)
    {
        _mouseInside = false;
        _hoverTimer.Stop();
        if (_expanded && _holdCount == 0)
        {
            _collapseTimer.Start();
        }
    }

    private void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded)
        {
            var alert = _alert;
            Expand(alert?.SectionId);
            e.Handled = true;
        }
    }

    private bool CursorInsidePill()
    {
        var pill = IslandHub.Pill;
        if (pill is null)
        {
            return false;
        }

        var p = Screens.CursorPosition();
        return p.X >= pill.Left - 2 && p.X <= pill.Right + 2 && p.Y >= pill.Top - 2 && p.Y <= pill.Bottom + 2;
    }

    // ---------------------------------------------------------------- animation

    private void AnimateTo(double width, double height, double radius, bool expanded, double duration)
    {
        if (!IsLoaded && !IsVisible && Handle == 0)
        {
            // Not shown yet: just set the final geometry.
            _pill.Width = width;
            _pill.Height = height;
            BottomRadius = radius;
            _pillW = width;
            _pillH = height;
            ApplyWindow(Math.Max(width, _winW), Math.Max(height, _winH));
            ApplyWindow(width, height);
            SwapLayers(expanded, instant: true);
            PublishPill();
            return;
        }

        var token = ++_animToken;
        var fromW = _pill.ActualWidth > 0 ? _pill.ActualWidth : _pillW;
        var fromH = _pill.ActualHeight > 0 ? _pill.ActualHeight : _pillH;
        var fromR = BottomRadius;
        _pillW = width;
        _pillH = height;
        PublishPill();

        // Grow the window first so the pill is never clipped while animating.
        ApplyWindow(Math.Max(Math.Max(fromW, width), _winW), Math.Max(Math.Max(fromH, height), _winH));

        if (!SystemParameters.ClientAreaAnimation || !IsVisible || (Math.Abs(fromW - width) < 0.5 && Math.Abs(fromH - height) < 0.5))
        {
            _sizeStoryboard?.Remove(this);
            _sizeStoryboard = null;
            _pill.Width = width;
            _pill.Height = height;
            BottomRadius = radius;
            SwapLayers(expanded, instant: true);
            ApplyWindow(width, height);
            return;
        }

        IEasingFunction ease = expanded
            ? new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };
        var length = TimeSpan.FromMilliseconds(duration);
        var sb = new Storyboard();
        Add(sb, _pill, FrameworkElement.WidthProperty, fromW, width, length, ease);
        Add(sb, _pill, FrameworkElement.HeightProperty, fromH, height, length, ease);
        Add(sb, this, BottomRadiusProperty, fromR, radius, length, ease);

        // Cross-fade the two layers.
        var fast = TimeSpan.FromMilliseconds(expanded ? 140 : 110);
        var delayed = TimeSpan.FromMilliseconds(expanded ? 110 : 0);
        var compactFade = new DoubleAnimation(expanded ? 0 : 1, fast) { BeginTime = expanded ? TimeSpan.Zero : TimeSpan.FromMilliseconds(120) };
        var expandedFade = new DoubleAnimation(expanded ? 1 : 0, expanded ? TimeSpan.FromMilliseconds(200) : fast) { BeginTime = delayed };
        Storyboard.SetTarget(compactFade, _compactLayer);
        Storyboard.SetTargetProperty(compactFade, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(expandedFade, _expandedLayer);
        Storyboard.SetTargetProperty(expandedFade, new PropertyPath(OpacityProperty));
        sb.Children.Add(compactFade);
        sb.Children.Add(expandedFade);

        sb.Completed += (_, _) =>
        {
            if (token != _animToken)
            {
                return;
            }

            sb.Remove(this);
            _sizeStoryboard = null;
            _pill.Width = width;
            _pill.Height = height;
            BottomRadius = radius;
            SwapLayers(expanded, instant: true);
            ApplyWindow(width, height);
        };
        _sizeStoryboard = sb;
        sb.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private static void Add(Storyboard sb, DependencyObject target, DependencyProperty property, double from, double to, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        sb.Children.Add(animation);
    }

    private void SwapLayers(bool expanded, bool instant)
    {
        _ = instant;
        _compactLayer.Opacity = expanded ? 0 : 1;
        _expandedLayer.Opacity = expanded ? 1 : 0;
        _compactLayer.IsHitTestVisible = !expanded;
        if (!expanded)
        {
            _expandedLayer.Visibility = Visibility.Collapsed;
        }
        else
        {
            _expandedLayer.Visibility = Visibility.Visible;
        }
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Moves the window to the right monitor and re-applies the geometry (display/DPI changes, setting changes).</summary>
    public void Reposition(bool useCursorMonitor)
    {
        _area = useCursorMonitor ? Screens.UnderCursor() : Screens.Primary();
        var centre = new Point(_area.Bounds.X + (_area.Bounds.Width / 2), _area.Bounds.Y + (_area.Bounds.Height / 2));
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)centre.X, Y = (int)centre.Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
        _scale = IslandNative.MonitorScale(monitor);
        ApplyWindow(Math.Max(_winW, _pillW), Math.Max(_winH, _pillH));
        PublishPill();
    }

    /// <summary>Handle of the monitor the pill is on and its bounds, for fullscreen checks.</summary>
    public (nint Monitor, RECT Bounds) MonitorInfo()
    {
        var b = _area.Bounds;
        var bounds = new RECT { Left = (int)b.Left, Top = (int)b.Top, Right = (int)b.Right, Bottom = (int)b.Bottom };
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)(b.X + (b.Width / 2)), Y = (int)(b.Y + (b.Height / 2)) }, NativeConstants.MONITOR_DEFAULTTONEAREST);
        return (monitor, bounds);
    }

    private void ApplyWindow(double widthDips, double heightDips)
    {
        _winW = widthDips;
        _winH = heightDips;
        if (_area.Bounds.Width <= 0)
        {
            _area = Screens.Primary();
            var c = new POINT { X = (int)(_area.Bounds.X + (_area.Bounds.Width / 2)), Y = (int)(_area.Bounds.Y + (_area.Bounds.Height / 2)) };
            _scale = IslandNative.MonitorScale(NativeMethods.MonitorFromPoint(c, NativeConstants.MONITOR_DEFAULTTONEAREST));
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var pw = (int)Math.Ceiling(widthDips * _scale);
            var ph = (int)Math.Ceiling(heightDips * _scale);
            var x = (int)Math.Round(_area.Bounds.X + ((_area.Bounds.Width - pw) / 2));
            var y = (int)Math.Round(_area.Bounds.Y + (TopGap * _scale));
            SetPhysicalBounds(x, y, pw, ph);

            // Moving to a monitor with another DPI changes the window's DPI synchronously; redo with the real scale.
            var actual = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (Math.Abs(actual - _scale) < 0.001)
            {
                break;
            }

            _scale = actual;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(() => ApplyWindow(_winW, _winH), DispatcherPriority.Background);
    }

    private void PublishPill()
    {
        if (_suppressed)
        {
            IslandHub.Pill = null;
            return;
        }

        var width = _pillW * _scale;
        var height = _pillH * _scale;
        var left = _area.Bounds.X + ((_area.Bounds.Width - width) / 2);
        var top = _area.Bounds.Y + (TopGap * _scale);
        var scrolls = IslandHub.Sections.FirstOrDefault(s => s.Id == _currentSection)?.ScrollsContent ?? false;
        IslandHub.Pill = new PillRect(
            (int)left,
            (int)top,
            (int)Math.Ceiling(left + width),
            (int)Math.Ceiling(top + height),
            (int)(top + (HeaderHeight * _scale)),
            _expanded,
            scrolls);
    }

    // ---------------------------------------------------------------- visibility

    /// <summary>Hides the island entirely (fullscreen app in front) or brings it back.</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        if (suppressed)
        {
            _hoverTimer.Stop();
            _collapseTimer.Stop();
            _watchTimer.Stop();
            UpdateBars(false);
            if (_expanded)
            {
                _expanded = false;
                var current = IslandHub.Sections.FirstOrDefault(s => s.Id == _currentSection);
                current?.VisibilityChanged?.Invoke(false);
                _sizeStoryboard?.Remove(this);
                _sizeStoryboard = null;
                _pill.Width = _pillW = IdleWidth;
                _pill.Height = _pillH = CompactHeight;
                BottomRadius = CompactHeight / 2;
                SwapLayers(false, true);
            }

            IslandHub.Pill = null;
            Hide();
        }
        else
        {
            ShowNoActivate();
            Reposition(false);
            RefreshCompact();
        }
    }

    public void Shutdown()
    {
        _hoverTimer.Stop();
        _collapseTimer.Stop();
        _watchTimer.Stop();
        _alertTimer.Stop();
        UpdateBars(false);
        _sizeStoryboard?.Remove(this);
        var current = IslandHub.Sections.FirstOrDefault(s => s.Id == _currentSection);
        current?.VisibilityChanged?.Invoke(false);
        IslandHub.SectionsChanged -= OnSectionsChanged;
        IslandHub.CompactChanged -= RefreshCompact;
        _media.Changed -= RefreshCompact;
        IslandHub.Pill = null;
        if (ReferenceEquals(IslandHub.Controller, this))
        {
            IslandHub.Controller = null;
        }

        Close();
    }
}
