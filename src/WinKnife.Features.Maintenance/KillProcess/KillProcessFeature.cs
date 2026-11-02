// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Process termination helpers shared by the process list and the port manager.</summary>
internal static class ProcessKiller
{
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "memory compression", "secure system", "system idle process",
    };

    /// <summary>Returns a reason the process must not be killed, or null when it may be.</summary>
    public static string? Refuse(int pid, string name)
    {
        if (pid <= 4)
        {
            return "This is a core Windows process and cannot be ended.";
        }

        if (pid == Environment.ProcessId)
        {
            return "That is WinKnife itself. Quit it from the tray menu instead.";
        }

        var bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return Critical.Contains(bare) ? $"\"{bare}\" is a critical Windows process; ending it would crash or sign you out of Windows." : null;
    }

    /// <summary>Ends a process (optionally with its children). Returns an error message, or null on success.</summary>
    public static string? Kill(int pid, string name, bool tree)
    {
        if (Refuse(pid, name) is { } reason)
        {
            return reason;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(tree);
            process.WaitForExit(3000);
            return null;
        }
        catch (ArgumentException)
        {
            return null; // already gone
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return $"Access denied for \"{name}\". Run WinKnife as administrator to end protected or elevated processes.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Ends a process and starts its executable again (command-line arguments are not preserved).</summary>
    public static string? Restart(int pid, string name)
    {
        var path = N.ProcessPath(pid);
        if (path is null)
        {
            return $"The location of \"{name}\" could not be read, so it cannot be restarted.";
        }

        if (Kill(pid, name, tree: false) is { } error)
        {
            return error;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty });
            return null;
        }
        catch (Exception ex)
        {
            return "The process ended but could not be relaunched: " + ex.Message;
        }
    }
}

internal sealed class ProcRow : INotifyPropertyChanged
{
    private double _cpu;
    private long _memory;

    public int Pid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public double Cpu
    {
        get => _cpu;
        set
        {
            if (Math.Abs(_cpu - value) > 0.05)
            {
                _cpu = value;
                Raise();
                Raise(nameof(CpuText));
            }
        }
    }

    public long MemoryBytes
    {
        get => _memory;
        set
        {
            if (_memory != value)
            {
                _memory = value;
                Raise();
                Raise(nameof(MemoryText));
            }
        }
    }

    public string CpuText => Cpu < 0.05 ? "0" : Cpu.ToString("0.0");

    public string MemoryText => UiKit.FormatBytes(MemoryBytes);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A searchable process list with end, end-tree and restart.</summary>
public sealed class KillProcessFeature : ToolFeatureBase
{
    private KillProcessWindow? _window;

    public override string Id => "kill-process";

    public override string Name => "Kill process";

    public override string Description => "Find any running process by name or ID and end it, with its children or restart it.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Kill process...", () => Safe(Open), ""),
        new FeatureAction("force-quit", "Force quit the active app", () => Safe(ForceQuitForeground), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Open);

    protected override void OnStop()
    {
        _window?.Close();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var open = UiKit.Btn("Open process list", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open process list", null, "Optional global shortcut.")
            .AddNote("Critical Windows processes are protected. Elevated processes need WinKnife to run as administrator.");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new KillProcessWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }

    private void ForceQuitForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return;
        }

        string name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch
        {
            return;
        }

        if (ProcessKiller.Refuse((int)pid, name) is { } reason)
        {
            Ctx.Notifier.Show(Name, reason, NotificationLevel.Warning);
            return;
        }

        var answer = MessageBox.Show($"Force quit \"{name}\" (PID {pid}) and its child processes?\r\n\r\nUnsaved work in that app will be lost.", "Force quit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var error = ProcessKiller.Kill((int)pid, name, tree: true);
        Ctx.Notifier.Show(Name, error ?? $"\"{name}\" was ended.", error is null ? NotificationLevel.Info : NotificationLevel.Error);
    }
}

internal sealed class KillProcessWindow : Window
{
    private readonly IFeatureContext _ctx;
    private readonly Dictionary<int, ProcRow> _rows = new();
    private readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<ProcRow> _items = new();
    private readonly ListCollectionView _view;
    private readonly DataGrid _grid = new();
    private readonly TextBox _search = new() { MinWidth = 260 };
    private readonly TextBlock _status = UiKit.Muted(string.Empty);
    private readonly DispatcherTimer _timer;
    private DateTime _lastSample = DateTime.UtcNow;
    private bool _refreshing;

    public KillProcessWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Kill process";
        Width = 860;
        Height = 600;
        MinWidth = 560;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _view = new ListCollectionView(_items)
        {
            Filter = o => Matches((ProcRow)o),
            IsLiveSorting = true,
        };
        _view.LiveSortingProperties.Add(nameof(ProcRow.MemoryBytes));
        _view.LiveSortingProperties.Add(nameof(ProcRow.Cpu));
        _view.SortDescriptions.Add(new SortDescription(nameof(ProcRow.MemoryBytes), ListSortDirection.Descending));

        _grid.ItemsSource = _view;
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.CanUserAddRows = false;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(ProcRow.Name)), Width = new DataGridLength(200) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PID", Binding = new Binding(nameof(ProcRow.Pid)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "CPU %", Binding = new Binding(nameof(ProcRow.CpuText)), SortMemberPath = nameof(ProcRow.Cpu), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Memory", Binding = new Binding(nameof(ProcRow.MemoryText)), SortMemberPath = nameof(ProcRow.MemoryBytes), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Path", Binding = new Binding(nameof(ProcRow.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.ContextMenu = BuildMenu();
        _grid.MouseDoubleClick += (_, _) => EndSelected(tree: false);
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                EndSelected(tree: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
        };

        _search.TextChanged += (_, _) => _view.Refresh();
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        searchRow.Children.Add(UiKit.Glyph("", 14, ThemeKeys.TextMuted));
        _search.Margin = new Thickness(8, 0, 0, 0);
        searchRow.Children.Add(_search);
        searchRow.Children.Add(new Border { Width = 16 });
        searchRow.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(UiKit.Btn("End process", () => EndSelected(false), true, ""));
        buttons.Children.Add(UiKit.Btn("End process tree", () => EndSelected(true)));
        buttons.Children.Add(UiKit.Btn("Restart", RestartSelected, false, ""));
        buttons.Children.Add(UiKit.Btn("Open file location", OpenLocation));

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(searchRow);
        layout.Children.Add(buttons);
        layout.Children.Add(_grid);
        Content = layout;

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshAsync(), Dispatcher);
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
            _search.Focus();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private bool Matches(ProcRow row)
    {
        var q = _search.Text.Trim();
        return q.Length == 0
            || row.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Pid.ToString().Contains(q)
            || row.Path.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("End process", () => EndSelected(false));
        Add("End process tree", () => EndSelected(true));
        Add("Restart", RestartSelected);
        menu.Items.Add(new Separator());
        Add("Open file location", OpenLocation);
        Add("Copy path", () =>
        {
            if (_grid.SelectedItem is ProcRow row && row.Path.Length > 0)
            {
                Clipboard.SetText(row.Path);
            }
        });
        return menu;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var previous = new Dictionary<int, TimeSpan>(_lastCpu);
            var sampleStart = _lastSample;
            var knownPaths = _rows.ToDictionary(r => r.Key, r => r.Value.Path);
            var snapshot = await Task.Run(() => Sample(previous, knownPaths));
            var elapsed = Math.Max(0.5, (DateTime.UtcNow - sampleStart).TotalSeconds);
            _lastSample = DateTime.UtcNow;
            var cores = Environment.ProcessorCount;

            var alive = new HashSet<int>();
            foreach (var s in snapshot)
            {
                alive.Add(s.Pid);
                var cpu = 0.0;
                if (previous.TryGetValue(s.Pid, out var before) && s.Cpu is { } now)
                {
                    cpu = Math.Clamp((now - before).TotalSeconds / (elapsed * cores) * 100, 0, 100);
                }

                if (s.Cpu is { } total)
                {
                    _lastCpu[s.Pid] = total;
                }

                if (!_rows.TryGetValue(s.Pid, out var row))
                {
                    row = new ProcRow { Pid = s.Pid, Name = s.Name, Path = s.Path };
                    _rows[s.Pid] = row;
                    _items.Add(row);
                }
                else if (row.Path.Length == 0 && s.Path.Length > 0)
                {
                    row.Path = s.Path;
                }

                row.MemoryBytes = s.Memory;
                row.Cpu = cpu;
            }

            foreach (var pid in _rows.Keys.Where(p => !alive.Contains(p)).ToList())
            {
                _items.Remove(_rows[pid]);
                _rows.Remove(pid);
                _lastCpu.Remove(pid);
            }

            _status.Text = $"{_rows.Count} processes";
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("Process refresh failed: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private sealed record Sampled(int Pid, string Name, long Memory, TimeSpan? Cpu, string Path);

    private static List<Sampled> Sample(Dictionary<int, TimeSpan> previous, Dictionary<int, string> knownPaths)
    {
        var list = new List<Sampled>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    TimeSpan? cpu = null;
                    try
                    {
                        cpu = p.TotalProcessorTime;
                    }
                    catch
                    {
                        // protected process
                    }

                    var path = knownPaths.TryGetValue(p.Id, out var known) ? known : N.ProcessPath(p.Id) ?? string.Empty;
                    list.Add(new Sampled(p.Id, p.ProcessName, p.WorkingSet64, cpu, path));
                }
                catch
                {
                    // process exited while sampling
                }
            }
        }

        return list;
    }

    private List<ProcRow> Selected() => _grid.SelectedItems.OfType<ProcRow>().ToList();

    private void EndSelected(bool tree)
    {
        var rows = Selected();
        if (rows.Count == 0)
        {
            return;
        }

        var what = rows.Count == 1 ? $"\"{rows[0].Name}\" (PID {rows[0].Pid})" : $"{rows.Count} selected processes";
        var text = tree ? $"End {what} and everything it started?" : $"End {what}?";
        if (MessageBox.Show(this, text + "\r\n\r\nUnsaved data in them will be lost.", "End process", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var errors = new List<string>();
        foreach (var row in rows)
        {
            if (ProcessKiller.Kill(row.Pid, row.Name, tree) is { } error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\r\n", errors.Distinct()), "Kill process", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _ = RefreshAsync();
    }

    private void RestartSelected()
    {
        if (Selected() is not [var row])
        {
            MessageBox.Show(this, "Select a single process to restart.", "Restart", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Restart \"{row.Name}\"?\r\n\r\nIt is ended and started again from its file; command-line arguments are not preserved.", "Restart", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (ProcessKiller.Restart(row.Pid, row.Name) is { } error)
        {
            MessageBox.Show(this, error, "Restart", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _ = RefreshAsync();
    }

    private void OpenLocation()
    {
        if (_grid.SelectedItem is ProcRow { Path.Length: > 0 } row)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
        }
    }
}
