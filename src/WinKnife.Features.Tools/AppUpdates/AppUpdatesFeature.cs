// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal sealed record WingetUpdate(string Name, string Id, string Version, string Available, string Source);

internal static class Winget
{
    public static bool IsAvailable()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (File.Exists(Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe")))
            {
                return true;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(dir.Trim(), "winget.exe")))
                {
                    return true;
                }
            }
        }
        catch
        {
            // treat as missing
        }

        return false;
    }

    public static ProcessStartInfo Start(string arguments) => new("winget", arguments)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    /// <summary>Parses the fixed-width table printed by "winget upgrade".</summary>
    public static List<WingetUpdate> ParseUpgrade(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .ToList();
        var separator = lines.FindIndex(l => Regex.IsMatch(l.Trim(), "^-{5,}$"));
        var result = new List<WingetUpdate>();
        if (separator < 1)
        {
            return result;
        }

        var header = lines[separator - 1];
        var starts = Regex.Matches(header, @"\S+(?: \S+)*").Select(m => m.Index).ToList();
        if (starts.Count < 4)
        {
            return result;
        }

        string Cell(string line, int column)
        {
            var from = starts[column];
            if (from >= line.Length)
            {
                return string.Empty;
            }

            var to = column + 1 < starts.Count ? Math.Min(starts[column + 1], line.Length) : line.Length;
            return line[from..to].Trim();
        }

        for (var i = separator + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || Regex.IsMatch(line.Trim(), @"^\d+\s"))
            {
                break;
            }

            var id = Cell(line, 1);
            if (id.Length == 0)
            {
                continue;
            }

            result.Add(new WingetUpdate(Cell(line, 0), id, Cell(line, 2), Cell(line, 3), starts.Count > 4 ? Cell(line, 4) : string.Empty));
        }

        return result;
    }
}

/// <summary>Lists and installs app updates through the Windows Package Manager (winget).</summary>
public sealed class AppUpdatesFeature : ToolFeatureBase
{
    private AppUpdatesWindow? _window;
    private bool _wingetAvailable;

    public override string Id => "app-updates";

    public override string Name => "App updates";

    public override string Description => "See which installed apps have newer versions and update them with winget.";

    public override IReadOnlyList<FeatureAction> Actions => _wingetAvailable
        ? [new FeatureAction("open", "Check for app updates", () => Safe(Open), "")]
        : Array.Empty<FeatureAction>();

    protected override void OnStart()
    {
        _wingetAvailable = Winget.IsAvailable();
        if (_wingetAvailable)
        {
            BindHotkey("hotkey", null, Open);
        }
    }

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

        if (!_wingetAvailable)
        {
            return new SettingsViewBuilder(Ctx.Settings)
                .AddNote("The Windows Package Manager (winget) was not found on this PC, so app updates are unavailable. Install \"App Installer\" from the Microsoft Store to enable this feature.")
                .Build();
        }

        var open = UiKit.Btn("Check for updates", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open app updates", null, "Optional global shortcut.")
            .AddToggle("includeUnknown", "Include apps with unknown versions", false, "Passes --include-unknown to winget.");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new AppUpdatesWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class AppUpdatesWindow : Window
{
    private readonly IFeatureContext _ctx;
    private readonly DataGrid _grid = new();
    private readonly TextBox _log = new() { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), Height = 140 };
    private readonly TextBlock _status = UiKit.Muted(string.Empty);
    private readonly StackPanel _consent = new();
    private readonly DockPanel _main = new();
    private readonly Button _upgradeSelected;
    private readonly Button _upgradeAll;
    private readonly Button _check;
    private Process? _running;
    private bool _busy;

    public AppUpdatesWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "App updates";
        Width = 860;
        Height = 660;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(WingetUpdate.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding(nameof(WingetUpdate.Id)), Width = new DataGridLength(220) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Installed", Binding = new Binding(nameof(WingetUpdate.Version)), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Available", Binding = new Binding(nameof(WingetUpdate.Available)), Width = new DataGridLength(100) });

        _check = UiKit.Btn("Check again", () => _ = CheckAsync(), false, "");
        _upgradeSelected = UiKit.Btn("Update selected", () => _ = UpgradeAsync(selectedOnly: true), true, "");
        _upgradeAll = UiKit.Btn("Update all", () => _ = UpgradeAsync(selectedOnly: false));
        var cancel = UiKit.Btn("Stop", () =>
        {
            try
            {
                _running?.Kill(entireProcessTree: true);
            }
            catch
            {
                // already finished
            }
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
        buttons.Children.Add(_check);
        buttons.Children.Add(_upgradeSelected);
        buttons.Children.Add(_upgradeAll);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_status);

        DockPanel.SetDock(buttons, Dock.Top);
        DockPanel.SetDock(_log, Dock.Bottom);
        _main.Children.Add(buttons);
        _main.Children.Add(_log);
        _main.Children.Add(_grid);

        _consent.VerticalAlignment = VerticalAlignment.Center;
        _consent.HorizontalAlignment = HorizontalAlignment.Center;
        _consent.MaxWidth = 520;
        _consent.Children.Add(UiKit.Label("Before the first check", null, 20, FontWeights.SemiBold));
        var text = UiKit.Muted("App updates run the Windows Package Manager (winget). winget downloads package lists from Microsoft's community source, and it requires you to agree to the source terms (https://aka.ms/winget-agreements) before it will work. WinKnife passes --accept-source-agreements and --accept-package-agreements on your behalf only after you accept here. Individual installers may ask for administrator approval.");
        text.Margin = new Thickness(0, 8, 0, 16);
        _consent.Children.Add(text);
        var accept = UiKit.Btn("Accept and continue", () =>
        {
            _ctx.Settings.Set("agreementsAccepted", true);
            ShowMain();
            _ = CheckAsync();
        }, true);
        accept.HorizontalAlignment = HorizontalAlignment.Left;
        _consent.Children.Add(accept);

        var host = new Grid { Margin = new Thickness(16) };
        host.Children.Add(_main);
        host.Children.Add(_consent);
        Content = host;

        if (_ctx.Settings.Get("agreementsAccepted", false))
        {
            ShowMain();
            Loaded += async (_, _) => await CheckAsync();
        }
        else
        {
            _main.Visibility = Visibility.Collapsed;
        }

        Closed += (_, _) =>
        {
            try
            {
                if (_running is { HasExited: false })
                {
                    _running.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }
        };
    }

    private void ShowMain()
    {
        _consent.Visibility = Visibility.Collapsed;
        _main.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _check.IsEnabled = !busy;
        _upgradeSelected.IsEnabled = !busy;
        _upgradeAll.IsEnabled = !busy;
    }

    private void Append(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private async Task CheckAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        _status.Text = "Checking for updates...";
        _log.Clear();
        try
        {
            var extra = _ctx.Settings.Get("includeUnknown", false) ? " --include-unknown" : string.Empty;
            var info = Winget.Start("upgrade --accept-source-agreements --disable-interactivity" + extra);
            using var process = Process.Start(info)!;
            _running = process;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout;
            var updates = Winget.ParseUpgrade(output);
            _grid.ItemsSource = updates;
            _status.Text = updates.Count == 0 ? "Everything is up to date." : $"{updates.Count} update(s) available";
            if (updates.Count == 0)
            {
                Append(output.Trim());
                var err = (await stderr).Trim();
                if (err.Length > 0)
                {
                    Append(err);
                }
            }
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("winget upgrade failed: " + ex.Message);
            _status.Text = "Could not run winget: " + ex.Message;
        }
        finally
        {
            _running = null;
            SetBusy(false);
        }
    }

    private async Task UpgradeAsync(bool selectedOnly)
    {
        if (_busy)
        {
            return;
        }

        var targets = selectedOnly ? _grid.SelectedItems.OfType<WingetUpdate>().ToList() : (_grid.ItemsSource as List<WingetUpdate> ?? new());
        if (targets.Count == 0)
        {
            MessageBox.Show(this, selectedOnly ? "Select one or more apps first." : "There is nothing to update.", "App updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var names = string.Join("\r\n", targets.Take(12).Select(t => $"  - {t.Name} {t.Version} -> {t.Available}"));
        if (targets.Count > 12)
        {
            names += $"\r\n  ... and {targets.Count - 12} more";
        }

        if (MessageBox.Show(this, $"Update {targets.Count} app(s)?\r\n\r\n{names}\r\n\r\nSome apps close while updating and installers may ask for permission.", "App updates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _log.Clear();
        try
        {
            var failures = 0;
            foreach (var target in targets)
            {
                _status.Text = $"Updating {target.Name}...";
                var code = await RunStreamedAsync($"upgrade --id \"{target.Id}\" --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity");
                if (code != 0)
                {
                    failures++;
                }
            }

            _status.Text = failures == 0 ? "Updates finished." : $"Finished with {failures} failure(s). See the log.";
            _ctx.Notifier.Show("App updates", _status.Text, failures == 0 ? NotificationLevel.Info : NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            _status.Text = "Update failed: " + ex.Message;
        }
        finally
        {
            _running = null;
            SetBusy(false);
        }

        await CheckAsync();
    }

    private async Task<int> RunStreamedAsync(string arguments)
    {
        Append("> winget " + arguments);
        using var process = new Process { StartInfo = Winget.Start(arguments) };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                var clean = line.Contains('\r') ? line[(line.LastIndexOf('\r') + 1)..] : line;
                if (clean.Trim().Length > 1 && !Regex.IsMatch(clean.Trim(), @"^[-\\|/]$"))
                {
                    Dispatcher.BeginInvoke(() => Append(clean));
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                Dispatcher.BeginInvoke(() => Append(line));
            }
        };
        process.Start();
        _running = process;
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        Append($"(exit code {process.ExitCode})");
        return process.ExitCode;
    }
}
