// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>Context handed to panel sections and hosted utility views.</summary>
internal sealed class PanelContext(IContributionServices services, TrayPanel panel, string tabId) : ContributionServicesBase(services), IPanelContext
{
    public string TabId { get; } = tabId;

    public bool IsEditing => panel.IsEditing;

    public event Action? EditingChanged
    {
        add => panel.EditingChanged += value;
        remove => panel.EditingChanged -= value;
    }

    public event Action? ResetRequested
    {
        add => panel.ResetRequested += value;
        remove => panel.ResetRequested -= value;
    }

    public bool IsActive => panel.IsVisible && panel.SelectedTab == TabId;

    public event Action? ActiveChanged;

    public void RaiseActiveChanged() => ActiveChanged?.Invoke();

    public void RequestRelayout() => panel.Dispatcher.BeginInvoke(panel.InvalidateMeasure);

    public void ClosePanel() => panel.ClosePanel();

    public void InvokeAfterClose(Action action, int delayMs = 150) => panel.InvokeAfterClose(action, delayMs);

    public void SetKeepOpen(bool keepOpen) => panel.KeepOpen = keepOpen;

    public void ShowDetail(string title, string glyph, FrameworkElement view) => panel.ShowDetail(title, glyph, view);

    public void CloseDetail() => panel.CloseDetail();

    public void HostUtility(string utilityId) => panel.HostUtility(utilityId);
}

/// <summary>
/// The tray flyout (spec 01-menu-panel section 3): rounded dark panel anchored above the taskbar with an arrow, brand mark,
/// icon tab bar, titled scroll body, and a Settings / Quit footer. Tab bodies are composed from contributions.
/// </summary>
internal sealed class TrayPanel : Window
{
    private const double PanelWidth = 332;
    private const double ShadowMargin = 16;
    private const double ArrowHeight = 12;
    private const double Chrome = 180;

    private static string? s_selectedTab;

    private readonly ShellServices _s;
    private readonly Point _anchor;
    private readonly TaskbarEdge _edge;
    private readonly Border _panel = new();
    private readonly Path _arrow = new();
    private readonly StackPanel _main = new();
    private readonly Grid _frame = new();
    private readonly Grid _tabBarHost = new();
    private readonly ScrollViewer _scroll = new();
    private readonly ContentControl _bodyHost = new();
    private readonly Dictionary<string, PanelContext> _contexts = new(StringComparer.Ordinal);
    private string? _hostedUtility;
    private string? _hostedSettings;
    private (string Title, string Glyph, FrameworkElement View)? _detail;
    private bool _closing;
    private bool _placed;
    private bool _keepOpen;
    private double _arrowCenterX = PanelWidth / 2;
    private bool _editing;

    public TrayPanel(ShellServices services, Point anchorPhysical)
    {
        _s = services;
        _anchor = anchorPhysical;
        _edge = TaskbarInfo.EdgeFor(anchorPhysical);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + ShadowMargin * 2;
        FontFamily = UiKit.TextFont;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Opacity = 0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;

        BuildFrame();
        Rebuild();
        _s.Runtime.Changed += OnStateChanged;
        _s.Contributions.Changed += OnStateChanged;
        _s.App.Changed += OnSettingChanged;
        ThemeManager.ThemeChanged += OnTheme;
        Closed += (_, _) =>
        {
            _s.Runtime.Changed -= OnStateChanged;
            _s.Contributions.Changed -= OnStateChanged;
            _s.App.Changed -= OnSettingChanged;
            ThemeManager.ThemeChanged -= OnTheme;
            LastClosedTick = Environment.TickCount64;
        };
        Loaded += (_, _) => Place();
        SizeChanged += (_, _) => Place();
        Deactivated += OnDeactivated;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ClosePanel();
                e.Handled = true;
            }
        };
    }

    /// <summary>Tick count of the last close, so the tray can toggle instead of reopening.</summary>
    public static long LastClosedTick { get; private set; }

    public string? SelectedTab => s_selectedTab;

    public bool KeepOpen
    {
        get => _keepOpen;
        set => _keepOpen = value;
    }

    public bool IsEditing => _editing;

    public event Action? EditingChanged;

    public event Action? ResetRequested;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // The window clips itself; no DWM rounding / backdrop (we draw the rounded panel).
        var style = NativeMethods.GetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE).ToInt64();
        style |= NativeConstants.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE, (nint)style);
    }

    // ------------------------------------------------------------------------------------------------ frame

    private void BuildFrame()
    {
        _panel.CornerRadius = new CornerRadius(18);
        _panel.BorderThickness = new Thickness(1);
        _panel.Padding = new Thickness(12);
        _panel.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        _panel.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        _panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.45 };
        _panel.Child = _main;

        _arrow.Height = ArrowHeight;
        _arrow.Width = 26;
        _arrow.HorizontalAlignment = HorizontalAlignment.Left;
        _arrow.StrokeThickness = 1;
        _arrow.SetResourceReference(Shape.FillProperty, ThemeKeys.PanelBase);
        _arrow.SetResourceReference(Shape.StrokeProperty, ThemeKeys.CardBorder);

        _frame.Margin = new Thickness(ShadowMargin);
        _frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var onTop = _edge == TaskbarEdge.Top;
        Grid.SetRow(_arrow, onTop ? 0 : 2);
        Grid.SetRow(_panel, 1);
        _frame.Children.Add(_panel);
        _frame.Children.Add(_arrow);
        _frame.RenderTransform = new TranslateTransform(0, onTop ? -8 : 8);
        Content = _frame;
        UpdateArrow();
    }

    private void UpdateArrow()
    {
        var onTop = _edge == TaskbarEdge.Top;
        var geometry = onTop
            ? Geometry.Parse("M 0,12 L 10,3 Q 13,0 16,3 L 26,12")
            : Geometry.Parse("M 0,0 L 10,9 Q 13,12 16,9 L 26,0");
        _arrow.Data = geometry;
        _arrow.Margin = new Thickness(Math.Clamp(_arrowCenterX - 13, 22, PanelWidth - 22 - 26), onTop ? 0 : -1, 0, onTop ? -1 : 0);
        // Fill covers the panel border where the arrow meets it.
        _arrow.StrokeThickness = 1;
    }

    private void Rebuild()
    {
        _main.Children.Clear();
        _main.Children.Add(Header());
        var tabs = VisibleTabs();
        if (s_selectedTab is null || tabs.All(t => t.Id != s_selectedTab))
        {
            s_selectedTab = tabs.FirstOrDefault()?.Id;
        }

        if (_detail is { } detail)
        {
            _main.Children.Add(DetailHeader(detail.Title, detail.Glyph));
            _main.Children.Add(Spaced(WrapScroll(detail.View), 12));
        }
        else
        {
            if (tabs.Count > 0)
            {
                _main.Children.Add(Spaced(TabBar(tabs), 12));
                _main.Children.Add(Spaced(WrapScroll(BuildBody(s_selectedTab!)), 12));
            }
            else
            {
                _main.Children.Add(Spaced(EmptyState(), 12));
            }
        }

        _main.Children.Add(Spaced(Footer(), 12));
    }

    private UIElement Header()
    {
        var host = new Border { Height = 28, Margin = new Thickness(0, 4, 0, 0) };
        var mark = BrandMark.Create(48, ThemeKeys.TextPrimary);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        host.Child = mark;
        return host;
    }

    private IReadOnlyList<PanelTabInfo> VisibleTabs()
    {
        var list = new List<PanelTabInfo>();
        foreach (var tab in PanelLayout.Ordered(_s.App))
        {
            if (!PanelLayout.IsShown(_s.App, tab) || !TabHasContent(tab))
            {
                continue;
            }

            list.Add(tab);
        }

        return list;
    }

    private bool TabHasContent(PanelTabInfo tab)
    {
        // A tab needs its gating features installed (brightness: also the control enabled) and at least one contribution.
        if (tab.Id == PanelTabs.Brightness && !_s.App.Get<bool>(AppKeys.BrightnessControlEnabled))
        {
            return false;
        }

        if (!_s.Runtime.AnyAvailable(tab.GateFeatures))
        {
            return false;
        }

        // No empty tab on a PC without Bluetooth: the service says so once Windows has answered.
        if (tab.Id == PanelTabs.Bluetooth && _s.Host.Services.Get<IBluetoothState>() is { HasAdapter: false })
        {
            return false;
        }

        return _s.Contributions.PanelSections(tab.Id).Count > 0
            || (tab.Id == PanelTabs.Utilities && _s.Contributions.PanelUtilities().Any(u => !u.HostedOnly));
    }

    private UIElement TabBar(IReadOnlyList<PanelTabInfo> tabs)
    {
        var bar = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), BorderThickness = new Thickness(0.7) };
        bar.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        bar.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        var grid = new Grid();
        for (var i = 0; i < tabs.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tab = tabs[i];
            var selected = tab.Id == s_selectedTab;
            var cell = new Border
            {
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tab.Title,
                Child = UiKit.Icon(tab.Glyph, 13.5, selected ? ThemeKeys.Accent : ThemeKeys.TabIdle, FontWeights.SemiBold),
            };
            System.Windows.Automation.AutomationProperties.SetName(cell, tab.Title);
            ToolTipService.SetInitialShowDelay(cell, 250);
            ToolTipService.SetPlacement(cell, System.Windows.Controls.Primitives.PlacementMode.Top);
            if (selected)
            {
                cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TabSelectedFill);
            }
            else
            {
                cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
                cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            }

            cell.MouseLeftButtonUp += (_, _) => SelectTab(tab.Id);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        bar.Child = grid;
        return bar;
    }

    private void SelectTab(string id)
    {
        if (id == s_selectedTab)
        {
            return;
        }

        var old = s_selectedTab;
        s_selectedTab = id;
        _hostedUtility = null;
        _hostedSettings = null;
        _keepOpen = false;
        _detail = null;
        SetEditing(false);
        if (old is not null && _contexts.TryGetValue(old, out var oldContext))
        {
            oldContext.RaiseActiveChanged();
        }

        Rebuild();
        if (_contexts.TryGetValue(id, out var ctx))
        {
            ctx.RaiseActiveChanged();
        }
    }

    public void SelectTabExternally(string tabId)
    {
        var tab = VisibleTabs().FirstOrDefault(t => t.Id == tabId);
        if (tab is not null)
        {
            SelectTab(tab.Id);
        }
    }

    /// <summary>The whole panel has nothing to show (no tab has content): a friendly "Add features" card.</summary>
    private UIElement EmptyState() => AddFeaturesCard(empty: true);

    /// <summary>Panels with this many features or fewer installed get the "Add features" card at the end of every tab.</summary>
    private const int FewFeatures = 14;

    private bool ShowAddFeaturesNudge => _s.Runtime.AvailableCount <= FewFeatures;

    /// <summary>
    /// "Add features" card (panel never looks empty): explains that the panel is built from the installed features and opens the
    /// Features hub. <paramref name="empty"/> is the big variant used when a tab or the whole panel has nothing to show.
    /// </summary>
    private UIElement AddFeaturesCard(bool empty = false, string? message = null)
    {
        var installed = _s.Runtime.AvailableCount;
        var total = _s.Runtime.InstallableCount;
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = UiKit.IconTile(Glyphs.Add, empty ? 34 : 28, empty ? 15 : 13, 9);
        tile.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(tile);
        var text = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(Loc.T("Add features"), empty ? 14 : 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var caption = message ?? (empty
            ? Loc.T("Nothing to show yet. This panel is built from the features you install: monitors, a volume mixer, window tools and more.")
            : Loc.F("{0} of {1} features installed. The panel grows with every feature you add.", installed, total));
        text.Children.Add(UiKit.Text(caption, empty ? 12 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true).Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        stack.Children.Add(head);
        var button = UiKit.Button(Loc.T("Open the Features hub"), ButtonKind.Prominent, 12, Glyphs.Add, () =>
        {
            ClosePanel();
            _s.Shell.OpenFeatures();
        });
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(button);
        var card = new PanelCard(stack, 12) { Cursor = Cursors.Arrow };
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.T("Add features"));
        return card;
    }

    // ------------------------------------------------------------------------------------------------ body

    private FrameworkElement WrapScroll(FrameworkElement content)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
            Focusable = false,
        };
        return scroll;
    }

    private PanelContext ContextFor(string tabId)
    {
        if (!_contexts.TryGetValue(tabId, out var ctx))
        {
            _contexts[tabId] = ctx = new PanelContext(_s.Host, this, tabId);
        }

        return ctx;
    }

    private FrameworkElement BuildBody(string tabId)
    {
        var tab = PanelTabs.Get(tabId)!;
        var ctx = ContextFor(tabId);
        var stack = new StackPanel();
        var sections = _s.Contributions.PanelSections(tabId);
        var supportsEditing = tab.SupportsEditing && (sections.Any(x => x.SupportsEditing) || tabId == PanelTabs.Utilities);

        if (_hostedUtility is { } hosted)
        {
            var utility = _s.Contributions.PanelUtilities().FirstOrDefault(u => u.Id == hosted);
            if (utility is not null)
            {
                return HostedBody(utility, ctx);
            }

            _hostedUtility = null;
        }

        stack.Children.Add(SectionTitleRowWithEdit(tab, supportsEditing));

        if (tabId == PanelTabs.Utilities)
        {
            stack.Children.Add(Spaced(UtilitiesBody(ctx), 10));
        }

        string[] categoryOrder = [PanelTabs.CategoryWindows, PanelTabs.CategoryInput, PanelTabs.CategoryFiles];
        var groups = tabId == PanelTabs.Controls
            ? sections.GroupBy(x => x.Category ?? string.Empty).OrderBy(g => Array.IndexOf(categoryOrder, g.Key) is var at && at >= 0 ? at : int.MaxValue).ToList()
            : [];
        if (tabId == PanelTabs.Controls)
        {
            foreach (var group in groups)
            {
                stack.Children.Add(Spaced(ControlsCategory(group.Key, group.ToList(), ctx), 10));
            }
        }
        else
        {
            foreach (var section in sections)
            {
                FrameworkElement view;
                try
                {
                    view = section.CreateView(ctx);
                }
                catch (Exception ex)
                {
                    _s.Log.Error($"Panel section {section.GetType().Name} failed", ex);
                    view = new PanelCard(UiKit.Text(ex.Message, 11, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
                }

                stack.Children.Add(Spaced(view, 10));
            }
        }

        // A tab whose features produced nothing, or a panel with only a few features installed, points at the Features hub.
        if (!_editing)
        {
            var hasContent = stack.Children.Count > 1;
            if (!hasContent)
            {
                stack.Children.Add(Spaced(AddFeaturesCard(empty: true, message: Loc.F("Nothing to show in {0} yet. Install a feature that belongs here, or hide this tab in Settings.", tab.Title)), 10));
            }
            else if (ShowAddFeaturesNudge)
            {
                stack.Children.Add(Spaced(AddFeaturesCard(), 12));
            }
        }

        return stack;
    }

    private UIElement SectionTitleRowWithEdit(PanelTabInfo tab, bool supportsEditing)
    {
        UIElement? trailing = null;
        if (supportsEditing)
        {
            if (_editing)
            {
                var bar = new StackPanel { Orientation = Orientation.Horizontal };
                var reset = new Button { Content = UiKit.Icon(Glyphs.Reset, 10.5, ThemeKeys.TextSecondary, FontWeights.SemiBold), Width = 22, Height = 22, ToolTip = Loc.T("Default"), Margin = new Thickness(0, 0, 6, 0) };
                reset.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
                reset.Click += (_, _) =>
                {
                    ResetRequested?.Invoke();
                    ResetUtilityLayout();
                    Rebuild();
                };
                bar.Children.Add(reset);
                var ok = new Border { Height = 24, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0), Cursor = Cursors.Hand };
                ok.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
                var okRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                okRow.Children.Add(UiKit.Icon(Glyphs.Check, 10, ThemeKeys.OnAccent, FontWeights.Bold));
                var okText = UiKit.Text(Loc.T("OK"), 10.5, FontWeights.Bold, ThemeKeys.OnAccent);
                okText.Margin = new Thickness(4, 0, 0, 0);
                okRow.Children.Add(okText);
                ok.Child = okRow;
                ok.MouseLeftButtonUp += (_, _) =>
                {
                    SetEditing(false);
                    Rebuild();
                };
                bar.Children.Add(ok);
                trailing = bar;
            }
            else
            {
                var edit = new Button { Content = UiKit.Icon(Glyphs.Edit, 11, ThemeKeys.TextSecondary, FontWeights.SemiBold), Width = 22, Height = 18, ToolTip = Loc.T("Edit") };
                edit.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
                edit.Padding = new Thickness(0);
                edit.Click += (_, _) =>
                {
                    SetEditing(true);
                    Rebuild();
                };
                trailing = edit;
            }
        }

        return new SectionTitleRow(tab.Title, trailing);
    }

    private void SetEditing(bool on)
    {
        if (_editing == on)
        {
            return;
        }

        _editing = on;
        EditingChanged?.Invoke();
    }

    // ---- Utilities tab -----------------------------------------------------------------------------------

    /// <summary>Default order of the Utilities rows (spec 01 5.8); a saved <c>panelUtilityOrder</c> goes first.</summary>
    private static readonly string[] DefaultUtilityOrder =
    [
        "screenshot", "quickLauncher", "appUpdates", "cleaner", "homebrew", "media", "clipboard", "windowLayout", "uninstaller",
        "cleanURL", "cleaning", "screenOCR", "colorPicker", "cameraPreview", "scratchpad", "commandBar", "screenRecorder", "portManager",
    ];

    private static readonly Dictionary<string, bool> HostedCache = new(StringComparer.Ordinal);

    private void ResetUtilityLayout()
    {
        _s.App.Reset(PanelKeys.UtilityOrder);
        foreach (var u in _s.Contributions.PanelUtilities())
        {
            _s.App.Reset(PanelKeys.Utility(u.Id));
        }
    }

    /// <summary>True when the utility opens a hosted view (probed once: creating the view just to test for null would build every hosted panel on each rebuild).</summary>
    private bool IsHosted(IPanelUtility u, PanelContext ctx)
    {
        if (!HostedCache.TryGetValue(u.Id, out var hosted))
        {
            try
            {
                hosted = u.CreateHostedView(ctx) is not null;
            }
            catch (Exception ex)
            {
                _s.Log.Error($"Hosted utility {u.Id} probe failed", ex);
                hosted = false;
            }

            HostedCache[u.Id] = hosted;
        }

        return hosted;
    }

    private FrameworkElement UtilitiesBody(PanelContext ctx)
    {
        var all = _s.Contributions.PanelUtilities().Where(u => !u.HostedOnly).ToList();
        var order = (_s.App.Get<string>(PanelKeys.UtilityOrder) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var id in DefaultUtilityOrder.Where(id => !order.Contains(id)))
        {
            order.Add(id);
        }

        all = [.. all.OrderBy(u => order.IndexOf(u.Id) is var i && i >= 0 ? i : int.MaxValue)];
        var group = new PanelRowGroup();
        var host = new StackPanel();
        foreach (var u in all)
        {
            var shown = _s.App.Get<bool>(PanelKeys.Utility(u.Id), true);
            if (!shown && !_editing)
            {
                continue;
            }

            var row = UtilityRow(u, ctx, shown);
            host.Children.Add(row);
        }

        if (host.Children.Count == 0)
        {
            // Everything is hidden (or nothing installed): say so instead of leaving a blank tab.
            return new PanelCard(UiKit.Text(Loc.T("Every utility is hidden. Click the pencil to show some again, or add more features."), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 12);
        }

        var card = new PanelCard(host, 0);
        _ = group;
        return card;
    }

    private FrameworkElement UtilityRow(IPanelUtility u, PanelContext ctx, bool shown)
    {
        var wrapper = new StackPanel { Tag = u.Id };
        var hosted = IsHosted(u, ctx);
        var row = new PanelActionRow(u.Glyph, u.Title, u.Caption, () =>
        {
            if (_editing)
            {
                return;
            }

            if (hosted)
            {
                HostUtility(u.Id);
            }
            else
            {
                var captured = u;
                InvokeAfterClose(() => captured.Invoke(ctx), u.InvokeDelayMs, close: true);
            }
        }, _editing ? null : u.ShortcutText, u.CaptionStaysVisible || _editing, u.NeedsAttention, chevron: !_editing);
        if (_editing)
        {
            var layout = row.Layout;
            var eye = new Button { Content = UiKit.Icon(shown ? Glyphs.EyeOff : Glyphs.Eye, 11, shown ? ThemeKeys.TextSecondary : ThemeKeys.Accent, FontWeights.Bold), Width = 24, Height = 22, ToolTip = shown ? Loc.T("Hide from panel") : Loc.T("Show in panel") };
            eye.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
            eye.Click += (_, e) =>
            {
                _s.App.Set(PanelKeys.Utility(u.Id), !shown);
                Rebuild();
            };
            Grid.SetColumn(eye, 4);
            layout.Children.Add(eye);
            row.Opacity = shown ? 1 : 0.55;
        }

        wrapper.Children.Add(row);
        if (u.AccessoryTitle is { } accessory && !_editing)
        {
            var mini = PanelButtons.Mini(accessory, u.AccessoryGlyph, () => u.OnAccessory(ctx));
            mini.HorizontalAlignment = HorizontalAlignment.Left;
            mini.Margin = new Thickness(41, -2, 0, 8);
            wrapper.Children.Add(mini);
        }

        return wrapper;
    }

    private FrameworkElement HostedBody(IPanelUtility u, PanelContext ctx)
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(UiKit.Icon(u.Glyph, 12.5, ThemeKeys.TextPrimary, FontWeights.SemiBold));
        var title = UiKit.Text(u.Title, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        title.Margin = new Thickness(7, 0, 0, 0);
        label.Children.Add(title);
        header.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (u.ShowsSettingsGear && u.SettingsDestination is { } dest)
        {
            buttons.Children.Add(IconButton(Glyphs.Settings, Loc.T("Settings…"), () => _s.Shell.OpenSettings(dest)));
        }

        buttons.Children.Add(IconButton(Glyphs.CloseCircle, Loc.T("Cancel"), () =>
        {
            _hostedUtility = null;
            _hostedSettings = null;
            _keepOpen = false;
            Rebuild();
        }));
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        stack.Children.Add(header);
        _hostedSettings = u.SettingsDestination;
        _keepOpen = u.HostedKeepsPanelOpen;
        FrameworkElement view;
        try
        {
            view = u.CreateHostedView(ctx) ?? new Border();
        }
        catch (Exception ex)
        {
            _s.Log.Error($"Hosted utility {u.Id} failed", ex);
            view = new PanelCard(UiKit.Text(ex.Message, 11, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
        }

        stack.Children.Add(Spaced(view, 10));
        return stack;
    }

    private static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button { Content = UiKit.Icon(glyph, 13, ThemeKeys.TextSecondary), Width = 22, Height = 22, ToolTip = tooltip, Padding = new Thickness(0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        b.Click += (_, _) => onClick();
        return b;
    }

    public void HostUtility(string utilityId)
    {
        _hostedUtility = utilityId;
        SetEditing(false);
        Rebuild();
    }

    // ---- Controls tab ------------------------------------------------------------------------------------

    private FrameworkElement ControlsCategory(string category, List<IPanelSection> sections, PanelContext ctx)
    {
        var title = category switch
        {
            PanelTabs.CategoryWindows => Loc.T("Windows"),
            PanelTabs.CategoryInput => Loc.T("Mouse and keyboard"),
            PanelTabs.CategoryFiles => Loc.T("Files"),
            _ => string.Empty,
        };
        var key = category switch
        {
            PanelTabs.CategoryWindows => "panelControlWindowsExpanded",
            PanelTabs.CategoryInput => "panelControlInputExpanded",
            PanelTabs.CategoryFiles => "panelControlFilesExpanded",
            _ => string.Empty,
        };
        var stack = new StackPanel();
        var expanded = key.Length == 0 || _editing || _s.App.Get<bool>(key);
        if (title.Length > 0)
        {
            var counts = sections.Select(x => x.GetCategoryCounts(ctx)).ToList();
            var enabled = counts.Sum(x => x.Enabled);
            var total = counts.Sum(x => x.Total);
            var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 8.5, ThemeKeys.TextSecondary, FontWeights.Bold);
            chevron.RenderTransform = new RotateTransform(expanded ? 90 : 0, 4, 6);
            chevron.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(chevron);
            var text = UiKit.Text(title.ToUpperInvariant(), 10.5, FontWeights.Bold, ThemeKeys.TextSecondary);
            Grid.SetColumn(text, 1);
            head.Children.Add(text);
            var count = UiKit.Text($"{enabled}/{total}", 10, FontWeights.SemiBold, enabled > 0 ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
            Grid.SetColumn(count, 2);
            head.Children.Add(count);
            if (!_editing)
            {
                head.MouseLeftButtonUp += (_, _) =>
                {
                    _s.App.Set(key, !expanded);
                    Rebuild();
                };
            }

            stack.Children.Add(head);
        }

        if (expanded)
        {
            foreach (var section in sections)
            {
                FrameworkElement view;
                try
                {
                    view = section.CreateView(ctx);
                }
                catch (Exception ex)
                {
                    _s.Log.Error($"Panel section {section.GetType().Name} failed", ex);
                    view = new PanelCard(UiKit.Text(ex.Message, 11, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
                }

                stack.Children.Add(Spaced(view, 8));
            }
        }

        return stack;
    }

    // ------------------------------------------------------------------------------------------------ detail / footer

    public void ShowDetail(string title, string glyph, FrameworkElement view)
    {
        _detail = (title, glyph, view);
        Rebuild();
    }

    public void CloseDetail()
    {
        _detail = null;
        Rebuild();
    }

    private UIElement DetailHeader(string title, string glyph)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var back = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0.7), Cursor = Cursors.Hand, Child = UiKit.Icon(Glyphs.ChevronLeft, 11, ThemeKeys.TextPrimary, FontWeights.SemiBold) };
        back.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        back.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        back.MouseLeftButtonUp += (_, _) => CloseDetail();
        row.Children.Add(back);
        var icon = UiKit.Icon(glyph, 12.5, ThemeKeys.TextPrimary, FontWeights.SemiBold);
        icon.Margin = new Thickness(10, 0, 6, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(title, 12.5, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        return row;
    }

    private UIElement Footer()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var settings = FooterButton(Loc.T("Settings"), Glyphs.Settings, () => _s.Shell.OpenSettings(_hostedSettings ?? SettingsPages.Last));
        var quit = FooterButton(Loc.T("Quit"), Glyphs.Power, () => _s.Shell.Quit());
        Grid.SetColumn(quit, 2);
        grid.Children.Add(settings);
        grid.Children.Add(quit);
        return grid;
    }

    private static Border FooterButton(string text, string glyph, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(UiKit.Icon(glyph, 12, ThemeKeys.TextSecondary));
        var label = UiKit.Text(text, 11.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        label.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(label);
        var b = new Border { MinHeight = 28, Height = 30, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0.8), Cursor = Cursors.Hand, Child = row };
        b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        b.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        b.MouseLeave += (_, _) => b.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    private static T Spaced<T>(T element, double top) where T : UIElement
    {
        if (element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + top, fe.Margin.Right, fe.Margin.Bottom);
        }

        return element;
    }

    // ------------------------------------------------------------------------------------------------ behaviour

    private void OnStateChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void OnSettingChanged(string key)
    {
        if (key.StartsWith("panel", StringComparison.Ordinal) || key.StartsWith("monitorShow", StringComparison.Ordinal) || key == AppKeys.BrightnessControlEnabled)
        {
            Dispatcher.BeginInvoke(Rebuild);
        }
    }

    private void OnTheme(bool light) => Dispatcher.BeginInvoke(Rebuild);

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closing || _keepOpen)
        {
            return;
        }

        // Working in our own Settings window must not close the panel.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_closing || _keepOpen || IsActive)
            {
                return;
            }

            if (Application.Current.Windows.OfType<Window>().Any(w => w != this && w.IsActive))
            {
                return;
            }

            ClosePanel();
        });
    }

    public void ClosePanel()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    public void InvokeAfterClose(Action action, int delayMs = 150, bool close = true)
    {
        if (close)
        {
            ClosePanel();
        }

        var dispatcher = Application.Current.Dispatcher;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(delayMs + 110) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _s.Log.Error("Panel action failed", ex);
            }
        };
        timer.Start();
    }

    // ------------------------------------------------------------------------------------------------ placement

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || ActualHeight < 10)
        {
            return;
        }

        // First move (position only) so the window lands on the anchor's monitor and WPF applies that monitor's DPI,
        // then size and position again with the final scale.
        var area = Screens.FromPoint(_anchor).WorkArea;
        if (!_placed)
        {
            NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, (int)area.Left, (int)area.Top, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
            UpdateLayout();
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var maxPanel = Math.Max(360, area.Height / dpi - 28);
        MaxHeight = maxPanel + ShadowMargin * 2 + ArrowHeight;
        var widthPx = (int)Math.Round((PanelWidth + ShadowMargin * 2) * dpi);
        var heightPx = (int)Math.Round(ActualHeight * dpi);
        const int gap = 6;
        int x;
        int y;
        switch (_edge)
        {
            case TaskbarEdge.Left:
            case TaskbarEdge.Right:
                x = _edge == TaskbarEdge.Left ? (int)area.Left + gap : (int)area.Right - widthPx - gap;
                y = Math.Clamp((int)_anchor.Y - heightPx / 2, (int)area.Top + gap, Math.Max((int)area.Top + gap, (int)area.Bottom - heightPx - gap));
                break;
            case TaskbarEdge.Top:
                x = Math.Clamp((int)_anchor.X - widthPx / 2, (int)area.Left + gap, Math.Max((int)area.Left + gap, (int)area.Right - widthPx - gap));
                y = (int)area.Top + gap;
                break;
            default:
                x = Math.Clamp((int)_anchor.X - widthPx / 2, (int)area.Left + gap, Math.Max((int)area.Left + gap, (int)area.Right - widthPx - gap));
                y = (int)area.Bottom - heightPx - gap;
                break;
        }

        _arrowCenterX = ((_anchor.X - x) / dpi) - ShadowMargin;
        UpdateArrow();
        NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, x, y, widthPx, heightPx, NativeConstants.SWP_NOACTIVATE);
        if (!_placed)
        {
            _placed = true;
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
            _frame.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_edge == TaskbarEdge.Top ? -8 : 8, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Activate();
        }
    }
}

/// <summary>Where the taskbar sits relative to a point.</summary>
internal enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Taskbar geometry helpers.</summary>
internal static class TaskbarInfo
{
    public static TaskbarEdge EdgeFor(Point physicalPoint)
    {
        var m = Screens.FromPoint(physicalPoint);
        var b = m.Bounds;
        var w = m.WorkArea;
        if (w.Bottom < b.Bottom - 1)
        {
            return TaskbarEdge.Bottom;
        }

        if (w.Top > b.Top + 1)
        {
            return TaskbarEdge.Top;
        }

        if (w.Left > b.Left + 1)
        {
            return TaskbarEdge.Left;
        }

        return w.Right < b.Right - 1 ? TaskbarEdge.Right : TaskbarEdge.Bottom;
    }
}
