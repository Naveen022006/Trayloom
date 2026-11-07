# 10b: Modes that switch themselves, and the mode badge (v0.8.0, packages P0 and P2)

Source: `10-next-batch-0.8.md`. This file records what was built. Behaviour of Modes itself stays in `08-modes-and-camera-leave.md`.

## P0: the contract (`src/Trayloom.Core/ModesContracts.cs`)

```csharp
public sealed record ModeSummary(string Id, string Name, string Glyph, string ColorHex);

public interface IModesService
{
    IReadOnlyList<ModeSummary> List { get; }
    string? ActiveId { get; }
    bool IsSwitching { get; }
    bool Enter(string modeId, string trigger);   // triggers add "auto" and "focus"
    bool Leave(string trigger);
    event Action? Changed;                        // raised on the UI thread
}
```

- `ColorHex` is `#RRGGBB`, taken from the same name-to-colour map the tiles use (`ModesLook`).
- The Modes feature publishes it with `Services.Provide<IModesService>(runtime)` in `Start` and withdraws it in `Stop`. Consumers must handle `Services.Get<IModesService>()` being null.
- Trigger strings: the existing `tile`, `panel`, `quick-panel`, `command-bar`, `shortcut`, `settings`, plus `"auto"` (a rule or an accepted question) and `"focus"` (the Focus session, package P3).

## What starts a mode

An app rule fires when the app's window first comes to the **front**, because Windows has no admin-free "a program started" event. The foreground WinEvent hook (the same one Screen Time uses) reads the process image name and creation time. The key `exe|pid|created` is remembered, so a program fires once per run. Programs that are already running when the rules load (Trayloom restart, rule added) are remembered without firing. Background-only processes cannot trigger a rule.

A time rule is checked by a 30 s timer that runs only while an enabled time rule exists. It fires when the day is on and `at <= now < at + 30 min`, once per day (`modesAutoFired`, `{ruleId: "yyyy-MM-dd"}`). Locked or Away at that moment: it waits inside the grace window. A rule that is decided (started, asked, or skipped because a mode is active) is recorded for the day.

Decision (`ModesAutoMath.Decide`): any active mode means skip; otherwise Ask or Enter. Safety rules:

- a rule never replaces an active mode;
- Ask is the default for new rules;
- "Leave at" leaves only when `modesAutoStartedBy` is this rule and the active mode is still the rule's mode; the key is cleared as soon as the active mode changes or is left, and before the leave is requested;
- skipped silently while Away mode is on, the session is locked, Trayloom is elevated, or a switch is running;
- entering a mode never closes anything.

Asking: `ModesRuntime.Suggest` shows the question on the island (through the `modes.notice` strip) when the island is presenting, otherwise as a toast. The Modes island page shows a card above the tiles for 2 minutes with **Start {mode}** (`AcceptSuggestion`, which tells the runner first so the rule claims the mode, then `Enter(id, "auto")`) and **Not now**.

## The mode badge

Strip `modes.resting` (`ModesRestingStrip`, feature `modes`): the mode's icon (11 px, mode colour) and name (9 px, max 90 wide, ellipsis); tooltip `Mode: {name}`. It returns null unless `modesShowInIsland` is on and a mode is active. The island's `Resting` view places it in the left wing if free and the companion is not on the left, otherwise in the right wing if free and the companion is not on the right, and widens the island to hold it symmetrically. It is not shown when music or battery fills the wings, or when a screen time or agents strip fills the middle. `LiveActivities` already ignores `.resting` strips, so any live activity or notice replaces it.

## Files

- Core: `ModesContracts.cs` (new).
- Windows/Modes (new): `ModesRuntime.Service.cs`, `ModesAuto.cs` (rule model, `ModesAutoMath`, `ModesRulesStore`, keys), `ModesAutoRunner.cs`, `ModesAutoSettings.cs`, `ModesRestingStrip.cs`, `ModesAutoStrings.cs`.
- Windows/Modes (changed): `ModesFeature.cs` (provide the service and the runner, stop the runner first), `ModesIsland.cs` (suggestion card in `ModesIslandView`).
- Island (one hunk): `ClosedViews.cs` `Resting`, the badge placement.
- Tests: `tests/Trayloom.Modes.Harness/AutoChecks.cs` and one line in `Program.cs`.

## Settings keys

| Key | Default | Meaning |
|---|---|---|
| `modesAutoEnabled` | true | Master switch. With no rules nothing is hooked. |
| `modesShowInIsland` | true | The badge. |
| `modesAutoStartedBy` | `""` | Id of the rule that started the active mode. |
| `modesAutoFired` | `""` | JSON `{ruleId: "yyyy-MM-dd"}`. |

`rules.json` (beside `modes.json`, written atomically): version 1, at most 24 rules of `{id, modeId, kind app|time, app, appName, at, until, days, ask, enabled}`. `app` is the executable file name only; `appName` is the display name saved with the rule. An unreadable file is kept as `rules.json.bad` and the rules start empty.

## Strings (Quick tools page, card "Start modes automatically", anchor `modes-auto`, order 86)

Card: "Start modes automatically", the master caption, "Show the active mode in the island" and its caption. List and form: "No rules yet.", "Add rule", "Mode", "Start when", "An app opens", "At a time", "App", "Choose an app", "Browse…", "Time", "Days", "Leave at" and its caption, "Ask before starting" and its caption, "Save", "Cancel", "Edit", "Delete", "Delete this rule?". Validation: "Enter a time like 18:30.", "Leave at must be later than the start time.", "Choose an app.", and two added: "Choose at least one day.", "Couldn’t save the rule.". Captions: "When {0} opens", "{0} at {1}", " · Ask first", " · Starts right away", " · Leaves at {0}", " · Off", "Every day", "Weekdays", "Weekends", "No days", Mon to Sun. Suggestion: "{0} opened · Start {1}?", "It’s {0} · Start {1}?", the two toast texts, "Start {0}", "Not now". A rule whose mode was deleted shows "This mode doesn’t exist anymore." and is skipped.

## Privacy and cost

Logs carry rule and mode ids and counts only. `rules.json` holds executable file names and times. The foreground hook costs one image-name query per front-window change and is installed only while an app rule is enabled; the 30 s timer runs only while a time rule is enabled.

## Manual tests

1. Rule "Coding when Code opens", Ask first: open Code; the question appears; **Start Coding** on the Modes page starts it.
2. Same without Ask: Coding starts. With Coding active, reopen Code: nothing.
3. Restart Trayloom with Code open: nothing fires.
4. Time rule two minutes ahead: fires once; after a restart it does not fire again today.
5. "Leave at": leaves only a mode that rule started and that is still active.
6. Away mode on: rules are skipped silently.
7. Badge: shows at rest, hides while music plays, does not collide with the companion on either side.
