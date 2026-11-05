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
    private const int PasteThreshold = 200;

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

    /// <summary>Inserts text at the caret (typed or pasted).</summary>
    public static void Insert(string text, Dispatcher ui, ILog? log)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (text.Contains('\n') || text.Contains('\r') || text.Length > PasteThreshold)
        {
            if (TryPaste(text, ui, log))
            {
                return;
            }
        }

        KeySender.TypeText(text);
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

    private static bool TryPaste(string text, Dispatcher ui, ILog? log)
    {
        (bool Complete, Dictionary<string, object> Map) snapshot = (false, []);
        var restoreDue = false;
        uint seqAfterSet = 0;
        try
        {
            snapshot = ui.Invoke(Snapshot);
            if (!snapshot.Complete)
            {
                // A busy clipboard or an unreadable format: replacing it could lose the user's data, so type instead.
                log?.Info("snippet: clipboard could not be saved, typing instead of pasting");
                return false;
            }

            var set = ui.Invoke(() =>
            {
                var ok = SetTransient(text);
                seqAfterSet = GetClipboardSequenceNumber();
                return ok;
            });
            if (!set)
            {
                return false;
            }

            restoreDue = true;
            Thread.Sleep(30);
            KeySender.SendChord([0x11, 0x56]);

            // Slow targets read the clipboard late; restoring earlier would paste the old contents.
            Thread.Sleep(1500);
            return true;
        }
        catch (Exception ex)
        {
            log?.Error("Pasting a snippet failed", ex);
            return false;
        }
        finally
        {
            if (restoreDue)
            {
                var map = snapshot.Map;
                var complete = snapshot.Complete;
                var expected = seqAfterSet;
                ui.BeginInvoke(() =>
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
