// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.Input;

/// <summary>
/// Types text at the caret of the foreground app: waits for modifiers to be released, erases the typed trigger with an
/// exact number of Backspaces, then inserts the text: short single-line text as Unicode key events, long or multi-line
/// text through a transient paste (the clipboard is filled with the exclusion formats so clipboard history ignores it,
/// Ctrl+V is sent and the previous clipboard is restored). Must be called from a worker thread.
/// </summary>
internal static class SnippetTyper
{
    /// <summary>Waits (15 ms steps, at most 1.5 s) until no modifier key is physically down, then settles for 60 ms.</summary>
    public static void WaitForModifiersReleased()
    {
        for (var i = 0; i < 100 && Win32.AnyModifierDown(); i++)
        {
            Thread.Sleep(15);
        }

        Thread.Sleep(30);
    }

    public static void Backspaces(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = KeySender.KeyInput(0x08, false);
            inputs[(i * 2) + 1] = KeySender.KeyInput(0x08, true);
        }

        InputSender.Send(inputs);
    }

    /// <summary>Inserts text at the caret: short single-line text is typed, everything else is pasted. Line breaks are never typed.</summary>
    public static void Insert(string text, Dispatcher ui, ILog? log)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (!SnippetPastePolicy.NeedsPaste(text))
        {
            KeySender.TypeText(text);
            return;
        }

        Paste(text, ui, log);
    }

    /// <summary>Reads the clipboard text (empty when none), on the UI thread.</summary>
    public static string ClipboardText(Dispatcher ui)
    {
        try
        {
            return ui.Invoke(() =>
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        Thread.Sleep(20);
                    }
                }

                return string.Empty;
            });
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Paste(string text, Dispatcher ui, ILog? log)
    {
        (bool Complete, Dictionary<string, object> Map) snapshot = (false, []);
        var mode = SnippetInsertMode.Skip;
        uint seqAfterSet = 0;
        try
        {
            snapshot = ui.Invoke(Snapshot);
            var set = ui.Invoke(() =>
            {
                var ok = SetTransient(text);
                seqAfterSet = GetClipboardSequenceNumber();
                return ok;
            });
            mode = SnippetPastePolicy.Decide(text, snapshot.Complete, set);
            if (mode == SnippetInsertMode.Skip)
            {
                log?.Info("snippet skipped: the clipboard is busy");
                return;
            }

            Thread.Sleep(30);
            KeySender.SendChord([0x11, 0x56]);

            // Slow targets read the clipboard late; restoring earlier would paste the old contents.
            Thread.Sleep(1500);
        }
        catch (Exception ex)
        {
            log?.Error("Pasting a snippet failed", ex);
        }
        finally
        {
            if (mode == SnippetInsertMode.PasteAndRestore)
            {
                RestoreIfUnchanged(snapshot.Map, seqAfterSet, snapshot.Complete, ui, log);
            }
            else if (mode == SnippetInsertMode.PasteKeepSnippet)
            {
                // The clipboard could not be saved completely (Office delayed formats and the like): leave the snippet on it.
                log?.Info("snippet: clipboard could not be saved, the snippet stays on the clipboard");
            }
        }
    }

    // Synchronous on purpose: the next snippet must see the user's clipboard, not this snippet's transient copy.
    private static void RestoreIfUnchanged(Dictionary<string, object> map, uint expected, bool complete, Dispatcher ui, ILog? log)
    {
        try
        {
            ui.Invoke(() =>
            {
                if (ClipboardRestorePolicy.ShouldRestore(expected, GetClipboardSequenceNumber(), complete))
                {
                    Restore(map);
                }
                else
                {
                    log?.Info("snippet: clipboard changed, not restored");
                }
            });
        }
        catch (Exception ex)
        {
            log?.Error("Restoring the clipboard failed", ex);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    /// <summary>Reads every clipboard format. <c>Complete</c> is false when the clipboard was busy or any format could not be read.</summary>
    private static (bool Complete, Dictionary<string, object> Map) Snapshot()
    {
        var map = new Dictionary<string, object>();
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null)
            {
                return (false, map);
            }

            var complete = true;
            foreach (var format in data.GetFormats(false))
            {
                try
                {
                    if (data.GetData(format, false) is { } value)
                    {
                        map[format] = value;
                    }
                    else
                    {
                        complete = false;
                    }
                }
                catch
                {
                    complete = false;
                }
            }

            return (complete, map);
        }
        catch
        {
            return (false, map);
        }
    }

    private static bool SetTransient(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream([1, 0, 0, 0]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream([0, 0, 0, 0]));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream([0, 0, 0, 0]));
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(20);
            }
        }

        return false;
    }

    private static void Restore(Dictionary<string, object> snapshot)
    {
        try
        {
            if (snapshot.Count == 0)
            {
                // The snapshot was complete and the clipboard was empty before the paste.
                Clipboard.Clear();
                return;
            }

            var data = new DataObject();
            foreach (var (format, value) in snapshot)
            {
                try
                {
                    data.SetData(format, value);
                }
                catch
                {
                    // skip
                }
            }

            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream([1, 0, 0, 0]));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream([0, 0, 0, 0]));
            Clipboard.SetDataObject(data, true);
        }
        catch
        {
            // best effort
        }
    }
}

/// <summary>When a snippet paste may put the saved clipboard back.</summary>
internal static class ClipboardRestorePolicy
{
    /// <summary>True only when the saved copy is complete and nobody changed the clipboard since Trayloom set the snippet.</summary>
    public static bool ShouldRestore(uint seqAfterSet, uint seqNow, bool complete) => complete && seqAfterSet == seqNow;
}

/// <summary>What to do with a snippet's text.</summary>
internal enum SnippetInsertMode
{
    /// <summary>Type it as key events (short, single-line text only).</summary>
    Type,

    /// <summary>Paste through the clipboard, then put the user's clipboard back.</summary>
    PasteAndRestore,

    /// <summary>Paste, but the clipboard could not be saved completely: leave the snippet on it.</summary>
    PasteKeepSnippet,

    /// <summary>Do nothing: pasting is impossible and typing the text would press Enter or flood the app.</summary>
    Skip,
}

/// <summary>The paste, type or skip decision for a snippet.</summary>
internal static class SnippetPastePolicy
{
    /// <summary>Longest text typed as key events.</summary>
    public const int PasteThreshold = 200;

    /// <summary>True for text with a line break or longer than the threshold. Typing a line break presses Enter, which sends chat messages.</summary>
    public static bool NeedsPaste(string text) => text.Contains('\n') || text.Contains('\r') || text.Length > PasteThreshold;

    /// <summary>Decides how to insert <paramref name="text"/> given whether the clipboard was saved completely and could be written.</summary>
    public static SnippetInsertMode Decide(string text, bool snapshotComplete, bool clipboardWritable)
    {
        if (!NeedsPaste(text))
        {
            return SnippetInsertMode.Type;
        }

        if (!clipboardWritable)
        {
            return SnippetInsertMode.Skip;
        }

        return snapshotComplete ? SnippetInsertMode.PasteAndRestore : SnippetInsertMode.PasteKeepSnippet;
    }
}
