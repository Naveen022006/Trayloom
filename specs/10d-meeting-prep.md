# 10d: Meeting heads-up (Island)

Part of the 0.8.0 batch (`10-next-batch-0.8.md`, package P4). The next meeting appears in the closed island shortly before it
starts, with a Join button, and the Calendar page offers a card to check the microphone and camera first.

## Behaviour
- **When it shows.** A timed (not all-day) event is shown from `start - lead` until `min(end, start + 10 min)`. It needs a
  video link when "Only meetings with a video link" is on, and it must not be dismissed. The earliest start wins; on a tie
  the event with a link wins.
- **Gates.** The island is on, the `notch-calendar` feature is available, `notchCalendarEnabled` and `notchMeetingPrep` are on and
  the Calendar page is visible. If any is off there is no activity.
- **Polling.** `MeetingService` re-picks every 15 s, whenever the calendar changes, when a setting or the feature runtime
  changes, and once a second from the strip (only to end a meeting on time). A new meeting makes the mascot react ("perk").
- **Priority** (`IslandCore.LiveActivities`): timer, watch, downloads, agents when waiting (or no music), **meeting**, microphone
  in use, calendar countdown, music, agents otherwise, keep awake, other strips. The calendar countdown skips the event the
  meeting already shows (compared by `CountdownKey`).
- **Strip** (`ActMeeting = "meeting"`, opens module `calendar`): colour dot, title (max 120, ellipsis, "Untitled event" when
  empty), countdown "4:32" or "Now", and a **Join** pill when the event has a link. Max width 320. The mic-in-use dot is added as
  for the other strips (`ClosedViews.Dotted` is now internal).
- **Join** opens the link with `CalendarService.OpenLink` (https only; anything else is ignored) and dismisses the heads-up.
- **Not now** dismisses the event until midnight (the set is in memory only).
- **Card** (top of the Calendar page while a meeting is current): "Up next", title, "10:30 – 11:00 · in 4 min" (or "· now"),
  Join, Check microphone, Check camera (when available), Not now.
  - **Check microphone** starts `MicCheck`, a WASAPI capture on the default Communications capture endpoint. It shows
    "Microphone: {name}", a level bar and "Speak to test". When the endpoint is muted it shows "Muted" and an **Unmute** button
    that calls `Audio.SetMicMuted(false)` on an explicit click only. Errors show inline. Clicking again stops it.
  - The check runs at most 15 s, ends when the page is left (`CalendarPage.Hidden`), and keeps only a peak value in memory.
    Windows shows its microphone indicator while it runs. The meter is throttled to 20 Hz.
  - **Check camera** opens the island's Camera mirror page when that page is visible, otherwise runs the Camera preview tool's
    action when that feature is available, otherwise the button is hidden. The mirror page still starts the camera only from its
    own "Open camera" button; a start that follows the card's click lets the camera go after 30 s, and leaving the page stops
    it as before.
- **Failure.** No calendar or no access: no activity. A non-https link is refused by `OpenLink`. Device errors are shown on the card.

## Settings (Content > Calendar)
| Key | Default | Values |
|---|---|---|
| `notchMeetingPrep` | true | |
| `notchMeetingLead` | 5 | 2, 5, 10, 15 (minutes) |
| `notchMeetingVideoOnly` | true | |

Strings: "Meeting heads-up" (caption: "Shows your next meeting in the closed island a few minutes before it starts, with Join. Titles may
appear in screen captures."), "Show it", "{0} min before", "Only meetings with a video link", "Up next", "Untitled event", "Now",
"in {0}", "Join", "Not now", "Check microphone", "Check camera", "Microphone: {0}", "Speak to test", "Muted", "Unmute",
"No microphone found", "Can’t open the microphone. Check Settings › Privacy & security › Microphone."

## Privacy
Titles show in the closed island, as the setting says. Audio is never stored or sent. Logs say only that a heads-up was shown or
ended, and the kind of a device error; never a title, link or device name.

## Code
`Prefs.Meeting.cs`, `MeetingPrep.cs` (`MeetingRules`, `MeetingService`, `MicCheck`, `MeetingStrip`, `MeetingSettings`),
`MeetingPrepCard.cs`. Small hunks in `IslandCore.cs`, `IslandWindow.State.cs`, `ClosedViews.cs`, `CalendarService.cs`
(`Upcoming`), `Pages.Calendar.cs`, `Pages.Live.cs` (30 s camera check limit), `Settings.Content.cs`, `IslandFeature.cs`
(`dev-meeting`), `Prefs.cs`.

## Tests
Harness: `tests/Trayloom.Island.Harness/MeetingChecks.cs` (Pick, Until, Countdown, PeakOf). Never opens a device.

Manual:
1. `dev-meeting` (TRAYLOOM_DEV=1): about a minute later (lead 5) the strip shows.
2. Join opens the browser and the strip goes.
3. With music playing the meeting outranks it; a waiting Claude permission outranks the meeting.
4. Calendar page, Check microphone: the bar moves; with the mic muted, Unmute works. Leave the page: the capture stops.
5. Check camera opens the mirror page; Open camera, and it stops by itself after 30 s.
6. A real Google event with a Meet link; the countdown for the same event does not show twice.
