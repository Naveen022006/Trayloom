// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>The few power-scheme calls the lid override needs, so the journal logic can be tested with a fake.</summary>
internal interface ILidPower
{
    /// <summary>The active power scheme.</summary>
    bool TryGetActive(out Guid scheme);

    /// <summary>The lid-close action (AC and DC) of a scheme.</summary>
    bool TryRead(Guid scheme, out uint ac, out uint dc);

    /// <summary>Writes the lid-close action (AC and DC) of a scheme.</summary>
    bool TryWrite(Guid scheme, uint ac, uint dc);

    /// <summary>Re-applies the scheme so the new values take effect, but only if it is still the active one.</summary>
    void SetActiveIfCurrent(Guid scheme);
}

/// <summary>
/// The record of the user's own lid-close action, written once before Trayloom overrides it and deleted only after the original values
/// were written back. It is never overwritten: if a record exists, the values in it are the user's, whatever the power scheme says now.
/// </summary>
internal sealed class LidJournal
{
    /// <summary>The journal file name (the same one earlier versions used).</summary>
    public const string FileName = "lid-journal.json";

    /// <summary>Taken around every read-modify-write of the lid action or the journal, by the service and by the recovery hook.</summary>
    public static readonly object Gate = new();

    // Windows' own default for the lid-close action ("sleep"), used when the original values were lost.
    private const uint DefaultLidAction = 1;

    private readonly string _path;
    private readonly ILidPower _power;
    private readonly ILog _log;
    private bool _hadCorrupt;

    public LidJournal(string dataDir, ILidPower power, ILog log)
    {
        _path = Path.Combine(dataDir, FileName);
        _power = power;
        _log = log;
    }

    /// <summary>True while a journal file exists.</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>True when a journal was unreadable (now set aside as <c>.bad</c>), so the lid action may still be Trayloom's 0.</summary>
    public bool HadCorruptJournal => _hadCorrupt || File.Exists(_path + ".bad");

    /// <summary>Records the original values. Returns false, changing nothing, when a journal already exists.</summary>
    public bool TryRecordOriginal(Guid scheme, uint ac, uint dc)
    {
        try
        {
            return AtomicFile.TryCreateNew(_path, JsonSerializer.Serialize(new Entry(scheme, ac, dc)));
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not write the lid journal: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes the recorded values back and deletes the journal only on success. No journal is a success. An unreadable journal is set
    /// aside as <c>.bad</c> and reported as false. A failed write keeps the journal so the next start can retry.
    /// </summary>
    public bool TryRestore()
    {
        if (!File.Exists(_path))
        {
            return true;
        }

        Entry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            entry = null;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not read the lid journal: {ex.Message}");
            return false;
        }

        if (entry is null || entry.Scheme == Guid.Empty)
        {
            SetAside();
            return false;
        }

        try
        {
            if (!_power.TryWrite(entry.Scheme, entry.Ac, entry.Dc))
            {
                _log.Warn("Could not put back the lid action; it is kept to retry.");
                return false;
            }

            _power.SetActiveIfCurrent(entry.Scheme);
            File.Delete(_path);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not restore the lid action: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Makes the lid do nothing. An existing journal is restored first (and kept if that fails); otherwise the current values are
    /// recorded before they are overwritten. Call under <see cref="Gate"/>.
    /// </summary>
    public bool TryOverride()
    {
        try
        {
            if (Exists)
            {
                TryRestore();
            }

            if (!_power.TryGetActive(out var scheme))
            {
                return false;
            }

            if (!Exists)
            {
                if (!_power.TryRead(scheme, out var ac, out var dc))
                {
                    return false;
                }

                // Both 0 after a lost journal is Trayloom's own earlier override, not the user's choice: record Windows' default.
                if (ac == 0 && dc == 0 && HadCorruptJournal)
                {
                    ac = DefaultLidAction;
                    dc = DefaultLidAction;
                }

                if (!TryRecordOriginal(scheme, ac, dc))
                {
                    return false;
                }
            }

            // A journal that is still here after a failed restore is kept as it is; its values are the user's.
            if (_power.TryWrite(scheme, 0, 0))
            {
                _power.SetActiveIfCurrent(scheme);
                return true;
            }

            TryRestore();
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn($"Closed-lid mode failed: {ex.Message}");
            return false;
        }
    }

    private void SetAside()
    {
        _hadCorrupt = true;
        try
        {
            var bad = _path + ".bad";
            File.Move(_path, bad, overwrite: true);
            _log.Warn("The lid journal was unreadable and was set aside as lid-journal.json.bad.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not set the unreadable lid journal aside: {ex.Message}");
        }
    }

    // Property names match the journal earlier versions wrote, so an old journal is still restored.
    private sealed record Entry(Guid Scheme, uint Ac, uint Dc);
}

/// <summary>The brightness the panel had before lid-dimming set it to 0, kept on disk so a crash cannot leave the panel dark.</summary>
internal static class LidBrightnessJournal
{
    public const string FileName = "lid-brightness.json";

    /// <summary>The lowest level a restore writes, so a bad record can never leave the panel black.</summary>
    public const int Floor = 10;

    public static string PathIn(string dataDir) => Path.Combine(dataDir, FileName);

    /// <summary>Records the level once; an existing record is never overwritten.</summary>
    public static bool TryRecord(string dataDir, int level)
    {
        try
        {
            return AtomicFile.TryCreateNew(PathIn(dataDir), JsonSerializer.Serialize(new Entry(level)));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The recorded level, or -1 when there is none or it is unreadable.</summary>
    public static int TryRead(string dataDir)
    {
        try
        {
            var path = PathIn(dataDir);
            return File.Exists(path) ? JsonSerializer.Deserialize<Entry>(File.ReadAllText(path))?.Level ?? -1 : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static bool Exists(string dataDir) => File.Exists(PathIn(dataDir));

    public static void Delete(string dataDir)
    {
        try
        {
            File.Delete(PathIn(dataDir));
        }
        catch
        {
            // retried at the next start
        }
    }

    /// <summary>The level to write back: the recorded one, never below <see cref="Floor"/> and never above 100.</summary>
    public static int RestoreLevel(int recorded) => Math.Clamp(recorded, Floor, 100);

    private sealed record Entry(int Level);
}

/// <summary>The real power-scheme calls.</summary>
internal sealed class Win32LidPower : ILidPower
{
    private static readonly Guid ButtonSubgroup = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public bool TryGetActive(out Guid scheme)
    {
        scheme = Guid.Empty;
        if (PowerGetActiveScheme(0, out var ptr) != 0 || ptr == 0)
        {
            return false;
        }

        scheme = Marshal.PtrToStructure<Guid>(ptr);
        LocalFree(ptr);
        return true;
    }

    public bool TryRead(Guid scheme, out uint ac, out uint dc)
    {
        ac = 0;
        dc = 0;
        var sub = ButtonSubgroup;
        var action = LidAction;
        return PowerReadACValueIndex(0, ref scheme, ref sub, ref action, out ac) == 0
               && PowerReadDCValueIndex(0, ref scheme, ref sub, ref action, out dc) == 0;
    }

    public bool TryWrite(Guid scheme, uint ac, uint dc)
    {
        var sub = ButtonSubgroup;
        var action = LidAction;
        return PowerWriteACValueIndex(0, ref scheme, ref sub, ref action, ac) == 0
               && PowerWriteDCValueIndex(0, ref scheme, ref sub, ref action, dc) == 0;
    }

    public void SetActiveIfCurrent(Guid scheme)
    {
        if (TryGetActive(out var active) && active == scheme)
        {
            PowerSetActiveScheme(0, ref scheme);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint root, out nint scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(nint root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
}

/// <summary>
/// Puts back what the lid override changed, at every start (even when the feature is off), after a crash and on uninstall. It acts only
/// when a journal says Trayloom changed something; a journal that cannot be restored is kept for the next start.
/// </summary>
public sealed class KeepAwakeRecovery : IStartupRecovery
{
    private readonly Func<string> _dataDir;
    private readonly ILidPower _power;
    private readonly Func<int, bool> _setBrightness;

    public KeepAwakeRecovery()
        : this(() => AppPaths.FeatureData("keep-awake"), new Win32LidPower(), SetPanelBrightness)
    {
    }

    internal KeepAwakeRecovery(Func<string> dataDir, ILidPower power, Func<int, bool> setBrightness)
    {
        _dataDir = dataDir;
        _power = power;
        _setBrightness = setBrightness;
    }

    public string Id => "keep-awake-lid";

    public void Recover(RecoveryStage stage, ILog log)
    {
        var dir = _dataDir();
        lock (LidJournal.Gate)
        {
            var journal = new LidJournal(dir, _power, log);
            if (journal.Exists && !journal.TryRestore())
            {
                log.Warn("keep-awake-lid: the lid action could not be put back; it will be retried at the next start.");
            }

            if (LidBrightnessJournal.Exists(dir))
            {
                var recorded = LidBrightnessJournal.TryRead(dir);
                // An unreadable record still means the panel may be dark: bring it to a visible level.
                var level = LidBrightnessJournal.RestoreLevel(recorded > 0 ? recorded : 50);
                if (_setBrightness(level))
                {
                    LidBrightnessJournal.Delete(dir);
                }
                else
                {
                    log.Warn("keep-awake-lid: the panel brightness could not be put back; it will be retried at the next start.");
                }
            }
        }
    }

    // Bounded to two seconds: the WMI call can hang on a broken driver.
    private static bool SetPanelBrightness(int level)
    {
        try
        {
            var task = Task.Run(() =>
            {
                BrightnessWrites.MarkOwn();
                var ok = Wmi.ForEach(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", o => Wmi.Invoke(o, "WmiSetBrightness", 1, level));
                BrightnessWrites.MarkOwn();
                return ok;
            });
            return task.Wait(2000) && task.Result;
        }
        catch
        {
            return false;
        }
    }
}
