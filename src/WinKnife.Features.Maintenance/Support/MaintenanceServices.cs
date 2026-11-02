// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Kill Process as a service for the Command Bar (list + identity-verified kill; protected processes never listed).</summary>
internal sealed class ProcessKillServiceImpl : IProcessKillService
{
    public IReadOnlyList<ProcessEntry> Browse(string query, int max)
    {
        var service = KillService.Shared;
        _ = service.RefreshAsync(false);
        var q = (query ?? string.Empty).Trim();
        return
        [
            .. service.Entries
                .Where(e => !e.IsProtected && (q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Pid.ToString() == q))
                .OrderByDescending(e => e.Memory)
                .Take(Math.Max(1, max))
                .Select(e => new ProcessEntry(e.Pid, e.Name, null, e.Memory)),
        ];
    }

    public bool Kill(int pid)
    {
        var entry = KillService.Shared.Entries.FirstOrDefault(e => e.Pid == pid);
        if (entry is null || entry.IsProtected)
        {
            return false;
        }

        var result = KillService.KillOne(new KillTarget(entry.Pid, entry.StartedAt, entry.Name), force: false);
        return result is KillResult.Killed or KillResult.AlreadyGone;
    }
}

/// <summary>Uninstaller entry point for other projects.</summary>
internal sealed class UninstallServiceImpl : IUninstallService
{
    public Task SelectAsync(string path) => UninstallerFeature.SelectAsync(path);
}

/// <summary>Media tools entry point for other projects.</summary>
internal sealed class MediaToolsServiceImpl : IMediaToolsService
{
    public void OpenWith(IReadOnlyList<string> paths) => MediaToolsFeature.OpenWith(paths, tool: 0);
}
