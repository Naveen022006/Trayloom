// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Reflection;
using Microsoft.Win32;

namespace WinKnife.Core;

/// <summary>State of Windows 11 Smart App Control.</summary>
public enum SmartAppControlState
{
    /// <summary>Could not be read (older Windows, no access).</summary>
    Unknown,

    /// <summary>Off (or not available on this edition).</summary>
    Off,

    /// <summary>Evaluation mode: Windows is still learning and may switch it on or off.</summary>
    Evaluation,

    /// <summary>On: unsigned code that Microsoft's cloud does not know is blocked.</summary>
    Enforcing,
}

/// <summary>A problem found while starting up (a feature that did not start, a DLL that did not load).</summary>
/// <param name="Subject">What failed, in words ("Window switcher", "WinKnife.Features.Input.dll").</param>
/// <param name="Message">The short technical reason.</param>
/// <param name="Exception">The exception, when there is one.</param>
/// <param name="BlockedByPolicy">True when the reason looks like an Application Control / Smart App Control block.</param>
public sealed record StartupProblem(string Subject, string Message, Exception? Exception, bool BlockedByPolicy);

/// <summary>
/// Detects Smart App Control (the registry policy state) and recognises load failures that it causes, so the app can explain
/// them in plain words. WinKnife only reads the state; it never changes it - that choice belongs to the user.
/// </summary>
public static class SmartAppControl
{
    private const string PolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    /// <summary>Reads <c>HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState</c> (0 off, 1 on, 2 evaluation).</summary>
    public static SmartAppControlState GetState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
            return key?.GetValue("VerifiedAndReputablePolicyState") switch
            {
                0 => SmartAppControlState.Off,
                1 => SmartAppControlState.Enforcing,
                2 => SmartAppControlState.Evaluation,
                _ => SmartAppControlState.Unknown,
            };
        }
        catch (Exception)
        {
            return SmartAppControlState.Unknown;
        }
    }

    /// <summary>
    /// True when the exception (or anything inside it) is Windows refusing to load a file because of an Application Control /
    /// Smart App Control / Device Guard policy or a signature problem.
    /// </summary>
    public static bool LooksBlocked(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < 8; depth++)
        {
            if (exception is ReflectionTypeLoadException rtl && rtl.LoaderExceptions.Any(e => LooksBlocked(e)))
            {
                return true;
            }

            if (exception.HResult is unchecked((int)0x800711C7) or unchecked((int)0x800704EC) or unchecked((int)0x80070241) or unchecked((int)0x800711C6))
            {
                return true;
            }

            var text = exception.Message;
            if (text.Contains("0x800711C7", StringComparison.OrdinalIgnoreCase)
                || text.Contains("0x800704EC", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Application Control", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Smart App Control", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Device Guard", StringComparison.OrdinalIgnoreCase)
                || text.Contains("blocked this file", StringComparison.OrdinalIgnoreCase)
                || text.Contains("blocked by group policy", StringComparison.OrdinalIgnoreCase)
                || text.Contains("digital signature", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    /// <summary>The file name a load failure is about (FileLoadException.FileName), when known.</summary>
    public static string? BlockedFile(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < 8; depth++)
        {
            switch (exception)
            {
                case FileLoadException { FileName: { Length: > 0 } name }:
                    return Path.GetFileName(name);
                case FileNotFoundException { FileName: { Length: > 0 } name }:
                    return Path.GetFileName(name);
                case ReflectionTypeLoadException rtl:
                    foreach (var inner in rtl.LoaderExceptions)
                    {
                        if (BlockedFile(inner) is { } file)
                        {
                            return file;
                        }
                    }

                    break;
            }

            exception = exception.InnerException;
        }

        return null;
    }

    /// <summary>One-line state description for the About page and the log.</summary>
    public static string Describe(SmartAppControlState state) => state switch
    {
        SmartAppControlState.Enforcing => "Smart App Control is on.",
        SmartAppControlState.Evaluation => "Smart App Control is in evaluation mode.",
        SmartAppControlState.Off => "Smart App Control is off.",
        _ => "Smart App Control status is unknown.",
    };

    /// <summary>Plain-words explanation of a Smart App Control block, with the user's options.</summary>
    public static string Explain(string? file)
    {
        var what = string.IsNullOrEmpty(file) ? "a WinKnife component" : $"“{file}”";
        return $"Windows blocked {what}. Your PC has Smart App Control turned on, a Windows 11 protection that only runs apps signed by a publisher Microsoft trusts, and this build of WinKnife is not signed yet. "
            + "Nothing is wrong with your files, and WinKnife never changes this protection by itself.\n\n"
            + "What you can do:\n"
            + "1. Use a signed build of WinKnife (the proper fix: whoever builds WinKnife signs it with a code-signing certificate).\n"
            + "2. Or turn Smart App Control off yourself in Windows Security > App & browser control > Smart App Control settings. Windows does not let you turn it back on later without resetting the PC, so it is your decision.";
    }
}
