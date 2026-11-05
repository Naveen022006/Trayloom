// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Features.WindowTools.Modes;
using static Trayloom.Modes.Harness.Program;

namespace Trayloom.Modes.Harness;

/// <summary>Checks of <see cref="ModesStore"/> in a temporary folder: save and load, and an unreadable file kept aside.</summary>
internal static class StoreChecks
{
    private sealed class NullLog : ILog
    {
        public int Warnings { get; private set; }

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings++;

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trayloom-modes-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var log = new NullLog();
            var store = new ModesStore(folder, log);
            Check(store.Load().Modes.Count == 0, "no file gives no modes");

            var sample = MathChecks.Sample();
            Check(store.Save(sample) && File.Exists(store.FilePath) && !File.Exists(store.FilePath + ".tmp"), "saving writes the file and leaves no temporary file");
            var loaded = store.Load();
            Check(loaded.Modes.Count == 1 && loaded.Modes[0].Items.Count == 5 && loaded.Modes[0].Name == "Coding", "a saved file loads back");
            var bytes = File.ReadAllBytes(store.FilePath);
            Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "the file has no byte order mark");

            File.WriteAllText(store.FilePath, "{ this is not json");
            var empty = store.Load();
            var aside = Directory.GetFiles(folder, "modes.json.bad-*");
            Check(empty.Modes.Count == 0 && aside.Length == 1 && !File.Exists(store.FilePath), "an unreadable file is renamed to .bad-* and the result is empty");
            Check(log.Warnings == 1, "the log says the file was kept aside");
            Check(File.ReadAllText(aside[0]) == "{ this is not json", "the kept-aside file is untouched");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
