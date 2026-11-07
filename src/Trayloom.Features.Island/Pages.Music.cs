// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

internal static class MusicUi
{
    /// <summary>Nothing is playing and the player to use is Apple Music: drive it in the background (started hidden when closed).</summary>
    public static bool HiddenApple(IslandCore core, string? preferred) =>
        core.AppleMusic.Available && (preferred is null || preferred.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));

    /// <summary>What an idle transport button does: Apple Music's own button without showing its window, or open the preferred player.</summary>
    public static Action IdleAction(IslandCore core, string? preferred, AmAction action, TransportOrigin origin = TransportOrigin.Island) =>
        HiddenApple(core, preferred) ? () => core.AppleMusic.Transport(action, origin) : () => PlaybackService.OpenPlayer(preferred);

    /// <summary>The idle subtitle / tooltip: "Play Apple Music" when it is driven in the background, else "Open &lt;player&gt;".</summary>
    public static string IdleLabel(IslandCore core, string? preferred) =>
        HiddenApple(core, preferred) ? Loc.T("Play Apple Music") : "Open " + PlaybackService.NameOf(preferred ?? "");

    /// <summary>Compact transport (previous, play/pause, next) used by the Controls card; the idle form plays Apple Music in the background or opens the preferred player.</summary>
    public static FrameworkElement Transport(IslandCore core, double side, double play, double height, double spacing, string? openApp)
    {
        var p = core.Playback.Current;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = height };
        HoverButton Btn(string glyph, double size, Action click, bool enabled = true, string? tip = null)
        {
            var b = new HoverButton(click) { Width = height, Height = height, CornerRadius = new CornerRadius(14), Margin = new Thickness(spacing / 2, 0, spacing / 2, 0) };
            var ic = D.I(glyph, size, D.Text);
            ic.FontWeight = FontWeights.SemiBold;
            b.Child = ic;
            b.IsEnabled = enabled;
            if (tip is not null)
            {
                D.Tip(b, tip);
            }

            return b;
        }

        if (openApp is not null)
        {
            var tip = IdleLabel(core, openApp);
            var working = HiddenApple(core, openApp) && core.AppleMusic.Busy;
            row.Children.Add(Btn(G.Previous, side, IdleAction(core, openApp, AmAction.SkipBack), !working, tip));
            var idlePlay = Btn(G.Play, play, IdleAction(core, openApp, AmAction.PlayPause), !working, tip);
            if (working)
            {
                var spinner = PageUi.Spinner(play);
                spinner.HorizontalAlignment = HorizontalAlignment.Center;
                spinner.VerticalAlignment = VerticalAlignment.Center;
                idlePlay.Child = spinner;
            }

            row.Children.Add(idlePlay);
            row.Children.Add(Btn(G.Next, side, IdleAction(core, openApp, AmAction.SkipForward), !working, tip));
            return row;
        }

        row.Children.Add(Btn(G.Previous, side, core.Playback.Previous, p.CanPrevious, Strings.Previous));
        row.Children.Add(Btn(p.IsPlaying ? G.Pause : G.Play, play, core.Playback.TogglePlayPause, p.CanPlayPause || p.HasTrack, Strings.PlayPause));
        row.Children.Add(Btn(G.Next, side, core.Playback.Next, p.CanNext, Strings.Next));
        return row;
    }
}

/// <summary>The playback timeline: elapsed, slider (seek or meter) and remaining time (<c>NotchMusicTimeline</c>).</summary>
internal sealed class MusicTimeline : Grid
{
    private readonly IslandCore _core;
    private readonly TextBlock _left = D.Digits("0:00", 10, FontWeights.Medium, D.Secondary);
    private readonly TextBlock _right = D.Digits("−0:00", 10, FontWeights.Medium, D.Secondary);
    private readonly LevelSlider _slider;
    private readonly Meter _meter = new(0, null, 6);
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
    private double? _released;
    private DateTime _releasedAt;
    private readonly bool _appleMusic;
    private Brush _accent;

    public MusicTimeline(IslandCore core, Playback p)
    {
        _core = core;
        Height = 14;
        _accent = p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FitWidths(p.Duration);
        _left.VerticalAlignment = _right.VerticalAlignment = VerticalAlignment.Center;
        _left.Margin = new Thickness(0, 0, 8, 0);
        _right.Margin = new Thickness(8, 0, 0, 0);
        _right.TextAlignment = TextAlignment.Right;
        Children.Add(_left);
        Grid.SetColumn(_right, 2);
        Children.Add(_right);
        // the slider is as high as the row, so a click a little above or below the thin bar still lands; the bar grows under the pointer
        _slider = new LevelSlider(false, 0, Math.Max(1, p.Duration), 5, _accent) { Height = 14, Scrollable = false, HoverThickness = 8, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_slider, "Island.Music.Timeline");
        _appleMusic = PlaybackService.IsAppleMusicId(p.Aumid);
        _meter.Tint = _accent;
        _meter.VerticalAlignment = VerticalAlignment.Center;
        if (p.CanSeek)
        {
            Grid.SetColumn(_slider, 1);
            Children.Add(_slider);
            D.Tip(_slider, Strings.PlaybackPosition);
            _slider.ValueChanged += v => { _left.Text = D.TrackTime(v); _right.Text = "−" + D.TrackTime(_core.Playback.Current.Duration - v); };
            _slider.ValueCommitted += v =>
            {
                _released = v;
                _releasedAt = DateTime.UtcNow;
                _core.Playback.Seek(v);
            };
        }
        else
        {
            Grid.SetColumn(_meter, 1);
            Children.Add(_meter);
        }

        _tick.Tick += (_, _) => Update();
        Loaded += (_, _) =>
        {
            _core.Playback.SeekFailed += OnSeekFailed;
            Update();
            _tick.Start();
        };
        Unloaded += (_, _) =>
        {
            _core.Playback.SeekFailed -= OnSeekFailed;
            _tick.Stop();
        };
    }

    /// <summary>The seek did not happen: the thumb goes back to where the player is instead of waiting for the hold to run out.</summary>
    private void OnSeekFailed()
    {
        _released = null;
        Update();
    }

    private void FitWidths(double duration)
    {
        _left.MinWidth = _right.MinWidth = MeasureWidth(duration >= 3600 ? "00:00:00" : "00:00", 10);
        _right.MinWidth += 6;
    }

    /// <summary>The track's length arrived or changed while the timeline was already showing: the slider and the time columns follow it, nothing is rebuilt.</summary>
    public void RefreshDuration()
    {
        var duration = _core.Playback.Current.Duration;
        if (duration <= 0)
        {
            return;
        }

        _slider.SetMaximum(Math.Max(1, duration));
        FitWidths(duration);
        Update();
    }

    private static double MeasureWidth(string text, double size)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(D.Font, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), size, Brushes.White, 1.25);
        return Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 2;
    }

    public void Update()
    {
        var p = _core.Playback.Current;
        if (_slider.IsDragging || p.Duration <= 0)
        {
            return;
        }

        var pos = p.Position;
        if (_released is { } r)
        {
            if (SeekRules.HoldEnds((DateTime.UtcNow - _releasedAt).TotalSeconds, _appleMusic, pos, r))
            {
                _released = null;
            }
            else
            {
                pos = r;
            }
        }

        _left.Text = D.TrackTime(pos);
        _right.Text = "−" + D.TrackTime(p.Duration - pos);
        _slider.Value = pos;
        _meter.Value = p.Duration > 0 ? pos / p.Duration : 0;
    }
}

internal enum MusicExtra
{
    None,
    Lyrics,
    Queue,
    Library,
}

internal sealed class MusicPage : IslandPage
{
    private readonly Grid _view = new();
    private MusicExtra _extra;
    private FrameworkElement? _extraView;
    private double _width = 344;
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private LyricsView? _lyrics;
    private IMusicBrowserPanel? _library;
    private double _extraHeight = 216;

    // Track changes arrive as several events (the title, then the length, sometimes a brief drop of the session): one look after they settle.
    private readonly DispatcherTimer _settle = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _active;
    private MusicTimeline? _timeline;

    // what the page on screen was built for: a change in any of these is visible and rebuilds it; anything else is not worth a rebuild
    private string _song = "";
    private bool _canSeek;
    private ImageSource? _artwork;
    private int _shownDuration;
    private string _extraKey = "";

    /// <summary>The source switch: a 24 px plate and the gap above it.</summary>
    private const double SwitchGap = 8;
    private const double GripHeight = 14;

    /// <summary>The most height the Library can have on this screen, worked out when the page is measured; the grip stops there.</summary>
    private double _libraryRoom = IslandSizes.DefaultLibraryHeight;
    private const double SwitchHeight = 24 + SwitchGap;

    // The page is rebuilt (a new instance) when the person changes what it shows, or the Music page is reopened: the open extra
    // (Library, Lyrics or Queue) is carried over to the new instance, as long as the head still offers it.
    private static (MusicExtra Extra, DateTime At)? s_carry;

    public MusicPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        if (s_carry is { } carry && (DateTime.UtcNow - carry.At).TotalSeconds < 3)
        {
            // the same conditions the head uses to offer each toggle
            _extra = carry.Extra switch
            {
                MusicExtra.Library when MusicSources.Any(Core) => MusicExtra.Library,
                MusicExtra.Lyrics when LyricsOn => MusicExtra.Lyrics,
                MusicExtra.Queue when QueueOn => MusicExtra.Queue,
                _ => MusicExtra.None,
            };
            if (_extra == MusicExtra.Lyrics)
            {
                Core.Lyrics.Ensure(Core.Playback.Current);
            }
        }

        s_carry = null;
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Settle();
        };
    }

    public override string ModuleId => "music";

    public override FrameworkElement View => _view;

    public override double? NaturalWidth => _extra == MusicExtra.Library ? 480 : 344;

    // The Library search box asks the island for the keyboard itself, and only while it is focused.
    public override bool WantsKeyboard => false;

    /// <summary>The magnifier: opens the Library with the search box focused (where the island can drive Apple Music, or Spotify / YouTube Music is turned on).</summary>
    public override FrameworkElement? Leading => HasWindow && !IsPreview && MusicSources.Any(Core) ? _leading ??= BuildSearchButton() : null;

    public override FrameworkElement? Trailing => null;

    private StackPanel? _leading;

    private StackPanel BuildSearchButton()
    {
        var search = D.IconButton(G.Search, MusicSources.SearchTip(Core), OpenSearch, _extra == MusicExtra.Library, size: 28, glyphSize: 12);
        System.Windows.Automation.AutomationProperties.SetAutomationId(search, "Island.Music.SearchButton");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { search } };
    }

    /// <summary>
    /// Opens the Library (when it is not open) and puts the caret in its search box. This and a click on the box, Ctrl+F, "/" and typing a
    /// letter are the only ways the box takes the keyboard.
    /// </summary>
    private void OpenSearch()
    {
        if (!HasWindow || !MusicSources.Any(Core))
        {
            return;
        }

        if (_extra != MusicExtra.Library)
        {
            // the page is rebuilt around the Library: the new box takes the request
            LibraryNav.Focus.RequestFocus(LibraryNav.Now);
            SetExtra(MusicExtra.Library);
        }
        else
        {
            _library?.FocusSearch(selectAll: true);
        }
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (!HasWindow || IsPreview || !MusicSources.Any(Core))
        {
            return false;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        if (ctrl && !alt && !shift && key == Key.F)
        {
            OpenSearch();
            return true;
        }

        if (!typing && !ctrl && !alt && !shift && key == Key.Oem2)
        {
            OpenSearch();
            return true;
        }

        // Space plays / pauses, unless the box has something typed in it
        return !typing && !ctrl && !alt && key == Key.Space && _extra == MusicExtra.Library && _library is { HasText: true } box && box.TypeInto(" ");
    }

    /// <summary>A letter typed while the Library is open and the box does not have the caret goes into the box.</summary>
    private void OnWindowText(object sender, TextCompositionEventArgs e)
    {
        if (_extra != MusicExtra.Library || _library is null || e.Text.Length == 0 || char.IsControl(e.Text[0]) || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        if (_library.TypeInto(e.Text))
        {
            e.Handled = true;
        }
    }

    public override bool GrowsBeyondBudget => _extra != MusicExtra.None;

    // the Library list scrolls with the wheel (the island's own wheel gestures leave it alone)
    public override bool ScrollsContent => _extra == MusicExtra.Library;

    private bool LyricsOn => Core.App.Get<bool>(K.LyricsEnabled) && Core.Ctx.Runtime.IsAvailable("notch-lyrics");

    private bool QueueOn => Core.App.Get<bool>(K.QueueEnabled) && Core.Ctx.Runtime.IsAvailable("notch-queue");

    public override double Height(double width, double budget)
    {
        _width = Math.Min(NaturalWidth ?? 344, width);
        var p = Core.Playback.Current;
        double h = (p.Duration > 0 ? 56 + 14 + 14 + 10 + 44 + 8 : 56 + 14 + 44 + 8) + SwitchHeight;
        if (Core.App.Get<bool>(K.MusicShowVolume) && Core.ModuleVisible("mixer"))
        {
            h += 38;
        }

        if (_extra == MusicExtra.Library)
        {
            // the Library takes the height the person gave it and the screen allows; the page's fixed parts and the grip come first
            h += GripHeight;
            _libraryRoom = budget - h - 10;
            _extraHeight = IslandSizes.LibraryHeightFor(Core.App.Get<double>(K.MusicLibraryHeight), _libraryRoom);
        }
        else
        {
            _extraHeight = 216;
        }

        if (_extra != MusicExtra.None)
        {
            h += 10 + _extraHeight;
        }

        Build();
        return Math.Min(h, budget);
    }

    /// <summary>0 = no Apple Music button, 1 = "Show Apple Music" (kept hidden), 2 = "Hide Apple Music" (its window is up).</summary>
    private int _appleButton;

    private int AppleButtonState()
    {
        if (!Core.AppleMusic.Available || !AppleMusicKeeper.IsRunning())
        {
            return 0;
        }

        var keeper = Core.AppleMusic.Keeper;

        // "Show" also for a window a release could not bring back, and for a hidden, transparent or parked one that nothing keeps (a
        // crash of an earlier run, or Apple Music started that way): the person must always have a way to get the window back
        if (keeper.KeptHwnd != 0 || keeper.PendingRelease != 0)
        {
            return 1;
        }

        if (keeper.HasVisibleWindow())
        {
            return 2;
        }

        // not while the driver works: its window is transparent or parked on purpose (a Show then would break the job)
        return !keeper.DriverActive && keeper.StuckWindow() != 0 ? 1 : 0;
    }

    private void OnKeeper() => _view.Dispatcher.BeginInvoke(() =>
    {
        if (_active && AppleButtonState() != _appleButton)
        {
            RefreshInPlace();
        }
    });

    // what the idle page shows of Apple Music's work (the spinner and the failure line); only an idle page shows it
    private bool _idle;
    private bool _amBusy;
    private LibraryNote? _amNote;

    /// <summary>The island's Apple Music requests started, ended or failed: the idle page shows it (a page that plays something does not).</summary>
    private void OnAppleMusic() => _view.Dispatcher.BeginInvoke(() =>
    {
        var svc = Core.AppleMusic;
        if (_active && _idle && (svc.Busy != _amBusy || !ReferenceEquals(svc.Note, _amNote)))
        {
            RefreshInPlace();
        }
        else if (_active && _awaitingLibrary && svc.Db is not null)
        {
            // the library finished loading: the "…" menu can now look up the song that is playing
            RefreshInPlace();
        }
    });

    // Apple Music is playing and its library has not been read yet, so the "Go to album / artist" lookup waits for it
    private bool _awaitingLibrary;

    public override void Shown()
    {
        // Shown also runs after an in-place rebuild (the window measures the page again), so the subscriptions are made once
        Core.Playback.Changed -= OnChanged;
        Core.Lyrics.Changed -= OnLyrics;
        Core.AppleMusic.Keeper.Changed -= OnKeeper;
        Core.AppleMusic.Changed -= OnAppleMusic;
        Core.Playback.Changed += OnChanged;
        Core.Lyrics.Changed += OnLyrics;
        Core.AppleMusic.Keeper.Changed += OnKeeper;
        Core.AppleMusic.Changed += OnAppleMusic;
        _active = true;
        _poll.Start();
        if (HasWindow && !IsPreview)
        {
            Window.PreviewTextInput -= OnWindowText;
            Window.PreviewTextInput += OnWindowText;
        }

        Core.Levels.ForgetSilence();
        if (!_viewing && !IsPreview)
        {
            _viewing = true;
            s_live = this;
            Core.AppleMusic.ViewerShown();
        }
    }

    public override void Hidden()
    {
        Core.Playback.Changed -= OnChanged;
        Core.Lyrics.Changed -= OnLyrics;
        Core.AppleMusic.Keeper.Changed -= OnKeeper;
        Core.AppleMusic.Changed -= OnAppleMusic;
        _active = false;
        _settle.Stop();
        _poll.Stop();
        if (HasWindow)
        {
            Window.PreviewTextInput -= OnWindowText;
        }

        // not a rebuild of this page (Reopen sets the carry): the Library is being left, so its box gives the keyboard back at once
        if (_extra == MusicExtra.Library && s_carry is null)
        {
            LibraryNav.Focus.LeaveRequested = true;
        }

        if (_viewing)
        {
            _viewing = false;
            if (ReferenceEquals(s_live, this))
            {
                s_live = null;
            }

            Core.AppleMusic.ViewerHidden();
        }
    }

    // the Music page that is on screen (the library is opened on it), and whether this one counts as a viewer of Apple Music's progress
    private static MusicPage? s_live;
    private bool _viewing;

    /// <summary>Shows the Apple Music library on the Music page (opens the island on it when it is closed or on another page).</summary>
    public static void OpenLibrary(IslandCore core)
    {
        if (!MusicSources.Any(core))
        {
            return;
        }

        if (s_live is { } live && live.HasWindow)
        {
            if (live._extra != MusicExtra.Library)
            {
                live.SetExtra(MusicExtra.Library);
            }

            return;
        }

        s_carry = (MusicExtra.Library, DateTime.UtcNow);
        if (core.Window is { Expanded: true } window)
        {
            window.ShowModule("music");
        }
        else
        {
            core.Open("music");
        }
    }

    public override bool HasLayers => true;

    public override bool CanStepBack => _extra != MusicExtra.None;

    public override bool StepBack()
    {
        if (_extra == MusicExtra.Library && _library?.StepBack() == true)
        {
            return true;
        }

        if (_extra == MusicExtra.None)
        {
            return false;
        }

        SetExtra(MusicExtra.None);
        return true;
    }

    private void OnLyrics() => _view.Dispatcher.BeginInvoke(() => _lyrics?.Refresh());

    private void OnChanged()
    {
        _view.Dispatcher.BeginInvoke(() =>
        {
            if (!_active)
            {
                return;
            }

            var p = Core.Playback.Current;
            if (NeedsRebuild(p))
            {
                // restarted by every event: the page is rebuilt once, when the title, the length and the cover have all arrived
                _settle.Stop();
                _settle.Start();
                return;
            }

            // nothing visible changed (or it changed back within the delay): no rebuild
            _settle.Stop();
            Lighten(p);
        });
    }

    /// <summary>Something the page shows changed in a way that needs the page built again: another song, the timeline appearing or going, idle / track, the failure line, a new cover.</summary>
    private bool NeedsRebuild(Playback p) =>
        SongOf(p) != _song || (p.Duration > 0) != _hadTimeline || !p.HasTrack != _idle || Core.Playback.CommandFailed != _commandFailed
        || (p.Duration > 0 && p.CanSeek != _canSeek) || !ReferenceEquals(p.Artwork, _artwork)
        || (p.HasTrack && (p.CanShuffle != _canShuffle || p.CanRepeat != _canRepeat));

    /// <summary>The song as the page shows it: the length is left out (it arrives after the title and must not rebuild the page).</summary>
    private static string SongOf(Playback p) => $"{p.Aumid}|{p.Title}|{p.Artist}|{p.Album}";

    /// <summary>The changes that never rebuild the page: play / pause, and a length that changed while the timeline already shows.</summary>
    private void Lighten(Playback p)
    {
        if (p.IsPlaying != _wasPlaying)
        {
            // play / pause only changes the glyph, the cover's size and the bars: the page is not rebuilt (a rebuild reloads
            // everything on it, the Apple Music queue view included, which would drive Apple Music's window again)
            ApplyPlaying(p);
        }

        if (_timeline is not null && p.Duration > 0 && (int)p.Duration != _shownDuration)
        {
            _shownDuration = (int)p.Duration;
            _timeline.RefreshDuration();
        }

        // shuffle / repeat turned on or off (here or in the player): only the two buttons change
        if (_shuffleButton is not null && p.Shuffle != _shownShuffle)
        {
            StyleToggle(_shuffleButton, G.Shuffle, p.Shuffle == true, p, Strings.Shuffle);
            _shownShuffle = p.Shuffle;
        }

        if (_repeatButton is not null && p.Repeat != _shownRepeat)
        {
            StyleRepeat(_repeatButton, p);
            _shownRepeat = p.Repeat;
        }
    }

    private HoverButton? _shuffleButton;
    private HoverButton? _repeatButton;
    private bool? _shownShuffle;
    private RepeatKind? _shownRepeat;
    private bool _canShuffle;
    private bool _canRepeat;

    /// <summary>The look of a shuffle / repeat toggle: grey glyph when off, the accent colour on a soft round plate when on.</summary>
    private static void StyleToggle(HoverButton button, string glyph, bool active, Playback p, string tip)
    {
        var accent = active ? (p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White) : D.White(0.55);
        var icon = D.I(glyph, 16, accent);
        icon.FontWeight = FontWeights.SemiBold;
        button.Child = active ? new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = D.White(0.12), Child = icon } : icon;
        D.Tip(button, tip);
    }

    private static void StyleRepeat(HoverButton button, Playback p)
    {
        var mode = p.Repeat ?? RepeatKind.None;
        StyleToggle(button, mode == RepeatKind.Track ? G.RepeatOne : G.RepeatAll, mode != RepeatKind.None, p, mode == RepeatKind.Track ? Strings.RepeatOne : Strings.Repeat);
    }

    private void Settle()
    {
        if (!_active || !HasWindow)
        {
            return;
        }

        var p = Core.Playback.Current;
        if (!NeedsRebuild(p))
        {
            Lighten(p);
            return;
        }

        if (_extra == MusicExtra.Lyrics && SongOf(p) != _song)
        {
            Core.Lyrics.Ensure(p);
        }

        RefreshInPlace();
    }

    /// <summary>
    /// Builds this page's content again and has the window measure it again, on the same page object: no new page, no cross-fade, and the
    /// open Library / Lyrics / Queue view is kept (so the list stays where it is and its tiles are not loaded again).
    /// </summary>
    private void RefreshInPlace()
    {
        if (HasWindow)
        {
            Window.BuildOpen(rebuildPage: false);
        }
    }

    private bool _hadTimeline;
    private bool _wasPlaying;
    private bool _commandFailed;
    private TextBlock? _playGlyph;
    private FrameworkElement? _cover;
    private EqBars? _bars;

    private void ApplyPlaying(Playback p)
    {
        _wasPlaying = p.IsPlaying;
        if (_playGlyph is not null)
        {
            _playGlyph.Text = p.IsPlaying ? G.Pause : G.Play;
        }

        if (_bars is not null)
        {
            _bars.Playing = p.IsPlaying;
        }

        if (_cover?.RenderTransform is ScaleTransform scale)
        {
            var to = p.IsPlaying || !p.HasTrack ? 1 : 0.94;
            var animation = D.ReduceMotion ? null : new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(180));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            if (animation is null)
            {
                scale.ScaleX = scale.ScaleY = to;
            }
        }
    }

    private void SetExtra(MusicExtra e)
    {
        var was = _extra;
        _extra = e == _extra ? MusicExtra.None : e;
        if (was == MusicExtra.Library && _extra != MusicExtra.Library)
        {
            _library?.ReleaseNow();
        }

        if (_extra == MusicExtra.Lyrics)
        {
            Core.Lyrics.Ensure(Core.Playback.Current);
        }

        Reopen();
    }

    private void Reopen()
    {
        if (!HasWindow)
        {
            return;
        }

        s_carry = (_extra, DateTime.UtcNow);

        // the rebuilt page gets a new Library box: the keyboard of the focused one is handed over to it
        if (_extra == MusicExtra.Library)
        {
            _library?.Park();
        }

        Window.ReopenPage();
    }

    private void Build()
    {
        _view.Children.Clear();
        var p = Core.Playback.Current;
        _hadTimeline = p.Duration > 0;
        _song = SongOf(p);
        _canSeek = p.CanSeek;
        _artwork = p.Artwork;
        _shownDuration = (int)p.Duration;
        _timeline = null;
        _wasPlaying = p.IsPlaying;
        _commandFailed = Core.Playback.CommandFailed;
        _playGlyph = null;
        _shuffleButton = null;
        _repeatButton = null;
        _shownShuffle = p.Shuffle;
        _shownRepeat = p.Repeat;
        _canShuffle = p.CanShuffle;
        _canRepeat = p.CanRepeat;
        _cover = null;
        _bars = null;
        var idle = !p.HasTrack;
        _idle = idle;
        _amBusy = Core.AppleMusic.Busy;
        _amNote = Core.AppleMusic.Note;
        var awaiting = idle && Core.Playback.AwaitingPlayback;
        var preferred = PlaybackService.ResolvePreferredPlayer(Core.App.Get<string>(K.PreferredPlayer), Core.Playback);
        var stack = new StackPanel { Width = _width, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(BuildHead(p, idle, awaiting, preferred));
        if (p.Duration > 0)
        {
            var tl = new MusicTimeline(Core, p) { Margin = new Thickness(0, 14, 0, 0) };
            _timeline = tl;
            stack.Children.Add(tl);
        }

        var transport = BuildTransport(p, idle, preferred);
        transport.Margin = new Thickness(0, p.Duration > 0 ? 10 : 14, 0, 0);
        stack.Children.Add(transport);
        if (Core.App.Get<bool>(K.MusicShowVolume) && Core.ModuleVisible("mixer"))
        {
            var vol = new VolumeLevel(Core, () => HasWindow ? Window.HoldOpen() : null, null) { Margin = new Thickness(0, 18, 0, 0) };
            stack.Children.Add(vol);
        }

        // the switch chooses what the Library below it shows, so it sits right above it, not between the song and its controls
        stack.Children.Add(BuildSourceSwitch());
        if (_extra != MusicExtra.None)
        {
            // The view is built once per page: a rebuild in place (a track change) puts the same Library / Lyrics / Queue view back, unless
            // what it needs changed (another source, another player's queue).
            var key = ExtraKey();
            if (_extraView is null || key != _extraKey)
            {
                if (_extraView is not null && _extraKey.StartsWith("library", StringComparison.Ordinal))
                {
                    _library?.ReleaseNow();
                }

                _extraKey = key;
                _extraView = _extra switch
                {
                    MusicExtra.Lyrics => _lyrics = new LyricsView(Core, () => HasWindow ? Window.HoldOpen() : null),
                    MusicExtra.Library => BuildBrowser(),
                    _ => BuildQueue(preferred),
                };
            }

            var holder = new Grid { Height = _extraHeight, ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0) };
            holder.Children.Add(Detach.From(_extraView)!);
            stack.Children.Add(holder);
            if (_extra == MusicExtra.Library)
            {
                stack.Children.Add(BuildLibraryGrip(holder));
            }
        }

        _view.Children.Add(stack);
    }

    /// <summary>
    /// The handle under the Library: dragging it makes the island taller or shorter and the height is kept. The page is not rebuilt during the
    /// drag: the Library's holder and the island follow the pointer, and the height is saved when the button goes up.
    /// </summary>
    private FrameworkElement BuildLibraryGrip(FrameworkElement holder)
    {
        var bar = new Border { Width = 44, Height = 4, CornerRadius = new CornerRadius(2), Background = D.White(0.22), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var grip = new Grid { Height = GripHeight, Background = Brushes.Transparent, Cursor = Cursors.SizeNS };
        grip.Children.Add(bar);
        D.Tip(grip, Loc.T("Drag to change the height"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(grip, "Island.Music.LibraryGrip");
        grip.MouseEnter += (_, _) => bar.Background = D.White(0.5);
        grip.MouseLeave += (_, _) => bar.Background = D.White(grip.IsMouseCaptured ? 0.5 : 0.22);
        var dragging = false;
        var startY = 0.0;
        var startHeight = 0.0;
        IDisposable? hold = null;
        void End()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            hold?.Dispose();
            hold = null;
            bar.Background = D.White(grip.IsMouseOver ? 0.5 : 0.22);
            Core.App.Set(K.MusicLibraryHeight, _extraHeight);
        }

        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (!HasWindow)
            {
                return;
            }

            dragging = true;
            startY = grip.PointToScreen(e.GetPosition(grip)).Y;
            startHeight = _extraHeight;
            hold = Window.HoldOpen();
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (!dragging || !HasWindow)
            {
                return;
            }

            // Screen pixels to island units: the display scale and the island's zoom. An island resting on the bottom edge grows upwards.
            var units = VisualTreeHelper.GetDpi(grip).DpiScaleY * Window.Zoom;
            var moved = (grip.PointToScreen(e.GetPosition(grip)).Y - startY) / units * (Window.OpensUp ? -1 : 1);
            var next = Math.Round(IslandSizes.LibraryHeightFor(startHeight + moved, _libraryRoom));
            if (next == _extraHeight)
            {
                return;
            }

            var delta = next - _extraHeight;
            _extraHeight = next;
            holder.Height = next;
            Window.ResizeOpen(delta);
        };
        grip.MouseLeftButtonUp += (_, e) =>
        {
            grip.ReleaseMouseCapture();
            End();
            e.Handled = true;
        };
        grip.LostMouseCapture += (_, _) => End();
        return grip;
    }

    /// <summary>
    /// What the open extra view was built for; when it differs from the last build the view is created again. The Queue includes the song:
    /// "Up next" changes with every track, and a new view reads it again (Apple Music's only when its last read is stale).
    /// </summary>
    private string ExtraKey() => _extra switch
    {
        MusicExtra.Library => "library:" + (MusicSources.Current(Core) ?? MusicSource.Apple),
        MusicExtra.Lyrics => "lyrics",
        _ => "queue:" + (Core.AppleMusic.Available && Core.Playback.Current.Aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            ? "apple"
            : Core.Music.Spotify.Connection == StreamConnection.Connected && Core.Playback.Current.Aumid.Contains("spotify", StringComparison.OrdinalIgnoreCase) ? "spotify" : "other") + ":" + _song,
    };

    /// <summary>The 24 px source switch under the head: Apple Music, Spotify and YouTube Music, always all three.</summary>
    private FrameworkElement BuildSourceSwitch()
    {
        var selected = MusicSources.Selected(Core);
        var plate = new Border { CornerRadius = new CornerRadius(9), Background = D.White(0.065), Padding = new Thickness(2), Height = 24, Margin = new Thickness(0, SwitchGap, 0, 0) };
        var row = new Grid();
        MusicSource[] all = [MusicSource.Apple, MusicSource.Spotify, MusicSource.YouTube];
        for (var i = 0; i < all.Length; i++)
        {
            var source = all[i];
            var isSelected = source == selected;

            // Apple Music is only offered where the island can drive it; the other two are turned on by choosing them
            var offered = source != MusicSource.Apple || Core.AppleMusic.Available;
            var segment = new HoverButton(() => ChooseSource(source)) { CornerRadius = new CornerRadius(7), Lifts = false, Selected = isSelected };
            var label = D.T(MusicSources.Brand(source), 11, isSelected ? FontWeights.SemiBold : FontWeights.Medium, isSelected ? D.Text : D.White(0.6), false, TextTrimming.CharacterEllipsis);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(4, 0, 4, 0);
            segment.Child = label;
            if (!offered)
            {
                segment.IsEnabled = false;
                segment.Opacity = 0.4;
                D.Tip(segment, LibStrings.Missing);
                ToolTipService.SetShowOnDisabled(segment, true);
            }

            System.Windows.Automation.AutomationProperties.SetAutomationId(segment, "Island.Music.Source." + source);
            System.Windows.Automation.AutomationProperties.SetName(segment, MusicSources.Brand(source));
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(segment, i);
            row.Children.Add(segment);
        }

        plate.Child = row;
        return plate;
    }

    /// <summary>
    /// The Library area: the Apple Music <see cref="LibraryPanel"/> or a <see cref="StreamPanel"/> for the source shown. The source switch
    /// is part of the page (see <see cref="BuildSourceSwitch"/>), not of this area.
    /// </summary>
    private FrameworkElement BuildBrowser()
    {
        var current = MusicSources.Current(Core) ?? MusicSource.Apple;
        var window = HasWindow ? Window : null;
        FrameworkElement panel;
        switch (current)
        {
            case MusicSource.Spotify:
                var spotify = new StreamPanel(Core, window, Core.Music.Spotify);
                _library = spotify;
                panel = spotify;
                break;
            case MusicSource.YouTube:
                var youTube = new StreamPanel(Core, window, Core.Music.YouTube);
                _library = youTube;
                panel = youTube;
                break;
            default:
                var apple = new LibraryPanel(Core, window);
                _library = apple;
                panel = apple;
                break;
        }

        return panel;
    }

    /// <summary>
    /// A tap on a segment of the source switch: turns Spotify / YouTube Music on when they are off (nothing is sent until the person connects
    /// or searches), remembers the source, lets the now-playing controls follow a live session of it, and opens the Library on it. Apple Music
    /// only ever shows the island's own Library: its player is never opened and nothing is played.
    /// </summary>
    private void ChooseSource(MusicSource source)
    {
        if (!HasWindow)
        {
            return;
        }

        var selected = MusicSources.Selected(Core);
        if (source == selected && _extra == MusicExtra.Library && MusicSources.Current(Core) == source)
        {
            return;
        }

        if (source == MusicSource.Spotify && !Core.Music.Spotify.ShowInIsland)
        {
            Core.App.Set(K.MusicSpotify, true);
        }
        else if (source == MusicSource.YouTube && !Core.Music.YouTube.ShowInIsland)
        {
            Core.App.Set(K.MusicYouTube, true);
        }

        MusicSources.Choose(Core, source);
        if (source != selected && MusicSources.SessionFor(Core, source) is { } aumid)
        {
            // the controls follow the player of that source; with no live session of it they stay as they are
            Core.Playback.Choose(aumid);
        }

        _library?.ReleaseNow();
        _extra = MusicExtra.Library;
        Reopen();
    }

    private FrameworkElement BuildHead(Playback p, bool idle, bool awaiting, string? preferred)
    {
        var head = new Grid { Height = 56 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Core.Playback.Sources.FirstOrDefault(s => s.Aumid == p.Aumid)?.Icon;

        // Apple Music is never opened from the island (that would show and activate its window): its cover opens the island's own Library
        var appleLibrary = !idle && Core.AppleMusic.Available && p.Aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase);
        var coverBtn = new HoverButton(() =>
        {
            if (idle)
            {
                MusicUi.IdleAction(Core, preferred, AmAction.PlayPause)();
            }
            else if (appleLibrary)
            {
                if (_extra != MusicExtra.Library)
                {
                    // the cover of an Apple Music song opens the Apple Music Library, even when the switcher was left on another source
                    MusicSources.Choose(Core, MusicSource.Apple);
                    SetExtra(MusicExtra.Library);
                }
            }
            else
            {
                PlaybackService.OpenPlayer(p.Aumid);
            }
        }) { Width = 56, Height = 56, CornerRadius = new CornerRadius(10.64), Lifts = false, IsEnabled = (!idle || preferred is not null || MusicUi.HiddenApple(Core, preferred)) && !(idle && MusicUi.HiddenApple(Core, preferred) && Core.AppleMusic.Busy) };
        var cover = Cover.For(p, 56, false, icon);
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        cover.RenderTransform = new ScaleTransform(p.IsPlaying || idle ? 1 : 0.94, p.IsPlaying || idle ? 1 : 0.94);
        coverBtn.Child = cover;
        _cover = cover;
        D.Tip(coverBtn, idle && (preferred is not null || MusicUi.HiddenApple(Core, preferred)) ? MusicUi.IdleLabel(Core, preferred) : appleLibrary ? LibStrings.Library : Strings.MediaNowPlaying);
        head.Children.Add(coverBtn);

        var titles = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Bottom, Height = 38 };
        Grid.SetColumn(titles, 1);
        if (awaiting)
        {
        }
        else if (idle)
        {
            titles.Children.Add(D.T(Strings.MediaNothingPlaying, 14, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
            if (MusicUi.HiddenApple(Core, preferred) && Core.AppleMusic.Busy)
            {
                // the island's request is starting Apple Music or working in it: the person sees that something is happening
                var working = new StackPanel { Orientation = Orientation.Horizontal };
                var spinner = PageUi.Spinner(12);
                spinner.Margin = new Thickness(0, 0, 7, 0);
                spinner.VerticalAlignment = VerticalAlignment.Center;
                working.Children.Add(spinner);
                working.Children.Add(D.T(Core.AppleMusic.Resuming ? LibStrings.Resuming : LibStrings.StartingApple, 13, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
                titles.Children.Add(working);
            }
            else if (MusicUi.HiddenApple(Core, preferred) && Core.AppleMusic.Note is { Error: true } failed)
            {
                titles.Children.Add(D.T(failed.Text, 13, null, D.Orange, false, TextTrimming.CharacterEllipsis));
            }
            else if (preferred is not null || MusicUi.HiddenApple(Core, preferred))
            {
                titles.Children.Add(D.T(MusicUi.IdleLabel(Core, preferred), 13, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
            }
        }
        else
        {
            titles.Children.Add(D.T(p.Display, 14, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
            titles.Children.Add(Core.Playback.CommandFailed
                ? D.T(Strings.PlaybackFailed, 13, null, D.Orange, false, TextTrimming.CharacterEllipsis)
                : D.T(p.Subtitle, 13, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        }

        head.Children.Add(titles);

        // right cluster: toggles + source + bars
        var cluster = new StackPanel { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Right };
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Height = 28 };
        void Toggle(string glyph, string title, MusicExtra extra)
        {
            var b = D.IconButton(glyph, title, () => SetExtra(extra), _extra == extra);
            if (top.Children.Count > 0)
            {
                b.Margin = new Thickness(6, 0, 0, 0);
            }

            top.Children.Add(b);
        }

        if (MusicSources.Any(Core))
        {
            Toggle(G.MusicLibrary, MusicSources.ToggleTitle(Core), MusicExtra.Library);
        }

        if (LyricsOn)
        {
            Toggle(G.Lyrics, Strings.Lyrics, MusicExtra.Lyrics);
        }

        if (QueueOn)
        {
            Toggle(G.List, Strings.Queue, MusicExtra.Queue);
        }

        _appleButton = AppleButtonState();
        if (_appleButton != 0)
        {
            var keeper = Core.AppleMusic.Keeper;
            var show = _appleButton == 1;
            var b = D.IconButton(show ? G.OpenApp : G.EyeOff, show ? LibStrings.ShowApple : LibStrings.HideApple, () =>
            {
                if (show)
                {
                    keeper.Show(true);
                }
                else
                {
                    keeper.HideNow();
                }
            });
            if (top.Children.Count > 0)
            {
                b.Margin = new Thickness(6, 0, 0, 0);
            }

            top.Children.Add(b);
        }

        cluster.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 6, 0, 0) };
        var sources = Core.Playback.Sources;
        if (sources.Count > 1 || Core.Playback.ChosenSource is not null)
        {
            bottom.Children.Add(BuildSourceMenu(sources, p));
        }

        if (appleLibrary && BuildGoToMenu(p) is { } goTo)
        {
            bottom.Children.Add(goTo);
        }

        var bars = new EqBars(4, 2.5, 15, D.White(0.5)) { Playing = p.IsPlaying, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _bars = bars;
        bottom.Children.Add(bars);
        cluster.Children.Add(bottom);
        Grid.SetColumn(cluster, 2);
        head.Children.Add(cluster);
        return head;
    }

    /// <summary>
    /// The "…" of an Apple Music song: "Go to album" / "Go to artist" open the island's Library on them. The song is matched to the library
    /// by its title, artist and album; without a match (or before the library is read) there is no button.
    /// </summary>
    private FrameworkElement? BuildGoToMenu(Playback p)
    {
        var svc = Core.AppleMusic;
        _awaitingLibrary = false;
        if (svc.Db is not { } db)
        {
            // read the library now; the page is refreshed when it arrives (see OnAppleMusic)
            _awaitingLibrary = svc.State is LibraryState.Idle or LibraryState.Loading;
            if (svc.State == LibraryState.Idle)
            {
                _view.Dispatcher.BeginInvoke(() => svc.EnsureLoaded());
            }

            return null;
        }

        if (AmIdle.FindPlaying(db.AllTracks, p.Title, p.Artist, p.Album) is not { } track)
        {
            return null;
        }

        var album = LibrarySearchModel.AlbumOf(db, track);
        var artist = LibrarySearchModel.ArtistOf(db, track);
        if (album is null && artist is null)
        {
            return null;
        }

        HoverButton? btn = null;
        btn = new HoverButton(() => IslandMenu.Show(btn!, GoToItems(album, artist), () => HasWindow ? Window.HoldOpen() : null)) { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Lifts = false, Margin = new Thickness(4, 0, 0, 0) };
        btn.Child = D.I(G.More, 13, D.Secondary);
        D.Tip(btn, LibStrings.More);
        return btn;
    }

    private IEnumerable<IslandMenu.Item> GoToItems(AmAlbum? album, AmArtist? artist)
    {
        if (album is not null)
        {
            yield return new IslandMenu.Item(LibStrings.GoToAlbum, () => GoToLibrary(LibrarySearchModel.ForAlbum(album)));
        }

        if (artist is not null)
        {
            yield return new IslandMenu.Item(LibStrings.GoToArtist, () => GoToLibrary(LibrarySearchModel.ForArtist(artist)));
        }
    }

    /// <summary>Opens the Library extra on Apple Music with an album or artist open (the one that is open already is replaced).</summary>
    private void GoToLibrary(LibContainer container)
    {
        if (!HasWindow)
        {
            return;
        }

        var svc = Core.AppleMusic;
        svc.Ui.PlaylistPid = null;
        svc.Ui.Search = "";
        LibraryNav.ReturnText = "";
        LibraryNav.Scope = LibScope.All;
        LibraryNav.Container = container;
        if (_extra == MusicExtra.Library && MusicSources.Current(Core) == MusicSource.Apple && _library is LibraryPanel panel)
        {
            panel.ShowContainer(container);
        }
        else
        {
            // a new Library view is built on Apple Music and picks the open container up from LibraryNav
            ChooseSource(MusicSource.Apple);
        }
    }

    private FrameworkElement BuildSourceMenu(IReadOnlyList<PlayerSource> sources, Playback p)
    {
        HoverButton? btn = null;
        var current = sources.FirstOrDefault(s => s.Aumid == Core.Playback.EffectiveSource);
        btn = new HoverButton(() => IslandMenu.Show(btn!, SourceItems(sources), () => HasWindow ? Window.HoldOpen() : null)) { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Lifts = false };
        if (current?.Icon is { } ic)
        {
            btn.Child = new Image { Source = ic, Width = 18, Height = 18 };
        }
        else
        {
            btn.Child = D.I(G.App, 13, D.Secondary);
        }

        D.Tip(btn, $"{Strings.PlaybackSource}: {current?.Name ?? Strings.AutomaticSource}");
        return btn;
    }

    private IEnumerable<IslandMenu.Item> SourceItems(IReadOnlyList<PlayerSource> sources)
    {
        yield return new IslandMenu.Item(Strings.AutomaticSource, () => Core.Playback.Choose(null), Core.Playback.ChosenSource is null);
        yield return IslandMenu.Sep();
        foreach (var s in sources)
        {
            var id = s.Aumid;
            yield return new IslandMenu.Item(s.Name, () => Core.Playback.Choose(id), Core.Playback.EffectiveSource == id);
        }
    }

    private FrameworkElement BuildTransport(Playback p, bool idle, string? preferred)
    {
        var grid = new Grid { Height = 44 };
        for (var i = 0; i < 5; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // the last slot holds repeat and the output menu side by side when the player offers repeat
        var tail = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(tail, 4);
        var showRepeat = !idle && p.CanRepeat;
        var showOutput = Core.Ctx.Runtime.IsAvailable("mixer") || Core.Audio.Available;

        HoverButton Btn(int slot, string glyph, double size, Action click, bool enabled = true, string? tip = null, bool visible = true, Brush? tint = null, bool plate = false, double width = 44, Panel? host = null)
        {
            var b = new HoverButton(click) { Width = width, Height = 44, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center };
            var ic = D.I(glyph, size, tint ?? D.Text);
            ic.FontWeight = FontWeights.SemiBold;
            if (plate)
            {
                var circle = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = D.White(0.12), Child = ic };
                b.Child = circle;
            }
            else
            {
                b.Child = ic;
            }

            b.IsEnabled = enabled;
            b.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (tip is not null)
            {
                D.Tip(b, tip);
            }

            if (host is not null)
            {
                host.Children.Add(b);
            }
            else
            {
                Grid.SetColumn(b, slot);
                grid.Children.Add(b);
            }

            return b;
        }

        if (idle)
        {
            if (preferred is not null || MusicUi.HiddenApple(Core, preferred))
            {
                var tip = MusicUi.IdleLabel(Core, preferred);
                // while the island's request works in Apple Music the buttons wait (a second press would only restart it) and Play shows a spinner
                var working = MusicUi.HiddenApple(Core, preferred) && Core.AppleMusic.Busy;
                Btn(1, G.Previous, 19, MusicUi.IdleAction(Core, preferred, AmAction.SkipBack), !working, tip);
                var idlePlay = Btn(2, G.Play, 28, MusicUi.IdleAction(Core, preferred, AmAction.PlayPause), !working, tip);
                Btn(3, G.Next, 19, MusicUi.IdleAction(Core, preferred, AmAction.SkipForward), !working, tip);
                if (working)
                {
                    var spinner = PageUi.Spinner(22);
                    spinner.HorizontalAlignment = HorizontalAlignment.Center;
                    spinner.VerticalAlignment = VerticalAlignment.Center;
                    idlePlay.Child = spinner;
                }
            }
            else
            {
                var hint = D.T(Strings.MusicHint, 12, null, D.Secondary, true);
                hint.TextAlignment = TextAlignment.Center;
                hint.MaxHeight = 32;
                hint.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumnSpan(hint, 5);
                grid.Children.Add(hint);
            }
        }
        else
        {
            var accent = p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White;
            if (p.CanShuffle)
            {
                _shuffleButton = Btn(0, G.Shuffle, 16, Core.Playback.ToggleShuffle, true, Strings.Shuffle, true, p.Shuffle == true ? accent : D.White(0.55), plate: p.Shuffle == true);
            }

            if (showRepeat)
            {
                _repeatButton = Btn(4, G.RepeatAll, 16, Core.Playback.ToggleRepeat, true, Strings.Repeat, true, width: showOutput ? 32 : 44, host: tail);
                StyleRepeat(_repeatButton, p);
            }

            Btn(1, G.Previous, 19, Core.Playback.Previous, p.CanPrevious, Strings.Previous, p.CanPrevious || true);
            var play = Btn(2, p.IsPlaying ? G.Pause : G.Play, 28, Core.Playback.TogglePlayPause, p.CanPlayPause || true, Strings.PlayPause);
            _playGlyph = play.Child as TextBlock;
            Btn(3, G.Next, 19, Core.Playback.Next, p.CanNext, Strings.Next);
        }

        if (showOutput)
        {
            var outputs = Core.Audio.Outputs();
            var def = outputs.FirstOrDefault(o => o.IsDefault);
            HoverButton? ob = null;
            ob = Btn(4, AudioService.GlyphFor(def), 16, () => IslandMenu.Show(ob!, OutputItems(outputs), () => HasWindow ? Window.HoldOpen() : null), true, def?.Name ?? Strings.OutputUnavailable, true, D.White(0.55), width: showRepeat ? 32 : 44, host: showRepeat ? tail : null);
        }

        if (tail.Children.Count > 0)
        {
            grid.Children.Add(tail);
        }

        return grid;
    }

    private IEnumerable<IslandMenu.Item> OutputItems(List<AudioEndpointInfo> outputs)
    {
        foreach (var o in outputs)
        {
            var id = o.Id;
            yield return new IslandMenu.Item(o.Name, () => Core.Audio.SetDefaultOutput(id), o.IsDefault);
        }

        if (Core.ModuleVisible("mixer"))
        {
            yield return IslandMenu.Sep();
            yield return new IslandMenu.Item(Strings.VolumeMixer, () => { if (HasWindow) { Window.ShowModule("mixer"); } }, Glyph: G.Mixer);
        }
    }

    private FrameworkElement BuildQueue(string? preferred)
    {
        if (Core.AppleMusic.Available && Core.Playback.Current.Aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
        {
            return new AppleQueueView(Core);
        }

        if (Core.Music.Spotify.Connection == StreamConnection.Connected && Core.Playback.Current.Aumid.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return new SpotifyQueueView(Core);
        }

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Height = 24 };
        header.Children.Add(D.T(Strings.Queue, 13, FontWeights.SemiBold, D.Text));
        var refresh = D.IconButton(G.Refresh, Strings.Refresh, () => { }, size: 24, glyphSize: 11);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(refresh);
        grid.Children.Add(header);
        var target = preferred ?? Core.Playback.Current.Aumid;
        var empty = D.EmptyView(G.List, Strings.QueueUnavailable, D.PillButton(Strings.OpenPlayer, () =>
        {
            // never open Apple Music itself (its window would show): the island's Library is the way in
            if (Core.AppleMusic.Available && target.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
            {
                SetExtra(MusicExtra.Library);
            }
            else
            {
                PlaybackService.OpenPlayer(target);
            }
        }, true));
        Grid.SetRow(empty, 1);
        grid.Children.Add(empty);
        return grid;
    }
}

/// <summary>The lyrics area (<c>NotchLyricsView</c>): synced list, plain text or the empty / loading / failed states.</summary>
internal sealed class LyricsView : Grid
{
    private readonly IslandCore _core;
    private readonly Func<IDisposable?> _hold;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private ScrollViewer? _scroll;
    private readonly List<TextBlock> _lines = [];
    private int _active = -2;

    public LyricsView(IslandCore core, Func<IDisposable?> hold)
    {
        _core = core;
        _hold = hold;
        Loaded += (_, _) =>
        {
            Refresh();
            _tick.Start();
        };
        Unloaded += (_, _) => _tick.Stop();
        _tick.Tick += (_, _) => Highlight();
    }

    public void Refresh()
    {
        Children.Clear();
        _lines.Clear();
        _active = -2;
        var s = _core.Lyrics;
        var p = _core.Playback.Current;
        if (s.Instrumental)
        {
            Children.Add(D.EmptyView(G.Music, Strings.LyricsInstrumental, ImportPill()));
            return;
        }

        if (s.State == LyricsState.Ready && s.Synced && p.HasPosition)
        {
            BuildSynced();
            return;
        }

        if (s.State == LyricsState.Ready && (s.Plain.Length > 0 || s.Synced))
        {
            BuildPlain(p);
            return;
        }

        if (s.State == LyricsState.Loading)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = PageUi.Spinner(16);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(sp);
            var t = D.T(Strings.LyricsLoading, 12, null, D.Secondary);
            t.Margin = new Thickness(0, 8, 0, 8);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(t);
            stack.Children.Add(ImportPill());
            Children.Add(stack);
            return;
        }

        var failed = s.State == LyricsState.Failed;
        var online = s.Online;
        var msg = string.Join("\n", new[] { failed ? Strings.LyricsFailed : (online ? Strings.LyricsUnavailable : null), online ? null : Strings.LyricsOnlineHint }.Where(m => m is not null));
        FrameworkElement next = online
            ? D.PillButton(Strings.LyricsRetry, () => _core.Lyrics.Ensure(p, true), true)
            : D.PillButton(Strings.FindLyricsOnline, () => { _core.App.Set(K.LyricsOnline, true); _core.Lyrics.Ensure(p, true); }, true);
        var empty = D.EmptyView(failed ? G.Warning : G.Lyrics, msg.Length == 0 ? Strings.LyricsUnavailable : msg, next, ImportPill());
        Children.Add(empty);
    }

    private FrameworkElement ImportPill()
    {
        var b = D.PillButton(Strings.ImportLyrics, Import);
        b.Margin = new Thickness(6, 0, 0, 0);
        return b;
    }

    private void Import()
    {
        using var hold = _hold();
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Lyrics (*.lrc;*.txt)|*.lrc;*.txt", Title = Strings.ImportLyrics.TrimEnd('…') };
        if (dlg.ShowDialog() == true)
        {
            _core.Lyrics.Import(dlg.FileName, _core.Playback.Current.Duration);
        }
    }

    private void BuildPlain(Playback p)
    {
        var s = _core.Lyrics;
        var stack = new StackPanel();
        if (!p.HasPosition)
        {
            stack.Children.Add(D.T(Strings.LyricsNoPosition, 12, null, D.Secondary));
        }

        var text = s.Plain.Length > 0 ? s.Plain : string.Join("\n", s.Lines.Select(l => l.Text));
        var t = D.T(text, 15, FontWeights.Medium, D.Text, true);
        t.Margin = new Thickness(0, 6, 0, 6);
        stack.Children.Add(t);
        var import = new HoverButton(Import) { Height = 22, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(D.I("", 11, D.Secondary));
        var lab = D.T(Strings.ImportLyrics, 11, null, D.Secondary);
        lab.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(lab);
        import.Child = row;
        stack.Children.Add(import);
        Children.Add(PageUi.VScroll(stack));
    }

    private void BuildSynced()
    {
        var s = _core.Lyrics;
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stack = new StackPanel { Margin = new Thickness(0, 60, 0, 60) };
        stack.Children.Add(D.T(Strings.LyricsWaiting, 12, null, D.Secondary));
        foreach (var l in s.Lines)
        {
            var t = D.T(string.IsNullOrWhiteSpace(l.Text) ? "♪" : l.Text, 17, FontWeights.Bold, D.White(0.35), true);
            t.Margin = new Thickness(0, 6, 0, 6);
            _lines.Add(t);
            stack.Children.Add(t);
        }

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = stack };
        g.Children.Add(_scroll);
        var timing = new Grid { Height = 18, Margin = new Thickness(0, 4, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(D.I("", 10, D.Secondary));
        var tl = D.T(Strings.LyricsTiming, 11, null, D.Secondary);
        tl.Margin = new Thickness(5, 0, 0, 0);
        left.Children.Add(tl);
        timing.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var offset = D.Digits(FormatOffset(s.Offset), 11, null, D.Secondary);
        offset.MinWidth = 44;
        offset.TextAlignment = TextAlignment.Center;
        offset.VerticalAlignment = VerticalAlignment.Center;
        HoverButton Step(string text, string tip, double delta)
        {
            var b = new HoverButton(() => { s.Offset = Math.Clamp(Math.Round((s.Offset + delta) * 4) / 4, -10, 10); offset.Text = FormatOffset(s.Offset); }) { Width = 24, Height = 18, CornerRadius = new CornerRadius(6) };
            b.Child = D.T(text, 12, FontWeights.Medium, D.Secondary);
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            D.Tip(b, tip);
            return b;
        }

        right.Children.Add(Step("−", Strings.LyricsEarlier, -0.25));
        var resetHolder = new HoverButton(() => { s.Offset = 0; offset.Text = FormatOffset(0); }) { CornerRadius = new CornerRadius(6), Child = offset };
        D.Tip(resetHolder, Strings.LyricsReset);
        right.Children.Add(resetHolder);
        right.Children.Add(Step("+", Strings.LyricsLater, 0.25));
        timing.Children.Add(right);
        Grid.SetRow(timing, 1);
        g.Children.Add(timing);
        Children.Add(g);
        Highlight();
    }

    private static string FormatOffset(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private void Highlight()
    {
        if (_scroll is null || _lines.Count == 0)
        {
            return;
        }

        var idx = _core.Lyrics.ActiveIndex(_core.Playback.Current.Position);
        if (idx == _active)
        {
            return;
        }

        _active = idx;
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].Foreground = i == idx ? D.Text : D.White(0.35);
        }

        if (idx >= 0 && idx < _lines.Count)
        {
            var pt = _lines[idx].TransformToAncestor(_scroll).Transform(new Point(0, 0));
            _scroll.ScrollToVerticalOffset(Math.Max(0, _scroll.VerticalOffset + pt.Y - (_scroll.ViewportHeight / 2) + (_lines[idx].ActualHeight / 2)));
        }
    }
}
