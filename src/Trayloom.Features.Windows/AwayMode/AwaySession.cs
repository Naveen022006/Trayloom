// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>One window Away mode minimized. No title and no path are kept (they are personal data).</summary>
internal sealed class AwayRecord
{
    [JsonPropertyName("hwnd")]
    public long Hwnd { get; set; }

    [JsonPropertyName("pid")]
    public uint Pid { get; set; }

    [JsonPropertyName("process")]
    public string Process { get; set; } = string.Empty;

    [JsonPropertyName("cls")]
    public string Cls { get; set; } = string.Empty;

    /// <summary>0 = topmost of the recorded windows.</summary>
    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("max")]
    public bool Max { get; set; }

    /// <summary>Normal position: left, top, right, bottom.</summary>
    [JsonPropertyName("normal")]
    public int[] Normal { get; set; } = [0, 0, 0, 0];

    /// <summary>Tick count when the minimize was requested (not saved): a record this fresh may not be iconic yet.</summary>
    [JsonIgnore]
    public long MinimizedAt { get; set; }

    [JsonIgnore]
    public nint Handle => (nint)Hwnd;
}

/// <summary>The saved state of an away period (<c>away-mode\session.json</c>).</summary>
internal sealed class AwaySessionFile
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("ownerPid")]
    public int OwnerPid { get; set; }

    [JsonPropertyName("ownerStart")]
    public long OwnerStart { get; set; }

    [JsonPropertyName("since")]
    public DateTime Since { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public long Active { get; set; }

    [JsonPropertyName("windows")]
    public List<AwayRecord> Windows { get; set; } = [];
}

/// <summary>
/// <c>session.json</c>: written synchronously (temp file + move) before any window is touched, rewritten when the records change and
/// deleted after a verified restore. It is what lets the next start put windows back after a crash or kill (spec 06 section 4.6).
/// </summary>
internal sealed class AwaySession
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILog _log;

    public AwaySession(string directory, ILog log)
    {
        _path = Path.Combine(directory, "session.json");
        _log = log;
    }

    /// <summary>Full path of the file.</summary>
    public string FilePath => _path;

    /// <summary>Process start time as a FILETIME, to tell this process from an earlier one that reused the pid.</summary>
    public static long CurrentProcessStart()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToFileTimeUtc();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Writes the file atomically. Failures are logged, never thrown (a missing file only costs the crash convenience).</summary>
    public void Write(AwaySessionFile file)
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
                _log.Warn($"[away] session.json not written: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }
    }

    /// <summary>Reads the file; null when it is missing or damaged.</summary>
    public AwaySessionFile? Read()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<AwaySessionFile>(File.ReadAllText(_path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>True when the file exists.</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>Deletes the file (and a leftover temp file).</summary>
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
                _log.Warn($"[away] session.json not deleted: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }
    }
}
