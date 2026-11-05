// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Agents;

/// <summary>What the mascot is doing: nothing in particular, typing on a laptop while Claude works, or waving because Claude needs the person.</summary>
internal enum MascotMood
{
    Idle,
    Working,
    Waiting,
}

/// <summary>One step of a clip: the rows of the picture and how many milliseconds it stays on screen.</summary>
internal readonly record struct MascotFrame(string[] Rows, int Milliseconds);

/// <summary>
/// The pixel art of the Claude Code mascot and the rules that play it. The animation is plain data: a handful of named poses drawn as
/// text grids, grouped into clips. Pure logic with no WPF types, so the harness can check every frame; <see cref="ClaudeMascotView"/>
/// only draws what this returns.
/// </summary>
/// <remarks>
/// Every frame is the same <see cref="Columns"/> by <see cref="Rows"/> box and every pose stands on the last row, so switching poses
/// never moves the floor. The ink characters are 'O' the front of the body, 'D' its shaded side, 'e' an eye, 'G' the laptop and 'W'
/// the lighter edge of the laptop; '.' is empty.
/// </remarks>
internal static class ClaudeMascotArt
{
    public const int Columns = 17;
    public const int Rows = 11;

    /// <summary>The characters a frame may contain besides the empty cell '.'.</summary>
    public const string Inks = "OeDGW";

    /// <summary>
    /// The rows that stand for the requested height when sizing a cell. The standing body is 8 rows; the rest is air for the lifted laptop,
    /// and this value keeps the cells the same size as they were for the previous 10-row picture.
    /// </summary>
    public const int HeightRows = 10;

    /// <summary>The first row and the row count of the standing body, which is what gets centred vertically so it lines up with the text beside it.</summary>
    public const int BodyTop = 3;
    public const int BodyRows = 8;

    /// <summary>Seconds each wave position lasts.</summary>
    private const double WaveSeconds = 0.4;

    // The standing mascot from the front, 8 rows tall. The arms are the two cells either side of rows 5 and 6.
    private static readonly string[] s_front =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".......OeOOOOeO..",
        ".....OOOOOOOOOOOO",
        ".....OOOOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The same body with both arms raised by one row: the wave while Claude waits for the person.
    private static readonly string[] s_wave =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".....OOOeOOOOeOOO",
        ".....OOOOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The body settling back after the laptop is gone: arms one row lower than in the front pose.
    private static readonly string[] s_frontLow =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".......OeOOOOeO..",
        ".......OOOOOOOO..",
        ".....OOOOOOOOOOOO",
        ".....OOOOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The body squashed by half a row with its right arm up and a bump on the left: the start and the end of a wink.
    private static readonly string[] s_squash =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOOOO",
        ".......OOOOOOOOOO",
        ".......OeOOOOeO..",
        "......OOOOOOOOO..",
        "......OOOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The wink: the right eye is a short dash. The bump on the left moves a row between the two variants, which is the small bob in the clip.
    private static readonly string[] s_winkA =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOOOO",
        ".......OOOOOOOOOO",
        "......OOeOOOeeO..",
        "......OOOOOOOOO..",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    private static readonly string[] s_winkB =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOOOO",
        ".......OOOOOOOOOO",
        ".......OeOOOeeO..",
        "......OOOOOOOOO..",
        "......OOOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The left arm reaches up for a small grey wedge, the closed laptop.
    private static readonly string[] s_grab =
    [
        ".................",
        ".................",
        ".....WW..........",
        "...GGG.OOOOOOOO..",
        ".....OOOeOOOOeO..",
        ".....OOOOOOOOOO..",
        "......OOOOOOOOOOO",
        ".......OOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The laptop held up open as an L: a screen going up from the left end of its base, with the arm under the base.
    private static readonly string[] s_lift =
    [
        "...W.............",
        "...W.............",
        "...WGGG..........",
        ".....OOOOOOOOOO..",
        ".....OOOeOOOOeO..",
        ".......OOOOOOOO..",
        ".......OOOOOOOOOO",
        ".......OOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The laptop swings down on the arm: a blob for the screen, then the base reaching the hand.
    private static readonly string[] s_swing =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".GG....OOOOOOOO..",
        ".GG....OeOOOOeOOO",
        "..GG..OOOOOOOOOOO",
        "...GGOOOOOOOOOO..",
        ".....OOOOOOOOOO..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The laptop lands on the floor to the left as a long diagonal screen and a base; the arm drops to the floor.
    private static readonly string[] s_land =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        ".......OeOOOOeO..",
        ".......OOOOOOOOOO",
        ".......OOOOOOOOOO",
        ".G...OOOOOOOOOO..",
        "..G..OOO.O..O.O..",
        "...GGG.O.O..O.O..",
    ];

    // The laptop settles flatter and the arms come back up to the front height.
    private static readonly string[] s_set =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        ".....OOOeOOOOeOOO",
        ".....OOOOOOOOOOOO",
        ".......OOOOOOOO..",
        ".......OOOOOOOO..",
        "GG.....O.O..O.O..",
        "..GGGG.O.O..O.O..",
    ];

    // The turn begins: the body stretches up with two ear-like corners and a shaded right edge, the first hint of the side view.
    private static readonly string[] s_turn =
    [
        ".................",
        "......OO...OO....",
        "......OO...OO....",
        "......OOOOOOOOD..",
        "......OOOOOOOOD..",
        ".......eOOOOeOD..",
        ".......OOOOOOOD..",
        ".......OOOOOOOD..",
        ".G.....OOOOOOOD..",
        "..G....O.O..O.D..",
        "...GGG.O.O..O.D..",
    ];

    // The side view arrives: a lighter face on the left, the shaded side on the right, and a dark dash for the mouth.
    private static readonly string[] s_head =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        ".....OOOOOOODD...",
        "....OOeOOOeODD...",
        "....OOOOOOOODD...",
        "....eeOOOOOODD...",
        ".G..OOOOOOOODD...",
        "..G.OOOOOOOODD...",
        "...GOOO.O..O.O...",
    ];

    // The three typing poses of the side view. The body and the legs stay put; only the arm moves on the keys.
    // A: the arm rests on the laptop, a chin forward in rows 7 and 8.
    private static readonly string[] s_typeA =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        "......OOOOOODD...",
        "......OOOOOODD...",
        "......eOOOeODD...",
        "....OOOOOOOODD...",
        ".G..OOOOOOOODD...",
        "..G...OOOOOODD...",
        "...GGGO.O..O.O...",
    ];

    // B: the hand comes down on the keys, with a dark dash above it.
    private static readonly string[] s_typeB =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        "......OOOOOODD...",
        "......OOOOOODD...",
        "......eOOOeODD...",
        ".....OOOOOOODD...",
        ".G...eOOOOOODD...",
        "..G.OOOOOOOODD...",
        "...GOOO.O..O.O...",
    ];

    // C: the arm is a taller block, raised from the keys.
    private static readonly string[] s_typeC =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        "......OOOOOODD...",
        "......OOOOOODD...",
        "....OOeOOOeODD...",
        "....OOOOOOOODD...",
        ".G..OOOOOOOODD...",
        "..G..OOOOOOODD...",
        "...GGGO.O..O.O...",
    ];

    // C again, with the arm reaching all the way down to the floor.
    private static readonly string[] s_typeC2 =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        "......OOOOOODD...",
        "......OOOOOODD...",
        "....OOeOOOeODD...",
        "....OOOOOOOODD...",
        ".G..OOOOOOOODD...",
        "..G.OOOOOOOODD...",
        "...GOOO.O..O.O...",
    ];

    // The arm picks the laptop up and lifts it off the floor, the head turned toward it.
    private static readonly string[] s_pick =
    [
        ".................",
        ".................",
        ".................",
        ".................",
        "......OOOOOOODD..",
        ".....OOeOOeOODD..",
        ".G..OOOOOOOOODD..",
        "..G.eeOOOOOOODD..",
        "...GOOOOOOOOODD..",
        "....OOOOOOOOODD..",
        "....OOO.O..O.O...",
    ];

    // The laptop folded into a small grey wedge in the hand while the body comes back to the front.
    private static readonly string[] s_wedge =
    [
        ".................",
        ".................",
        ".................",
        ".......OOOOOOOD..",
        ".......OOOOOOOD..",
        ".......eOOOOeOD..",
        "....WGOOOOOOOODO.",
        "...GGGOOOOOOOODO.",
        "...GGGGOOOOOOOD..",
        ".......O.O..O.O..",
        ".......O.O..O.O..",
    ];

    // The clips below are the recording read frame by frame at 30 frames a second: each line is one distinct picture and how long it held.

    /// <summary>The Claude Code session opens: stand, wink, grab a laptop, lift it, set it on the floor and turn to face it.</summary>
    private static readonly MascotFrame[] s_intro =
    [
        new(s_front, 200),
        new(s_winkA, 100),
        new(s_winkB, 67),
        new(s_winkA, 100),
        new(s_winkB, 133),
        new(s_grab, 100),
        new(s_lift, 167),
        new(s_swing, 67),
        new(s_land, 67),
        new(s_set, 133),
        new(s_turn, 67),
        new(s_head, 67),
    ];

    /// <summary>The typing, repeated for as long as Claude works. The arm alternates between the three poses at the pace of the recording.</summary>
    private static readonly MascotFrame[] s_typing =
    [
        new(s_typeA, 100),
        new(s_typeB, 67),
        new(s_typeC, 100),
        new(s_typeA, 100),
        new(s_typeB, 67),
        new(s_typeC, 67),
        new(s_typeA, 67),
        new(s_typeB, 133),
        new(s_typeA, 100),
        new(s_typeB, 133),
        new(s_typeA, 133),
        new(s_typeB, 100),
        new(s_typeC, 67),
        new(s_typeA, 67),
        new(s_typeB, 67),
        new(s_typeC2, 133),
    ];

    /// <summary>The work ends: pick the laptop up, fold it, and settle back into the standing pose.</summary>
    private static readonly MascotFrame[] s_outro =
    [
        new(s_pick, 100),
        new(s_wedge, 67),
        new(s_squash, 233),
        new(s_frontLow, 133),
    ];

    /// <summary>The wink an idle mascot plays now and then, taken from the wink of the recording.</summary>
    private static readonly MascotFrame[] s_wink =
    [
        new(s_squash, 67),
        new(s_winkA, 100),
        new(s_winkB, 67),
        new(s_winkA, 100),
        new(s_squash, 67),
    ];

    private static readonly double s_introSeconds = TotalMilliseconds(s_intro) / 1000.0;
    private static readonly double s_typingSeconds = TotalMilliseconds(s_typing) / 1000.0;
    private static readonly double s_outroSeconds = TotalMilliseconds(s_outro) / 1000.0;
    private static readonly double s_winkSeconds = TotalMilliseconds(s_wink) / 1000.0;
    private static readonly double s_laptopAppearsSeconds = SecondsBeforeLaptop(s_intro);

    /// <summary>The standing pose.</summary>
    public static string[] Front => s_front;

    /// <summary>The standing pose with the arms up, the wave.</summary>
    public static string[] Wave => s_wave;

    /// <summary>The pose shown instead of the typing loop when animation is off.</summary>
    public static string[] TypingStill => s_typeA;

    /// <summary>The opening of a session, played once.</summary>
    public static IReadOnlyList<MascotFrame> Intro => s_intro;

    /// <summary>The typing loop that follows the intro.</summary>
    public static IReadOnlyList<MascotFrame> TypingLoop => s_typing;

    /// <summary>The close of a session, played once when the work ends.</summary>
    public static IReadOnlyList<MascotFrame> Outro => s_outro;

    /// <summary>The wink of an idle mascot.</summary>
    public static IReadOnlyList<MascotFrame> Wink => s_wink;

    /// <summary>Seconds into the intro at which the laptop first shows; stopping before that needs no outro, as there is nothing to put away.</summary>
    public static double LaptopAppearsSeconds => s_laptopAppearsSeconds;

    /// <summary>The length of a clip in milliseconds.</summary>
    public static int TotalMilliseconds(IReadOnlyList<MascotFrame> clip)
    {
        var total = 0;
        foreach (var frame in clip)
        {
            total += frame.Milliseconds;
        }

        return total;
    }

    // The time before the first frame that carries laptop ink.
    private static double SecondsBeforeLaptop(IReadOnlyList<MascotFrame> clip)
    {
        var milliseconds = 0;
        foreach (var frame in clip)
        {
            if (frame.Rows.Any(row => row.Contains('G') || row.Contains('W')))
            {
                break;
            }

            milliseconds += frame.Milliseconds;
        }

        return milliseconds / 1000.0;
    }

    /// <summary>The picture of a clip <paramref name="seconds"/> after it started; the last picture once the clip is over, the first for a negative time.</summary>
    public static string[] FrameAt(IReadOnlyList<MascotFrame> clip, double seconds)
    {
        var left = (int)Math.Floor(Math.Max(0, seconds) * 1000.0);
        foreach (var frame in clip)
        {
            if (left < frame.Milliseconds)
            {
                return frame.Rows;
            }

            left -= frame.Milliseconds;
        }

        return clip[^1].Rows;
    }

    /// <summary>The picture <paramref name="seconds"/> after the mascot started working: the intro once, then the typing loop for as long as it lasts.</summary>
    public static string[] WorkingFrameAt(double seconds) =>
        seconds < s_introSeconds ? FrameAt(s_intro, seconds) : FrameAt(s_typing, (seconds - s_introSeconds) % s_typingSeconds);

    /// <summary>The picture of the closing clip <paramref name="seconds"/> after the work ended; null once the mascot is standing again.</summary>
    public static string[]? OutroFrameAt(double seconds) => seconds < s_outroSeconds ? FrameAt(s_outro, seconds) : null;

    /// <summary>True when the laptop is out after <paramref name="workedSeconds"/> of work, so the closing clip has something to put away.</summary>
    public static bool NeedsOutro(double workedSeconds) => workedSeconds >= s_laptopAppearsSeconds;

    /// <summary>
    /// The picture <paramref name="seconds"/> after the mascot appeared. <paramref name="seed"/> spreads the winks of several mascots apart.
    /// With reduced motion nothing moves, but a working mascot keeps its typing pose and a waiting one keeps its arms up so the state still shows.
    /// </summary>
    public static string[] FrameAt(MascotMood mood, double seconds, int seed, bool reduceMotion)
    {
        if (reduceMotion)
        {
            return mood switch
            {
                MascotMood.Working => s_typeA,
                MascotMood.Waiting => s_wave,
                _ => s_front,
            };
        }

        switch (mood)
        {
            case MascotMood.Working:
                return WorkingFrameAt(seconds);
            case MascotMood.Waiting:
                return (long)Math.Floor(seconds / WaveSeconds) % 2 == 1 ? s_wave : s_front;
            default:
                var period = 4.0 + (((seed % 1500) + 1500) % 1500) / 1000.0;
                var phase = seconds % period;
                return seconds > 1 && phase < s_winkSeconds ? FrameAt(s_wink, phase) : s_front;
        }
    }

    /// <summary>
    /// How long until the picture of <paramref name="mood"/> changes again, so an idle or waiting mascot can sleep instead of ticking
    /// 30 times a second. A working mascot (and a wink in progress) steps every frame. Never less than one frame.
    /// </summary>
    public static double SecondsUntilChange(MascotMood mood, double seconds, int seed)
    {
        const double frame = 1.0 / 30;
        const double margin = 0.002;
        switch (mood)
        {
            case MascotMood.Working:
                return frame;
            case MascotMood.Waiting:
                return Math.Max(frame, WaveSeconds - (seconds % WaveSeconds) + margin);
            default:
                if (seconds <= 1)
                {
                    return Math.Max(frame, 1 - seconds + margin);
                }

                var period = 4.0 + (((seed % 1500) + 1500) % 1500) / 1000.0;
                var phase = seconds % period;
                return phase < s_winkSeconds ? frame : Math.Max(frame, period - phase + margin);
        }
    }

    /// <summary>Every distinct picture the mascot can show, for the harness and for anything that wants to check them all.</summary>
    public static IEnumerable<string[]> AllFrames() =>
        new[] { s_front, s_wave }
            .Concat(s_intro.Select(f => f.Rows))
            .Concat(s_typing.Select(f => f.Rows))
            .Concat(s_outro.Select(f => f.Rows))
            .Concat(s_wink.Select(f => f.Rows))
            .Distinct();

    /// <summary>
    /// Whole device pixels to move an art of <paramref name="artPixels"/> so it sits in the middle of a box of <paramref name="boxPixels"/>.
    /// Rounded down to a whole pixel so every cell edge stays on the device grid; negative when the art is larger than the box.
    /// </summary>
    public static int CenterOffsetPixels(int boxPixels, int artPixels) => (boxPixels - artPixels) >> 1;

    /// <summary>
    /// Whole device pixels per art cell for a target height. A whole number keeps every edge on a pixel boundary at any scale,
    /// so the art never blurs; the picture may come out a little smaller or larger than asked for.
    /// </summary>
    public static int CellPixels(double targetHeightDip, double deviceScale) =>
        Math.Max(1, (int)Math.Round(targetHeightDip * deviceScale / HeightRows, MidpointRounding.AwayFromZero));

    /// <summary>Merges horizontal neighbours of the same ink into one run, so a frame is drawn with a few rectangles instead of one per cell.</summary>
    public static IEnumerable<(int Row, int Col, int Len, char Ink)> Runs(string[] frame)
    {
        for (var r = 0; r < frame.Length; r++)
        {
            var row = frame[r];
            var c = 0;
            while (c < row.Length)
            {
                var ink = row[c];
                var end = c;
                while (end + 1 < row.Length && row[end + 1] == ink)
                {
                    end++;
                }

                if (ink != '.')
                {
                    yield return (r, c, end - c + 1, ink);
                }

                c = end + 1;
            }
        }
    }
}

/// <summary>
/// The mascot drawn from code as a pixel grid, never a bitmap. Cells are whole device pixels, edges are aliased and the origin is snapped
/// to the device grid, so it stays crisp at 100 to 200 percent scaling and under the island's zoom.
/// </summary>
internal sealed class ClaudeMascotView : FrameworkElement
{
    private static readonly Brush s_body = Frozen(ClaudeMark.Orange);

    // The shaded side of the body in the side view, measured from the recording.
    private static readonly Brush s_side = Frozen(Color.FromRgb(0xC0, 0x68, 0x4C));

    // Black on purpose: the eyes are painted rather than left transparent so they read on a light preview too.
    private static readonly Brush s_eye = Frozen(Colors.Black);

    // The laptop is a mid grey with a lighter edge on its screen.
    private static readonly Brush s_laptop = Frozen(Color.FromRgb(0x8C, 0x8C, 0x8C));
    private static readonly Brush s_laptopEdge = Frozen(Color.FromRgb(0xB8, 0xB8, 0xB8));

    private readonly double _target;
    private readonly double _box;
    private readonly int _seed;
    private MascotMood _mood;
    private string[] _frame;
    private int _cell = 1;
    private IDisposable? _subscription;
    private DispatcherTimer? _sleepTimer;
    private double _start;
    private bool _leaving;
    private double _leaveStart;

    // The island rebuilds its strip and cards often while an agent works. Every working mascot shares one start time, so a new view picks
    // up the typing where the last one was instead of replaying the laptop intro, and two mascots on screen type in step.
    private static double s_workingSince = double.NaN;
    private static double s_workingSeen = double.NegativeInfinity;

    // A working mascot that was on screen this recently counts as the same stretch of work.
    private const double WorkingGapSeconds = 2.0;

    public ClaudeMascotView(double targetHeight, MascotMood mood)
    {
        _target = targetHeight;
        _box = targetHeight * 1.45 + 1;
        _mood = mood;
        _seed = GetHashCode() & int.MaxValue;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        _frame = ClaudeMascotArt.FrameAt(mood, 0, _seed, true);
        Loaded += (_, _) =>
        {
            Subscribe();
            InvalidateMeasure();
            InvalidateVisual();
        };
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) =>
        {
            Unsubscribe();
            Subscribe();
        };
    }

    /// <summary>The mood to show; changing it restarts the animation.</summary>
    public MascotMood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value)
            {
                return;
            }

            var wasWorking = _mood == MascotMood.Working;
            _mood = value;
            if (wasWorking && _subscription is not null && ClaudeMascotArt.NeedsOutro(AgentClock.Instance.Time - _start))
            {
                // Keep the clock running and put the laptop away before taking up the new mood.
                _leaving = true;
                _leaveStart = AgentClock.Instance.Time;
                Step(_leaveStart);
                return;
            }

            Unsubscribe();
            Subscribe();
        }
    }

    /// <summary>True when the mascot is animating right now.</summary>
    public bool IsAnimating => _subscription is not null || _sleepTimer is not null;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Subscribe()
    {
        if (!IsLoaded || !IsVisible || _subscription is not null)
        {
            return;
        }

        if (AgentClock.ReduceMotion)
        {
            SetFrame(ClaudeMascotArt.FrameAt(_mood, 0, _seed, true));
            return;
        }

        var now = AgentClock.Instance.Time;
        _start = now;
        if (_mood == MascotMood.Working)
        {
            if (double.IsNaN(s_workingSince) || now - s_workingSeen > WorkingGapSeconds)
            {
                s_workingSince = now;
            }

            _start = s_workingSince;
            s_workingSeen = now;
        }

        SetFrame(ClaudeMascotArt.FrameAt(_mood, now - _start, _seed, false));
        if (_mood == MascotMood.Working)
        {
            _subscription = AgentClock.Instance.Subscribe(Step);
        }
        else
        {
            Sleep(now);
        }
    }

    /// <summary>
    /// Standing and waiting poses change rarely: a one-shot timer wakes the mascot just when the picture changes (a wink, a wave)
    /// instead of keeping the shared 30 Hz clock running for a picture that does not move.
    /// </summary>
    private void Sleep(double now)
    {
        _sleepTimer ??= new DispatcherTimer(DispatcherPriority.Render);
        _sleepTimer.Stop();
        _sleepTimer.Interval = TimeSpan.FromSeconds(ClaudeMascotArt.SecondsUntilChange(_mood, now - _start, _seed));
        _sleepTimer.Tick -= OnSleepTick;
        _sleepTimer.Tick += OnSleepTick;
        _sleepTimer.Start();
    }

    private void OnSleepTick(object? sender, EventArgs e)
    {
        var now = AgentClock.Instance.Time;
        SetFrame(ClaudeMascotArt.FrameAt(_mood, now - _start, _seed, false));
        Sleep(now);
    }

    private void Unsubscribe()
    {
        _leaving = false;
        _subscription?.Dispose();
        _subscription = null;
        if (_sleepTimer is not null)
        {
            _sleepTimer.Stop();
            _sleepTimer.Tick -= OnSleepTick;
            _sleepTimer = null;
        }

        SetFrame(ClaudeMascotArt.FrameAt(_mood, 0, _seed, true));
    }

    private void Step(double time)
    {
        if (_leaving)
        {
            if (ClaudeMascotArt.OutroFrameAt(time - _leaveStart) is { } closing)
            {
                SetFrame(closing);
                return;
            }

            _leaving = false;
            _start = time;
        }

        if (_mood == MascotMood.Working)
        {
            s_workingSeen = time;
        }

        SetFrame(ClaudeMascotArt.FrameAt(_mood, time - _start, _seed, false));
        if (_mood != MascotMood.Working && !_leaving)
        {
            // The laptop is away: from here on the picture changes rarely, so leave the shared clock.
            _subscription?.Dispose();
            _subscription = null;
            Sleep(time);
        }
    }

    /// <summary>Redraws only when the picture changed; the art hands out the same array for the same pose, so a reference check is enough.</summary>
    private void SetFrame(string[] frame)
    {
        if (ReferenceEquals(frame, _frame))
        {
            return;
        }

        _frame = frame;
        InvalidateVisual();
    }

    /// <summary>Device pixels per layout unit of this element, including the zoom of every ancestor.</summary>
    private double Scale()
    {
        try
        {
            if (PresentationSource.FromVisual(this) is { RootVisual: Visual root, CompositionTarget: { } target })
            {
                var ancestor = TransformToAncestor(root) is Transform t ? t.Value.M11 : 1.0;
                var scale = target.TransformToDevice.M11 * ancestor;
                if (scale > 0)
                {
                    return scale;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Not connected to the root yet: the DPI of the window it will live in is the best guess.
        }

        return Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var s = Scale();
        _cell = ClaudeMascotArt.CellPixels(_target, s);
        // The same square footprint as the classic glyph, so switching the symbol never moves the title; the art is centred in it.
        return new Size(_box, _box);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Scale();
        if (ClaudeMascotArt.CellPixels(_target, s) != _cell)
        {
            // The scale changed since the last measure; measuring again redraws, and the next render finds the cell settled.
            InvalidateMeasure();
            return;
        }

        var unit = _cell / s;
        var offsetX = ClaudeMascotArt.CenterOffsetPixels((int)Math.Round(RenderSize.Width * s), ClaudeMascotArt.Columns * _cell) / s;
        // Vertically it is the standing body that is centred, not the whole box: the rows above it are air for the lifted laptop, and
        // centring those too would sit the mascot low next to the text.
        var offsetY = (ClaudeMascotArt.CenterOffsetPixels((int)Math.Round(RenderSize.Height * s), ClaudeMascotArt.BodyRows * _cell)
            - ClaudeMascotArt.BodyTop * _cell) / s;
        var pushed = false;
        try
        {
            if (PresentationSource.FromVisual(this) is { RootVisual: Visual root, CompositionTarget: { } target })
            {
                // Shift the origin onto a whole device pixel; with aliased edges that is what keeps every cell exact.
                var device = target.TransformToDevice.Transform(TransformToAncestor(root).Transform(new Point(0, 0)));
                var fracX = device.X - Math.Floor(device.X);
                var fracY = device.Y - Math.Floor(device.Y);
                if (fracX > 0.001 || fracY > 0.001)
                {
                    dc.PushTransform(new TranslateTransform(-fracX / s, -fracY / s));
                    pushed = true;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Detached while rendering: draw unsnapped, the next render after attaching corrects it.
        }

        foreach (var (row, col, len, ink) in ClaudeMascotArt.Runs(_frame))
        {
            var brush = ink switch
            {
                'e' => s_eye,
                'D' => s_side,
                'G' => s_laptop,
                'W' => s_laptopEdge,
                _ => s_body,
            };
            dc.DrawRectangle(brush, null, new Rect(offsetX + col * unit, offsetY + row * unit, len * unit, unit));
        }

        if (pushed)
        {
            dc.Pop();
        }
    }
}

/// <summary>Chooses between the mascot and the classic spark for an agent's mark, so every place follows the one setting.</summary>
internal static class AgentSymbol
{
    public static FrameworkElement Create(AgentProvider provider, double size, MascotMood mood, bool useMascot) =>
        provider == AgentProvider.Claude && useMascot
            ? new ClaudeMascotView(size, mood)
            : new AgentGlyph(provider, size, mood != MascotMood.Idle, spin: mood != MascotMood.Waiting);
}

/// <summary>English strings of the mascot and the "stay between prompts" setting.</summary>
internal static class MascotStrings
{
    public static string Symbol => Loc.T("Claude symbol");
    public static string Mascot => Loc.T("Pixel mascot");
    public static string Spark => Loc.T("Classic spark");
    public static string SymbolCaption => Loc.T("The mascot opens a laptop and types while Claude works, waves when Claude needs you, and winks in between.");
    public static string Linger => Loc.T("Keep agents in the island between prompts");
    public static string LingerCaption => Loc.T("For 3 minutes after an agent answers, so the closed island does not come and go with every turn.");
}
