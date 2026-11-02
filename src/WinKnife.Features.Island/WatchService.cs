// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinKnife.Core;

namespace WinKnife.Features.Island;

internal enum WatchState
{
    Idle,
    Watching,
    Hidden,
    Finished,
}

internal enum WatchCondition
{
    Changes,
    Settles,
    Contains,
    Reaches,
}

/// <summary>Watch: follows a region of a window (or a display region), reads it with OCR and ends on a rule (spec Part D 8).</summary>
internal sealed class WatchService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out Rect32 rect, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect32
    {
        public int Left, Top, Right, Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    private readonly IslandCore _core;
    private DispatcherTimer? _loop;
    private nint _hwnd;
    private Rect32 _region;
    private bool _displayRegion;
    private int _generation;
    private bool _busy;
    private DateTime _since;
    private byte[]? _lastPrint;
    private DateTime _lastRead = DateTime.MinValue;
    private string _signature = "";
    private string? _baseline;
    private DateTime? _differingSince;
    private DateTime? _lastChange;
    private bool _everChanged;
    private double? _startNumber;
    private bool _countingUp;
    private OcrEngine? _ocr;

    public WatchService(IslandCore core) => _core = core;

    public WatchState State { get; private set; }

    public bool Active => State is WatchState.Watching or WatchState.Hidden;

    public string AppName { get; private set; } = "";

    public string WindowTitle { get; private set; } = "";

    public ImageSource? AppIcon { get; private set; }

    public BitmapSource? Picture { get; private set; }

    public string FullText { get; private set; } = "";

    public List<string> Lines { get; private set; } = [];

    public string Headline { get; private set; } = "";

    public bool Reading { get; private set; }

    public bool HasReading { get; private set; }

    public string? Outcome { get; private set; }

    public bool OutcomeClosed { get; private set; }

    public DateTime Since => _since;

    public bool CanWatchAgain => State == WatchState.Finished && !OutcomeClosed && (_displayRegion || (_hwnd != 0 && IsWindow(_hwnd)));

    public WatchCondition Condition
    {
        get => Enum.TryParse<WatchCondition>(_core.App.Get<string>(K.WatchCondition), true, out var c) ? c : WatchCondition.Changes;
        set
        {
            _core.App.Set(K.WatchCondition, value.ToString().ToLowerInvariant());
            ResetTracker();
        }
    }

    public string TypedText { get; set; } = "";

    public string TypedNumber { get; set; } = "";

    public int HeadlineLine { get; set; } = -1;

    public event Action? Changed;

    public bool Enabled => _core.IslandOn && _core.Ctx.Runtime.IsAvailable("notch-watch") && _core.App.Get<bool>(K.WatchEnabled);

    public void Start()
    {
        _core.Session.Changed += () =>
        {
            if (!_core.Session.CanPresent)
            {
                _loop?.Stop();
            }
            else if (Active)
            {
                _loop?.Start();
            }
        };
    }

    public void Sync()
    {
        if (!Enabled && State != WatchState.Idle)
        {
            Stop();
        }
    }

    // ---------------------------------------------------------------- choosing

    public void ChooseArea(Action? done = null)
    {
        if (!Enabled)
        {
            return;
        }

        var selector = new RegionSelectWindow(Loc.T("Choose what to watch"));
        selector.Selected += rect =>
        {
            Begin(rect);
            done?.Invoke();
        };
        selector.Show();
    }

    private void Begin(Rect32 screenRect)
    {
        var mid = new System.Drawing.Point(screenRect.Left + (screenRect.Width / 2), screenRect.Top + (screenRect.Height / 2));
        var hwnd = WindowAt(mid);
        Stop(clearOutcome: true);
        _generation++;
        if (hwnd != 0 && GetWindowRect(hwnd, out var wr))
        {
            _displayRegion = false;
            _hwnd = hwnd;
            var clip = new Rect32
            {
                Left = Math.Max(screenRect.Left, wr.Left),
                Top = Math.Max(screenRect.Top, wr.Top),
                Right = Math.Min(screenRect.Right, wr.Right),
                Bottom = Math.Min(screenRect.Bottom, wr.Bottom),
            };
            if (clip.Width >= 8 && clip.Height >= 8)
            {
                _region = new Rect32 { Left = clip.Left - wr.Left, Top = clip.Top - wr.Top, Right = clip.Right - wr.Left, Bottom = clip.Bottom - wr.Top };
                DescribeWindow(hwnd);
                StartLoop();
                return;
            }
        }

        _displayRegion = true;
        _hwnd = 0;
        _region = screenRect;
        AppName = Loc.T("Display");
        WindowTitle = "";
        AppIcon = null;
        StartLoop();
    }

    private static nint WindowAt(System.Drawing.Point p)
    {
        var self = (uint)Environment.ProcessId;
        for (var h = GetTopWindow(0); h != 0; h = GetWindow(h, 2))
        {
            if (!IsWindowVisible(h) || IsIconic(h))
            {
                continue;
            }

            GetWindowThreadProcessId(h, out var pid);
            if (pid == self)
            {
                continue;
            }

            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0)
            {
                continue;
            }

            if ((GetWindowLong(h, -20) & 0x80) != 0 && (GetWindowLong(h, -20) & 0x40000) == 0)
            {
                continue;
            }

            if (DwmGetWindowAttribute(h, 9, out Rect32 b, Marshal.SizeOf<Rect32>()) != 0 && !GetWindowRect(h, out b))
            {
                continue;
            }

            if (p.X >= b.Left && p.X < b.Right && p.Y >= b.Top && p.Y < b.Bottom)
            {
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                var cls = new StringBuilder(64);
                if (sb.Length == 0 && b.Width * b.Height < 40000)
                {
                    continue;
                }

                return h;
            }
        }

        return 0;
    }

    private void DescribeWindow(nint hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, 256);
        WindowTitle = sb.ToString();
        AppName = Loc.T("App");
        AppIcon = null;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            var path = p.MainModule?.FileName;
            AppName = p.ProcessName;
            if (path is not null)
            {
                AppName = FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d ? d : p.ProcessName;
                AppIcon = PlaybackService.IconOf(path);
            }
        }
        catch
        {
        }

        if (string.Equals(WindowTitle, AppName, StringComparison.OrdinalIgnoreCase))
        {
            WindowTitle = "";
        }
    }

    private void StartLoop()
    {
        State = WatchState.Watching;
        _since = DateTime.Now;
        Outcome = null;
        OutcomeClosed = false;
        HeadlineLine = -1;
        HasReading = false;
        Headline = "";
        FullText = "";
        Lines = [];
        Picture = null;
        _lastPrint = null;
        _lastRead = DateTime.MinValue;
        ResetTracker();
        _loop ??= new DispatcherTimer(DispatcherPriority.Background, _core.Ui);
        _loop.Tick -= OnLoop;
        _loop.Tick += OnLoop;
        _loop.Interval = TimeSpan.FromSeconds(1);
        _loop.Start();
        Changed?.Invoke();
        OnLoop(null, EventArgs.Empty);
    }

    public void WatchAgain()
    {
        if (CanWatchAgain)
        {
            StartLoop();
        }
    }

    public void Stop(bool clearOutcome = true)
    {
        _generation++;
        _loop?.Stop();
        if (clearOutcome)
        {
            State = WatchState.Idle;
            Outcome = null;
        }

        Picture = null;
        Changed?.Invoke();
    }

    public void SetVisiblePage(bool visible)
    {
        if (_loop is not null)
        {
            _loop.Interval = TimeSpan.FromSeconds(visible ? 1 : 2);
        }
    }

    // ---------------------------------------------------------------- loop

    private async void OnLoop(object? sender, EventArgs e)
    {
        if (_busy || State is not (WatchState.Watching or WatchState.Hidden) || !_core.Session.CanPresent)
        {
            return;
        }

        if (!_displayRegion && (_hwnd == 0 || !IsWindow(_hwnd)))
        {
            Finish(Loc.T("The window closed"), "closed", closed: true);
            return;
        }

        _busy = true;
        var gen = _generation;
        try
        {
            var hwnd = _hwnd;
            var region = _region;
            var display = _displayRegion;
            var result = await Task.Run(() => CaptureAndRead(hwnd, region, display, gen));
            if (gen != _generation)
            {
                return;
            }

            if (result is null)
            {
                if (State != WatchState.Hidden)
                {
                    State = WatchState.Hidden;
                    Changed?.Invoke();
                }

                return;
            }

            State = WatchState.Watching;
            Apply(result);
        }
        catch
        {
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed record ReadResult(BitmapSource Picture, string Text, List<string> Lines, string Signature, bool Reread);

    private ReadResult? CaptureAndRead(nint hwnd, Rect32 region, bool display, int gen)
    {
        using var bmp = Capture(hwnd, region, display);
        if (bmp is null || IsBlank(bmp))
        {
            return null;
        }

        var print = Fingerprint(bmp);
        var same = _lastPrint is not null && SamePrint(_lastPrint, print);
        var pic = ToBitmapSource(bmp);
        if (same && (DateTime.Now - _lastRead).TotalSeconds < 10 && HasReading)
        {
            return new ReadResult(pic, FullText, Lines, _signature, false);
        }

        _lastPrint = print;
        _lastRead = DateTime.Now;
        var (text, lines) = Recognise(bmp);
        var sig = text.Length == 0 ? "image:" + string.Join(",", print) : Normalise(text);
        return new ReadResult(pic, text, lines, sig, true);
    }

    private static System.Drawing.Bitmap? Capture(nint hwnd, Rect32 region, bool display)
    {
        try
        {
            if (display)
            {
                if (region.Width < 4 || region.Height < 4)
                {
                    return null;
                }

                var bmp = new System.Drawing.Bitmap(region.Width, region.Height);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.CopyFromScreen(region.Left, region.Top, 0, 0, new System.Drawing.Size(region.Width, region.Height));
                return bmp;
            }

            if (IsIconic(hwnd) || !GetWindowRect(hwnd, out var wr) || wr.Width <= 0 || wr.Height <= 0)
            {
                return null;
            }

            using var full = new System.Drawing.Bitmap(wr.Width, wr.Height);
            using (var g = System.Drawing.Graphics.FromImage(full))
            {
                var hdc = g.GetHdc();
                try
                {
                    PrintWindow(hwnd, hdc, 2);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            var rect = new System.Drawing.Rectangle(region.Left, region.Top, region.Width, region.Height);
            rect.Intersect(new System.Drawing.Rectangle(0, 0, full.Width, full.Height));
            if (rect.Width < 4 || rect.Height < 4)
            {
                return null;
            }

            return full.Clone(rect, full.PixelFormat);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBlank(System.Drawing.Bitmap bmp)
    {
        // an all-black capture means the window cannot be read
        var step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 8);
        for (var y = 0; y < bmp.Height; y += step)
        {
            for (var x = 0; x < bmp.Width; x += step)
            {
                var c = bmp.GetPixel(x, y);
                if (c.R > 3 || c.G > 3 || c.B > 3)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static byte[] Fingerprint(System.Drawing.Bitmap bmp)
    {
        using var small = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(bmp, 0, 0, 16, 16);
        }

        var cells = new byte[256];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var c = small.GetPixel(x, y);
                cells[(y * 16) + x] = (byte)(((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 16);
            }
        }

        return cells;
    }

    private static bool SamePrint(byte[] a, byte[] b)
    {
        var differ = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = Math.Abs(a[i] - b[i]);
            if (d > 1)
            {
                return false;
            }

            if (d > 0)
            {
                differ++;
            }
        }

        return differ <= 3;
    }

    private (string Text, List<string> Lines) Recognise(System.Drawing.Bitmap bmp)
    {
        try
        {
            _ocr ??= OcrEngine.TryCreateFromUserProfileLanguages();
            if (_ocr is null)
            {
                return ("", []);
            }

            System.Drawing.Bitmap source = bmp;
            var scale = 900.0 / Math.Max(bmp.Width, bmp.Height);
            System.Drawing.Bitmap? scaled = null;
            if (scale < 1)
            {
                scaled = new System.Drawing.Bitmap(bmp, (int)(bmp.Width * scale), (int)(bmp.Height * scale));
                source = scaled;
            }

            using var ms = new MemoryStream();
            source.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            scaled?.Dispose();
            ms.Position = 0;
            using var stream = ms.AsRandomAccessStream();
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            using var soft = decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
            var result = _ocr.RecognizeAsync(soft).AsTask().GetAwaiter().GetResult();
            var lines = result.Lines.Select(l => l.Text.Trim()).Where(l => l.Length > 0).ToList();
            return (string.Join("\n", lines), lines);
        }
        catch
        {
            return ("", []);
        }
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private void Apply(ReadResult r)
    {
        Picture = r.Picture;
        if (r.Reread)
        {
            FullText = r.Text;
            Lines = r.Lines;
            _signature = r.Signature;
            HasReading = true;
            Headline = ComputeHeadline();
        }

        Evaluate(r.Signature);
        if (State == WatchState.Watching)
        {
            Changed?.Invoke();
        }
    }

    // ---------------------------------------------------------------- tracker

    private void ResetTracker()
    {
        _baseline = null;
        _differingSince = null;
        _lastChange = null;
        _everChanged = false;
        _startNumber = null;
        if (HasReading && State == WatchState.Watching)
        {
            Evaluate(_signature);
        }
    }

    private void Evaluate(string signature)
    {
        if (State != WatchState.Watching)
        {
            return;
        }

        var now = DateTime.Now;
        switch (Condition)
        {
            case WatchCondition.Changes:
                if (_baseline is null)
                {
                    _baseline = signature;
                    break;
                }

                if (signature != _baseline)
                {
                    _differingSince ??= now;
                    if ((now - _differingSince.Value).TotalSeconds >= 3)
                    {
                        Finish(Loc.T("It changed"), "perk");
                    }
                }
                else
                {
                    _differingSince = null;
                }

                break;
            case WatchCondition.Settles:
                if (_baseline is null)
                {
                    _baseline = signature;
                    _lastChange = now;
                    break;
                }

                if (signature != _baseline)
                {
                    _baseline = signature;
                    _lastChange = now;
                    _everChanged = true;
                }
                else if (_everChanged && _lastChange is { } lc && (now - lc).TotalSeconds >= 30)
                {
                    Finish(Loc.T("It stopped changing"), "celebrate");
                }

                break;
            case WatchCondition.Contains:
                if (!string.IsNullOrWhiteSpace(TypedText) && Normalise(FullText).Contains(Normalise(TypedText), StringComparison.Ordinal))
                {
                    Finish(Loc.F("It shows {0}", TypedText.Trim()), "celebrate");
                }

                break;
            case WatchCondition.Reaches:
                if (TryParseNumber(TypedNumber, out var target) && FirstNumber(FullText) is { } value)
                {
                    _startNumber ??= value;
                    _countingUp = target >= _startNumber.Value;
                    if ((_countingUp && value >= target) || (!_countingUp && value <= target) || Math.Abs(value - target) < 1e-9)
                    {
                        Finish(Loc.F("It reached {0}", TypedNumber.Trim()), "celebrate");
                    }
                }

                break;
        }
    }

    private void Finish(string outcome, string reaction, bool closed = false)
    {
        _loop?.Stop();
        State = WatchState.Finished;
        Outcome = outcome;
        OutcomeClosed = closed;
        if (_core.App.Get<bool>(K.WatchSound))
        {
            try
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch
            {
            }
        }

        var onPage = _core.Window is { Expanded: true } w && w.CurrentModule == "watch";
        if (!onPage)
        {
            if (_core.Window is not null && _core.Session.CanPresent && !_core.Window.Suppressed)
            {
                _core.ShowNotice(new Notice { Event = NoticeEvents.Watch, Glyph = G.Eye, Title = outcome, Detail = AppName, Reaction = reaction });
            }
            else
            {
                _core.Ctx.Notifier.Show(outcome, string.IsNullOrEmpty(WindowTitle) ? AppName : WindowTitle);
            }
        }

        _core.Mascot.React(reaction == "perk" ? "perk" : reaction);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- helpers

    public static string Normalise(string text)
    {
        var d = text.Normalize(NormalizationForm.FormKD).ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    public static bool TryParseNumber(string text, out double value)
    {
        value = 0;
        var m = Regex.Match(text ?? "", @"([-+−])?\s*(\d[\d.,   ]*\d|\d)\s*%?");
        if (!m.Success)
        {
            return false;
        }

        var sign = m.Groups[1].Value is "-" or "−" ? -1 : 1;
        var num = m.Groups[2].Value.Replace(' ', ' ').Replace(' ', ' ').Replace(" ", "");
        var lastDot = num.LastIndexOf('.');
        var lastComma = num.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            var dec = Math.Max(lastDot, lastComma);
            num = num[..dec].Replace(".", "").Replace(",", "") + "." + num[(dec + 1)..];
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var parts = num.Split(sep);
            if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3 && parts[0].Length is >= 1 and <= 3 && parts[0] != "0"))
            {
                num = num.Replace(sep.ToString(), "");
            }
            else
            {
                num = num.Replace(sep, '.');
            }
        }

        if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            value = sign * v;
            return true;
        }

        return false;
    }

    private static double? FirstNumber(string text)
    {
        var pct = Regex.Match(text, @"\d+(?:[.,]\d+)?\s*%");
        if (pct.Success && TryParseNumber(pct.Value, out var p))
        {
            return p;
        }

        return TryParseNumber(text, out var v) ? v : null;
    }

    private string ComputeHeadline()
    {
        string Cut(string s) => s.Length > 22 ? s[..21] + "…" : s;
        if (HeadlineLine >= 0 && HeadlineLine < Math.Min(12, Lines.Count))
        {
            return Cut(Lines[HeadlineLine]);
        }

        return Auto(FullText, Lines);
    }

    public string AutoHeadline() => Auto(FullText, Lines);

    private static string Auto(string text, List<string> lines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var pct = Regex.Match(text, @"\d+(?:[.,]\d+)?\s*%");
        if (pct.Success)
        {
            return pct.Value.Replace(" ", "");
        }

        var first = lines.FirstOrDefault() ?? "";
        if (first.Length is > 0 and <= 22)
        {
            return first;
        }

        var clock = Regex.Match(text, @"\b\d{1,2}:\d{2}(?::\d{2})?\b");
        if (clock.Success)
        {
            return clock.Value;
        }

        var unit = Regex.Match(text, @"\b\d+(?:[.,]\d+)?\s?[A-Za-z]{1,3}\b");
        if (unit.Success)
        {
            return unit.Value;
        }

        return first.Length > 22 ? first[..21] + "…" : first;
    }

    public void SetHeadlineLine(int index)
    {
        HeadlineLine = index;
        Headline = ComputeHeadline();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _generation++;
        _loop?.Stop();
    }
}

/// <summary>Full-screen drag-a-rectangle overlay on the monitor under the pointer.</summary>
internal sealed class RegionSelectWindow : Window
{
    private readonly System.Windows.Shapes.Rectangle _box = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Visibility = Visibility.Collapsed };
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)) };
    private Point _start;
    private bool _dragging;
    private readonly string _prompt;

    public RegionSelectWindow(string prompt)
    {
        _prompt = prompt;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = System.Windows.Input.Cursors.Cross;
        Content = _canvas;
        _canvas.Children.Add(_box);
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Child = D.T(prompt, 13, FontWeights.SemiBold),
        };
        Canvas.SetLeft(hint, 24);
        Canvas.SetTop(hint, 24);
        _canvas.Children.Add(hint);
        var area = WinKnife.Core.Ui.Screens.UnderCursor();
        var m = WinKnife.Core.Ui.Screens.FromPoint(WinKnife.Core.Ui.Screens.CursorPosition());
        _ = area;
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WinKnife.Core.Native.NativeMethods.SetWindowPos(handle, WinKnife.Core.Native.NativeConstants.HWND_TOPMOST, (int)m.Bounds.X, (int)m.Bounds.Y, 100, 100, 0);
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = m.Bounds.X / dpi.DpiScaleX;
            Top = m.Bounds.Y / dpi.DpiScaleY;
            Width = m.Bounds.Width / dpi.DpiScaleX;
            Height = m.Bounds.Height / dpi.DpiScaleY;
            _origin = m.Bounds;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _dragging = true;
            _box.Visibility = Visibility.Visible;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging)
            {
                return;
            }

            var p = e.GetPosition(_canvas);
            Canvas.SetLeft(_box, Math.Min(_start.X, p.X));
            Canvas.SetTop(_box, Math.Min(_start.Y, p.Y));
            _box.Width = Math.Abs(p.X - _start.X);
            _box.Height = Math.Abs(p.Y - _start.Y);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            ReleaseMouseCapture();
            var p = e.GetPosition(_canvas);
            var dpi = VisualTreeHelper.GetDpi(this);
            var x1 = Math.Min(_start.X, p.X) * dpi.DpiScaleX;
            var y1 = Math.Min(_start.Y, p.Y) * dpi.DpiScaleY;
            var x2 = Math.Max(_start.X, p.X) * dpi.DpiScaleX;
            var y2 = Math.Max(_start.Y, p.Y) * dpi.DpiScaleY;
            Close();
            if (x2 - x1 >= 8 && y2 - y1 >= 8)
            {
                Selected?.Invoke(new WatchService.Rect32 { Left = (int)(_origin.X + x1), Top = (int)(_origin.Y + y1), Right = (int)(_origin.X + x2), Bottom = (int)(_origin.Y + y2) });
            }
        };
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private Rect _origin;

    public event Action<WatchService.Rect32>? Selected;
}
