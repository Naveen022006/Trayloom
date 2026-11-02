// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Input;

/// <summary>
/// App identity helper: an app is identified by the lower-cased full path of its executable (or just its file name
/// when only a name was stored). Used by every "Apps to leave alone" list.
/// </summary>
internal static class AppIdentity
{
    private static readonly ConcurrentDictionary<uint, (string? Path, long Tick)> PathCache = new();

    /// <summary>Identity (lower-case exe path) of the process owning <paramref name="hwnd"/>'s root window.</summary>
    public static string? OfWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : OfProcess(pid);
    }

    /// <summary>Identity of a process id (cached for 30 s).</summary>
    public static string? OfProcess(uint pid)
    {
        var now = Environment.TickCount64;
        if (PathCache.TryGetValue(pid, out var hit) && now - hit.Tick < 30_000)
        {
            return hit.Path;
        }

        string? path = null;
        var process = Win32.OpenProcess(0x1000, false, pid);
        if (process != 0)
        {
            try
            {
                var buffer = new char[1024];
                var size = (uint)buffer.Length;
                if (Win32.QueryFullProcessImageNameW(process, 0, buffer, ref size))
                {
                    path = new string(buffer, 0, (int)size).ToLowerInvariant();
                }
            }
            finally
            {
                Win32.CloseHandle(process);
            }
        }

        if (PathCache.Count > 512)
        {
            PathCache.Clear();
        }

        PathCache[pid] = (path, now);
        return path;
    }

    /// <summary>True when <paramref name="identity"/> (stored in a list) names the app at <paramref name="exePath"/>.</summary>
    public static bool Matches(string identity, string exePath)
    {
        if (identity.Length == 0)
        {
            return false;
        }

        if (identity.Contains('\\') || identity.Contains('/'))
        {
            return string.Equals(identity, exePath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(identity, Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Display name of an identity (file description, else file name without extension).</summary>
    public static string DisplayName(string identity)
    {
        try
        {
            if (File.Exists(identity))
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(identity);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                {
                    return info.FileDescription.Trim();
                }
            }
        }
        catch
        {
            // fall through
        }

        var name = Path.GetFileNameWithoutExtension(identity);
        return name.Length == 0 ? identity : name;
    }

    /// <summary>True when the root window of <paramref name="hwnd"/> belongs to WinKnife itself.</summary>
    public static bool IsOwnWindow(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == Environment.ProcessId;
    }
}

/// <summary>
/// One "Apps to leave alone" list: loads <c>[string]</c> from a flat key, reloads on change and answers
/// "does the window under the pointer / the foreground window belong to a listed app?" cheaply. Nothing is resolved
/// while the list is empty. The pointer lookup is cached for 500 ms while the pointer stays inside the same window.
/// </summary>
internal sealed class AppRuleSet : IDisposable
{
    private readonly IAppSettings _app;
    private readonly string _key;
    private volatile string[] _ids = [];
    private long _cacheTick;
    private nint _cacheRoot;
    private RECT _cacheRect;
    private bool _cacheHit;

    public AppRuleSet(IAppSettings app, string key)
    {
        _app = app;
        _key = key;
        Reload();
        app.Changed += OnChanged;
    }

    public bool IsEmpty => _ids.Length == 0;

    public IReadOnlyList<string> Ids => _ids;

    public void Dispose() => _app.Changed -= OnChanged;

    /// <summary>True when the window under the physical-pixel point belongs to a listed app. Hook-thread safe.</summary>
    public bool MatchesPoint(int x, int y)
    {
        var ids = _ids;
        if (ids.Length == 0)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (_cacheRoot != 0 && now - _cacheTick < 500 && x >= _cacheRect.Left && x < _cacheRect.Right && y >= _cacheRect.Top && y < _cacheRect.Bottom)
        {
            return _cacheHit;
        }

        var hit = false;
        nint root = 0;
        var rect = default(RECT);
        try
        {
            var hwnd = Win32.WindowFromPoint(new POINT { X = x, Y = y });
            root = hwnd == 0 ? 0 : Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (root != 0)
            {
                NativeMethods.GetWindowRect(root, out rect);
                hit = MatchesWindow(root, ids);
            }
        }
        catch
        {
            // best effort
        }

        _cacheRoot = root;
        _cacheRect = rect;
        _cacheHit = hit;
        _cacheTick = now;
        return hit;
    }

    /// <summary>True when the foreground window belongs to a listed app.</summary>
    public bool MatchesForeground() => _ids.Length > 0 && MatchesWindow(NativeMethods.GetForegroundWindow(), _ids);

    public bool MatchesWindow(nint hwnd)
    {
        var ids = _ids;
        return ids.Length > 0 && MatchesWindow(hwnd, ids);
    }

    private static bool MatchesWindow(nint hwnd, string[] ids)
    {
        var path = AppIdentity.OfWindow(hwnd);
        if (path is null)
        {
            return false;
        }

        foreach (var id in ids)
        {
            if (AppIdentity.Matches(id, path))
            {
                return true;
            }
        }

        return false;
    }

    private void OnChanged(string key)
    {
        if (key == _key)
        {
            Reload();
        }
    }

    private void Reload()
    {
        _ids = [.. (_app.Get<List<string>?>(_key) ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant())];
        _cacheRoot = 0;
    }
}
