// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Music app blocker (spec 02a 3.5 / 05 D5): stops Windows' music apps from launching on their own right after a media key.
/// A media key arms the blocker for 2 s; a blocked app that starts inside that window, with no click or ordinary key explaining it,
/// is closed and an optional replacement app is opened instead.
/// </summary>
public sealed class MusicBlockFeature : IFeature
{
    private static readonly string[] BlockedProcesses = ["wmplayer", "Music.UI", "Microsoft.Media.Player", "iTunes", "AppleMusic"];
    private static readonly int[] MediaKeys = [0xB0, 0xB1, 0xB2, 0xB3];

    private IFeatureContext? _ctx;
    private IDisposable? _hook;
    private DispatcherTimer? _watch;
    private long _armedAt;
    private int _armedKey;
    private long _lastGesture;
    private HashSet<int> _baseline = [];

    public string Id => "music-block";

    /// <summary>True while the media-key hook is installed (the "unavailable right now" caption reads this).</summary>
    public static bool IsMonitoring { get; private set; }

    public void Start(IFeatureContext context)
    {
        _ctx = context;
        SystemDefaults.Register(context.App);
        context.App.Changed += OnSetting;
        Apply();
    }

    public void Stop()
    {
        if (_ctx is not null)
        {
            _ctx.App.Changed -= OnSetting;
        }

        Teardown();
        _ctx = null;
    }

    private void OnSetting(string key)
    {
        if (key == "musicBlockEnabled")
        {
            _ctx?.Ui.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        Teardown();
        var ctx = _ctx;
        if (ctx is null || !ctx.App.Get<bool>("musicBlockEnabled"))
        {
            return;
        }

        try
        {
            _hook = ctx.Hooks.SubscribeKeyboard(OnKey, 5);
            IsMonitoring = true;
            _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Watch(), ctx.Ui);
            _watch.Stop();
        }
        catch (Exception ex)
        {
            IsMonitoring = false;
            ctx.Log.Warn($"Music blocker could not listen for media keys: {ex.Message}");
        }
    }

    private void Teardown()
    {
        _hook?.Dispose();
        _hook = null;
        _watch?.Stop();
        _watch = null;
        IsMonitoring = false;
        Interlocked.Exchange(ref _armedAt, 0);
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        // Hook thread: record timestamps only.
        if (!e.IsKeyDown)
        {
            return false;
        }

        if (Array.IndexOf(MediaKeys, e.VkCode) >= 0)
        {
            if (!e.IsInjected)
            {
                Interlocked.Exchange(ref _armedKey, e.VkCode);
                Interlocked.Exchange(ref _armedAt, Environment.TickCount64);
                _ctx?.Ui.BeginInvoke(Arm);
            }

            return false;
        }

        // Modifier keys can carry a media function on some keyboards: they are not gestures.
        if (e.VkCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
        {
            return false;
        }

        Interlocked.Exchange(ref _lastGesture, Environment.TickCount64);
        return false;
    }

    private void Arm()
    {
        _baseline = [.. SnapshotBlocked()];
        _watch?.Start();
    }

    private static IEnumerable<int> SnapshotBlocked()
    {
        foreach (var name in BlockedProcesses)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var p in found)
            {
                yield return p.Id;
                p.Dispose();
            }
        }
    }

    private void Watch()
    {
        var ctx = _ctx;
        if (ctx is null)
        {
            _watch?.Stop();
            return;
        }

        var armedAt = Interlocked.Read(ref _armedAt);
        if (armedAt == 0 || Environment.TickCount64 - armedAt > 2000)
        {
            _watch?.Stop();
            return;
        }

        foreach (var name in BlockedProcesses)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var p in found)
            {
                using (p)
                {
                    if (_baseline.Contains(p.Id))
                    {
                        continue;
                    }

                    // "No gesture explains it": the last click / ordinary key is older than the media key.
                    var gesture = Interlocked.Read(ref _lastGesture);
                    if (gesture > armedAt || GestureSince(armedAt))
                    {
                        _baseline.Add(p.Id);
                        continue;
                    }

                    Block(p, Interlocked.CompareExchange(ref _armedKey, 0, 0));
                    Interlocked.Exchange(ref _armedAt, 0);
                    _watch?.Stop();
                    return;
                }
            }
        }
    }

    /// <summary>A pointer button pressed after the media key explains the launch (a click on a shortcut).</summary>
    private static bool GestureSince(long armedAt)
    {
        var left = GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0;
        return left && Environment.TickCount64 - armedAt >= 0;
    }

    private void Block(Process process, int key)
    {
        var ctx = _ctx!;
        try
        {
            ctx.Log.Info($"Music blocker closed {process.ProcessName} after a media key");
            if (!process.CloseMainWindow() || !process.WaitForExit(1000))
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            ctx.Log.Warn($"Music blocker could not close {process.ProcessName}: {ex.Message}");
        }

        var path = ctx.App.Get<string>("musicBlockReplacementPath");
        if (string.IsNullOrWhiteSpace(path) || IsBlockedApp(path))
        {
            return;
        }

        var play = ctx.App.Get<bool>("musicBlockPlayReplacement") && key == 0xB3;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ctx.Log.Warn($"Could not open the replacement app: {ex.Message}");
            return;
        }

        if (play)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui) { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                InputSender.SendKey(0xB3);
                InputSender.SendKey(0xB3, keyUp: true);
            };
            timer.Start();
        }
    }

    /// <summary>True when the file is one of the blocked music apps (they cannot be the replacement: it would loop).</summary>
    public static bool IsBlockedApp(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return BlockedProcesses.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase))
               || name.Contains("itunes", StringComparison.OrdinalIgnoreCase)
               || name.Contains("zunemusic", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}

/// <summary>General page: the Media keys card (anchor <c>musicBlocking</c>).</summary>
public sealed class MusicBlockSettingsSection : ISettingsSection
{
    public string? FeatureId => "music-block";

    public string PageId => SettingsPages.General;

    public string? Anchor => "musicBlocking";

    public string? Title => "Media keys";

    public int Order => 400;

    public IReadOnlyList<string> Keywords => ["music", "stop music from opening on its own", "open instead", "choose app", "play after opening replacement"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        SystemDefaults.Register(c.App);
        var app = c.App;
        var card = new SettingsCard(Strings.MusicBlock.Title);
        var detail = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
        string? rejection = null;

        void Render()
        {
            detail.Children.Clear();
            if (!app.Get<bool>("musicBlockEnabled"))
            {
                detail.Visibility = Visibility.Collapsed;
                return;
            }

            detail.Visibility = Visibility.Visible;
            if (!MusicBlockFeature.IsMonitoring)
            {
                detail.Children.Add(UiKit.Text(Strings.MusicBlock.Unavailable, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
            }

            var path = app.Get<string>("musicBlockReplacementPath");
            var row = new Grid { Margin = new Thickness(0, detail.Children.Count > 0 ? 8 : 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiKit.Text(Strings.MusicBlock.OpenInstead, 12, FontWeights.Normal, ThemeKeys.TextPrimary));
            var current = UiKit.Text(string.IsNullOrWhiteSpace(path) ? Strings.MusicBlock.None : System.IO.Path.GetFileNameWithoutExtension(path), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
            current.Margin = new Thickness(0, 0, 10, 0);
            current.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(current, 1);
            row.Children.Add(current);
            var choose = UiKit.Button(Strings.MusicBlock.ChooseApp, ButtonKind.Bordered, 12, null, () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files|*.*", Title = Strings.MusicBlock.ChooseApp };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                if (MusicBlockFeature.IsBlockedApp(dialog.FileName))
                {
                    rejection = Strings.MusicBlock.Rejected;
                }
                else
                {
                    rejection = null;
                    app.Set("musicBlockReplacementPath", dialog.FileName);
                }

                Render();
            });
            Grid.SetColumn(choose, 2);
            row.Children.Add(choose);
            if (!string.IsNullOrWhiteSpace(path))
            {
                var clear = Bits.FlatIcon(Gl.CloseCircle, null, () =>
                {
                    app.Set("musicBlockReplacementPath", string.Empty);
                    rejection = null;
                    Render();
                }, 13, 24);
                Grid.SetColumn(clear, 3);
                row.Children.Add(clear);
            }

            detail.Children.Add(row);
            if (!string.IsNullOrWhiteSpace(path))
            {
                var play = Bits.Check(app.Get<bool>("musicBlockPlayReplacement"));
                play.Margin = new Thickness(0, 8, 0, 0);
                var label = UiKit.Text(Strings.MusicBlock.PlayAfter, 12 / 0.8, FontWeights.Normal, ThemeKeys.TextPrimary);
                play.Content = label;
                play.Click += (_, _) => app.Set("musicBlockPlayReplacement", play.IsChecked == true);
                detail.Children.Add(play);
            }

            if (rejection is not null)
            {
                var note = UiKit.Text(rejection, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
                note.Margin = new Thickness(0, 8, 0, 0);
                detail.Children.Add(note);
            }

            var caption = UiKit.Text(Strings.MusicBlock.BlockedSet, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            caption.Margin = new Thickness(0, 8, 0, 0);
            detail.Children.Add(caption);
        }

        card.Add(new SettingsRow(Gl.PlayPause, Strings.MusicBlock.Row, Strings.MusicBlock.Caption, new MacSwitch(app.Get<bool>("musicBlockEnabled"), SwitchSize.Regular, on =>
        {
            app.Set("musicBlockEnabled", on);
            // The feature starts or stops its hook on the setting change; give it a moment before reading the monitoring state.
            card.Dispatcher.BeginInvoke(DispatcherPriority.Background, Render);
        })));
        card.Add(detail);
        Render();
        return card;
    }
}
