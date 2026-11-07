// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>Settings > Dynamic Island > Content: section list, per-section options and a live preview (spec 02d section 3).</summary>
internal sealed class ContentEditor : Grid
{
    private readonly ISettingsContext _c;
    private readonly IslandCore? _core;
    private readonly StackPanel _listHost = new();
    private readonly StackPanel _detail = new();
    private readonly Border _preview = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(0), MinHeight = 200 };
    private string _selected;
    private List<string> _order = [];

    public ContentEditor(ISettingsContext c, string? pending)
    {
        _c = c;
        _core = IslandCore.Current;
        _selected = pending ?? "controls";
        var listColumn = new ColumnDefinition { Width = new GridLength(216) };
        ColumnDefinitions.Add(listColumn);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // A narrower section list at small widths gives the options column room. The editor's own width does not depend on its columns, so this cannot feed back.
        SizeChanged += (_, e) => listColumn.Width = new GridLength(e.NewSize.Width < 660 ? 180 : 216);
        var list = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(10) };
        list.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
        var lstack = new StackPanel();
        var h = UiKit.Text(Loc.T("Sections"), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        h.Margin = new Thickness(6, 0, 6, 2);
        lstack.Children.Add(h);
        var cap = SUi.Caption(Loc.T("Drag to reorder. Uncheck a section to hide it from the island."));
        cap.Margin = new Thickness(6, 0, 6, 8);
        lstack.Children.Add(cap);
        lstack.Children.Add(_listHost);
        list.Child = lstack;
        Children.Add(list);
        var right = new StackPanel();
        right.Children.Add(_detail);
        Grid.SetColumn(right, 2);
        Children.Add(right);
        BuildList();
        BuildDetail();
    }

    private IEnumerable<ModuleDef> Defs => _order.Select(IslandCatalog.Get).Where(m => m is not null).Select(m => m!);

    private bool Available(ModuleDef m) => IslandCatalog.FeatureAvailable(_c.Runtime, m) && (!IslandCatalog.IsContributed(m.Id) || (_core?.External(m.Id) is not null));

    private bool Shown(ModuleDef m)
    {
        var app = _c.App;
        return !K.Csv(app.Get<string>(K.HiddenModules)).Contains(m.Id) && Available(m) && (m.EnableKey is null || app.Get<bool>(m.EnableKey));
    }

    private void SetShown(ModuleDef m, bool on)
    {
        var app = _c.App;
        var hidden = K.Csv(app.Get<string>(K.HiddenModules));
        hidden.Remove(m.Id);
        if (!on)
        {
            hidden.Add(m.Id);
        }

        hidden.Sort(StringComparer.Ordinal);
        app.Set(K.HiddenModules, string.Join(",", hidden));
        if (m.EnableKey is not null)
        {
            app.Set(m.EnableKey, on);
        }

        BuildList();
        BuildDetail();
    }

    private void BuildList()
    {
        _order = IslandCatalog.OrderedIds(_c.App);
        _listHost.Children.Clear();
        foreach (var m in Defs)
        {
            _listHost.Children.Add(Row(m));
        }
    }

    private FrameworkElement Row(ModuleDef m)
    {
        var selected = m.Id == _selected;
        var available = Available(m);
        var shown = Shown(m);
        var row = new Border { MinHeight = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), Cursor = Cursors.Hand, Tag = m.Id };
        if (selected)
        {
            row.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
        }
        else
        {
            row.Background = Brushes.Transparent;
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cb = new CheckBox { IsChecked = shown && available, IsEnabled = available, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        cb.Click += (_, _) => SetShown(m, cb.IsChecked == true);
        g.Children.Add(cb);
        var tile = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = shown && available ? new SolidColorBrush(m.Tint) : new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), Child = D.I(m.Glyph, 10, shown && available ? (m.DarkGlyph ? D.White(0.75) : Brushes.White) : D.White(0.5)), Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(tile, 1);
        g.Children.Add(tile);
        var title = UiKit.Text(m.Title, 12.5, selected ? FontWeights.SemiBold : FontWeights.Normal, available ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, true);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 2);
        g.Children.Add(title);
        row.Child = g;
        row.ToolTip = m.Summary;
        Point? start = null;
        row.MouseLeftButtonDown += (_, e) => start = e.GetPosition(row);
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not null)
            {
                _selected = m.Id;
                BuildList();
                BuildDetail();
            }

            start = null;
        };
        row.MouseMove += (_, e) =>
        {
            if (start is { } s && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(row) - s).Length > 6)
            {
                start = null;
                DragDrop.DoDragDrop(row, new DataObject("island-module", m.Id), DragDropEffects.Move);
            }
        };
        row.AllowDrop = true;
        row.DragEnter += (_, e) =>
        {
            if (e.Data.GetData("island-module") is string from && from != m.Id)
            {
                var order = _order.ToList();
                var a = order.IndexOf(from);
                var b = order.IndexOf(m.Id);
                order.RemoveAt(a);
                order.Insert(b, from);
                _c.App.Set(K.ModuleOrder, string.Join(",", order));
                BuildList();
            }
        };
        row.ContextMenu = new ContextMenu();
        var up = new MenuItem { Header = Loc.T("Move up") };
        up.Click += (_, _) => Move(m.Id, -1);
        var down = new MenuItem { Header = Loc.T("Move down") };
        down.Click += (_, _) => Move(m.Id, 1);
        row.ContextMenu.Items.Add(up);
        row.ContextMenu.Items.Add(down);
        return row;
    }

    private void Move(string id, int by)
    {
        var order = _order.ToList();
        var i = order.IndexOf(id);
        var j = i + by;
        if (i < 0 || j < 0 || j >= order.Count)
        {
            return;
        }

        (order[i], order[j]) = (order[j], order[i]);
        _c.App.Set(K.ModuleOrder, string.Join(",", order));
        BuildList();
    }

    private void BuildDetail()
    {
        _detail.Children.Clear();
        var m = IslandCatalog.Get(_selected) ?? IslandCatalog.Modules[0];
        var available = Available(m);
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(m.Tint), Child = D.I(m.Glyph, 17, m.DarkGlyph ? D.White(0.75) : Brushes.White), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        head.Children.Add(tile);
        var col = new StackPanel();
        col.Children.Add(UiKit.Text(m.Title, 16, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        col.Children.Add(UiKit.Text(m.Summary, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, true));
        if (!available)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var missing = IslandCatalog.MissingFeatureTitle(_c.Runtime, m.Features);
            row.Children.Add(UiKit.Text(missing is null ? Strings.Disabled : Loc.F("Enable “{0}” in Features.", missing), 12.5, FontWeights.Normal, ThemeKeys.Orange));
            var open = UiKit.Button(Loc.T("Open Features"), ButtonKind.Bordered, 11.5, null, () => _c.Shell.OpenFeatures(m.Features.FirstOrDefault(f => !_c.Runtime.IsAvailable(f))));
            open.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(open);
            col.Children.Add(row);
        }

        Grid.SetColumn(col, 1);
        head.Children.Add(col);
        _detail.Children.Add(head);
        // preview
        _preview.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsCard);
        _preview.Margin = new Thickness(0, 14, 0, 0);
        BuildPreview(m, available);
        if (_preview.Parent is Panel p)
        {
            p.Children.Remove(_preview);
        }

        _detail.Children.Add(_preview);
        // options
        var card = new SettingsCard();
        var any = Options(m, card);
        card.Margin = new Thickness(0, 14, 0, 0);
        if (available && any)
        {
            _detail.Children.Add(card);
            if (m.Id == "music")
            {
                var services = MusicServicesCard.Create(_c, _core);
                services.Margin = new Thickness(0, 14, 0, 0);
                _detail.Children.Add(services);
            }
        }
        else if (available)
        {
            var t = UiKit.Text(Loc.T("This section has no options."), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            t.Margin = new Thickness(0, 14, 0, 0);
            _detail.Children.Add(t);
        }
    }

    private void BuildPreview(ModuleDef m, bool available)
    {
        var grid = new Grid { Height = 260 };
        var hiddenFromIsland = !Shown(m);
        FrameworkElement? island = null;
        if (_core is not null)
        {
            try
            {
                var custom = IslandSizes.IsCustom(_c.App);
                var w = IslandSizes.Width(_c.App);
                IslandPage? page = m.Id == "camera"
                    ? new StillPage(Loc.T("The camera turns on only in the island."), G.Video)
                    : (m.Id == "agents" && _core.External("agents") is null ? new StillPage(Loc.T("Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs."), G.Claude) : PageFactory.Create(_core, null, m.Id));
                if (page is not null)
                {
                    page.IsPreview = true;
                    if (page.NaturalWidth is { } nw && !custom)
                    {
                        w = Math.Min(w, nw + 56);
                    }

                    var budget = IslandSizes.Budget(_c.App, 76);
                    var ch = Math.Clamp(page.Height(w - 56, budget), 40, budget);

                    // the preview draws what the island draws: the notch only exists while the island rests against its edge
                    var position = SettingsTabs.SettingsPosition(_c.App);
                    var capsule = IslandSilhouette.DrawsCapsule(_c.App.Get<string>(K.Silhouette), position);
                    var bottom = position.Edge == IslandEdge.Bottom;
                    var h = page.HasTopRow ? (capsule ? 12 : 0) + 64 + ch : 32 + ch;
                    var corners = capsule ? new CornerRadius(28) : (bottom ? new CornerRadius(28, 28, 0, 0) : new CornerRadius(0, 0, 28, 28));
                    var surface = new Grid { Width = w, Height = h, IsHitTestVisible = false };
                    surface.Children.Add(new Border { Background = Brushes.Black, CornerRadius = corners, Width = w, Height = h });
                    var inner = new Grid { Margin = new Thickness(28, page.HasTopRow ? (capsule ? 60 : 48) : 16, 28, 16), ClipToBounds = true };
                    inner.Children.Add(page.View);
                    surface.Children.Add(inner);
                    var vb = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = 236, Child = surface, Opacity = hiddenFromIsland ? 0.4 : 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 14, 16, 0) };
                    island = vb;
                }
            }
            catch (Exception ex)
            {
                _core.Ctx.Log.Warn($"Island preview failed: {ex.Message}");
            }
        }

        if (island is not null)
        {
            grid.Children.Add(island);
        }

        var cap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 8) };
        cap.Children.Add(UiKit.Icon(hiddenFromIsland ? "" : "", 11, ThemeKeys.TextSecondary));
        var ct = UiKit.Text(hiddenFromIsland ? Loc.T("Hidden from the island") : Loc.T("Preview"), 11, FontWeights.Medium, ThemeKeys.TextSecondary);
        ct.Margin = new Thickness(5, 0, 0, 0);
        cap.Children.Add(ct);
        grid.Children.Add(cap);
        _preview.Child = grid;
        _ = available;
    }

    // ---------------------------------------------------------------- options

    private bool Options(ModuleDef m, SettingsCard card)
    {
        var app = _c.App;
        var rt = _c.Runtime;
        switch (m.Id)
        {
            case "controls":
                ControlsOptions(card);
                return true;
            case "music":
                card.Add(SUi.SwitchRow(app, K.ShowPlayingMusic, G.Music, Loc.T("Show music while playing")));
                card.AddDivider();
                card.Add(SUi.SwitchRow(app, K.IncludeOtherPlayers, "", Loc.T("Automatically include videos and other apps")));
                card.AddDivider();
                card.Add(new FeatureSwitchRow(rt, _c.Shell, "mixer", G.Speaker, Loc.T("Show volume in the player"), null, app.Get<bool>(K.MusicShowVolume), on => app.Set(K.MusicShowVolume, on), Loc.T("Enable “Volume mixer” in Features.")));
                card.AddDivider();
                var players = new List<(string Value, string Label)> { ("", Loc.T("Automatic")) };
                players.AddRange((PlaybackService.InstalledPlayers()).Select(p => (p.Aumid, p.Name)));
                var saved = app.Get<string>(K.PreferredPlayer);
                if (!string.IsNullOrEmpty(saved) && players.All(p => p.Value != saved))
                {
                    players.Add((saved, PlaybackService.PrettyName(saved)));
                }

                card.Add(new PickerRow<string>("", Loc.T("Open when nothing is playing"), Loc.T("The cover and the playback buttons open this app. Automatic opens the music app that played last, then Spotify."), players, saved, v => app.Set(K.PreferredPlayer, v), 180));
                card.AddDivider();
                card.Add(new FeatureSwitchRow(rt, _c.Shell, "notch-lyrics", "", Loc.T("Show lyrics"), null, app.Get<bool>(K.LyricsEnabled), on => { app.Set(K.LyricsEnabled, on); _c.RequestRebuild(); }, Loc.T("Enable “Lyrics” in Features.")));
                if (app.Get<bool>(K.LyricsEnabled) && rt.IsAvailable("notch-lyrics"))
                {
                    card.Add(SUi.Indent(SUi.SwitchRow(app, K.LyricsOnline, "", Loc.T("Find lyrics online"), Strings.LyricsOnlineHint)));
                }

                if (IslandCore.Current?.AppleMusic.Available ?? false)
                {
                    card.AddDivider();
                    card.Add(SUi.SwitchRow(app, K.AppleMusicKeepHidden, "", Loc.T("Keep Apple Music out of sight"), Loc.T("While the island plays Apple Music its window stays hidden and off the taskbar. Minimising Apple Music while it plays sends it to the island."), _ => _c.RequestRebuild()));
                    if (app.Get<bool>(K.AppleMusicKeepHidden))
                    {
                        card.Add(SUi.Indent(SUi.SwitchRow(app, K.AppleMusicResumeOnQuit, "", Loc.T("Keep playing if Apple Music is closed"), Loc.T("Starts it again in the background and continues the song where it stopped (takes about 20 seconds)."))));
                    }
                }

                card.AddDivider();
                card.Add(new FeatureSwitchRow(rt, _c.Shell, "notch-queue", "", Loc.T("Show upcoming songs"), Loc.T("See the actual upcoming songs shared by your player."), app.Get<bool>(K.QueueEnabled), on => app.Set(K.QueueEnabled, on), Loc.T("Enable “Up next” in Features.")));
                card.AddDivider();
                card.Add(new FeatureSwitchRow(rt, _c.Shell, "notch-live-equalizer", "", Loc.T("Bars follow the music"), Loc.T("Listens to the player’s own audio output to move the bars. Nothing is stored or sent. Without it, the bars keep their usual motion."), app.Get<bool>(K.LiveEqualizer), on => app.Set(K.LiveEqualizer, on), Loc.T("Enable “Bars follow the music” in Features.")));
                return true;
            case "notifications":
                card.Add(SUi.SwitchRow(app, K.DismissNative, "", Loc.T("Hide the system banner"), Loc.T("Hide the original while the Dynamic Island shows it."), enabled: false, badge: null));
                card.AddCaption(Loc.T("Not available on this PC"));
                card.AddDivider();
                card.Add(SUi.SwitchRow(app, K.NotificationsReadScreen, "", Loc.T("Read notification banners on screen"), Loc.T("Windows only lets packaged apps use the notification listener, so Trayloom can instead read each banner’s text as it appears. Banners can contain codes and private messages."), _ => _c.RequestRebuild()));
                var status = IslandCore.Current?.Notifications;
                if (status is { NeedsIdentity: true } && !app.Get<bool>(K.NotificationsReadScreen))
                {
                    card.AddCaption(Loc.T("Turn on “Read notification banners on screen” above to see banners in the island."));
                }
                else if (status is { NeedsIdentity: true } or { AccessDenied: true })
                {
                    var row = new SettingsRow("", Loc.T("Notification access"), status.NeedsIdentity ? Loc.T("Reading banners works best when Trayloom is installed as an app.") : Loc.T("Allow notification access in Windows Settings."), UiKit.Button(status.AccessDenied ? Loc.T("Open Settings") : "Allow", ButtonKind.Bordered, 11.5, null, () => { if (status.AccessDenied) { status.OpenAccessSettings(); } else { _ = status.RequestAccessAsync(); } }));
                    card.Add(row);
                }

                return true;
            case "downloads":
                DownloadsOptions(card);
                return true;
            case "calendar":
                CalendarOptions(card);
                return true;
            case "timer":
                card.Add(SUi.SwitchRow(app, K.HideTimerCountdown, "", Loc.T("Hide timer countdown"), null, enabled: rt.IsAvailable("notch-timer")));
                card.AddDivider();
                TimerAlarmSettings.Add(card, app, rt, _c.RequestRebuild);
                FocusSettings.Add(card, app, _c);
                return true;
            case "camera":
                card.AddCaption(Loc.T("Open a live mirror here. The camera stops when you leave this view."));
                card.Add(UiKit.Button(Loc.T("Open Settings…"), ButtonKind.Bordered, 12, null, () => UiKit.OpenUrl("ms-settings:privacy-webcam")));
                return true;
            case "files":
                card.Add(SUi.DestinationRow(app, K.Shelf, G.Files, Loc.T("Files"), null, rt.IsAvailable("shelf")));
                if (app.Get<bool>(K.Shelf))
                {
                    card.AddDivider();
                    card.Add(SUi.SwitchRow(app, K.DragReveal, "", Loc.T("Show a drop target while dragging")));
                }

                if (rt.IsAvailable("media-tools"))
                {
                    card.AddCaption(Loc.T("Drag images or one video onto the island, then drop on Optimize media to choose how to save a copy."));
                }

                return true;
            case "clipboard":
                card.Add(SUi.DestinationRow(app, K.ClipboardWindow, G.Clipboard, Loc.T("Clipboard"), null, rt.IsAvailable("clipboard-history")));
                card.AddDivider();
                card.Add(SUi.SwitchRow(app, K.Clipboard, G.Clipboard, Loc.T("Notify when something is copied"), Loc.T("Copied content stays private until you open the clipboard. These options follow the original feature’s settings.")));
                if (app.Get<bool>(K.ClipboardWindow) && rt.IsAvailable("clipboard-history"))
                {
                    var seg = new SegmentedControl([Loc.T("Compact"), Loc.T("Comfortable")], app.Get<string>(K.ClipboardCardSize) == "compact" ? 0 : 1, false, 12);
                    seg.SelectionChanged += i => app.Set(K.ClipboardCardSize, i == 0 ? "compact" : "comfortable");
                    card.Add(new SettingsRow("", Loc.T("Entry size"), Loc.T("Compact entries show one or two lines and open up when the pointer rests on them."), seg));
                }

                return true;
            case "captures":
                card.Add(new FeatureSwitchRow(rt, _c.Shell, "screenshot", G.Camera, Loc.T("Show screenshot previews here"), null, app.Get<bool>(K.Capture), on => { app.Set(K.Capture, on); _c.RequestRebuild(); }, Loc.T("Enable “Screenshot” in Features.")));
                if (app.Get<bool>(K.Capture) && rt.IsAvailable("screenshot"))
                {
                    var actions = new List<(string Value, string Label)> { ("", Loc.T("Ask each time")), ("save", Loc.T("Save")), ("saveAndCopy", Loc.T("Save & Copy")), ("copy", Loc.T("Copy")), ("edit", Loc.T("Edit")) };
                    var row = new PickerRow<string>(null, Loc.T("Default action"), null, actions, app.Get<string>(K.ScreenshotDefaultAction), v => app.Set(K.ScreenshotDefaultAction, v), 160);
                    card.Add(SUi.Indent(row));
                }

                return true;
            case "scratchpad":
                card.Add(SUi.DestinationRow(app, K.Scratchpad, G.Note, Loc.T("Scratchpad"), null, rt.IsAvailable("scratchpad")));
                return true;
            case "agents":
                card.Add(SUi.SwitchRow(app, K.AgentsEnabled, G.Sparkle, Loc.T("AI Agents"), Loc.T("Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this PC and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your PC.")));
                return true;
            case "watch":
                card.Add(SUi.SwitchRow(app, K.WatchEnabled, G.Eye, Loc.T("Watch"), Loc.T("Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for."), enabled: rt.IsAvailable("notch-watch")));
                if (app.Get<bool>(K.WatchEnabled))
                {
                    card.AddDivider();
                    card.Add(SUi.SwitchRow(app, K.WatchSound, "", Loc.T("Play a sound")));
                }

                return true;
        }

        return false;
    }

    private void ControlsOptions(SettingsCard card)
    {
        var app = _c.App;
        var rt = _c.Runtime;
        var hidden = K.Csv(app.Get<string>(K.HiddenControls));
        void Toggle(string id)
        {
            var h = K.Csv(app.Get<string>(K.HiddenControls));
            if (!h.Remove(id))
            {
                h.Add(id);
            }

            h.Sort(StringComparer.Ordinal);
            app.Set(K.HiddenControls, string.Join(",", h));
            BuildDetail();
        }

        FrameworkElement CardFor(ControlDef d)
        {
            var avail = d.Features.Length == 0 || rt.AnyAvailable(d.Features);
            var reason = avail ? null : Loc.F("Enable “{0}” in Features.", FeatureCatalog.Get(d.Features[0])?.Title ?? d.Features[0]);
            return SUi.ToggleCard(d.Glyph, d.Title, !hidden.Contains(d.Id), avail, reason, () => Toggle(d.Id), () => _c.Shell.OpenFeatures(d.Features.FirstOrDefault()), true);
        }

        var primary = IslandCatalog.Controls.Where(d => d.Id is "music" or "volume" or "brightness" or "keyboardLight").OrderBy(d => d.Id switch { "music" => 0, "volume" => 1, "brightness" => 2, _ => 3 }).ToList();
        var primaryGrid = new TileGrid { MinItemWidth = 92, MaxColumns = 4 };
        foreach (var d in primary)
        {
            primaryGrid.Children.Add(CardFor(d));
        }

        card.Add(primaryGrid);
        var sub = UiKit.Text(Loc.T("Controls and shortcuts"), 12.5, FontWeights.Medium, ThemeKeys.TextPrimary);
        card.Add(sub);
        var order = IslandCatalog.ControlOrder(app).Select(IslandCatalog.Control).Where(d => d is { IsLevel: false } && d.Id != "music").Select(d => d!).ToList();
        // TileGrid lays tiles out in child order, so the drag-to-reorder below (which rewrites the order and rebuilds) keeps working.
        var grid = new TileGrid { MinItemWidth = 92, MaxColumns = 4, Spacing = 8 };
        for (var i = 0; i < order.Count; i++)
        {
            var d = order[i];
            var item = CardFor(d);
            item.AllowDrop = true;
            Point? start = null;
            item.MouseLeftButtonDown += (_, e) => start = e.GetPosition(item);
            item.MouseMove += (_, e) =>
            {
                if (start is { } s && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(item) - s).Length > 6)
                {
                    start = null;
                    DragDrop.DoDragDrop(item, new DataObject("island-control", d.Id), DragDropEffects.Move);
                }
            };
            item.DragEnter += (_, e) =>
            {
                if (e.Data.GetData("island-control") is string from && from != d.Id)
                {
                    var ids = order.Select(x => x.Id).ToList();
                    var a = ids.IndexOf(from);
                    var b = ids.IndexOf(d.Id);
                    ids.RemoveAt(a);
                    ids.Insert(b, from);
                    app.Set(K.ControlOrder, string.Join(",", ids));
                    BuildDetail();
                }
            };
            grid.Children.Add(item);
        }

        card.Add(grid);
        card.AddDivider();
        card.Add(new FeatureSwitchRow(rt, _c.Shell, "keep-awake", "", Loc.T("Show “Keep awake” in the closed island"), Loc.T("A running session appears in the island with the time it has left, or ∞ when it has no end."), app.Get<bool>(K.KeepAwakeActivity), on => app.Set(K.KeepAwakeActivity, on), Loc.T("Enable “Keep awake” in Features.")));
    }

    private void DownloadsOptions(SettingsCard card)
    {
        var app = _c.App;
        var svc = _core?.Downloads;
        card.Add(SUi.SwitchRow(app, K.DownloadsEnabled, G.Download, Loc.T("Downloads"), Loc.T("Choose the folder where your browser saves downloads. Only that folder is watched."), enabled: _c.Runtime.IsAvailable("notch-downloads")));
        if (svc is not null)
        {
            if (svc.FolderUnavailable)
            {
                card.AddCaption(Loc.T("This folder is unavailable. Choose it again to restore access."));
            }
            else if (!string.IsNullOrEmpty(svc.Folder))
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal };
                r.Children.Add(UiKit.Icon(G.Folder, 12, ThemeKeys.TextSecondary));
                var t = UiKit.Text(svc.FolderName, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
                t.Margin = new Thickness(6, 0, 0, 0);
                r.Children.Add(t);
                card.Add(r);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(UiKit.Button(Loc.T("Choose Folder…"), ButtonKind.Bordered, 11.5, null, () =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.T("Choose the folder where your browser saves downloads. Only that folder is watched."), UseDescriptionForTitle = true };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    svc.Choose(dlg.SelectedPath);
                    BuildDetail();
                }
            }));
            if (!string.IsNullOrEmpty(svc.Folder) || svc.FolderUnavailable)
            {
                var forget = UiKit.Button(Loc.T("Forget Folder"), ButtonKind.Bordered, 11.5, null, () =>
                {
                    svc.Forget();
                    BuildDetail();
                });
                forget.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(forget);
            }

            card.Add(buttons);
        }
    }

    private void CalendarOptions(SettingsCard card)
    {
        var app = _c.App;
        var svc = _core?.Calendar;
        card.AddCaption(Loc.T("Read your calendars to show upcoming appointments. Events stay on this PC."));
        GoogleCalendarSettings.Add(card, app, svc, _c);
        card.AddDivider();

        // The Windows permission rows only matter while Windows Calendar feeds the island.
        var usesWindows = svc is null || svc.UsesWindows;
        if (svc is not null && usesWindows)
        {
            if (svc.Access == CalendarAccess.Allowed)
            {
                var ok = new StackPanel { Orientation = Orientation.Horizontal };
                ok.Children.Add(UiKit.Icon("", 13, ThemeKeys.Green));
                var t = UiKit.Text(Loc.T("Granted"), 12.5, FontWeights.Normal, ThemeKeys.Green);
                t.Margin = new Thickness(6, 0, 0, 0);
                ok.Children.Add(t);
                card.Add(ok);
            }
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(UiKit.Button(Loc.T("Allow Calendar Access"), ButtonKind.Bordered, 11.5, null, async () => { await svc.RequestAccessAsync(); _c.RequestRebuild(); }));
                var open = UiKit.Button(Loc.T("Open Windows Settings"), ButtonKind.Bordered, 11.5, null, CalendarPage.OpenSettingsUri);
                open.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(open);
                card.Add(row);
                if (svc.RequestFailed)
                {
                    card.Add(UiKit.Text(Loc.T("Could not request calendar access. Please try again."), 11.5, FontWeights.Normal, ThemeKeys.Orange));
                }
            }
        }

        if (usesWindows)
        {
            card.AddDivider();
        }

        card.Add(SUi.SwitchRow(app, K.CalendarCountdown,"", Loc.T("Event countdown"), Loc.T("Show every timed event in the closed island before it starts. To count down to only some, turn this off and right-click them on the island’s Calendar page. Titles may appear in screen captures.")));
        var leads = new List<(int Value, string Label)>();
        var cur = Math.Clamp(app.Get<int>(K.CalendarLead), 5, 240);
        foreach (var v in new[] { 15, 30, 60, 120, cur }.Distinct().OrderBy(x => x))
        {
            leads.Add((v, v >= 60 && v % 60 == 0 ? Loc.F("{0} hr", v / 60) : Loc.F("{0} min", v)));
        }

        card.Add(new PickerRow<int>("", Loc.T("Time before event"), null, leads, cur, v => app.Set(K.CalendarLead, v), 120));
        card.Add(SUi.SwitchRow(app, K.CalendarTimeLeft, "", Loc.T("Time left in current event"), Loc.T("Show a timed event in progress in the closed island during the hour before it ends. Its title may appear in screen captures.")));
        card.Add(SUi.SwitchRow(app, K.CalendarWeekNumbers, "", Loc.T("Week numbers")));
        MeetingSettings.Add(card, app, _c.RequestRebuild);
        if (svc is { Access: CalendarAccess.Allowed } || svc is { UsesGoogle: true })
        {
            card.AddDivider();
            card.Add(UiKit.Text(Loc.T("Calendars shown"), 12.5, FontWeights.Medium, ThemeKeys.TextPrimary));
            var excluded = app.Get<List<string>>(K.CalendarExcluded) ?? [];
            foreach (var group in svc.Calendars.GroupBy(c => c.Account).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var gh = SUi.Caption(group.Key);
                gh.Margin = new Thickness(0, 4, 0, 0);
                card.Add(gh);
                foreach (var cal in group.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var id = cal.Id;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    var cb = new CheckBox { IsChecked = !excluded.Contains(id), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (_, _) =>
                    {
                        var list = new List<string>(app.Get<List<string>>(K.CalendarExcluded) ?? []);
                        list.Remove(id);
                        if (cb.IsChecked != true)
                        {
                            list.Add(id);
                        }

                        list.Sort(StringComparer.Ordinal);
                        app.Set(K.CalendarExcluded, list);
                        svc.Refresh();
                    };
                    row.Children.Add(cb);
                    row.Children.Add(new Ellipse_ { Width = 8, Height = 8, Fill = new SolidColorBrush(cal.Color), Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(UiKit.Text(cal.Name, 12, FontWeights.Normal, ThemeKeys.TextPrimary));
                    card.Add(row);
                }
            }
        }
    }
}

/// <summary>A static explanation shown in previews instead of a live page (camera, empty agents).</summary>
internal sealed class StillPage : IslandPage
{
    private readonly string _text;
    private readonly string _glyph;

    public StillPage(string text, string glyph)
        : base(IslandCore.Current!, null)
    {
        _text = text;
        _glyph = glyph;
    }

    public override string ModuleId => "still";

    public override FrameworkElement View => D.EmptyView(_glyph, _text);

    public override double Height(double width, double budget) => Math.Min(budget, 150);
}
