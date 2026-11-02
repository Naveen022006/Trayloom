// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinKnife.Core;

namespace WinKnife.App;

/// <summary>What the install dialog collected.</summary>
/// <param name="StartMenuShortcut">Create the Start menu shortcut.</param>
/// <param name="DesktopShortcut">Create the Desktop shortcut.</param>
/// <param name="LaunchAtLogin">Register WinKnife to start at sign-in.</param>
internal sealed record InstallOptions(bool StartMenuShortcut, bool DesktopShortcut, bool LaunchAtLogin);

/// <summary>What the uninstall dialog collected.</summary>
/// <param name="RemoveSettings">Also delete the settings and data folder.</param>
internal sealed record UninstallOptions(bool RemoveSettings);

/// <summary>
/// The per-user install flow: copies the running app into <c>%LOCALAPPDATA%\Programs\WinKnife</c>, adds shortcuts and an Apps &amp;
/// features entry (HKCU), and removes all of it again on <c>WinKnife.exe --uninstall</c>. No administrator rights, no installer
/// toolchain. Every location can be redirected with an environment variable so the flow can be tested without touching the real
/// Start menu, Desktop or registry:
/// <c>WINKNIFE_INSTALL_ROOT</c>, <c>WINKNIFE_STARTMENU_DIR</c>, <c>WINKNIFE_DESKTOP_DIR</c>, <c>WINKNIFE_UNINSTALL_KEY</c>
/// (an HKCU sub key) and <c>WINKNIFE_RUN_KEY</c> (see <see cref="AutoStart"/>).
/// </summary>
internal static class InstallManager
{
    private const string DefaultUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinKnife";
    private const string ManifestName = ".winknife-files.txt";
    private const string ShortcutName = "WinKnife.lnk";

    /// <summary>Where the installed copy lives (<c>%LOCALAPPDATA%\Programs\WinKnife</c> unless overridden).</summary>
    public static string InstallRoot => Normalize(Env("WINKNIFE_INSTALL_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinKnife"));

    /// <summary>The per-user Start menu Programs folder.</summary>
    public static string StartMenuDirectory => Env("WINKNIFE_STARTMENU_DIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>The Desktop folder.</summary>
    public static string DesktopDirectory => Env("WINKNIFE_DESKTOP_DIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The HKCU sub key of the Apps &amp; features entry.</summary>
    public static string UninstallKeyPath => Env("WINKNIFE_UNINSTALL_KEY") ?? DefaultUninstallKey;

    /// <summary>True when the paths above are redirected (tests).</summary>
    public static bool IsRedirected => Env("WINKNIFE_INSTALL_ROOT") is not null;

    /// <summary>The folder of the running executable.</summary>
    public static string CurrentDirectory => Normalize(AppContext.BaseDirectory);

    /// <summary>The path of the installed executable.</summary>
    public static string InstalledExe => Path.Combine(InstallRoot, ExeName);

    /// <summary>File name of the running executable (WinKnife.exe).</summary>
    public static string ExeName => Path.GetFileName(Environment.ProcessPath ?? "WinKnife.exe");

    /// <summary>True when the running copy is the installed one.</summary>
    public static bool IsInstalledCopy => string.Equals(CurrentDirectory, InstallRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when running from a source tree build (<c>bin\Debug</c> / <c>bin\Release</c>): no install offer for developers.</summary>
    public static bool IsDevelopmentBuild =>
        CurrentDirectory.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || CurrentDirectory.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)
        || CurrentDirectory.EndsWith(@"\bin\Debug", StringComparison.OrdinalIgnoreCase) || CurrentDirectory.EndsWith(@"\bin\Release", StringComparison.OrdinalIgnoreCase);

    /// <summary>The installed version recorded in the Apps &amp; features entry, or null when nothing is installed.</summary>
    public static string? InstalledVersion
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
                if (key?.GetValue("DisplayVersion") is string version)
                {
                    return File.Exists(InstalledExe) ? version : null;
                }

                return File.Exists(InstalledExe) ? "unknown" : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>The Start menu shortcut path.</summary>
    public static string StartMenuShortcut => Path.Combine(StartMenuDirectory, ShortcutName);

    /// <summary>The Desktop shortcut path.</summary>
    public static string DesktopShortcut => Path.Combine(DesktopDirectory, ShortcutName);

    // ------------------------------------------------------------------------------------------------ install

    /// <summary>
    /// Copies the running app to <see cref="InstallRoot"/>, creates the shortcuts and the uninstall entry. Throws on failure after
    /// logging; nothing half-installed is registered (the entry is written last).
    /// </summary>
    public static string Install(InstallOptions options, ILog log, IProgress<double>? progress = null)
    {
        var source = CurrentDirectory;
        var destination = InstallRoot;
        log.Info($"Installing from {source} to {destination}");
        Directory.CreateDirectory(destination);
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Equals("devcmd.txt", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var copied = new List<string>();
        long total = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var relative = Path.GetRelativePath(source, files[i]);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var info = new FileInfo(files[i]);
            total += info.Length;
            var existing = new FileInfo(target);
            if (!existing.Exists || existing.Length != info.Length || existing.LastWriteTimeUtc != info.LastWriteTimeUtc)
            {
                CopyFile(files[i], target, log);
            }

            copied.Add(relative);
            progress?.Report((i + 1) / (double)files.Count);
        }

        RemoveStaleFiles(destination, copied, log);
        File.WriteAllLines(Path.Combine(destination, ManifestName), copied);

        var exe = Path.Combine(destination, ExeName);
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("The copy of WinKnife.exe is missing after installing.", exe);
        }

        if (options.StartMenuShortcut)
        {
            CreateShortcut(StartMenuShortcut, exe, log);
        }
        else
        {
            TryDelete(StartMenuShortcut);
        }

        if (options.DesktopShortcut)
        {
            CreateShortcut(DesktopShortcut, exe, log);
        }
        else
        {
            TryDelete(DesktopShortcut);
        }

        WriteUninstallEntry(exe, destination, total / 1024, log);
        if (options.LaunchAtLogin || AutoStart.IsEnabled)
        {
            // Also re-points an older entry that still targets the folder the zip was extracted to.
            AutoStart.SetEnabled(true, exe);
        }

        log.Info("Install finished");
        return exe;
    }

    private static void CopyFile(string from, string to, ILog log)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                log.Error($"Copying {from} failed", ex);
                throw;
            }
        }
    }

    private static void RemoveStaleFiles(string destination, List<string> current, ILog log)
    {
        var manifest = Path.Combine(destination, ManifestName);
        if (!File.Exists(manifest))
        {
            return;
        }

        var keep = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in File.ReadAllLines(manifest).Where(l => l.Length > 0 && !keep.Contains(l)))
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(destination, old));
                if (path.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                log.Warn($"Could not remove old file {old}: {ex.Message}");
            }
        }
    }

    private static void WriteUninstallEntry(string exe, string root, long sizeKb, ILog log)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "WinKnife");
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("Publisher", "WinKnife contributors");
        key.SetValue("InstallLocation", root);
        key.SetValue("DisplayIcon", exe + ",0");
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        log.Info($"Registered uninstall entry HKCU\\{UninstallKeyPath}");
    }

    // ------------------------------------------------------------------------------------------------ uninstall

    /// <summary>
    /// Removes the shortcuts, the Apps &amp; features entry and the start-at-login entry, optionally the settings, and schedules the
    /// deletion of the install folder once this process has exited.
    /// </summary>
    /// <returns>The folders to delete once this process exits (pass them to <see cref="ScheduleDeletion"/>).</returns>
    public static List<string> Uninstall(UninstallOptions options, ILog log)
    {
        log.Info("Uninstalling WinKnife");
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        try
        {
            AutoStart.SetEnabled(false);
        }
        catch (Exception ex)
        {
            log.Warn("Could not remove the start-at-login entry: " + ex.Message);
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            log.Warn("Could not remove the Apps & features entry: " + ex.Message);
        }

        var later = new List<string>();
        if (Directory.Exists(InstallRoot))
        {
            later.Add(InstallRoot);
        }

        if (options.RemoveSettings && Directory.Exists(AppPaths.Root))
        {
            later.Add(AppPaths.Root);
        }

        return later;
    }

    /// <summary>
    /// Deletes folders from a hidden helper that outlives this process (the running exe cannot delete itself): it retries every
    /// second for up to 30 seconds until the folder is gone. Call right before exiting.
    /// </summary>
    public static void ScheduleDeletion(IEnumerable<string> folders, ILog log)
    {
        foreach (var folder in folders)
        {
            var command = $"/c \"for /l %i in (1,1,30) do (ping -n 2 127.0.0.1 >nul & rmdir /s /q \"{folder}\" 2>nul & if not exist \"{folder}\" exit /b)\"";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                log.Info("Scheduled deleting " + folder);
            }
            catch (Exception ex)
            {
                log.Warn($"Could not schedule deleting {folder}: {ex.Message}");
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    private static void CreateShortcut(string lnkPath, string target, ILog log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetIconLocation(target, 0);
        link.SetDescription("WinKnife - a discreet tray utility for Windows");
        ((IPersistFile)link).Save(lnkPath, true);
        Marshal.ReleaseComObject(link);
        log.Info("Created shortcut " + lnkPath);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData, uint flags);

        void GetIDList(out nint pidl);

        void SetIDList(nint pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out ushort hotkey);

        void SetHotkey(ushort hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(nint hwnd, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
