// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Trayloom.Features.Island;

/// <summary>
/// Takes files out of the island: drags them to Explorer, the desktop or another app, or puts them on the clipboard. Both only ever
/// offer a copy, so the original is never moved or deleted by the receiving side.
/// </summary>
internal static class IslandFileDrag
{
    /// <summary>A marker format carried by every drag that starts here, so the island's own drop handler can tell it apart.</summary>
    internal const string OwnFormat = "Trayloom.IslandFileDrag";

    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;

    /// <summary>Whether a drag was started by the island itself; dropping it back on the island must not park the files again.</summary>
    internal static bool IsOwn(IDataObject data) => data.GetDataPresent(OwnFormat);

    /// <summary>
    /// Makes <paramref name="element"/> start a copy-only file drag once the pointer has moved past the system drag distance.
    /// <paramref name="paths"/> is asked at that moment, so a selection made since the tile was built is honoured.
    /// </summary>
    internal static void Attach(HoverButton element, Func<IReadOnlyList<string>> paths, Func<IDisposable?> hold)
    {
        Point? start = null;
        element.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(element);
        element.PreviewMouseLeftButtonUp += (_, _) => start = null;
        element.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start is not { } s)
            {
                return;
            }

            var now = e.GetPosition(element);
            if (Math.Abs(now.X - s.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - s.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            start = null;
            var existing = Existing(paths());
            if (existing.Count == 0)
            {
                return;
            }

            var data = new DataObject();
            data.SetFileDropList(ToCollection(existing));
            data.SetData(PreferredDropEffect, CopyEffect());
            data.SetData(OwnFormat, true);
            using var keepOpen = hold();
            try
            {
                DragDrop.DoDragDrop(element, data, DragDropEffects.Copy | DragDropEffects.Link);
            }
            finally
            {
                // The drag swallowed the button-up, so without this the button would still look pressed and fire a click later.
                element.CancelPress();
            }
        };
    }

    /// <summary>Puts the files on the clipboard as a copy, so Ctrl+V in Explorer copies them; false when the clipboard stayed busy.</summary>
    internal static bool CopyToClipboard(IReadOnlyList<string> paths)
    {
        var existing = Existing(paths);
        if (existing.Count == 0)
        {
            return false;
        }

        var data = new DataObject();
        data.SetFileDropList(ToCollection(existing));
        data.SetData(PreferredDropEffect, CopyEffect());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                // Another program holds the clipboard open for a moment; try again shortly.
                Thread.Sleep(40);
            }
        }

        return false;
    }

    private static List<string> Existing(IEnumerable<string> paths)
    {
        return paths.Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static System.Collections.Specialized.StringCollection ToCollection(List<string> paths)
    {
        var collection = new System.Collections.Specialized.StringCollection();
        collection.AddRange(paths.ToArray());
        return collection;
    }

    /// <summary>The shell's "Preferred DropEffect" value for a copy: a 4-byte integer in a memory stream.</summary>
    private static MemoryStream CopyEffect() => new(BitConverter.GetBytes(DropEffectCopy));
}
