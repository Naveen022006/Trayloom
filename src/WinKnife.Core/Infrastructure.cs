// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>Well-known file system locations.</summary>
public static class AppPaths
{
    /// <summary><c>%APPDATA%\WinKnife</c> (overridable for tests with the WINKNIFE_DATA environment variable).</summary>
    public static string Root { get; } = ResolveRoot();

    /// <summary>The settings JSON file.</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>The log directory.</summary>
    public static string LogDirectory => Path.Combine(Root, "logs");

    /// <summary>The current log file.</summary>
    public static string LogFile => Path.Combine(LogDirectory, "winknife.log");

    /// <summary>Per-feature data directory; created on demand.</summary>
    public static string FeatureData(string featureId)
    {
        var path = Path.Combine(Root, featureId);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("WINKNIFE_DATA");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinKnife");
    }
}

/// <summary>Small rolling file log. Thread-safe; never throws.</summary>
public sealed class FileLog : ILog
{
    private const long MaxBytes = 512 * 1024;

    private readonly object _gate = new();
    private readonly string _path;

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
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var rolled = Path.ChangeExtension(_path, ".1.log");
                    File.Move(_path, rolled, overwrite: true);
                }

                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
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
        _log.Info($"Notification ({level}): {title} - {message}");
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
