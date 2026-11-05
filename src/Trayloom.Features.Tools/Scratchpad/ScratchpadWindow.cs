// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>Strings of the scratchpad (Vorssaint <c>ScratchpadFeatureStrings</c>, enUS).</summary>
internal static class PadStrings
{
    public static  string Title => Loc.T("Scratchpad");
    public static  string PanelCaption => Loc.T("Quick notes in separate tabs");
    public static  string OpenButton => Loc.T("Open scratchpad");
    public static  string Placeholder => Loc.T("Type anything. It saves by itself.");
    public static  string CopyAll => Loc.T("Copy all");
    public static  string Copied => Loc.T("Copied");
    public static  string ExportAction => Loc.T("Save as file");
    public static  string ExportFailed => Loc.T("The file could not be saved");
    public static  string LoadFailed => Loc.T("Your notes could not be opened. They were left unchanged.");
    public static  string SaveFailed => Loc.T("Your notes could not be saved. Copy them elsewhere before quitting.");
    public static  string ClearAction => Loc.T("Clear");
    public static  string RetentionTitle => Loc.T("Clear on its own");
    public static  string RetentionCaption => Loc.T("The pad empties itself once the text goes that long without edits.");
    public static  string CloseOnClickOutside => Loc.T("Close when I click outside");
    public static  string KeepOpen => Loc.T("Keep open");
    public static  string BackgroundOpacity => Loc.T("Pad background");
    public static  string Translucent => Loc.T("Translucent");
    public static  string Opaque => Loc.T("Opaque");
    public static  string NewPad => Loc.T("New scratchpad");
    public static  string PadActions => Loc.T("Scratchpad actions");
    public static  string RenamePad => Loc.T("Rename scratchpad");
    public static  string ClosePad => Loc.T("Close scratchpad");
    public static  string SaveName => Loc.T("Save");
    public static  string Cancel => Loc.T("Cancel");
    public static  string Close => Loc.T("Close");
    public static  string PreviewFormatting => Loc.T("Show formatting");
    public static  string EditText => Loc.T("Edit text");
    public static  string FormatMarks => Loc.T("Formatting");
    public static  string TextSize => Loc.T("Text size");

    public static string DeleteMessage(string name) => Loc.F("Delete “{0}” and everything in it?", name);

    public static string PadLimit(int count) => Loc.F("You can keep up to {0} scratchpads", count);

    public static string[] MarkLabels => _loc_MarkLabels.Value;

    private static readonly LocValue<string[]> _loc_MarkLabels = new(() => [Loc.T("Bold"), Loc.T("Italic"), Loc.T("Strikethrough"), Loc.T("Heading"), Loc.T("Bulleted list"), Loc.T("Numbered list"), Loc.T("Quote"), Loc.T("Code"), Loc.T("Link")]);
}

/// <summary>A flat icon button (26x26, hover fill, never takes focus).</summary>
internal sealed class PadButton : Border
{
    private readonly TextBlock _glyph;
    private string _tintKey = ThemeKeys.TextSecondary;
    private bool _enabled = true;

    public PadButton(string glyph, string tooltip, Action click, double size = 26, double iconSize = 13, double radius = 6)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(radius);
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Focusable = false;
        ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(this, tooltip);
        _glyph = new TextBlock
        {
            Text = glyph,
            FontFamily = UiKit.IconFont,
            FontSize = iconSize,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, _tintKey);
        Child = _glyph;
        MouseEnter += (_, _) =>
        {
            if (_enabled)
            {
                this.Themed(BackgroundProperty, ThemeKeys.RowHover);
            }
        };
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) =>
        {
            if (_enabled)
            {
                e.Handled = true;
                click();
            }
        };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_enabled)
            {
                e.Handled = true;
            }
        };
    }

    public string Glyph
    {
        get => _glyph.Text;
        set => _glyph.Text = value;
    }

    public void Tint(string key)
    {
        _tintKey = key;
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, key);
    }

    public new bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Opacity = value ? 1 : 0.5;
            Cursor = value ? Cursors.Hand : Cursors.Arrow;
        }
    }

    public TextBlock GlyphBlock => _glyph;
}

/// <summary>Modal helpers (rename / close confirmation) shown over the pad.</summary>
internal static class PadDialogs
{
    public static string? Prompt(Window owner, string title, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 12, 0, 16), MinWidth = 260 };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        string? result = null;
        var window = Create(owner, title, 320);
        var save = UiKit.Button(PadStrings.SaveName, ButtonKind.Prominent, 13, onClick: () =>
        {
            if (box.Text.Trim().Length > 0)
            {
                result = box.Text;
                window.DialogResult = true;
            }
        });
        var cancel = UiKit.Button(PadStrings.Cancel, ButtonKind.Bordered, 13, onClick: () => window.DialogResult = false);
        window.Content = Layout(title, box, cancel, save);
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                if (box.Text.Trim().Length > 0)
                {
                    result = box.Text;
                    window.DialogResult = true;
                }
            }
        };
        return window.ShowDialog() == true ? result : null;
    }

    public static bool Confirm(Window owner, string title, string message, string destructive)
    {
        var window = Create(owner, title, 340);
        var text = UiKit.Text(message, 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        text.Margin = new Thickness(0, 10, 0, 16);
        var ok = UiKit.Button(destructive, ButtonKind.Destructive, 13, onClick: () => window.DialogResult = true);
        var cancel = UiKit.Button(PadStrings.Cancel, ButtonKind.Bordered, 13, onClick: () => window.DialogResult = false);
        window.Content = Layout(title, text, cancel, ok);
        return window.ShowDialog() == true;
    }

    private static Window Create(Window owner, string title, double width)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = true,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        return window;
    }

    private static FrameworkElement Layout(string title, UIElement body, UIElement cancel, UIElement ok)
    {
        var stack = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        stack.Children.Add(UiKit.Text(title, 15, FontWeights.SemiBold));
        stack.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        ((FrameworkElement)ok).Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        return stack;
    }
}

/// <summary>The floating scratchpad: up to 12 tabs, Markdown marks, formatted reading, find, autosave.</summary>
internal sealed class ScratchpadWindow : GlassWindow
{
    private const double MinWidthDip = 280;
    private const double MinHeightDip = 220;

    private readonly IContributionServices _ctx;
    private readonly PadStore _store;
    private readonly Border _overlay = new() { IsHitTestVisible = false };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _tabScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 6, 12, 6) };
    private readonly TextBox _editor = new();
    private readonly TextBlock _placeholder = UiKit.Text(PadStrings.Placeholder, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
    private readonly RichTextBox _reading = new() { IsReadOnly = true, IsDocumentEnabled = true, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Grid _findBar = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _findBox = new();
    private readonly TextBlock _findCount = UiKit.Text(string.Empty, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
    private readonly Border _formatBar = new() { Height = 30, Visibility = Visibility.Collapsed };
    private readonly PadButton _pin;
    private readonly PadButton _formatToggle;
    private readonly PadButton _previewToggle;
    private readonly PadButton _copy;
    private readonly PadButton _export;
    private readonly PadButton _clear;
    private readonly List<PadButton> _newTabButtons = [];
    private PadButton? _newTab;
    private OutsideClickMonitor? _outside;
    private DispatcherTimer? _copiedTimer;
    private bool _pinned;
    private bool _previewing;
    private bool _marksExpanded;
    private bool _suppress;
    private bool _modal;
    private bool _hasPlace;
    private bool _resizing;

    public ScratchpadWindow(IContributionServices ctx, PadStore store)
        : base(14)
    {
        _ctx = ctx;
        _store = store;
        Title = PadStrings.Title;
        Width = 380;
        Height = 300;
        MinWidth = MinWidthDip;
        MinHeight = MinHeightDip;
        _store.SaveStateChanged += UpdateBanner;

        _pin = new PadButton("", PadStrings.CloseOnClickOutside, () => SetPinned(!_pinned), 22, 12);
        _formatToggle = new PadButton("", PadStrings.FormatMarks, ToggleFormat);
        _previewToggle = new PadButton("", PadStrings.PreviewFormatting, TogglePreview);
        _copy = new PadButton("", PadStrings.CopyAll, CopyAll);
        _export = new PadButton("", PadStrings.ExportAction, Export);
        _clear = new PadButton("", PadStrings.ClearAction, ClearAll);

        var root = new Grid();
        for (var i = 0; i < 7; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        void Place(UIElement element, int row)
        {
            Grid.SetRow(element, row);
            root.Children.Add(element);
        }

        Place(BuildHeader(), 0);
        Place(BuildTabBar(), 1);
        BuildBanner();
        Place(_banner, 2);
        BuildFindBar();
        Place(_findBar, 3);
        Place(BuildEditorArea(), 4);
        BuildFormatBar();
        Place(_formatBar, 5);
        Place(BuildFooter(), 6);

        _overlay.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        var host = new Grid();
        host.Children.Add(_overlay);
        host.Children.Add(root);
        AddResizeGrips(host);
        Body = host;

        PreviewKeyDown += OnWindowKey;
        Deactivated += (_, _) => { };
        Closed += (_, _) => _outside?.Dispose();
    }

    public bool IsModal => _modal;

    public bool HasKeyboard => IsVisible && IsActive;

    // ------------------------------------------------------------------------------------------------ build

    private UIElement BuildHeader()
    {
        var grid = new Grid { Height = 34, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UiKit.Text(PadStrings.Title, 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        title.Margin = new Thickness(12, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);
        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1 && e.OriginalSource is not PadButton)
            {
                try
                {
                    DragMove();
                    _hasPlace = true;
                }
                catch (InvalidOperationException)
                {
                    // released during the call
                }
            }
        };
        Grid.SetColumn(_pin, 1);
        grid.Children.Add(_pin);
        var close = new PadButton("", PadStrings.Close, Dismiss, 22, 14);
        close.Margin = new Thickness(2, 0, 12, 0);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        UpdatePinVisual();
        return grid;
    }

    private UIElement BuildTabBar()
    {
        var bar = new Grid { Height = 32, Margin = new Thickness(10, 0, 10, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tabScroll.Content = _tabs;
        _tabScroll.VerticalAlignment = VerticalAlignment.Center;
        bar.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        bar.Children.Add(_tabScroll);
        _newTab = new PadButton("", PadStrings.NewPad, NewTab, 22, 12);
        Grid.SetColumn(_newTab, 1);
        _newTab.Margin = new Thickness(6, 0, 0, 0);
        bar.Children.Add(_newTab);
        var more = new PadButton("", PadStrings.PadActions, () => { }, 22, 12);
        more.Margin = new Thickness(6, 0, 0, 0);
        more.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = PadMenu(_store.Selected);
            menu.PlacementTarget = more;
            menu.IsOpen = true;
        };
        Grid.SetColumn(more, 2);
        bar.Children.Add(more);
        var wrapper = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bar, Opacity = 1 };
        wrapper.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        return wrapper;
    }

    private void BuildBanner()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(UiKit.Icon("", 11, ThemeKeys.TextSecondary));
        var text = UiKit.Text(PadStrings.SaveFailed, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        text.Margin = new Thickness(6, 0, 0, 0);
        text.MaxWidth = 320;
        row.Children.Add(text);
        _banner.Child = row;
    }

    private void BuildFindBar()
    {
        _findBar.Margin = new Thickness(10, 6, 10, 2);
        _findBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
        {
            _findBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        _findBox.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        _findBox.FontSize = 12;
        _findBox.TextChanged += (_, _) => FindFrom(true, true);
        _findBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                FindFrom(Keyboard.Modifiers != ModifierKeys.Shift, false);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideFind();
            }
        };
        _findBar.Children.Add(_findBox);
        _findCount.Margin = new Thickness(8, 0, 4, 0);
        _findCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_findCount, 1);
        _findBar.Children.Add(_findCount);
        var prev = new PadButton("", Loc.T("Previous"), () => FindFrom(false, false), 22, 11);
        var next = new PadButton("", Loc.T("Next"), () => FindFrom(true, false), 22, 11);
        var close = new PadButton("", PadStrings.Close, HideFind, 22, 11);
        Grid.SetColumn(prev, 2);
        Grid.SetColumn(next, 3);
        Grid.SetColumn(close, 4);
        _findBar.Children.Add(prev);
        _findBar.Children.Add(next);
        _findBar.Children.Add(close);
    }

    private UIElement BuildEditorArea()
    {
        var grid = new Grid();
        _editor.AcceptsReturn = true;
        _editor.AcceptsTab = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.BorderThickness = new Thickness(0);
        _editor.Background = Brushes.Transparent;
        _editor.Padding = new Thickness(7, 2, 7, 2);
        _editor.IsInactiveSelectionHighlightEnabled = true;
        _editor.FontFamily = UiKit.TextFont;
        _editor.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _editor.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeKeys.TextPrimary);
        _editor.FocusVisualStyle = null;
        _editor.Template = PlainTextTemplate();
        System.Windows.Controls.SpellCheck.SetIsEnabled(_editor, false);
        _editor.TextChanged += OnEditorChanged;
        _editor.PreviewKeyDown += OnEditorKey;
        _editor.PreviewTextInput += OnEditorText;
        DataObject.AddPastingHandler(_editor, (_, e) =>
        {
            // plain text only: images and rich content are dropped
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText) && e.DataObject.GetData(DataFormats.UnicodeText) is string text)
            {
                e.DataObject = new DataObject(DataFormats.UnicodeText, text);
            }
            else
            {
                e.CancelCommand();
            }
        });
        grid.Children.Add(_editor);
        _placeholder.Margin = new Thickness(12, 2, 12, 0);
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(_placeholder);
        _reading.Padding = new Thickness(0);
        _reading.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        grid.Children.Add(_reading);
        return grid;
    }

    private static ControlTemplate PlainTextTemplate()
    {
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(UIElement.FocusableProperty, false);
        template.VisualTree = host;
        return template;
    }

    private void BuildFormatBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 2, 12, 2) };
        var glyphs = new[] { "", "", "", "H", "", "", "“", "", "" };
        for (var i = 0; i < 9; i++)
        {
            var mark = (PadMark)i;
            var button = new PadButton(glyphs[i], PadStrings.MarkLabels[i], () => ApplyMark(mark));
            button.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
            if (glyphs[i] is "H" or "“")
            {
                button.GlyphBlock.FontFamily = UiKit.TextFont;
                button.GlyphBlock.FontSize = glyphs[i] == "H" ? 13 : 19;
                button.GlyphBlock.FontWeight = FontWeights.SemiBold;
            }

            row.Children.Add(button);
        }

        _formatBar.Child = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private UIElement BuildFooter()
    {
        var grid = new Grid { Height = 36, Margin = new Thickness(12, 0, 12, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(row);
        row.Children.Add(_formatToggle);
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        group.Children.Add(_previewToggle);
        _copy.Margin = _export.Margin = new Thickness(12, 0, 0, 0);
        group.Children.Add(_copy);
        group.Children.Add(_export);
        row.Children.Add(group);
        _clear.HorizontalAlignment = HorizontalAlignment.Right;
        _clear.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_clear);
        return grid;
    }

    private void AddResizeGrips(Grid host)
    {
        void Grip(HorizontalAlignment h, VerticalAlignment v, double w, double ht, Cursor cursor, int dirX, int dirY)
        {
            var grip = new Border { Background = Brushes.Transparent, Width = w, Height = ht, HorizontalAlignment = h, VerticalAlignment = v, Cursor = cursor };
            if (double.IsNaN(w))
            {
                grip.Width = double.NaN;
            }

            if (double.IsNaN(ht))
            {
                grip.Height = double.NaN;
            }

            Point start = default;
            Rect rect = default;
            grip.MouseLeftButtonDown += (_, e) =>
            {
                grip.CaptureMouse();
                _resizing = true;
                start = Plat.Cursor();
                NativeMethods.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var r);
                rect = new Rect(r.Left, r.Top, r.Width, r.Height);
                e.Handled = true;
            };
            grip.MouseMove += (_, _) =>
            {
                if (!grip.IsMouseCaptured)
                {
                    return;
                }

                var now = Plat.Cursor();
                var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var minW = MinWidthDip * dpi;
                var minH = MinHeightDip * dpi;
                double x = rect.Left, y = rect.Top, w = rect.Width, hgt = rect.Height;
                var dx = now.X - start.X;
                var dy = now.Y - start.Y;
                if (dirX < 0)
                {
                    var nw = Math.Max(minW, rect.Width - dx);
                    x = rect.Right - nw;
                    w = nw;
                }
                else if (dirX > 0)
                {
                    w = Math.Max(minW, rect.Width + dx);
                }

                if (dirY < 0)
                {
                    var nh = Math.Max(minH, rect.Height - dy);
                    y = rect.Bottom - nh;
                    hgt = nh;
                }
                else if (dirY > 0)
                {
                    hgt = Math.Max(minH, rect.Height + dy);
                }

                NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, 0, (int)x, (int)y, (int)w, (int)hgt, NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
            };
            grip.MouseLeftButtonUp += (_, _) =>
            {
                grip.ReleaseMouseCapture();
                _resizing = false;
                _hasPlace = true;
            };
            host.Children.Add(grip);
        }

        const double e = 6;
        const double c = 12;
        Grip(HorizontalAlignment.Left, VerticalAlignment.Stretch, e, double.NaN, Cursors.SizeWE, -1, 0);
        Grip(HorizontalAlignment.Right, VerticalAlignment.Stretch, e, double.NaN, Cursors.SizeWE, 1, 0);
        Grip(HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, e, Cursors.SizeNS, 0, -1);
        Grip(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, e, Cursors.SizeNS, 0, 1);
        Grip(HorizontalAlignment.Left, VerticalAlignment.Top, c, c, Cursors.SizeNWSE, -1, -1);
        Grip(HorizontalAlignment.Right, VerticalAlignment.Top, c, c, Cursors.SizeNESW, 1, -1);
        Grip(HorizontalAlignment.Left, VerticalAlignment.Bottom, c, c, Cursors.SizeNESW, -1, 1);
        Grip(HorizontalAlignment.Right, VerticalAlignment.Bottom, c, c, Cursors.SizeNWSE, 1, 1);
    }

    // ------------------------------------------------------------------------------------------------ presenting

    /// <summary>Applies the live settings (text size, background opacity).</summary>
    public void ApplySettings(double textSize, double backgroundOpacity, bool closeOnClickOutside)
    {
        _editor.FontSize = textSize;
        _placeholder.FontSize = textSize;
        _overlay.Opacity = Math.Clamp(double.IsFinite(backgroundOpacity) ? backgroundOpacity : 1, 0, 1);
        if (!_pinnedExplicit)
        {
            _pinned = !closeOnClickOutside;
            UpdatePinVisual();
        }

        if (_previewing)
        {
            RenderReading();
        }
    }

    private bool _pinnedExplicit;

    /// <summary>Shows the pad (loading the document first). False when the document could not be loaded.</summary>
    public bool Present(double textSize, double backgroundOpacity, bool closeOnClickOutside)
    {
        if (!_store.Load())
        {
            ToolHud.Show("", PadStrings.LoadFailed);
            return false;
        }

        _pinnedExplicit = false;
        _previewing = false;
        _marksExpanded = false;
        _reading.Visibility = Visibility.Collapsed;
        _formatBar.Visibility = Visibility.Collapsed;
        HideFind();
        ApplySettings(textSize, backgroundOpacity, closeOnClickOutside);
        _pinned = !closeOnClickOutside;
        UpdatePinVisual();
        LoadSelected();
        RebuildTabs();
        UpdateBanner();
        UpdateFooter();
        Position();
        Plate.Opacity = 0;
        Show();
        Plate.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
        Activate();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
        _editor.ScrollToEnd();
        _outside?.Dispose();
        _outside = new OutsideClickMonitor(_ctx.Hooks, _ctx.Ui, FramePhysical, Dismiss, () => IsVisible && !_pinned && !_modal && !_resizing);
        return true;
    }

    /// <summary>Flushes and hides.</summary>
    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        _store.SetText(_editor.Text);
        _store.Flush();
        if (_store.SaveFailed)
        {
            ToolHud.Show("", PadStrings.SaveFailed);
        }

        _outside?.Dispose();
        _outside = null;
        _modal = false;
        _pinned = false;
        _previewing = false;
        Hide();
    }

    private Rect FramePhysical()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Rect(Left * dpi, Top * dpi, ActualWidth * dpi, ActualHeight * dpi);
    }

    private void Position()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var monitor = Plat.Monitor().WorkArea;
        var width = Width * dpi;
        var height = Height * dpi;
        if (_hasPlace)
        {
            var frame = new Rect(Left * dpi, Top * dpi, width, height);
            if (System.Windows.Forms.Screen.AllScreens.Any(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height).IntersectsWith(frame)))
            {
                return;
            }
        }

        var x = monitor.Left + (monitor.Width - width) / 2;
        var y = monitor.Top + (monitor.Height - height) * 0.42;
        x = Math.Clamp(x, monitor.Left + 16, Math.Max(monitor.Left + 16, monitor.Right - width - 16));
        y = Math.Clamp(y, monitor.Top + 16, Math.Max(monitor.Top + 16, monitor.Bottom - height - 16));
        Plat.PlaceWindow(this, x, y, 0, 0);
        _hasPlace = true;
    }

    // ------------------------------------------------------------------------------------------------ editor <-> store

    private void LoadSelected()
    {
        _suppress = true;
        _editor.IsUndoEnabled = false;
        _editor.Text = _store.Selected.Text.Replace("\n", "\r\n");
        _editor.IsUndoEnabled = true;
        _suppress = false;
        UpdatePlaceholder();
        UpdateFooter();
    }

    private void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _store.SetText(_editor.Text);
        UpdatePlaceholder();
        UpdateFooter();
        if (_findBar.Visibility == Visibility.Visible)
        {
            UpdateFindCount();
        }
    }

    private void UpdatePlaceholder() =>
        _placeholder.Visibility = _editor.Text.Length == 0 && !_previewing ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateFooter()
    {
        var has = _editor.Text.Length > 0;
        foreach (var b in new[] { _previewToggle, _copy, _export, _clear })
        {
            b.IsEnabled = has;
        }

        _formatToggle.Tint(_marksExpanded ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _previewToggle.Glyph = _previewing ? "" : "";
        _previewToggle.ToolTip = _previewing ? PadStrings.EditText : PadStrings.PreviewFormatting;
        _previewToggle.Tint(_previewing ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _formatToggle.IsEnabled = !_previewing;
        if (!has && _previewing)
        {
            SetPreview(false);
        }
    }

    private void UpdateBanner() => _banner.Visibility = _store.SaveFailed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Replaces the editor text through the undo path with a minimal change (one undo step).</summary>
    private void ApplyEdit(PadEdit edit)
    {
        var old = _editor.Text;
        var neu = edit.Text;
        var prefix = 0;
        var max = Math.Min(old.Length, neu.Length);
        while (prefix < max && old[prefix] == neu[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == neu[neu.Length - 1 - suffix])
        {
            suffix++;
        }

        _editor.BeginChange();
        try
        {
            _editor.Select(prefix, old.Length - prefix - suffix);
            _editor.SelectedText = neu.Substring(prefix, neu.Length - prefix - suffix);
        }
        finally
        {
            _editor.EndChange();
        }

        _editor.Select(Math.Clamp(edit.SelStart, 0, _editor.Text.Length), Math.Clamp(edit.SelLength, 0, Math.Max(0, _editor.Text.Length - edit.SelStart)));
    }

    // ------------------------------------------------------------------------------------------------ keys

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.Return && mods == ModifierKeys.Shift)
        {
            e.Handled = true;
            ApplyEdit(ScratchpadText.LineBreak(_editor.Text, _editor.SelectionStart, _editor.SelectionLength));
        }
        else if (key is Key.Up or Key.Down && mods == ModifierKeys.Alt)
        {
            e.Handled = true;
            if (ScratchpadText.MoveLines(key == Key.Up, _editor.Text, _editor.SelectionStart, _editor.SelectionLength) is { } moved)
            {
                ApplyEdit(moved);
            }
        }
    }

    private void OnEditorText(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "]" && _editor.SelectionLength == 0 && ScratchpadText.CompletesTaskBox(_editor.Text, _editor.CaretIndex))
        {
            e.Handled = true;
            var at = _editor.CaretIndex;
            _editor.SelectedText = " ]";
            _editor.CaretIndex = at + 2;
        }
    }

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (_modal)
        {
            return;
        }

        if (key == Key.Escape)
        {
            e.Handled = true;
            if (_findBar.Visibility == Visibility.Visible)
            {
                HideFind();
            }
            else
            {
                Dismiss();
            }

            return;
        }

        if (mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt))
        {
            var ctrlOnly = mods == ModifierKeys.Control;
            switch (key)
            {
                case Key.T when ctrlOnly:
                    e.Handled = true;
                    NewTab();
                    break;
                case Key.W when ctrlOnly:
                    e.Handled = true;
                    if (_store.Doc!.Pads.Count > 1)
                    {
                        CloseTab(_store.Selected);
                    }
                    else
                    {
                        Dismiss();
                    }

                    break;
                case Key.F when ctrlOnly:
                    e.Handled = true;
                    ShowFind();
                    break;
                case Key.G when mods is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift):
                    e.Handled = true;
                    FindFrom(!mods.HasFlag(ModifierKeys.Shift), false);
                    break;
                case Key.Return when ctrlOnly:
                    e.Handled = true;
                    TogglePreview();
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ tabs

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        var doc = _store.Doc!;
        foreach (var pad in doc.Pads)
        {
            _tabs.Children.Add(BuildTab(pad, doc.Pads.Count > 1));
        }

        if (_newTab is not null)
        {
            var full = doc.Pads.Count >= PadRules.MaxPads;
            _newTab.IsEnabled = !full;
            _newTab.ToolTip = full ? PadStrings.PadLimit(PadRules.MaxPads) : PadStrings.NewPad;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var selected = _tabs.Children.OfType<FrameworkElement>().FirstOrDefault(c => (string)c.Tag == doc.SelectedId);
            if (selected is not null)
            {
                var point = selected.TransformToAncestor(_tabs).Transform(new Point(selected.ActualWidth / 2, 0));
                _tabScroll.ScrollToHorizontalOffset(Math.Max(0, point.X - _tabScroll.ViewportWidth / 2));
            }
        }, DispatcherPriority.Loaded);
    }

    private FrameworkElement BuildTab(PadItem pad, bool closable)
    {
        var selected = pad.Id == _store.Doc!.SelectedId;
        var border = new Border { Height = 24, MinWidth = 46, MaxWidth = 130, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Tag = pad.Id, Background = Brushes.Transparent };
        if (selected)
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(pad.Name, 11, selected ? FontWeights.SemiBold : FontWeights.Normal, selected ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
        label.Margin = new Thickness(8, 0, 8, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        PadButton? x = null;
        if (closable)
        {
            x = new PadButton("", PadStrings.ClosePad, () => CloseTab(pad), 14, 8);
            x.Margin = new Thickness(0, 0, 4, 0);
            x.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(x, 1);
            grid.Children.Add(x);
        }

        label.Margin = new Thickness(8, 0, closable ? 2 : 8, 0);
        border.Child = grid;
        border.MouseEnter += (_, _) =>
        {
            if (x is not null)
            {
                x.Visibility = Visibility.Visible;
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (x is not null && !selected)
            {
                x.Visibility = Visibility.Collapsed;
            }
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                SelectTab(pad);
            }
        };
        border.ContextMenu = PadMenu(pad);
        System.Windows.Automation.AutomationProperties.SetName(border, pad.Name);
        return border;
    }

    private ContextMenu PadMenu(PadItem pad)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = PadStrings.RenamePad };
        rename.Click += (_, _) => Rename(pad);
        var close = new MenuItem { Header = PadStrings.ClosePad, IsEnabled = _store.Doc!.Pads.Count > 1 };
        close.Click += (_, _) => CloseTab(pad);
        menu.Items.Add(rename);
        menu.Items.Add(close);
        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => _modal = false;
        return menu;
    }

    private void SelectTab(PadItem pad)
    {
        if (pad.Id == _store.Doc!.SelectedId)
        {
            return;
        }

        _store.SetText(_editor.Text);
        _store.Select(pad.Id);
        SetPreview(false);
        LoadSelected();
        RebuildTabs();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
    }

    private void NewTab()
    {
        _store.SetText(_editor.Text);
        if (_store.Add() is null)
        {
            return;
        }

        SetPreview(false);
        LoadSelected();
        RebuildTabs();
        _editor.Focus();
    }

    private void Rename(PadItem pad)
    {
        _modal = true;
        try
        {
            var name = PadDialogs.Prompt(this, PadStrings.RenamePad, pad.Name);
            if (name is not null && _store.Rename(pad.Id, name))
            {
                RebuildTabs();
            }
        }
        finally
        {
            _modal = false;
            Activate();
        }
    }

    private void CloseTab(PadItem pad)
    {
        if (_store.Doc!.Pads.Count <= 1)
        {
            return;
        }

        if (pad.Text.Length > 0)
        {
            _modal = true;
            try
            {
                if (!PadDialogs.Confirm(this, PadStrings.ClosePad, PadStrings.DeleteMessage(pad.Name), PadStrings.ClosePad))
                {
                    return;
                }
            }
            finally
            {
                _modal = false;
                Activate();
            }
        }

        _store.SetText(_editor.Text);
        var wasSelected = pad.Id == _store.Doc.SelectedId;
        if (_store.Remove(pad.Id))
        {
            if (wasSelected)
            {
                SetPreview(false);
                LoadSelected();
            }

            RebuildTabs();
            _editor.Focus();
        }
    }

    // ------------------------------------------------------------------------------------------------ footer / format actions

    private void SetPinned(bool pinned)
    {
        _pinned = pinned;
        _pinnedExplicit = true;
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        _pin.Glyph = _pinned ? "" : "";
        _pin.Tint(_pinned ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _pin.ToolTip = _pinned ? PadStrings.CloseOnClickOutside : PadStrings.KeepOpen;
    }

    private void ToggleFormat()
    {
        if (_previewing)
        {
            return;
        }

        _marksExpanded = !_marksExpanded;
        _formatBar.Visibility = _marksExpanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateFooter();
        _editor.Focus();
    }

    private void ApplyMark(PadMark mark)
    {
        _editor.Focus();
        ApplyEdit(ScratchpadText.ApplyMark(mark, _editor.Text, _editor.SelectionStart, _editor.SelectionLength));
        _editor.Focus();
    }

    private void TogglePreview()
    {
        if (_editor.Text.Length == 0)
        {
            SetPreview(false);
            return;
        }

        SetPreview(!_previewing);
    }

    private void SetPreview(bool on)
    {
        if (on && _editor.Text.Length == 0)
        {
            on = false;
        }

        _previewing = on;
        if (on)
        {
            _marksExpanded = false;
            _formatBar.Visibility = Visibility.Collapsed;
            HideFind();
            RenderReading();
        }

        _reading.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _editor.Opacity = on ? 0 : 1;
        _editor.IsHitTestVisible = !on;
        UpdatePlaceholder();
        UpdateFooter();
        if (!on)
        {
            _editor.Focus();
        }
    }

    private void RenderReading()
    {
        var blocks = ScratchpadMarkdown.Parse(_editor.Text);
        _reading.Document = ScratchpadReading.Build(blocks, _editor.FontSize, offset =>
        {
            var toggled = ScratchpadText.ToggleTask(_editor.Text, offset);
            if (toggled != _editor.Text)
            {
                ApplyEdit(new PadEdit(toggled, _editor.SelectionStart, _editor.SelectionLength));
                RenderReading();
            }
        }, url =>
        {
            if (!UiKit.OpenWebUrl(url))
            {
                System.Media.SystemSounds.Beep.Play();
            }
        });
    }

    private void CopyAll()
    {
        var text = _editor.Text;
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(30);
                }
            }
        }
        catch
        {
            return;
        }

        _copy.Glyph = "";
        _copy.Tint(ThemeKeys.Green);
        _copy.ToolTip = PadStrings.Copied;
        _copiedTimer?.Stop();
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer?.Stop();
            _copy.Glyph = "";
            _copy.Tint(ThemeKeys.TextSecondary);
            _copy.ToolTip = PadStrings.CopyAll;
        };
        _copiedTimer.Start();
    }

    private void Export()
    {
        if (_editor.Text.Length == 0 || _modal)
        {
            return;
        }

        _store.SetText(_editor.Text);
        _store.Flush();
        var padId = _store.Selected.Id;
        var name = _store.Selected.Name;
        _modal = true;
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text (*.txt)|*.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = $"{ScratchpadText.SafeFileStem(name)} {DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.txt",
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var current = _store.Doc?.Pads.FirstOrDefault(p => p.Id == padId);
            if (current is null)
            {
                ToolHud.Show("", PadStrings.ExportFailed);
                return;
            }

            var temp = dialog.FileName + ".tmp";
            try
            {
                File.WriteAllText(temp, current.Text.Replace("\n", "\r\n"), new UTF8Encoding(false));
                File.Move(temp, dialog.FileName, true);
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // nothing to clean
                }

                ToolHud.Show("", PadStrings.ExportFailed);
            }
        }
        finally
        {
            _modal = false;
            Activate();
            _editor.Focus();
        }
    }

    private void ClearAll()
    {
        if (_editor.Text.Length == 0)
        {
            return;
        }

        SetPreview(false);
        _editor.Focus();
        _editor.BeginChange();
        try
        {
            _editor.SelectAll();
            _editor.SelectedText = string.Empty;
        }
        finally
        {
            _editor.EndChange();
        }

        _store.SetText(_editor.Text);
        _store.Flush();
    }

    // ------------------------------------------------------------------------------------------------ find

    private void ShowFind()
    {
        if (_previewing)
        {
            SetPreview(false);
        }

        _findBar.Visibility = Visibility.Visible;
        if (_editor.SelectionLength > 0 && !_editor.SelectedText.Contains('\n') && !_editor.SelectedText.Contains('\r'))
        {
            _findBox.Text = _editor.SelectedText;
        }

        _findBox.Focus();
        _findBox.SelectAll();
        UpdateFindCount();
    }

    private void HideFind()
    {
        if (_findBar.Visibility != Visibility.Visible)
        {
            return;
        }

        _findBar.Visibility = Visibility.Collapsed;
        if (IsVisible)
        {
            _editor.Focus();
        }
    }

    private int CountMatches()
    {
        var query = _findBox.Text;
        if (query.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var i = 0;
        while ((i = _editor.Text.IndexOf(query, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            i += query.Length;
        }

        return count;
    }

    private void UpdateFindCount()
    {
        var total = CountMatches();
        if (total == 0)
        {
            _findCount.Text = _findBox.Text.Length == 0 ? string.Empty : "0";
            return;
        }

        var before = 0;
        var query = _findBox.Text;
        var i = 0;
        var pos = _editor.SelectionStart;
        while ((i = _editor.Text.IndexOf(query, i, StringComparison.OrdinalIgnoreCase)) >= 0 && i <= pos)
        {
            before++;
            i += query.Length;
        }

        _findCount.Text = Loc.F("{0} of {1}", Math.Max(1, before), total);
    }

    private void FindFrom(bool forward, bool fromSelectionStart)
    {
        var query = _findBox.Text;
        var text = _editor.Text;
        if (query.Length == 0 || text.Length == 0)
        {
            UpdateFindCount();
            return;
        }

        int index;
        if (forward)
        {
            var start = fromSelectionStart ? _editor.SelectionStart : _editor.SelectionStart + Math.Max(1, _editor.SelectionLength);
            start = Math.Min(start, text.Length);
            index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            var start = Math.Max(0, _editor.SelectionStart - 1);
            index = start == 0 && _editor.SelectionStart == 0 ? -1 : text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                index = text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (index >= 0)
        {
            _editor.Select(index, query.Length);
            var line = _editor.GetLineIndexFromCharacterIndex(index);
            if (line >= 0)
            {
                _editor.ScrollToLine(line);
            }
        }

        UpdateFindCount();
    }

    // ------------------------------------------------------------------------------------------------ test hooks

    internal string EditorText
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    internal bool Previewing => _previewing;

    internal void TestApply(PadMark mark, int start, int length)
    {
        _editor.Select(start, length);
        ApplyMark(mark);
    }

    internal string TestUndo()
    {
        _editor.Undo();
        return _editor.Text;
    }

    internal int TestTabCount => _store.Doc!.Pads.Count;

    internal void TestNewTab() => NewTab();

    internal void TestTogglePreview() => TogglePreview();

    internal void TestShowFormatBar() => ToggleFormat();
}
