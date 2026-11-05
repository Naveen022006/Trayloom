// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Buffers.Binary;
using System.Text;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>One normal (non-popup, not closed) browser window of a session file and the addresses of its tabs, in tab order.</summary>
internal sealed record SessionWindow(int Id, List<string> Urls);

/// <summary>
/// Reads the windows and tab addresses out of a Chromium session file (spec 08 section 2.5). The format is Chromium's SNSS
/// command log from <c>components/sessions</c>: stable for years, but reverse-engineered and not a public API, so the parser fails
/// closed: any inconsistency returns null and the caller falls back to "paste the addresses". Page titles are never read: parsing
/// stops after the address of a navigation. Pure: it works on bytes in memory and touches no file.
/// </summary>
internal static class ChromiumSession
{
    private const int HeaderSize = 8;
    private const int CommandSetTabWindow = 0;
    private const int CommandSetTabIndexInWindow = 2;
    private const int CommandPrunedFromBack = 5;
    private const int CommandUpdateTabNavigation = 6;
    private const int CommandSetSelectedNavigationIndex = 7;
    private const int CommandSetWindowType = 9;
    private const int CommandPrunedFromFront = 11;
    private const int CommandTabClosed = 16;
    private const int CommandWindowClosed = 17;
    private const int CommandPruned = 24;

    /// <summary>The windows of the session, or null when the data is not a session file this parser understands.</summary>
    public static List<SessionWindow>? Parse(ReadOnlySpan<byte> data)
    {
        // "SNSS" and a version. Versions 2 and 4 are the encrypted variants and are refused.
        if (data.Length < HeaderSize || data[0] != (byte)'S' || data[1] != (byte)'N' || data[2] != (byte)'S' || data[3] != (byte)'S')
        {
            return null;
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (version is not (1 or 3))
        {
            return null;
        }

        var tabWindow = new Dictionary<int, int>();
        var tabIndex = new Dictionary<int, int>();
        var selected = new Dictionary<int, int>();
        var navigations = new Dictionary<int, SortedDictionary<int, string>>();
        var windowType = new Dictionary<int, int>();
        var closedTabs = new HashSet<int>();
        var closedWindows = new HashSet<int>();

        var offset = HeaderSize;
        while (offset + 2 <= data.Length)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            if (size == 0)
            {
                // A record with no id byte cannot be valid: what follows is padding or damage, so reading stops here.
                break;
            }

            if (offset + 2 + size > data.Length)
            {
                // The tail that Chromium was still writing; it is normal and simply ignored.
                break;
            }

            var id = data[offset + 2];
            var payload = data.Slice(offset + 3, size - 1);
            offset += 2 + size;
            switch (id)
            {
                case CommandSetTabWindow:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    tabWindow[Int(payload, 4)] = Int(payload, 0);
                    break;
                case CommandSetTabIndexInWindow:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    tabIndex[Int(payload, 0)] = Int(payload, 4);
                    break;
                case CommandPrunedFromBack:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    PruneBack(navigations, Int(payload, 0), Int(payload, 4));
                    break;
                case CommandUpdateTabNavigation:
                    if (!ReadNavigation(payload, navigations))
                    {
                        return null;
                    }

                    break;
                case CommandSetSelectedNavigationIndex:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    selected[Int(payload, 0)] = Int(payload, 4);
                    break;
                case CommandSetWindowType:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    windowType[Int(payload, 0)] = Int(payload, 4);
                    break;
                case CommandPrunedFromFront:
                    if (payload.Length < 8)
                    {
                        return null;
                    }

                    PruneFront(navigations, selected, Int(payload, 0), Int(payload, 4));
                    break;
                case CommandTabClosed:
                    if (payload.Length < 4)
                    {
                        return null;
                    }

                    closedTabs.Add(Int(payload, 0));
                    break;
                case CommandWindowClosed:
                    if (payload.Length < 4)
                    {
                        return null;
                    }

                    closedWindows.Add(Int(payload, 0));
                    break;
                case CommandPruned:
                    if (payload.Length < 12)
                    {
                        return null;
                    }

                    PruneMiddle(navigations, selected, Int(payload, 0), Int(payload, 4), Int(payload, 8));
                    break;
                default:
                    // Commands that do not matter here, including the marker 255 that ends a block of commands.
                    break;
            }
        }

        return Collect(tabWindow, tabIndex, selected, navigations, windowType, closedTabs, closedWindows);
    }

    private static int Int(ReadOnlySpan<byte> payload, int at) => BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);

    /// <summary>Command 6: a pickle of the payload size, tab id, navigation index and the address (length, UTF-8 bytes, padded to 4). The rest (title, state) is not read.</summary>
    private static bool ReadNavigation(ReadOnlySpan<byte> payload, Dictionary<int, SortedDictionary<int, string>> navigations)
    {
        if (payload.Length < 16)
        {
            return false;
        }

        var pickleSize = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (pickleSize > (uint)(payload.Length - 4))
        {
            return false;
        }

        var tab = Int(payload, 4);
        var index = Int(payload, 8);
        var length = Int(payload, 12);
        if (length < 0 || length > payload.Length - 16)
        {
            return false;
        }

        string url;
        try
        {
            url = new UTF8Encoding(false, true).GetString(payload.Slice(16, length));
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!navigations.TryGetValue(tab, out var list))
        {
            navigations[tab] = list = [];
        }

        list[index] = url;
        return true;
    }

    private static void PruneBack(Dictionary<int, SortedDictionary<int, string>> navigations, int tab, int index)
    {
        if (navigations.TryGetValue(tab, out var list))
        {
            foreach (var key in list.Keys.Where(k => k >= index).ToList())
            {
                list.Remove(key);
            }
        }
    }

    /// <summary>Drops the first <paramref name="count"/> navigations and shifts the rest (and the selected one) down by that many.</summary>
    private static void PruneFront(Dictionary<int, SortedDictionary<int, string>> navigations, Dictionary<int, int> selected, int tab, int count)
    {
        if (navigations.TryGetValue(tab, out var list))
        {
            navigations[tab] = Shift(list, 0, count);
        }

        if (selected.TryGetValue(tab, out var current))
        {
            selected[tab] = Math.Max(-1, current - count);
        }
    }

    /// <summary>Drops <paramref name="count"/> navigations from <paramref name="index"/> and shifts the higher ones (and the selected one) down.</summary>
    private static void PruneMiddle(Dictionary<int, SortedDictionary<int, string>> navigations, Dictionary<int, int> selected, int tab, int index, int count)
    {
        if (navigations.TryGetValue(tab, out var list))
        {
            navigations[tab] = Shift(list, index, count);
        }

        if (selected.TryGetValue(tab, out var current))
        {
            selected[tab] = current >= index + count ? current - count : current >= index ? -1 : current;
        }
    }

    private static SortedDictionary<int, string> Shift(SortedDictionary<int, string> list, int index, int count)
    {
        var result = new SortedDictionary<int, string>();
        foreach (var (key, value) in list)
        {
            if (key < index)
            {
                result[key] = value;
            }
            else if (key >= index + count)
            {
                result[key - count] = value;
            }
        }

        return result;
    }

    private static List<SessionWindow> Collect(
        Dictionary<int, int> tabWindow,
        Dictionary<int, int> tabIndex,
        Dictionary<int, int> selected,
        Dictionary<int, SortedDictionary<int, string>> navigations,
        Dictionary<int, int> windowType,
        HashSet<int> closedTabs,
        HashSet<int> closedWindows)
    {
        var byWindow = new SortedDictionary<int, List<(int Index, int Tab, string Url)>>();
        foreach (var (tab, window) in tabWindow)
        {
            // Closed tabs and windows, and windows that are not normal browser windows (popups, apps), are not part of a workspace.
            if (closedTabs.Contains(tab) || closedWindows.Contains(window) || windowType.GetValueOrDefault(window, 0) != 0)
            {
                continue;
            }

            if (!navigations.TryGetValue(tab, out var list) || list.Count == 0)
            {
                continue;
            }

            var key = selected.TryGetValue(tab, out var index) && list.ContainsKey(index) ? index : list.Keys.Max();
            if (ModesMath.NormalizeUrl(list[key]) is not { } url || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!byWindow.TryGetValue(window, out var tabs))
            {
                byWindow[window] = tabs = [];
            }

            tabs.Add((tabIndex.GetValueOrDefault(tab, int.MaxValue), tab, url));
        }

        var result = new List<SessionWindow>();
        foreach (var (window, tabs) in byWindow)
        {
            var urls = tabs.OrderBy(t => t.Index).ThenBy(t => t.Tab).Select(t => t.Url).ToList();
            if (urls.Count > 0)
            {
                result.Add(new SessionWindow(window, urls));
            }
        }

        return result;
    }
}
