// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>Header card of the Screen capture page: tool picker, the selected tool's shortcut rows, Recent captures shortcut.</summary>
public sealed class ScreenCaptureHeaderSection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["screenshot", "screen-recorder", "screen-ocr", "color-picker"];

    public string PageId => SettingsPages.ScreenCapture;

    public string? Title => Strings.ScreenCapture;

    public IReadOnlyList<string> Keywords => ["screenshot", "recording", "ocr", "color", "eyedropper", "recent captures", "shortcut", "capture menu"];

    public int Order => 0;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        ScreenCapturePage.FromAnchor(c.RequestedAnchor, c.Runtime);
        ScreenCapturePage.EnsureAvailable(c.Runtime);
        var card = new SettingsCard(Loc.T("Capture shortcuts"));
        var tools = CaptureTools.Installed(c).ToList();
        var holder = new StackPanel();
        if (tools.Count >= 2)
        {
            var seg = new SegmentedControl(tools.Select(CaptureTools.Title), Math.Max(0, tools.IndexOf(ScreenCapturePage.Selected)), true, 12);
            seg.SelectionChanged += i => ScreenCapturePage.Select(tools[i]);
            seg.Margin = new Thickness(0, 0, 0, 12);
            card.Add(seg);
        }

        card.Add(holder);

        void Build()
        {
            holder.Children.Clear();
            var tool = ScreenCapturePage.Selected;
            var inner = new SettingsCard();
            var role = CaptureTools.RoleId(tool);
            var enabledKey = CaptureTools.EnabledKey(tool);
            SettingsRow? menuRow = null;
            Sk.ShortcutToggle(inner, c, Loc.F("{0} shortcut", CaptureTools.Title(tool)), enabledKey, role, changed: on =>
            {
                if (menuRow is not null)
                {
                    menuRow.IsEnabled = on;
                }
            });
            inner.AddDivider();
            var menuKey = CaptureTools.ShowMenuKey(tool);
            var shortcutOn = c.App.Get<bool>(enabledKey);
            menuRow = Sk.Toggle(inner, c.App, Strings.ShowMenuOnShortcut, menuKey,
                Loc.T("Requires the shortcut above to be enabled."));
            menuRow.IsEnabled = shortcutOn;
            if (c.Runtime.AnyAvailable(["screenshot", "screen-recorder"]))
            {
                inner.AddDivider();
                Sk.ShortcutToggle(inner, c, Strings.Screenshot.RecentCaptures, K.RecentShortcutEnabled, "recentCaptures",
                    Loc.T("Open the recent captures list with a shortcut."));
            }

            // flatten: reuse the rows of the inner card inside the header card
            var stack = inner.Content;
            var rows = stack.Children.Cast<UIElement>().ToList();
            stack.Children.Clear();
            foreach (var r in rows)
            {
                holder.Children.Add(r);
            }
        }

        Build();
        ScreenCapturePage.Changed += RebuildOnChange;
        card.Unloaded += (_, _) => ScreenCapturePage.Changed -= RebuildOnChange;
        void RebuildOnChange() => card.Dispatcher.BeginInvoke(Build);
        return card;
    }
}

/// <summary>Base of the four per-tool section groups: shown only while its tool is selected in the picker.</summary>
public abstract class ToolSectionBase : ISettingsSection
{
    private protected abstract CaptureTool Tool { get; }

    public string? FeatureId => CaptureTools.FeatureId(Tool);

    public string PageId => SettingsPages.ScreenCapture;

    public string? Anchor => CaptureTools.Anchor(Tool);

    public abstract string? Title { get; }

    public virtual IReadOnlyList<string> Keywords => [];

    public int Order => 10 + (int)Tool;

    protected abstract void Fill(StackPanel stack, ISettingsContext c);

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        ScreenCapturePage.FromAnchor(c.RequestedAnchor, c.Runtime);
        var stack = new StackPanel();
        Fill(stack, c);
        void Apply() => stack.Visibility = ScreenCapturePage.Selected == Tool ? Visibility.Visible : Visibility.Collapsed;
        Apply();
        ScreenCapturePage.Changed += OnChanged;
        stack.Unloaded += (_, _) => ScreenCapturePage.Changed -= OnChanged;
        void OnChanged() => stack.Dispatcher.BeginInvoke(Apply);
        return stack;
    }

    protected static void AddCard(StackPanel stack, SettingsCard card)
    {
        card.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 12, 0, 0);
        stack.Children.Add(card);
    }
}

public sealed class ScreenshotSection : ToolSectionBase
{
    private protected override CaptureTool Tool => CaptureTool.Screenshot;

    public override string? Title => Strings.Tools.Screenshot;

    public override IReadOnlyList<string> Keywords =>
        ["capture now", "scrolling", "delay", "freeze", "pointer", "magnifier", "default action", "clipboard", "folder", "file name", "subfolder", "editor tools", "preview"];

    protected override void Fill(StackPanel stack, ISettingsContext c)
    {
        var app = c.App;

        // SC1
        var sc1 = new SettingsCard(Strings.Tools.Screenshot);
        var buttons = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var now = UiKit.Button(Strings.Screenshot.CaptureNow, ButtonKind.Bordered, 13, G.Camera, () => c.Ui.BeginInvoke(() => CaptureHost.Screenshot?.CaptureNow()));
        var scrolling = UiKit.Button(Strings.Screenshot.ScrollingCapture, ButtonKind.Bordered, 13, G.Stack, () => c.Ui.BeginInvoke(() => CaptureHost.Screenshot?.CaptureScrolling()));
        now.Padding = scrolling.Padding = new Thickness(10, 9, 10, 9);
        Grid.SetColumn(scrolling, 2);
        buttons.Children.Add(now);
        buttons.Children.Add(scrolling);
        sc1.Add(buttons);
        sc1.Add(Sk.Caption(Strings.Screenshot.PanelCaption));
        sc1.AddDivider();
        Sk.ShortcutToggle(sc1, c, Strings.Screenshot.CaptureWholeScreen, K.FullScreenShortcutEnabled, "screenshotFullScreen");
        sc1.AddDivider();
        Sk.ShortcutToggle(sc1, c, Strings.Screenshot.EditLatest, K.LastCaptureShortcutEnabled, "screenshotLastCapture");
        sc1.AddDivider();
        Sk.ShortcutToggle(sc1, c, Strings.Screenshot.EditClipboard, K.ClipboardShortcutEnabled, "screenshotClipboard");
        AddCard(stack, sc1);

        // SC2
        var sc2 = new SettingsCard();
        Sk.Toggle(sc2, app, Strings.Screenshot.FreezeToggle, K.Freeze, Strings.Screenshot.FreezeCaption);
        sc2.AddDivider();
        Sk.Toggle(sc2, app, Strings.Screenshot.HideWindows, K.HideOwn);
        sc2.AddDivider();
        var delays = new[] { 0, 3, 5, 10 };
        sc2.Add(Sk.Segmented(Strings.Screenshot.Delay, delays.Select(d => d == 0 ? Strings.Off : Strings.Seconds(d)).ToList(), Math.Max(0, Array.IndexOf(delays, app.Get<int>(K.Delay))), i => app.Set(K.Delay, delays[i])));
        sc2.AddDivider();
        Sk.Toggle(sc2, app, Strings.Screenshot.IncludePointer, K.IncludePointer);
        sc2.AddDivider();
        Sk.Toggle(sc2, app, Strings.Screenshot.LastRegion, K.ShowLastRegion);
        sc2.AddDivider();
        Sk.Toggle(sc2, app, Strings.Screenshot.HighlightWindows, K.HighlightWindows, Strings.Screenshot.HighlightCaption);
        sc2.AddDivider();
        sc2.Add(Sk.Disclosure(Strings.MoreOptions, MoreOptions(c)));
        AddCard(stack, sc2);

        // SC3
        var sc3 = new SettingsCard();
        var copyOn = app.Get<bool>(K.CopyToClipboard) || app.Get<string>(K.DefaultAction) is "copy" or "saveAndCopy";
        Sk.Toggle(sc3, app, Strings.Screenshot.AutoCopy, K.CopyToClipboard, Strings.Screenshot.AutoCopyCaption, on =>
        {
            if (!on)
            {
                app.Set(K.CopyToClipboard, false);
                var action = app.Get<string>(K.DefaultAction);
                if (action == "saveAndCopy")
                {
                    app.Set(K.DefaultAction, "save");
                }
                else if (action == "copy")
                {
                    app.Set(K.DefaultAction, string.Empty);
                }
            }
        }, shownValue: copyOn);
        sc3.AddDivider();
        Sk.Toggle(sc3, app, Strings.Screenshot.CopyImageOnly, K.CopyImageOnly, Strings.Screenshot.CopyImageOnlyCaption);
        sc3.AddDivider();
        var shelfOn = c.Runtime.IsAvailable("shelf") && app.Get<bool>("shelfEnabled");
        Sk.Toggle(sc3, app, Strings.Screenshot.AddToShelf, K.AddToShelf, Strings.Screenshot.AddToShelfCaption, null, shelfOn, shelfOn && app.Get<bool>(K.AddToShelf));
        sc3.AddDivider();
        Sk.Toggle(sc3, app, Loc.T("Keep recent captures"), K.RecentCapturesEnabled, Loc.T("Recent screenshots are kept in Trayloom’s local folder so you can find them again. Turn this off to keep none."), on =>
        {
            if (!on)
            {
                RecentCaptureService.Instance.Clear();
            }
        });
        sc3.AddDivider();
        sc3.Add(Sk.FolderRow(app, K.SaveFolder));
        sc3.AddDivider();
        sc3.Add(PatternRow(app, Strings.Screenshot.Subfolder, K.SaveSubfolder, Strings.Screenshot.SubfolderCaption, false));
        sc3.AddDivider();
        sc3.Add(PatternRow(app, Strings.Screenshot.FileName, K.FileNamePattern, Strings.Screenshot.FileNameCaption, true));
        sc3.AddDivider();
        Sk.Toggle(sc3, app, Strings.Screenshot.Downscale, K.Downscale, Strings.Screenshot.DownscaleCaption);
        AddCard(stack, sc3);

        // SC4
        var sc4 = new SettingsCard(Strings.Screenshot.EditorTools);
        sc4.Add(new ToolOrderPanel(app, dark: false, headline: false));
        AddCard(stack, sc4);
    }

    private static UIElement MoreOptions(ISettingsContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        var inner = new StackPanel();
        void Rebuild()
        {
            inner.Children.Clear();
            var card = new SettingsCard();
            Sk.Toggle(card, app, Strings.Screenshot.LoupeStartsOn, K.LoupeStartsOn);
            card.AddDivider();
            Sk.Toggle(card, app, Strings.Screenshot.LoupeRemember, K.LoupeRememberZoom, null, _ => Rebuild());
            if (!app.Get<bool>(K.LoupeRememberZoom))
            {
                card.AddDivider();
                var zooms = new (string, string)[] { ("0.5", "0.5×"), ("1", "1×"), ("2", "2×"), ("4", "4×") };
                var current = app.Get<double>(K.LoupeDefaultZoom).ToString("0.#", CultureInfo.InvariantCulture);
                card.Add(Sk.Picker(Strings.Screenshot.LoupeDefaultZoom, zooms, zooms.Any(z => z.Item1 == current) ? current : "1", v => app.Set(K.LoupeDefaultZoom, double.Parse(v, CultureInfo.InvariantCulture)), null, 110));
            }

            card.AddDivider();
            card.Add(Sk.Segmented(Strings.Screenshot.LoupeWheel, [Strings.Screenshot.LoupeFast, Strings.Screenshot.LoupeStepped], app.Get<bool>(K.LoupeStepped) ? 1 : 0, i => app.Set(K.LoupeStepped, i == 1)));
            card.AddCaption(Strings.Screenshot.LoupeCaption);
            card.AddDivider();
            card.Add(Sk.Picker(Strings.Screenshot.PreviewPosition, Strings.Screenshot.PreviewPositions, app.Get<string>(K.PreviewPosition) ?? string.Empty, v => app.Set(K.PreviewPosition, v), null, 150));
            card.AddDivider();
            Sk.Toggle(card, app, Strings.Screenshot.PreviewFocus, K.PreviewTakesFocus, Strings.Screenshot.PreviewFocusCaption);
            card.AddDivider();
            var action = app.Get<string>(K.DefaultAction) ?? string.Empty;
            card.Add(Sk.Picker(Strings.Screenshot.DefaultAction, Strings.Screenshot.DefaultActions, action, v =>
            {
                app.Set(K.DefaultAction, v);
                Rebuild();
            }, null, 150));
            if (action != "edit")
            {
                card.AddCaption(Strings.Screenshot.DefaultActionCaption);
            }

            if (action is "save" or "saveAndCopy" or "copy")
            {
                card.AddDivider();
                Sk.Toggle(card, app, Strings.Screenshot.ConfirmationPreview, K.PreviewEnabled, null, _ => Rebuild());
                if (app.Get<bool>(K.PreviewEnabled))
                {
                    card.AddDivider();
                    var durations = new (string, string)[] { ("1", Loc.T("1 s")), ("2", Loc.T("2 s")), ("3", Loc.T("3 s")), ("5", Loc.T("5 s")), ("10", Loc.T("10 s")), ("0", Strings.Screenshot.UntilDismissed) };
                    var d = app.Get<int>(K.PreviewDuration).ToString(CultureInfo.InvariantCulture);
                    card.Add(Sk.Picker(Strings.Screenshot.ConfirmationDuration, durations, durations.Any(x => x.Item1 == d) ? d : "3", v => app.Set(K.PreviewDuration, int.Parse(v, CultureInfo.InvariantCulture)), null, 150));
                }

                card.AddCaption(Strings.Screenshot.ConfirmationCaption);
            }

            inner.Children.Add(card.Content.Children.Count > 0 ? Flatten(card) : card);
        }

        Rebuild();
        stack.Children.Add(inner);
        return stack;
    }

    private static UIElement Flatten(SettingsCard card)
    {
        var stack = card.Content;
        var rows = stack.Children.Cast<UIElement>().ToList();
        stack.Children.Clear();
        var panel = new StackPanel();
        foreach (var r in rows)
        {
            if (r is FrameworkElement fe && panel.Children.Count > 0 && fe is not Border)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            }

            panel.Children.Add(r);
        }

        return panel;
    }

    private static UIElement PatternRow(IAppSettings app, string title, string key, string caption, bool filename)
    {
        var host = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(title, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var preview = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        preview.HorizontalAlignment = HorizontalAlignment.Right;
        preview.VerticalAlignment = VerticalAlignment.Center;
        preview.Margin = new Thickness(12, 0, 8, 0);
        Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        var box = new TextBox { Width = 150, Text = app.Get<string>(key) ?? string.Empty };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        Grid.SetColumn(box, 2);
        grid.Children.Add(box);
        host.Children.Add(grid);
        host.Children.Add(Sk.Caption(caption));
        ((FrameworkElement)host.Children[1]).Margin = new Thickness(0, 6, 0, 0);
        var numbering = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        host.Children.Add(numbering);

        void Refresh()
        {
            var text = box.Text.Trim();
            var now = DateTime.Now;
            if (filename)
            {
                var next = app.Get<int>(K.FileNumberNext);
                preview.Text = text.Length == 0 ? Output.DefaultName(Strings.Screenshot.FileNamePrefix, now) + ".png" : Output.ApplyNumber(Output.ExpandTokens(text, now), next) + ".png";
                numbering.Children.Clear();
                if (text.Contains("%#", StringComparison.Ordinal))
                {
                    numbering.Children.Add(NumberRow(app, Refresh));
                }
            }
            else
            {
                preview.Text = text.Length == 0 ? string.Empty : Output.ExpandTokens(text, now);
            }
        }

        box.TextChanged += (_, _) =>
        {
            app.Set(key, box.Text);
            Refresh();
        };
        Refresh();
        return host;
    }

    private static UIElement NumberRow(IAppSettings app, Action refresh)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = UiKit.Text(Strings.Screenshot.StartsAt, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var start = new TextBox { Width = 70, Text = app.Get<int>(K.FileNumberStart).ToString(CultureInfo.InvariantCulture), Margin = new Thickness(10, 0, 4, 0) };
        start.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        var next = UiKit.Text(string.Format(Strings.Screenshot.NextFormat, app.Get<int>(K.FileNumberNext)), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        next.HorizontalAlignment = HorizontalAlignment.Right;
        next.VerticalAlignment = VerticalAlignment.Center;

        void SetStart(int v)
        {
            v = Math.Clamp(v, 0, 999_999);
            app.Set(K.FileNumberStart, v);
            app.Set(K.FileNumberNext, v);
            start.Text = v.ToString(CultureInfo.InvariantCulture);
            next.Text = string.Format(Strings.Screenshot.NextFormat, v);
            refresh();
        }

        start.LostFocus += (_, _) => SetStart(int.TryParse(start.Text, out var v) ? v : 1);
        Grid.SetColumn(start, 1);
        var minus = UiKit.Button("−", ButtonKind.Bordered, 12, null, () => SetStart(app.Get<int>(K.FileNumberStart) - 1));
        var plus = UiKit.Button("+", ButtonKind.Bordered, 12, null, () => SetStart(app.Get<int>(K.FileNumberStart) + 1));
        Grid.SetColumn(minus, 2);
        Grid.SetColumn(plus, 3);
        var reset = UiKit.Button(Strings.Reset, ButtonKind.Bordered, 12, null, () =>
        {
            app.Set(K.FileNumberNext, app.Get<int>(K.FileNumberStart));
            next.Text = string.Format(Strings.Screenshot.NextFormat, app.Get<int>(K.FileNumberStart));
            refresh();
        });
        reset.Margin = new Thickness(8, 0, 0, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(reset);
        Grid.SetColumn(right, 4);
        grid.Children.Add(start);
        grid.Children.Add(minus);
        grid.Children.Add(plus);
        grid.Children.Add(right);
        var host = new StackPanel();
        host.Children.Add(grid);
        next.Margin = new Thickness(0, 4, 0, 0);
        host.Children.Add(next);
        return host;
    }
}

public sealed class ScreenRecorderSection : ToolSectionBase
{
    private protected override CaptureTool Tool => CaptureTool.Recording;

    public override string? Title => Strings.Tools.Recording;

    public override IReadOnlyList<string> Keywords => ["record now", "countdown", "microphone", "system audio", "editor", "zoom", "quality", "frames per second", "gif"];

    protected override void Fill(StackPanel stack, ISettingsContext c)
    {
        var app = c.App;
        var r1 = new SettingsCard(Strings.Tools.Recording);
        var caption = Sk.Caption(Strings.Recorder.PanelCaption);
        var button = UiKit.Button(Strings.Recorder.RecordNow, ButtonKind.Bordered, 13, G.Record);
        button.Padding = new Thickness(10, 9, 10, 9);
        void Refresh()
        {
            var rec = CaptureHost.Recorder;
            var recording = rec?.IsRecording == true;
            if (button.Content is StackPanel sp && sp.Children.Count == 2)
            {
                ((TextBlock)sp.Children[0]).Text = recording ? G.Stop : G.Record;
                ((TextBlock)sp.Children[1]).Text = recording ? Strings.Recorder.Stop : Strings.Recorder.RecordNow;
            }

            ((TextBlock)caption).Text = recording ? RecorderSupport.ElapsedLabel(rec!.ElapsedSeconds) : Strings.Recorder.PanelCaption;
            ((TextBlock)caption).FontFamily = recording ? new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") : UiKit.TextFont;
        }

        button.Click += (_, _) => c.Ui.BeginInvoke(() => CaptureHost.Recorder?.Toggle());
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
        button.Loaded += (_, _) => { Refresh(); timer.Start(); };
        button.Unloaded += (_, _) => timer.Stop();
        r1.Add(button);
        r1.Add(caption);
        ((FrameworkElement)caption).Margin = new Thickness(0, 6, 0, 0);
        AddCard(stack, r1);

        var r2 = new SettingsCard();
        var countdowns = new[] { 0, 3, 5, 10 };
        r2.Add(Sk.Segmented(Strings.Recorder.Countdown, countdowns.Select(d => d == 0 ? Strings.Off : Strings.Seconds(d)).ToList(), Math.Max(0, Array.IndexOf(countdowns, app.Get<int>(K.RecCountdown))), i => app.Set(K.RecCountdown, countdowns[i])));
        r2.AddDivider();
        Sk.Toggle(r2, app, Strings.Recorder.SystemAudio, K.RecSystemAudio, Strings.Recorder.SystemAudioCaption);
        r2.AddDivider();
        Sk.Toggle(r2, app, Strings.Recorder.Microphone, K.RecMicrophone, Strings.Recorder.MicrophoneCaption);
        r2.AddDivider();
        Sk.Toggle(r2, app, Strings.Recorder.OpenEditor, K.RecOpenEditor, Strings.Recorder.OpenEditorCaption);
        r2.AddDivider();
        Sk.Toggle(r2, app, Strings.Recorder.AutoZoom, K.RecAutoZoom, Strings.Recorder.AutoZoomCaption);
        AddCard(stack, r2);

        var r3 = new SettingsCard();
        r3.Add(Sk.FolderRow(app, K.RecSaveFolder));
        r3.AddDivider();
        var more = new StackPanel();
        var qualities = new (string, string)[] { ("small", Loc.T("Small file")), ("balanced", Loc.T("Balanced")), ("high", Loc.T("High")) };
        more.Children.Add(Sk.Picker(Strings.Recorder.Quality, qualities, RecorderSupport.SanitizedQuality(app.Get<string>(K.RecQuality)), v => app.Set(K.RecQuality, v), null, 140));
        more.Children.Add(Sk.Caption(Strings.Recorder.QualityCaption));
        var fps = new[] { 30, 60 };
        var seg1 = Sk.Segmented(Strings.Recorder.FrameRate, fps.Select(f => f + " fps").ToList(), Array.IndexOf(fps, RecorderSupport.SanitizedFps(app.Get<int>(K.RecFrameRate))), i => app.Set(K.RecFrameRate, fps[i]));
        ((FrameworkElement)seg1).Margin = new Thickness(0, 12, 0, 0);
        more.Children.Add(seg1);
        var sizes = new[] { "small", "medium", "large" };
        var seg2 = Sk.Segmented(Strings.Recorder.GifSize, [Loc.T("Small"), Loc.T("Medium"), Loc.T("Large")], Math.Max(0, Array.IndexOf(sizes, app.Get<string>(K.RecGifSize))), i => app.Set(K.RecGifSize, sizes[i]));
        ((FrameworkElement)seg2).Margin = new Thickness(0, 12, 0, 0);
        more.Children.Add(seg2);
        var gifFps = new[] { 8, 12, 15 };
        var seg3 = Sk.Segmented(Strings.Recorder.GifSmoothness, gifFps.Select(f => f + " fps").ToList(), Math.Max(0, Array.IndexOf(gifFps, app.Get<int>(K.RecGifFps))), i => app.Set(K.RecGifFps, gifFps[i]));
        ((FrameworkElement)seg3).Margin = new Thickness(0, 12, 0, 0);
        more.Children.Add(seg3);
        r3.Add(Sk.Disclosure(Strings.MoreOptions, more));
        AddCard(stack, r3);

        AddRecoveryCard(stack, c);
    }

    /// <summary>One row per finished recording that was never saved (a crash, a forced quit): open it in the editor, or delete it.</summary>
    private void AddRecoveryCard(StackPanel stack, ISettingsContext c)
    {
        var owned = CaptureHost.Recorder?.OwnedFolders() ?? [];
        var takes = Take.Recoverable(owned);
        if (takes.Count == 0)
        {
            return;
        }

        var card = new SettingsCard();
        for (var i = 0; i < takes.Count; i++)
        {
            if (i > 0)
            {
                card.AddDivider();
            }

            var (folder, time) = takes[i];
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var open = UiKit.Button(Loc.T("Open"), ButtonKind.Bordered, 12, null, () => c.Ui.BeginInvoke(() =>
            {
                CaptureHost.Recorder?.OpenRecovered(folder);
                c.RequestRebuild();
            }));
            var delete = UiKit.Button(Loc.T("Delete"), ButtonKind.Bordered, 12, null, () =>
            {
                var answer = MessageBox.Show(Loc.T("This recording hasn’t been saved anywhere else. Delete it?"), Loc.T("Delete recording?"), MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
                if (answer == MessageBoxResult.OK)
                {
                    if (CaptureHost.Recorder?.OwnedFolders().Any(o => string.Equals(o, folder, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        CaptureHost.Beep();
                        c.RequestRebuild();
                        return;
                    }

                    new Take { Folder = folder }.Delete();
                    c.RequestRebuild();
                }
            });
            delete.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(open);
            buttons.Children.Add(delete);
            card.Add(new SettingsRow(null, Loc.F("Unsaved recording from {0}", time.ToString("g", System.Globalization.CultureInfo.CurrentCulture)), null, buttons));
        }

        AddCard(stack, card);
    }
}

public sealed class ScreenTextSection : ToolSectionBase
{
    private protected override CaptureTool Tool => CaptureTool.Text;

    public override string? Title => Strings.Tools.Text;

    public override IReadOnlyList<string> Keywords => ["ocr", "qr", "line breaks", "recognize text"];

    protected override void Fill(StackPanel stack, ISettingsContext c)
    {
        var card = new SettingsCard(Strings.Tools.Text);
        var btn = UiKit.Button(Strings.Tools.Text, ButtonKind.Bordered, 13, G.TextScan, () => c.Ui.BeginInvoke(() => CaptureHost.Text?.Capture()));
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(btn);
        card.Add(Sk.Caption(Strings.Ocr.Caption));
        ((FrameworkElement)card.Content.Children[^1]).Margin = new Thickness(0, 6, 0, 0);
        card.AddDivider();
        Sk.Toggle(card, c.App, Strings.Ocr.RemoveBreaks, K.OcrRemoveBreaks, Strings.Ocr.RemoveBreaksCaption);
        card.AddDivider();
        Sk.Toggle(card, c.App, Strings.Ocr.ReadQr, K.OcrQr, Strings.Ocr.ReadQrCaption);
        if (!OcrSupport.HasEngine())
        {
            card.AddDivider();
            card.Add(Sk.Warning(Strings.Ocr.NoLanguage));
        }

        AddCard(stack, card);
    }
}

public sealed class ColorPickerSection : ToolSectionBase
{
    private protected override CaptureTool Tool => CaptureTool.Color;

    public override string? Title => Strings.Tools.Color;

    public override IReadOnlyList<string> Keywords => ["eyedropper", "hex", "rgb", "hsl", "swiftui", "format"];

    protected override void Fill(StackPanel stack, ISettingsContext c)
    {
        var app = c.App;
        var card = new SettingsCard(Strings.Tools.Color);
        var btn = UiKit.Button(Strings.Color.PickNow, ButtonKind.Bordered, 13, G.Eyedropper, () => c.Ui.BeginInvoke(() => CaptureHost.ColorSampler?.Pick()));
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(btn);
        card.Add(Sk.Caption(Strings.Color.Caption));
        ((FrameworkElement)card.Content.Children[^1]).Margin = new Thickness(0, 6, 0, 0);
        card.AddDivider();
        var formats = new[] { "hex", "rgb", "hsl", "swiftui" };
        var holder = new StackPanel();
        void Build()
        {
            holder.Children.Clear();
            var current = app.Get<string>(K.ColorFormat) ?? "hex";
            if (Array.IndexOf(formats, current) < 0)
            {
                current = "hex";
            }

            holder.Children.Add(Sk.Segmented(Strings.Color.Format, ["HEX", "RGB", "HSL", "SwiftUI"], Array.IndexOf(formats, current), i =>
            {
                app.Set(K.ColorFormat, formats[i]);
                Build();
            }));
            if (current == "hex")
            {
                var row = new SettingsCard();
                Sk.Toggle(row, app, Strings.Color.BareHex, K.ColorBareHex);
                var rows = row.Content.Children.Cast<UIElement>().ToList();
                row.Content.Children.Clear();
                foreach (var r in rows)
                {
                    ((FrameworkElement)r).Margin = new Thickness(0, 12, 0, 0);
                    holder.Children.Add(r);
                }
            }
        }

        Build();
        card.Add(holder);
        AddCard(stack, card);
    }
}
