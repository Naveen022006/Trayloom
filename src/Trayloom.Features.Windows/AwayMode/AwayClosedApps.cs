// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>What kind of app a listed app is (spec 07 section 2.1).</summary>
internal enum AwayAppKind
{
    Browser,
    BrowserMin,
    Packaged,
    App,
}

/// <summary>Why an app was only minimized or left open instead of being closed (spec 07 section 9.1 lists the wording).</summary>
internal enum AwayKeepReason
{
    None,
    Private,
    Download,
    OtherDesktop,
    Unsupported,
    Prompt,
    Timeout,
    NotResponding,
    Refused,
    Elevated,
    OtherUser,
    Cancelled,
    Limit,
    Failed,
    Partial,
}

/// <summary>Where an app is in its close and reopen cycle.</summary>
internal enum AwayAppState
{
    Closing,
    Closed,
    Kept,
    Reopening,
    Reopened,
    Failed,
}

/// <summary>One window of a closed app: where it was, so it can be put back. No title, no URL.</summary>
internal sealed class ClosedWindow
{
    /// <summary>0 = topmost of all recorded windows (same numbering as <c>session.json</c>).</summary>
    [JsonPropertyName("z")]
    public int Z { get; set; }

    /// <summary><c>normal</c>, <c>max</c> or <c>min</c>.</summary>
    [JsonPropertyName("show")]
    public string Show { get; set; } = "normal";

    /// <summary>Normal position in workspace coordinates: left, top, right, bottom.</summary>
    [JsonPropertyName("normal")]
    public int[] Normal { get; set; } = [0, 0, 0, 0];

    /// <summary>The handle the window had before it closed (this run only; links it to its <see cref="AwayRecord"/>).</summary>
    [JsonIgnore]
    public long OldHwnd { get; set; }
}

/// <summary>One app Trayloom closed (or looked at and kept open).</summary>
internal sealed class ClosedApp
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public AwayAppKind Kind { get; set; }

    [JsonPropertyName("exe")]
    public string Exe { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public string Args { get; set; } = string.Empty;

    [JsonPropertyName("aumid")]
    public string? Aumid { get; set; }

    /// <summary>The browser's user data folder; empty for the default one.</summary>
    [JsonPropertyName("udd")]
    public string Udd { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public uint Pid { get; set; }

    [JsonPropertyName("pidStart")]
    public long PidStart { get; set; }

    /// <summary><c>rm</c> (Restart Manager) or <c>wm_close</c>.</summary>
    [JsonPropertyName("how")]
    public string How { get; set; } = "wm_close";

    [JsonPropertyName("state")]
    public AwayAppState State { get; set; } = AwayAppState.Closing;

    [JsonPropertyName("reason")]
    public AwayKeepReason Reason { get; set; }

    /// <summary>The app kept running with no window (a minimize-to-tray app).</summary>
    [JsonPropertyName("tray")]
    public bool Tray { get; set; }

    /// <summary>A browser whose process outlived its windows (background mode).</summary>
    [JsonPropertyName("background")]
    public bool Background { get; set; }

    /// <summary>The app that owned the window the person was working in.</summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("windows")]
    public List<ClosedWindow> Windows { get; set; } = [];

    /// <summary>Display name for the cards (never the arguments).</summary>
    [JsonIgnore]
    public string Name => AppIdentity.DisplayName(Exe);
}

/// <summary>The saved list of closed apps (<c>away-mode\closed-apps.json</c>, spec 07 section 7.2).</summary>
internal sealed class ClosedAppsFile
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("ownerPid")]
    public int OwnerPid { get; set; }

    [JsonPropertyName("ownerStart")]
    public long OwnerStart { get; set; }

    [JsonPropertyName("since")]
    public DateTime Since { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTime ClosedAt { get; set; }

    [JsonPropertyName("apps")]
    public List<ClosedApp> Apps { get; set; } = [];
}

/// <summary>
/// <c>closed-apps.json</c>: its own lifetime, separate from <c>session.json</c>. Written synchronously (temp file + move) before the
/// first close message and after every state change, deleted when no record is left. It never goes through the debounced settings
/// store, because it has to survive a crash or kill.
/// </summary>
internal sealed class AwayClosedStore
{
    /// <summary>A record older than this is dropped instead of offered.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILog _log;

    public AwayClosedStore(string directory, ILog log)
    {
        _path = Path.Combine(directory, "closed-apps.json");
        _log = log;
    }

    public bool Exists => File.Exists(_path);

    /// <summary>Writes the file atomically. Failures are logged, never thrown.</summary>
    public void Write(ClosedAppsFile file)
    {
        lock (_gate)
        {
            try
            {
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _log.Warn($"[away] closed-apps.json not written: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }
    }

    /// <summary>Reads the file; null when it is missing or damaged.</summary>
    public ClosedAppsFile? Read()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<ClosedAppsFile>(File.ReadAllText(_path), Options) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            try
            {
                File.Delete(_path);
                File.Delete(_path + ".tmp");
            }
            catch (Exception ex)
            {
                _log.Warn($"[away] closed-apps.json not deleted: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }
    }

    /// <summary>
    /// The start-up decision of spec 07 section 7.3: what an earlier run left that is worth offering. Returns null when there is nothing
    /// (the file is deleted or left to its live owner). Never reopens anything.
    /// </summary>
    public ClosedAppsFile? Recover(Func<ClosedApp, bool> alreadyOpen)
    {
        var file = Read();
        if (file is null)
        {
            if (Exists)
            {
                _log.Info("[away] closed-apps.json unreadable: deleted");
                Delete();
            }

            return null;
        }

        if (file.Version != ClosedAppsFile.CurrentVersion || DateTime.UtcNow - file.ClosedAt > MaxAge)
        {
            _log.Info("[away] closed-apps.json too old or from another version: deleted");
            Delete();
            return null;
        }

        if (OwnerAlive(file))
        {
            return null;
        }

        var offer = new List<ClosedApp>();
        foreach (var app in file.Apps)
        {
            switch (app.State)
            {
                case AwayAppState.Closing:
                    // Still running means it never closed; gone means it did.
                    using (var handle = AwayProcess.Handle.Open(app.Pid, app.PidStart))
                    {
                        if (handle is not null && !handle.HasExited)
                        {
                            continue;
                        }
                    }

                    app.State = AwayAppState.Closed;
                    break;
                case AwayAppState.Closed or AwayAppState.Failed or AwayAppState.Reopening:
                    break;
                default:
                    continue;
            }

            if (!alreadyOpen(app))
            {
                offer.Add(app);
            }
        }

        if (offer.Count == 0)
        {
            Delete();
            return null;
        }

        file.Apps = offer;
        return file;
    }

    private static bool OwnerAlive(ClosedAppsFile file)
    {
        if (file.OwnerPid == Environment.ProcessId)
        {
            return true;
        }

        using var handle = AwayProcess.Handle.Open((uint)file.OwnerPid, file.OwnerStart);
        return handle is not null && !handle.HasExited;
    }
}
