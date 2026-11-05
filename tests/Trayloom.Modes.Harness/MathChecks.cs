// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.WindowTools.Modes;
using static Trayloom.Modes.Harness.Program;

namespace Trayloom.Modes.Harness;

/// <summary>Checks of <see cref="ModesMath"/>: sanitising, web addresses, keys and dedupe, argument rules.</summary>
internal static class MathChecks
{
    public static ModesFile Sample()
    {
        var mode = new Mode
        {
            Id = "m3f9a2c1d",
            Name = "Coding",
            Glyph = "\uE943",
            Color = "blue",
            OnEnter = new ModeEnterActions { KeepAwake = "120", Volume = 25, Timer = true },
            OnLeave = "close",
            Items =
            [
                new ModeItem { Id = "i1", Kind = ModeItemKind.App, Name = "Visual Studio Code", Exe = @"c:\users\me\appdata\local\programs\microsoft vs code\code.exe", Args = "\"D:\\src\\trayloom\"", Zone = "leftHalf", Front = true },
                new ModeItem { Id = "i2", Kind = ModeItemKind.App, Name = "Terminal", Exe = @"c:\users\me\appdata\local\microsoft\windowsapps\wt.exe", Args = "-d \"D:\\src\\trayloom\"", Dir = @"D:\src\trayloom", Zone = "rightHalf" },
                new ModeItem { Id = "i3", Kind = ModeItemKind.Web, Name = "GitHub and docs", Browser = "default", Urls = ["https://github.com/Naveen022006/Trayloom", "https://learn.microsoft.com/dotnet/"] },
                new ModeItem { Id = "i4", Kind = ModeItemKind.Path, Name = "Notes", Path = @"D:\notes" },
                new ModeItem { Id = "i5", Kind = ModeItemKind.App, Name = "Calculator", Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Zone = "maximize" },
            ],
        };
        return new ModesFile { Modes = [mode] };
    }

    private static string Json(ModesFile file) => System.Text.Json.JsonSerializer.Serialize(file, ModesStore.Json);

    public static void Run()
    {
        Sanitising();
        WebAddresses();
        KeysAndDedupe();
        OtherRules();
    }

    private static void Sanitising()
    {
        var sample = Sample();
        var text = Json(sample);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<ModesFile>(text, ModesStore.Json)!;
        Check(Json(ModesMath.Sanitize(parsed)) == text, "a file covering all kinds survives serialize, parse and sanitize unchanged");

        var unknown = System.Text.Json.JsonSerializer.Deserialize<ModesFile>(
            """{"v":1,"modes":[{"id":"a","name":"A","items":[{"id":"x","kind":"gizmo","name":"?"},{"id":"y","kind":"path","name":"P","path":"D:\\p"}]}]}""", ModesStore.Json)!;
        var cleaned = ModesMath.Sanitize(unknown);
        Check(cleaned.Modes[0].Items.Count == 1 && cleaned.Modes[0].Items[0].Id == "y", "an unknown item kind is dropped, the rest stays");

        var many = new ModesFile { Modes = [.. Enumerable.Range(0, 13).Select(i => new Mode { Id = "m" + i, Name = "M" + i })] };
        Check(ModesMath.Sanitize(many).Modes.Count == 12, "13 modes are capped to 12");

        var dup = new ModesFile { Modes = [new Mode { Id = "same", Name = "A" }, new Mode { Id = "same", Name = "B" }] };
        var dupClean = ModesMath.Sanitize(dup);
        Check(dupClean.Modes[0].Id == "same" && dupClean.Modes[1].Id == "same-2", "a duplicate mode id gets the suffix -2");

        var twoFront = new Mode
        {
            Id = "f",
            Name = "F",
            Items = [new ModeItem { Id = "a", Kind = ModeItemKind.Path, Path = @"D:\a", Front = true }, new ModeItem { Id = "b", Kind = ModeItemKind.Path, Path = @"D:\b", Front = true }],
        };
        var front = ModesMath.Sanitize(new ModesFile { Modes = [twoFront] }).Modes[0].Items;
        Check(front[0].Front && !front[1].Front, "two front items are reduced to the first");
        var noFront = new Mode { Id = "n", Name = "N", Items = [new ModeItem { Id = "a", Kind = ModeItemKind.Path, Path = @"D:\a" }, new ModeItem { Id = "b", Kind = ModeItemKind.Path, Path = @"D:\b" }] };
        Check(ModesMath.Sanitize(new ModesFile { Modes = [noFront] }).Modes[0].Items[0].Front, "with no front item the first one becomes it");

        var bad = new Mode
        {
            Id = "b",
            Name = "B",
            Color = "chartreuse",
            Glyph = "x",
            OnLeave = "explode",
            Items = [new ModeItem { Id = "a", Kind = ModeItemKind.Path, Path = @"D:\a", Zone = "sideways" }, new ModeItem { Id = "c", Kind = ModeItemKind.Path, Path = @"D:\c", Zone = "topQuarter" }],
        };
        var fixedMode = ModesMath.Sanitize(new ModesFile { Modes = [bad] }).Modes[0];
        Check(fixedMode.Color == "blue", "a colour that is not on the list becomes blue");
        Check(fixedMode.Glyph == ModesMath.Glyphs[0] && fixedMode.OnLeave == "minimize", "a bad glyph and a bad leave choice are put back");
        Check(fixedMode.Items[0].Zone is null && fixedMode.Items[1].Zone is null, "a zone that does not exist, or is not allowed, becomes null");

        var unusable = new Mode
        {
            Id = "u",
            Name = "U",
            Items =
            [
                new ModeItem { Id = "1", Kind = ModeItemKind.App },
                new ModeItem { Id = "2", Kind = ModeItemKind.Web, Urls = ["javascript:alert(1)"] },
                new ModeItem { Id = "3", Kind = ModeItemKind.Path },
            ],
        };
        Check(ModesMath.Sanitize(new ModesFile { Modes = [unusable] }).Modes[0].Items.Count == 0, "apps without a program, webs without an address and paths without a path are dropped");

        var limits = new Mode
        {
            Id = "l",
            Name = new string('n', 60),
            Items = [.. Enumerable.Range(0, 25).Select(i => new ModeItem { Id = "i" + i, Kind = ModeItemKind.Path, Path = @"D:\p" + i })],
        };
        var limited = ModesMath.Sanitize(new ModesFile { Modes = [limits] }).Modes[0];
        Check(limited.Items.Count == 20 && limited.Name.Length == 40, "20 items and 40 characters per name");

        Check(ModesMath.NewModeId().Length == 9 && ModesMath.NewModeId().StartsWith('m'), "a new mode id is m and eight characters");
        Check(ModesMath.NewItemId(sample.Modes[0]) == "i6", "a new item id continues after the items");
    }

    private static void WebAddresses()
    {
        Check(ModesMath.NormalizeUrl("github.com/x") == "https://github.com/x", "a host without a scheme gets https");
        Check(ModesMath.NormalizeUrl("javascript:alert(1)") is null, "javascript: is refused");
        Check(ModesMath.NormalizeUrl("https://u:p@h/") is null, "an address with a user name and password is refused");
        Check(ModesMath.NormalizeUrl("chrome://settings") is null, "chrome:// is refused");
        Check(ModesMath.NormalizeUrl("file:///C:/x.txt") is null && ModesMath.NormalizeUrl("data:text/html,hi") is null, "file: and data: are refused");
        Check(ModesMath.NormalizeUrl("HTTP://Example.com/A") == "http://example.com/A", "the scheme and host are lower-cased, the path keeps its case");
        Check(ModesMath.NormalizeUrl("https://example.com/" + new string('a', 2049 - "https://example.com/".Length)) is null, "2049 characters are refused");
        Check(ModesMath.NormalizeUrl("https://example.com/" + new string('a', 2048 - "https://example.com/".Length)) is not null, "2048 characters are accepted");
        Check(ModesMath.NormalizeUrl("https://example.com/a b") == "https://example.com/a%20b", "a space is percent-encoded");
        Check(ModesMath.NormalizeUrl("   ") is null && ModesMath.NormalizeUrl(null) is null, "blank text is refused");

        var list = ModesMath.ParseUrlList("a.com\r\nb.com  a.com\tjavascript:x\n\nc.org", out var rejected);
        Check(list.SequenceEqual(["https://a.com/", "https://b.com/", "https://c.org/"]), "pasted text is split on spaces and line breaks, duplicates dropped, the first kept");
        Check(rejected == 1, "one rejected entry is counted");
        var sixty = ModesMath.ParseUrlList(string.Join('\n', Enumerable.Range(0, 60).Select(i => $"h{i}.example.com")), out var none);
        Check(sixty.Count == 50 && none == 0, "60 entries are capped to 50");
        Check(ModesMath.ParseUrlList("a.com/x,y", out _).SequenceEqual(["https://a.com/x,y"]), "commas do not split an address");

        var groups = ModesMath.GroupUrls([.. Enumerable.Range(0, 120).Select(i => "https://e.com/" + i)]);
        Check(groups.Select(g => g.Count).SequenceEqual([50, 50, 20]), "120 addresses give windows of 50, 50 and 20");

        Check(ModesMath.ChromiumArgs("Profile 1", ["https://a.com/", "https://b.com/"]).SequenceEqual(["--profile-directory=Profile 1", "--new-window", "https://a.com/", "https://b.com/"]), "Chromium arguments: profile, new window, addresses");
        Check(ModesMath.ChromiumArgs("", ["https://a.com/"]).SequenceEqual(["--new-window", "https://a.com/"]), "Chromium arguments without a profile");
    }

    private static void KeysAndDedupe()
    {
        var a = new ModeItem { Kind = ModeItemKind.App, Exe = @"C:\Tools\Code.exe", Args = " \"D:\\Src\" " };
        var b = new ModeItem { Kind = ModeItemKind.App, Exe = @"c:\tools\code.EXE", Args = "\"d:\\src\"" };
        Check(ModesMath.ItemKey(a) == ModesMath.ItemKey(b), "an app key ignores the case of the program and the arguments");
        var w1 = new ModeItem { Kind = ModeItemKind.Web, Browser = "Default", Urls = ["https://a.com/", "https://b.com/"] };
        var w2 = new ModeItem { Kind = ModeItemKind.Web, Browser = "default", Urls = ["https://b.com/", "https://a.com/"] };
        Check(ModesMath.ItemKey(w1) != ModesMath.ItemKey(w2), "a web key keeps the order of the addresses");
        Check(ModesMath.ItemKey(new ModeItem { Kind = ModeItemKind.Path, Path = @"D:\Notes\" }) == ModesMath.ItemKey(new ModeItem { Kind = ModeItemKind.Path, Path = @"d:\notes" }), "a path key ignores case and a trailing separator");
        Check(ModesMath.ItemKey(new ModeItem { Kind = ModeItemKind.App, Aumid = "X!App", Exe = @"C:\a.exe" }) == "pkg|X!App", "a packaged app is keyed by its AppUserModelId");

        var first = new ModeItem { Id = "1", Kind = ModeItemKind.App, Exe = @"c:\a.exe" };
        var second = new ModeItem { Id = "2", Kind = ModeItemKind.App, Exe = @"C:\A.EXE" };
        var third = new ModeItem { Id = "3", Kind = ModeItemKind.Path, Path = @"D:\x" };
        var deduped = ModesMath.Dedupe([first, second, third]);
        Check(deduped.Count == 2 && deduped[0].Id == "1", "dedupe keeps the first of equal keys");

        var leaving = new Mode { Id = "l", Items = [first, third, new ModeItem { Id = "4", Kind = ModeItemKind.Path, Path = @"D:\y" }] };
        var entering = new Mode { Id = "e", Items = [second] };
        Check(ModesMath.ToPutAway(leaving, entering).Select(i => i.Id).SequenceEqual(["3", "4"]), "put away leaves out the items the entered mode shares");
        Check(ModesMath.ToPutAway(leaving, null).Count == 3, "leaving to no mode puts everything away");
    }

    private static void OtherRules()
    {
        Check(ModesMath.ArgsForFolder(@"C:\x\Code.exe", @"D:\src", out var dir1) == "\"D:\\src\"" && dir1 == string.Empty, "an editor takes the quoted folder");
        Check(ModesMath.ArgsForFolder("wt.exe", @"D:\src", out var dir2) == "-d \"D:\\src\"" && dir2 == @"D:\src", "Windows Terminal takes -d and starts in the folder");
        Check(ModesMath.ArgsForFolder("explorer.exe", @"D:\src", out var dir3) == "\"D:\\src\"" && dir3 == string.Empty, "File Explorer takes the quoted folder");
        Check(ModesMath.ArgsForFolder("notepad.exe", @"D:\src", out var dir4, "old") == "old" && dir4 == @"D:\src", "another program keeps its arguments and starts in the folder");
        Check(ModesMath.IsForwarder(@"C:\x\code.exe") && ModesMath.IsForwarder("Cursor.exe") && !ModesMath.IsForwarder("notepad.exe"), "editors are forwarders, notepad is not");
        Check(ModesMath.IsBlockedFile(@"D:\a.ps1") && ModesMath.IsBlockedFile(@"D:\a.LNK") && !ModesMath.IsBlockedFile(@"D:\a.pdf"), "scripts and shortcuts are blocked, a PDF is not");
    }
}
