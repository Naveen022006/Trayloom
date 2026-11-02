// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Microsoft.Win32;

namespace WinKnife.App;

/// <summary>"Start with Windows" through the per-user Run registry key (redirectable with <c>WINKNIFE_RUN_KEY</c> for tests).</summary>
internal static class AutoStart
{
    private const string ValueName = "WinKnife";

    private static string RunKey
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("WINKNIFE_RUN_KEY");
            return string.IsNullOrWhiteSpace(overridden) ? @"Software\Microsoft\Windows\CurrentVersion\Run" : overridden;
        }
    }

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>True when the user switched the entry off in Task Manager / Settings > Apps > Startup.</summary>
    public static bool IsDisabledByUser
    {
        get
        {
            if (Environment.GetEnvironmentVariable("WINKNIFE_RUN_KEY") is { Length: > 0 })
            {
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
        }
    }

    /// <summary>Registers (or removes) the start-at-sign-in entry. <paramref name="exePath"/> defaults to the running executable.</summary>
    public static void SetEnabled(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unknown.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
