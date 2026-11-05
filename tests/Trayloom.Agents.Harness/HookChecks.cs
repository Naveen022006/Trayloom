// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json.Nodes;
using Trayloom.Core;
using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

/// <summary>
/// The Claude Code hook: install and remove keep every other entry, writes leave no temp file behind, the uninstall hook removes
/// ours plus the script and the event files, and does nothing at start-up. Everything happens in a temp folder: the environment of
/// this process is pointed at it first, the real <c>~/.claude</c> is never read.
/// </summary>
internal static partial class Program
{
    private sealed class HarnessLog : ILog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    private static void Hooks()
    {
        var root = Path.Combine(Path.GetTempPath(), "tl-hookchecks-" + Guid.NewGuid().ToString("N"));
        var oldLocal = Environment.GetEnvironmentVariable("TRAYLOOM_LOCALDATA");
        var oldConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        try
        {
            var local = Path.Combine(root, "local");
            var home = Path.Combine(root, "home");
            Directory.CreateDirectory(home);
            Environment.SetEnvironmentVariable("TRAYLOOM_LOCALDATA", local);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            var settings = AgentHooks.SettingsPath(home);
            Check(settings.StartsWith(home, StringComparison.OrdinalIgnoreCase), "hooks: the settings path is inside the temp home");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);

            var original = """
                {
                  "model": "opus",
                  "permissions": { "allow": ["Bash(git status)"] },
                  "hooks": {
                    "Notification": [ { "hooks": [ { "type": "command", "command": "echo foreign" } ] } ],
                    "Stop": [ { "hooks": [ { "type": "command", "command": "echo stop" } ] } ]
                  }
                }
                """;
            File.WriteAllText(settings, original);
            var before = JsonNode.Parse(original)!.ToJsonString();

            var install = AgentHooks.Install(settings);
            Check(install.Ok && AgentHooks.Status(settings).Installed, "hooks: install adds both entries");
            Check(File.Exists(AgentHooks.ScriptPath) && !AgentHooks.ScriptIsStale(), "hooks: the script was written and is current");
            Check(File.ReadAllText(AgentHooks.ScriptPath).Contains("AddHours(-1)"), "hooks: the script prunes event files older than an hour");
            Check(Directory.GetFiles(Path.GetDirectoryName(settings)!, "*.tmp").Length == 0 && Directory.GetFiles(local, "*.tmp").Length == 0, "hooks: install leaves no temp file");

            var firstBackup = install.BackupPath;
            var again = AgentHooks.Install(settings);
            Check(again.Ok && firstBackup is not null && again.BackupPath is not null && firstBackup != again.BackupPath && File.Exists(firstBackup), "hooks: backups get their own names and are never overwritten");

            var remove = AgentHooks.Remove(settings);
            Check(remove.Ok && !AgentHooks.Status(settings).Installed, "hooks: remove takes ours out");
            var after = JsonNode.Parse(File.ReadAllText(settings))!.ToJsonString();
            Check(after == before, "hooks: every other entry is equal after install then remove");
            Check(Directory.GetFiles(Path.GetDirectoryName(settings)!, "*.tmp").Length == 0, "hooks: remove leaves no temp file");

            var untouchedBytes = File.ReadAllBytes(settings);
            AgentHooks.Remove(settings);
            Check(File.ReadAllBytes(settings).SequenceEqual(untouchedBytes), "hooks: removing when nothing of ours is there rewrites nothing");

            File.WriteAllText(settings, "{ // comment\n \"a\": 1 }");
            var refused = AgentHooks.Install(settings);
            Check(!refused.Ok && File.ReadAllText(settings).Contains("// comment"), "hooks: a file with comments is left alone");
            File.WriteAllText(settings, original);

            // Uninstall recovery.
            AgentHooks.Install(settings);
            File.WriteAllText(Path.Combine(AgentHooks.EventsFolder, "x.json"), "{}");
            var hook = new AgentUninstall();
            hook.Recover(RecoveryStage.Startup, new HarnessLog());
            Check(AgentHooks.Status(settings).Installed && File.Exists(AgentHooks.ScriptPath), "hooks: the start-up stage does nothing");
            hook.Recover(RecoveryStage.Crash, new HarnessLog());
            Check(AgentHooks.Status(settings).Installed, "hooks: the crash stage does nothing");
            AgentUninstall.Run(settings, new HarnessLog());
            Check(!AgentHooks.Status(settings).Installed && !File.Exists(AgentHooks.ScriptPath) && !Directory.Exists(AgentHooks.EventsFolder), "hooks: the uninstall stage removes ours, the script and the events");
            Check(JsonNode.Parse(File.ReadAllText(settings))!.ToJsonString() == before, "hooks: the uninstall keeps the foreign entries");

            // A single leftover entry (the other was deleted by hand) is still removed, and a foreign agent-hook.ps1 is left alone.
            AgentHooks.Install(settings);
            var doc = JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
            doc["hooks"]!.AsObject().Remove("PermissionRequest");
            doc["hooks"]!["Stop"]!.AsArray().Add(JsonNode.Parse("""{ "hooks": [ { "type": "command", "command": "node C:/other/agent-hook.ps1" } ] }"""));
            File.WriteAllText(settings, doc.ToJsonString());
            Check(!AgentHooks.Status(settings).Installed, "hooks: with one entry gone the hook counts as not installed");
            AgentUninstall.Run(settings, new HarnessLog());
            var left = File.ReadAllText(settings);
            Check(!left.Contains("Trayloom") && left.Contains("C:/other/agent-hook.ps1") && left.Contains("echo foreign"), "hooks: a single leftover entry is removed, foreign entries stay");
            Check(Directory.GetFiles(Path.GetDirectoryName(settings)!, "settings.json.trayloom-backup-*").Length >= 1, "hooks: the uninstall backup sits beside settings.json");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRAYLOOM_LOCALDATA", oldLocal);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", oldConfig);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception)
            {
                // temp folder: leave it
            }
        }
    }
}
