// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>
/// At uninstall, removes Trayloom's Claude Code hook: the two entries in <c>settings.json</c> (everything else in the file stays as
/// it was), the hook script and the folder of event files. Leaving a hook that runs a script after the app is gone would be a defect.
/// It does nothing at any other stage, and never opens <c>.credentials.json</c>.
/// </summary>
public sealed class AgentUninstall : IStartupRecovery
{
    /// <inheritdoc />
    public string Id => "agents-claude-hook";

    /// <inheritdoc />
    public void Recover(RecoveryStage stage, ILog log)
    {
        if (stage != RecoveryStage.Uninstall)
        {
            return;
        }

        Run(AgentHooks.SettingsPath(AgentLogRoot.Home), log);
    }

    /// <summary>The uninstall work for one settings file (the harness points it at a temp file).</summary>
    public static void Run(string settingsPath, ILog log)
    {
        try
        {
            // Always asked, even when only one of the two entries is left: Remove does nothing (no backup, no write) when none is ours.
            var result = AgentHooks.Remove(settingsPath, backupBesideFile: true);
            if (result.BackupPath is not null)
            {
                log.Info("Claude hook removed: " + result.Message);
            }
            else if (!result.Ok)
            {
                log.Warn("Claude hook: " + result.Message);
            }
        }
        catch (Exception ex)
        {
            log.Warn("Claude hook: could not edit settings.json: " + ex.Message);
        }

        try
        {
            if (File.Exists(AgentHooks.ScriptPath))
            {
                File.Delete(AgentHooks.ScriptPath);
            }
        }
        catch (Exception ex)
        {
            log.Warn("Claude hook: could not delete the script: " + ex.Message);
        }

        try
        {
            var events = AgentHooks.EventsFolder;
            if (Directory.Exists(events)
                && SafePaths.IsUnder(events, AgentLogRoot.LocalRoot)
                && !new DirectoryInfo(events).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(events, recursive: true);
            }
        }
        catch (Exception ex)
        {
            log.Warn("Claude hook: could not delete the event files: " + ex.Message);
        }
    }
}
