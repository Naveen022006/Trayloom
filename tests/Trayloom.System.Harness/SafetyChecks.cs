// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Features.SystemTools;

namespace Trayloom.System.Harness;

/// <summary>
/// Checks of the journals that undo Trayloom's system changes: the lid action, the lid-dim brightness, the mic mute, the pending
/// Bluetooth and the audio-routing marker. Everything runs in temp folders against fakes; no power scheme, WMI call, radio or
/// microphone is touched.
/// </summary>
internal static class SafetyChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    private sealed class FakeLog : ILog
    {
        public int Warnings;

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings++;

        public void Error(string message, Exception? exception = null) => Warnings++;
    }

    private sealed class FakePower : ILidPower
    {
        public Guid Scheme = Guid.NewGuid();
        public uint Ac = 1;
        public uint Dc = 2;
        public bool FailWrites;
        public int Writes;
        public int Activated;

        public bool TryGetActive(out Guid scheme)
        {
            scheme = Scheme;
            return true;
        }

        public bool TryRead(Guid scheme, out uint ac, out uint dc)
        {
            ac = Ac;
            dc = Dc;
            return true;
        }

        public bool TryWrite(Guid scheme, uint ac, uint dc)
        {
            Writes++;
            if (FailWrites)
            {
                return false;
            }

            Ac = ac;
            Dc = dc;
            return true;
        }

        public void SetActiveIfCurrent(Guid scheme) => Activated++;
    }

    private sealed class FakeMic : IMicEndpoint
    {
        public string Id { get; init; } = "mic";

        public bool Mute { get; set; } = true;

        public float Level { get; set; }
    }

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Directory.CreateDirectory(Temp());
        try
        {
            Lid();
            Brightness();
            Recovery();
            Mic();
            Marker();
            BluetoothSleep();
            Floor();
        }
        finally
        {
            try
            {
                Directory.Delete(Temp(), true);
            }
            catch
            {
                // leave it
            }
        }
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "trayloom-system-harness");

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Temp(), name + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Lid()
    {
        var dir = Fresh("lid");
        var log = new FakeLog();
        var power = new FakePower { Ac = 1, Dc = 2 };
        var journal = new LidJournal(dir, power, log);
        _check(journal.TryOverride(), "lid: override succeeds");
        _check(power is { Ac: 0, Dc: 0 }, "lid: lid action is now 0/0");
        _check(journal.Exists, "lid: the original values were journaled");
        _check(!journal.TryRecordOriginal(power.Scheme, 0, 0), "lid: an existing journal is never overwritten");

        // A second override while the journal exists restores first, then records the same originals again.
        _check(journal.TryOverride(), "lid: a second override works");
        _check(journal.Exists, "lid: a journal exists after the second override");

        power.FailWrites = true;
        _check(!journal.TryRestore(), "lid: a failed restore reports false");
        _check(journal.Exists, "lid: a failed restore keeps the journal");
        power.FailWrites = false;
        _check(journal.TryRestore(), "lid: a later restore succeeds");
        _check(power is { Ac: 1, Dc: 2 }, "lid: the original values are back");
        _check(!journal.Exists, "lid: success deletes the journal");
        _check(journal.TryRestore(), "lid: restore with no journal is a no-op success");

        // A failed override write puts things back and reports failure.
        var power2 = new FakePower { Ac = 1, Dc = 1 };
        var journal2 = new LidJournal(Fresh("lid2"), power2, log);
        power2.FailWrites = true;
        _check(!journal2.TryOverride(), "lid: a failed write reports false");
        _check(power2 is { Ac: 1, Dc: 1 }, "lid: a failed write leaves the values alone");

        // Corrupt journal.
        var dir3 = Fresh("lid3");
        File.WriteAllText(Path.Combine(dir3, LidJournal.FileName), "{ not json");
        var power3 = new FakePower { Ac = 0, Dc = 0 };
        var journal3 = new LidJournal(dir3, power3, log);
        _check(!journal3.TryRestore(), "lid: a corrupt journal reports false");
        _check(!journal3.Exists && File.Exists(Path.Combine(dir3, LidJournal.FileName + ".bad")), "lid: a corrupt journal is set aside as .bad");
        _check(journal3.HadCorruptJournal, "lid: the corrupt journal is remembered");
        _check(journal3.TryOverride(), "lid: override after a corrupt journal works");
        _check(journal3.TryRestore() && power3 is { Ac: 1, Dc: 1 }, "lid: 0/0 after a corrupt journal is recorded as 1/1 and comes back as 1/1");

        // A journal written by an earlier version is still restored.
        var dir4 = Fresh("lid4");
        var scheme = Guid.NewGuid();
        File.WriteAllText(Path.Combine(dir4, LidJournal.FileName), "{\"Scheme\":\"" + scheme + "\",\"Ac\":2,\"Dc\":3}");
        var power4 = new FakePower { Scheme = scheme, Ac = 0, Dc = 0 };
        _check(new LidJournal(dir4, power4, log).TryRestore() && power4 is { Ac: 2, Dc: 3 }, "lid: an earlier version's journal is restored");
    }

    private static void Brightness()
    {
        var dir = Fresh("bright");
        _check(LidBrightnessJournal.TryRecord(dir, 70), "brightness: the level is recorded");
        _check(!LidBrightnessJournal.TryRecord(dir, 0), "brightness: an existing record is never overwritten");
        _check(LidBrightnessJournal.TryRead(dir) == 70, "brightness: the original level is kept");
        LidBrightnessJournal.Delete(dir);
        _check(!LidBrightnessJournal.Exists(dir) && LidBrightnessJournal.TryRead(dir) == -1, "brightness: delete removes the record");
        _check(LidBrightnessJournal.RestoreLevel(0) == 10 && LidBrightnessJournal.RestoreLevel(3) == 10, "brightness: restore never goes below 10 %");
        _check(LidBrightnessJournal.RestoreLevel(70) == 70 && LidBrightnessJournal.RestoreLevel(500) == 100, "brightness: restore keeps a normal level and caps at 100");
    }

    private static void Recovery()
    {
        var dir = Fresh("recovery");
        var log = new FakeLog();
        var power = new FakePower { Ac = 1, Dc = 2 };
        var set = new List<int>();
        var recovery = new KeepAwakeRecovery(() => dir, power, level =>
        {
            set.Add(level);
            return true;
        });

        recovery.Recover(RecoveryStage.Startup, log);
        _check(power.Writes == 0 && set.Count == 0, "recovery: no journal changes nothing");

        foreach (var stage in new[] { RecoveryStage.Startup, RecoveryStage.Crash, RecoveryStage.Uninstall })
        {
            new LidJournal(dir, power, log).TryOverride();
            LidBrightnessJournal.TryRecord(dir, 4);
            recovery.Recover(stage, log);
            _check(power is { Ac: 1, Dc: 2 }, $"recovery: lid action is restored at {stage}");
        }

        _check(!new LidJournal(dir, power, log).Exists, "recovery: the lid journal is gone after success");
        _check(set.Count == 3 && set.All(l => l == 10), "recovery: a stale low brightness record is raised to the floor");
        _check(!LidBrightnessJournal.Exists(dir), "recovery: the brightness record is deleted after success");

        // Failure keeps both for the next start.
        var dir2 = Fresh("recovery2");
        var power2 = new FakePower { Ac = 1, Dc = 2 };
        new LidJournal(dir2, power2, log).TryOverride();
        LidBrightnessJournal.TryRecord(dir2, 60);
        power2.FailWrites = true;
        new KeepAwakeRecovery(() => dir2, power2, _ => false).Recover(RecoveryStage.Startup, log);
        _check(new LidJournal(dir2, power2, log).Exists, "recovery: a failed restore keeps the lid journal");
        _check(LidBrightnessJournal.Exists(dir2), "recovery: a failed brightness restore keeps the record");
    }

    private static void Mic()
    {
        var dir = Fresh("mic");
        MicMuteJournal.Write(dir, true, new Dictionary<string, double> { ["a"] = 0.6 });
        var read = MicMuteJournal.Read(dir);
        _check(read is { Active: true } r && Math.Abs(r.Levels["a"] - 0.6) < 1e-9, "mic: journal round trip");

        var endpoint = new FakeMic { Id = "a", Mute = true, Level = 0f };
        var other = new FakeMic { Id = "b", Mute = true, Level = 0.5f };
        var log = new FakeLog();
        var recovery = new MicMuteRecovery(() => dir, () => [endpoint, other]);
        recovery.Recover(RecoveryStage.Startup, log);
        _check(endpoint.Mute && File.Exists(MicMuteJournal.PathIn(dir)), "mic: startup leaves the mute alone");
        recovery.Recover(RecoveryStage.Uninstall, log);
        _check(!endpoint.Mute && !other.Mute, "mic: uninstall un-mutes every microphone");
        _check(Math.Abs(endpoint.Level - 0.6f) < 1e-6 && Math.Abs(other.Level - 0.5f) < 1e-6, "mic: a zeroed level comes back, others are untouched");
        _check(!File.Exists(MicMuteJournal.PathIn(dir)), "mic: the journal is deleted");

        var dir2 = Fresh("mic2");
        var untouched = new FakeMic { Mute = true };
        new MicMuteRecovery(() => dir2, () => [untouched]).Recover(RecoveryStage.Uninstall, log);
        _check(untouched.Mute, "mic: no journal means nothing is touched");

        MicMuteJournal.Write(dir, true, new Dictionary<string, double> { ["a"] = 0.6 });
        MicMuteJournal.Write(dir, false, new Dictionary<string, double>());
        _check(!File.Exists(MicMuteJournal.PathIn(dir)), "mic: nothing to remember deletes the journal");
    }

    private static void Marker()
    {
        var dir = Fresh("marker");
        _check(!RoutingSelfTestMarker.IsPending(dir, "26300.1"), "routing: no marker, the self-test may run");
        RoutingSelfTestMarker.Begin(dir, "26300.1");
        _check(RoutingSelfTestMarker.IsPending(dir, "26300.1"), "routing: a marker means an earlier test did not finish");
        _check(!RoutingSelfTestMarker.IsPending(dir, "26300.2"), "routing: the marker is per build");
        _check(Path.GetFileName(RoutingSelfTestMarker.PathFor(dir, "26300.1")) == "audio-routing-selftest-26300.1.inprogress", "routing: marker file name");
        RoutingSelfTestMarker.Clear(dir, "26300.1");
        _check(!RoutingSelfTestMarker.IsPending(dir, "26300.1"), "routing: clearing removes the marker");
    }

    private static void BluetoothSleep()
    {
        var dir = Fresh("bt");
        var log = new FakeLog();
        var calls = 0;
        var recovery = new BluetoothRecovery(() => dir, () =>
        {
            calls++;
            return Task.FromResult(true);
        });
        recovery.Recover(RecoveryStage.Startup, log);
        _check(calls == 0, "bluetooth: no journal means the radio is not touched");

        var path = BluetoothSleepJournal.PathIn(dir);
        File.WriteAllText(path, "{}");
        recovery.Recover(RecoveryStage.Startup, log);
        _check(calls == 1 && !File.Exists(path), "bluetooth: a journal turns the radio on and is deleted");

        File.WriteAllText(path, "{}");
        new BluetoothRecovery(() => dir, () => Task.FromResult(false)).Recover(RecoveryStage.Uninstall, log);
        _check(File.Exists(path), "bluetooth: a failed turn-on keeps the journal for the next start");
    }

    private static void Floor()
    {
        _check(DisplayMath.ClampHardware(0) == 5 && DisplayMath.ClampHardware(4) == 5, "floor: hardware writes never go below 5 %");
        _check(DisplayMath.ClampHardware(50) == 50 && DisplayMath.ClampHardware(150) == 100, "floor: hardware writes keep normal levels and cap at 100");
    }
}
