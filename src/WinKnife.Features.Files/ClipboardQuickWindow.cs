// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace WinKnife.Features.Files;

/// <summary>
/// The clipboard history quick window ("compact palette"): search, pinned and recent entries as a list (resizable window) or as
/// cards (a shelf along the bottom of the display), an optional 280 px preview sidebar, a pile for multi-paste and a footer.
/// The window is only hidden between uses.
/// </summary>
internal sealed class ClipboardQuickWindow : Window, IQuickActions
{
    private const int EagerRowLimit = 300;
    private static readonly Size ListDefault = new(560, 420);
    private static readonly Size ListMinimum = new(560, 300);
    private static readonly Size PreviewExtra = new(280, 80);

    private readonly IClipboardHost _host;
    private readonly TextBox _search;
    private readonly Grid _searchHost;
    private readonly Border _previewButton = new();
    private readonly Border _contentHost = new();
    private readonly Border _previewHost = new();
    private readonly ColumnDefinition _previewColumn = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition _previewDivider = new() { Width = new GridLength(0) };
    private readonly Border _dividerLine;
    private readonly StackPanel _footerLeft = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _count;
    private readonly DispatcherTimer _hoverTimer;
    private readonly Dictionary<string, EntryVisual> _visuals = [];
    private readonly HashSet<string> _batch = [];
    private PreviewPane? _preview;
    private List<ClipEntry> _filtered = [];
    private ScrollViewer? _scroll;
    private string _query = string.Empty;
    private string? _selectedId;
    private string? _anchorId;
    private string? _hoverId;
    private string? _pendingHoverId;
    private bool _selectionVisible;
    private bool _arrowNavigated;
    private bool _cards;
    private bool _previewOpen;
    private bool _suppressDeactivate;
    private bool _closing;
    private int _renderLimit = EagerRowLimit;
    private System.Windows.Point? _keyboardPointer;
    private HwndSource? _source;

    public ClipboardQuickWindow(IClipboardHost host)
    {
        _host = host;
        Title = Loc.T("Clipboard");
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ResizeMode = ResizeMode.CanResize;
        this.Themed(BackgroundProperty, ThemeKeys.PanelBase);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Input, OnHoverTimer, Dispatcher);
        _hoverTimer.Stop();

        // toolbar ------------------------------------------------------------------------------------------
        _searchHost = Ui.PlaceholderBox(out _search, Strings.Clipboard.Search, 12.5);
        _search.TextChanged += (_, _) =>
        {
            _query = _search.Text;
            _selectedId = null;
            _selectionVisible = false;
            _arrowNavigated = false;
            Rebuild(scrollToTop: true);
        };
        _search.Padding = new Thickness(8, 5, 8, 5);
        var toolbar = new Grid { Margin = new Thickness(12, 9, 12, 9), Background = Brushes.Transparent };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(_searchHost);
        BuildPreviewButton();
        _previewButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_previewButton, 1);
        toolbar.Children.Add(_previewButton);
        var close = Ui.CloseButton(Dismiss);
        close.Margin = new Thickness(8, 0, 0, 0);
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 2);
        toolbar.Children.Add(close);
        toolbar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Grid && e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // released before the move started
                }
            }
        };

        // footer -------------------------------------------------------------------------------------------
        var footer = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_footerLeft);
        var counter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        counter.Children.Add(UiKit.Icon(G.Clipboard, 10.5, ThemeKeys.TextSecondary));
        _count = UiKit.Text("0", 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        _count.Margin = new Thickness(5, 0, 0, 0);
        counter.Children.Add(_count);
        Grid.SetColumn(counter, 1);
        footer.Children.Add(counter);

        // body ---------------------------------------------------------------------------------------------
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(_previewDivider);
        body.ColumnDefinitions.Add(_previewColumn);
        body.Children.Add(_contentHost);
        _dividerLine = new Border { Width = 1 };
        _dividerLine.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
        Grid.SetColumn(_dividerLine, 1);
        body.Children.Add(_dividerLine);
        Grid.SetColumn(_previewHost, 2);
        body.Children.Add(_previewHost);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var line1 = UiKit.Divider();
        DockPanel.SetDock(line1, Dock.Top);
        root.Children.Add(line1);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var line2 = UiKit.Divider();
        DockPanel.SetDock(line2, Dock.Bottom);
        root.Children.Add(line2);
        root.Children.Add(body);

        var frame = new Border { BorderThickness = new Thickness(1), Child = root, CornerRadius = new CornerRadius(8) };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        Content = frame;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        Deactivated += (_, _) =>
        {
            if (!_suppressDeactivate && IsVisible && !_closing)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsActive && !_suppressDeactivate && IsVisible)
                    {
                        Dismiss();
                    }
                }, DispatcherPriority.Background);
            }
        };
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            WinUtil.RoundCorners(this);
            WinUtil.MakeToolWindow(this);
        };
        _host.StateChanged += OnHostStateChanged;
        _host.Store.Changed += OnStoreChanged;
    }

    // ---------------------------------------------------------------------------------------------- IQuickActions

    ClipboardHistoryStore IQuickActions.Store => _host.Store;

    public IReadOnlyList<string> Tokens => ClipSearch.Tokens(_query);

    public bool ShowSourceIcons => _host.Services.App.Get<bool>("clipboardHistoryShowSourceAppIcons");

    public bool CanReorder => string.IsNullOrWhiteSpace(_query);

    public bool CanEditImages => _host.CanEditImages;

    public void Activate(ClipEntry entry)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            TogglePile(entry);
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            ExtendPile(entry);
        }
        else if (_batch.Contains(entry.Id))
        {
            PasteBatch(paste: true);
        }
        else
        {
            PasteOne(entry);
        }
    }

    public void TogglePile(ClipEntry entry)
    {
        _selectedId = entry.Id;
        _anchorId = entry.Id;
        if (!_batch.Add(entry.Id))
        {
            _batch.Remove(entry.Id);
        }

        UpdateStates();
        UpdateFooter();
    }

    public void PasteOne(ClipEntry entry) => _host.PasteEntries([entry], paste: true);

    public void CopyOnly(ClipEntry entry) => _host.PasteEntries([entry], paste: false);

    public void TogglePin(ClipEntry entry)
    {
        if (!_host.Store.TogglePin(entry, _host.Limit, _host.RetentionDays))
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void Move(ClipEntry entry, int direction) => _host.Store.Move(entry, direction);

    public void Delete(ClipEntry entry)
    {
        _batch.Remove(entry.Id);
        var index = _filtered.FindIndex(e => e.Id == entry.Id);
        _host.Store.Remove(entry);
        KeepSelectionNear(index);
    }

    public void EditImage(ClipEntry entry) => _host.EditImage(entry);

    public void Reorder(string movingId, ClipEntry target)
    {
        if (_host.Store.Find(movingId) is { } moving)
        {
            _host.Store.MoveTo(moving, target);
        }
    }

    public void HoverPreview(ClipEntry entry, bool hovering)
    {
        if (!_previewOpen || _preview is null || _preview.IsEditing)
        {
            return;
        }

        if (hovering)
        {
            // A row arriving under a pointer that has not moved since the arrow keys took over is not a hover.
            if (_keyboardPointer is { } p && Mouse.GetPosition(this) == p)
            {
                return;
            }

            _pendingHoverId = entry.Id;
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
        else
        {
            _hoverTimer.Stop();
        }
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_pendingHoverId is null || _preview is null || _preview.IsEditing)
        {
            return;
        }

        _hoverId = _pendingHoverId;
        UpdatePreview();
    }

    // ---------------------------------------------------------------------------------------------- presenting

    public void Present()
    {
        var app = _host.Services.App;
        _cards = app.Get<string>("clipboardHistoryLayout") == "cards";
        _previewOpen = app.Get<bool>("clipboardHistoryQuickPreview");
        _query = string.Empty;
        _search.Text = string.Empty;
        _selectedId = null;
        _anchorId = null;
        _hoverId = null;
        _batch.Clear();
        _selectionVisible = false;
        _arrowNavigated = false;
        _keyboardPointer = null;
        _renderLimit = EagerRowLimit;
        Rebuild(scrollToTop: true);
        ApplyPreviewVisibility(resizeWindow: false);
        PlaceOnPointerDisplay();
        ShowActivated = true;
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        WinUtil.ForceForeground(hwnd);
        Activate();
        _search.Focus();
        Keyboard.Focus(_search);
    }

    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        _hoverTimer.Stop();
        if (_preview?.IsEditing == true)
        {
            _preview.CancelEdit();
        }

        foreach (var visual in _visuals.Values)
        {
            visual.ResetHover();
        }

        Hide();
    }

    public void ForceClose()
    {
        _closing = true;
        _host.StateChanged -= OnHostStateChanged;
        _host.Store.Changed -= OnStoreChanged;
        Close();
    }

    /// <summary>Re-applies the layout choice (list / cards) while the window exists.</summary>
    public void ApplyLayout()
    {
        var cards = _host.Services.App.Get<string>("clipboardHistoryLayout") == "cards";
        if (cards == _cards)
        {
            return;
        }

        _cards = cards;
        if (IsVisible)
        {
            Rebuild(scrollToTop: true);
            PlaceOnPointerDisplay();
        }
    }

    private void PlaceOnPointerDisplay()
    {
        var cursor = Screens.CursorPosition();
        var area = Screens.FromPoint(cursor);
        var scale = WinUtil.PrepareOnMonitorAt(this, (int)cursor.X, (int)cursor.Y);
        var work = area.WorkArea;
        var workWidth = work.Width / scale;
        var workHeight = work.Height / scale;
        if (_cards)
        {
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 0;
            MinHeight = 0;
            Width = Math.Max(400, workWidth - 32);
            Height = Math.Min(318, workHeight - 16);
            var x = (int)(work.Left + 16 * scale);
            var y = (int)(work.Bottom - 8 * scale - Height * scale);
            WinUtil.MoveTo(this, x, y);
            return;
        }

        ResizeMode = ResizeMode.CanResize;
        MinWidth = ListMinimum.Width + (_previewOpen ? PreviewExtra.Width : 0);
        MinHeight = ListMinimum.Height + (_previewOpen ? PreviewExtra.Height : 0);
        var app = _host.Services.App;
        var savedW = app.Get<double>("clipboardHistoryWindowWidth");
        var savedH = app.Get<double>("clipboardHistoryWindowHeight");
        var contentW = savedW > 0 ? savedW : ListDefault.Width;
        var contentH = savedH > 0 ? savedH : ListDefault.Height;
        var width = Math.Max(MinWidth, contentW + (_previewOpen ? PreviewExtra.Width : 0));
        var height = Math.Max(MinHeight, contentH + (_previewOpen ? PreviewExtra.Height : 0));
        width = Math.Min(width, workWidth - 32);
        height = Math.Min(height, workHeight - 32);
        Width = width;
        Height = height;
        var left = (int)(work.Left + (work.Width - width * scale) / 2);
        var top = (int)(work.Top + 54 * scale);
        var maxTop = work.Bottom - height * scale - 16 * scale;
        top = (int)Math.Max(work.Top + 16 * scale, Math.Min(top, maxTop));
        WinUtil.MoveTo(this, left, top);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == WinApi.WM_EXITSIZEMOVE && !_cards && IsVisible)
        {
            // Saved only when a live resize ends, without the preview's extra room.
            var app = _host.Services.App;
            var width = ActualWidth - (_previewOpen ? PreviewExtra.Width : 0);
            var height = ActualHeight - (_previewOpen ? PreviewExtra.Height : 0);
            if (width >= ListMinimum.Width && height >= ListMinimum.Height)
            {
                app.Set("clipboardHistoryWindowWidth", Math.Round(width));
                app.Set("clipboardHistoryWindowHeight", Math.Round(height));
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------------------------------------- building

    private void OnHostStateChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (IsVisible)
        {
            Rebuild();
        }
    });

    private void OnStoreChanged()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var offset = _scroll is null ? 0 : (_cards ? _scroll.HorizontalOffset : _scroll.VerticalOffset);
            Rebuild();
            if (_scroll is not null && offset > 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_cards)
                    {
                        _scroll.ScrollToHorizontalOffset(offset);
                    }
                    else
                    {
                        _scroll.ScrollToVerticalOffset(offset);
                    }
                }, DispatcherPriority.Loaded);
            }
        }, DispatcherPriority.Background);
    }

    private ClipEntry? SelectedEntry => _filtered.FirstOrDefault(e => e.Id == _selectedId) ?? _filtered.FirstOrDefault();

    private bool IsQueryEmpty => string.IsNullOrWhiteSpace(_query);

    private void Rebuild(bool scrollToTop = false)
    {
        var store = _host.Store;
        _filtered = store.Filter(_query);
        var ids = _filtered.Select(e => e.Id).ToHashSet();
        _batch.RemoveWhere(id => !ids.Contains(id));
        _visuals.Clear();

        if (_filtered.Count == 0)
        {
            var message = store.Entries.Count == 0
                ? (_host.HistoryEnabled ? Strings.Clipboard.Empty : Strings.Clipboard.Disabled)
                : Strings.Clipboard.NoResults;
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var glyph = UiKit.Icon(G.Clipboard, 27, ThemeKeys.TextTertiary);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(glyph);
            var label = UiKit.Text(message, 12, FontWeights.Medium, ThemeKeys.TextSecondary);
            label.Margin = new Thickness(0, 10, 0, 0);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(label);
            if (store.Entries.Count == 0 && !_host.HistoryEnabled)
            {
                // Say how to fix it right where the problem is seen (the island page offers the same button).
                var enable = UiKit.Button(Strings.Clipboard.Enable, ButtonKind.Bordered, 12.5, onClick: () =>
                {
                    _host.Services.App.Set("clipboardHistoryEnabled", true);
                    Rebuild();
                });
                enable.HorizontalAlignment = HorizontalAlignment.Center;
                enable.Margin = new Thickness(0, 12, 0, 0);
                empty.Children.Add(enable);
            }

            _contentHost.Child = empty;
            _scroll = null;
        }
        else
        {
            _contentHost.Child = _cards ? BuildCards() : BuildList();
            if (scrollToTop && _scroll is not null)
            {
                _scroll.ScrollToTop();
                _scroll.ScrollToLeftEnd();
            }
        }

        UpdateStates();
        UpdateFooter();
        UpdatePreview();
        UpdatePreviewButton();
    }

    private IEnumerable<(ClipEntry Entry, int? Shortcut)> Numbered(IEnumerable<ClipEntry> entries)
    {
        foreach (var entry in entries)
        {
            var index = _filtered.IndexOf(entry);
            yield return (entry, index is >= 0 and < 9 ? index : null);
        }
    }

    private UIElement BuildList()
    {
        var stack = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
        var shown = _filtered.Take(_renderLimit).ToList();
        if (IsQueryEmpty)
        {
            var pinned = shown.Where(e => e.IsPinned).ToList();
            var recent = shown.Where(e => !e.IsPinned).ToList();
            AddRows(stack, Strings.Clipboard.Pinned, pinned, false);
            AddRows(stack, Strings.Clipboard.Recent, recent, pinned.Count > 0);
        }
        else
        {
            AddRows(stack, null, shown, false);
        }

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack, Focusable = false };
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange > 0 && _scroll.VerticalOffset + _scroll.ViewportHeight >= _scroll.ExtentHeight - 200 && _renderLimit < _filtered.Count)
            {
                _renderLimit += 100;
                var offset = _scroll.VerticalOffset;
                Rebuild();
                _scroll?.ScrollToVerticalOffset(offset);
            }
        };
        return _scroll;
    }

    private void AddRows(StackPanel stack, string? title, List<ClipEntry> entries, bool followsSection)
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (followsSection)
        {
            stack.Children.Add(UiKit.Divider(6, 6));
            ((FrameworkElement)stack.Children[^1]).Margin = new Thickness(6, 5, 6, 5);
        }

        if (title is not null)
        {
            var header = UiKit.Text(title.ToUpperInvariant(), 9.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            header.Margin = new Thickness(8, 6, 8, 6);
            System.Windows.Documents.Typography.SetCapitals(header, FontCapitals.Normal);
            stack.Children.Add(header);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var index = _filtered.IndexOf(entry);
            var visual = new EntryVisual(this, entry, isRow: true, shortcutIndex: index is >= 0 and < 9 ? index : null);
            _visuals[entry.Id] = visual;
            stack.Children.Add(visual);
            if (i < entries.Count - 1)
            {
                var divider = UiKit.Divider(43, 8);
                stack.Children.Add(divider);
            }
        }
    }

    private UIElement BuildCards()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var shown = _filtered.Take(_renderLimit).ToList();
        IEnumerable<ClipEntry> pinned = IsQueryEmpty ? shown.Where(e => e.IsPinned) : [];
        IEnumerable<ClipEntry> recent = IsQueryEmpty ? shown.Where(e => !e.IsPinned) : shown;
        void Add(IEnumerable<ClipEntry> entries)
        {
            foreach (var entry in entries)
            {
                var index = _filtered.IndexOf(entry);
                var card = new EntryVisual(this, entry, isRow: false, shortcutIndex: index is >= 0 and < 9 ? index : null) { Margin = new Thickness(0, 0, 12, 0) };
                _visuals[entry.Id] = card;
                strip.Children.Add(card);
            }
        }

        Add(pinned);
        if (pinned.Any() && recent.Any())
        {
            var divider = new Border { Width = 1, Height = EntryVisual.CardHeight - 24, Margin = new Thickness(0, 0, 12, 0) };
            divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
            strip.Children.Add(divider);
        }

        Add(recent);
        _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = strip, Focusable = false };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            // The wheel scrolls the strip sideways.
            _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        return _scroll;
    }

    private void UpdateStates()
    {
        var selected = SelectedEntry?.Id;
        foreach (var (id, visual) in _visuals)
        {
            visual.SetState(_selectionVisible && id == selected, _batch.Contains(id));
        }
    }

    private void UpdateFooter()
    {
        _footerLeft.Children.Clear();
        var count = _batch.Count;
        if (count > 0)
        {
            var paste = UiKit.Button(string.Format(Strings.Clipboard.PasteSelectedFormat, count), ButtonKind.Prominent, 11, null, () => PasteBatch(paste: true));
            var copy = UiKit.Button(string.Format(Strings.Clipboard.CopySelectedFormat, count), ButtonKind.Bordered, 11, null, () => PasteBatch(paste: false));
            var delete = UiKit.Button(string.Format(Strings.Clipboard.DeleteSelectedFormat, count), ButtonKind.Destructive, 11, null, DeleteBatch);
            var clear = UiKit.Button(Strings.Clipboard.ClearSelection, ButtonKind.Bordered, 11, null, ClearBatch);
            foreach (var b in new[] { paste, copy, delete, clear })
            {
                b.Margin = new Thickness(0, 0, 8, 0);
                _footerLeft.Children.Add(b);
            }
        }
        else
        {
            var clearRecent = UiKit.Button(Strings.Clipboard.ClearRecent, ButtonKind.Bordered, 11, G.Delete, ConfirmClearRecent);
            clearRecent.IsEnabled = _host.Store.Recent.Any();
            _footerLeft.Children.Add(clearRecent);
        }

        _count.Text = _host.Store.Entries.Count.ToString();
    }

    private void ConfirmClearRecent()
    {
        var snapshot = _host.Store.RecentSnapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        _suppressDeactivate = true;
        try
        {
            var answer = MessageBox.Show(
                this,
                Strings.Clipboard.ClearRecentConfirmMessage,
                string.Format(Strings.Clipboard.ClearRecentConfirmFormat, snapshot.Count),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (answer == MessageBoxResult.OK)
            {
                _host.Store.ClearRecent(snapshot);
            }
        }
        finally
        {
            _suppressDeactivate = false;
            Activate();
            _search.Focus();
        }
    }

    // ---------------------------------------------------------------------------------------------- preview

    private void BuildPreviewButton()
    {
        _previewButton.CornerRadius = new CornerRadius(6);
        _previewButton.Padding = new Thickness(9, 4, 9, 4);
        _previewButton.Cursor = Cursors.Hand;
        _previewButton.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        _previewButton.ToolTip = Strings.Clipboard.PreviewLabel;
        _previewButton.VerticalAlignment = VerticalAlignment.Center;
        _previewButton.MouseLeftButtonUp += (_, _) =>
        {
            if (_previewButton.IsEnabled)
            {
                TogglePreview();
            }
        };
        UpdatePreviewButton();
    }

    private void UpdatePreviewButton()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var brush = _previewOpen ? ThemeKeys.Accent : ThemeKeys.TextSecondary;
        row.Children.Add(UiKit.Icon(_previewOpen ? G.Sidebar : G.Eye, 11.5, brush));
        var label = UiKit.Text(Strings.Clipboard.PreviewLabel, 11.5, FontWeights.Medium, brush);
        label.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(label);
        _previewButton.Child = row;
        var enabled = SelectedEntry is not null || _previewOpen;
        _previewButton.IsEnabled = enabled;
        _previewButton.Opacity = enabled ? 1 : 0.45;
    }

    private void TogglePreview()
    {
        _previewOpen = !_previewOpen;
        _host.Services.App.Set("clipboardHistoryQuickPreview", _previewOpen);
        _hoverId = null;
        ApplyPreviewVisibility(resizeWindow: true);
        UpdatePreviewButton();
    }

    private void ApplyPreviewVisibility(bool resizeWindow)
    {
        if (_previewOpen)
        {
            _preview ??= new PreviewPane(_host, this, () =>
            {
                _previewOpen = false;
                _host.Services.App.Set("clipboardHistoryQuickPreview", false);
                ApplyPreviewVisibility(resizeWindow: true);
                UpdatePreviewButton();
            });
            _previewHost.Child = _preview;
            _previewColumn.Width = new GridLength(280);
            _previewDivider.Width = new GridLength(1);
            _dividerLine.Visibility = Visibility.Visible;
        }
        else
        {
            _previewColumn.Width = new GridLength(0);
            _previewDivider.Width = new GridLength(0);
            _dividerLine.Visibility = Visibility.Collapsed;
            _previewHost.Child = null;
        }

        if (!_cards)
        {
            MinWidth = ListMinimum.Width + (_previewOpen ? PreviewExtra.Width : 0);
            MinHeight = ListMinimum.Height + (_previewOpen ? PreviewExtra.Height : 0);
            if (resizeWindow && IsVisible)
            {
                var growth = _previewOpen ? 1 : -1;
                var area = Screens.FromPoint(Screens.CursorPosition());
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var maxWidth = area.WorkArea.Width / scale - 32;
                Width = Math.Max(MinWidth, Math.Min(maxWidth, Width + growth * PreviewExtra.Width));
                Height = Math.Max(MinHeight, Height + growth * PreviewExtra.Height);
            }
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_previewOpen || _preview is null)
        {
            return;
        }

        var entry = _filtered.FirstOrDefault(e => e.Id == _hoverId) ?? SelectedEntry;
        _preview.Show(entry);
    }

    // ---------------------------------------------------------------------------------------------- pile and selection

    private void ExtendPile(ClipEntry entry)
    {
        var target = _filtered.FindIndex(e => e.Id == entry.Id);
        if (target < 0)
        {
            return;
        }

        var anchorId = _anchorId ?? SelectedEntry?.Id;
        var anchor = Math.Max(0, _filtered.FindIndex(e => e.Id == anchorId));
        var (from, to) = anchor <= target ? (anchor, target) : (target, anchor);
        for (var i = from; i <= to; i++)
        {
            _batch.Add(_filtered[i].Id);
        }

        _selectedId = entry.Id;
        _selectionVisible = true;
        UpdateStates();
        UpdateFooter();
    }

    private List<ClipEntry> PrimaryEntries()
    {
        if (_batch.Count > 0)
        {
            return [.. _filtered.Where(e => _batch.Contains(e.Id))];
        }

        return SelectedEntry is { } entry ? [entry] : [];
    }

    private void PasteBatch(bool paste)
    {
        var entries = PrimaryEntries();
        if (entries.Count > 0)
        {
            _host.PasteEntries(entries, paste);
        }
    }

    private void DeleteBatch()
    {
        var entries = PrimaryEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var position = Math.Max(0, _filtered.FindIndex(e => e.Id == SelectedEntry?.Id));
        _batch.Clear();
        _host.Store.Remove(entries.Select(e => e.Id));
        KeepSelectionNear(position);
    }

    private void ClearBatch()
    {
        _batch.Clear();
        UpdateStates();
        UpdateFooter();
    }

    private void KeepSelectionNear(int position)
    {
        // The highlight stays where the removed entry was.
        var remaining = _host.Store.Filter(_query);
        _selectedId = remaining.Count == 0 ? null : remaining[Math.Clamp(position, 0, remaining.Count - 1)].Id;
    }

    private void MoveSelection(int delta)
    {
        if (_filtered.Count == 0)
        {
            _selectedId = null;
            _selectionVisible = false;
            return;
        }

        var current = Math.Max(0, _filtered.FindIndex(e => e.Id == SelectedEntry?.Id));
        var index = _selectionVisible ? Math.Clamp(current + delta, 0, _filtered.Count - 1) : current;
        _selectedId = _filtered[index].Id;
        _selectionVisible = true;
        _arrowNavigated = true;
        _hoverId = null;
        _keyboardPointer = Mouse.GetPosition(this);
        UpdateStates();
        UpdatePreview();
        UpdatePreviewButton();
        if (_visuals.TryGetValue(_selectedId, out var visual))
        {
            visual.BringIntoView();
        }
    }

    // ---------------------------------------------------------------------------------------------- keyboard

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Typing anywhere goes to the search box (unless an editor owns the keys).
        if (Keyboard.FocusedElement is TextBox || e.Text.Length == 0 || char.IsControl(e.Text[0]))
        {
            return;
        }

        _search.Focus();
        _search.Text += e.Text;
        _search.CaretIndex = _search.Text.Length;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // The preview editor (or a text view being composed) owns the keys.
        if (_preview?.IsEditing == true && Keyboard.FocusedElement is TextBox editor && editor != _search)
        {
            return;
        }

        if (key == Key.Escape)
        {
            e.Handled = true;
            if (_batch.Count > 0)
            {
                ClearBatch();
            }
            else
            {
                Dismiss();
            }

            return;
        }

        if (ctrl && !alt && key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            e.Handled = true;
            var digit = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
            if (digit < _filtered.Count)
            {
                PasteOne(_filtered[digit]);
            }

            return;
        }

        if (alt && key == Key.P)
        {
            e.Handled = true;
            if (SelectedEntry is { } entry)
            {
                TogglePin(entry);
            }

            return;
        }

        if ((alt && key == Key.Delete) || (ctrl && key == Key.Delete && _batch.Count > 0))
        {
            e.Handled = true;
            DeleteBatch();
            return;
        }

        if (ctrl && key == Key.A && (_batch.Count > 0 || IsQueryEmpty))
        {
            e.Handled = true;
            foreach (var entry in _filtered)
            {
                _batch.Add(entry.Id);
            }

            UpdateStates();
            UpdateFooter();
            return;
        }

        if (ctrl && key == Key.C && _batch.Count > 0 && Keyboard.FocusedElement is not TextBox { IsReadOnly: true, SelectionLength: > 0 })
        {
            e.Handled = true;
            PasteBatch(paste: false);
            return;
        }

        switch (key)
        {
            case Key.Return or Key.Enter:
                e.Handled = true;
                if (ctrl)
                {
                    if (SelectedEntry is { } toggled)
                    {
                        TogglePile(toggled);
                    }
                }
                else
                {
                    PasteBatch(paste: !shift);
                }

                return;
            case Key.Down when !_cards:
            case Key.Right when _cards && Keyboard.FocusedElement == _search && _search.CaretIndex >= _search.Text.Length:
                e.Handled = true;
                MoveSelection(1);
                return;
            case Key.Up when !_cards:
                e.Handled = true;
                MoveSelection(-1);
                return;
            case Key.Left when _cards && Keyboard.FocusedElement == _search && _search.CaretIndex == 0:
                e.Handled = true;
                MoveSelection(-1);
                return;
            case Key.Space when _arrowNavigated && !ctrl && !alt:
                e.Handled = true;
                if (SelectedEntry is not null || _previewOpen)
                {
                    TogglePreview();
                }

                return;
        }
    }
}
