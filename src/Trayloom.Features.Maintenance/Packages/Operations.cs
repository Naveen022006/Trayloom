// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

internal enum PkgAction
{
    Install,
    Uninstall,
    Upgrade,
    UpgradeAll,
    UpgradeMany,
    UpdateSources,
}

internal enum OpState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    NeedsTerminal,
}

internal enum OpPhase
{
    Preparing,
    Downloading,
    Installing,
    Uninstalling,
    Upgrading,
    Refreshing,
    Finalizing,
}

/// <summary>Observable state of the single running (or just finished) winget operation.</summary>
internal sealed class OperationStatus
{
    public PkgAction Action { get; init; }

    /// <summary>Package display name ("packages" for all-package operations).</summary>
    public string Target { get; init; } = string.Empty;

    public string? TargetId { get; init; }

    public OpState State { get; set; } = OpState.Running;

    public OpPhase Phase { get; set; } = OpPhase.Preparing;

    public double? Fraction { get; set; }

    public string? Activity { get; set; }

    public DateTime Started { get; } = DateTime.Now;

    public DateTime? Finished { get; set; }

    /// <summary>Command to hand to an elevated terminal when the operation needs administrator approval.</summary>
    public string? Command { get; set; }

    /// <summary>The argument lists behind <see cref="Command"/>; the terminal button rebuilds its command from these, never from the display text.</summary>
    public IReadOnlyList<string[]> CommandArgs { get; set; } = [];

    public StringBuilder Log { get; } = new();

    public string? Error { get; set; }

    /// <summary>The output asked for source agreements (trust card).</summary>
    public string? PendingAgreementSource { get; set; }

    public bool IsRunning => State == OpState.Running;

    public string Title => State switch
    {
        OpState.Running => Action switch
        {
            PkgAction.Install => Loc.F("Installing {0}", Target),
            PkgAction.Uninstall => Loc.F("Uninstalling {0}", Target),
            PkgAction.Upgrade => Loc.F("Updating {0}", Target),
            PkgAction.UpdateSources => Loc.T("Updating winget"),
            _ => Loc.T("Updating packages"),
        },
        OpState.Succeeded => Action switch
        {
            PkgAction.Install => Loc.F("{0} installed.", Target),
            PkgAction.Uninstall => Loc.F("{0} uninstalled.", Target),
            PkgAction.Upgrade => Loc.F("{0} updated.", Target),
            PkgAction.UpdateSources => Loc.T("winget updated."),
            _ => Loc.T("Packages updated."),
        },
        OpState.Cancelled => Loc.T("Operation cancelled."),
        OpState.NeedsTerminal => Loc.T("Continue in your terminal."),
        _ => Loc.F("Could not finish {0}.", Target),
    };

    public string PhaseText => Phase switch
    {
        OpPhase.Downloading => Loc.T("Downloading files…"),
        OpPhase.Installing => Loc.T("Installing files…"),
        OpPhase.Uninstalling => Loc.T("Removing files…"),
        OpPhase.Upgrading => Loc.T("Updating files…"),
        OpPhase.Refreshing => Loc.T("Refreshing list…"),
        OpPhase.Finalizing => Loc.T("Finishing…"),
        _ => Loc.T("Preparing…"),
    };
}

/// <summary>A winget operation to run: one or more commands run one after the other as a single status.</summary>
internal sealed record OperationRequest(PkgAction Action, string Target, string? TargetId, IReadOnlyList<string[]> Commands);

/// <summary>
/// The single operation lane of the package manager, shared by the Packages page, App updates and the Uninstaller
/// (spec 7.4.8): one operation at a time, streamed output, progress/phase/activity parsing, cancel and auto-clear.
/// </summary>
internal sealed class WingetOperations
{
    private static readonly Regex Percent = new(@"([0-9]{1,3}(?:\.[0-9]+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex Bytes = new(@"([0-9]+(?:[.,][0-9]+)?)\s*(B|KB|MB|GB)\s*/\s*([0-9]+(?:[.,][0-9]+)?)\s*(B|KB|MB|GB)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Ansi = new(@"\u001B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private Process? _process;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _clearTimer;

    public static WingetOperations Shared { get; } = new();

    public OperationStatus? Status { get; private set; }

    public bool IsBusy => Status is { IsRunning: true };

    /// <summary>Raised on the UI thread whenever the status changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when an operation ended: (request, final state).</summary>
    public event Action<OperationRequest, OpState>? Finished;

    private static Dispatcher Ui => System.Windows.Application.Current.Dispatcher;

    /// <summary>Starts an operation; ignored while another one runs.</summary>
    public bool Start(OperationRequest request, bool acceptAgreements = true)
    {
        if (IsBusy || !WingetCli.IsAvailable)
        {
            return false;
        }

        _clearTimer?.Stop();
        var status = new OperationStatus
        {
            Action = request.Action,
            Target = request.Target,
            TargetId = request.TargetId,
            Phase = request.Action == PkgAction.Uninstall ? OpPhase.Uninstalling : OpPhase.Preparing,
        };
        status.CommandArgs = request.Commands;
        status.Command = string.Join(" ; ", request.Commands.Select(c => "winget " + string.Join(' ', c.Select(Quote))));
        Status = status;
        _cts = new CancellationTokenSource();
        Raise();
        _ = Task.Run(() => Execute(request, status, _cts.Token));
        return true;
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private async Task Execute(OperationRequest request, OperationStatus status, CancellationToken ct)
    {
        var failedCommands = 0;
        var output = new StringBuilder();
        try
        {
            foreach (var command in request.Commands)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var exit = await RunStreaming(command, status, output, ct).ConfigureAwait(false);
                var ok = exit == 0 || exit == WingetCli.NoApplicableUpdate;
                if (!ok && !ct.IsCancellationRequested)
                {
                    failedCommands++;
                }
            }
        }
        catch (Exception ex)
        {
            status.Error = ex.Message;
            failedCommands++;
        }

        var text = output.ToString();
        var final = ct.IsCancellationRequested
            ? OpState.Cancelled
            : failedCommands == 0
                ? OpState.Succeeded
                : WingetCli.NeedsAdministrator(text) ? OpState.NeedsTerminal : OpState.Failed;
        if (final == OpState.Failed && WingetCli.NeedsSourceAgreement(text))
        {
            status.PendingAgreementSource = text.Contains("msstore", StringComparison.OrdinalIgnoreCase) ? "msstore" : "winget";
        }

        if (final == OpState.Failed && status.Error is null)
        {
            status.Error = VisibleError(text);
        }

        status.Phase = OpPhase.Finalizing;
        if (final == OpState.Succeeded)
        {
            status.Fraction = 1;
        }

        status.State = final;
        status.Finished = DateTime.Now;
        _process = null;
        await Ui.InvokeAsync(() =>
        {
            Raise();
            Finished?.Invoke(request, final);
            ScheduleClear(final);
        });
    }

    private async Task<int> RunStreaming(string[] arguments, OperationStatus status, StringBuilder output, CancellationToken ct)
    {
        using var process = Process.Start(WingetCli.Start(arguments)) ?? throw new InvalidOperationException("winget did not start");
        _process = process;
        process.StandardInput.Close();
        var last = Stopwatch.StartNew();
        var gate = new object();

        void Pump(StreamReader reader)
        {
            var buffer = new char[2048];
            int n;
            while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new string(buffer, 0, n);
                lock (gate)
                {
                    last.Restart();
                    output.Append(chunk);
                    status.Log.Append(chunk.Replace("\r\n", "\n").Replace('\r', '\n'));
                    Apply(status, chunk, arguments);
                }

                Ui.BeginInvoke(Raise);
            }
        }

        var readers = new[] { Task.Run(() => Pump(process.StandardOutput)), Task.Run(() => Pump(process.StandardError)) };
        using var registration = ct.Register(() =>
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // already ended
            }
        });
        var waiter = process.WaitForExitAsync();
        while (!waiter.IsCompleted)
        {
            await Task.WhenAny(waiter, Task.Delay(1000)).ConfigureAwait(false);
            if (last.Elapsed > TimeSpan.FromMinutes(15))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignore
                }
            }
        }

        await Task.WhenAll(readers).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static void Apply(OperationStatus status, string chunk, string[] arguments)
    {
        var clean = Ansi.Replace(chunk, string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var percent = Percent.Matches(clean);
        if (percent.Count > 0 && double.TryParse(percent[^1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
        {
            status.Fraction = Math.Clamp(p / 100, 0, 1);
        }
        else if (Bytes.Matches(clean) is { Count: > 0 } pair)
        {
            var m = pair[^1];
            var done = ToBytes(m.Groups[1].Value, m.Groups[2].Value);
            var total = ToBytes(m.Groups[3].Value, m.Groups[4].Value);
            if (total > 0)
            {
                status.Fraction = Math.Clamp(done / total, 0, 1);
            }
        }

        var lower = clean.ToLowerInvariant();
        if (lower.Contains("downloading") || lower.Contains("fetching") || lower.Contains("downloaded"))
        {
            status.Phase = OpPhase.Downloading;
        }
        else if (lower.Contains("starting package uninstall") || lower.Contains("uninstalling") || lower.Contains("purging"))
        {
            status.Phase = OpPhase.Uninstalling;
        }
        else if (lower.Contains("successfully verified installer hash") || lower.Contains("starting package install") || lower.Contains("installing"))
        {
            status.Phase = status.Action switch
            {
                PkgAction.Uninstall => OpPhase.Uninstalling,
                PkgAction.Upgrade or PkgAction.UpgradeAll or PkgAction.UpgradeMany => OpPhase.Upgrading,
                PkgAction.UpdateSources => OpPhase.Refreshing,
                _ => OpPhase.Installing,
            };
        }
        else if (lower.Contains("successfully installed") || lower.Contains("successfully uninstalled") || lower.Contains("successfully upgraded") || lower.Contains("cleanup"))
        {
            status.Phase = OpPhase.Finalizing;
        }
        else if (status.Action == PkgAction.UpdateSources && (lower.Contains("updating") || lower.Contains("done")))
        {
            status.Phase = OpPhase.Refreshing;
        }

        var activity = LastActivity(clean);
        if (activity is not null)
        {
            status.Activity = activity;
        }
    }

    private static double ToBytes(string value, string unit)
    {
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return 0;
        }

        return unit.ToUpperInvariant() switch
        {
            "KB" => number * 1024,
            "MB" => number * 1024 * 1024,
            "GB" => number * 1024 * 1024 * 1024,
            _ => number,
        };
    }

    private static bool LooksLikeProgress(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length <= 1 && "-\\|/".Contains(trimmed))
        {
            return true;
        }

        var nonSpace = trimmed.Where(c => !char.IsWhiteSpace(c)).ToList();
        if (nonSpace.Count == 0)
        {
            return true;
        }

        var symbols = nonSpace.Count(c => "#=->.:%0123456789█▒░▓".Contains(c));
        return symbols > nonSpace.Count * 0.85;
    }

    /// <summary>Last meaningful line of a text chunk (not blank, not a progress bar, not a "$ " echo).</summary>
    public static string? LastActivity(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("$ ") || LooksLikeProgress(line))
            {
                continue;
            }

            return line.TrimStart('=', '>', '-', ' ');
        }

        return null;
    }

    /// <summary>Last three meaningful lines (error detail).</summary>
    public static string VisibleError(string text)
    {
        var lines = Ansi.Replace(text, string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !LooksLikeProgress(l))
            .ToList();
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 3)));
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>Clears a finished status (and its log).</summary>
    public void Clear()
    {
        if (IsBusy)
        {
            return;
        }

        _clearTimer?.Stop();
        Status = null;
        Raise();
    }

    private void ScheduleClear(OpState state)
    {
        _clearTimer?.Stop();
        var seconds = state switch
        {
            OpState.Succeeded => 8,
            OpState.Cancelled => 6,
            OpState.Failed or OpState.NeedsTerminal => 20,
            _ => 0,
        };
        if (seconds == 0)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Status is null || Status.IsRunning)
            {
                return;
            }

            if (Status.State is OpState.Succeeded or OpState.Cancelled)
            {
                Status = null;
            }
            else
            {
                Status.Log.Clear();
            }

            Raise();
        };
        _clearTimer = timer;
        timer.Start();
    }

    private void Raise() => Changed?.Invoke();

    // ---- request builders (winget argument lists, never concatenated through a shell) ----

    public static string[] InstallArgs(string id, string? source) => Tail(["install", "--id", id, "--exact", "--silent", "--accept-package-agreements", .. WingetCli.Common], source);

    public static string[] UninstallArgs(string id, string? source) => Tail(["uninstall", "--id", id, "--exact", "--silent", "--disable-interactivity"], source);

    public static string[] UpgradeArgs(string id, string? source) => Tail(["upgrade", "--id", id, "--exact", "--silent", "--accept-package-agreements", .. WingetCli.Common], source);

    public static string[] UpgradeAllArgs(bool includeUnknown) => includeUnknown
        ? ["upgrade", "--all", "--silent", "--accept-package-agreements", .. WingetCli.Common, "--include-unknown"]
        : ["upgrade", "--all", "--silent", "--accept-package-agreements", .. WingetCli.Common];

    public static string[] SourceUpdateArgs() => ["source", "update", .. WingetCli.Common];

    private static string[] Tail(string[] head, string? source) =>
        string.IsNullOrEmpty(source) ? head : [.. head, "--source", source];
}
