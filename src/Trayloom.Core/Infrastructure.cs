// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Windows.Threading;

namespace Trayloom.Core;

/// <summary>Well-known file system locations.</summary>
public static class AppPaths
{
    /// <summary><c>%APPDATA%\Trayloom</c> (overridable for tests with the TRAYLOOM_DATA environment variable).</summary>
    public static string Root { get; } = ResolveRoot();

    /// <summary>
    /// Per-machine data root (<c>%LOCALAPPDATA%\Trayloom</c>): <c>TRAYLOOM_LOCALDATA</c> if set, else <c>TRAYLOOM_DATA\local</c> when
    /// <c>TRAYLOOM_DATA</c> is set, so tests and dev instances never touch the real folder.
    /// </summary>
    public static string LocalRoot { get; } = ResolveLocalRoot();

    /// <summary>The settings JSON file.</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>The log directory.</summary>
    public static string LogDirectory => Path.Combine(Root, "logs");

    /// <summary>The current log file.</summary>
    public static string LogFile => Path.Combine(LogDirectory, "trayloom.log");

    /// <summary>Per-feature data directory; created on demand.</summary>
    public static string FeatureData(string featureId)
    {
        var path = Path.Combine(Root, featureId);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveLocalRoot()
    {
        var local = Environment.GetEnvironmentVariable("TRAYLOOM_LOCALDATA");
        if (!string.IsNullOrWhiteSpace(local))
        {
            return local;
        }

        var data = Environment.GetEnvironmentVariable("TRAYLOOM_DATA");
        if (!string.IsNullOrWhiteSpace(data))
        {
            return Path.Combine(data, "local");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trayloom");
    }

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("TRAYLOOM_DATA");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trayloom");
    }
}

/// <summary>Small rolling file log. Thread-safe; never throws.</summary>
public sealed class FileLog : ILog
{
    private const long MaxBytes = 512 * 1024;

    private readonly object _gate = new();
    private readonly string _path;
    private string? _fallback;
    private string? _failure;

    /// <summary>Creates a log writing to <paramref name="path"/> (default: <see cref="AppPaths.LogFile"/>).</summary>
    public FileLog(string? path = null)
    {
        _path = path ?? AppPaths.LogFile;
    }

    /// <inheritdoc />
    public void Info(string message) => Write("INF", message);

    /// <inheritdoc />
    public void Warn(string message) => Write("WRN", message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) =>
        Write("ERR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        lock (_gate)
        {
            if (_fallback is null)
            {
                try
                {
                    Append(_path, line);
                    return;
                }
                catch (Exception e)
                {
                    // A log that cannot be written would otherwise hide every later problem: move to a folder the person can write to.
                    _failure = e.GetType().Name + ": " + e.Message;
                    _fallback = ChooseFallback();
                }
            }

            if (_fallback is null)
            {
                return;
            }

            try
            {
                if (_failure is not null)
                {
                    Append(_fallback, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WRN] The log file could not be written, so this log is used instead ({_failure}){Environment.NewLine}"));
                    _failure = null;
                }

                Append(_fallback, line);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void Append(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var info = new FileInfo(path);
        if (info.Exists && info.Length > MaxBytes)
        {
            File.Move(path, Path.ChangeExtension(path, ".1.log"), overwrite: true);
        }

        File.AppendAllText(path, line, Encoding.UTF8);
    }

    /// <summary>The first of the per-user fallback logs that accepts a write; null when none does.</summary>
    private static string? ChooseFallback()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trayloom", "logs", "trayloom.log"),
            Path.Combine(Path.GetTempPath(), "Trayloom", "trayloom.log"),
        };
        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
                File.AppendAllText(candidate, "", Encoding.UTF8);
                return candidate;
            }
            catch
            {
                // try the next one
            }
        }

        return null;
    }
}

/// <summary>Decorates another log with a fixed prefix (used for per-feature logs).</summary>
public sealed class PrefixedLog : ILog
{
    private readonly ILog _inner;
    private readonly string _prefix;

    /// <summary>Creates a log that writes <c>[prefix] message</c> to <paramref name="inner"/>.</summary>
    public PrefixedLog(ILog inner, string prefix)
    {
        _inner = inner;
        _prefix = $"[{prefix}] ";
    }

    /// <inheritdoc />
    public void Info(string message) => _inner.Info(_prefix + message);

    /// <inheritdoc />
    public void Warn(string message) => _inner.Warn(_prefix + message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => _inner.Error(_prefix + message, exception);
}

/// <summary>
/// Notification hub. The host application sets <see cref="Sink"/> (e.g. to a tray balloon);
/// without a sink notifications are only logged.
/// </summary>
public sealed class Notifier : INotifier
{
    private readonly Dispatcher _ui;
    private readonly ILog _log;

    /// <summary>Creates the notifier.</summary>
    public Notifier(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
    }

    /// <summary>Delivery callback, invoked on the UI thread.</summary>
    public Action<string, string, NotificationLevel>? Sink { get; set; }

    /// <inheritdoc />
    public void Show(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        _log.Info($"Notification ({level}): {title}");
        var sink = Sink;
        if (sink is null)
        {
            return;
        }

        _ui.BeginInvoke(() =>
        {
            try
            {
                sink(title, message, level);
            }
            catch (Exception ex)
            {
                _log.Error("Notification sink failed", ex);
            }
        });
    }
}
