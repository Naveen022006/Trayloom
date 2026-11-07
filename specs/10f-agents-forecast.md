# 10f: Claude limit forecast (Agents)

Part of the 0.8.0 batch (`10-next-batch-0.8.md`, package P6). It adds one answer to the plan-limit display: *when* a limit would
run out at the pace so far. It is arithmetic on readings Trayloom already has; nothing new is read, stored or sent.

## Rule
For a window with a used percent, a length and a reset time: `rate = used / elapsed`, where `elapsed = observedAt - (reset - length)`,
and `eta = observedAt + (100 - used) / rate`. The forecast is `eta` only when `now < eta < reset`; otherwise there is none.

No forecast when any of these holds:
- the window is Renewed or NotStarted, or has no length or no reset time (`AgentLimitSupport.DisplayedReset` is null);
- used is under 10 % or at 100 % or more;
- the reading is more than 30 minutes old;
- the window has run less than max(10 minutes, 5 % of its length) at the reading.

Code: `AgentLimitForecast.cs` (`RatePerMinute`, `RunsOutAt`, `ShouldWarn`, `WarnKey`, `Text`). Times are UTC like `ResetsAt` and
shown in local time with the weekday when not today (`LimitPresenter.Clock`).

## Where it shows
- **Limits card** (island Agents page): a row with no other note gets the note "Runs out around {time} at this pace" (the tooltip
  has it too). It never replaces "Limit reached", the paused-by-weekly note or the "starts with your next message" note.
- **Island notice**: title is the usual "{Provider} · {window}", detail "Runs out around {time}", orange, same duration as other notices.

## Alert
- Checked in the existing usage tick, right after renewals (`ReportForecasts`), for every provider's windows, only once the first
  full read is done (`ReportsTransitions`).
- Fires when the forecast exists and `eta - now <= lead`. Once per window and renewal: the key is provider, window id and the
  reset rounded to a quarter hour (sources restate resets by seconds). The set is cleared with the other alert state on reset.
- Dropped at `Report` when the provider is off, the setting is off, or the plan has no such window (same gate as limit warnings).
- Independent of the "Near a plan limit" threshold alert; either can be on without the other.

## Settings (Settings > Dynamic Island > AI Agents > Alerts)
| Key | Default | Meaning |
|---|---|---|
| `notchAgentsForecastAlert` | true | Switch "Before a limit runs out", caption "Warns when, at your pace so far, a limit would run out before it resets." |
| `notchAgentsForecastLead` | 30 | Menu "Warn", "15 min before", "30 min before", "60 min before" (stored as a number; nearest option is used) |

## Strings
"Runs out around {0} at this pace", "Runs out around {0}", "Before a limit runs out", the caption above, "Warn", "{0} min before".
All through `Loc.T` / `Loc.F` in `AgentStrings.cs`; `Strings.*.json` untouched.

## Privacy and cost
No new data and no logging. Negligible cost inside the existing tick.

## Harness
`tests/Trayloom.Agents.Harness/ForecastChecks.cs`: rate from a reading, projected run-out, clamping to the reset (later or equal
means none), the 10 % / 100 % / early / stale / renewed / no-reset / no-length cases, `ShouldWarn` at 29, 30 and 31 minutes,
`WarnKey` changing with the reset, and the Limits card note.

## Manual tests
1. Claude Code active, 5-hour window above 10 % used after more than 15 minutes with a pace above the window: the Limits card
   shows "Runs out around ...".
2. Set Warn to 60 min: the notice appears once and does not repeat for the same window.
