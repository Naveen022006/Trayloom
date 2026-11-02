// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>The 460 px "Open apps" window (spec 2.3.2 / 2.3.3): rows, batch buttons, one highlight, keyboard model.</summary>
internal sealed class QuitAppsWindow : GlassWindow
{
    private const double RowHeight = 42;
    private const int MaxRows = 8;

    private static QuitAppsWindow? _current;

    private readonly IContributionServices _ctx;
    private readonly Func<List<QuitApp>>? _loader;
    private readonly TextBlock _count;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _rows = new() { Margin = new Thickness(8, 0, 8, 0) };
    private readonly Border _listHost = new();
    private readonly TextBlock _empty;
    private readonly Border _buttonBar = new() { Padding = new Thickness(16, 12, 16, 12) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<int, DateTime> _quitting = [];
    private OutsideClickMonitor? _outside;
    private ForegroundWatcher? _foreground;
    private List<QuitApp> _apps = [];
    private List<string> _buttons = ["cancel"];
    private bool _focusIsRow;
    private int _focusPid;
    private int _focusButton;
    private string? _exceptFrontName;
    private string _typed = string.Empty;
    private DateTime _lastType = DateTime.MinValue;
    private bool _shown;

    private QuitAppsWindow(IContributionServices ctx, bool exceptFront, int frontPid, Func<List<QuitApp>>? loader)
        : base(22)
    {
        _ctx = ctx;
        _loader = loader;
        ExceptScope = exceptFront;
        FrontPid = frontPid;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        Title = "WinKnife";

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(17, 14, 17, 8) };
        header.Children.Add(UiKit.Text("Open apps", 13, FontWeights.SemiBold));
        _count = UiKit.Text("0", 13, FontWeights.Medium, ThemeKeys.TextTertiary);
        _count.FontFamily = UiKit.DisplayFont;
        _count.Margin = new Thickness(6, 0, 0, 0);
        header.Children.Add(_count);

        _empty = UiKit.Text("No other apps are open.", 12.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _scroll.Content = _rows;
        _listHost.Child = _scroll;

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(_listHost);
        var divider = UiKit.Divider();
        divider.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(divider);
        root.Children.Add(_buttonBar);
        Body = root;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnTextInput;
        _timer.Tick += (_, _) => Reload();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _outside?.Dispose();
            _foreground?.Dispose();
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };
    }

    /// <summary>The open window (tests).</summary>
    internal static QuitAppsWindow? Current => _current;

    private int FrontPid { get; set; }

    private bool ExceptScope { get; set; }

    /// <summary>Opens (or refreshes) the window. <paramref name="loader"/> is a test seam that replaces the process listing (no quitting happens then).</summary>
    public static void Open(IContributionServices ctx, bool exceptFront, Func<List<QuitApp>>? loader = null, int? frontPid = null)
    {
        var front = frontPid ?? QuitAppsModel.ForegroundPid();
        if (_current is { } existing)
        {
            existing.Restart(exceptFront, front);
            return;
        }

        var window = new QuitAppsWindow(ctx, exceptFront, front, loader);
        _current = window;
        window.Present();
    }

    /// <summary>Closes the window when open.</summary>
    public static void CloseCurrent() => _current?.Close();

    private void Restart(bool exceptFront, int front)
    {
        ExceptScope = exceptFront;
        FrontPid = front;
        Reload(resetFocus: true);
        Activate();
    }

    private void Present()
    {
        Reload(resetFocus: true);
        Plate.Opacity = 0;
        var body = Body!;
        body.Measure(new Size(460, double.PositiveInfinity));
        var area = Screens.UnderCursor().WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var h = body.DesiredSize.Height * dpi;
        var left = area.Left + (area.Width - 460 * dpi) / 2;
        var top = area.Top + area.Height / 2 - h / 2 - area.Height * 0.12;
        top = Math.Max(area.Top + 16, Math.Min(top, area.Bottom - h - 16));
        Plat.PlaceWindow(this, left, top, 460, 0);
        Show();
        Activate();
        Plate.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _shown = true;
        _timer.Start();
        if (_loader is null)
        {
            _outside = new OutsideClickMonitor(_ctx.Hooks, _ctx.Ui, FrameBounds, Close);
            _foreground = new ForegroundWatcher(_ctx.Ui);
            _foreground.ForegroundChanged += hwnd =>
            {
                if (_shown && QuitAppsModel.OwnerPid(hwnd) != Environment.ProcessId)
                {
                    Close();
                }
            };
        }
    }

    private Rect FrameBounds()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return handle != 0 && NativeMethods.GetWindowRect(handle, out var r) ? new Rect(r.Left, r.Top, r.Width, r.Height) : Rect.Empty;
    }

    // ---------------------------------------------------------------- data

    private void Reload(bool resetFocus = false)
    {
        var oldIndex = _apps.FindIndex(a => a.Pid == _focusPid);
        _apps = _loader?.Invoke() ?? QuitAppsModel.Load(FrontPid, QuitAppsModel.Kept(_ctx.App));
        var now = DateTime.UtcNow;
        foreach (var pid in _quitting.Keys.ToList())
        {
            if (now - _quitting[pid] > TimeSpan.FromSeconds(4))
            {
                _quitting.Remove(pid);
            }
        }

        var front = _apps.FirstOrDefault(a => a.IsFront);
        _exceptFrontName = front is { IsKept: false } && QuitAppsModel.Targets(_apps, front.Pid).Any() ? front.Name : null;
        _buttons = ["cancel"];
        if (_exceptFrontName is not null)
        {
            _buttons.Add("exceptFront");
        }

        if (QuitAppsModel.Targets(_apps).Any())
        {
            _buttons.Add("all");
        }

        var resting = RestingButton();
        if (resetFocus)
        {
            _focusIsRow = false;
            _focusButton = resting;
        }
        else if (_focusIsRow && !_apps.Any(a => a.Pid == _focusPid))
        {
            if (_apps.Count == 0)
            {
                _focusIsRow = false;
                _focusButton = resting;
            }
            else
            {
                _focusPid = _apps[Math.Clamp(oldIndex, 0, _apps.Count - 1)].Pid;
            }
        }
        else if (!_focusIsRow && _focusButton >= _buttons.Count)
        {
            _focusButton = resting;
        }

        Render();
    }

    private int RestingButton()
    {
        if (!_apps.Any(a => a.IsFront))
        {
            return 0;
        }

        if (ExceptScope && _buttons.Contains("exceptFront"))
        {
            return _buttons.IndexOf("exceptFront");
        }

        var all = _buttons.IndexOf("all");
        return all >= 0 ? all : 0;
    }

    // ---------------------------------------------------------------- view

    private void Render()
    {
        _count.Text = _apps.Count.ToString();
        _rows.Children.Clear();
        if (_apps.Count == 0)
        {
            _listHost.Child = new Border { MinHeight = 56, Child = _empty };
        }
        else
        {
            _listHost.Child = _scroll;
            _scroll.Height = RowHeight * Math.Min(_apps.Count, MaxRows);
            foreach (var app in _apps)
            {
                _rows.Children.Add(BuildRow(app));
            }
        }

        BuildButtons();
        if (_focusIsRow)
        {
            Dispatcher.BeginInvoke(() =>
            {
                foreach (FrameworkElement row in _rows.Children)
                {
                    if (row.Tag is int pid && pid == _focusPid)
                    {
                        row.BringIntoView();
                    }
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private FrameworkElement BuildRow(QuitApp app)
    {
        var focused = _focusIsRow && _focusPid == app.Pid;
        var row = new Border
        {
            Height = RowHeight,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 0, 9, 0),
            Tag = app.Pid,
            Background = focused ? Accent(0.14) : Brushes.Transparent,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Width = 28, Height = 28, Source = Plat.ShellImage(app.Path, 28), VerticalAlignment = VerticalAlignment.Center });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var name = UiKit.Text(app.Name, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.MaxWidth = 230;
        left.Children.Add(name);
        if (app.IsFront)
        {
            left.Children.Add(Badge("Front", null));
        }

        if (app.IsKept)
        {
            left.Children.Add(Badge("Kept", "Quit all leaves these apps running. File Explorer, WinKnife and Phone Link are always kept. You can still quit a kept app from its own row."));
        }

        Grid.SetColumn(left, 1);
        grid.Children.Add(left);

        FrameworkElement trailing;
        if (_quitting.ContainsKey(app.Pid))
        {
            trailing = new ProgressBar { IsIndeterminate = true, Width = 28, Height = 4, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var x = UiKit.Icon(Glyphs.CloseCircle, 15, ThemeKeys.TextSecondary);
            x.Opacity = focused ? 1 : 0;
            x.IsHitTestVisible = focused;
            x.Cursor = Cursors.Hand;
            x.ToolTip = $"Quit {app.Name}";
            x.MouseEnter += (_, _) => x.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
            x.MouseLeave += (_, _) => x.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            x.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                QuitOne(app);
            };
            trailing = x;
        }

        trailing.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        row.Child = grid;
        row.MouseEnter += (_, _) => FocusRow(app.Pid);
        row.MouseLeftButtonUp += (_, _) => FocusRow(app.Pid);
        return row;
    }

    private static Border Badge(string text, string? tip) => new()
    {
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(6, 2, 6, 2),
        Margin = new Thickness(7, 0, 0, 0),
        Child = UiKit.Text(text, 10, FontWeights.Medium, ThemeKeys.TextSecondary),
        ToolTip = tip,
        Background = Tint(0.08),
    };

    private void BuildButtons()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _buttons.Count; i++)
        {
            var id = _buttons[i];
            var (title, keys) = id switch
            {
                "cancel" => ("Cancel", "Esc"),
                "exceptFront" => ($"Quit all except {_exceptFrontName}", "Ctrl+Alt+↵"),
                _ => ("Quit all", "Ctrl+↵"),
            };
            var index = i;
            var button = MakeButton(title, keys, !_focusIsRow && _focusButton == i);
            button.MouseEnter += (_, _) =>
            {
                if (_focusIsRow || _focusButton != index)
                {
                    _focusIsRow = false;
                    _focusButton = index;
                    Render();
                }
            };
            button.MouseLeftButtonUp += (_, _) => ActivateButton(id);
            if (id == "cancel")
            {
                Grid.SetColumn(button, 0);
                grid.Children.Add(button);
            }
            else
            {
                button.Margin = new Thickness(right.Children.Count > 0 ? 8 : 0, 0, 0, 0);
                right.Children.Add(button);
            }
        }

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        _buttonBar.Child = grid;
    }

    private static Border MakeButton(string title, string keys, bool prominent)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var brush = prominent ? ThemeKeys.OnAccent : ThemeKeys.TextPrimary;
        var k = UiKit.Text(keys, 10, FontWeights.Medium, brush);
        k.FontFamily = UiKit.DisplayFont;
        k.Opacity = 0.6;
        k.Margin = new Thickness(6, 0, 0, 0);
        var label = UiKit.Text(title, 12, FontWeights.Normal, brush, trimming: TextTrimming.CharacterEllipsis);
        label.MaxWidth = 140;
        content.Children.Add(label);
        content.Children.Add(k);
        var border = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 6, 9, 6),
            Cursor = Cursors.Hand,
            Child = content,
            BorderThickness = new Thickness(1),
        };
        border.SetResourceReference(Border.BackgroundProperty, prominent ? ThemeKeys.Accent : ThemeKeys.ControlFill);
        border.SetResourceReference(Border.BorderBrushProperty, prominent ? ThemeKeys.Accent : ThemeKeys.CardBorder);
        return border;
    }

    private static Brush Accent(double alpha) => new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), ThemeManager.AccentColor.R, ThemeManager.AccentColor.G, ThemeManager.AccentColor.B));

    private static Brush Tint(double alpha) => new SolidColorBrush(ThemeManager.IsLight ? Color.FromArgb((byte)(alpha * 255), 0, 0, 0) : Color.FromArgb((byte)(alpha * 255), 255, 255, 255));

    // ---------------------------------------------------------------- actions

    private void FocusRow(int pid)
    {
        if (_focusIsRow && _focusPid == pid)
        {
            return;
        }

        _focusIsRow = true;
        _focusPid = pid;
        Render();
    }

    private void QuitOne(QuitApp app)
    {
        if (_loader is not null)
        {
            return;
        }

        if (QuitAppsModel.SoftQuit(app))
        {
            _quitting[app.Pid] = DateTime.UtcNow;
            Render();
        }
    }

    private void ActivateButton(string id)
    {
        switch (id)
        {
            case "cancel":
                Close();
                break;
            case "exceptFront":
                QuitBatch(_apps.FirstOrDefault(a => a.IsFront)?.Pid ?? 0);
                break;
            default:
                QuitBatch(0);
                break;
        }
    }

    private void QuitBatch(int keepingPid)
    {
        if (_loader is not null)
        {
            return;
        }

        var targets = QuitAppsModel.Targets(_apps, keepingPid).ToList();
        Close();
        _ctx.Ui.BeginInvoke(() =>
        {
            foreach (var app in targets)
            {
                QuitAppsModel.SoftQuit(app);
            }
        });
    }

    private void ActivateFocus()
    {
        if (_focusIsRow)
        {
            if (_apps.FirstOrDefault(a => a.Pid == _focusPid) is { } app)
            {
                QuitOne(app);
            }
        }
        else if (_focusButton < _buttons.Count)
        {
            ActivateButton(_buttons[_focusButton]);
        }
    }

    // ---------------------------------------------------------------- keys

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                return;
            case Key.Return:
                e.Handled = true;
                if (e.IsRepeat)
                {
                    return;
                }

                if (ctrl && alt)
                {
                    if (_buttons.Contains("exceptFront"))
                    {
                        ActivateButton("exceptFront");
                    }
                }
                else if (ctrl)
                {
                    if (_buttons.Contains("all"))
                    {
                        ActivateButton("all");
                    }
                }
                else
                {
                    ActivateFocus();
                }

                return;
            case Key.Delete or Key.Back when ctrl:
                e.Handled = true;
                if (!e.IsRepeat && _focusIsRow && _apps.FirstOrDefault(a => a.Pid == _focusPid) is { } victim)
                {
                    QuitOne(victim);
                }

                return;
            case Key.Up:
                e.Handled = true;
                Move(-1, 0);
                return;
            case Key.Down:
                e.Handled = true;
                Move(1, 0);
                return;
            case Key.Left:
                e.Handled = true;
                Move(0, -1);
                return;
            case Key.Right:
                e.Handled = true;
                Move(0, 1);
                return;
            case Key.Tab:
                e.Handled = true;
                if (_focusIsRow)
                {
                    _focusIsRow = false;
                    _focusButton = RestingButton();
                }
                else if (_buttons.Count > 0)
                {
                    _focusButton = (_focusButton + (shift ? _buttons.Count - 1 : 1)) % _buttons.Count;
                }

                Render();
                return;
        }

        // Every other key is consumed except printable characters (type-to-jump is handled in PreviewTextInput).
        var printable = !ctrl && !alt && ((key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || key == Key.Space);
        e.Handled = !printable;
    }

    private void Move(int vertical, int horizontal)
    {
        if (_focusIsRow)
        {
            var index = _apps.FindIndex(a => a.Pid == _focusPid);
            if (vertical < 0 && index > 0)
            {
                _focusPid = _apps[index - 1].Pid;
            }
            else if (vertical > 0)
            {
                if (index + 1 < _apps.Count)
                {
                    _focusPid = _apps[index + 1].Pid;
                }
                else
                {
                    _focusIsRow = false;
                    _focusButton = RestingButton();
                }
            }
        }
        else if (vertical < 0 && _apps.Count > 0)
        {
            _focusIsRow = true;
            _focusPid = _apps[^1].Pid;
        }
        else if (horizontal != 0)
        {
            _focusButton = Math.Clamp(_focusButton + horizontal, 0, _buttons.Count - 1);
        }

        Render();
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        e.Handled = true;
        var now = DateTime.UtcNow;
        if ((now - _lastType).TotalSeconds > 1)
        {
            _typed = string.Empty;
        }

        _lastType = now;
        _typed += e.Text;
        var hit = _apps.FirstOrDefault(a => a.Name.StartsWith(_typed, StringComparison.CurrentCultureIgnoreCase));
        if (hit is not null)
        {
            FocusRow(hit.Pid);
        }
    }
}
