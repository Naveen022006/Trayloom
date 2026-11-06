// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.WindowTools.Modes;
using static Trayloom.Modes.Harness.Program;

namespace Trayloom.Modes.Harness;

/// <summary>Checks of <see cref="ModesDraft"/>, the pure state behind the Modes form in the island: validation, items, pasted addresses and the capture ticks.</summary>
internal static class DraftChecks
{
    public static void Run()
    {
        Validation();
        Items();
        WebItems();
        Capture();
        Result();
    }

    private static ModeItem App(string exe) => new() { Kind = ModeItemKind.App, Name = exe, Exe = exe };

    private static void Validation()
    {
        var stored = new List<Mode> { new() { Id = "m1", Name = "Coding" }, new() { Id = "m2", Name = "Study" } };

        var fresh = ModesDraft.ForNew();
        Check(fresh.Problem(stored, true) == DraftProblem.NameMissing, "a new draft has no name yet");
        fresh.Mode.Name = "   ";
        Check(fresh.Problem(stored, true) == DraftProblem.NameMissing, "a name of spaces is no name");
        fresh.Mode.Name = " coding ";
        Check(fresh.Problem(stored, true) == DraftProblem.NameTaken, "a name another mode has is refused, ignoring case and spaces");
        fresh.Mode.Name = "Gaming";
        Check(fresh.Problem(stored, true) == DraftProblem.None, "a free name is fine");
        Check(fresh.Problem(stored, false) == DraftProblem.LimitReached, "a new mode is refused when twelve modes exist");

        var edit = ModesDraft.ForEdit(stored[0]);
        Check(edit.Problem(stored, false) == DraftProblem.None, "an edited mode keeps its own name and needs no free slot");
        edit.Mode.Name = "study";
        Check(edit.Problem(stored, false) == DraftProblem.NameTaken, "an edited mode cannot take another mode's name");
        Check(!ReferenceEquals(edit.Mode, stored[0]), "editing works on a copy");
        Check(edit.EditId == "m1" && !edit.IsNew, "an edit draft knows which mode it replaces");
    }

    private static void Items()
    {
        var draft = ModesDraft.ForNew();
        var first = draft.AddItems([App(@"c:\a.exe"), App(@"c:\b.exe")]);
        Check(first.Added == 2 && !first.HitLimit, "two apps are added");
        Check(draft.Mode.Items.Select(i => i.Id).Distinct().Count() == 2 && draft.Mode.Items.All(i => i.Id.Length > 0), "added items get distinct ids");

        var again = draft.AddItems([App(@"C:\A.EXE")]);
        Check(again.Added == 0 && draft.Mode.Items.Count == 2, "the same app twice is added once, whatever the case");

        Check(draft.Remove(draft.Mode.Items[0].Id) && draft.Mode.Items.Count == 1, "an item can be removed by id");
        Check(!draft.Remove("nope"), "removing an unknown id does nothing");
        var ids = draft.AddItems([App(@"c:\c.exe")]);
        Check(ids.Added == 1 && draft.Mode.Items.Select(i => i.Id).Distinct().Count() == 2, "an id freed by a removal is not handed out twice");

        var full = ModesDraft.ForNew();
        var many = full.AddItems(Enumerable.Range(0, 25).Select(i => App($@"c:\app{i}.exe")));
        Check(full.Mode.Items.Count == ModesMath.MaxItems && many.Added == ModesMath.MaxItems && many.HitLimit, "twenty items at most, and the form is told something was left out");
        Check(!full.CanAddItem, "a full mode takes no more");
        Check(full.AddItems([App(@"c:\extra.exe")]).HitLimit, "adding to a full mode reports the limit");

        var front = ModesDraft.ForNew();
        front.AddItems([App(@"c:\x.exe"), App(@"c:\y.exe")]);
        front.Mode.Items[1].Front = true;
        front.EnsureFront();
        Check(front.Mode.Items.Count(i => i.Front) == 1 && front.Mode.Items[1].Front, "exactly one front item, the marked one");
        front.Mode.Items.ForEach(i => i.Front = false);
        front.EnsureFront();
        Check(front.Mode.Items[0].Front && front.Mode.Items.Count(i => i.Front) == 1, "with none marked the first is the front item");
    }

    private static void WebItems()
    {
        var draft = ModesDraft.ForNew();
        draft.BrowserId = @"c:\browsers\chrome.exe";
        draft.Profile = "Profile 1";

        var one = draft.PastedWebItem("github.com", out var rejected);
        Check(one is not null && rejected == 0 && one.Urls.SequenceEqual(["https://github.com/"]) && one.Name == "github.com", "one pasted host becomes one item named after it");
        Check(one!.Kind == ModeItemKind.Web && one.Browser == @"c:\browsers\chrome.exe" && one.Profile == "Profile 1", "the item takes the chosen browser and profile");

        var several = draft.PastedWebItem("https://www.example.com/a\nhttps://www.example.com/a  docs.example.org/x\njavascript:alert(1)\r\nfile:///c:/x", out rejected);
        Check(several is not null && several.Urls.Count == 2 && rejected == 2, "pasted lines are normalised, de-duplicated and the non-web ones counted");
        Check(several!.Name.Contains("example.com", StringComparison.Ordinal) && !several.Name.Contains("www.", StringComparison.Ordinal), "a group is named after the first host, without www");

        Check(draft.PastedWebItem("  \n ", out rejected) is null && rejected == 0, "blank text adds nothing");
        Check(draft.PastedWebItem("javascript:alert(1)", out rejected) is null && rejected == 1, "only rejected entries add nothing and are counted");

        var added = draft.AddItems([several]);
        var twice = draft.AddItems([draft.PastedWebItem("https://www.example.com/a docs.example.org/x", out _)!]);
        Check(added.Added == 1 && twice.Added == 0, "the same pages pasted twice are one item");

        var windows = new List<(int, IReadOnlyList<string>)>
        {
            (1, ["https://a.example/", "javascript:void(0)", "b.example/page"]),
            (2, [.. Enumerable.Range(0, 60).Select(i => "https://many.example/" + i)]),
        };
        var items = draft.TabWindowItems("Chrome", windows);
        Check(items.Count == 3, "a window of sixty tabs is split in two items of at most fifty");
        Check(items[0].Urls.Count == 2 && items[0].Name == ModesStrings.BrowserWindow("Chrome", 1), "tab windows drop non-web tabs and are named by browser and number");
        Check(items[1].Urls.Count == 50 && items[2].Urls.Count == 10 && items[2].Name == ModesStrings.BrowserWindow("Chrome", 2), "the split keeps the window's number");
        Check(ModesDraft.HostOf("https://www.Example.com/x") == "example.com" && ModesDraft.HostOf("not a url") == "not a url", "HostOf strips www and leaves other text");
    }

    private static ModeCapture Sample() => new(
        [
            new CapturedItem(App(@"c:\code.exe"), true, "Code", 1),
            new CapturedItem(new ModeItem { Kind = ModeItemKind.Web, Name = "Chrome window 1", Urls = ["https://github.com/"] }, true, "3 tabs", 1),
            new CapturedItem(new ModeItem { Kind = ModeItemKind.Path, Name = "notes", Path = @"d:\notes" }, false, string.Empty, 1),
        ],
        ["Private windows are never saved."]);

    private static void Capture()
    {
        var draft = ModesDraft.ForNew();
        Check(draft.Captured is null && draft.TickedCount == 0 && draft.AddTicked().Added == 0, "nothing captured, nothing to add");

        draft.SetCapture(Sample());
        Check(draft.TickedCount == 2, "entries start ticked as the capture says");
        draft.Tick(0, false);
        draft.Tick(1, false);
        Check(draft.TickedCount == 0, "all unticked leaves nothing to add");
        Check(draft.AddTicked().Added == 0 && draft.Captured is not null && draft.Mode.Items.Count == 0, "with no tick nothing is added and the list stays");

        draft.Tick(2, true);
        draft.Tick(0, true);
        var result = draft.AddTicked();
        Check(result.Added == 2 && draft.Mode.Items.Select(i => i.Kind).SequenceEqual([ModeItemKind.App, ModeItemKind.Path]), "the ticked entries are added in order");
        Check(draft.Captured is null && draft.Unticked.Count == 0, "the capture list closes after adding");

        draft.SetCapture(Sample());
        draft.Tick(2, true);
        var repeat = draft.AddTicked();
        Check(repeat.Added == 1 && draft.Mode.Items.Count == 3, "what is already in the mode is not added twice");
        Check(draft.Mode.Items.Select(i => i.Id).Distinct().Count() == 3, "items added from a capture get fresh ids");

        var source = Sample();
        var fresh = ModesDraft.ForNew();
        fresh.SetCapture(source);
        fresh.AddTicked();
        fresh.Mode.Items[0].Args = "changed";
        Check(source.Items[0].Item.Args.Length == 0, "adding copies the captured item, so the capture is never changed");
    }

    private static void Result()
    {
        var draft = ModesDraft.ForNew();
        draft.Mode.Name = "  Focus  ";
        draft.AddItems([App(@"c:\a.exe"), App(@"c:\b.exe")]);
        draft.Mode.Items[1].Front = true;
        var mode = draft.ToMode();
        Check(mode.Name == "Focus" && mode.Id.Length == 0, "the result has a trimmed name and no id (the runtime makes one)");
        Check(mode.Items.Count(i => i.Front) == 1 && mode.Items[1].Front, "the result has one front item");
        Check(!ReferenceEquals(mode.Items, draft.Mode.Items), "the result is a copy");

        var stored = new Mode { Id = "m7", Name = "Old", Color = "teal", Glyph = ModesMath.Glyphs[3], OnLeave = "close", OnEnter = new ModeEnterActions { KeepAwake = "60", Volume = 50, Timer = true }, Items = [App(@"c:\a.exe")] };
        var edit = ModesDraft.ForEdit(stored);
        edit.Mode.Name = "New";
        var back = edit.ToMode();
        Check(back.Id == "m7" && back.Name == "New" && back.Color == "teal" && back.OnLeave == "close" && back.OnEnter.Volume == 50 && back.OnEnter.Timer, "an edit keeps the id and every other setting");
        Check(stored.Name == "Old", "the stored mode is untouched until the runtime is given the result");
    }
}
