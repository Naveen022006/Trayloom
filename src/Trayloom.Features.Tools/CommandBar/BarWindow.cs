// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>The Command Bar panel (spec 1.2): 560 wide glass window with the field, chips, list and footer.</summary>
internal sealed class BarWindow : GlassWindow
{
    private const double BarWidth = 560;
    private const double ListCeiling = 452;
    private double _listCeiling = ListCeiling;

    private readonly BarService _svc;
    private readonly TextBox _field;
    private readonly TextBlock _placeholder;
    private readonly StackPanel _root = new();
    private readonly Grid _fieldStrip = new();
    private readonly StackPanel _chipHost = new();
    private readonly StackPanel _hintHost = new();
    private readonly Border _clear;
    private readonly ContentControl _leading = new();
    private readonly StackPanel _body = new();
    private readonly StackPanel _footerHost = new();
    private readonly List<(Border Row, TextBlock? CmdChip)> _rowViews = [];
    private object? _renderedItems;
    private bool _suppress;
    private bool _mouseMoved;
    private Point _lastMouse;
    private readonly DispatcherTimer _saveTimer;
    private bool _dragging;
    private nint _savedLayout;
    private bool _capturing;
    private bool _hiding;
    private ScrollViewer? _scroll;
    private IDisposable? _outside;

    public BarWindow(BarService svc)
        : base(22)
    {
        _svc = svc;
        Width = BarWidth;
        SizeToContent = SizeToContent.Height;
        ShowActivated = true;
        Title = Loc.T("Command Bar");
        Focusable = true;

        _field = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 16,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            MinHeight = 22,
            AcceptsReturn = false,
            SpellCheck = { IsEnabled = false },
            FontFamily = UiKit.TextFont,
        };
        var fieldTemplate = new ControlTemplate(typeof(TextBox)) { VisualTree = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" } };
        _field.Template = fieldTemplate;
        _field.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _field.SetResourceReference(TextBoxBase.CaretBrushProperty, ThemeKeys.TextPrimary);
        _field.TextChanged += (_, _) =>
        {
            _placeholder!.Visibility = _field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateClear();
            if (!_suppress)
            {
                _svc.SetQuery(_field.Text);
            }
        };
        _placeholder = new TextBlock { FontSize = 16, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary);

        var mark = BrandMark.Create(22);
        mark.Opacity = 0.85;
        mark.Cursor = Cursors.Hand;
        mark.ToolTip = BarStrings.DragHint;
        var markHost = new Border { Background = Brushes.Transparent, Child = mark, Cursor = Cursors.Hand, ToolTip = BarStrings.DragHint, Width = 26 };
        markHost.MouseLeftButtonDown += OnMarkDown;

        _clear = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, Child = UiKit.Icon("", 14, ThemeKeys.TextTertiary) };
        _clear.MouseLeftButtonUp += (_, _) =>
        {
            _svc.SetQueryDirect("");
            _field.Focus();
        };

        _fieldStrip.Margin = new Thickness(16, 14, 16, 14);
        _fieldStrip.MinHeight = 22;
        _fieldStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fieldStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fieldStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _fieldStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fieldStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(markHost, 0);
        markHost.Margin = new Thickness(0, 0, 10, 0);
        _leading.VerticalAlignment = VerticalAlignment.Center;
        _leading.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(_leading, 1);
        var fieldHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        fieldHost.Children.Add(_placeholder);
        fieldHost.Children.Add(_field);
        Grid.SetColumn(fieldHost, 2);
        Grid.SetColumn(_hintHost, 3);
        _hintHost.Orientation = Orientation.Horizontal;
        _hintHost.VerticalAlignment = VerticalAlignment.Center;
        _hintHost.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(_clear, 4);
        _fieldStrip.Children.Add(markHost);
        _fieldStrip.Children.Add(_leading);
        _fieldStrip.Children.Add(fieldHost);
        _fieldStrip.Children.Add(_hintHost);
        _fieldStrip.Children.Add(_clear);

        _root.Children.Add(_fieldStrip);
        _root.Children.Add(_body);
        _root.Children.Add(_footerHost);
        Body = _root;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_dragging)
            {
                return;
            }

            SavePositionOffset();
        };

        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, _) => UpdateCommandChips();
        PreviewMouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if ((p - _lastMouse).Length > 1)
            {
                _mouseMoved = true;
            }

            _lastMouse = p;
        };
        Deactivated += (_, _) =>
        {
            if (!_hiding && IsVisible && !_capturing)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (IsVisible && !IsActive && !_capturing)
                    {
                        _svc.Hide();
                    }
                }, DispatcherPriority.Background);
            }
        };
        _svc.Changed += OnChanged;
        _svc.QueryReplaced += text =>
        {
            SetText(text);
        };
        _svc.FocusFieldRequested += text =>
        {
            SetText(text);
            _field.Focus();
        };
        _svc.HideRequested += HideBar;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ApplyRegion(sizeInfo.NewSize);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ApplyRegion(new Size(ActualWidth, ActualHeight)));
    }

    private void ApplyRegion(Size size)
    {
        if (AllowsTransparency)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0 && size.Width > 0 && size.Height > 0)
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var d = (int)Math.Round(44 * dpi);
            SetWindowRgn(handle, CreateRoundRectRgn(0, 0, (int)Math.Ceiling(size.Width * dpi) + 1, (int)Math.Ceiling(size.Height * dpi) + 1, d, d), true);
        }
    }

    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    // ------------------------------------------------------------------ presentation

    /// <summary>Shows the bar at its position with a fresh state.</summary>
    public void Present(string initialQuery = "")
    {
        _hiding = false;
        _mouseMoved = false;
        _renderedItems = null;
        _svc.Visible = true;
        SetText("");
        Place();
        Show();
        Activate();
        _field.Focus();
        Keyboard.Focus(_field);
        if (_svc.Prefs.AsciiLayout)
        {
            SwitchToEnglish();
        }

        Render();
        _outside?.Dispose();
        _outside = null;
    }

    public void HideBar()
    {
        if (_hiding)
        {
            return;
        }

        _hiding = true;
        _svc.Visible = false;
        _svc.End();
        _saveTimer.Stop();
        Hide();
        RestoreLayout();
    }

    private void SetText(string text)
    {
        _suppress = true;
        _field.Text = text;
        _field.CaretIndex = text.Length;
        _suppress = false;
        _placeholder.Text = _svc.Placeholder;
        _placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateClear();
    }

    private void UpdateClear() => _clear.Visibility = _field.Text.Length > 0 && _svc.Mode == BarMode.Search ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------------ position (1.7.10)

    private void Place()
    {
        var area = Screens.UnderCursor().WorkArea;
        Plat.PlaceWindow(this, area.Left + 100, area.Top + 100, BarWidth, 0);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Work areas are physical pixels; the window and its content use device-independent units.
        var width = Math.Min(BarWidth, Math.Max(1, area.Width / dpi - 32));
        Width = width;
        _listCeiling = Math.Min(ListCeiling, Math.Max(80, area.Height / dpi - 220));
        var offset = _svc.Prefs.PositionOffset ?? (0, 0);
        var left = area.Left + (area.Width - width * dpi) / 2 + offset.Dx * dpi;
        var top = area.Top + area.Height * 0.28 + offset.Dy * dpi;
        left = Math.Clamp(left, area.Left + 16 * dpi, Math.Max(area.Left + 16 * dpi, area.Right - width * dpi - 16 * dpi));
        top = Math.Clamp(top, area.Top + 16 * dpi, Math.Max(area.Top + 16 * dpi, area.Bottom - (_listCeiling + 200) * dpi));
        Plat.PlaceWindow(this, left, top, width, 0);
    }

    private void OnMarkDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _svc.Prefs.PositionOffset = null;
            Place();
            return;
        }

        _dragging = true;
        try
        {
            DragMove();
        }
        catch
        {
            // drag aborted
        }
        finally
        {
            _dragging = false;
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void SavePositionOffset()
    {
        var area = Screens.FromPoint(new Point((Left + ActualWidth / 2) * VisualTreeHelper.GetDpi(this).DpiScaleX, (Top + 20) * VisualTreeHelper.GetDpi(this).DpiScaleX)).WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dx = (int)Math.Round((Left * dpi + ActualWidth * dpi / 2 - (area.Left + area.Width / 2)) / dpi);
        var dy = (int)Math.Round((Top * dpi - (area.Top + area.Height * 0.28)) / dpi);
        _svc.Prefs.PositionOffset = (dx, dy);
    }

    // ------------------------------------------------------------------ keyboard layout (1.7.11)

    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, nint[]? list);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint hkl, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    private void SwitchToEnglish()
    {
        try
        {
            var target = _svc.Target;
            var thread = target == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(target, out _);
            var current = GetKeyboardLayout(thread);
            if (((long)current & 0xFF) == 0x09 && ((long)current >> 16 & 0xFFFF) == ((long)current & 0xFFFF))
            {
                return;
            }

            var count = GetKeyboardLayoutList(0, null);
            var list = new nint[count];
            GetKeyboardLayoutList(count, list);
            var pick = list.FirstOrDefault(h => ((long)h & 0xFFFF) == 0x0409);
            if (pick == 0)
            {
                pick = list.FirstOrDefault(h => ((long)h & 0xFF) == 0x09 && ((long)h >> 16 & 0xFFFF) == ((long)h & 0xFFFF));
            }

            if (pick != 0 && ActivateKeyboardLayout(pick, 0x100) != 0)
            {
                _savedLayout = current;
            }
        }
        catch
        {
            // layout switching is best effort
        }
    }

    private void RestoreLayout()
    {
        if (_savedLayout == 0)
        {
            return;
        }

        var hkl = _savedLayout;
        _savedLayout = 0;
        var target = _svc.Target;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (target != 0)
                {
                    PostMessageW(target, 0x0050, 0, hkl);
                }
            }
            catch
            {
                // ignored
            }
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ keys (1.4.2 / 1.4.3)

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        UpdateCommandChips();
        if (key == Key.ImeProcessed || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            if (_svc.Mode == BarMode.Capturing)
            {
                e.Handled = true;
            }

            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (_svc.Mode == BarMode.Capturing)
        {
            e.Handled = true;
            if (key == Key.Escape)
            {
                _svc.StepBack();
            }
            else if (key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None)
            {
                _svc.CaptureCombination(null);
            }
            else if (Keyboard.Modifiers != ModifierKeys.None)
            {
                var mods = HotkeyModifiers.None;
                if (ctrl)
                {
                    mods |= HotkeyModifiers.Control;
                }

                if (alt)
                {
                    mods |= HotkeyModifiers.Alt;
                }

                if (shift)
                {
                    mods |= HotkeyModifiers.Shift;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
                {
                    mods |= HotkeyModifiers.Win;
                }

                _svc.CaptureCombination(new Hotkey(mods, key));
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
            }

            return;
        }

        if (_svc.Mode == BarMode.Confirm && key is not (Key.Return or Key.Escape or Key.Enter))
        {
            _svc.StepBack();
            return;
        }

        switch (key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_svc.StepBack())
                {
                    _svc.Hide();
                }

                return;
            case Key.Return or Key.Enter:
                e.Handled = true;
                if (_field.IsFocused && Keyboard.IsKeyDown(Key.ImeProcessed))
                {
                    return;
                }

                if (_svc.Mode == BarMode.Naming)
                {
                    _svc.SubmitName(_field.Text);
                }
                else if (_svc.Mode == BarMode.Argument)
                {
                    _svc.SubmitArgument(_field.Text);
                }
                else if (ctrl && _svc.Mode == BarMode.Search)
                {
                    _svc.RevealSelected();
                }
                else if (_svc.Mode == BarMode.Search && _svc.Rows.Count == 0 && _svc.Query.Length > 0)
                {
                    _svc.SetQueryDirect("");
                }
                else
                {
                    _svc.RunSelected();
                }

                return;
            case Key.Up:
                e.Handled = true;
                _svc.Move(-1);
                return;
            case Key.Down:
                e.Handled = true;
                _svc.Move(1);
                return;
            case Key.P when ctrl && !shift:
                e.Handled = true;
                _svc.Move(-1);
                return;
            case Key.N when ctrl:
                e.Handled = true;
                _svc.Move(1);
                return;
            case Key.P when ctrl && shift:
                e.Handled = true;
                _svc.TogglePinSelected();
                return;
            case Key.K when ctrl:
                e.Handled = true;
                _svc.OpenActions();
                return;
            case Key.OemComma when ctrl:
                e.Handled = true;
                _svc.Hide();
                _svc.Ctx.Shell.OpenSettings(SettingsPages.CommandBar);
                return;
            case Key.Left when _field.Text.Length == 0 && _svc.Mode == BarMode.Search:
                e.Handled = true;
                _svc.WalkChips(-1);
                return;
            case Key.Right when _field.Text.Length == 0 && _svc.Mode == BarMode.Search:
                e.Handled = true;
                _svc.WalkChips(1);
                return;
            case Key.Tab:
                e.Handled = true;
                _svc.Tab();
                return;
        }

        if (ctrl && !alt && key is >= Key.D1 and <= Key.D9 && _svc.Mode == BarMode.Search)
        {
            e.Handled = true;
            _svc.HandleDigit(key - Key.D0);
            return;
        }

        if (ctrl && !alt && key is >= Key.NumPad1 and <= Key.NumPad9 && _svc.Mode == BarMode.Search)
        {
            e.Handled = true;
            _svc.HandleDigit(key - Key.NumPad0);
            return;
        }

        if (ctrl && key is Key.W or Key.M or Key.H or Key.Q)
        {
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ rendering

    private void OnChanged()
    {
        if (!IsVisible)
        {
            return;
        }

        _placeholder.Text = _svc.Placeholder;
        if (_svc.Mode == BarMode.Search && ReferenceEquals(_svc.Items, _renderedItems) && _rowViews.Count > 0)
        {
            UpdateSelection();
            return;
        }

        Render();
    }

    private static Color PrimaryAlpha(double a) => ThemeManager.IsLight ? Color.FromArgb((byte)(a * 255), 0, 0, 0) : Color.FromArgb((byte)(a * 255), 255, 255, 255);

    private static Brush AccentAlpha(double a)
    {
        var c = ThemeManager.AccentColor;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static Brush Primary(double a)
    {
        var brush = new SolidColorBrush(PrimaryAlpha(a));
        brush.Freeze();
        return brush;
    }

    private static TextBlock Label(string text, double size, string brushKey, FontWeight? weight = null, bool rounded = false)
    {
        var tb = UiKit.Text(text, size, weight ?? FontWeights.Normal, brushKey);
        if (rounded)
        {
            tb.FontFamily = UiKit.DisplayFont;
        }

        return tb;
    }

    private void Render()
    {
        _body.Children.Clear();
        _footerHost.Children.Clear();
        _hintHost.Children.Clear();
        _rowViews.Clear();
        _renderedItems = _svc.Items;
        _leading.Content = null;
        _scroll = null;
        var mode = _svc.Mode;
        UpdateClear();
        _placeholder.Text = _svc.Placeholder;

        if (mode is BarMode.Argument or BarMode.Naming && _svc.ModeEntry is { } chipEntry)
        {
            _leading.Content = TitleChip(chipEntry.Title);
        }

        if (_svc.IsCollapsed)
        {
            RenderCompactHints();
            return;
        }

        switch (mode)
        {
            case BarMode.Argument:
                _body.Children.Add(UiKit.Divider());
                _body.Children.Add(ArgumentCard());
                return;
            case BarMode.Confirm:
                _body.Children.Add(UiKit.Divider());
                _body.Children.Add(ConfirmCard());
                return;
            case BarMode.Naming:
                _body.Children.Add(UiKit.Divider());
                _body.Children.Add(NamingCard());
                return;
            case BarMode.Capturing:
                _body.Children.Add(UiKit.Divider());
                _body.Children.Add(CaptureCard());
                return;
            case BarMode.Actions:
                _body.Children.Add(UiKit.Divider());
                RenderActions();
                _footerHost.Children.Add(UiKit.Divider());
                _footerHost.Children.Add(Footer(false));
                return;
        }

        _body.Children.Add(UiKit.Divider());
        var chips = _svc.CategoryChips();
        if (chips.Count > 0)
        {
            _body.Children.Add(ChipRow(chips));
        }

        if (_svc.ShowedHome && _svc.ActiveCategory is null && _svc.Query.Length == 0 && chips.Count > 0)
        {
            _body.Children.Add(TryRow());
        }

        if (_svc.Rows.Count == 0 && _svc.Query.Trim().Length > 0)
        {
            _body.Children.Add(EmptyState());
        }
        else
        {
            _body.Children.Add(ListView());
        }

        _footerHost.Children.Add(UiKit.Divider());
        _footerHost.Children.Add(Footer(true));
        UpdateCommandChips();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollSelectedIntoView);
    }

    private Border TitleChip(string title)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(100),
            Padding = new Thickness(8, 3, 8, 3),
            Background = AccentAlpha(0.14),
            Child = Label(title, 12, ThemeKeys.Accent, FontWeights.SemiBold),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return chip;
    }

    private void RenderCompactHints()
    {
        _hintHost.Children.Add(Label("↓", 9, ThemeKeys.TextTertiary, FontWeights.SemiBold, true));
        var s = Label(BarStrings.Suggestions, 9, ThemeKeys.TextTertiary);
        s.Margin = new Thickness(3, 0, 0, 0);
        _hintHost.Children.Add(s);
        var esc = Label("Esc", 9, ThemeKeys.TextTertiary, FontWeights.SemiBold, true);
        esc.Margin = new Thickness(8, 0, 0, 0);
        _hintHost.Children.Add(esc);
    }

    private FrameworkElement ArgumentCard()
    {
        var entry = _svc.ModeEntry!;
        var stack = new StackPanel { Margin = new Thickness(17, 12, 17, 12) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(IconBox(entry));
        var title = Label(entry.Title, 13, ThemeKeys.TextPrimary, FontWeights.Medium);
        title.Margin = new Thickness(10, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        stack.Children.Add(row);
        var hint = Label(BarStrings.ArgumentHint, 10.5, ThemeKeys.TextTertiary);
        hint.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(hint);
        return stack;
    }

    private FrameworkElement ConfirmCard()
    {
        var entry = _svc.ModeEntry!;
        var outer = new Border { Margin = new Thickness(12), Padding = new Thickness(12), CornerRadius = new CornerRadius(12) };
        var red = Color.FromArgb(0x17, 0xFF, 0x45, 0x3A);
        outer.Background = new SolidColorBrush(red);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = IconBox(entry);
        icon.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(entry.ConfirmPrompt ?? "", 13, ThemeKeys.TextPrimary, FontWeights.SemiBold));
        var hint = Label(BarStrings.ConfirmHint, 10.5, ThemeKeys.TextSecondary);
        hint.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(hint);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cancel = UiKit.Button(BarStrings.Cancel, ButtonKind.Bordered, 12, null, () => _svc.StepBack());
        var confirm = UiKit.Button(BarStrings.Confirm, ButtonKind.Prominent, 12, null, () => _svc.ConfirmYes());
        confirm.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        outer.Child = grid;
        return outer;
    }

    private FrameworkElement NamingCard()
    {
        var entry = _svc.ModeEntry!;
        var stack = new StackPanel { Margin = new Thickness(17, 12, 17, 12) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(IconBox(entry));
        var title = Label(entry.Title, 13, ThemeKeys.TextPrimary, FontWeights.Medium);
        title.Margin = new Thickness(10, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        stack.Children.Add(row);
        var hint = Label(_svc.Warning ?? BarStrings.ArgumentHint, 10.5, _svc.Warning is null ? ThemeKeys.TextTertiary : ThemeKeys.Orange);
        hint.Margin = new Thickness(0, 6, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(hint);
        return stack;
    }

    private FrameworkElement CaptureCard()
    {
        var entry = _svc.ModeEntry!;
        var stack = new StackPanel { Margin = new Thickness(17, 12, 17, 12) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(IconBox(entry));
        var title = Label(entry.Title, 13, ThemeKeys.TextPrimary, FontWeights.Medium);
        title.Margin = new Thickness(10, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        if (_svc.Prefs.RowShortcuts().TryGetValue(entry.StableKey, out var current))
        {
            var cap = new Border
            {
                CornerRadius = new CornerRadius(100),
                Padding = new Thickness(7, 3, 7, 3),
                Background = AccentAlpha(0.14),
                Margin = new Thickness(10, 0, 0, 0),
                Child = Label(current.ToString(), 11, ThemeKeys.Accent, FontWeights.SemiBold, true),
            };
            row.Children.Add(cap);
        }

        stack.Children.Add(row);
        var hint = Label(_svc.Warning ?? BarStrings.ShortcutCaptureHint, 10.5, _svc.Warning is null ? ThemeKeys.TextTertiary : ThemeKeys.Orange);
        hint.Margin = new Thickness(0, 6, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(hint);
        return stack;
    }

    private void RenderActions()
    {
        var host = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        var header = Label(BarStrings.ActionsTitle.ToUpperInvariant(), 9, ThemeKeys.TextTertiary, FontWeights.Bold);
        header.Margin = new Thickness(8, 8, 8, 3);
        host.Children.Add(header);
        for (var i = 0; i < _svc.Actions.Count; i++)
        {
            var action = _svc.Actions[i];
            var selected = i == _svc.ActionSelected;
            var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 1, 0, 0), Cursor = Cursors.Hand };
            var destructive = action.Destructive;
            row.Background = selected ? (destructive ? new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0x45, 0x3A)) : AccentAlpha(0.14)) : Brushes.Transparent;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var plate = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Primary(0.06), Child = UiKit.Icon(action.Glyph, 13.5, destructive ? ThemeKeys.Red : ThemeKeys.TextPrimary, FontWeights.SemiBold) };
            grid.Children.Add(plate);
            var title = Label(action.Title, 13, destructive ? ThemeKeys.Red : ThemeKeys.TextPrimary, FontWeights.Medium);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 2);
            grid.Children.Add(title);
            var ret = Label("↵", 10, ThemeKeys.TextTertiary, FontWeights.SemiBold);
            ret.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            ret.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ret, 3);
            grid.Children.Add(ret);
            row.Child = grid;
            var index = i;
            row.MouseMove += (_, _) =>
            {
                if (_mouseMoved && _svc.ActionSelected != index)
                {
                    while (_svc.ActionSelected != index)
                    {
                        _svc.Move(index > _svc.ActionSelected ? 1 : -1);
                    }
                }
            };
            row.MouseLeftButtonUp += (_, _) => _svc.RunAction(action);
            host.Children.Add(row);
        }

        _body.Children.Add(host);
    }

    private FrameworkElement ChipRow(IReadOnlyList<string> chips)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 9, 16, 9) };
        row.Children.Add(Chip(null, Loc.T("All")));
        foreach (var raw in chips)
        {
            row.Children.Add(Chip(raw, BarSource.CategoryTitle(raw) is var t && raw == "menus" ? Loc.T("Menu commands") : t));
        }

        scroll.Content = row;
        return scroll;
    }

    private UIElement Chip(string? raw, string label)
    {
        var active = _svc.ActiveCategory == raw;
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        if (raw is not null)
        {
            var icon = UiKit.Icon(BarSource.Get(raw).Glyph, 8.5, active ? ThemeKeys.Accent : ThemeKeys.TextSecondary, FontWeights.SemiBold);
            icon.Margin = new Thickness(0, 0, 4, 0);
            inner.Children.Add(icon);
        }

        inner.Children.Add(Label(label, 10, active ? ThemeKeys.Accent : ThemeKeys.TextSecondary, active ? FontWeights.SemiBold : FontWeights.Normal));
        var chip = new Border
        {
            CornerRadius = new CornerRadius(100),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 5, 0),
            Child = inner,
            Cursor = Cursors.Hand,
            Background = active ? AccentAlpha(0.18) : Primary(0.06),
            BorderThickness = new Thickness(1),
            BorderBrush = active ? AccentAlpha(0.45) : Brushes.Transparent,
        };
        if (active && raw is not null)
        {
            chip.Loaded += (_, _) => chip.BringIntoView();
        }

        chip.MouseLeftButtonUp += (_, _) => _svc.SetCategory(raw);
        return chip;
    }

    private FrameworkElement TryRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 10, 16, 0) };
        row.Children.Add(Label(BarStrings.TryLabel.ToUpperInvariant(), 9, ThemeKeys.TextTertiary, FontWeights.Bold));
        ((TextBlock)row.Children[0]).Margin = new Thickness(0, 0, 8, 0);
        foreach (var example in _svc.ExampleChips)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(100),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 5, 0),
                Background = Primary(0.06),
                Cursor = Cursors.Hand,
                Child = Label(example, 10, ThemeKeys.TextSecondary, null, true),
            };
            var text = example;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _svc.SetQueryDirect(text);
                _field.Focus();
            };
            row.Children.Add(chip);
        }

        return row;
    }

    private FrameworkElement EmptyState()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center };
        var mark = BrandMark.Create(26);
        mark.Opacity = 0.3;
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(mark);
        var text = Label(BarStrings.NoResultsTitle, 12, ThemeKeys.TextSecondary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(text);
        var button = new Border
        {
            CornerRadius = new CornerRadius(100),
            Padding = new Thickness(10, 4, 10, 4),
            Background = AccentAlpha(0.12),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Label(BarStrings.NoResultsAction, 11, ThemeKeys.Accent, FontWeights.SemiBold),
        };
        button.MouseLeftButtonUp += (_, _) =>
        {
            _svc.SetCategory(null);
            _svc.SetQueryDirect("");
        };
        stack.Children.Add(button);
        return stack;
    }

    private FrameworkElement ListView()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        var index = 0;
        foreach (var item in _svc.Items)
        {
            if (item.Heading is { } heading)
            {
                var h = Label(heading.ToUpperInvariant(), 9, ThemeKeys.TextTertiary, FontWeights.Bold);
                h.Margin = new Thickness(8, 6, 8, 3);
                panel.Children.Add(h);
                continue;
            }

            var row = BuildRow(item.Entry!, index);
            panel.Children.Add(row.Row);
            _rowViews.Add(row);
            index++;
        }

        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = _listCeiling };
        if (_svc.Rows.Count > 14)
        {
            scroll.Height = _listCeiling;
        }

        _scroll = scroll;
        return scroll;
    }

    private FrameworkElement IconBox(BarEntry entry)
    {
        var host = new Grid { Width = 30, Height = 30 };
        host.Children.Add(IconVisual(entry));
        return host;
    }

    private UIElement IconVisual(BarEntry entry)
    {
        if (entry.Swatch is { } color)
        {
            return new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(0.5),
                BorderBrush = Primary(0.2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var image = entry.Icon ?? (entry.IconLoader is not null ? (entry.Icon = entry.IconLoader()) : null);
        if (image is not null)
        {
            return new Image { Source = image, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        var plate = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = entry.IsActive ? AccentAlpha(0.16) : Primary(0.06),
            Child = UiKit.Icon(entry.Glyph, 13.5, entry.IsActive ? ThemeKeys.Accent : ThemeKeys.TextPrimary, FontWeights.SemiBold),
        };
        if (entry.Glyph.Length > 0 && char.IsSurrogate(entry.Glyph[0]) || entry.Glyph.Length > 0 && entry.Glyph[0] > 0x2000 && entry.Glyph[0] < 0xE000)
        {
            plate.Child = new TextBlock { Text = entry.Glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji") };
        }

        return plate;
    }

    private (Border Row, TextBlock? CmdChip) BuildRow(BarEntry entry, int index)
    {
        var selected = index == _svc.Selected;
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, entry.IsAnswer ? 9 : 7, 9, entry.IsAnswer ? 9 : 7),
            Margin = new Thickness(0, 1, 0, 0),
            Background = selected ? AccentAlpha(0.14) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = entry,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center };
        iconHost.Children.Add(IconVisual(entry));
        if (entry.IsActive && !entry.IsAnswer)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, -2.5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ThemeKeys.Green);
            dot.Stroke = Brushes.Transparent;
            iconHost.Children.Add(dot);
        }

        grid.Children.Add(iconHost);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock title;
        if (entry.BigTitle)
        {
            title = Label(entry.Title, 17, ThemeKeys.TextPrimary, FontWeights.SemiBold, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            System.Windows.Documents.Typography.SetNumeralAlignment(title, FontNumeralAlignment.Tabular);
        }
        else
        {
            title = Label("", 13, ThemeKeys.TextPrimary, FontWeights.Medium);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            FillHighlighted(title, entry.Title, HighlightQuery(entry));
        }

        text.Children.Add(title);
        var subtitle = entry.NeedsSetupFeature is { } needs ? BarStrings.NeedsSetup(needs) : entry.Subtitle;
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = Label(subtitle, 10.5, entry.NeedsSetupFeature is null ? ThemeKeys.TextSecondary : ThemeKeys.Orange);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(sub);
        }

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        TextBlock? cmd = null;
        if (index < 9)
        {
            cmd = Label("Ctrl+" + (index + 1), 10, ThemeKeys.Accent, FontWeights.SemiBold, true);
            var chip = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2.5, 6, 2.5), Background = AccentAlpha(0.12), Child = cmd, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 4, 0), Tag = "cmd" };
            right.Children.Add(chip);
        }

        if (!string.IsNullOrEmpty(entry.AnswerValue))
        {
            var value = Label(entry.AnswerValue, 12, ThemeKeys.Accent, FontWeights.SemiBold, true);
            System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            value.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(value);
        }

        var shortcut = BarShortcutOf(entry);
        if (shortcut is not null)
        {
            var chip = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2.5, 6, 2.5), Background = Primary(0.06), Child = Label(shortcut, 10, ThemeKeys.TextSecondary, FontWeights.SemiBold, true), Margin = new Thickness(0, 0, 8, 0) };
            right.Children.Add(chip);
        }

        var ret = Label("↵", 10, ThemeKeys.TextTertiary, FontWeights.SemiBold);
        ret.Tag = "ret";
        ret.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        right.Children.Add(ret);
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);
        row.Child = grid;

        row.MouseMove += (_, _) =>
        {
            if (_mouseMoved)
            {
                _svc.SelectRow(index);
            }
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (row.Tag is BarEntry clicked && _svc.Mode == BarMode.Search)
            {
                _svc.Run(clicked);
            }
        };
        return (row, cmd);
    }

    private string? BarShortcutOf(BarEntry entry)
    {
        if (_svc.Prefs.RowShortcuts().TryGetValue(entry.StableKey, out var own))
        {
            return own.ToString();
        }

        return entry.Shortcut ?? entry.MenuShortcut;
    }

    private string HighlightQuery(BarEntry entry)
    {
        var q = _svc.Query.Trim();
        if (entry.IsAnswer)
        {
            return "";
        }

        if (q.StartsWith(':'))
        {
            q = q[1..].Trim();
        }

        if (entry.TakesArgument)
        {
            return entry.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? q : "";
        }

        return q;
    }

    private static void FillHighlighted(TextBlock block, string title, string query)
    {
        block.Inlines.Clear();
        var offsets = BarSearch.Highlight(title, query);
        if (offsets.Count == 0)
        {
            block.Inlines.Add(new System.Windows.Documents.Run(title));
            return;
        }

        var sb = new System.Text.StringBuilder();
        var on = false;
        void Flush()
        {
            if (sb.Length == 0)
            {
                return;
            }

            var run = new System.Windows.Documents.Run(sb.ToString());
            if (on)
            {
                run.FontWeight = FontWeights.Bold;
                run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeKeys.Accent);
            }

            block.Inlines.Add(run);
            sb.Clear();
        }

        for (var i = 0; i < title.Length; i++)
        {
            var h = offsets.Contains(i);
            if (h != on)
            {
                Flush();
                on = h;
            }

            sb.Append(title[i]);
        }

        Flush();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _rowViews.Count; i++)
        {
            var selected = i == _svc.Selected;
            var (row, _) = _rowViews[i];
            row.Background = selected ? AccentAlpha(0.14) : Brushes.Transparent;
            if (row.Child is Grid g)
            {
                foreach (var child in g.Children.OfType<StackPanel>())
                {
                    foreach (var tb in child.Children.OfType<TextBlock>().Where(t => (t.Tag as string) == "ret"))
                    {
                        tb.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        RefreshFooter();
        ScrollSelectedIntoView();
    }

    private void RefreshFooter()
    {
        if (_svc.Mode == BarMode.Search && !_svc.IsCollapsed)
        {
            _footerHost.Children.Clear();
            _footerHost.Children.Add(UiKit.Divider());
            _footerHost.Children.Add(Footer(true));
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (_svc.Selected >= 0 && _svc.Selected < _rowViews.Count)
        {
            _rowViews[_svc.Selected].Row.BringIntoView();
        }
    }

    private void UpdateCommandChips()
    {
        var held = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        foreach (var (row, _) in _rowViews)
        {
            if (row.Child is Grid g)
            {
                foreach (var chip in g.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Border>()).Where(b => (b.Tag as string) == "cmd"))
                {
                    chip.Visibility = held ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private FrameworkElement Footer(bool search)
    {
        // Wrap whole hints so long translations and narrow screens do not overlap the configured shortcut.
        var hints = new WrapPanel { Margin = new Thickness(16, 6, 16, 6) };
        void Hint(string text, bool rounded = true, double margin = 12)
        {
            var tb = Label(text, 10.5, ThemeKeys.TextSecondary, rounded ? FontWeights.SemiBold : null, rounded);
            tb.Margin = new Thickness(0, 2, margin, 2);
            hints.Children.Add(tb);
        }

        Hint(ShortcutText.Configured(_svc.Ctx, "commandBar"));
        if (search && _svc.CanOpenActions)
        {
            Hint("Ctrl+K " + BarStrings.ActionsTitle, false);
        }

        Hint(_svc.CategoryChips().Count > 0 ? Loc.T("Ctrl+P Ctrl+N ↑↓ ←→") : Loc.T("Ctrl+P Ctrl+N ↑↓"));
        if (search && _svc.CanTabComplete)
        {
            Hint("⇥ " + BarStrings.Continue, false);
        }

        Hint("↵ Esc", true, 0);
        return hints;
    }

    /// <summary>Lets the dialog code know the window must stay open (the capture card suspends hotkeys).</summary>
    public void SetCapturing(bool on) => _capturing = on;
}
