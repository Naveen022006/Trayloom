// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.Files;

/// <summary>
/// Pastes the clipboard text without any formatting, then puts the original clipboard back. Non-text content is forwarded
/// as a plain paste; if the original cannot be preserved in full the transient paste fails open (pastes normally).
/// </summary>
public sealed class PastePlainFeature : IFeature
{
    private IFeatureContext? _context;
    private bool _busy;

    public string Id => "paste-plain";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("paste-plain", Strings.PastePlain.Name, () => Run(), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        context.App.Register("pastePlainEnabled", false);
        context.Shortcuts.Bind("pastePlain", () => Run());
    }

    public void Stop() => _context = null;

    private static bool IsTerminalWindow(nint hwnd)
    {
        var cls = WinApi.GetClass(hwnd);
        return cls is "ConsoleWindowClass" or "mintty" or "PuTTY" or "VirtualConsoleClass";
    }

    private async void Run()
    {
        var context = _context;
        if (context is null || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            // The shortcut's own modifier keys are still down; Shift+Alt+Win+V would not paste. Wait for them.
            await ModifierHelper.ReleaseAsync(1500);
            if (_context is null)
            {
                return;
            }

            var target = NativeMethods.GetForegroundWindow();
            var terminal = IsTerminalWindow(target);

            // Non-text content (images, files, promised data) is never read: it is forwarded as an ordinary paste.
            if (!ClipboardIO.IsAvailable(ClipFormats.UnicodeText) || ClipboardIO.IsAvailable(ClipFormats.HDrop)
                || ClipboardIO.IsAvailable(ClipFormats.FileGroupDescriptorW) || ClipboardIO.IsAvailable(ClipFormats.Png))
            {
                SendPaste(terminal);
                return;
            }

            var text = ClipboardIO.ReadText();
            if (string.IsNullOrEmpty(text))
            {
                SendPaste(terminal);
                return;
            }

            if (context.App.Get("pastePlainTrim", false))
            {
                text = text.Trim();
            }

            var snapshot = ClipboardSnapshot.Capture();
            if (snapshot.Unpreservable || snapshot.HasFileData)
            {
                // Fail open: paste what is there rather than risk clearing the user's data.
                SendPaste(terminal);
                return;
            }

            if (!ClipboardIO.WriteText(text, excludeFromHistory: true))
            {
                context.Notifier.Show(Strings.PastePlain.Name, "The clipboard is busy. Try again.", NotificationLevel.Warning);
                return;
            }

            var sequence = WinApi.GetClipboardSequenceNumber();
            await Task.Delay(30);
            SendPaste(terminal);

            await Task.Delay(Math.Clamp(context.App.Get("pastePlainRestoreDelay", 500), 100, 5000));
            if (WinApi.GetClipboardSequenceNumber() == sequence)
            {
                // Nothing else was copied meanwhile: bring back the original formats.
                snapshot.Restore();
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Paste as plain text failed", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private static void SendPaste(bool terminal)
    {
        if (terminal)
        {
            InputSender.SendChord((ushort)VK_SHIFT, 0x2D); // Shift+Insert
        }
        else
        {
            InputSender.SendChord((ushort)VK_CONTROL, (ushort)'V');
        }
    }
}
