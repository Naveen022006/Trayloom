// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trayloom.Features.Island;

internal enum EyeKind
{
    Open,
    Closed,
    Squint,
    Wide,
    Happy,
    Heart,
    Sleepy,
    Determined,
}

internal sealed record MoodDef(EyeKind Left, EyeKind Right, double GazeX, double GazeY, double Tilt);

/// <summary>Pose + animation state of one companion figure (the island has one, the settings stage another).</summary>
internal sealed class MascotActor
{
    public const int Points = 24;

    public static readonly Dictionary<string, MoodDef> Moods = new()
    {
        ["idle"] = new(EyeKind.Open, EyeKind.Open, 0, 0, 0),
        ["happy"] = new(EyeKind.Happy, EyeKind.Happy, 0, -0.02, 0),
        ["wink"] = new(EyeKind.Open, EyeKind.Happy, 0, 0, -0.08),
        ["love"] = new(EyeKind.Heart, EyeKind.Heart, 0, 0, 0),
        ["sleepy"] = new(EyeKind.Sleepy, EyeKind.Sleepy, 0, 0.03, 0.05),
        ["determined"] = new(EyeKind.Determined, EyeKind.Determined, 0, 0, 0),
        ["surprised"] = new(EyeKind.Wide, EyeKind.Wide, 0, -0.02, 0),
        ["searching"] = new(EyeKind.Open, EyeKind.Open, 0.05, 0, 0),
        ["thinking"] = new(EyeKind.Squint, EyeKind.Open, 0.06, -0.06, -0.10),
        ["confused"] = new(EyeKind.Open, EyeKind.Squint, -0.03, 0.01, 0.14),
        ["alert"] = new(EyeKind.Wide, EyeKind.Wide, 0, 0, 0),
    };

    private sealed record Reaction(double Length, string Mood, double MoodFor, double Hop, double HopTime, string Extra);

    private static readonly Dictionary<string, Reaction> Reactions = new()
    {
        ["celebrate"] = new(1.0, "happy", 1.0, 0.28, 0.62, "hop"),
        ["love"] = new(1.4, "love", 1.3, 0, 0, "heartbeat"),
        ["surprised"] = new(0.9, "surprised", 0.85, 0.2, 0.5, "hop"),
        ["flash"] = new(0.5, "surprised", 0.25, 0, 0, "flash"),
        ["confused"] = new(1.2, "confused", 1.15, 0, 0, "wobble"),
        ["wakeUp"] = new(1.6, "happy", 0.9, 0.12, 0.5, "wake"),
        ["yawn"] = new(1.2, "sleepy", 1.15, 0, 0, "stretch"),
        ["perk"] = new(1.0, "alert", 1.0, 0.2, 0.55, "hop"),
        ["bounce"] = new(1.0, "alert", 1.0, 0.25, 0.4, "hop2"),
        ["hush"] = new(1.0, "sleepy", 0.95, 0, 0, "duck"),
        ["ready"] = new(1.0, "determined", 1.0, 0, 0, "nod"),
        ["groove"] = new(1.6, "happy", 1.6, 0.14, 0.4, "groove"),
    };

    private string _mood = "idle";
    private string _prevMood = "idle";
    private double _moodBlend = 1;
    private DateTime _moodAt = DateTime.MinValue;
    private string? _reaction;
    private DateTime _reactionAt;
    private DateTime _nextBlink = DateTime.Now.AddSeconds(3);
    private DateTime _blinkAt = DateTime.MinValue;
    private bool _doubleBlink;
    private int _blinks;
    private bool _dozing;
    private DateTime _lastActivity = DateTime.Now;
    private Point _gazeTarget;
    private double _gx, _gy;

    public EyeKind LeftEye { get; private set; } = EyeKind.Open;

    public EyeKind RightEye { get; private set; } = EyeKind.Open;

    public double[] LeftPoints { get; private set; } = new double[Points * 2];

    public double[] RightPoints { get; private set; } = new double[Points * 2];

    public double Tilt { get; private set; }

    public double GazeX { get; private set; }

    public double GazeY { get; private set; }

    public double BlinkScale { get; private set; } = 1;

    public double Hop { get; private set; }

    public double Squash { get; private set; }

    public double EyeScale { get; private set; } = 1;

    public double Wobble { get; private set; }

    public double Glow { get; private set; }

    public bool BulbLit { get; private set; } = true;

    public bool Sleeping => _dozing;

    public bool Asleep { get; set; }

    public bool Animating => _reaction is not null || _moodBlend < 1 || BlinkScale < 1 || _dozing || Math.Abs(GazeX - _gx) > 0.001 || Math.Abs(GazeY - _gy) > 0.001;

    public string Mood => _mood;

    public bool Playing => _reaction is not null;

    public DateTime LastReaction { get; private set; } = DateTime.MinValue;

    public event Action? Frame;

    public void SetMood(string mood)
    {
        if (_mood == mood)
        {
            return;
        }

        _prevMood = _mood;
        _mood = mood;
        _moodBlend = 0;
        _moodAt = DateTime.Now;
        if (mood == "surprised")
        {
            StartExtra("pop");
        }
    }

    private string? _extra;
    private DateTime _extraAt;

    private void StartExtra(string name)
    {
        _extra = name;
        _extraAt = DateTime.Now;
    }

    public bool Play(string reaction, bool reduceMotion)
    {
        if (!Reactions.TryGetValue(reaction, out var r))
        {
            return false;
        }

        _reaction = reaction;
        _reactionAt = DateTime.Now;
        LastReaction = DateTime.Now;
        _dozing = false;
        _blinks = 0;
        SetMood(r.Mood);
        _lastActivity = DateTime.Now;
        if (!reduceMotion)
        {
            StartExtra(r.Extra);
        }

        return true;
    }

    public static double Length(string reaction) => Reactions.TryGetValue(reaction, out var r) ? r.Length : 1;

    public void Wake()
    {
        if (_dozing)
        {
            _dozing = false;
            _blinks = 0;
            SetMood("idle");
            StartExtra("pop");
        }

        _lastActivity = DateTime.Now;
    }

    public void LookAt(double dx, double dy)
    {
        _gazeTarget = new Point(dx, dy);
        _gx = Math.Clamp(dx, -1, 1) * 0.08;
        _gy = Math.Clamp(dy, -1, 1) * 0.05;
    }

    public void ClearLook()
    {
        _gx = _gy = 0;
    }

    /// <summary>Advances the animation to now (called by the decorative clock).</summary>
    public void Step(bool reduceMotion, bool batterySaver = false)
    {
        var now = DateTime.Now;
        // mood morph
        if (_moodBlend < 1)
        {
            _moodBlend = Math.Min(1, (now - _moodAt).TotalSeconds / 0.22);
        }

        var cur = Moods[_mood];
        var prev = Moods[_prevMood];
        var t = _moodBlend * _moodBlend * (3 - (2 * _moodBlend));
        LeftEye = cur.Left;
        RightEye = cur.Right;
        LeftPoints = Lerp(EyeOutline(prev.Left, false), EyeOutline(cur.Left, false), t);
        RightPoints = Lerp(EyeOutline(prev.Right, true), EyeOutline(cur.Right, true), t);
        var tilt = prev.Tilt + ((cur.Tilt - prev.Tilt) * t);
        var targetGx = cur.GazeX + _gx;
        var targetGy = cur.GazeY + _gy;
        GazeX += (targetGx - GazeX) * 0.25;
        GazeY += (targetGy - GazeY) * 0.25;
        Tilt = tilt;

        // reaction timeline
        Hop = 0;
        Squash = 0;
        EyeScale = 1;
        Wobble = 0;
        Glow = 0;
        if (_reaction is not null && Reactions.TryGetValue(_reaction, out var r))
        {
            var el = (now - _reactionAt).TotalSeconds;
            if (el >= r.Length)
            {
                _reaction = null;
                SetMood("idle");
                _extra = null;
            }
            else
            {
                Glow = el < 0.12 ? el / 0.12 : (el < 0.7 ? 1 : Math.Max(0, 1 - ((el - 0.7) / 0.3)));
            }
        }

        if (_extra is not null && !reduceMotion)
        {
            var el = (now - _extraAt).TotalSeconds;
            var done = true;
            switch (_extra)
            {
                case "hop":
                {
                    var r2 = _reaction is not null && Reactions.TryGetValue(_reaction, out var rr) ? rr : new Reaction(1, "idle", 1, 0.2, 0.5, "hop");
                    if (el < r2.HopTime)
                    {
                        Hop = r2.Hop * Math.Sin(Math.PI * el / r2.HopTime);
                        Squash = el < 0.1 ? -0.12 * (el / 0.1) : (el > r2.HopTime - 0.08 ? 0.12 : 0);
                        done = false;
                    }

                    break;
                }

                case "hop2":
                    if (el < 0.95)
                    {
                        var ph = el < 0.42 ? el / 0.42 : (el - 0.46) / 0.45;
                        Hop = 0.25 * Math.Sin(Math.PI * Math.Clamp(ph, 0, 1));
                        done = false;
                    }

                    break;
                case "heartbeat":
                    if (el < 0.92)
                    {
                        var h = Math.Max(0, el - 0.22);
                        EyeScale = h < 0.7 ? 1 + (0.3 * Math.Sin(Math.PI * h / 0.35) * (h < 0.35 ? 1 : 0.75)) : 1;
                        done = false;
                    }

                    break;
                case "wobble":
                    if (el < 0.5)
                    {
                        Wobble = 0.12 * Math.Sin(el * 2 * Math.PI * 3) * (1 - (el / 0.5));
                        done = false;
                    }

                    break;
                case "pop":
                    if (el < 0.34)
                    {
                        Squash = Math.Sin(Math.PI * el / 0.34) * -0.14;
                        done = false;
                    }

                    break;
                case "nod":
                    if (el < 0.5)
                    {
                        Squash = 0.1 * Math.Sin(Math.PI * el / 0.5);
                        done = false;
                    }

                    break;
                case "duck":
                    if (el < 0.5)
                    {
                        Squash = 0.1 * Math.Sin(Math.PI * el / 0.5);
                        done = false;
                    }

                    break;
                case "stretch":
                case "wake":
                    if (el < 0.9)
                    {
                        Squash = -0.12 * Math.Sin(Math.PI * el / 0.9);
                        done = false;
                    }

                    break;
                case "groove":
                    if (el < 1.6)
                    {
                        Hop = 0.14 * Math.Abs(Math.Sin(el * Math.PI / 0.4));
                        Wobble = 0.11 * Math.Sin(el * Math.PI / 0.4);
                        done = false;
                    }

                    break;
                case "flash":
                    if (el < 0.45)
                    {
                        BlinkScale = 0.1;
                        done = false;
                    }

                    break;
            }

            if (done)
            {
                _extra = null;
            }
        }

        BulbLit = !_dozing;

        // blink
        if (_blinkAt != DateTime.MinValue)
        {
            var el = (now - _blinkAt).TotalSeconds;
            var total = _doubleBlink ? 0.44 : 0.17;
            if (el >= total)
            {
                _blinkAt = DateTime.MinValue;
                BlinkScale = 1;
                ScheduleBlink(batterySaver);
            }
            else if (_doubleBlink)
            {
                var ph = el / 0.44;
                BlinkScale = ph < 0.2 ? 1 - (0.9 * ph / 0.2) : (ph < 0.4 ? 0.1 + (0.9 * (ph - 0.2) / 0.2) : (ph < 0.58 ? 1 - (0.9 * (ph - 0.4) / 0.18) : 0.1 + (0.9 * Math.Min(1, (ph - 0.58) / 0.22))));
            }
            else
            {
                var ph = el / 0.17;
                BlinkScale = ph < 0.45 ? 1 - (0.9 * ph / 0.45) : 0.1 + (0.9 * (ph - 0.45) / 0.55);
            }
        }
        else if (_extra != "flash")
        {
            BlinkScale = 1;
            if (now >= _nextBlink && _reaction is null && !_dozing)
            {
                if (reduceMotion)
                {
                    _nextBlink = now.AddSeconds(5);
                }
                else
                {
                    _blinkAt = now;
                    _doubleBlink = Random.Shared.NextDouble() < 0.18;
                    _blinks++;
                    var limit = now.Hour is >= 22 or < 6 ? 12 : 36;
                    if (_blinks >= limit)
                    {
                        _dozing = true;
                        SetMood("sleepy");
                    }
                }
            }
        }

        Frame?.Invoke();
    }

    private void ScheduleBlink(bool batterySaver)
    {
        var min = batterySaver ? 5.2 : 2.6;
        var max = batterySaver ? 12.8 : 6.4;
        _nextBlink = DateTime.Now.AddSeconds(min + (Random.Shared.NextDouble() * (max - min)));
    }

    // ---------------------------------------------------------------- eye outlines (24 points: top curve left-to-right, bottom curve back)

    private static double[] Capsule(double w, double h, double cy)
    {
        var pts = new double[Points * 2];
        var r = Math.Min(w, h) / 2;
        for (var i = 0; i < 12; i++)
        {
            var u = i / 11.0;
            var x = -w / 2 + (w * u);
            var dx = Math.Max(0, Math.Abs(x) - ((w / 2) - r));
            var off = r - Math.Sqrt(Math.Max(0, (r * r) - (dx * dx)));
            pts[i * 2] = x;
            pts[(i * 2) + 1] = cy - (h / 2) + off;
            pts[(Points - 1 - i) * 2] = x;
            pts[((Points - 1 - i) * 2) + 1] = cy + (h / 2) - off;
        }

        return pts;
    }

    public static double[] EyeOutline(EyeKind kind, bool right)
    {
        const double w = 1, h = 2;
        double[] pts;
        switch (kind)
        {
            case EyeKind.Closed:
                pts = Capsule(1.5 * w, 0.45 * w, 0.12 * h);
                break;
            case EyeKind.Squint:
                pts = Capsule(w, 0.55 * h, 0.08 * h);
                break;
            case EyeKind.Wide:
                pts = Capsule(1.6 * w, 1.6 * w, -0.02 * h);
                break;
            case EyeKind.Happy:
            {
                pts = new double[Points * 2];
                var span = 2.1 * w;
                var rise = 0.42 * h;
                var thick = 0.55 * w;
                var top = -0.18 * h;
                for (var i = 0; i < 12; i++)
                {
                    var u = i / 11.0;
                    var x = -span / 2 + (span * u);
                    var n = x / (span / 2);
                    var y = top + (rise * n * n) - (rise * 0.4);
                    pts[i * 2] = x;
                    pts[(i * 2) + 1] = y;
                    pts[(Points - 1 - i) * 2] = x * 0.92;
                    pts[((Points - 1 - i) * 2) + 1] = y + thick;
                }

                break;
            }

            case EyeKind.Heart:
            {
                var raw = new double[Points * 2];
                for (var i = 0; i < Points; i++)
                {
                    var t = Math.PI - (2 * Math.PI * i / Points);
                    var x = 16 * Math.Pow(Math.Sin(t), 3);
                    var y = -((13 * Math.Cos(t)) - (5 * Math.Cos(2 * t)) - (2 * Math.Cos(3 * t)) - Math.Cos(4 * t));
                    raw[i * 2] = x / 32.0 * 2.0 * w;
                    raw[(i * 2) + 1] = (y / 30.0 * 1.8 * w) - (0.1 * h);
                }

                var start = 0;
                for (var i = 1; i < Points; i++)
                {
                    if (raw[i * 2] < raw[start * 2])
                    {
                        start = i;
                    }
                }

                pts = new double[Points * 2];
                for (var i = 0; i < Points; i++)
                {
                    var src = (start + i) % Points;
                    pts[i * 2] = raw[src * 2];
                    pts[(i * 2) + 1] = raw[(src * 2) + 1];
                }

                break;
            }

            case EyeKind.Sleepy:
            {
                pts = new double[Points * 2];
                var ww = 1.25 * w;
                var depth = 0.38 * h;
                for (var i = 0; i < 12; i++)
                {
                    var u = i / 11.0;
                    var x = -ww / 2 + (ww * u);
                    var n = x / (ww / 2);
                    pts[i * 2] = x;
                    pts[(i * 2) + 1] = -depth / 2;
                    pts[(Points - 1 - i) * 2] = x;
                    pts[((Points - 1 - i) * 2) + 1] = (-depth / 2) + (depth * Math.Sqrt(Math.Max(0, 1 - (n * n))));
                }

                break;
            }

            case EyeKind.Determined:
            {
                pts = Capsule(w, h, 0);
                for (var i = 0; i < Points; i++)
                {
                    var x = pts[i * 2];
                    var u = (x + (w / 2)) / w;
                    var lineY = (-0.30 * h) + ((0.32 * h) * (right ? 1 - u : u));
                    if (pts[(i * 2) + 1] < lineY)
                    {
                        pts[(i * 2) + 1] = lineY;
                    }
                }

                break;
            }

            default:
                pts = Capsule(w, h, 0);
                break;
        }

        if (right && kind is not (EyeKind.Determined or EyeKind.Happy or EyeKind.Heart or EyeKind.Sleepy))
        {
            return pts;
        }

        return pts;
    }

    private static double[] Lerp(double[] a, double[] b, double t)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            r[i] = a[i] + ((b[i] - a[i]) * t);
        }

        return r;
    }
}

/// <summary>Companion settings and look.</summary>
internal sealed record MascotLook(string Style, string Shape, string Palette)
{
    public static (Color Light, Color Shade) Palette_(string name) => name switch
    {
        "lemon" => (F(1.00, 0.96, 0.64), F(0.97, 0.83, 0.30)),
        "peach" => (F(1.00, 0.86, 0.74), F(0.98, 0.64, 0.50)),
        "rose" => (F(1.00, 0.80, 0.87), F(0.95, 0.54, 0.68)),
        "lilac" => (F(0.88, 0.83, 1.00), F(0.69, 0.59, 0.96)),
        "sky" => (F(0.77, 0.90, 1.00), F(0.43, 0.67, 0.97)),
        "mint" => (F(0.72, 0.97, 0.86), F(0.40, 0.83, 0.66)),
        _ => (F(0.99, 0.98, 0.96), F(0.84, 0.85, 0.90)),
    };

    private static Color F(double r, double g, double b) => Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
}

/// <summary>Draws the companion (<c>NotchMascotView</c>) from an actor's pose.</summary>
internal sealed class MascotView : FrameworkElement
{
    private readonly MascotActor _actor;
    private readonly double _size;
    private MascotLook _look;
    private bool _subscribed;

    public bool StepsActor { get; set; }

    public MascotView(MascotActor actor, double size, MascotLook look)
    {
        _actor = actor;
        _size = size;
        _look = look;
        Width = Height = size;
        IsHitTestVisible = false;
        Loaded += (_, _) => Sub();
        Unloaded += (_, _) => Unsub();
    }

    public MascotLook Look
    {
        get => _look;
        set
        {
            _look = value;
            InvalidateVisual();
        }
    }

    private void Sub()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            _actor.Frame += OnFrame;
            if (StepsActor)
            {
                DecorativeClock.Subscribe(OnClock);
            }
        }
    }

    private void Unsub()
    {
        if (_subscribed)
        {
            _subscribed = false;
            _actor.Frame -= OnFrame;
            if (StepsActor)
            {
                DecorativeClock.Unsubscribe(OnClock);
            }
        }
    }

    private void OnClock() => _actor.Step(D.ReduceMotion);

    private void OnFrame() => InvalidateVisual();

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(18, 18, 20));
    private static readonly Brush Visor = new SolidColorBrush(Color.FromRgb(23, 23, 28));

    protected override void OnRender(DrawingContext dc)
    {
        var s = _size;
        var (light, shade) = MascotLook.Palette_(_look.Palette);
        var grad = new LinearGradientBrush(light, shade, new Point(0.5, 0), new Point(0.5, 1));
        var robot = _look.Style == "robot";
        var shape = robot ? "robot" : _look.Shape;

        var cx = s / 2;
        dc.PushTransform(new TranslateTransform(0, -_actor.Hop * s));
        dc.PushTransform(new RotateTransform((_actor.Tilt + _actor.Wobble) * 180 / Math.PI, cx, s * 0.6));
        var sx = 1 + (0.8 * _actor.Squash);
        var sy = 1 - _actor.Squash;
        dc.PushTransform(new ScaleTransform(sx, sy, cx, s * 0.92));

        if (robot)
        {
            Fill(dc, grad, new Rect(s * 0.12, s * 0.26, s * 0.76, s * 0.68), s * 0.20);
            Fill(dc, new SolidColorBrush(shade), new Rect(s * 0.05, s * 0.50, s * 0.08, s * 0.20), s * 0.035);
            Fill(dc, new SolidColorBrush(shade), new Rect(s * 0.87, s * 0.50, s * 0.08, s * 0.20), s * 0.035);
            Fill(dc, new SolidColorBrush(shade), new Rect(s * 0.475, s * 0.12, s * 0.05, s * 0.16), s * 0.025);
            var bulb = new SolidColorBrush(_actor.BulbLit ? light : shade);
            dc.DrawEllipse(bulb, null, new Point(s * 0.5, s * 0.11), s * 0.07, s * 0.07);
            Fill(dc, Visor, new Rect(s * 0.20, s * 0.44, s * 0.60, s * 0.38), s * 0.15);
            DrawEyes(dc, s, new Point(0.36, 0.63), new Point(0.64, 0.63), 0.085, 0.16, new SolidColorBrush(light), shine: false);
        }
        else
        {
            var geo = BodyGeometry(shape, s);
            dc.DrawGeometry(grad, null, geo);
            var (l, r, w, h, shc, shr, ang) = Face(shape);
            DrawEyes(dc, s, l, r, w, h, Ink, shine: true);
            dc.PushTransform(new RotateTransform(ang * 180 / Math.PI, shc.X * s, shc.Y * s));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), null, new Point(shc.X * s, shc.Y * s), shr.X * s, shr.Y * s);
            dc.Pop();
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void Fill(DrawingContext dc, Brush b, Rect r, double radius) => dc.DrawRoundedRectangle(b, null, r, radius, radius);

    private void DrawEyes(DrawingContext dc, double s, Point l, Point r, double w, double h, Brush brush, bool shine)
    {
        _ = shine;
        var unitW = w * s;
        var unitH = h * s / 2;
        DrawEye(dc, _actor.LeftPoints, new Point(l.X * s, l.Y * s), unitW, unitH, brush);
        DrawEye(dc, _actor.RightPoints, new Point(r.X * s, r.Y * s), unitW, unitH, brush);
    }

    private void DrawEye(DrawingContext dc, double[] pts, Point center, double uw, double uh, Brush brush)
    {
        // points are in eye units (w = 1, h = 2): scale so that 1 unit of width = uw and 2 units of height = 2*uh
        var gx = _actor.GazeX * _size;
        var gy = _actor.GazeY * _size;
        var scale = _actor.EyeScale;
        var fig = new PathFigure { IsClosed = true };
        for (var i = 0; i < MascotActor.Points; i++)
        {
            var x = center.X + gx + (pts[i * 2] * uw * scale);
            var y = center.Y + gy + (pts[(i * 2) + 1] * uh * _actor.BlinkScale * scale);
            if (i == 0)
            {
                fig.StartPoint = new Point(x, y);
            }
            else
            {
                fig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
        }

        var geo = new PathGeometry([fig]);
        dc.DrawGeometry(brush, null, geo);
        if (_look.Style == "robot")
        {
            // soft glow
            dc.PushOpacity(0.35 * (0.4 + _actor.Glow));
            dc.DrawGeometry(brush, new Pen(brush, Math.Max(0.6, _size * 0.04)), geo);
            dc.Pop();
        }
    }

    private static (Point L, Point R, double W, double H, Point Shine, Point ShineR, double Angle) Face(string shape) => shape switch
    {
        "egg" => (new(0.375, 0.55), new(0.625, 0.55), 0.095, 0.19, new(0.33, 0.31), new(0.075, 0.05), -0.6),
        "squircle" => (new(0.35, 0.53), new(0.65, 0.53), 0.10, 0.20, new(0.24, 0.28), new(0.08, 0.05), -0.55),
        "pill" => (new(0.37, 0.57), new(0.63, 0.57), 0.09, 0.19, new(0.20, 0.42), new(0.07, 0.045), -0.45),
        _ => (new(0.36, 0.50), new(0.64, 0.50), 0.10, 0.20, new(0.31, 0.29), new(0.085, 0.055), -0.5),
    };

    private static Geometry BodyGeometry(string shape, double s)
    {
        switch (shape)
        {
            case "egg":
            {
                var fig = new PathFigure { StartPoint = new Point(0.5 * s, 0.10 * s), IsClosed = true };
                fig.Segments.Add(new BezierSegment(new Point(0.71 * s, 0.10 * s), new Point(0.86 * s, 0.36 * s), new Point(0.86 * s, 0.60 * s), true));
                fig.Segments.Add(new BezierSegment(new Point(0.86 * s, 0.80 * s), new Point(0.70 * s, 0.96 * s), new Point(0.5 * s, 0.96 * s), true));
                fig.Segments.Add(new BezierSegment(new Point(0.30 * s, 0.96 * s), new Point(0.14 * s, 0.80 * s), new Point(0.14 * s, 0.60 * s), true));
                fig.Segments.Add(new BezierSegment(new Point(0.14 * s, 0.36 * s), new Point(0.29 * s, 0.10 * s), new Point(0.5 * s, 0.10 * s), true));
                return new PathGeometry([fig]);
            }

            case "squircle":
            {
                var fig = new PathFigure { IsClosed = true };
                for (var i = 0; i < 72; i++)
                {
                    var t = 2 * Math.PI * i / 72;
                    var x = 0.5 + (0.43 * Math.Sign(Math.Cos(t)) * Math.Pow(Math.Abs(Math.Cos(t)), 2 / 3.4));
                    var y = 0.55 + (0.40 * Math.Sign(Math.Sin(t)) * Math.Pow(Math.Abs(Math.Sin(t)), 2 / 3.4));
                    var p = new Point(x * s, y * s);
                    if (i == 0)
                    {
                        fig.StartPoint = p;
                    }
                    else
                    {
                        fig.Segments.Add(new LineSegment(p, true));
                    }
                }

                return new PathGeometry([fig]);
            }

            case "pill":
                return new RectangleGeometry(new Rect(0.05 * s, 0.28 * s, 0.90 * s, 0.60 * s), 0.30 * s, 0.30 * s);
            default:
                return new EllipseGeometry(new Rect(0.08 * s, 0.12 * s, 0.84 * s, 0.84 * s));
        }
    }
}

/// <summary>Companion behaviour in the island: reaction gate, visits, rest / hide, pointer gaze (spec Part D 7).</summary>
internal sealed class MascotService : IDisposable
{
    private readonly IslandCore _core;
    private readonly DispatcherTimer _timer;
    private DateTime _nextVisit = DateTime.MaxValue;
    private DateTime _lastActivity = DateTime.Now;
    private DateTime _lastMusic = DateTime.MinValue;
    private DateTime _lastAgent = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _lastByReaction = [];
    private string? _waiting;
    private DateTime _waitingSince;
    private bool _wasEnabled;
    private bool _running;

    public MascotService(IslandCore core)
    {
        _core = core;
        Actor = new MascotActor();
        _timer = new DispatcherTimer(DispatcherPriority.Background, core.Ui) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnSecond();
    }

    public MascotActor Actor { get; }

    public bool Enabled => _core.IslandOn && _core.Ctx.Runtime.IsAvailable("notch-mascot") && _core.App.Get<bool>(K.MascotEnabled);

    public bool Reacts => Enabled && _core.App.Get<bool>(K.MascotReactions);

    public bool VisitsOn => Enabled && _core.App.Get<bool>(K.MascotVisits);

    /// <summary>The companion hid inside the island (after 30 s idle).</summary>
    public bool Tucked { get; private set; }

    /// <summary>Horizontal position in the resting strip: 0 = rest, -1..1 during a stroll (fraction of the wing travel).</summary>
    public double Stroll { get; private set; }

    public event Action? Changed;

    public MascotLook Look => new(_core.App.Get<string>(K.MascotStyle), _core.App.Get<string>(K.MascotShape), _core.App.Get<string>(K.MascotPalette));

    public void Start()
    {
        _core.Playback.Changed += OnMusic;
        _core.Timer.Started += p => React(p == TimerPhase.Focus || p == TimerPhase.Timer ? "ready" : "ready");
        _core.Timer.Finished += (p, cycle) => React(p == TimerPhase.Focus ? "celebrate" : (p is TimerPhase.ShortBreak or TimerPhase.LongBreak ? "ready" : "surprised"));
        _core.Downloads.Arrived += _ => React("celebrate");
        _core.Downloads.Failed += () => React("confused");
        _core.Session.Unlocked += () => React("wakeUp");
        Sync();
    }

    public void Sync()
    {
        var en = Enabled;
        if (en && !_wasEnabled)
        {
            SetRunning(true);
            Tucked = false;
            Actor.Wake();
            Actor.Play("wakeUp", D.ReduceMotion);
            ScheduleVisit(welcome: true);
        }
        else if (!en && _wasEnabled)
        {
            _nextVisit = DateTime.MaxValue;
            SetRunning(false);
        }

        _wasEnabled = en;
        Changed?.Invoke();
    }

    /// <summary>Runs the 1 s timer and the 30 Hz clock only while the companion is on; a disabled companion costs nothing.</summary>
    private void SetRunning(bool on)
    {
        if (on == _running)
        {
            return;
        }

        _running = on;
        if (on)
        {
            _timer.Start();
            DecorativeClock.Subscribe(Tick);
        }
        else
        {
            _timer.Stop();
            DecorativeClock.Unsubscribe(Tick);
        }
    }

    private void Tick()
    {
        // Stepping continues while enabled: the idle blink and breathing are time-driven inside Step, not gated by Animating.
        if (Enabled)
        {
            Actor.Step(D.ReduceMotion);
        }
    }

    private void OnMusic()
    {
        if (_core.Playback.Current is { IsPlaying: true } && (DateTime.Now - _lastMusic).TotalSeconds > 600)
        {
            _lastMusic = DateTime.Now;
            _core.Ui.BeginInvoke(async () =>
            {
                await Task.Delay(1200);
                React("groove");
            });
        }
    }

    public void React(string reaction)
    {
        if (!Reacts)
        {
            return;
        }

        var now = DateTime.Now;
        if ((now - Actor.LastReaction).TotalSeconds < 2.5 || (_lastByReaction.TryGetValue(reaction, out var last) && (now - last).TotalSeconds < 30))
        {
            if (_waiting is null)
            {
                _waiting = reaction;
                _waitingSince = now;
            }

            return;
        }

        _lastByReaction[reaction] = now;
        Tucked = false;
        _lastActivity = now;
        Actor.Wake();
        Actor.Play(reaction, D.ReduceMotion);
        Changed?.Invoke();
    }

    /// <summary>Plays a reaction from the settings preview without the gate.</summary>
    public void PlayNow(string reaction)
    {
        Tucked = false;
        Actor.Play(reaction, D.ReduceMotion);
        Changed?.Invoke();
    }

    public void Pet()
    {
        if (Enabled && _core.App.Get<bool>(K.MascotReactions))
        {
            Actor.Wake();
            Actor.Play("love", D.ReduceMotion);
        }
    }

    private void ScheduleVisit(bool welcome = false)
    {
        if (welcome)
        {
            _nextVisit = DateTime.Now.AddSeconds(1.5);
            return;
        }

        var (min, max) = _core.App.Get<string>(K.MascotFrequency) switch { "rare" => (600, 1200), "frequent" => (90, 180), _ => (240, 540) };
        _nextVisit = DateTime.Now.AddSeconds(min + (Random.Shared.NextDouble() * (max - min)));
    }

    private void OnSecond()
    {
        if (!Enabled)
        {
            return;
        }

        var now = DateTime.Now;
        if (_waiting is { } w)
        {
            if ((now - _waitingSince).TotalSeconds > 8)
            {
                _waiting = null;
            }
            else if ((now - Actor.LastReaction).TotalSeconds >= 2.5)
            {
                _waiting = null;
                React(w);
            }
        }

        var busy = _core.LiveActivities().Count > 0 || _core.Notices.Current is not null || (_core.Window?.Expanded ?? false);
        if (busy)
        {
            _lastActivity = now;
        }

        if (VisitsOn && now >= _nextVisit && !busy && !Tucked)
        {
            Actor.Wake();
            Actor.Play(Random.Shared.Next(3) switch { 0 => "celebrate", 1 => "love", _ => "perk" }, D.ReduceMotion);
            ScheduleVisit();
        }

        if (_core.App.Get<bool>(K.MascotHides) && !Tucked && !busy && (now - _lastActivity).TotalSeconds >= 30 && !Actor.Playing)
        {
            Actor.Play("yawn", D.ReduceMotion);
            Tucked = true;
            Changed?.Invoke();
        }
    }

    /// <summary>The pointer came back: wake with a stretch.</summary>
    public void PointerArrived()
    {
        _lastActivity = DateTime.Now;
        if (Tucked)
        {
            Tucked = false;
            Actor.Wake();
            Actor.Play("wakeUp", D.ReduceMotion);
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        SetRunning(false);
    }
}
