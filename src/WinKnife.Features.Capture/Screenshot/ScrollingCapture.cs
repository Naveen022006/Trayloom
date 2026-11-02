// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Capture;

internal enum ScrollStatus { Completed, Partial, Limited, Failed, Cancelled }

internal sealed record ScrollResult(ScrollStatus Status, BitmapSource? Image);

/// <summary>
/// Scrolling screenshot: the user scrolls the target, the region is polled every 90 ms and only the new pixels of each advance are kept
/// (no scroll events are injected). The HUD offers Cancel (Esc) and Done (Enter).
/// </summary>
internal sealed class ScrollingCaptureSession
{
    private const int SampleMs = 90;
    private const double SettleSeconds = 0.22;
    private const double MaxSeconds = 120;
    private const int MaxFrames = 512;
    private const long MaxPixels = 60_000_000;

    private readonly RegionPick _pick;
    private readonly List<byte[]> _strips = new();
    private readonly List<int> _stripHeights = new();
    private readonly int _w;
    private readonly int _h;
    private CancellationTokenSource _cts = new();
    private Task? _task;
    private volatile bool _finishRequested;
    private volatile int _totalHeight;
    private ScrollHud? _hud;
    private IDisposable? _keys;
    private bool _completed;
    private bool _partial;

    public ScrollingCaptureSession(RegionPick pick)
    {
        _pick = pick;
        _w = pick.Screen.Width;
        _h = pick.Screen.Height;
    }

    public event Action<ScrollResult>? Completed;

    public void Start()
    {
        _hud = new ScrollHud(this);
        _hud.Present(_pick);
        _keys = CaptureHost.Services.Hooks.SubscribeKeyboard(e =>
        {
            if (!e.IsKeyDown || e.IsFromWinKnife)
            {
                return false;
            }

            if (e.Key is Key.Return or Key.Enter)
            {
                CaptureHost.Ui.BeginInvoke(Finish);
                return true;
            }

            if (e.Key == Key.Escape)
            {
                CaptureHost.Ui.BeginInvoke(Cancel);
                return true;
            }

            return false;
        }, 100);
        _task = Task.Run(() => Loop(_cts.Token));
    }

    public void Finish() => _finishRequested = true;

    public void Cancel()
    {
        if (_completed)
        {
            return;
        }

        _cts.Cancel();
        Complete(new ScrollResult(ScrollStatus.Cancelled, null));
    }

    public int TotalHeight => _totalHeight;

    private void Complete(ScrollResult result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _keys?.Dispose();
        _hud?.Close();
        Completed?.Invoke(result);
    }

    private void Loop(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        byte[]? accepted = null;
        byte[]? pending = null;
        DateTime pendingSince = default;
        var frames = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var finishing = _finishRequested;
                var frame = Grab();
                frames++;
                if (accepted is null)
                {
                    accepted = frame;
                    _strips.Add(frame);
                    _stripHeights.Add(_h);
                    _totalHeight = _h;
                }
                else
                {
                    var next = pending ?? accepted;
                    if (IsStable(frame, next))
                    {
                        if (pending is not null && (DateTime.UtcNow - pendingSince).TotalSeconds >= SettleSeconds || pending is not null && finishing)
                        {
                            Accept(ref accepted, pending);
                            pending = null;
                        }
                    }
                    else if (!IsStable(frame, accepted))
                    {
                        pending = frame;
                        pendingSince = DateTime.UtcNow;
                    }
                    else
                    {
                        pending = null;
                    }
                }

                if (finishing)
                {
                    if (pending is not null && accepted is not null)
                    {
                        Accept(ref accepted, pending);
                    }

                    Thread.Sleep(150);
                    var last = Grab();
                    if (accepted is not null && !IsStable(last, accepted))
                    {
                        Accept(ref accepted, last);
                    }

                    Finalize(_partial ? ScrollStatus.Partial : ScrollStatus.Completed);
                    return;
                }

                if ((DateTime.UtcNow - started).TotalSeconds > MaxSeconds || frames > MaxFrames || (long)_w * _totalHeight > MaxPixels)
                {
                    Finalize(ScrollStatus.Limited);
                    return;
                }

                Thread.Sleep(SampleMs);
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("scrolling capture failed", ex);
            Finalize(_strips.Count > 1 ? ScrollStatus.Partial : ScrollStatus.Failed);
        }
    }

    private byte[] Grab()
    {
        var bmp = ScreenGrab.CaptureRect(_pick.Screen);
        return Pixels.Get(bmp);
    }

    private void Accept(ref byte[]? accepted, byte[] frame)
    {
        var offset = FindOffset(accepted!, frame);
        if (offset <= 0)
        {
            _partial = true;
            return;
        }

        var rows = Math.Min(offset, _h);
        var strip = new byte[rows * _w * 4];
        Buffer.BlockCopy(frame, (_h - rows) * _w * 4, strip, 0, strip.Length);
        _strips.Add(strip);
        _stripHeights.Add(rows);
        _totalHeight += rows;
        accepted = frame;
    }

    private bool IsStable(byte[] a, byte[] b)
    {
        // mean absolute difference over sampled pixels, ignoring the top and bottom 1/24 of the rows
        var y0 = _h / 24;
        var y1 = _h - _h / 24;
        long sum = 0;
        long n = 0;
        var step = Math.Max(1, _w / 64);
        for (var y = y0; y < y1; y += 2)
        {
            var row = y * _w * 4;
            for (var x = 0; x < _w; x += step)
            {
                var o = row + x * 4;
                sum += Math.Abs(a[o] - b[o]) + Math.Abs(a[o + 1] - b[o + 1]) + Math.Abs(a[o + 2] - b[o + 2]);
                n += 3;
            }
        }

        return n == 0 || (double)sum / n <= 1.5;
    }

    /// <summary>Finds how many rows the content moved up between two frames (new rows at the bottom); 0 when unmatched.</summary>
    private int FindOffset(byte[] prev, byte[] cur)
    {
        var cols = Math.Min(48, _w);
        var xs = new int[cols];
        for (var i = 0; i < cols; i++)
        {
            xs[i] = (int)((i + 0.5) * _w / cols);
        }

        var gp = Gray(prev, xs);
        var gc = Gray(cur, xs);
        var minOff = Math.Max(2, _h / 100);
        var maxOff = Math.Min(_h - 8, (int)(_h * 0.88));
        var needRun = Math.Max(8, Math.Min(28, _h / 12));
        var best = (Offset: 0, Match: -1, Diff: double.MaxValue);
        var second = (Offset: 0, Match: -1, Diff: double.MaxValue);
        for (var d = minOff; d <= maxOff; d++)
        {
            var overlap = _h - d;
            var matching = 0;
            var run = 0;
            var longest = 0;
            long diffSum = 0;
            for (var y = 0; y < overlap; y++)
            {
                // cur[y] should equal prev[y + d]
                var rp = (y + d) * cols;
                var rc = y * cols;
                var rowDiff = 0;
                for (var c = 0; c < cols; c++)
                {
                    rowDiff += Math.Abs(gp[rp + c] - gc[rc + c]);
                }

                diffSum += rowDiff;
                if (rowDiff <= cols * 8)
                {
                    matching++;
                    run++;
                    longest = Math.Max(longest, run);
                }
                else
                {
                    run = 0;
                }
            }

            if (longest < needRun || matching < Math.Max(longest, overlap / 3))
            {
                continue;
            }

            var mean = (double)diffSum / Math.Max(1, overlap * cols);
            if (matching > best.Match || matching == best.Match && mean < best.Diff)
            {
                second = best;
                best = (d, matching, mean);
            }
            else if (matching > second.Match)
            {
                second = (d, matching, mean);
            }
        }

        if (best.Match < 0)
        {
            return 0;
        }

        if (second.Match >= 0 && Math.Abs(second.Offset - best.Offset) > 2 && second.Match >= best.Match * 0.98 && second.Diff <= best.Diff + Math.Min(0.75, best.Diff))
        {
            return 0; // ambiguous (repeating content)
        }

        return best.Offset;
    }

    private byte[] Gray(byte[] px, int[] xs)
    {
        var cols = xs.Length;
        var g = new byte[_h * cols];
        for (var y = 0; y < _h; y++)
        {
            var row = y * _w * 4;
            for (var c = 0; c < cols; c++)
            {
                var o = row + xs[c] * 4;
                g[y * cols + c] = (byte)((px[o] * 29 + px[o + 1] * 150 + px[o + 2] * 77) >> 8);
            }
        }

        return g;
    }

    private void Finalize(ScrollStatus status)
    {
        BitmapSource? image = null;
        if (_strips.Count > 0)
        {
            var total = _totalHeight;
            var all = new byte[_w * total * 4];
            var at = 0;
            foreach (var s in _strips)
            {
                Buffer.BlockCopy(s, 0, all, at, s.Length);
                at += s.Length;
            }

            image = Pixels.Make(all, _w, total);
        }

        if (status is ScrollStatus.Completed or ScrollStatus.Partial && _strips.Count == 1 && !_partial)
        {
            status = ScrollStatus.Completed;
        }

        CaptureHost.Ui.BeginInvoke(() => Complete(new ScrollResult(image is null ? ScrollStatus.Failed : status, image)));
    }

    /// <summary>The floating HUD: message, live height, Cancel and Done.</summary>
    private sealed class ScrollHud : OverlayWindow
    {
        private readonly ScrollingCaptureSession _owner;
        private readonly TextBlock _height;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

        public ScrollHud(ScrollingCaptureSession owner)
        {
            _owner = owner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ClickThrough = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 9, 14, 9) };
            row.Children.Add(UiKit.Icon(G.Stack, 16, ThemeKeys.Accent, FontWeights.SemiBold));
            var texts = new StackPanel { Margin = new Thickness(10, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            var msg = UiKit.Text(Strings.Screenshot.ScrollingProgress, 12, FontWeights.SemiBold, wrap: true);
            msg.MaxWidth = 360;
            texts.Children.Add(msg);
            _height = UiKit.Text(Loc.T("0 px"), 10, FontWeights.Medium, ThemeKeys.TextSecondary);
            texts.Children.Add(_height);
            row.Children.Add(texts);
            row.Children.Add(UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 11, null, owner.Cancel));
            var done = UiKit.Button(Strings.Done, ButtonKind.Prominent, 11, null, owner.Finish);
            done.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(done);
            var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = row, Margin = new Thickness(10) };
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.3, Direction = 270 };
            Content = card;
            _timer.Tick += (_, _) => _height.Text = Loc.F("{0} px", _owner.TotalHeight);
        }

        public void Present(RegionPick pick)
        {
            SourceInitialized += (_, _) => N.ExcludeFromCapture(Handle);
            ShowNoActivate();
            UpdateLayout();
            var mon = pick.Monitor;
            var scale = mon.Scale;
            var w = (int)(ActualWidth * scale);
            var h = (int)(ActualHeight * scale);
            var x = mon.Work.X + (mon.Work.Width - w) / 2;
            var y = mon.Work.Y + (int)(24 * scale);
            N.SetWindowPos(Handle, -1, x, y, w, h, 0x0010);
            _timer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
