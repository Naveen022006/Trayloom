// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using Trayloom.Features.WindowTools.Modes;
using static Trayloom.Modes.Harness.Program;

namespace Trayloom.Modes.Harness;

/// <summary>A small writer of Chromium session-file bytes (the SNSS command log), so the parser is checked against synthesised data and never a real session.</summary>
internal sealed class SnssWriter
{
    private readonly List<byte> _bytes = [];

    public SnssWriter(int version = 1)
    {
        _bytes.AddRange("SNSS"u8.ToArray());
        _bytes.AddRange(BitConverter.GetBytes(version));
    }

    public SnssWriter Command(byte id, params byte[] payload)
    {
        _bytes.AddRange(BitConverter.GetBytes((ushort)(payload.Length + 1)));
        _bytes.Add(id);
        _bytes.AddRange(payload);
        return this;
    }

    public SnssWriter Ints(byte id, params int[] values) => Command(id, [.. values.SelectMany(BitConverter.GetBytes)]);

    public SnssWriter TabWindow(int window, int tab) => Ints(0, window, tab);

    public SnssWriter TabIndex(int tab, int index) => Ints(2, tab, index);

    public SnssWriter PrunedFromBack(int tab, int index) => Ints(5, tab, index);

    public SnssWriter Selected(int tab, int index) => Ints(7, tab, index);

    public SnssWriter WindowType(int window, int type) => Ints(9, window, type);

    public SnssWriter PrunedFromFront(int tab, int count) => Ints(11, tab, count);

    public SnssWriter TabClosed(int tab) => Command(16, [.. BitConverter.GetBytes(tab), .. BitConverter.GetBytes(0L)]);

    public SnssWriter WindowClosed(int window) => Command(17, [.. BitConverter.GetBytes(window), .. BitConverter.GetBytes(0L)]);

    public SnssWriter Pruned(int tab, int index, int count) => Ints(24, tab, index, count);

    /// <summary>A navigation: the pickle of size, tab, index, address (length, bytes, padded to 4) and a title length the parser must not read.</summary>
    public SnssWriter Navigation(int tab, int index, string url)
    {
        var address = Encoding.UTF8.GetBytes(url);
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(tab));
        body.AddRange(BitConverter.GetBytes(index));
        body.AddRange(BitConverter.GetBytes(address.Length));
        body.AddRange(address);
        while (body.Count % 4 != 0)
        {
            body.Add(0);
        }

        body.AddRange(BitConverter.GetBytes(0));
        return Command(6, [.. BitConverter.GetBytes(body.Count), .. body]);
    }

    public SnssWriter Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>Checks of <see cref="ChromiumSession.Parse"/> against synthesised session bytes.</summary>
internal static class SessionChecks
{
    private const string A = "https://a.example/";
    private const string B = "https://b.example/";
    private const string C = "https://c.example/";

    private static List<SessionWindow>? Parse(SnssWriter writer) => ChromiumSession.Parse(writer.ToArray());

    private static string Show(List<SessionWindow>? windows) =>
        windows is null ? "null" : string.Join(" | ", windows.Select(w => string.Join(",", w.Urls)));

    private static SnssWriter History() =>
        new SnssWriter().TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, A).Navigation(10, 1, B).Navigation(10, 2, C);

    public static void Run()
    {
        // Two windows, three tabs, in tab order (the tabs were written out of order).
        var basic = new SnssWriter()
            .TabWindow(1, 10).TabIndex(10, 1).Navigation(10, 0, B)
            .TabWindow(1, 11).TabIndex(11, 0).Navigation(11, 0, A)
            .TabWindow(2, 20).TabIndex(20, 0).Navigation(20, 0, C);
        var result = Parse(basic);
        Check(result is { Count: 2 } && result[0].Id == 1 && result[0].Urls.SequenceEqual([A, B]) && result[1].Urls.SequenceEqual([C]), "2 windows and 3 tabs give the right addresses in tab order: " + Show(result));

        // Closed tabs and windows, and popups.
        var closed = new SnssWriter()
            .TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, A)
            .TabWindow(1, 11).TabIndex(11, 1).Navigation(11, 0, B).TabClosed(11)
            .TabWindow(2, 20).TabIndex(20, 0).Navigation(20, 0, C).WindowClosed(2)
            .TabWindow(3, 30).TabIndex(30, 0).Navigation(30, 0, B).WindowType(3, 1)
            .WindowType(1, 0);
        result = Parse(closed);
        Check(result is { Count: 1 } && result[0].Urls.SequenceEqual([A]), "a closed tab, a closed window and a popup (type 1) are left out: " + Show(result));

        // The selected navigation; the highest index when none is selected.
        Check(Show(Parse(History().Selected(10, 1))) == B, "the selected navigation index is honoured");
        Check(Show(Parse(History())) == C, "without a selected index the highest navigation is used");
        Check(Show(Parse(History().Selected(10, 9))) == C, "a selected index that does not exist falls back to the highest");

        // Pruning.
        Check(Show(Parse(History().PrunedFromBack(10, 1))) == A, "pruning from the back drops the navigations from that index on");
        Check(Show(Parse(History().Selected(10, 1).PrunedFromFront(10, 1))) == B, "pruning from the front shifts the indices and the selection together");
        Check(Show(Parse(History().PrunedFromFront(10, 2))) == C, "pruning from the front leaves the last navigation");
        Check(Show(Parse(History().Selected(10, 2).Pruned(10, 1, 1))) == C, "pruning a range shifts the higher indices and the selection");
        Check(Show(Parse(History().Selected(10, 1).Pruned(10, 1, 1))) == C, "a selection inside a pruned range falls back to the highest");
        Check(Show(Parse(History().Navigation(10, 1, "https://new.example/").Selected(10, 1))) == "https://new.example/", "a later navigation at an index replaces the earlier one");

        // A tail that was still being written is ignored.
        var truncated = new SnssWriter().TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, A).Raw(0x64, 0x00, 0x06, 0x01, 0x02);
        Check(Show(Parse(truncated)) == A, "a truncated tail still parses");

        // Fail closed.
        Check(ChromiumSession.Parse(new SnssWriter(2).TabWindow(1, 10).ToArray()) is null, "version 2 (encrypted) gives null");
        Check(ChromiumSession.Parse(new SnssWriter(4).TabWindow(1, 10).ToArray()) is null, "version 4 (encrypted) gives null");
        Check(ChromiumSession.Parse(new SnssWriter(3).TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, A).ToArray()) is { Count: 1 }, "version 3 is accepted");
        var badMagic = basic.ToArray();
        badMagic[0] = (byte)'X';
        Check(ChromiumSession.Parse(badMagic) is null, "a bad magic gives null");
        Check(ChromiumSession.Parse([]) is null && ChromiumSession.Parse("SNSS"u8) is null, "data shorter than the header gives null");
        Check(ChromiumSession.Parse(new SnssWriter().Command(0, 1, 2, 3).ToArray()) is null, "a record shorter than its command needs gives null");
        Check(ChromiumSession.Parse(new SnssWriter().Command(6, [.. BitConverter.GetBytes(60), .. new byte[8], .. BitConverter.GetBytes(1000), .. new byte[8]]).ToArray()) is null, "an address longer than its record gives null");

        var stopped = new SnssWriter().TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, A).Raw(0, 0).TabWindow(2, 20).TabIndex(20, 0).Navigation(20, 0, B);
        Check(Show(Parse(stopped)) == A, "a 0-length record stops parsing");

        // Only web pages; unknown commands (including the marker 255) are skipped.
        var pages = new SnssWriter()
            .TabWindow(1, 10).TabIndex(10, 0).Navigation(10, 0, "chrome://newtab/")
            .TabWindow(1, 11).TabIndex(11, 1).Navigation(11, 0, A).Command(255).Command(33, 1, 2, 3)
            .TabWindow(2, 20).TabIndex(20, 0).Navigation(20, 0, "chrome://settings/");
        result = Parse(pages);
        Check(result is { Count: 1 } && result[0].Urls.SequenceEqual([A]), "chrome:// pages are filtered out and windows left with no address are dropped: " + Show(result));
    }
}
