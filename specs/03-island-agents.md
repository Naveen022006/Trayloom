# 03 - Dynamic Island ("Notch") and AI Agents (the "Claude bar" / "Codex bar")

Status: replica spec for Trayloom. Follows `SPEC_RULES.md` (per feature: Layout, Strings, Settings, Behaviour, Windows mapping,
Gap vs Trayloom). Source of truth: Swift in `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint` (the island is `Notch` in code), the
reference GIF `Resources/Gifs/highlights-notch.gif` and the current Windows port in `E:\REA TEST 1\trayloom\src\Trayloom.Features.Island`.
Product name in all user-visible text: Trayloom (never the source product's name/logo). The feature itself keeps the English name
"Dynamic Island". Segoe Fluent Icons code points are suggestions and must be checked visually.

## How this file is organised
| Part | Content | Section numbers inside the part |
|---|---|---|
| **A** | AI AGENTS in depth: files read on disk (verified on this Windows machine), parsing, plan limits (Claude CLI / Claude app history / Codex app-server / session logs), pricing, summaries, what the island shows, settings, strings | A1-A12 |
| **B** | Island shell: sections/modules, state machine, geometry, motion, gestures, window host, display handling, notices, capsules, settings page (5 tabs) and all `notch*` keys | 1-13 |
| **C** | Island pages: Music (strip, view, queue, lyrics, equalizer bars that follow the music), Controls, Mixer, System, Capture/Camera/Mirror, Keep Awake, Calendar (month + strip), Timer (view/strip/ruler) | 0-8 |
| **D** | Island extensions: Downloads, Files, Clipboard, Scratchpad, Notifications/Notice/Alerts, Lock screen, Companion (mascot), Watch, Accessories | 0-10 |

Cross references such as "Part B 7.7" mean section 7.7 of Part B.

## Windows mapping - one-paragraph summary
The island is a borderless, always-on-top, non-activating (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) layered WPF window pinned to the top-centre
of the chosen monitor (no camera notch: every display uses the "capsule/flat bar" geometry). Media = `GlobalSystemMediaTransportControlsSessionManager`;
equalizer bars = WASAPI loopback (per-process when possible) with a synthetic fallback; calendar = Windows appointment store / Outlook / ICS;
notifications = `UserNotificationListener` (needs package identity) with a UIA fallback; the lock-screen island is impossible (secure desktop) and is
replaced by lock/unlock session notifications; "watch" = Windows Graphics Capture + `Windows.Media.Ocr`; accessories = Bluetooth LE/Classic device watcher.
Items that need adaptation or are impossible are flagged "Windows mapping" in each part (Part B 12, Part D 5/6/8/9).

## Top gaps (summary - details per part)
1. Trayloom has **no AI Agents feature at all** (Part A12). 2. Island shell is a tab bar; the source has an Explore gallery, floating quick-access buttons, top row, pin,
context menu, peek, notices, activity picker (Part B 13). 3. Only 4 of 16 sections exist (Music, Timer, Calendar, Downloads); Controls, Mixer, System, Clipboard,
Captures, Files, Tools, Notifications, Camera, Scratchpad, AI Agents, Watch are missing. 4. Music lacks the source list, shuffle, lyrics, queue, tinted cover and the
7-band live equalizer (Part C 1). 5. Settings page is 6 options instead of 5 tabs with ~100 keys (Part B 9-10).

---------------------------------------------------------------------------------------------------------------------------------

# PART A - AI AGENTS (the "Claude bar" / "Codex bar")

Source of truth: `Sources/Vorssaint/Services/AgentUsage/*`, `Services/Notch/NotchAgentSupport.swift`,
`UI/Notch/NotchAgents*.swift`, `UI/Settings/NotchAgentsSettings.swift`, `ClaudeAccountSettings.swift`,
`CodexAccountsSettings.swift`, `Core/NotchAgentStrings.swift`, `Core/ClaudeAccountStrings.swift`,
`Core/CodexAccountStrings.swift`, `Resources/agent-prices.json`.

Product name: "AI Agents" (island section title). Section id `agents`, SF symbol `sparkles`, gallery shortcut letter `g`
(shown as "⌥⌘G" in the Explore gallery; the Windows modifier mapping is defined in Part B section 7.4).
The feature is OFF by default (`notchAgentsEnabled=false`); the user turns it on in Settings > Dynamic Island > Content > AI Agents.
Everything is read-only against files written by the agents themselves: no prompt, reply, file content or token/credential is ever
decoded into anything stored; only counters, model names, times and folder names survive.

Visual reference: `Resources/Gifs/highlights-notch.gif` (first/middle frames): pure-black rounded island (corner radius ~ 24 at expanded
size, flush with the top edge), a 5-column gallery of rounded 12-radius tiles (icon on top, label, shortcut caption), "AI Agents" with the
sparkles icon is the last tile of the third row. Two floating black circular buttons sit left and right of the island (undo, timer / gear,
speaker, sparkles = quick access). When collapsed: a black pill with an orange timer glyph on the left and "14m" on the right, tab buttons
"Timer | Music" below and "+ Combine" (the closed-island activity picker). The agents capsule follows the same pill look: provider mark
left, reading right, camera gap (Windows: no camera, the gap is simply the centre spacer, see 3.2).

---------------------------------------------------------------------------------------------------------------------------------

## A1. Data sources - exactly which files are read (macOS paths and the verified Windows paths)

Home folder = `FileManager.homeDirectoryForCurrentUser` -> Windows `%USERPROFILE%` (`C:\Users\<user>`). All reads are READ-ONLY and
must open files with `FileShare.ReadWrite | FileShare.Delete` (agents keep logs open while they write).

| Agent | What is read | macOS path (home-relative) | Windows path (VERIFIED on this machine) | Notes |
|---|---|---|---|---|
| Claude Code (usage) | every `*.jsonl` (recursive) | `~/.claude/projects/**`, plus `~/.config/claude/projects/**` | `%USERPROFILE%\.claude\projects\<mangled-cwd>\<sessionId>.jsonl` (e.g. folder `C--Users-ADMIN`, 1563 logs here); `%USERPROFILE%\.config\claude\projects` does not exist here but must still be probed | Also honour env `CLAUDE_CONFIG_DIR` if the user has set it (Claude Code does). Subagent logs live at `<project>\<sessionId>\subagents\...\agent-*.jsonl` (also `subagents\workflows\wf_*\agent-*.jsonl`); parent log = `<project>\<sessionId>.jsonl` (the text before the LAST `\subagents\`). Subagents are read after all sessions. The Claude Desktop app's own Code/Cowork sessions are written to the SAME root (project folders named `...AppData-Local-Claude-Data-...`), so one root covers both. |
| Claude Code (profile / plan) | `~/.claude.json` (default profile) | `~/.claude.json` | `%USERPROFILE%\.claude.json` (exists; ~140 KB) | Only these keys are decoded: `oauthAccount.accountUuid`, `oauthAccount.emailAddress`, `oauthAccount.organizationUuid`, `oauthAccount.organizationType` (here `claude_max`), `oauthAccount.organizationRateLimitTier` (here `default_claude_max_20x`) / `userRateLimitTier`, and `cachedUsageUtilization.fetchedAtMs` (ms epoch). Size cap 4 MB. Never read or store tokens. Note that `%USERPROFILE%\.claude\.credentials.json` and `%USERPROFILE%\.codex\auth.json` exist on this machine and hold CREDENTIALS - Trayloom must never open them. |
| Claude Code (live process registry) | `<claudeConfigDir>/sessions/<pid>.json` | `~/.claude/sessions/` | `%USERPROFILE%\.claude\sessions\<pid>.json` (also stray `<pid>.<hash>.key` files - ignore anything that is not `^\d+\.json$`) | Keys used: `pid` (int), `sessionId`, `pidDomain`. **Windows difference:** macOS skips records whose `pidDomain != "darwin"`; on Windows the value is `win32:<host>` -> accept any `pidDomain` that starts with `win32:` (skip other domains, e.g. a container). Process liveness = `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `GetExitCodeProcess == STILL_ACTIVE` (compare `procStart` if you want PID-reuse protection). The file also carries `status` (`busy`/`idle`) - optional bonus signal, the Mac code does not use it. |
| Claude plan limits (fallback 1) | Claude desktop app history `plan-usage-history.json` | `~/Library/Application Support/Claude/plan-usage-history.json` | `%LOCALAPPDATA%\Claude-Data\claude_<packageFamilySuffix>\plan-usage-history.json` (here `...\Claude-Data\claude_pzs8sxrjxfjjc\plan-usage-history.json`, 402 samples, last written today). Resolve with a wildcard `Claude-Data\claude_*\plan-usage-history.json`; also probe `%APPDATA%\Claude\plan-usage-history.json` (non-MSIX installs). | JSON `{ "version": 1|2, "samples": [ { "t": <ms epoch>, "org": "<org uuid>", "u": { "fh": %, "sd": %, "so": %, "sn": % } } ] }`. Version 1 keeps `fh`/`sd`/`so`/`sn` directly on the sample instead of under `u`. `fh`=5-hour session, `sd`=7-day, `so`=7-day Opus, `sn`=7-day Sonnet; values are percent used 0-100; only the windows present in the sample are reported (here the latest sample only has `sd`). Size cap 4 MB. Any other `version` -> ignore the file. |
| Claude plan limits (fallback 2 - NEW on Windows, optional) | `cachedUsageUtilization` inside `.claude.json` | (mac does not use it) | `%USERPROFILE%\.claude.json` -> `cachedUsageUtilization.utilization.{five_hour,seven_day,seven_day_opus,seven_day_sonnet}.{utilization,resets_at}` with `fetchedAtMs` | Cheap, no process launch; Claude Code refreshes it itself. Useful when `claude.exe` cannot be started. Same shape as the CLI answer in A2. `resets_at` is ISO-8601 (`2026-11-04T01:59:59.806645+00:00`) or null. |
| Claude plan limits (primary) | `claude` CLI, control protocol | see A2 | `%USERPROFILE%\.local\bin\claude.exe` (found here), `%APPDATA%\npm\claude.cmd`, `%USERPROFILE%\.claude\local\claude.exe`, then PATH | See A2. |
| Codex (usage) | every `*.jsonl` (recursive) under both roots | `~/.codex/sessions/**`, `~/.codex/archived_sessions/**` | `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-<ISO-ts>-<uuid>.jsonl` (2026 files here). `%USERPROFILE%\.codex\archived_sessions` holds `*.jsonl.zst` (zstd-compressed) files here - the Mac reader only accepts `.jsonl`, so these are NOT read (keep that behaviour; optionally add zstd support later). | Honour `CODEX_HOME` if set (the root becomes `$CODEX_HOME\sessions`). Any line longer than 32 MB is skipped. |
| Codex (plan/limits in log) | `event_msg` / `token_count` lines | same files | same files | `payload.rate_limits = { limit_id, primary{used_percent,window_minutes,resets_at}, secondary, plan_type, credits, individual_limit... }`; verified here: `limit_id:"codex"`, `primary.window_minutes:43200`, `plan_type:"go"`. |
| Codex (account limits, resets) | `codex app-server` JSON-RPC | see A3 | `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe` (newest first; found here, along with codex-command-runner etc.), `%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe` (found here), `%USERPROFILE%\.local\bin\codex.exe`, PATH (`%APPDATA%\npm\codex.cmd` shim - must be launched as `cmd.exe /c codex.cmd ...`) | MSIX copy `%ProgramFiles%\WindowsApps\OpenAI.Codex_*` is not readable by normal users; do not rely on it. |
| OpenCode | SQLite database | `~/.local/share/opencode/opencode.db` (+ `-wal`) | `%USERPROFILE%\.local\share\opencode\opencode.db` (+ `.db-wal`, `.db-shm`) - exists here (tables `message`, `session`, `part`, ..., 4 messages, 2 sessions); also probe `%LOCALAPPDATA%\opencode\opencode.db` and `$XDG_DATA_HOME\opencode\opencode.db` | Opened strictly read-only. Only the OpenCode root FOLDER existing counts as "found". OpenCode 2 schema (`session_v2`, `session_message`) is also supported (see A4.5). |
| GitHub Copilot | per-session event log | `~/.copilot/session-state/<sessionId>/events.jsonl` | `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl` (exists here; siblings `checkpoints\`, `files\`, `workspace.yaml`, `inuse.<pid>.lock`) | Exactly ONE level: `<root>\<non-hidden dir>\events.jsonl`. Never walk the session folder (can contain a whole workspace checkout). Copilot has no plan limits (`reportsLimits=false`). |

"Found / Not found" in Settings = the root FOLDER exists (Claude: any of its `projects` folders; Codex: `sessions` or `archived_sessions`;
OpenCode: the data folder; Copilot: `session-state`). Re-checked when the page opens and every 5 minutes while running.

Archive of progress (macOS: `~/Library/Caches/<bundle id>/AgentUsage/agent-usage.bin`, a private binary format with checksum, versioned by app build): Windows -> `%LOCALAPPDATA%\Trayloom\AgentUsage\agent-usage.bin`. Format is private to the app; any
unreadable/foreign/other-build file reads as "nothing" and logs are read from the start. It holds: per-response records (key, provider, date, model, project,
session, requests, tokens, cost, savings, billable flags, source logs), the last limits per provider, the Codex plan, open turns, and per-log
cursors (path, offset, file identity, pending partial line, discarding flag, mtime, parser state, 4 KB-edge fingerprint). OpenCode data is
NEVER archived (re-read from the database at every launch). It is deleted when the AI Agents section is turned off. File identity on Windows
= (VolumeSerialNumber, FileIndex) from `GetFileInformationByHandle` (replaces inode).

Privacy statements to keep: nothing is sent anywhere except (a) the optional daily price-list download (carries nothing from the PC) and (b) the
agent CLIs themselves talking to their own account servers.

---------------------------------------------------------------------------------------------------------------------------------

## A2. Claude plan limits ("Claude bar") - how they are obtained

Priority (`AgentUsageService.readClaudeApp`):
1. **Account reading** via `claude` CLI (`AgentClaudeServer`) - exact, account-wide (includes use on other machines).
2. **Desktop app history** `plan-usage-history.json` (`AgentClaudeAppUsage`) - percentages only; renewal times are inferred.
3. (Windows addition) `cachedUsageUtilization` in `.claude.json`.
4. Nothing: the card shows an estimate from local activity (5-hour block, A6.5) or a hint.

Rule: "one source while the account answers": if the account reading is younger than `freshness = 30 min` it wins outright; otherwise the
newest `observedAt` of {desktop, account} wins. (Prevents flip-flop that would re-fire alerts.) For a non-default Claude profile (A8) the
desktop history is ignored entirely - only that profile's own CLI reading counts.

### A2.1 CLI conversation (`AgentClaudeServer.Request.run`)
- Executable search (first existing, non-directory, executable file): `~/.local/bin/claude`, `~/.claude/local/claude`, `/opt/homebrew/bin`, `/usr/local/bin`,
  each folder on the app PATH, then Node-manager folders (`.volta/bin`, fnm aliases/default/bin, newest `.nvm/versions/node/*/bin`, `.asdf/installs/nodejs/*/bin`,
  `.local/share/mise/installs/node/*/bin`, `.bun/bin`, `Library/pnpm`, `.local/share/pnpm`, `.npm-global/bin`). The Claude desktop app's own copy is deliberately excluded.
  **Windows list:** `%USERPROFILE%\.local\bin\claude.exe`, `%USERPROFILE%\.claude\local\claude.exe`, `%APPDATA%\npm\claude.cmd`, `%LOCALAPPDATA%\Volta\bin`, `%LOCALAPPDATA%\pnpm`, `%USERPROFILE%\.bun\bin`,
  `%USERPROFILE%\.npm-global`, then every PATH entry (`claude.exe`, `claude.cmd`). A `.cmd` shim is started as `cmd.exe /d /c "<path>" <args>` with proper quoting.
- Arguments (verbatim): `--print --input-format stream-json --output-format stream-json --verbose --no-session-persistence --tools "" --strict-mcp-config --mcp-config {"mcpServers":{}} --setting-sources "" --settings {"disableAllHooks":true}`.
  Working directory = filesystem root (Windows: `%SystemRoot%\System32` or `C:\`); stdin/stdout are pipes, stderr discarded.
- Environment: copy of the app's environment but DROP every variable starting with `CLAUDE_` or `ANTHROPIC_` (except `DISABLE_TELEMETRY`, `DISABLE_ERROR_REPORTING`,
  `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`) and drop `CLAUDECODE`; set `DISABLE_AUTOUPDATER=1`; for a non-default profile set `CLAUDE_CONFIG_DIR=<profile dir>`.
  Re-apply from `<configDir>\settings.json` -> `env` the keys `DISABLE_TELEMETRY`, `DISABLE_ERROR_REPORTING`, `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`,
  `HTTPS_PROXY`, `HTTP_PROXY`, `NO_PROXY`, `NODE_EXTRA_CA_CERTS` (strings as-is, booleans -> "true"/"false", numbers -> text). PATH given to the child = inherited PATH + the manager folders above (`;`-separated on Windows).
- Protocol (one JSON object per line on stdin, replies one per line on stdout; overall deadline 25 s; stop reading if the buffer exceeds 4 MB):
  1. send `{"type":"control_request","request_id":"initialize","request":{"subtype":"initialize"}}` -> wait for a line with `type=="control_response"` and `response.request_id=="initialize"`.
  2. send `{"type":"control_request","request_id":"usage","request":{"subtype":"get_usage","skip_behaviors":true}}` -> wait for `response.request_id=="usage"`.
  - `response.subtype != "success"`: error text (lowercased) containing "supported" or "unknown" -> `outdated`; containing "auth", "login" or "sign in" -> `needsSignIn`; anything else -> `unreachable`.
  - `response.response.rate_limits_available != true` -> `needsSignIn`.
  - Success: close stdin, wait up to 3 s for exit, then re-read the profile (`.claude.json`): the account identity must be unchanged, and `cachedUsageUtilization.fetchedAtMs` must not be older than 60 s before the request started (otherwise Claude answered from its cache and the account was not reached -> `unreachable`). `observedAt = min(now, fetchedAtMs)`.
- Mapping of `response.response.rate_limits`:
  | CLI key | window id | kind | minutes | scope label |
  |---|---|---|---|---|
  | `five_hour` | `claude.fh` | session | 300 | - |
  | `seven_day` | `claude.sd` | weekly | 10080 | - |
  | `seven_day_opus` | `claude.so` | weekly | 10080 | "Opus" |
  | `seven_day_sonnet` | `claude.sn` | weekly | 10080 | "Sonnet" |
  Each: `utilization` (number, NOT bool, finite) = percent used clamped 0-100; `resets_at` = ISO-8601 string or null. A window whose `resets_at` is present but `<= observedAt` is dropped; null `resets_at` (idle window) keeps its percentage. If no window survives -> unreachable. Other keys seen on this machine (`seven_day_oauth_apps`, `seven_day_cowork`, `iguana_necktie`, ...) are ignored.
- `current(reading, now)`: windows are removed when `resetsAt <= now`; a window without a renewal expires after `minutes*60` (session; default 300) or 24 h (others) from `observedAt`. Never invent "0% used" after a reset.
- Schedule (`AgentClaudeServer.interval`): no failure -> 60 s while the AI page is visible, 300 s in the background; `unreachable` -> `min(1800, 300 * 2^(min(3, repeated-1)))`; `missing`, `needsSignIn`, `outdated` -> 1800 s. Runs only when: service running, not paused, online, Claude enabled, no request in flight, and `limitsInUse` (A5.2). Cancelled on pause/stop. Failure strings are in A10 (`claudeCode*`). A "Check" retry (`checkClaudeAccount`) resets the wait.
- Offline detection: `NWPathMonitor` -> Windows `NetworkChange.NetworkAvailabilityChanged` / `INetworkListManager`. After the machine has been offline >= 20 s awake, Claude turns end without a notice (A5).

### A2.2 Desktop-app history -> windows (`AgentClaudeAppUsage.limits`)
The file keeps only percentages, so renewal times are reconstructed:
- `freshness = 30 min` (the app writes every 5-15 min). Use the LAST sample (after filtering to the signed-in organization: samples with `org == nil` or `org == claudeOrganization`); ignore it if it is `> now + 300 s` in the future or `>= 7 days` old.
- For each window in order fh(session,300), sd(weekly,10080), so(weekly,"Opus"), sn(weekly,"Sonnet") that the sample has:
  - session: dropped if the reading is `>= 5 h` old. Renewal: from the unbroken run of readings above zero that ends with the latest one (a drop is a renewal too); `resetsAt` = `latest` possible end = first reading of the run + 5 h; if Claude Code's own first request (A6.5 block start) lies inside the bracket it fixes `start + 5h` exactly; else `estimatedResetsAt` = midpoint estimate (shown with "≈ " and the "Estimated from this PC" tooltip; only the card uses the estimate, alerts use the latest end).
  - weekly: renewal comes from usage DROPS (`after + 1 < before`) in the sample history: a drop's moment is the next whole UTC hour inside its span (renewals are on the UTC hour, `hour(of:)`); a drop confirmed by another drop an integral number of weeks away (both spans < 24 h) fixes the schedule; otherwise the last drop stands; advance by 7 days until after the reading. A schedule point that went by in plain sight (readings within the hour on both sides, `before >= 10`, `after + 1 >= before`, no drop within the hour) means the schedule moved -> last drop stands.
  - If the computed renewal `<= now` drop the window; with no renewal date and reading `>= 24 h` old drop it.
- Result `AgentLimits(provider: claude, windows, observedAt: latest.date, source: claudeApp)`.
- Re-read when the file's mtime changes (checked every 30 s tick and on page open).

---------------------------------------------------------------------------------------------------------------------------------

## A3. Codex plan limits ("Codex bar")

Two layers:
1. **Session-log readings** (always, no process): every `token_count` event with `payload.rate_limits` whose `limit_id` is empty or `"codex"` (main bucket; model-specific buckets are ignored) -> windows via `codexWindows`; `payload.rate_limits.plan_type` -> Codex plan (newest reading wins, guarded by `codexPlanObserved`). Source `.sessionLog`.
   - Windows are classified by LENGTH not slot: `primary`, `secondary`; each needs finite `used_percent`; `minutes = window_minutes`; `resets_at` = unix seconds (or ms if > 1e11); fallback `resets_in_seconds` -> `observed + delay`. id = `codex.<minutes>` (or `codex.primary|secondary`). Kind: `minutes<=720` session, `6*1440...8*1440` weekly, else other (this machine's `go` plan reports a single `43200` min window -> `other`, labelled "30d" by `AgentFormat.duration(units:1)`). Sorted ascending by minutes. If neither slot gives a window and `individual_limit.remaining_percent` exists (Business accounts) -> one window `codex.individual`, kind other, used = 100 - remaining.
2. **Account reading** via `codex app-server` (`AgentCodexServer`, `AgentCodexResetService`, `CodexProfileLimitsService`) - fresher than any log; used by the Resets card and the per-account cards.
   - Launch: `<codex> -c features.plugins=false app-server`; stdio JSON, one object per line; cwd = filesystem root; stderr discarded; deadlines: check 20 s, redeem 30 s; buffer cap 4 MB. Env: inherited, PATH = [executable folder] + [login-shell PATH or defaults] + inherited PATH. (Windows: executable folder + inherited PATH; `.cmd` shim via `cmd.exe /d /c`.) For a profile add `CODEX_HOME=<profile dir>` and restrict the inherited environment to an allow-list: `HOME, USER, LOGNAME, TMPDIR, HTTP(S)_PROXY, ALL_PROXY, NO_PROXY` (and lowercase variants), `SSL_CERT_FILE, SSL_CERT_DIR, NODE_EXTRA_CA_CERTS, DO_NOT_TRACK, DISABLE_TELEMETRY` (+ on Windows `USERPROFILE, APPDATA, LOCALAPPDATA, SystemRoot, TEMP, TMP, PATH, PATHEXT, ComSpec`), and set `LANG=en_US.UTF-8`.
   - Handshake: request `{"id":1,"method":"initialize","params":{"clientInfo":{"name":"trayloom","title":"Trayloom","version":"<app version>"},"capabilities":null}}`, then notification `{"method":"initialized"}`. Then requests with incrementing ids; skip messages that have a `method` (notifications/server requests) or a different `id`; `result` object -> success; `error` -> map: code -32601 or message contains "unknown variant"/"method not found" -> outdated; contains "authentication"/"not logged in" -> needsSignIn; else refused.
   - `account/read` (params `{}`; for per-profile checks `{"refreshToken":false}` so Trayloom never triggers a sign-in refresh): `result.account.type == "chatgpt"` means a plan account, otherwise `needsSignIn` (signed out, API key, cloud account). `result.account.planType` in {free, go, plus, pro, team, business, enterprise, edu} gives the plan name.
   - `account/rateLimits/read`: `result.rateLimitsByLimitId.codex` (preferred) or `result.rateLimits` when its `limitId` is empty/"codex" -> windows parsed with camel-case keys `usedPercent, windowDurationMins, resetsAt, individualLimit, remainingPercent` (same classification as above). `result.rateLimitResetCredits` {`availableCount`, `credits:[{id,status,expiresAt}]`}: key must exist (else "outdated" - Codex too old); credits with `status=="available"` (default) and not expired; sorted soonest-expiry first; `available = max(listed, availableCount)`.
   - `account/rateLimitResetCredit/consume` with `{"idempotencyKey":"<uuid>","creditId":"<id or omitted>"}` -> `result.outcome` in `reset | nothingToReset | noCredit | alreadyRedeemed`. Afterwards re-read the account (a lost reply may still have spent one).
   - Fresh limits found this way are injected into the store (`noteLimits`, `store.updateLimits` keeps the NEWER reading) so alerts and the closed island update immediately.
- **Reset service** (card "Resets", default account only): `refreshIfStale` when the card appears: re-check if older than 300 s (settled failures = nil/needsSignIn/outdated) or 60 s (unreachable/refused/missing). Redeem: serialised; keeps `pending {key, creditId, date}` for 10 minutes so a retry after a lost reply reuses the SAME idempotency key and never spends two. Result chip shown for 60 s.
- **Profile service** (`CodexProfileLimitsService`): up to 8 saved homes (A8). One check at a time (FIFO queue). A home is "due" when never attempted, or `now - attempted >= interval`; `interval`: no failure -> 300 s (60 s when a window renewed since the reading), `unavailable/cancelled` -> 300 s, `invalidProfile/missingCLI/needsSignIn/outdated` -> 1800 s. A 30 s timer (tolerance 5 s) checks due homes ONLY while the AI page is visible; Settings "Check limits" forces all. A card that was queued but not user-forced is dropped when the page closes. In-memory only. Expired windows are filtered at display time (`currentLimits(at:)`): unknown, never "fresh 0%".
- Staleness styling: an account card reading older than 600 s (two missed checks) is shown dimmed (0.6 opacity) with an orange caption.

Missing CLI behaviour: card says "Needs the Codex app or CLI" (`resetsNeedCodex`); the "Retry" pill asks the login shell for PATH once (macOS runs the user's shell startup files). Windows: retry re-enumerates the candidate list plus `where.exe codex`; do NOT start `powershell -Login` profiles.

---------------------------------------------------------------------------------------------------------------------------------

## A4. Log parsing (`AgentLogParser`, `AgentLogObject`, harness readers)

### A4.1 Reading discipline
- Incremental per file with a cursor (`offset`, `identity`, `pending`, `discarding`, `modified`, parser `state`). Read chunks of 4 MiB (`chunkSize`), split on `\n`; the trailing partial line is kept in `pending` and completed next time. A line > 32 MiB (`maximumLine`) is dropped (and the rest of it skipped: `discarding`).
- File replaced/truncated/rewritten (`identity` changed, `size < offset`, or `size > offset` and the 4 KB head+tail fingerprint of the first `offset` bytes no longer matches) -> start over from byte 0 and mark `restarted` (not archived).
- Cheap filters first: the parsers match structural byte strings (`"type":"assistant"`, `"type":"user"`, `"token_count"`, ...) before JSON-decoding, because lines can be tens of MB (tool output). `AgentLogObject` is a tiny scanner that indexes ONE object's top-level fields (skipping nested values/strings in place, max nesting 128, keys <= 256 bytes, duplicate keys reject the object) so a nested tool result can never supply an envelope's `type`/`agentId`. Windows: implement with `Utf8JsonReader` over `ReadOnlySpan<byte>` (single pass, `Skip()` nested values) - same semantics.
- First read covers files modified in the last 91 days (`horizon = AgentUsageSnapshot.dayCount * 86400`); discovery order = oldest-modified first, Claude subagent logs after all sessions.
- Timestamps: ISO-8601 (`2026-09-21T23:42:45.078Z`, also `+hh:mm` offsets) parsed by a fast path; unix numbers accepted as seconds or ms (> 1e11 -> ms). Numbers: negative/NaN -> 0, capped at 1e12.
- Project name = last path component of the recorded `cwd`; a worktree under `<repo>/.claude/worktrees/<x>` belongs to `<repo>` (cut at `/.claude/worktrees/`). **Windows:** split on both `\` and `/`, and match `\.claude\worktrees\` too. Strip trailing separators first.

### A4.2 Token model (`AgentTokens`)
`input` (uncached), `cacheWrite`, `cacheRead`, `output` (already includes reasoning), `reasoning` (informational). `total = input+cacheWrite+cacheRead+output`; `prompt = input+cacheWrite+cacheRead`; `cacheHitRate = cacheRead/prompt`. Streamed replies are logged once per content block with growing output: duplicates merge field-wise by MAX (`merged(with:)`).

### A4.3 Claude Code (`ClaudeReader`)
- `type=="assistant"` line with `message.usage` and non-empty `message.model` not starting with `<` (`<synthetic>`): record with
  `input_tokens`, `cache_creation_input_tokens` -> cacheWrite, `cache_read_input_tokens`, `output_tokens`, `output_tokens_details.thinking_tokens` -> reasoning,
  `cache_creation.ephemeral_1h_input_tokens` -> long cache write (priced at the 1-hour rate), `speed=="fast"` -> fast tier, `inference_geo=="us"` -> domestic multiplier,
  `server_tool_use.web_search_requests` -> web searches. Dedupe key `claude:<message.id>:<requestId>` (fallback `claude:<session>:<time>`). Session = `sessionId`, project from `cwd`.
- Turn tracking: a `type=="user"` line (not `isMeta`, not `isSidechain`) opens a turn (`turnBegan(timestamp)`); an assistant line with a non-terminal `stop_reason` keeps it open (`turnActive`); `stop_reason` in {end_turn, stop_sequence, max_tokens, refusal} ends it (`completed = !(isApiErrorMessage || model starts with "<")`); text starting `[Request interrupted by user` or `<local-command-std` in the PERSON's message ends it without completion; `toolEndsTurn:true` ends it completed. Subagent (`isSidechain:true`) lines count usage but never open/close turns. While a turn is open a user line is only "still active" (tool results can be huge). Bash tool_use ids are remembered in `runningCommands` until their `tool_result.tool_use_id` arrives (commands run locally, so the turn survives going offline).

### A4.4 Codex (`CodexReader`) - line `type` / payload
- `session_meta` (first one per file only describes THIS thread; later ones are copied history): `payload.id` = session, `cwd` -> project, `source.subagent`/`source.internal` present -> this log is a subagent/review/memory task (owns no turn; its parent = `payload.session_id` or `parent_thread_id`).
- `turn_context`: `model`, `cwd`, `service_tier` (`fast`/`priority` -> fast tier premium).
- `token_usage_record` (newer logs): `payload.usage {input_tokens, cached_input_tokens, cache_write_input_tokens, output_tokens, reasoning_output_tokens}` where input INCLUDES cached: `cacheRead = min(input, cached)`, `cacheWrite = min(input-cached, cache_write)`, `input = input - cached - written`. Key `codex:<response_id>` (fallback `codex:<session>:<time>`). Once a file has shown one such record, running totals are ignored.
- `event_msg` types (only these are decoded: `token_count`, `task_started`, `task_complete`, `turn_aborted`, `thread_settings_applied`): `token_count` -> limits + plan (see A3) and, for logs without usage records, a delta from `info.total_token_usage` vs the previous total (use `info.last_token_usage` when present) keyed `codex:<totalsSession>:total:<total>`; `task_started` opens a turn (`started_at`); `task_complete`/`turn_aborted` close it (`completed` only for task_complete; `duration_ms`, `completed_at` honoured); `thread_settings_applied` updates `service_tier`.

### A4.5 OpenCode (`AgentOpenCodeReader` + `OpenCodeReader`)
Read-only SQLite, polled when `opencode.db` or `opencode.db-wal` mtime changes. Details:
- Tables: v1 = `message` (+ `session`, optional `part`); v2 = `session_message` + `session_v2` (detected by `sqlite_master` having `session_v2`). `session.parent_id` (if column exists) marks subagent sessions.
- New rows = `rowid > cursor.offset` ordered by rowid; replies still being written (assistant with no `time.completed`, or compaction `status=='running'`) are re-queried by id until their `time_updated`/data length stop changing (abandoned after 24 h). The last 64 `(rowid,id)` pairs are kept to detect deleted tail / replaced DB (inode change) -> emit `{"type":"reset"}` and start over from the horizon (binary search on `time_created`; v2 uses index `session_message_time_created_idx`).
- ONLY these JSON values are extracted (so prompts/replies/summaries/error text never leave the DB): `role` (v2: `m.type`), `parentID`, `modelID`, `model_id`, `model`, `tokens`, `cost`, `finish`, `time`, `path.cwd`, `status`, `outcome`, plus `error` presence (json_type only), computed flags `tool_calls` (finish not tool-calls/unknown but has unexecuted tool parts) and `auto_compaction`.
- Record: `tokens.input`, `tokens.cache.write`, `tokens.cache.read`, `tokens.output + tokens.reasoning` (reasoning also kept separately); key `opencode:<session>:<message id>`; cost = list-price cost when the model is known, else the recorded `cost` (`reportedCost=true` when > 0); a later reported cost replaces a derived one. Messages with 0 tokens and 0 cost are skipped.
- Turn logic: user message opens a turn (a new user message after `idleTurn` (10 min) of silence, or `openCodeSettle` (30 s) after a "settled" step, starts a fresh turn instead of continuing); assistant finish other than `tool-calls`/`unknown` (or error, or `time.completed` for v2) ends it; `finish=="tool-calls"` marks the turn "settled" (`turnSettled`) and, if nothing follows within 30 s, it is closed without a notice; `idle` rows (`outcome=="succeeded"`) close it. Per-session state is kept inside the shared state (`openCodeSessions`, bounded id sets of 100).
- Not archived: re-read at every launch; progress key is `<dbpath>#<sessionId>` for turns.

### A4.6 GitHub Copilot (`CopilotReader`) - `events.jsonl`
- Only top-level event types in {`user.message`, `assistant.turn_start`, `assistant.message`, `assistant.turn_end`, `session.start`, `session.context_changed`, `session.model_change`, `session.shutdown`, `abort`} are decoded (the first read goes through a filter that copies only those lines - `readsFullHistoryFirst`). Others (e.g. `hook.*`, `tool.*`, `permission.*`, `session.usage_record` seen here) are ignored.
- `session.start.data`: `sessionId`, `selectedModel`, `context.gitRoot|cwd|repository` -> project (first non-empty). `session.context_changed`, `session.model_change.newModel` update context.
- Turns: `user.message`/`assistant.turn_start` open a turn; `assistant.message` per API call yields an ACTIVITY record (zero tokens, zero cost, keyed `copilot:<session>:<root|agent:id>:<api:apiCallId|message:id>:activity`; model attribution can arrive later via `usageModel`); a turn ends at `assistant.turn_end` only when the last message was a final response (no `toolRequests` items, `phase` not thinking/commentary); `abort` ends non-completed; `session.shutdown` ends open turn non-completed. Subagent (`agentId` present) events only count `assistant.message` activity.
- Tokens: only from `session.shutdown.data.modelMetrics.<model> {tokenDetails{input,cache_write,cache_read,output}.tokenCount | usage{inputTokens,cacheWriteTokens,cacheReadTokens,outputTokens,reasoningTokens}, requests.count}` which are CUMULATIVE per session: growth = difference to the previous total (full total when it decreased). Records keyed `copilot:<session>:<shutdown event id>:<model>` marked aggregate (priced at base rates, no long-context inference). Request counts reconcile reported `requests.count` against counted activity records per model (unknown model bucket "" for ambiguity).
- Copilot never reports plan limits; Limits card shows "Limits appear after the next reply" and last-used time.

### A4.7 What is NOT used on Windows
Nothing from the Mac readers is macOS-API specific except: FSEvents (use `FileSystemWatcher`/`ReadDirectoryChangesW` + poll), inode (use file index), `memmem/memchr` (use spans), `NWPathMonitor`, `SQLite3` C API (use `Microsoft.Data.Sqlite` or `System.Data.SQLite`, read-only, `json_extract`/`json_valid`/`json_type` are built into modern SQLite).

---------------------------------------------------------------------------------------------------------------------------------

## A5. Service loop and timing (`AgentUsageService`)

- Runs only while `NotchAgentSupport.isEnabled()` (island enabled AND `notchAgentsEnabled` AND section in modules). `syncWithPreferences()` is called whenever any agent setting changes: a change of the enabled provider set or Claude profile stops and restarts WITHOUT reusing progress; Codex account checks survive.
- Threads: one private background queue for reading/parsing; UI thread publishes `snapshot`. Never block the UI thread; never wait on the UI from the queue.
- `tick` = 30 s timer: price refresh check, Claude limits check, close idle turns, drop records older than 91 days, poll open logs (modified within 24 h), re-scan roots every 300 s (a first Codex session created later is picked up), re-read Claude plan + app history, check limits/renewals, save progress every 300 s, publish if anything changed or a sliding window moved (burn rate, Claude block, day rollover).
- File watching on roots: debounce 1 s; on "lost events"/root change do a full rescan. PLUS a 2 s fast poll for any log modified within the last 30 min or holding a turn (open files may not emit events until closed); the poller is armed only while such logs/turns exist.
- Publish coalescing: 1 s after the last change.
- `pause()` when the island is away (display off / session locked) and `resume()` after: reading stops and Claude/Codex account checks are cancelled, state kept; on resume logs are read from their cursors.
- Page visibility: `pageDidAppear(preview:)` (the AI page is on screen; Settings previews pass `preview:true` and do not count) triggers immediate Claude plan/app reads + a CLI check (60 s cadence while visible); `pageDidDisappear`.
- Network: while offline Claude checks are skipped; when back online the unreachable counter resets.
- Turn bookkeeping constants: `idleTurn` 10 min (turn silent this long is moved to "waiting", not shown as working); a waiting turn is dropped after `resumeWindow` = 1 h (Claude) / 6 h (others); a Claude session whose registry process died ends at once without a notice; a turn whose last step "settled" (OpenCode) with 30 s of silence ends silently; offline >= 20 s awake ends all Claude turns that have no running Bash command and no reply since going offline. `finished` events fire only for completed turns that ended <= 5 min ago (`lateEnd`) and only after the initial history read (`reportsTransitions`).
- Subagent responses count toward the parent's turn (tokens, cost) but do not change its model/project.

### A5.2 `limitsInUse` (does anything need plan limits?)
Claude/Codex account checks run only when ANY of: the limit alert is on (`notchAgentsLimitAlert`); the live strip is on AND readout == `limit`; the resting content is AI limits; or the AI page is shown and the Limits card is not hidden. Otherwise nothing asks for limits.

### A5.1 Alerts (events -> island notices; see the Notice table in Part B 7.7, event `agents`, priority 1, 5 s)
- `finished(provider, duration, cost, tokens, project)`: emitted when a turn completes AND `finishAlert` on AND `duration >= finishMinimum`. Notice title `"<Provider> finished"` (`finishedFormat`), detail `"<duration> · <cost>"` (duration via `AgentFormat.duration` e.g. "4m 12s"; cost omitted when 0), icon = provider symbol, tinted with the provider tint; the companion mascot "celebrates".
- `limitWarning(provider, window)`: when a window's usedPercent crosses `threshold` (default 80) between two readings of the same account (also when a window renewed and is above again, or - for exact account readings - a window began after the previous reading). Notice title `"<Provider> · <Session|Week|Week · Opus|<duration>>"`, detail = share per the display mode ("12% left" / "88% used"), symbol `exclamationmark.triangle.fill` (orange). Remembered in `warned` until the window renews (usage drops below threshold and `renewed(...)`, or its `resetsAt` passes).
- `limitReset(provider, window)`: title as above, detail `"Limit renewed"`, symbol `arrow.clockwise`, mascot celebrates.
- `budgetReached(spent, budget)`: when today's API value >= `dailyBudget` (once per day; a budget already exceeded at launch is history): title `"Daily budget"`, detail = cost, symbol `dollarsign.circle.fill`.
- Events are suppressed (not forwarded) when the provider is off or its alert switch is off.
- Crossing/renewal logic (`AgentLimitSupport`): `current(window, now)` -> a window whose `resetsAt <= now` reads 0% used with no reset; `displayedReset` = `estimatedResetsAt ?? resetsAt` only while in the future; `pace` = elapsed fraction of the window (needs minutes and a reset time and elapsed > 0); `binding(limits)` = window with highest usedPercent (tie: later reset); `renewed(before, window, observedAt)` = usedPercent fell by more than 1, or the reset moved forward by > 60 s after the old one passed.

---------------------------------------------------------------------------------------------------------------------------------

## A6. Pricing, API value, summaries

### A6.1 `Resources/agent-prices.json` (ship it VERBATIM as an embedded/bundled resource; 25 KB, schema 1, `updated` "2026-10-30")
Top-level keys: `schema` (must equal 1), `updated` (day string `YYYY-MM-DD`, UTC, year 2024-2100), `sources` (URL list, ignored), `notes`, `claude`, `codex`.
- `claude` = `{ webSearch: 0.01 (USD per search, 0-1), usOnlyMultiplier: 1.1 (1-3), models: [...25], plans: [...] }`; `codex` = `{ models: [...75], plans: [...] }`.
- Model entry: `id` (lowercase letters/digits/`.`/`-`/`_`, <= 64; Claude ids must start `claude-`, Codex ids must NOT), `input`, `output`, `cacheRead` (USD per MILLION tokens, 0-1000, required), optional `cacheWrite` (default = input), `cacheWriteLong` (1-hour write; default = cacheWrite), `fastMultiplier` (1-10, default 1), `longContext {above (1000..1e7 tokens), inputMultiplier, outputMultiplier (1-10)}`. 1-500 entries, ids unique.
  Examples: `claude-opus-5-5` input 4 / output 20 / cacheRead 0.2 / cacheWrite 5 / cacheWriteLong 8 / fast x2; `gpt-5.6-sol` 4/20/0.4/5, fast x2, long context above 272000 tokens (x2 input, x1.5 output).
- Plan entries: `{match, name, monthly?}`: Claude `max_20x` "Max 20×" $200, `max_5x` "Max 5×" $100, `max` "Max", `enterprise` "Enterprise", `team` "Team", `pro` "Pro" $20 (first entry whose `match` is contained in `"<organizationRateLimitTier> <organizationType>"` lowercased wins; unknown plan -> organizationType minus `claude_`, underscores -> spaces, capitalised, no price); Codex `free` $0, `go` $8, `plus` $20, `pro`, `business`, `enterprise`, `edu` (exact match on the lowercased `plan_type`/`planType`; unknown -> capitalised raw). Monthly price is used only for the "N× the price of <plan>" chip.
- Validation: whole list refused if anything violates the rules (size <= 256 KiB, counts, ranges). Later fields are ignored. The NEWER `updated` of {bundled, downloaded cache} wins; same day -> downloaded wins.

### A6.2 Download (`AgentPriceSource`)
Optional ("Keep prices up to date", default ON): at most once per 24 h (`refreshInterval`), retry after 6 h on failure (`retryInterval`), only while the section runs. Single GET, no cookies, no cache, 15 s request / 30 s total timeout, <= 256 KiB, follows redirects only to the same HTTPS host, `User-Agent: Trayloom/<version>`; response is validated before use; cache saved to `%LOCALAPPDATA%\Trayloom\agent-prices.json` (mtime = download time). The URL is Trayloom's own published copy of the file (constant `AgentPriceSource.Remote`, to be set when hosted; the macOS app points at its own GitHub raw URL - never point Trayloom at another project's). When a newer list installs, every non-reported record is re-priced and a snapshot published. Settings caption: "Price list of <day>" appended to the hint.

### A6.3 Cost formula (`AgentPricing.cost`)
- Normalise the model id: lowercase/trim; cut everything before `claude-`; for `claude-*` replace `.` with `-`; keep text after the last `/`; cut at `@` and `[`.
- Table = `claude` if id starts with `claude-` else `codex`. Match = longest family `f` such that id starts with `f` and the rest is empty OR starts with `-`/`_`/`:` followed by NO digits or >= 4 digits (a snapshot date) - so `gpt-5` does not match `gpt-5.1` and `claude-opus-5` does not match `claude-opus-5-6`. If the remaining text contains a sibling word (`mini, nano, pro, lite, research, search, audio, realtime, transcribe, tts, cyber, image`) -> NO price ("better no figure than a wrong one").
- `cost = ( input*P.input*r + (cacheWrite - long)*P.cacheWrite*r + long*P.cacheWriteLong*r + cacheRead*P.cacheRead*r + output*P.output*r_out ) / 1e6 + webSearches * webSearch`, where `r = (fast ? fastMultiplier : 1) * (domestic ? usOnlyMultiplier : 1)`, and when NOT aggregate and `tokens.prompt > longContext.above`: `r_in *= longContext.input`, `r_out *= longContext.output`. `savings = cacheRead * max(0, P.input - P.cacheRead) * r / 1e6`. An aggregate record (Copilot totals) with 0 tokens -> cost 0. Unknown model -> `cost = nil` ("unpriced": excluded from sums, counted in `unpriced`, UI shows "≥" prefix and the tooltip "Some models have no known price, so this is a minimum.").
- "API value" = what the same traffic would cost at API list prices; plans charge a flat price instead (`valueNote`).

### A6.4 Display names (`AgentPricing.displayName`)
`claude-opus-5-5` -> "Opus 5.5"; `claude-sonnet-4-20250514` -> "Sonnet 4"; drop 6+ digit date parts, `v<n>` parts and `latest`; first non-numeric word capitalised, then dotted version (`5-5` -> `5.5`), then remaining words capitalised; text after `:` in the id is ignored. `gpt-6-astra` -> "GPT-6 Astra"; `gpt-5.6-sol` -> "GPT-5.6 Sol"; dated snapshots stripped (`-2026-10-01`, `-20261001`); non-claude non-gpt names pass through normalised (OpenCode/Copilot model ids).

### A6.5 Summary (`AgentUsageSummary`, `AgentUsageSnapshot`)
- Periods: `today` (1 day), `week` (7 days = "7 days"), `month` (30 days). `AgentUsageSnapshot.dayCount = 91`. Calendar = local calendar (week start from culture).
- Snapshot fields: `loaded`, `now`, `periods[period] {total, byProvider, models, projects}`, `days` (91 buckets oldest first ending today, each by provider), `hours` (today's 24 hourly buckets), `limits`, `plans`, `live` (open turns sorted by start), `claudeBlock`, `burnRate` (last 30 min scaled to an hour: tokens, cost, savings x2), `lastActivity[provider]`, `seen` (providers with anything on disk: usage, limits or a turn).
- `AgentTotals`: tokens, cost, savings, requests, unpriced. `weight(byCost)` = cost when every response in the period had a price, else tokens, so bars never mix dollars and tokens.
- Models breakdown key = `<provider>:<displayName>`; projects key = project name (records with empty project skipped); both sorted by weight desc then name; the UI shows the top 3.
- `claudeBlock` (local estimate of the 5-hour session): over Claude records of the last 24 h (`blockHistory`) sorted by date: a block starts at the first request after the previous block ended and lasts 5 h; current block = the one ending after `now`.
- `AgentFormat` (US dollars, user's number format): `cost`: `|v|>=10000` -> `$12.3K`-style (scaled), `>=100` -> 0 decimals, else 2 decimals ("$4.56"); `tokens`: `<1000` plain, `K`, `M`, `B` with 1 decimal below 10 and none above, truncated (rounded down); `percent` 0 decimals; `duration` abbreviated units max 2 ("4m 12s", "1h 05m", "2d 3h"; days+hours when >= 86400, hours+minutes when >= 3600, else minutes+seconds); `span(days)` spelled out ("13 weeks" when divisible by 7, else "N days"); `clock` "0:42", "12:05", "1:02:05"; `day` = UTC calendar day abbreviated ("Oct 30, 2026").

---------------------------------------------------------------------------------------------------------------------------------

## A7. What the island shows

### A7.1 Open island page "AI Agents" (`NotchAgentsView`)
Inside the expanded island content area (width `size.width`, height `size.height` = min(content height, budget); see Part B 4.5 and `agentsHeight`).

States:
1. Snapshot not loaded yet and no Codex accounts: centred spinner (small) + text "Reading usage…".
2. Loaded but no provider has anything (`providers` empty; providers = enabled agents that are in `snapshot.seen`, or Codex with saved accounts, or Claude with a non-default profile): empty page, icon `sparkles`, message "No usage from Claude Code, Codex, OpenCode or GitHub Copilot yet. It appears here as soon as any of them works on this Mac." (Windows wording: "...on this PC.").
3. Providers exist but every card is hidden: icon `square.grid.2x2`, message "Choose what this page shows in Dynamic Island settings."
4. Otherwise the card grid. The grid refreshes every 15 s (`TimelineView periodic 15`); when total content height exceeds the available height it scrolls vertically (indicators automatic), else it is static.

Top row (island "band", leading slot): a period menu button (rounded 9) with the current period name ("Today" / "7 days" / "30 days") and a checkmark on the selected item; shown only while at least one visible card follows the period (Spending, Trend, Models, Projects). Choice persisted in `notchAgentsPeriod`.

Grid rules (`NotchAgentSupport`): spacing 10; cards pair two per row in reading order when width >= 390 pt, a full-width card (Trend, Activity) or an unpaired card takes the whole row; below 390 every card takes its own row. Heights: normal 96, chart rows (Trend/Activity) 118, rows containing an account card or the Resets card that names the default account 116. Content height = sum(row heights) + 10 * (rows-1).

Card list and order (default order = `limits, spend, live, trend, models, projects, activity, resets`; user-reorderable; hidden set = `notchAgentsHiddenCards`; new cards appended at the end). Symbols: limits `gauge.with.dots.needle.33percent`, spend `dollarsign.circle`, live `waveform.path.ecg`, trend `chart.bar.xaxis`, models `cpu`, projects `folder`, activity `square.grid.3x3.fill`, resets `arrow.counterclockwise.circle`.
Tile expansion: `limits` -> one tile per provider that reports limits (Claude, Codex, OpenCode; Copilot excluded) among enabled+seen providers; for Claude with a non-default profile the tile carries the account; for Codex with saved accounts there is one tile per saved account INSTEAD of the single Codex tile; `resets` -> one tile only when Codex is among the providers (names "Codex · Default account" caption when other Codex accounts exist); other cards one tile each.

Common card chrome (`NotchAgentCardChrome`): padding 12 horizontal / 10 vertical, content top-leading, fills its cell, rounded rectangle radius 18 (continuous), background white 6.5% (non-glass), no border. Card header (height 17): [icon 13 wide or 17x17 provider mark] [title 10.5 semibold white 90%] [spacer] [accessory]. Chip: font 9 semibold, text = tint 90%, padding 5 x 1.5, background tint 14% capsule.

Provider tints: Claude `#D97857` (0.85,0.47,0.34), Codex `#7D99FF` (0.49,0.60,1.0), OpenCode `#0FBA82` (0.06,0.73,0.51), GitHub Copilot `#4DC7AD` (0.30,0.78,0.68). `agentLimitTint(used)`: `>= 0.95` red, `>= 0.80` orange, else provider tint (the percentage text is white when it is the provider tint).
Provider marks: macOS borrows the template mark / icon from the installed app (`TrayIconTemplate` of the Claude app, `chatgptTemplate` of Codex app; OpenCode/Copilot use the app icon), falling back to SF symbols `sparkle` (Claude), `chevron.left.forwardslash.chevron.right` (Codex), `terminal` (OpenCode), `infinity` (Copilot). Windows: ship four small vector marks in the tint (Claude starburst, Codex "</>"-style blossom, OpenCode terminal prompt, Copilot infinity-like mark); fallback Segoe Fluent glyphs: Claude E735/"sparkle" (use a custom path; no exact glyph), Codex E943 (Code), OpenCode E756 (CommandPrompt), Copilot custom path. Mark scale: Claude 1.45x, others 1.2x of the nominal size; app-icon marks 1.35x.

**Limits card** (`NotchAgentLimitsCard`)
- Header title: `<Provider>`; with a Claude profile or Codex account: `<Provider> · <account name>` (Claude: `<profile name> · <email>`; default Claude account shows just the email when known). Accessory: plan chip (provider-tinted; name from the plan detection) and, when an agent of that provider is working, a 5 pt pulsing dot. For account cards the accessory is the plan chip plus either a mini spinner (while checking; a queued check shows as checking) or a refresh button (`arrow.clockwise`, 10 pt, tooltip "Check limits"/`check`).
- Body windows (max two): the session window (kind session) and the longest/most-used non-session window (kind != session, highest usedPercent). Each row: reading line + meter.
  - Reading line: `[title 10 medium white 85%] [↻ countdown 9.5 secondary] ... [share 12 semibold rounded monospaced-digit, tint]`; when the card is too narrow the countdown is dropped (ViewThatFits) and the title may scale to 0.8.
  - Title: session -> "Session"; weekly -> "Week" or "Week · Opus"/"Week · Sonnet"; other -> duration of `minutes` (units 1, e.g. "30d") or the scope or "Limit".
  - Share: display mode `remaining` -> "`<n>%` left" (`leftFormat`), `used` -> "`<n>%` used" (`usedFormat`) - the text always names which share it is.
  - Countdown: `displayedReset` -> "resets in" shown as `AgentFormat.duration(max(60 s, remaining), units: remaining >= 1h ? 2 : 1)` with a prefix "≈ " when the time is an estimate, with the `arrow.clockwise` icon; hidden when no future reset is known.
  - Meter (`NotchAgentMeter`): capsule track white 13%, fill tint 92% (minimum width = height when value > 0), height 4 (control height + 5 for the pace tick), value = remaining or used fraction by display mode; a PACE tick (1.5 wide, height+5, white 85%) at the elapsed-time fraction of the window (for `remaining` mode it is drawn at `1 - elapsed`), shown only when 0.02 < pace < 0.98.
  - Tooltip: `"<title> · <n>% used · <n>% left · <reset date+time> · Estimated from this PC · Updated <relative>"` (updated only when the reading is older than 10 min).
- Stale reading (Claude reading >= 30 min old; account card reading >= 10 min old): meters at 60% opacity; a single-window card shows a caption "Updated <relative>" (9.5 tertiary) when older than 10 min.
- No windows:
  - Claude with a local 5-hour block: "Session" title + countdown to block end + API value of the block (12 semibold) and a dimmed meter (elapsed fraction of the block); tooltip "Estimated from this PC"; below it a link-style button "Set up plan limits…" (`claudeLimitsHint`, arrow `arrow.up.forward`, provider tint) that opens Settings > AI Agents.
  - Claude without a block: "No session running" + the same button.
  - OpenCode: if today has tokens/requests/cost -> "Today" + cost (`≥ $x` when some unpriced, "—" when none priced) + line "`<tokens>` · `<n>%` from cache" + last used; else "No session running" + last used.
  - Codex (no windows yet)/Copilot: "Limits appear after the next reply" (2 lines) + last used relative.
- Account cards (Codex account / Claude profile) when no windows: placeholder line = "Reading usage…" while checking, else the failure text, else the hint ("Refresh to check this account's limits."); under windows a caption (8.5) = the failure (orange) or "Updated <relative>" (secondary, orange when stale).

**Spending card**: header "Spending" (accessory for `30 days`: one chip per provider whose plan has a monthly price and spent > 0: `<x>×` with 1 decimal when < 10 else 0 (e.g. "3.2×"), tooltip "3.2× the price of Claude Max 20×"). Value: 22 medium rounded monospaced-digit "`≥ ` + cost" (the "≥ " only when not fully priced), lineLimit 1 scale 0.7, then "API value" (9.5 secondary); tooltip = valueNote or the unpriced warning. A 4 px stacked bar splits the period by provider (weight by cost or tokens; every provider with >0 keeps a 4 px sliver, the rest shares remaining width, gap 2; empty = white 13% capsule). Footer (10 secondary, scale 0.85): "`<tokens>` tokens · `<n>%` from cache" (cache part only if > 0); tooltip "`$x` saved by the cache".

**Now card** ("Now"): header accessory: "+N" chip when > 2 sessions, 5 pt pulse in the first session's tint; header tint = first working provider tint else secondary. Working: up to two rows, clock ticks every 1 s: `[glyph 9][project name or provider name 11 semibold, middle truncation] ... [elapsed clock 11 medium monospaced, provider tint]`, subline 9.5 secondary: `"<Model display name> · <n> written · $cost"` (`written` = output tokens only, text "%@ written"; cost only if > 0); tooltip "`<total tokens>` tokens · `<n>%` from cache". Idle: one row per provider: static glyph + provider name 10.5 medium + last activity relative ("5m ago" style, abbreviated named presentation) or "Idle".

**Trend card**: header accessory = caption 9.5 medium monospaced secondary: `"<period> · <value>"` where value = cost when fully priced else "`<n>` tokens"; when a bar is hovered: `"<bucket label long> · <value>"` + (when by cost) `" · <tokens>"`. Bars: `today` = 24 hourly buckets, `week` = last 7 days, `month` = last 30 days; stacked per provider (bottom = first provider; segment colour = tint at 95%, 50% for non-hovered bars while another is hovered); bar corner radius min(3, width/2); empty bucket = 2 px white 10% capsule; gap 4 (<= 24 bars) or 2; heights relative to the peak bucket; hovered bar sets the caption. Axis (8.5 medium tertiary, height 10): week = 7 narrow weekday letters evenly spread; today = first/middle/last hour labels; month = first/middle/last "d MMM".

**Models / Projects cards**: top 3 rows of height 14: `[name 10.5 medium, middle truncation, fills] [bar 44 wide x 4, tint 85%, width max(3, 44*weight/peak)] [value 10 medium monospaced secondary, min width 40, right aligned]`; value = cost or tokens per `byCost`; project bars are white; empty -> "Nothing in this period". Tooltip: "`<name>` · `<n>` tokens · `$cost`".

**Activity card** ("Activity", full width): the 13-week heatmap. Layout: heatmap on the left (width = columns*cell + gaps) + figures column (gap 14). Heatmap: `columns` = weeks (Monday-start or culture first day), 7 rows, cell = min((height - 2.5*6)/7, width-based), spacing 2.5, corner radius min(2.5, cell/3); 91 days = up to 14 columns (first/last column partial). Colour = WHITE with opacity: 0.07 when no use, else by quartile of non-zero days (thresholds at 25/50/75% of the sorted non-zero values): 0.28 / 0.45 / 0.65 / 0.90; today's cell outlined white 45% 1 px, hovered cell white 90%. Weight = cost when every day is fully priced else tokens. Figures: big total (17 medium rounded) of all 91 days (cost or tokens), under it "13 weeks" (`span`), then `Active days <n>` (days with requests > 0) and `Busiest day <d MMM · value>` (hovered day replaces it with its weekday name + date/value). Header accessory: streak "🔥 N" (`flame.fill`, orange, 9.5 semibold) only when the streak >= 2; tooltip "Days in a row". Streak = consecutive days with requests ending today (a zero today is skipped, a zero earlier ends it).

**Resets card** ("Resets", Codex only; header uses the Codex mark; tint Codex): accessory = count chip (when available > 0) or mini spinner while checking; tooltip `resetsHelp`. Content states: redeeming -> spinner + "Resetting…"; confirming -> "Reset the session and weekly limits now?" + pills [Cancel] [Reset]; just finished (< 60 s) -> outcome line (reset -> green check "Limits reset"; nothingToReset -> info "Your usage doesn't need a reset yet"; alreadyRedeemed -> info "That reset was already used"; noCredit -> "No resets available"; no answer -> orange "Couldn't use the reset" plus the "Use a reset" button to retry the same use); failure with no summary -> failure text + [Retry] pill (messages: missing "Needs the Codex app or CLI", needsSignIn "Sign in to Codex with a plan to see resets", outdated "Update Codex to use resets here", unreachable/refused "Couldn't check resets"); summary with available > 0 -> "Next expires <relative>" (orange when < 24 h) + [Use a reset] pill (icon `arrow.counterclockwise`); available == 0 -> "No resets available". Pill = capsule height 20, padding 9, font 10.5 semibold; prominent = Codex tint on tint 20% bg; secondary = white 85% on white 10%; label scales to 0.8.

### A7.2 Closed island - live activity strip (`NotchAgentStrip`, `NotchCapsuleAgentStrip`)
Shown while at least one agent turn is open AND `notchAgentsLiveActivity` is on. The strip is a "compact activity" in the island priority list (position and combination rules in Part B 3.2; agents priority/duration in the activity table). It stays until the list empties (`NotchStripHold` keeps the last frame while the collapse animation runs).
- Notch-attached strip (`NotchAgentStrip`): two buttons around the camera gap. Left wing: the marks of every working provider (unique providers in the fixed order claude, codex, opencode, copilot), one glyph of size 14 (11 when two or more providers), frame = size*1.45+1; shown only when the wing is >= 28 wide. Right wing (shown when >= 42 wide): reading text, font size `min(15, height - 7)`, medium weight, monospaced digits, provider tint of the first working provider, scale >= 0.6, trailing aligned. Wing width range 44...80 pt each side; camera gap 6 pt. Click -> `openActivity(.agents)` (opens the island on the AI Agents page). Accessibility: label = provider names, value = the reading.
- Capsule form (`NotchCapsuleAgentStrip`) for non-notch surfaces (this is what Trayloom shows on every display, since Windows has no notch): `HStack(spacing: CapsuleLayout.spacing) { marks, reading }` with the same reading font as other capsules, `fixedSize`, tint = first working provider. Marks size: `agentMarkSize(working)` = 12 (10 when two+), `agentMarksWidth` = n*(size*1.45+1) + (n-1).
- Reading (`stripReading`) by `notchAgentsReadout`: `elapsed` = clock "m:ss"/"h:mm:ss" since the EARLIEST started open turn (ticks each second; width shape stays stable: every digit treated as "0" for measuring, a new digit count re-measures the island); `tokens` = sum of OUTPUT tokens of the live turns ("4.2K"); `cost` = sum of live turn cost ("$4.56"); `limit` = share of the focused limit of the first working provider ("68%" in the chosen display mode), falling back to elapsed when no limit is known. `tokens`/`cost` change only with a new snapshot; `elapsed`/`limit` use a 1 s timeline.
- Limit focus (`notchAgentsLimitFocus`): `mostUsed` = the binding window (highest used%), `session` = the plan-wide session window, `weekly` = plan-wide weekly window (scope == nil), falling back to the binding window when the account reports no such window.
- Animation (`NotchAgentAnimationView`): the glyph "breathes": scale `0.84 + 0.16*b`, opacity `0.7 + 0.3*b`, where `b = swing(t/1.2)`: `out = triangle wave over 0..1..0 with period 2*1.2 s`, `b = (1 - cos(pi*out))/2` (ease-in-out). The pulse dot (5 pt in cards/headers): dot of diameter `size` and an expanding ring (stroke 1 px, same tint): ring scale `1 + 0.9*s`, ring opacity `0.6*(1 - s)`, `s = sin(pi/2 * ((t mod 1.6)/1.6))`. Both are stepped by a shared 30 Hz decorative clock (never run at display refresh rate), only while the view is visible and not occluded and Reduce Motion is off (static otherwise). Windows: a `DispatcherTimer`/`CompositionTarget.Rendering` throttled to 30 Hz, stopped when the island window is hidden/minimised.

### A7.3 Resting island (idle) with AI limits (`NotchAgentRestingWing`)
When Settings > Activity > "resting content" = AI limits (`notchIdleContent == agents`): the idle island shows, left wing a 11x11 ring (stroke 2) of the focused/most-spent limit across ALL accounts (`restingLimit`) and, right wing, the percentage (9 medium monospaced), refreshed every 60 s; coloured by `agentLimitTint`. If no limit is known: left = the first seen provider's mark (size 10) and right = today's API value (9 medium, scale 0.7). Option label: "AI limits" (`restingTitle`), icon `sparkles`; it is offered only when the AI section is available (and can rest, `NotchSupport.canRest(with: .agents)`). Because an idle island needing limits makes `limitsInUse` true, the Claude/Codex checks keep running in the background at the 5-minute cadence.

---------------------------------------------------------------------------------------------------------------------------------

## A8. Settings (Settings > Dynamic Island > Content > "AI Agents" card, `NotchAgentsSettingsControls`) - top to bottom

All controls use the settings row styles of the app (SettingsRow, SettingsMenuRow, SettingsChoiceRow); switches are trailing `.switch` toggles; the whole page re-syncs the island (`NotchService.syncWithPreferences`) on any change.

1. Description (callout, secondary): "Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this Mac and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your Mac." (Windows: "... record on this PC and the plan limits the Claude app saves. ... never leaves your PC.")
2. Four provider rows (`providerRow`): [26x26 mark tile, corner 7, tinted 14%] [name + caption "Found on this Mac" / "Not found on this Mac" (caption 12 secondary)] [switch]. Names: "Claude", "Codex", "OpenCode", "GitHub Copilot". The last enabled agent's switch is disabled (at least one stays on; turning the whole section off stops all).
3. If Claude on: **Claude account** group (`ClaudeAccountSettings`).
4. If Codex on: **Codex accounts** group (`CodexAccountsSettings`).
5. Divider. Subheading "Cards". Reorderable grid (adaptive columns min 104, spacing 8) of editor items (icon + card name, included/hidden check) - drag to reorder (keyboard/accessibility: "Move up"/"Move down" actions). Hint (caption): "Drag to reorder. Charts take the full width of the island." If Codex on and Resets card included: caption `resetsHelp` ("A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which Vorssaint never reads." -> Windows: "...which Trayloom never reads.").
6. Choice row (icon limits gauge) "Show limits as": segmented `Left` | `Used` (`notchAgentsLimitDisplay`: remaining/used, default remaining).
7. Menu row (icon `rectangle.topthird.inset.filled`) "Limit to show": `Most used` | `Session` | `Week` (`notchAgentsLimitFocus`, default mostUsed). Used by the resting island and by the live reading when it shows a limit.
8. Divider. Subheading "While an agent works". Switch row (icon `waveform.path.ecg`) "Show it in the closed Dynamic Island" (`notchAgentsLiveActivity`, default on). When on (indented): menu row (icon `camera.metering.center.weighted`) "Beside the camera" with options `Time`, `Tokens written`, `API value`, `Limit` (`notchAgentsReadout` elapsed/tokens/cost/limit, default elapsed; the label for cost is "API value" capitalised from the string) and a live SAMPLE strip below it: a 24 pt high black notch-shaped pill (`camera 64 + 116` wide, radius 9, dark scheme) with the glyph left, the reading right in the provider tint (uses real live data or an example: 12:34 elapsed, 1.18M input/20K output tokens, $4.56, limits 32% session / 57% week). Windows: the sample is a capsule-shaped pill (no camera gap).
9. Divider. Subheading "Alerts". Switch "When a task finishes" (`notchAgentsFinishAlert` default on) -> when on, menu "For tasks longer than" (`notchAgentsFinishMinimum` seconds; options 0 = "Any length", 30 s, 60 s (default), 120 s, 300 s shown as short durations e.g. "30 sec", "1 min", "2 min", "5 min"; clamp 0...3600). Switch "Near a plan limit" (`notchAgentsLimitAlert` default on) -> when on, menu "Warn at" (`notchAgentsLimitThreshold`: 50, 75, **80**, 90, 95 shown as "50% used"...; clamp 1...100). Menu "Daily API value budget" (`notchAgentsDailyBudget`: Off(0), $5, $10, $25, $50, $100, $250; default Off).
10. Caption (valueNote): "API value is what the same work would cost at API list prices. Plans charge a fixed price instead."
11. Row "Keep prices up to date" with caption "Downloads the public price list from GitHub once a day, so new models get an API value without an app update. Nothing about your usage is sent." (+ " Price list of <day>" when a list date is known; Windows wording: "from the Trayloom project"), switch (`notchAgentsPriceUpdates`, default on).
12. If Claude on: Divider, subheading "Claude plan limits", then a status line refreshed every 30 s (`claudeLimitsStatus`) - see states below; spinner when a Claude check is running; if the default profile is selected, a caption "Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which Vorssaint never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this Mac are used." (Windows: Trayloom / this PC).

Status line states (icon + text, `.callout`, wrapping):
- Account reading exists (source account): green check if < 30 min old else orange exclamation: "Claude Code · Updated <relative>" (+ the failure explanation underneath when the last check failed and the reading is stale).
- Non-default profile, no reading: info icon + failure text or "Claude Code has not returned limits for this account yet."
- App history fresh (< 30 min): green check "Read from the Claude app, updated <relative>".
- No Claude app found and no history: info icon "Plan limits come from Claude Code or from the Claude app, which is not on this Mac. Until then, the 5-hour session is estimated from Claude Code activity." + button "Get the Claude app" (opens https://claude.ai/download).
- App found but history stale/missing: orange exclamation "The Claude app last checked them <relative>." + "Claude checks your limits only while its icon is in the menu bar. Turn the icon on in the Claude app settings, and your limits appear here within minutes." (Windows wording: "... while its icon is in the notification area (system tray). Turn the icon on in the Claude app settings...") + button "Open Claude". Claude desktop on Windows = MSIX `Claude_pzs8sxrjxfjjc` (launch through its AppUserModelID / `shell:AppsFolder`), detection = presence of the history file/package.
Failure explanations: missing "Claude Code was not found on this Mac."; signed out "Sign in to Claude Code so it can check your account’s limits."; outdated "Update Claude Code so it can check your account’s limits."; unreachable "Claude Code could not check your account’s limits. It tries again later."

### A8.1 Claude account (`ClaudeAccountSettings`)
- Menu row (icon `person.crop.circle`) "Claude account": options = [Default account] + discovered profiles + the saved one; label = `<name>` or `<name> · <email>`. Stored in `notchAgentsClaudeProfile` (empty = default `~\.claude` + `~\.claude.json`). Profile name = folder name with a leading `.claude-` removed (max 80 chars).
- Under it: if the chosen folder is invalid -> orange caption "Choose an existing Claude Code profile folder that belongs to you and contains its own .claude.json file. The folder must not be writable by others, and the file must not be a link."; unknown identity -> secondary "No account identified in this profile."; path caption (middle truncated, tooltip full path) when not default.
- Button "Choose account folder…" (folder picker showing hidden folders, starting at home). Rule: choosing `~\.claude` itself without a nested `.claude.json` means the default account (clears the setting); otherwise the folder must satisfy `isAvailable` else an alert titled "Claude account" with the invalid-folder text and OK.
- Caption: "Choose a profile already signed in to Claude Code. Usage comes from that profile’s sessions, and Claude Code checks its account’s limits. Changing this selection does not change the account in your other apps."
- Link "How to sign in to another Claude account" -> https://code.claude.com/docs/en/authentication#log-in-with-multiple-accounts.
- Discovery (`ClaudeAccountProfile.discover`): `~\.claude`, `~\.claude-*` and `~\.config\claude` folders that are available; at most 32. Refreshed on appear, on selection change, when the app becomes active, and when the identity changes.
- `isAvailable` for a non-default profile: absolute path <= 4096 UTF-8 bytes; not `/` and not the home folder; is a real directory owned by the current user and not group/other-writable; contains its own regular `.claude.json` owned by the user with a single hard link. **Windows:** replace Unix owner/permission tests with: directory exists, is not a reparse point/junction/symlink, is on a local fixed or user-profile volume, owner SID == current user (or Administrators), and the DACL does not grant write to Everyone/Users/Authenticated Users; the `.claude.json` must exist, be a regular file (no reparse point), hard-link count 1, owner = current user.
- Profile log roots: default = `~\.claude\projects` + `~\.config\claude\projects`; profile = `<dir>\projects`; config/profile JSON = `<dir>\.claude.json` for a non-default profile.

### A8.2 Codex accounts (`CodexAccountsSettings`)
- Subheading "Codex accounts"; caption "Each CODEX_HOME folder you add gets its own limits card. While the AI Agents page is open, Codex checks each one about every five minutes with that folder’s own sign-in. No prompt is sent."
- One row per saved account: text field (placeholder "Account name", max 40 chars, cannot be emptied) + the folder path (caption, middle truncated) + a minus-circle button (tooltip "Remove this account’s card").
- Buttons: "Find local accounts" (discovers `~\.codex` and `~\.codex-<name>` homes; the usual one is named "Default"; `.codex-work` -> "Work"), "Add folder…" (folder picker, starting at home), "Check limits" (only when accounts exist; disabled when the service is disabled or every account is already checking). Find/Add disabled at 8 accounts.
- Footer caption: with no accounts "With no accounts added, Codex limits come from its local sessions." else "The Resets card still belongs to the default Codex account."
- Storage `notchAgentsCodexProfiles`: JSON array `[{"id":"<uuid>","name":"...","directory":"<abs path>"}]`, max 8, validated on load (UUID id, name 1-40 chars, absolute path, unique ids and unique canonical paths).
- Valid home (`canonicalDirectory`): absolute, resolved, not `/`, not the home folder itself, exists as a directory owned by the user and not group/other-writable; contains `auth.json` that is a regular file owned by the user, mode `0600`-style private (no group/other access) and single-linked, OR (no auth.json) a regular `config.toml` (sign-in may live in the OS keychain). Invalid -> alert "Choose a Codex folder" / "Choose a separate CODEX_HOME folder with config.toml or auth.json in it. The folder must be yours and not writable by others, and auth.json must be private and not a link. Codex may refresh its own sign-in and caches there." **Windows:** same intent via ACL/owner/reparse-point/hard-link checks on `auth.json` (existence/metadata ONLY - never read its contents).
- Account checks run through `CodexProfileLimitsService` (A3) only when the Limits card is shown (not hidden) and the Codex agent is on.
- Error text in cards: missingCLI -> "Needs the Codex app or CLI"; invalidProfile -> "Choose this account’s folder again in Settings."; needsSignIn -> "Sign in with ChatGPT in this Codex folder."; outdated -> "Update Codex to check limits."; unavailable/cancelled -> "Could not check limits. Try again."

### A8.3 Settings key table
| Key | Type | Default | Values / notes |
|---|---|---|---|
| `notchAgentsEnabled` | bool | false | master switch of the AI Agents section (existing users who had it on keep it via a one-time migration marker `notchAgentsOptInMigrated`, local only) |
| `notchAgentsClaude` / `notchAgentsCodex` / `notchAgentsOpenCode` / `notchAgentsCopilot` | bool | true each | absent key = true; at least one must remain on in the UI |
| `notchAgentsClaudeProfile` | string | "" | "" = default account, else absolute config dir |
| `notchAgentsCodexProfiles` | string (JSON) | "" | see A8.2, max 8 |
| `notchAgentsCardOrder` | string (csv of card ids) | "" | any order of `limits,spend,live,trend,models,projects,activity,resets`; missing cards are appended |
| `notchAgentsHiddenCards` | string (csv) | "" | cards not shown |
| `notchAgentsPeriod` | string | `today` | `today`/`week`/`month` |
| `notchAgentsLimitDisplay` | string | `remaining` | `remaining`/`used` |
| `notchAgentsLimitFocus` | string | `mostUsed` | `mostUsed`/`session`/`weekly` |
| `notchAgentsLiveActivity` | bool | true | closed-island strip |
| `notchAgentsReadout` | string | `elapsed` | `elapsed`/`tokens`/`cost`/`limit` |
| `notchAgentsFinishAlert` | bool | true | |
| `notchAgentsFinishMinimum` | double (s) | 60 | 0,30,60,120,300 (clamped 0..3600) |
| `notchAgentsLimitAlert` | bool | true | |
| `notchAgentsLimitThreshold` | double (%) | 80 | 50,75,80,90,95 (clamped 1..100) |
| `notchAgentsDailyBudget` | double (USD) | 0 | 0=off,5,10,25,50,100,250 |
| `notchAgentsPriceUpdates` | bool | true | |
Also relevant: `notchIdleContent` = `none|battery|music|agents`; the section toggle is mirrored by the `modules` list (`agents` must be in the island's module list - Part B section 2).

---------------------------------------------------------------------------------------------------------------------------------

## A9. Strings (English, verbatim from `NotchAgentStrings.enUS`, `ClaudeAccountStrings`, `CodexAccountStrings`)

Windows rewrites are shown after `=>` only where the English text says Mac/menu bar/Vorssaint.

```
title                    "AI Agents"
hubDescription           "Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island."
settingsDescription      "Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this Mac and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your Mac."   => "...on this PC ... never leaves your PC."
restingTitle             "AI limits"
limitsCard               "Limits"          spendCard "Spending"     liveCard "Now"      trendCard "Trend"
modelsCard               "Models"          projectsCard "Projects"  activityCard "Activity"   resetsCard "Resets"
today "Today"   week "7 days"   month "30 days"
session "Session"   weekly "Week"
estimated                "Estimated from this Mac"                              => "Estimated from this PC"
claudeLimitsHint         "Set up plan limits…"
waitingForLimits         "Limits appear after the next reply"
noSession                "No session running"
apiValue                 "API value"
tokensFormat             "%@ tokens"       cachedFormat "%@ from cache"    writtenFormat "%@ written"
savedFormat              "%@ saved by the cache"
planMultipleFormat       "%1$@ the price of %2$@"
idle "Idle"   noActivity "Nothing in this period"
empty                    "No usage from Claude Code, Codex, OpenCode or GitHub Copilot yet. It appears here as soon as any of them works on this Mac."   => "...on this PC."
loading                  "Reading usage…"
noCards                  "Choose what this page shows in Dynamic Island settings."
unpriced                 "Some models have no known price, so this is a minimum."
streak "Days in a row"   activeDays "Active days"   busiestDay "Busiest day"
finishedFormat           "%@ finished"     limitRenewed "Limit renewed"      budgetTitle "Daily budget"
leftFormat "%@ left"     usedFormat "%@ used"
agents "Agents"          found "Found on this Mac" => "Found on this PC"     notFound "Not found on this Mac" => "Not found on this PC"
cardsTitle "Cards"       cardsHint "Drag to reorder. Charts take the full width of the island."
limitsAs "Show limits as"   remaining "Left"   used "Used"   limitFocus "Limit to show"   mostUsed "Most used"
liveTitle "While an agent works"   liveActivity "Show it in the closed Dynamic Island"
readout "Beside the camera"  (Windows: "Beside the island centre" / keep "Beside the camera" only if the camera fit exists)   readoutElapsed "Time"   readoutTokens "Tokens written"   readoutLimit "Limit"   (cost readout label = apiValue capitalised: "API value")
alerts "Alerts"   finishAlert "When a task finishes"   finishAfter "For tasks longer than"   anyLength "Any length"
limitAlert "Near a plan limit"   limitAt "Warn at"   budget "Daily API value budget"   off "Off"
updatedFormat "Updated %@"
claudeLimitsTitle "Claude plan limits"
claudeLimitsCurrentFormat "Read from the Claude app, updated %@"
claudeLimitsStaleFormat   "The Claude app last checked them %@."
claudeLimitsMenuBar       "Claude checks your limits only while its icon is in the menu bar. Turn the icon on in the Claude app settings, and your limits appear here within minutes."  => "...while its icon is in the notification area..."
claudeLimitsNoApp         "Plan limits come from Claude Code or from the Claude app, which is not on this Mac. Until then, the 5-hour session is estimated from Claude Code activity."
claudeLimitsPrivacy       "Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which Vorssaint never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this Mac are used."  => Trayloom / this PC
claudeCodeMissing "Claude Code was not found on this Mac."   claudeCodeSignedOut "Sign in to Claude Code so it can check your account’s limits."
claudeCodeOutdated "Update Claude Code so it can check your account’s limits."   claudeCodeUnreachable "Claude Code could not check your account’s limits. It tries again later."
openClaude "Open Claude"   getClaude "Get the Claude app"
priceUpdates "Keep prices up to date"
priceUpdatesHint "Downloads the public price list from GitHub once a day, so new models get an API value without an app update. Nothing about your usage is sent."
pricesFromFormat "Price list of %@"
valueNote "API value is what the same work would cost at API list prices. Plans charge a fixed price instead."
useReset "Use a reset"   resetConfirm "Reset the session and weekly limits now?"   confirmReset "Reset"   resetting "Resetting…"
resetDone "Limits reset"   resetNotNeeded "Your usage doesn’t need a reset yet"   resetTaken "That reset was already used"   resetFailed "Couldn’t use the reset"
resetsNone "No resets available"   resetsExpiryFormat "Next expires %@"
resetsNeedCodex "Needs the Codex app or CLI"   resetsSignIn "Sign in to Codex with a plan to see resets"   resetsUpdate "Update Codex to use resets here"   resetsCheckFailed "Couldn’t check resets"
resetsHelp "A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which Vorssaint never reads."  => Trayloom
```
Shared strings reused by the page: Cancel (clipboard strings `cancel`), Retry (`notchMusicExtras.retry`), Move up / Move down (clipboard strings).

Claude account (`ClaudeAccountStrings.enUS`): title "Claude account"; defaultAccount "Default account"; chooseFolder "Choose account folder…"; explanation, invalidFolder, unknownAccount "No account identified in this profile.", noLimits "Claude Code has not returned limits for this account yet.", signInHelp "How to sign in to another Claude account" (full texts quoted in A8.1).
Codex accounts (`CodexAccountStrings.enUS`): title "Codex accounts"; accountName "Account name"; remove "Remove this account’s card"; find "Find local accounts"; add "Add folder…"; check "Check limits"; fallback "With no accounts added, Codex limits come from its local sessions."; resetsNote "The Resets card still belongs to the default Codex account."; chooseFolder "Choose a Codex folder"; invalidFolder (A8.2); folderProblem "Choose this account’s folder again in Settings."; defaultName "Default"; defaultAccount "Default account"; refreshHint "Refresh to check this account’s limits."; signIn "Sign in with ChatGPT in this Codex folder."; update "Update Codex to check limits."; failed "Could not check limits. Try again."

---------------------------------------------------------------------------------------------------------------------------------

## A10. Windows mapping and adaptation notes (agents)

| Concern | Windows implementation |
|---|---|
| Home / paths | `Environment.GetFolderPath(UserProfile)`; honour `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `XDG_DATA_HOME`. |
| File reading | `FileStream(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)`; 4 MiB chunks; line splitter on 0x0A (also tolerate `\r\n`); `Utf8JsonReader`. |
| Watching | One `FileSystemWatcher` per existing root (`IncludeSubdirectories`, filters `*.jsonl`, `opencode.db*`; `InternalBufferSize` 64 KB; on `Error`/overflow do a full rescan) + 2 s poll of recently-modified logs + 30 s sweep + 5 min root re-check. |
| SQLite | `Microsoft.Data.Sqlite` with `Mode=ReadOnly` (`Data Source=...;Mode=ReadOnly`); WAL read needs the `-shm`; if open fails, copy `opencode.db*` to `%TEMP%` and read the copy; busy timeout 2 s. Use the exact SELECT shape of `AgentOpenCodeReader` (A4.5) so no message text is ever extracted. |
| Processes (CLI conversations) | `System.Diagnostics.Process` with `RedirectStandardInput/Output`, `CreateNoWindow=true`, `StandardOutputEncoding=UTF8`; read lines on a worker thread with a deadline; kill the process tree on timeout/cancel (`Process.Kill(entireProcessTree:true)`); put it in a Job Object so it dies with Trayloom. `.cmd/.ps1` shims: `cmd.exe /d /c`. |
| Executable lookup | Candidate lists in A1/A2/A3; `File.Exists` + not directory; for npm global installs prefer the native `codex.exe` under `@openai\codex\node_modules\@openai\codex-win32-x64\vendor\...\bin\` over the `.cmd` (avoids Node start-up). Do not enumerate `%ProgramFiles%\WindowsApps`. |
| Liveness of Claude sessions | `OpenProcess` + `GetExitCodeProcess` for the `pid` in `~\.claude\sessions\<pid>.json`; accept `pidDomain` starting `win32:`. |
| Network state | `NetworkInformation.GetInternetConnectionProfile()`/`NetworkChange` events. |
| Pause on lock / display off | `SystemEvents.SessionSwitch` (SessionLock/Unlock), `PowerModeChanged`, monitor-power broadcast `GUID_CONSOLE_DISPLAY_STATE`. |
| Dates/numbers | `CultureInfo.CurrentCulture` for number/relative-time formats; relative dates ("5m ago") via a small formatter; durations as in A6.5. Prices always US dollars with `$`. |
| Settings storage | the app's settings store (JSON/registry) with the key names of A8.3. |
| Archive/cache | `%LOCALAPPDATA%\Trayloom\AgentUsage\agent-usage.bin`, `%LOCALAPPDATA%\Trayloom\agent-prices.json`; create with user-only ACL. |
| Fonts/icons | Segoe UI Variable for text; Segoe Fluent Icons suggestions: gauge `E9D9`/(custom ring), dollar circle `E8EF` (verify), waveform `E9D2`, chart bars `E9D2`/`EB05`-class (verify), cpu `E950`, folder `E8B7`, grid `F0E2`, reset `E777`, refresh `E72C`, warning `E7BA`, checkmark `E73E`, check-circle `E930`, info `E946`, sparkles (custom), minus-circle `F78A`/`E738`, up-forward arrow `E8AD`/`E8A7`, flame (custom). All code points are suggestions: verify visually in Character Map.|
| "Mac" wording | Replace "this Mac" with "this PC"; "menu bar" icon with "notification area (system tray)". |
| Impossible/different | There is no camera notch on Windows: the "Beside the camera" strip has no camera gap - the capsule form is used everywhere (A7.2). App-icon marks cannot be pulled from the Claude/Codex apps the same way (no template images) - use bundled vector marks. The Claude desktop app writes `plan-usage-history.json` on Windows as it does on macOS (verified here); whether it needs the tray icon on Windows is UNVERIFIED - keep the wording conditional. `archived_sessions\*.jsonl.zst` (Codex) is not readable without a zstd library - skipped, like the Mac code skips non-`.jsonl`. |

---------------------------------------------------------------------------------------------------------------------------------

## A11. Actual layout of the agent folders on THIS Windows machine (read only; no values copied)

`%USERPROFILE%\.claude` (exists): `.claude.json` (883 B, legacy), `.credentials.json` (SECRET - never read), `settings.json`, `history.jsonl`, `stats-cache.json`, `projects\` (many `<cwd-mangled>\<sessionId>.jsonl`; 1563 `.jsonl` files total incl. subagents; 332 touched in the last 3 days), `sessions\<pid>.json`, `file-history\`, `plans\`, `plugins\`, `skills\`, `shell-snapshots\`, `session-env\`, `telemetry\`, `backups\`, `cache\`, `daemon\`, `ide\`, `jobs\`, `paste-cache\`, `downloads\`, `spares\`.
`%USERPROFILE%\.claude.json` (143 KB) - the file the Mac code reads for the default profile; contains `oauthAccount{accountUuid,emailAddress,organizationUuid,organizationType=claude_max,organizationRateLimitTier=default_claude_max_20x,...}` and `cachedUsageUtilization{fetchedAtMs,accountUuid,utilization{five_hour,seven_day,seven_day_opus,seven_day_sonnet,...}}`.
Claude JSONL line types seen: `user`, `assistant`, `system`, `attachment`, `mode`, `permission-mode`, `last-prompt`, `cost-state`, `file-history-snapshot`, `api-request*`, `atis-latch`; assistant lines carry `message.{id,model,usage,stop_reason,content}`, `requestId`, `sessionId`, `cwd`, `isSidechain`, `timestamp`, `version`.
`%LOCALAPPDATA%\Claude-Data\claude_pzs8sxrjxfjjc\plan-usage-history.json` (version 2, 402 samples). `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc` is the MSIX package of the Claude desktop app. `%USERPROFILE%\.local\bin\claude.exe` is the CLI.
`%USERPROFILE%\.codex` (exists): `sessions\2026\10\25\rollout-*.jsonl` (1 file), `archived_sessions\*.jsonl.zst`, `auth.json` (SECRET - never read), `config.toml`, `history.jsonl`, `session_index.jsonl`, `models_cache.json`, `*.sqlite` (goals/logs/memories/queue - not used), `.sandbox*`, `plugins\`, `rules\`. Rollout line types: `session_meta`, `turn_context`, `token_usage_record`, `event_msg` (`task_started`, `token_count`, `thread_settings_applied`, `item_completed`, ...), `response_item`, `world_state`. Codex CLIs: `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`, npm vendor `codex.exe`, shim `%APPDATA%\npm\codex.cmd`.
`%USERPROFILE%\.local\share\opencode\opencode.db` (+ `-wal`, `-shm`, `auth.json` SECRET, `log\`, `repos\`) - v1 schema (tables `account, account_state, control_account, credential, data_migration, event, event_sequence, message, migration, part, permission, project, project_directory, session, session_context_epoch, session_input, session_message, session_share, todo, workspace`; both `message`/`session` and `session_message` present -> detect v2 via `session_v2`).
`%USERPROFILE%\.copilot` (exists): `config.json`, `session-state\<sessionId>\{events.jsonl,workspace.yaml,checkpoints\,files\,research\,inuse.<pid>.lock}`, `logs\`, `ide\`, `installed-plugins\`. Event types seen: `hook.start/end`, `assistant.turn_start/turn_end`, `assistant.message`, `session.usage_record`, `tool.execution_*`, `permission.*`.
Not present here: `%USERPROFILE%\.config\claude`, `%APPDATA%\Claude\plan-usage-history.json` (data is under `%LOCALAPPDATA%\Claude-Data`), `%USERPROFILE%\.claude\local`.

---------------------------------------------------------------------------------------------------------------------------------

## A12. Gap vs Trayloom (agents)

Current `E:\REA TEST 1\trayloom\src` contains NO agents code at all (a repo-wide search for agent/claude/codex only hits the Shelf window). Everything in A1-A10 is new:
1. New project/folder `Trayloom.Features.Agents` (or inside Island): `AgentUsageService`, `AgentUsageStore`, `AgentLogParser` + 4 readers, `AgentOpenCodeReader`, `AgentClaudeServer`, `AgentClaudeAppUsage`, `AgentCodexServer`, `CodexProfileLimitsService`, `AgentCodexResetService`, `AgentPricing/AgentPriceSource` with embedded `agent-prices.json`, `AgentUsageSummary`, `AgentUsageArchive`, `ClaudeAccountProfile`, `CodexAccountProfile`, `NotchAgentSupport`/`AgentFormat`.
2. Island: new "AI Agents" section (page, 8 cards, period menu, scrolling grid), live-activity capsule with animated glyph/pulse (30 Hz clock), resting-island AI limits wing, notices for finish/limit/renewal/budget, activity-picker integration.
3. Settings: AI Agents block in the Content tab (providers with found/not found, Claude account, Codex accounts, cards editor, limit display/focus, live readout + sample, alerts, budget, price updates, Claude limits status).
4. Strings: add all `NotchAgentStrings`, `ClaudeAccountStrings`, `CodexAccountStrings` English texts (Windows-adapted).
5. Decisions to flag to product owner: (a) hosting URL for the price list; (b) bundled provider marks (licensing - use generic marks if brand assets cannot be shipped); (c) verify on Windows whether Claude desktop updates `plan-usage-history.json` without a tray icon; (d) zstd support for `archived_sessions` is optional.


---------------------------------------------------------------------------------------------------------------------------------

# PART B - ISLAND SHELL, WINDOW, GEOMETRY, SETTINGS PAGE

## Dynamic Island - core shell, settings page and chrome

Spec area: the island window itself (states, geometry, motion, gestures, display handling), the island's own chrome
(top row, Explore gallery, quick-access buttons, update control, capsule/notch strips) and the whole
"Dynamic Island" settings page (5 tabs). Section *contents* (Music, Timer, Calendar, Clipboard...) are specified in
other files; this file only specifies how the shell hosts them and the settings that belong to the island page.

Conventions
- Product name in all spec text: Trayloom. The feature keeps the English name "Dynamic Island" (it is a UI string, quoted verbatim).
- "pt" = macOS points = WPF DIPs. All sizes below are DIPs and must be multiplied by the monitor scale when placing the Win32 window.
- Source files cited as `Services/Notch/...`, `UI/Notch/...`, `UI/Settings/...`, `Core/...` are paths inside the Swift source tree under the vorssaint-mac Sources folder.
- Strings are the `enUS` cases. Where the English text mentions macOS-only concepts the Windows-adapted string is given as **Win:**.
- "Segoe" glyph codes are suggestions (Segoe Fluent Icons); verify visually. SF Symbol name is kept for traceability.

---------------------------------------------------------------------------------------------------------------------

### 1. Mental model

The island is ONE borderless always-on-top overlay window, horizontally centred on a display, glued to its top edge.
It has these visual forms (all drawn black, white foreground, always dark colour scheme):

| Form | When | Rough size |
|---|---|---|
| Resting (bare) | nothing to show | camera-sized strip (see 4.2) |
| Resting with content | idle content = battery / music / AI limits, or companion | strip with two 44 pt "wings" |
| Activity strip | timer, watch, downloads, AI agents, calendar countdown, music, keep-awake | strip, wings 44-150 pt |
| Hover emphasis | pointer rests on the closed island | resting + up to 10 pt each side, +5 pt tall |
| Activity picker | pointer rests while >1 activity is live | strip + button grid below |
| Notice | transient feedback (volume, brightness, battery, banner...) | strip with two asymmetric wings |
| Peek | hover preview (when "Preview on hover") | 340x(safeTop+52) |
| Notification preview | hover on a mirrored banner | card ~400 wide |
| Expanded | a page, the Explore gallery, app panel, metric detail or Command Bar | 360-600 wide |
| Drop placeholder | file drag in progress (Files section on) | peek width x (safeTop+66) |
| Capture controls | screen-capture toolbar | own layout, collapsible |
| Full-screen compact | "Hide content in full screen" and a full-screen app is in front | bare cutout / nothing |

Floating "quick-access" circle buttons (44 pt) drop out of the island's left, right and bottom when it is open.

#### 1.1 Windows simplification of the notch model (IMPORTANT)
Windows has no camera housing and no menu bar. Treat **every Windows display as a "display without a notch"**:
- `isNotched = false`, `cameraWidth` is the *simulated* camera: `180 * stripProfileHeight / 32`.
- Synthetic menu-bar height `barHeight = 24` DIP (clamped 16-64 in the Swift model). `barEdge = 0`.
- `compactSideRoom` (free menu space beside the camera) is unlimited on Windows (no menus to avoid): use `screen.width` so wings never get dropped and "Show over the menus" is always satisfied.
- Silhouette: `capsule` (default; a rounded pill floating `gap` DIP from the top and bottom of a 24 DIP strip) or `notch` (flat top edge, hanging, shoulders flare outward). Both options remain user-selectable.
- Settings that only make sense with a physical camera or a macOS menu bar are NOT shown on Windows: "Notch fit" card, "Hide gap below notch", "Show over the menus", the Accessibility permission hint. (Their keys may still be stored for settings-backup parity, with Swift defaults.)

Worked numbers for the default Windows case (24 DIP bar, capsule, scale 1, fit 0):
- capsule gap = min(2, max(0, floor((24-20)/2))) = 2 -> strip 24 high, capsule body 20 high.
- cameraWidth (simulated) = 180*24/32 = 135.
- Bare resting capsule: shoulders = shoulder(24)*2 = min(14, 24*0.19)=4.56 -> 9.12; capsule = max(5*20 + fitWidth, 2*20) = 100; width = round(100+9.12) = **109 x 24**.
- Resting with content (wings): width = min(screen-24, cameraWidth + 2*44) = **223 x 24** (wings are 44 only when room >= 44, otherwise 0).

---------------------------------------------------------------------------------------------------------------------

### 2. Sections ("modules") of the island

Source: `Services/Notch/NotchSupport.swift` (`NotchModule`), `UI/Notch/NotchView.swift` (titles), `UI/Settings/NotchContentEditor.swift` (tints).

Default order = declaration order (also the order in Explore and in the Content tab list). Order is user-editable (key `notchModuleOrder`).

| # | id (rawValue) | Title (EN) | SF symbol | Suggested Segoe | Direct key (with Alt+Cmd) | Page top row | Tint (settings tile) | Available when |
|---|---|---|---|---|---|---|---|---|
| 1 | controls | Controls | slider.horizontal.3 | E9E9 | C | row | blue #0A84FF | always |
| 2 | mixer | Volume mixer | speaker.wave.2 | E767 | V | row | purple #BF5AF2 | Mixer feature on |
| 3 | music | Now Playing | music.note | E8D6 | M | wrapsCamera | pink #FF375F | always |
| 4 | clipboard | Clipboard | doc.on.clipboard | E77F | B | row | brown #AC8E68 | Clipboard history feature on |
| 5 | captures | Recent captures | camera.viewfinder | E722 | S | row | indigo #5E5CE6 | any of screenshot / screen recorder / OCR / colour picker on |
| 6 | files | Files | tray.full | E8B7 | F | row | cyan #64D2FF | Shelf feature on |
| 7 | system | System | gauge.with.dots.needle.50percent | E9D9 | I | none | green #30D158 | any monitor feature (CPU, GPU, memory, network, disk, power, fans, connected devices) |
| 8 | tools | Tools | (Quick Launcher mark) | E90F | T | row | gray #98989D | Quick Launcher on |
| 9 | calendar | Calendar | calendar | E787 | A | row | red #FF453A | Calendar feature on + `notchCalendarEnabled` |
| 10 | notifications | Notifications | bell | EA8F | N | row | orange #FF9F0A | Notifications feature on + `notchNotificationsEnabled` |
| 11 | timer | Timer | timer | E916 | R | row | mint #63E6E2 | Timer feature on + `notchTimerEnabled` |
| 12 | camera | Camera mirror | web.camera | E722 | W | row | teal #40C8E0 | Camera preview feature on + `notchCameraEnabled` |
| 13 | downloads | Downloads | arrow.down.circle | E896 | D | row | blue | Downloads feature on |
| 14 | scratchpad | Scratchpad | note.text | E70B | P | row | yellow #FFD60A (glyph black 75 %) | Scratchpad feature on |
| 15 | agents | AI Agents | sparkles | (custom) | G | row | rgb(217,120,87) | AI Agents feature on + `notchAgentsEnabled` |
| 16 | watch | Watch | eye | E7B3 | O | row | purple | Watch feature on + `notchWatchEnabled` |

Notes
- `NotchSupport.modules()` = (stored order + allCases, de-duplicated) minus ids in `notchHiddenModules` minus unavailable ones minus the per-module enable keys (timer/camera/calendar/notifications/agents/watch). The result is the sequence the island and Explore show.
- "Direct key" is shown in each Explore tile as `⌥⌘` + letter (Windows: `Ctrl+Alt+` letter; see 7.4).
- Page top: `row` = the page puts its controls in the 36 pt top row; `none` = nothing in the row (System); `wrapsCamera` = the page lays out its own leading side (Music: the cover sits where the camera would be).
- The selected module is remembered (`selected`); if it becomes unavailable the first available module is selected (fallback `controls`).
- Home ("Controls") page shortcut items (`NotchControlItem`), default order: volume, brightness, keyboardLight, music, mixer, keepAwake, timer, calendar, microphone, screenshot, recording, speedTest, panel, commandBar, scratchpad. Hidden by default (`notchHiddenControls`): `keyboardLight,microphone,screenshot,recording,speedTest,panel,commandBar,scratchpad`. Levels (`isLevel`): volume, brightness, keyboardLight. Item titles: Volume, Brightness, Keyboard light, Now Playing, Volume mixer, Keep awake, Timer, Calendar, Mute microphone, Screenshot, Screen recording, Speed test, Open app panel, Command Bar, Scratchpad. (Controls page body is specified elsewhere.)

---------------------------------------------------------------------------------------------------------------------

### 3. State machine (NotchService)

`Services/Notch/NotchService.swift`. Published state: `expanded`, `peeking`, `pinned`, `selected`, `showingSections`, `showingAppPanel`, `selectedMetric`, `showingCommandBar`, `notice`, `noticeExpanded`, `dragPlaceholder`, `captureControls`(+collapsed), `hiddenInFullscreen`, `hoverEmphasized`, `inside`(pointer).

#### 3.1 Priority of what the closed surface shows (surfaceSize / NotchView.surface)
1. Full-screen compact (hidden in full screen) -> bare cutout / nothing.
2. Capture controls (collapsed strip `cameraWidth+56` x strip, or full toolbar).
3. Expanded (page / Explore / app panel / metric / Command Bar).
4. Drop placeholder (dashed 16 pt-radius rounded rectangle, 1 pt stroke white 24 %, dash 5/4, text "Drop files here" 13 pt medium, min height 52, horizontal padding 18).
5. Notice (or its departing copy).
6. Peek.
7. Activity picker (hover with >1 live activity).
8. Activity strip (first of the live activities in the order below, or the user-picked one).
9. Departing / lingering music strip.
10. Resting (bare capsule/notch, or wings with idle content, or companion).

#### 3.2 Activity priority (`NotchSupport.compactActivities`)
Order of precedence (first live wins unless the user picked one in the picker): **timer, watch, downloads, agents, calendar, music, keep-awake**. Keep-awake is last because it can run all day.
Activity sources:
- timer: a running/paused/finished timer session that shows an activity (hidden when `notchHideTimerCountdown`).
- watch: Watch feature active.
- downloads: `routes(.download)` and a download that is active and not completed.
- agents: `notchAgentsLiveActivity` on and any live agent.
- calendar: countdown enabled (all timed events, or those the user chose with the context menu) and inside the lead window; or an ongoing event with `notchCalendarTimeLeft` inside the last hour.
- music: `showsMusicActivity` = playing && Music section visible && idle content != none (or companion on) && `notchShowPlayingMusic`.
- keep-awake: `notchKeepAwakeActivity` on and a Keep Awake session running.
A user choice sticks while its activity exists (`NotchActivitySelection`), survives short gaps (song change), and clears when nothing is live.
Pairs ("Combine", companion on the camera's left): timer + {downloads, agents (running), calendar (running), music (running)}; calendar + {downloads, agents, music}. Picker shows a "Combine" menu listing `Companion + Primary` titles.

#### 3.3 Transitions
- `open(module?, pinned, takeFocus, appPanel, metric, sections)`: refused when island disabled/suspended; if capture controls are up it expands them instead. Destination = given module if available, else the "reopening destination" (see 11.4). Sets `expanded`, clears peek, makes panel key when `takeFocus`, plays haptic (if enabled and the presentation changed), installs click-away + key monitors, remembers the clipboard paste target (if clipboard section on).
- `collapse()`: refused while capture controls up or a held drag; clears pinned, section search, Explore state, selected metric; resigns key; removes monitors.
- `toggle()`; `toggleSections()` (Explore); `goBack()` (leaves app panel/metric detail); `stepBack()` = Esc: detail -> page (or close if opened from outside), page layer (a sheet over the page) -> closes that layer, else collapse.
- Opened by hover (`openedByHover`, no key focus) closes when the pointer leaves; opened by click/shortcut/gesture stays until clicked away, Esc, Collapse, or another app becomes active.
- `closesOnActivation`: another app becoming frontmost closes the island like a click away, EXCEPT a hover-opened island with the pointer still resting inside and no click.
- `menuPanelWillShow` (the app panel opens from the tray) collapses the island.
- Settings write -> debounced `syncWithPreferences()`: starts/stops services, rebinds event routing, rebuilds modules, re-reads geometry.

#### 3.4 Hover algorithm (`hover(entered)`)
- Hover opening is enabled by `notchOpenOnHover` (or, in full screen, by `notchHoverInFullscreen`).
- Emphasis (pulse): inside && closed && no notice/peek/placeholder/capture && hover will not open fully && not Reduce Motion -> island grows by `min(10, freeSideRoom)` on each side and +5 pt tall (capsule: width only). Hover emphasis is cancelled when the pointer leaves.
- Opening: pointer inside for `notchHoverDelay` (default **0.25 s**, range 0.10-1.00, slider step 0.05) -> `open(takeFocus:false)` if `notchHoverExpands`, else `peeking = true` + haptic. The deadline is NOT restarted by repeated enter reports. Cancelled if a drag, pin, notice (unless hidden-until-hover), picker, capture controls, tracking menu, or `hoverState.suppressed` applies. After an explicit close with the pointer still inside, hover is suppressed until the pointer actually leaves.
- With a compact activity showing and wings present and `notchHoverExpands` off, hover does nothing (the strip pulses only); a click opens.
- Closing: when `openedByHover` (expanded) or peeking, pointer outside for `notchCloseDelay` (default **0.18 s**, range 0.10-2.00) -> collapse (peek closes after 0.12 s). Delay is not applied if the pointer is over a child window (popover/menu) or the on-screen keyboard.
- While the pointer is in the hover-exit corridor around the floating buttons, moves are polled so leaving is always noticed.
- Mirrored notification banner (system notifications): hovering it holds it (stops its 3 s timer); hovering for the hover delay opens the whole message in place (`noticeExpanded`) if `notchOpenOnHover`; leaving dismisses it.
- Click on the closed island: opens the *visible activity's page* (`notchOpensActivity` true) else the reopening destination. Calendar countdown click opens Calendar scrolled to that event.

#### 3.5 Click-away
Any left/right/other mouse-down outside the island window (and outside its child popups, the status icon and mirrored copies) collapses it. A capture preview closes through its controller instead.

---------------------------------------------------------------------------------------------------------------------

### 4. Geometry reference (NotchLayout / NotchGeometry)

`Services/Notch/NotchSupport.swift`.

#### 4.1 Global constants (DIP)
- shoulder radius 14 (clamped `min(14, height*0.19)`); surface bottom radius `min(28, height*0.34)`.
- horizontalInset 28; headerHeight 36 (top row); navigationHeight 36; spacing 12; bottomInset 16; rowSpacing 10.
- content budgets: compact 180, spacious 264, pageContentHeight 320 (vertical pages under a preset).
- outline stroke width 2.
- Home page: cardHeight 80 (min 68), levelRowHeight 28, shortcutWidth 76 x shortcutHeight 74, shortcutSpacing 8 (least 0), shortcutLeastRowSpacing 6, bandLevelMinimumWidth 76, bandLevelSpacing 12, homeReadingWidth 220 (min 160), levelReadingDetailWidth 92.
- System cards 128x72; tools 76x72 spacing 6; Explore tiles 92x86 spacing 8, indicator column 12; clipboard search 36, card 104 (compact 52), spacing 8; emptyHeight 140.
- Music: content width 344, band side 70, artwork 56, title 38, timeline 14, transport 44, head spacing 14, camera clearance 6, bottom room 8, minimum extras 88; lyrics/queue area 216 high.
- Timer: content width 344, row spacing 8, settings row 28, ruler 82 (min 56), start 28.
- Calendar month: weekday row 14, 6 rows of 16-30. Mixer minimum 104. Camera preview crops to 16:9 (wide) or 4:3 (narrow, tall); min page 144.
- Top-row band: camera clearance 8; sections button room 34 (28 + 6).
- Compact strips: edge gap 5; equalizer 7 bars x 1.8 wide, spacing 0.85 of bar width.

#### 4.2 Closed island on Windows (non-notched)
- `stripHeight = cameraHeight = barHeight (24)`; capsule: `stripBodyHeight = 24 - 2*gap(2) = 20`.
- bare size: notch silhouette = `cameraWidth x 24` (135x24); capsule = 109x24 (see 1.1).
- Collapsed with wings: `min(screen.width - 24, cameraWidth + 2 * restingWingWidth)` x 24; wing 44 or 0.
- Capsule corner radius = `min(height/2, 28)`; capsule ends are true half circles.
- Notch shape path: flat top, shoulders flare outward by `shoulder` with cubic arcs (control factor 0.55228475), straight sides, bottom corners radius `min(radius, h/2, (w - 2*shoulder)/2)`.
- Capsule fit sliders move/size the capsule (see 11.2): width -40..40 (even steps of 2), height -4..4, distance from top 0..20.

#### 4.3 Wing ranges by activity (px each side of camera)
timer 44-64 (80 with a download companion); agents 44-80; watch 56-150; calendar 72-120 (paired: 44-120); music = cover + bars inset (computed, >= 44); download default 56; notice default 80; minimum notice wing 36; level notice inset 13; text notice max 240 (accessory 160).
Capsule strip maxima (capsule width of strips): notification 380, notice 340, music 260, calendar 300, download 280, other activity 220. End padding 8; spacing 6; group spacing 10; symbol 12 (frame 16); meter 56x4; calendar dot 6; download meter 36; spinner 16; fonts: title 12 semibold, detail 12 medium, level 12 mono-digit medium, reading 13 mono-digit semibold, small 11 mono-digit medium.

#### 4.4 Peek / notice / notification preview / placeholder
- peek: width `min(screen-24, max(cameraWidth+110, 340))`, height `safeContentTop + 52` (on Windows `safeContentTop` = 16 for capsule, cameraHeight+10 = 34 for hanging notch).
- notice: width `cameraWidth + leadingWing + trailingWing`, height = strip.
- notification preview: width `min(max(400, cameraWidth+200), expandedWidth)`, height `safeContentTop + content + 16`, max `screen.height - 48`.
- drop placeholder: peek width x `safeContentTop + 66`.
- Activity picker: columns `min(3, count)`, row 32, spacing 6, side inset 24, vertical inset 12, "Combine" button 24 high; width = max(strip + 48, columns*(labelWidth+48) + spacing + 2*24) capped at `screen - 24`.

#### 4.5 Expanded island size
`expandedWidth`: preset compact = 480, spacious = 560, custom = `notchCustomWidth` (360-600, default 440). A page with its own natural width takes `min(chosen, natural + 2*28)` under a preset. Final: `min(max(preferred, cameraWidth + 36), screen.width - 24 - 2*72)`.
`expandedHeight = headerTopInset + headerChromeHeight + contentHeight`, capped `screen.height - 48 - bottomQuickAccessInset` and, for custom, `notchCustomHeight` (260-640, default 480).
Windows values (non-notched):
- hanging notch silhouette, page with a top row: headerTopInset 0, pageTopInset 48 (36 row + 12), chrome = 48 + 16 = 64.
- capsule: headerTopInset 12, pageTopInset 60, chrome 64; total = 12 + 64 + content.
- page without a row (System): hanging -> top inset 16; capsule 16.
So Compact home page height = 64 + (content <= 180) [+12 capsule]; Spacious = 64 + (content <= 264) [+12].
Content height per page: controls = fit to content (min with budget); music = player height (+ extras 216, + 38 volume row when "Show volume in the player"); system = rail rows * 72 + 10*(rows-1) + hover inset; tools = rail; timer = fit; agents = its cards; mixer/calendar/clipboard/captures/files/notifications/downloads/camera/scratchpad/watch = the full budget. Detail/app-panel pages use `pageBudget` (320 under presets).
- Sections gallery: tiles 92x86, columns = floor((contentWidth - 12 + 8)/(92+8)), visible rows = as many as fit the page budget (typically 3), size = railHeight(rows) clipped to budget.
- Quick-access gutter: 72 pt each side is reserved *outside* the island (window grows by it), plus 72 below if any bottom button.

#### 4.6 Floating quick-access buttons
- 44 pt black circles, 0.75 pt white 16 % border (60 % when selected or high contrast), glyph 17 pt medium.
- Placement: edge = island body edge; centre offset = `-14 + t*(14 + 12 + 22)` from the edge (t = reveal progress 0..1, up to 1.1); rows spaced **54** pt; side columns start at `max(body.minY + 22 + 12, min(headerTop, body.midY - span/2))`; bottom row centred under the island.
- Max **3 per side** (left, right, bottom); max 64 total stored; label max 40 chars, single line.
- "Drop" shape: a liquid neck joins each circle to the island and dissolves before the circle finishes moving (neck = radius*(1 - t/0.84)).
- Reveal: spring duration 0.38 bounce 0.12 (optional delay), scale 0.4 -> 1 and opacity with progress; withdraw: ease-in 0.16 s. Not interactive until the reveal completes.
- Hover corridor: rectangle spanning all circles of a side, inflated 16, so moving across the gaps keeps the island open; exit delay 0.18 (= `notchCloseDelay` default).
- Selected state: explore = Explore is open, pin = pinned, keep awake = running, microphone = muted, recording = recording, module = that page is open (not in Explore / app panel).
- Dynamic symbols: Explore while open -> "arrow.uturn.backward" with title "Back"; Pin when pinned -> "pin.fill" and title "Allow automatic closing" (else "Keep open"); mic muted -> "mic.slash.fill" / "Unmute microphone" (else "Mute microphone"); keep awake active -> "cup.and.saucer.fill"; recording -> "stop.circle.fill".
- Default layout (stable ids 0001..0005): left = **Explore, Timer**; right = **Settings, Volume mixer**; bottom = **Now Playing**. Stored JSON (version 1, sorted keys) in `notchQuickAccessLayout` (max 32 768 bytes); legacy keys `notchQuickAccessSide/Second/Third` migrate.
- Action ids: `explore`, `settings`, `pin`, a module id, or `control.<item>`. A button whose action is unavailable is hidden in the island (kept in the layout, dimmed in Settings).
- Click action (`activateQuickAction`): explore -> toggle Explore; settings -> open the island's page in Settings; pin -> toggle pin; module -> open it; control.keepAwake / microphone / screenshot / recording / speedTest (opens System > network detail) / panel (app panel) / mixer / music / timer / calendar / commandBar / scratchpad; volume/brightness/keyboardLight -> Controls page.
- If "Explore" is not among the buttons, the top row leads with an Explore button (28 pt icon button, grid glyph) so sections stay reachable.

---------------------------------------------------------------------------------------------------------------------

### 5. Motion

`NotchMotion` springs (perceptual duration + bounce, SwiftUI/Core Animation definition):
| Case | Width | Height |
|---|---|---|
| growing | 0.44 s, bounce 0.25 | 0.38 s, bounce 0.22 |
| shrinking | 0.30 s, bounce 0 | 0.26 s, bounce 0 |
| steady (same strip refitting a reading, e.g. 9% -> 10%) | 0.50 s, bounce 0 | - |
- Max overshoot 12 pt (bounce reduced to respect it); settled when within 0.5 pt.
- Each side is its own spring; the centre slides with the width spring.
- Content transitions (`NotchContentTransition`): `reveal` (opening): content opacity keyframes [0 hold until 35 % of 0.45 s, then ease-out to 1], plus a blur (radius 12 -> 6 -> 0) and scale 0.86 -> 1 from the top centre (timing curve 0.22,1,0.36,1); `dismiss` (collapse): opacity 0 over 0.40 s (hold until 65 %), blur 6, no scale; `depart` (a notice/song leaving): content fades out over 0.16 s ease-in then the next content fades in over 0.14 s after a 0.06 s delay (departure complete at 0.2 s); `replace`: cross-fade 0.18 s ease-in-out. Shapes that "snap" (tiny sizes) use 0.2 s with keyTimes [0, .25, 1] and no blur.
- Reduce Motion (Windows: `SystemParameters.ClientAreaAnimation == false`): no springs, no hover pulse, 0 duration crossfades, no scale/blur.
- Button style: hover wash white 9 % (rounded rect, cornerRadius per control), press opacity 0.8 and scale 0.965, hover scale 1.022 (spring response 0.28/0.32, damping 0.7/0.75); disabled opacity 0.4.
- Icon replace: 0.24 s smooth symbol cross-fade. Gallery row step: spring 0.3 s, bounce 0. Dots: 0.15 s ease-out. Search field unfold: 0.15 s ease-out. Level notice meter: 0.14 s ease-out; level key easing: 0.22 s cubic-out. Header level reading swapping with the levels: 0.25 s smooth.
- Scroll edge fade: 16 pt gradient mask at an edge only when content continues beyond it (hidden at rest at the start; last row at the end stays clear); fade in 0.18 s ease-out.
- Decorative loops (music bars, agent pulse) step at most 30 fps (not a free-running 60/120 fps animation).

---------------------------------------------------------------------------------------------------------------------

### 6. Surface appearance

- Fill: pure black. Optional (a) *Translucent background* `notchTranslucentBackground`: while open, system blur (hudWindow material, behind-window) masked to the island contour with a black ramp over the top strip (ramp 36 pt below the strip) so it meets the camera/bar black; disabled when Reduce Transparency; (b) *Liquid Glass* `notchLiquidGlassEnabled` (macOS 26 only). Open states only; resting strips, wings, compact activities and small notices stay black.
- Outline `notchOutlineEnabled`: stroke 2 pt white at 65 % (45-85 % with contrast settings) drawn inside the edge; capsule outlines go all round; hanging shape leaves the top edge open; when off a 0.5 pt hairline at 0-45 % (high contrast).
- Foreground white; secondary text white 55-65 %; pills and chips white 12-14 % fills; hover wash 9 %; control surfaces (`NotchControlSurface`) fill white 6.5 % (selected 12 %), 0.5 pt stroke white 6.5 % on glass.
- Drop shadow: none (window hasShadow = false).
- Excluded from screenshots/recordings when `notchShowInCaptures` is false.

---------------------------------------------------------------------------------------------------------------------

### 7. Behaviour details

#### 7.1 Windows/host (NotchWindowHost, NotchPanel)
- Borderless, non-activating, transparent, no shadow, never released, `hidesOnDeactivate = false`, dark appearance.
- Window level `statusBar + 1` (above status items, below native menus/dialogs); full-screen-auxiliary / all-Spaces / stationary / ignores-cycle collection behaviour; lives in its own Space so desktop-switching swipes do not drag it.
- Can become key only while open with `acceptsKeyFocus` (so typing in Explore search/Scratchpad works), never main; keeps "active" appearance without keyboard focus.
- The native window is sized to the *envelope* of the transition (including overshoot) and shrinks after the animation settles; margins are whole pixels; the black silhouette is animated by the GPU, not by relayout.
- Hit test: only the visible surface, floating buttons and drop zones receive events; the transparent reservation passes clicks through (ignoresMouseEvents while concealed).
- Mission Control concealment: window hidden/click-through while Mission Control is on screen, restored (re-reads state) afterwards.
- File drop target: accepts file URLs when Files section is on and the shelf accepts the pasteboard; `dragPlaceholder` shows while a drag is in progress (if `notchDragReveal`), pointer position over the island highlights it.
- Mirrored copies: with Display = "All displays" each other display shows a click-through-to-open closed copy (same strip, own geometry).

#### 7.2 Display selection (`NotchSupport.screenIndex`)
`notchDisplay`: `automatic` (built-in notched display if any -> any notched display -> main), `builtIn` (internal panel; if none: main; hidden when a lid is closed on a laptop), `main`, `pointer` (island moves to the display under the pointer after the pointer stays 0.2 s; stays on its display until it can follow), `all` (island follows the pointer AND every other display shows a closed copy). Unknown stored values act as `automatic`. If the chosen display disappears (lid closed) the island withdraws: tears down the presentation, cancels capture controls, suspends the timer and returns media keys to the system.
`reachesDisplayWithoutNotch(preference)` drives the "Displays without a notch" card hint.

#### 7.3 Full screen
- `notchHideInFullscreen` (default false): when a full-screen Space or a borderless "app full screen" window (frontmost app, layer > 0, alpha > 0, bounds cover the display within 1 pt, not the island's own process; polled once per second while the option is on) covers the island's display, content is hidden: closed island becomes a bare cutout (notch) or disappears (non-notched), notices/hover/picker are cancelled, the island collapses, media-key routing is handed back.
- `notchHoverInFullscreen` (default false; only meaningful with the above): hover still opens the island in full screen; the pointer is then tracked by polling its position over the island's place (there is no window to enter).
- Hidden-until-hover (`notchHideUntilHover`, requires hover on): the closed island is invisible until the pointer reaches its place; with `notchHiddenShowsActivity` it also appears while an activity runs or a notice shows (non-notched displays only when the menu bar leaves room - always true on Windows).

#### 7.4 Keyboard (island window has key focus)
| Keys (macOS) | Action | Windows |
|---|---|---|
| Cmd+K | toggle Explore | Ctrl+K |
| Esc (in Explore) | leave Explore | Esc |
| Esc | step back one level (see 3.3) | Esc |
| Cmd+, | open the island page in Settings | Ctrl+, |
| Opt+Cmd+<module key> | open that section | Ctrl+Alt+<key> (only while the island has focus; note AltGr layouts) |
| Ctrl+Tab / Ctrl+Shift+Tab | next / previous section | same |
| Tab / Shift+Tab (Explore) | next / previous tile | same |
| Arrow keys, Return (Explore) | move highlight (grid by rows; left/right edit the search text while a query exists), open highlighted | same |
| Cmd+1..9 (Clipboard page) | paste the entry at that visible place (also keypad digits) | Ctrl+1..9 |
| Scratchpad: Cmd+T new pad, Cmd+W close pad (with one pad left: close the island), Cmd+F/G/Shift+G find | Ctrl+... | same with Ctrl |
Esc is ignored by the island while an IME is composing, while a mixer percent field is being typed, and while a find bar is open.
Global shortcut `notchShortcut` toggles the island (see 11.4).

#### 7.5 Gestures (`NotchGestureSupport`) - toggle `notchGesturesEnabled` (default true)
- Scroll *down* (wheel toward the user / two fingers up on natural scrolling) over the closed island or its peek: **open**. Scroll *up* over the open island's **top row** (or the strip above a row-less page): **close**.
- Movement normalised as `delta * (naturalScrolling ? 1 : -1) * (precise ? 1 : 24)`; direction is decided after accumulating >= 4 pt of travel with a 1.5 : 1 dominance; **vertical threshold 16 pt**; horizontal threshold **40 pt** (fingers/touchpad only, precise): left = next track, right = previous track. One decision per physical scroll sequence (phase begin..end); events with no phase expire a sequence after 0.35 s of silence.
- Momentum after lifting can *finish* an open/close that started (within 0.35 s, mostly vertical) but never a track change.
- Gestures are ignored over controls (buttons, text), over scrollable lists (vertical) unless in the header or over the Music surface without a scroll view, while a menu is tracking, while capture controls are up, during a held file drag, or with Cmd/Ctrl/Opt/Shift held.
- Horizontal track-change is only offered when the Music section is available AND the compact music strip is showing or the expanded Music page is on screen.
- Explore: the wheel steps whole rows (`NotchSectionScroll`): trackpad first step after 24 pt, then every 94 pt (86 tile + 8), a wheel notch = 1 row, momentum ignored, reversing restarts with the short step.
- Level sliders: scroll over a slider moves it (wheel notch 5 %, trackpad 0.5 % per point, up/right raise it); a gesture belongs to the slider only if it begins on it; the momentum after a flick on the slider is swallowed.
- Haptic feedback `notchHapticFeedback` (default true) on open, section switch, peek, Explore row step, timer minute step. (Windows: no haptics on most hardware; see mapping.)

#### 7.6 Context menu (secondary click on the open island)
Items (when sections exist): "Explore" (or "Back" while Explore is open) with grid symbol; "Keep open" / "Allow automatic closing"; "Settings…" (gear); "Update" (only when an update is offered); divider; "Collapse" (chevron up). VoiceOver/UIA custom actions mirror these.

#### 7.7 Notices - event table
`NotchEvent` (routing needs the island on, the event's toggle on and its feature available). Priority decides replacement: a new notice replaces the shown one only if priority >= current (held/expanded notices: only same event or higher priority). Duration = seconds on screen (timer restarts when replaced).
| Event | Toggle key (default) | Priority | Duration (s) | Click opens | Requires |
|---|---|---|---|---|---|
| volume | notchVolume (true) | 3 | 1.6 | Controls | Mixer feature |
| brightness | notchBrightness (true) | 3 | 1.6 | Controls | Brightness feature + `brightnessControlEnabled` |
| keyboardLight | notchKeyboardLight (true) | 3 | 1.6 | Controls | Brightness feature (+ keyboard backlight present) |
| microphone | notchMicrophone (true) | 3 | 1.6 | Volume mixer | Mic Mute feature |
| inputSource | notchInputSource (true) | 3 | 1.6 | Controls | none (always available) |
| capture | notchCapture (true) | 2 | 12 | Controls | Screenshot feature + Captures section |
| timer | notchTimerEnabled (true) | 2 | 6 | Timer | Timer on |
| watch | notchWatchEnabled (true) | 2 | 6 | Watch | Watch on |
| battery | notchBattery (true) | 1 | 4 | Controls | Power monitor |
| systemNotification | notchNotificationsEnabled (true) | 1 | 3 | Notifications (or the source app) | Notifications on |
| accessory | notchAccessoriesEnabled (true) | 1 | 4 | System | Accessory + Power monitor |
| agents | notchAgentsEnabled (false) | 1 | 5 | AI Agents | Agents section on |
| clipboard | notchClipboard (true) | 0 | 2.5 | Clipboard | Clipboard section + clipboard history setting |
| download | notchDownloadsEnabled (true) | 0 | 6 | Downloads | Downloads section on |
| track | notchTrackChange (true) | 0 | 3 | Now Playing | Music section |
Notice composition: level notices = symbol + meter (capsule: 56x4) + percentage, wings sized for "100%"; text notices = mark + title (left wing) and detail (right wing), text 11 pt mono-digit medium; track notices wait 0.5 s after a track change so skipping songs shows one notice; the song strip holds the old song meanwhile. Microphone notice only draws on the closed island (otherwise the floating HUD keeps the job).
Notices are cancelled when hidden-until-hover without activity, when entering full-screen hide, when the island opens (mirrored banners).

#### 7.8 Island alert (confirmation dialogs)
`NotchIslandAlert.run(alert, above: island)`: confirmation questions (e.g. clear captures) open as a *separate modal window* one level above the island (not a sheet attached to the borderless surface), re-raised after each activation, and the island regains key afterwards if it is open.

#### 7.9 Update control (island top row, before pin mark)
- Update available: capsule `borderedProminent` small button, glyph "arrow.down.circle.fill" + label "Update" (icon-only when compact/besideCamera), tint **blue** for stable, **orange** for pre-release; tooltip/AX label = "Update available, Version <x>".
- Downloading: spinner + `NN%` (10 pt medium mono digits; text hidden when compact), tooltip "Downloading update…". Installing: spinner + "Installing and restarting…" (max 110 wide, truncated).
- The top row shows, in this order while they fit their side: page controls, trailing sections button, update control, pin mark; the update shrinks to compact, then the pin disappears, before page controls move.
- Pin mark: "pin.fill" icon button with title "Allow automatic closing" (selected) shown only while pinned.

#### 7.10 Top row ("band")
36 pt tall (or camera height); two sides around the camera gap (each side = `(contentWidth - cameraGap)/2 - 8`), or one row across when there is no room: leading controls at the start, trailing at the end. Leading: Back button (Explore: chevron.left "Back" + the search field; detail: back button + detail title; else the 28 pt Explore button when no floating Explore button exists), then the page's own leading items. A key-driven level notice (volume, brightness, keyboard light) *covers* one side for a moment with the readout (meter + %), unless the home page's own level already moves. 28 pt icon buttons use radius 9, white 55 % glyphs (selected: white, 12 % fill).

#### 7.11 Explore (sections gallery) - `NotchSectionsView`
- Tiles: 92 x 86, radius 16 continuous; icon 22 pt medium (music pink, calendar red, timer orange, else white 85 %), title 11 pt medium max 2 lines (27 pt high, min scale 0.85), caption `⌥⌘ + KEY` 10 pt medium rounded, white 60 %. Fill white 4.5 % (highlighted 12 %), 1 pt border white 4 % (highlighted 55 %, high contrast 40 %). Current section: checkmark.circle.fill 10 pt white 70 % top-trailing, padding 7. Tooltip "<Title>  ⌥⌘X".
- Search: magnifier button in the top row that unfolds on hover/typing into a 12 pt text field (max 150 wide, or the full side beside a camera), placeholder "Find a section" (falls back to "Search", then none if it would be cut), clear button, 28 pt high, radius 9, fill white 7 %. Keyboard goes straight to it when Explore opens. Filtering is on normalised title + id (+ "Music" alias for Now Playing); all terms must match.
- Empty search: empty view with magnifier and "No results".
- Row paging: rows step whole; one 6 pt dot (12 pt hit area) per resting position at the right (white 90 % current, 28 % others, 50 % high contrast); click a dot to rest there. Accessibility hint: "Use the arrow keys, then press Return". Rows not visible take no clicks.
- Opening Explore highlights the current section and scrolls the minimum rows to reveal it.

#### 7.12 Empty island
When no section is available: `NotchEmptyView` (glyph 26 pt light in a 56 pt rounded square, message 12 pt, a "Settings…" prominent pill) with the text "Choose what appears in Dynamic Island settings." Pill button: 12 pt semibold, height 28, white 14 % fill when prominent. Layout stacks vertically (max 250 wide text) or side by side (glyph + text 260 wide) if the island is too short.

---------------------------------------------------------------------------------------------------------------------

### 8. Visible-chrome strings (English, verbatim)

From `Core/NotchStrings.swift` (enUS):
- title "Dynamic Island"; combineActivities "Combine"; enable "Enable Dynamic Island"
- description "Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels."
- open "Open Dynamic Island"; collapse "Collapse"; pin "Keep open"; unpin "Allow automatic closing"; panel "Open app panel"
- dropHint "Drop files here"; empty "Choose what appears in Dynamic Island settings."
- volume "Volume"; inputSource "Input source"; brightness "Brightness"; battery "Battery"; onBattery "On battery"; charging "Charging"; charged "Fully charged"; lowBattery "Low battery"
- controls "Controls"; system "System"; files "Files"; tools "Tools"; music "Music"; captures "Captures"; newTrack "New track"
- disabled "Enable the corresponding feature to use it here."
- switchSection "Switch section"; sectionsTitle "Explore"; searchSections "Find a section"; sectionKeyboardHint "Use the arrow keys, then press Return"; quickAccessLeft "Left"; quickAccessRight "Right"; customizeTools "Customize tools"
- playbackPosition "Playback position"; musicHint "Your music and playback controls appear here."; activity "Activity"
- Titles used in the island: Controls, Volume mixer ("mixerSection"), Now Playing ("mediaNowPlaying"), Clipboard, Recent captures, Files, System, Tools, Calendar, Notifications, Timer, Camera mirror, Downloads, Scratchpad, AI Agents, Watch; "Nothing playing" (empty player).
- Other chrome: "Settings…" (menuSettings), "Back" (obBack), "Update" (updateBannerAction), "Update available" (updateBannerTitle), "Version" (versionPrefix), "Downloading update…", "Installing and restarting…", "Clear" , "Search", "No results", "Move up", "Move down", "Mute microphone", "Unmute microphone", "Mic Mute on", "Mic Mute off", "Keep awake", "Speed test", "Open app panel", "Screen recording", "Screenshot", "Command Bar", "Keyboard light".
- Detail title for the app panel: **Trayloom** (macOS source shows the product name).
- Tooltip of the navigation button: "Switch section  ⌘K" (Win: "Switch section  Ctrl+K").

Windows-adapted copies
- menuBarAccessHint "Allow Accessibility so Dynamic Island can appear on this display without covering the menus." -> **not shown on Windows**.

---------------------------------------------------------------------------------------------------------------------

### 9. Settings page "Dynamic Island" (`UI/Settings/NotchSettings.swift`)

#### 9.1 Page frame
- Padding 22 left/right, 22 top; vertical spacing 20. Scrollable content (`pageScroll`, bottom padding 22) for Layout, Activity, Behavior; the Content tab is a full-height 3-column editor; Companion hosts its own page.
- Header row: Title (title2 bold) "Dynamic Island" + description (callout, secondary) on the left; on the right a switch (**Enable Dynamic Island**, `notchEnabled`, default **false**) — disabled when the feature is not installed/available.
- Under the header (macOS only): if enabled && not notched && no Accessibility permission && not (hover && hideUntilHover && !hiddenShowsActivity): hint text + permission row. **Not on Windows.**
- Tab row (`NotchSettingsTabRow`): segmented picker (falls back to a menu picker when too narrow) with tabs **Layout | Content | Activity | Behavior** (+ the companion's title as 5th tab when the Companion feature is installed; if it is uninstalled while its tab shows, go back to Layout), then a bordered square button with glyph arrow.up.forward.app, tooltip/label **"Open Dynamic Island"**, disabled unless the island is enabled (opens the island).
- Settings navigation requests can jump to the Content tab with a section preselected (from the island's per-section buttons) and to the Companion tab.
- Changing any setting triggers `NotchService.syncWithPreferences()` (plus lyrics stop, queue sync, brightness sync); `notchBrowserCovers` also resyncs browser covers; quick-access layout changes sync too.

#### 9.2 Tab "Layout"
1. **Island layout editor** (full width): canvas 400 high (`640*0.5 + 20 + 58 + 2`), radius 22, quaternary 35 % fill; the island home page drawn at its real size scaled by 0.5 (or `(canvasWidth - 112)/islandWidth`, min 0.2), centred, 20 from the top; real black silhouette (outline preview if enabled, 2 pt white 65 %). Floating buttons are 30 pt black circles (white glyph 12 pt, 1.5 pt border: accent when being edited else white 20 %, shadow 3/y2 12 %; dimmed 40 % when the action is hidden in the island). "+" add buttons are 30 pt dashed accent circles (shown on a side with room). Slot positions: sides at `island.minY + 30 + index*40`, 18 pt away from the edge; bottom slots centred, 24 pt below, spacing 40.
   - Drop zones appear while dragging: left (x = island.minX-48, w 62, full island height), right (x = island.maxX-14, w 62), bottom (full width, h 58); radius 16, accent fill 4 % (15 % under the pointer), dashed accent 30 % border.
   - Drag a bubble to another zone/slot to move/reorder (min drag 5). Click a bubble to edit.
   - Corner resize grip (24 pt circle, glyph arrow.up.left.and.arrow.down.right 10 pt, accent) at the island's bottom-right (-3 px, never above `minY+139`): dragging switches Size to Custom and sets width/height (clamped; min drag 3).
   - Clicking the island preview opens the Content tab (tooltip "Content").
   - Hint under the canvas (callout, secondary): **"Click + to add a button. Drag buttons around the island. Click a button to edit it."** (`layoutHint`)
   - Add popover (340 wide, padding 16): title "Add button", search field "Find an action", scroll list 200 high with two groups — "Open a section" (Explore, Settings…, then every module) and "Quick actions" (Keep open + every non-level control item) — as a 2-column grid of buttons (min height 38, radius 9, fill secondary 7 %, selected accent 14 %); unavailable actions disabled; "No results" when none match.
   - Edit popover (340 wide, padding 16): "Edit button" headline; text field "Name" with the action's title as prompt (stored as `label`, max 40); segmented "Position" = **Left | Right | Bottom** (a side without room is disabled); **Move up** / **Move down** small buttons (swap with neighbour on the same side); the action chooser (to change the action); destructive **"Remove button"**.
   - Animation of layout changes: smooth 0.2 s (none with Reduce Motion).
2. Card **Size** (`SettingsCard(title: "Size")`):
   - Three tiles (68 high, radius 12): **Compact** (rectangle.compress.vertical), **Spacious** (rectangle.expand.vertical), **Custom** (arrow.up.left.and.arrow.down.right). Default Spacious.
   - If Custom: grid with sliders **Width** (360-600, step 10, default 440) and **Maximum height** (260-640, step 10, default 480), readout 38 wide, then caption "Controls stay compact. Longer lists use up to this height."
   - Switch row (drop.halffull) **Translucent background** + caption "When open, Dynamic Island blurs what is behind it with the system's own material." If Liquid Glass is on: shows ON and disabled with caption "Liquid Glass is on, so Dynamic Island uses it instead." (Windows: translucent = Acrylic/Mica backdrop; no Liquid Glass row.)
3. Card **Displays without a notch** (always shown on Windows): two tiles (Capsule / Notch) with a drawn sample (menu-bar slice 400x48 at 0.5 scale, 14 radius, island silhouette inside) — **Capsule**, **Notch**. If the island is enabled and the chosen Display never reaches such a display: caption "With the Display chosen on the Behavior tab, Dynamic Island doesn't appear on these displays." + button **Show on all displays**. (On Windows this can never be unreached except builtIn with no internal panel; keep the logic.)
4. Card **Capsule fit** (only when Capsule is selected): sliders **Width** (-40..40, step 2), **Height** (-4..4, step 1), **Distance from top** (0..20, step 1), value shown signed with 0-1 decimals; caption "Resizes the capsule and lowers it from the top of the screen on displays without a notch."
5. Card (no title): switch **Show outline** (capsule glyph), `notchOutlineEnabled`, default off.
6. Card **Notch fit** (only if a display has a physical notch - **omit on Windows**): switch **Hide gap below notch** + "Extends Dynamic Island to the bottom of the menu bar to hide the thin strip below the notch." ; sliders **Width** (-10..10 step 1) **Height** (-6..6 step 0.5); caption "If an edge of the notch shows around Dynamic Island, adjust these values until it disappears."

#### 9.3 Tab "Content"
Layout: if page width >= 940: three columns [list 216] [detail] [preview <= 560]; else two columns [list 196] [detail with the preview above its options, max height 280].
- Left list container: radius 16, padding 10, quaternary 35 %; headline **"Sections"** + caption **"Drag to reorder. Uncheck a section to hide it from the island."**; rows (min height 32, radius 8, hover primary 5 %, selected accent 16 %): checkbox (shows the section, disabled/unchecked when its feature is off), 22 pt coloured tile, title 12.5 pt (semibold selected, up to 2 lines); tooltip = the section's one-line summary; drag handle = whole row (45 % opacity while dragging); accessibility actions Move up / Move down. List scrolls to keep the selected row in view (0.2 s smooth).
- Detail column: Section header (38 pt tile, title3 semibold, summary callout secondary; when the section is unavailable an orange reason and a small **"Open Features"** button goes to the Features page for that feature). Then a card of the section's options (below), or "This section has no options." (callout secondary) for Volume mixer, System, Tools.
- Preview column: caption pill bottom-left: eye "Preview" or eye.slash "Hidden from the island"; stage = slice of menu bar (rect `menuBarHeight*scale` primary 6 %) with the real page of the selected section scaled to fit the tallest island (scale = min(1, (w-32)/largestW, (h-28)/largestH), min 0.1), 40 % opacity when hidden, radius 16, quaternary 35 %. Camera shows a still ("The camera turns on only in the island."), Scratchpad a still, Watch the explanation, AI Agents shows "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs." while off.
- Per-section one-line summaries (`editor.summary`):
  - Controls: "Playback, volume, brightness and your shortcuts."
  - Volume mixer: "The volume of each app, and where sound plays."
  - Now Playing: "The song playing now, with its controls."
  - Clipboard: "What you copied recently, ready to paste again."
  - Recent captures: "Your latest screenshots and recordings."
  - Files: "A shelf for the files you drop on the island."
  - System: "CPU, memory, disk, network and battery at a glance."
  - Tools: "Tools and utilities, one click away."
  - Calendar: "Today's events and the month ahead."
  - Notifications: "Your Mac's notifications, kept in the island." (Win: "Your PC's notifications, kept in the island.")
  - Timer: "Timer, Pomodoro and stopwatch."
  - Camera mirror: "A mirror to check yourself before a call."
  - Downloads: "Downloads in progress and just finished."
  - Scratchpad: "Quick notes that save by themselves."
  - AI Agents: "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs."
  - Watch: "Any part of a window, read live, with an alert when it changes."
- Reasons/links strings: `Enable "%@" in Features.` ; `Enable "%@" in its settings.` ; `Show "%@" on the Content tab.` ; disabled generic = "Enable the corresponding feature to use it here."
- Section show/hide model: checkbox writes `notchHiddenModules` (comma list of rawValues, sorted) and, for timer/camera/calendar/notifications/agents/watch, also their dedicated enable keys (`notchTimerEnabled`, `notchCameraEnabled`, `notchCalendarEnabled`, `notchNotificationsEnabled`, `notchAgentsEnabled`, `notchWatchEnabled`). Order writes `notchModuleOrder`.

Per-section option cards (moduleOptions)
- **Controls**: four primary toggle cards (Now Playing, Volume, Brightness, Keyboard light) in a row; heading **"Controls and shortcuts"**; a reorderable adaptive grid (min 116) of toggle cards for the remaining items (all non-level, non-music items: Volume mixer, Keep awake, Timer, Calendar, Mute microphone, Screenshot, Screen recording, Speed test, Open app panel, Command Bar, Scratchpad); a card shows the item's glyph (20 pt), title (11 pt medium, 2 lines, 28 high) and a badge button (check = shown, plus = hidden, minus.circle = unavailable, arrow.up.right.circle = unavailable with a fix); an unavailable item shows an explanation (3 lines, caption2) that is also its tooltip and, if fixable, is accent coloured and opens Features / the setting / the Content tab. Divider, then **Show "Keep awake" in the closed island** switch (`notchKeepAwakeActivity`, default off) with hint "A running session appears beside the camera with the time it has left, or ∞ when it has no end." (feature-bound row).
- **Now Playing**: 
  - "Show music while playing" (music.note) `notchShowPlayingMusic` true
  - "Automatically include videos and other apps" (play.rectangle) `notchIncludeOtherPlayers` true
  - "Show volume in the player" (speaker.wave.2, Mixer feature row) `notchMusicShowVolume` false
  - Picker "Open when nothing is playing" (arrow.up.forward.app) `notchPreferredPlayer` ("" = automatic) + hint "The cover and the playback buttons open this app. Automatic opens the music app that played last, then Spotify or Apple Music." (Win: "...then Spotify."); first option "Automatic", then installed players.
  - "Show lyrics" (text.quote, feature row) `notchLyricsEnabled` true; when on: indented "Find lyrics online" (globe) `notchLyricsOnline` false + hint "While lyrics are open, send the song title, artist, album and duration to lrclib.net. Imported lyrics stay on this Mac. Song titles, artist names and albums may also be checked with musicbrainz.org." (Win: "...stay on this PC...")
  - "Find covers for Firefox and Zen" (photo) `notchBrowserCovers` false + hint "When Firefox, Zen or another Firefox-based browser plays without a sharp cover, read its saved session on this Mac to find the song's YouTube tab and load its thumbnail from i.ytimg.com. If no tab shows the song but a YouTube Music tab is open, send the song title, artist and region to itunes.apple.com and load the album art from mzstatic.com."
  - "Show upcoming songs" (list.bullet) `notchQueueEnabled` true + "See the actual upcoming songs shared by your player."
  - "Bars follow the music" (waveform) `notchLiveEqualizer` false + hint "Listens to the player's own audio output to move the bars. macOS asks once to allow system audio recording. Nothing is stored or sent. Without it, the bars keep their usual motion." (Win: WASAPI loopback peak meter; drop the macOS sentence) / unavailable text "Needs macOS 14.4 or later." (not needed on Windows).
- **Notifications**: switch "Hide the system banner" (bell.slash) `notchDismissNativeNotifications` false + "Hide the original while the Dynamic Island shows it."; accessibility permission row when Notifications on (macOS only).
- **Downloads**: downloads settings controls (folder choice, etc.; specified in the Downloads spec); toggle style = trailing switch.
- **Calendar**: callout "Read your calendars to show upcoming appointments. Events stay on this Mac." then either green check "Granted" or buttons "Allow Calendar Access" + "Open System Settings" (Win: Windows has no calendar permission model; connect account / ICS sources — see Calendar spec); Divider; "Event countdown" (calendar.badge.clock) `notchCalendarCountdown` false + hint "Show every timed event in the closed island before it starts. To count down to only some, turn this off and right-click them on the island's Calendar page. Titles may appear in screen captures."; menu row "Time before event" (timer) `notchCalendarCountdownLead` (minutes; presets in `NotchCalendarSupport.countdownLeadPresets`, current stored value always selectable; labels as short durations); "Time left in current event" (hourglass) `notchCalendarTimeLeft` false + "Show a timed event in progress in the closed island during the hour before it ends. Its title may appear in screen captures."; "Week numbers" (number) `notchCalendarWeekNumbers` false; when permitted: **Calendars shown** group (see 9.7).
- **Timer**: "Hide timer countdown" (eye.slash) `notchHideTimerCountdown` false; "Play sound when time is up" (speaker.wave.2) `notchTimerSoundEnabled` true; both disabled when the Timer feature is off.
- **Camera mirror**: callout "Open a live mirror here. The camera stops when you leave this view."; permission: green "Granted" or buttons "Grant access" / "Open System Settings…" (Win: "Open Settings > Privacy > Camera").
- **Files**: destination row "Files" with menu **Dynamic Island | Separate window** (`notchShelf`, true = island); if island: "Show a drop target while dragging" (hand.draw) `notchDragReveal` true; if Media tools on: caption "Drag images or one video onto the island, then drop on Optimize media to choose how to save a copy."
- **Clipboard**: destination row "Clipboard" (`notchClipboardWindow`, default true); switch "Notify when something is copied" (doc.on.clipboard) `notchClipboard` true + caption "Copied content stays private until you open the clipboard. These options follow the original feature's settings."; when island clipboard on: choice row "Entry size" **Compact | Comfortable** (`notchClipboardCardSize` = `compact` | `comfortable`, default comfortable; unknown -> comfortable) + hint "Compact entries show one or two lines and open up when the pointer rests on them."
- **Recent captures**: "Show screenshot previews here" (camera.viewfinder, feature row) `notchCapture` true; when on: the screenshot "default action" picker (`screenshotDefaultAction`, default "").
- **Scratchpad**: destination row "Scratchpad" (`notchScratchpad` true = island).
- **AI Agents**: `NotchAgentsSettingsControls` (separate spec).
- **Watch**: `NotchWatchSettingsControls` (separate spec).

#### 9.4 Tab "Activity"
1. Card **"At rest"**: tiles for "While inactive" content (mini island sample: icon + black 20x12 + reading, capsule black, 10 pad): **Nothing** (shows the companion if it rests there) | **Battery** (only on a machine with an internal battery; icon battery.75percent, sample "76%") | **Music** (sample waveform) | **AI limits** (sparkles, "62%"; offered once the AI Agents section is on, or if already saved). Key `notchIdleContent` = `none|battery|music|agents`, default **music**. A saved choice that cannot show now stays selectable, dimmed 45 %, with a link-button below it saying why (e.g. `Show "Now Playing" on the Content tab.`) and a VoiceOver value "Hidden from the island". The one the island actually rests with is outlined/marked.
   - When Battery is the resting choice: switch **"Turn red when low"** (battery.25percent) `notchLowBatteryTint` false + caption "Shows the charge in red at or below this level while on battery."; if on: slider **"Red level"** (1-100 step 1, but stored 1-99, default **10**); switch **"Turn amber first"** (battery.50percent) `notchLowBatteryEarly` false + "Shows the charge in amber at or below a higher level, as an early warning."; if on: slider **"Amber level"** (stored 2-100, default **20**, always >= red + 1; raising red pushes amber up); switch **"Also in the menu bar"** (menubar.rectangle) `notchLowBatteryMenuBar` true (Win: "Also in the tray"). Both level sliders share the 1-100 scale. Warning rule: only while on battery; percent <= red -> red; else if early && percent <= amber -> amber (colour `.orange`), else white 90 %.
   - Switch **"Show over the menus"** (menubar.rectangle) `notchCoversMenus` true + "Keeps the timer, music and other compact activity on screen when the menu bar has no room, covering the menus beside the camera." (**omit on Windows**).
2. Card **"Indicators"** (adaptive grid of toggle cards, min 116, spacing 10; same card component as 9.3; unavailable cards explain why and link to the fix): **Volume** (speaker.wave.2, `notchVolume`), **Brightness** (sun.max, `notchBrightness`; needs Brightness feature + its setting; reason `Enable "…" in its settings.`), **Keyboard light** (light.max, `notchKeyboardLight`; "Keyboard backlight control is unavailable on this Mac." — Win: "…on this PC."), **Microphone** (mic, `notchMicrophone`), **Battery** (only with internal battery; battery.75percent, `notchBattery`), **Accessory alerts** (headphones, `notchAccessoriesEnabled`; requires Power monitor), **Clipboard** (doc.on.clipboard, `notchClipboard`), **Captures** (camera.viewfinder, `notchCapture`), **New track** (music.note, `notchTrackChange`), **Input source** (keyboard, `notchInputSource`, always available).
   - Under the grid, when accessory alerts are on: caption "Show connected accessories and warn once when their battery falls to 20%."
   - macOS: Accessibility permission row if volume/brightness/keyboard light indicators are on. (**Windows: not needed**; volume/brightness keys are read via the low-level keyboard hook / system volume events.)
3. Card **"Lock Screen"**: switch **"Show on the Lock Screen"** (lock.display) `notchLockScreen` false + hint "Music and the Dynamic Island's activities, like a timer, AI agents, downloads and your next event, appear above the password field. Anyone who can see your screen can read them."; switch **"Lock and unlock sounds"** (speaker.wave.2) `notchLockSounds` false + "Play the macOS padlock sound when your Mac locks and unlocks." (**Windows: lock screen is a different secure desktop; see mapping - cannot be drawn there. Hide this card or keep it with a note.**)

#### 9.5 Tab "Behavior"
1. Card **"Opening"**: 2-column tiles (68 high):
   - **Click to open** (cursorarrow) selected when `notchOpenOnHover` is false
   - **Preview on hover** (rectangle.topthird.inset.filled): hover true, `notchHoverExpands` false, `notchHideUntilHover` false
   - **Expand on hover** (arrow.up.left.and.arrow.down.right): hover true, `notchHoverExpands` true, hideUntilHover false
   - **Hidden until hover** (eye.slash): sets hover true, hoverExpands true, `notchHideUntilHover` true
   Default selection: **Click to open** (`notchOpenOnHover` default false, `notchHoverExpands` default true).
   - If hidden-until-hover: switch **"Show activity and indicators"** (sparkles) `notchHiddenShowsActivity` false; caption (when on) "Comes into view while an activity runs or an indicator shows, then hides again."
   - If hover is on (or hide-in-full-screen with hover-in-full-screen): sliders **"Activation time"** (`notchHoverDelay`, 0.10-1.00, step 0.05, default 0.25, readout `%.2f s`) hint "Keep the pointer over the island for this long to open it."; and when expanding on hover: **"Closing time"** (`notchCloseDelay`, 0.10-2.00, default 0.18) hint "Wait this long after the pointer leaves the island before closing it."
   - Switch **"Global shortcut"** (keyboard) `notchShortcutEnabled` false; when on: shortcut recorder row (`notchShortcut`, default `GlobalShortcut.notchDefault`) and, if registration failed, orange text "macOS rejected this shortcut. Choose another one." (Win: "Windows rejected this shortcut. Choose another one.")
   - Feature row **"Dynamic Island Gestures"** (hand.draw, feature `notchGestures`) `notchGesturesEnabled` true; caption when on (verbatim): "Scroll down to open. Scroll up over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling." (feature description: "Open and close the Dynamic Island with scrolling, and swipe to change tracks.")
   - Switch **"Haptic feedback"** (waveform.path) `notchHapticFeedback` true (hint string exists: "Gentle taps when opening, switching sections or adjusting timer minutes, on a compatible trackpad."). **Windows: show only if a precision touchpad with haptics is present; otherwise hide.**
   - Menu row **"When reopening"** (arrow.uturn.backward): options **Last page** (`notchReturnHome` false), **Open app panel**, **Explore**, then every available section; stores `notchReturnHome` (true when a destination is chosen) + `notchHomeModule` (a module id, `appPanel` or `explore`; default `controls`); an unavailable saved section appears disabled.
   - Switch **"Open the visible activity"** (livephoto) `notchOpensActivity` true + "When the closed island shows music, a timer or another activity, it opens on that page instead."
2. Card **"Display"**: switch **"Hide content in full screen"** (arrow.up.left.and.arrow.down.right) `notchHideInFullscreen` false; if on, indented switch **"Hidden until hover in full screen"** (cursorarrow) `notchHoverInFullscreen` false; 2-col tiles: **Automatic** (display.2) | **Built-in display** (laptopcomputer) | **Main display** (display) | **Follow pointer** (cursorarrow.motionlines) | **All displays** (rectangle.on.rectangle). Key `notchDisplay` = `automatic|builtIn|main|pointer|all`, default **automatic**. (Windows labels may read "Main display" = primary monitor; keep English text.)
3. Card **"Where things open"** (destinations, each is a row with menu **Dynamic Island | Separate window**):
   - **Open app panel** (bubble.middle.top) `notchAppPanel` false (true on migrated profiles) + caption "Choose where clicking the Trayloom tray icon opens the app panel, on any display." (adapted from the menu-bar-icon wording of the source)
   - Switch **"Hide the menu bar icon"** (menubar.rectangle) `notchHidesMenuBarIcon` false + "Settings and the app panel open from Dynamic Island. The icon returns when the island is off or hidden in full screen, or when an update is ready or the microphone is muted." (Win: "Hide the tray icon", same body with "tray icon")
   - **Tools** `notchQuickPanel` true (needs Quick Launcher)
   - **Clipboard** `notchClipboardWindow` true (needs Clipboard history)
   - **Files** `notchShelf` true (needs Shelf)
   - **Captures** (camera.viewfinder) `notchCaptureControls` true (capture toolbar in the island)
   - **Scratchpad** `notchScratchpad` true (needs Scratchpad)
   Disabled (greyed) when the underlying feature is off.
4. Card **"Privacy"**: switch **"Show in screenshots and videos"** (camera.viewfinder) `notchShowInCaptures` true (off excludes the window from capture).

#### 9.6 Tab "Companion" (only when the companion feature is installed)
Title = `FeatureStrings.notchMascot(...).title`; embedded page of the Companion (keys `notchMascotEnabled` false, `notchMascotVisits` true, `notchMascotReactions` true, `notchMascotStyle` = minimal, `notchMascotShape` = ball, `notchMascotPalette` = pearl, `notchMascotSide` = left, `notchMascotVisitFrequency` = normal, `notchMascotHidesWhenIdle` true). Specified in its own file.

#### 9.7 Calendar selection (`NotchCalendarSelection`)
Appears at the bottom of the Calendar options when permitted. Divider, subheadline-medium **"Calendars shown"**, then calendars grouped by account: account name (caption, secondary, top padding 4) and one checkbox per calendar (colour dot 8 pt + title, 1 line). Unchecked calendars are stored in `notchCalendarExcluded` ([String] of calendar identifiers) and removed from the Calendar page and the countdown. Reloads on calendar store change. (Windows: group by connected account/ICS source.)

---------------------------------------------------------------------------------------------------------------------

### 10. Settings keys (UserDefaults) - complete table for the island shell

Type: B bool, S string, D double, I int, J JSON data. "Win" = applies on Windows (Y), N/A, or adapted.

| Key | Type | Default | Allowed / notes | Win |
|---|---|---|---|---|
| notchEnabled | B | false | master switch | Y |
| notchSize | S | `spacious` | `compact|spacious|custom` (migrated old configured profiles: previously compact) | Y |
| notchCustomWidth | D | 440 | 360-600 | Y |
| notchCustomHeight | D | 480 | 260-640 | Y |
| notchOutlineEnabled | B | false | | Y |
| notchHideMenuBarGap | B | false | notch only | N/A |
| notchCameraFitWidth | D | 0 | -10..10, whole points | N/A |
| notchCameraFitHeight | D | 0 | -6..6, half points | N/A |
| notchSilhouette | S | `capsule` | `capsule|notch` | Y |
| notchCapsuleFitWidth | D | 0 | -40..40 even | Y |
| notchCapsuleFitHeight | D | 0 | -4..4 whole | Y |
| notchCapsuleFitDrop | D | 0 | 0..20 whole | Y |
| notchTranslucentBackground | B | false | | Y (acrylic) |
| notchLiquidGlassEnabled | B | false | macOS 26 | N/A |
| notchOpenOnHover | B | false | | Y |
| notchHoverExpands | B | true | true = expand fully, false = peek | Y |
| notchHideUntilHover | B | false | needs hover | Y |
| notchHiddenShowsActivity | B | false | | Y |
| notchHoverDelay | D | 0.25 | 0.10-1.00 | Y |
| notchCloseDelay | D | 0.18 | 0.10-2.00 (unset keeps 0.18) | Y |
| notchShortcutEnabled | B | false | | Y |
| notchShortcut | S | `GlobalShortcut.notchDefault` | recorder | Y |
| notchGesturesEnabled | B | true | | Y |
| notchHapticFeedback | B | true | | adapted |
| notchReturnHome | B | false | | Y |
| notchHomeModule | S | `controls` | module id, `appPanel`, `explore` | Y |
| notchOpensActivity | B | true | | Y |
| notchHideInFullscreen | B | false | | Y |
| notchHoverInFullscreen | B | false | | Y |
| notchDisplay | S | `automatic` | `automatic|builtIn|main|pointer|all` | Y |
| notchCoversMenus | B | true | | N/A |
| notchShowInCaptures | B | true | | Y |
| notchHiddenModules | S | "" | CSV of module ids (sorted) | Y |
| notchModuleOrder | S | "" | CSV of module ids | Y |
| notchHiddenControls | S | `keyboardLight,microphone,screenshot,recording,speedTest,panel,commandBar,scratchpad` | CSV | Y |
| notchControlOrder | S | "" | CSV | Y |
| notchQuickAccessLayout | J | empty (-> initial layout) | JSON v1, <= 32 768 B | Y |
| notchIdleContent | S | `music` | `none|battery|music|agents` | Y |
| notchLowBatteryTint | B | false | | Y |
| notchLowBatteryThreshold | I | 10 | 1-99 | Y |
| notchLowBatteryEarly | B | false | | Y |
| notchLowBatteryEarlyThreshold | I | 20 | 2-100, >= red+1 | Y |
| notchLowBatteryMenuBar | B | true | | adapted (tray) |
| notchVolume / notchInputSource / notchMicrophone / notchBrightness / notchBattery / notchClipboard / notchCapture / notchTrackChange | B | true each | per-notice toggles | Y |
| notchKeyboardLight | B | true (false on previously-configured profiles) | | Y |
| notchAccessoriesEnabled | B | true | | Y |
| notchNotificationsEnabled | B | true | | Y |
| notchDismissNativeNotifications | B | false | | adapted |
| notchTimerEnabled | B | true | | Y |
| notchTimerSoundEnabled | B | true | | Y |
| notchHideTimerCountdown | B | false | | Y |
| notchTimerMode / Minutes / Pomodoro* | | `timer`, 15, 25/5/15/4/4 | timer spec | Y |
| notchCameraEnabled | B | true | | Y |
| notchCalendarEnabled | B | true | | Y |
| notchCalendarCountdown | B | false | | Y |
| notchCalendarCountdownLead | I | `NotchCalendarSupport.defaultCountdownLeadMinutes` (calendar spec) | minutes | Y |
| notchCalendarTimeLeft | B | false | | Y |
| notchCalendarWeekNumbers | B | false | | Y |
| notchCalendarExcluded | [S] | [] | | Y |
| notchAgentsEnabled | B | false | | Y |
| notchWatchEnabled | B | true | | Y |
| notchLyricsEnabled | B | true (false on migrated) | | Y |
| notchLyricsOnline | B | false | | Y |
| notchBrowserCovers | B | false | | Y |
| notchLiveEqualizer | B | false | | Y |
| notchQueueEnabled | B | true (false on migrated) | | Y |
| notchShowPlayingMusic | B | true | | Y |
| notchIncludeOtherPlayers | B | true | | Y |
| notchMusicShowVolume | B | false | | Y |
| notchPreferredPlayer | S | "" | bundle id (Win: AUMID) | adapted |
| notchDownloadsEnabled | B | true | | Y |
| notchClipboardWindow | B | true | | Y |
| notchClipboardCardSize | S | `comfortable` | `compact|comfortable` | Y |
| notchShelf | B | true | | Y |
| notchDragReveal | B | true | | Y |
| notchCaptureControls | B | true | | Y |
| notchQuickPanel | B | true | | Y |
| notchAppPanel | B | false | | Y |
| notchHidesMenuBarIcon | B | false | | adapted (tray) |
| notchKeepAwakeActivity | B | false | | Y |
| notchScratchpad | B | true | | Y |
| notchLockScreen / notchLockSounds | B | false | | N/A (see mapping) |
| notchCommandBar / notchCommandBarStyle | B/S | true / `droplet` | Command Bar spec | Y |
| notchHideInCaptures | B | false | legacy | - |
| notchDefaultProfileInitialized, notchInitialExtensionsInstalled, notchAgentsOptInMigrated, notchScratchpadControlHidden, notchKeyboardLightControlHidden | B | markers | local, never backed up | - |
First-run profile: a configuration that has never saved any `notch*` key gets `notchSize = spacious` pinned; a previously configured one keeps the older defaults listed with "migrated" above (open on hover, app panel in island, etc.).

---------------------------------------------------------------------------------------------------------------------

### 11. Closed-surface catalogue ("capsules" and notch strips)

Two renderings exist for every closed state: **hanging strip** (notch silhouette: camera gap in the middle, content in the two wings) and **capsule strip** (one row, ends to ends; `NotchCapsule*` views). On Windows the default is the capsule; the notch renderer is used when `notchSilhouette = notch`.

| Closed state | Capsule view | Content (left -> right) | When |
|---|---|---|---|
| Resting | NotchCapsuleRestingView | idle: battery glyph (12 pt, tinted) + `NN%`; or AI-limits wings; or music cover (round, concentric with the round end) + 3-bar equaliser (bar 2 x 10, tinted with artwork colour, only while playing); companion mascot if on | no activity/notice; content per `notchIdleContent` |
| Notice | NotchCapsuleNoticeView | level: symbol + meter + reading; message: mark (symbol / agent mark / track cover / companion) + title + dimmed detail; banner: app icon 16 + sender + message | event routed (see 7.7) |
| Music | NotchCapsuleMusicStrip | cover in the round end, title (12 semibold, only after the 0.3 s "naming" delay or on hover), bars at the other end; hovering the cover names the song; hovering the bars turns them into play/pause (a paused-by-button song stays until the pointer leaves) | music playing |
| Timer | NotchCapsuleTimerStrip | mark (timer glyph, orange when ending) or companion mark (download arrow+%, agent marks, cover, event dot+countdown), reading (13 semibold mono digits; digits roll) | timer running/paused/finished and countdown not hidden |
| Agents | NotchCapsuleAgentStrip | marks of working agents (12 pt, 10 pt if >1) + reading (elapsed / limit, per `notchAgentsReadout`) | live agent run |
| Downloads | NotchCapsuleDownloadStrip | arrow + file name (12 medium) + 36 pt meter + `%` (spinner 16 until size known) | active download |
| Watch | NotchCapsuleWatchStrip | eye mark + reading (or thumbnail of the area when it has no text; spinner until first reading) | watch active |
| Calendar | NotchCapsuleCalendarStrip | event colour dot (6) + title + countdown + time (starts/ends); in a pair: dot + countdown only; time-left colour uses the "happening now" colour | upcoming/ongoing per settings |
| Keep Awake | NotchCapsuleKeepAwakeStrip | cup glyph + time left (changes each minute) or ∞ | running session and `notchKeepAwakeActivity` |
| Capture controls (collapsed) | NotchCapsuleCaptureStrip | selected tool glyph + chevron.down | screen capture selection folded |
| Fullscreen compact | - | bare cutout | see 7.3 |
Notch (hanging) equivalents live in `NotchTimerStrip, NotchWatchStrip, NotchDownloadStrip, NotchAgentStrip, NotchCalendarStrip, NotchMusicStrip, NotchKeepAwakeStrip, NotchRestingStrip, NotchNoticeView`.
Resting strip in notch form: left wing = battery glyph (12 pt) / music cover (<= 22 pt, radius 5) / AI wing; right wing = `NN%` (9 pt medium) / 3-bar equaliser (2 x 11) / AI wing; wing content inset `min(16, shoulder + 5)`.
Activity picker (hover with 2+ live activities): replaces opening-on-hover; header shows the current strip; below, a grid of chips (12 medium, glyph + title, row 32, radius 8, white 12 %; chosen = white fill + black text, slides with matched geometry), then a "Combine" menu (plus / checkmark glyph, 11 medium) listing pairs; choosing animates 0.26 s smooth; clicking the strip still opens its page.
Artwork on strips: music cover side = `min(26, stripHeight - 10)` (resting strip: min(22, stripHeight - 6)); radius concentric with the strip corner.

---------------------------------------------------------------------------------------------------------------------

### 12. Windows mapping

| Piece | Windows implementation |
|---|---|
| Overlay window | WPF `Window` (AllowsTransparency, WindowStyle=None, ShowInTaskbar=false, Topmost) with extended styles `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`; toggle `WS_EX_NOACTIVATE` off (and `SetForegroundWindow`) only while the island is open with key focus is needed (search, Scratchpad, text fields), back on when collapsed. Prefer a layered/`UpdateLayeredWindow`-style or DWM-composited transparent window; keep the HWND size = envelope of the animation so transparent areas do not intercept clicks (existing `OverlayWindow` already does grow/shrink). Place with `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` using per-monitor DPI math (monitor work/bounds `rcMonitor`, top = 0 or `topGap`). |
| Always on top over taskbar / fullscreen | Use `HWND_TOPMOST` (above the taskbar). It cannot draw over exclusive full-screen apps - therefore "Hide content in full screen" default should be **on for Windows** if the product wants parity of intent (mac default false; keep key default false but see Gap). |
| Z-level vs menus | Windows menus/dialogs are topmost too; ordering is not controllable. Popups owned by the island should set `Owner` so they stack above. |
| Click-away | `WH_MOUSE_LL` hook (existing hook infra `context.Hooks.SubscribeMouse`): any button-down outside island rect (and outside owned popups) -> collapse. Foreground change via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` implements `closesOnActivation`. |
| Hover | `MouseEnter/Leave` on the black shape plus 150 ms cursor polling while expanded (existing); hover delay/close delay from settings; for hidden-until-hover or full-screen-hover the window is not shown/click-through, so poll `GetCursorPos` (30-60 ms) against the island's rect. Cursor over child popups counts as inside (HitTest owned windows). |
| Screen edge click | Not needed (no menu bar steals clicks); the island at y=0 gets them directly. When hidden/click-through, a transparent 1-px-tall hot strip is unnecessary: polling suffices. |
| Gestures | `WH_MOUSE_LL` wheel events: `WM_MOUSEWHEEL` (vertical) and `WM_MOUSEHWHEEL` (horizontal, touchpad). Convert delta to pt: wheel notch (120) = 24 pt; precision touchpad deltas (< 120) accumulate as `delta/120*24`. Natural-scroll inversion: read `HKCU\Control Panel\Mouse` / touchpad "FlipFlopWheel" (or provide the existing "Reverse the scroll direction" toggle). Sequence = events within 350 ms. Thresholds per 7.5. Modifier-held (Ctrl/Alt/Shift/Win) disables. Shift+wheel = horizontal fallback for mice. |
| Display / multi-monitor | `EnumDisplayMonitors`/`GetMonitorInfo`; built-in = `DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL` via `QueryDisplayConfig`; main = primary monitor; pointer = `MonitorFromPoint(GetCursorPos)` re-evaluated after the pointer stays 200 ms; "All displays" = one extra click-to-open mirror window per other monitor; `WM_DISPLAYCHANGE` / `SystemEvents.DisplaySettingsChanged`, `WM_DPICHANGED` re-run placement; lid-closed built-in = monitor disappears -> withdraw. |
| Full-screen detection | Foreground window rect (use `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`) covers the monitor and is not the shell/desktop/taskbar class, plus `SHQueryUserNotificationState` (QUNS_BUSY / RUNNING_D3D_FULL_SCREEN / PRESENTATION_MODE) - the existing `IslandNative.IsFullscreenForeground` implements this. Re-evaluate on foreground events and a 1 s poll while the option is on. |
| Synthetic menu bar | constants: barHeight 24, no AX menu measurement (`NotchMenuBarSpace` is not ported), `compactSideRoom = screen.width`. |
| Translucent background | `SetWindowCompositionAttribute` acrylic/`DWMWA_SYSTEMBACKDROP_TYPE` (Mica/Acrylic) clipped by the island region (`SetWindowRgn` or a rounded clip + backdrop on Win 11 22H2+); black ramp over the top strip as in 6. Fallback: solid black. No Liquid Glass equivalent. |
| Rounded animated shape | Draw the silhouette as a WPF `Path`/`Geometry` (capsule: rounded rect with radius h/2; notch: shoulder + bottom radii from 4.2) animated by spring functions (WPF `SpringAnimation` is not built in: implement `NotchMotion.Spring` as an `IEasingFunction` or drive frames from `CompositionTarget.Rendering`). Springs: see section 5 constants. |
| Blur on content arrival | Optional `BlurEffect` Radius 12->0 and `ScaleTransform` 0.86->1 on the content host (top centre origin); skip when animations are off. |
| Capture exclusion | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` when `notchShowInCaptures` is false (Win10 2004+); fallback `WDA_MONITOR` black rectangle. |
| Global shortcut | `RegisterHotKey` (existing hotkey service); failure sets `shortcutRegistrationFailed`. |
| Media keys / volume notices | System volume via Core Audio (`IAudioEndpointVolume` callbacks) for notices; brightness via WMI/DDC; keyboard-light and input-source notices via keyboard layout change hook (`WM_INPUTLANGCHANGE`); media transport via GlobalSystemMediaTransportControls. |
| Haptics | Precision-touchpad haptics have no public API for apps; omit (hide the setting) unless a feasible alternative; optionally play no sound. |
| Lock screen | The Windows lock screen runs on the Winlogon secure desktop; no third-party window can draw there. Not implementable. Equivalent: hide the island while locked (`WTS_SESSION_LOCK`) and show nothing; omit the "Lock Screen" card or keep it disabled with the note "Not available on Windows". |
| Mission Control | Task View (Win+Tab) is not a conceal-worthy overlay; optionally hide while the foreground class is `MultitaskingViewFrame`. |
| Menu bar icon / app panel | "Menu bar icon" = tray icon; app panel opens from it (or from the island when `notchAppPanel`); "Hide the menu bar icon" = hide the tray icon but keep it shown when an update is ready or the mic is muted. |
| Accessibility permission | Not needed on Windows. |
| Dialogs above the island | Show `MessageBox`/TaskDialog with `Owner = island window`; the dialog is topmost relative to the island; after close, `Activate()` the island if open. |
| Keyboard | In-window `PreviewKeyDown` handler with Ctrl instead of Cmd; Ctrl+Alt+letter may collide with AltGr composition on some layouts - treat the combination only when no text control has focus or when the produced char is not a printable letter. |
| Accessibility | UIA names per control; island exposes custom actions (Keep open, Settings, Collapse); Reduce Motion = `SystemParameters.ClientAreaAnimation`; high contrast = `SystemParameters.HighContrast`. |
| Settings backup | Keys listed in section 10 are stored in Trayloom's settings store with the same names (without the macOS-only ones). |

---------------------------------------------------------------------------------------------------------------------

### 13. Gap vs Trayloom (current state of `src/Trayloom.Features.Island`)

What exists
- `IslandWindow` (WPF `OverlayWindow`, no-activate) as a black pill centred at the top with `CompactHeight 34`, idle width 116, expanded 480 x 292, header height 46, bottom corner radius 17 (collapsed) / 30 (expanded); grows/shrinks the native window around the animation; publishes pill rect.
- Hover open after 130 ms timer, collapse after configurable 450 ms (200/450/900/2000 presets), 150 ms cursor polling while expanded, `HoldOpen` token.
- Collapsed content: left artwork 22 pt / glyph, centre title, right reading or 3 equaliser bars, a grabber bar when idle; alert messages with duration; width 236/276/112.
- Expanded: header with tab pills (Segoe Fluent glyph + label per registered section), content host 20/4/20/18 margins, cross-fade between layers, easing: ExponentialEase(6) open 380 ms / CubicEase close 240-280 ms.
- Sections registry (`IslandHub`): music (Order 0), timer (10), calendar (20), downloads (30). Compact info (single owner wins) and alerts.
- Gestures feature (scroll down open / up close, sideways swipe or Shift+wheel for tracks, optional invert).
- Display: primary vs pointer monitor, top gap 0-24, hide while a full-screen app is in front (default on), per-monitor DPI placement, display-change handling.
- Settings: 6 simple options (Open the island hover/click, Show on primary/pointer monitor, close delay choices, distance from top 0-24, show track name in collapsed island, hide in full screen).

What is missing or different (to build)
1. **Settings page**: replace the 6-option panel with the full page of section 9: header with master switch (default OFF in source; Trayloom currently on), tab row Layout/Content/Activity/Behavior (+Companion), "Open Dynamic Island" button, all cards, strings and keys of sections 9-10. Current key names (`trigger`, `monitor`, `collapseDelay`, `topGap`, `compactTitle`, `hideInFullscreen`) must map to/replace `notchOpenOnHover`/`notchHoverExpands`, `notchDisplay`, `notchCloseDelay` (0.10-2.00 s slider instead of 4 presets), capsule "Distance from top" (0-20), `notchHideInFullscreen` (default false in source).
2. **Defaults**: opening default is "Click to open" (not hover); hover delay default 0.25 s (existing 130 ms); close delay 0.18 s (existing 450 ms); Spacious size; idle content Music.
3. **Island shell**: the source island has no tab strip in its header. Its chrome is: an *Explore* gallery (grid of section tiles with search, dots, keyboard hints), a 36 pt *top row* that holds each page's own controls, floating 44 pt *quick-access* buttons (left/right/bottom, defaults Explore+Timer / Settings+Volume mixer / Now Playing), a context menu, Esc-to-step-back. Trayloom uses a tab bar - needs the Explore gallery, quick-access layer, top-row band, pin/"Keep open", update control, context menu.
4. **Geometry**: replace fixed 116x34 / 480x292 with the NotchGeometry model (section 4): 24 DIP strip, 109x24 resting capsule (or 135x24 notch), wings 44, activity wing ranges, expanded widths 480/560/custom 360-600, heights from content (+64 chrome), cap `screen - 24`, gutters 72, silhouette radii (shoulder 14, bottom min(28, 0.34 h), capsule half-height).
5. **Motion**: replace ease storyboards with the dual springs (growing 0.44/0.38 bounce .25/.22, shrinking 0.30/0.26 bounce 0, steady 0.5), content reveal/dismiss/depart/replace transitions with blur+scale+opacity keyframes, hover pulse (+10 / +5), quick-access reveal spring 0.38/0.12.
6. **States**: add peek (hover preview), activity picker + combine, notices (volume, brightness, keyboard light, microphone, input source, battery, clipboard, capture, accessory, download, track, timer, watch, agents, system notification with banner hold/expand), drop placeholder, capture controls, hidden-until-hover (+show activity), full-screen compact with hover-in-full-screen, pinned island, notification preview.
7. **Closed content**: current single "compact info" model lacks: priority list (timer, watch, downloads, agents, calendar, music, keep-awake), user-picked activity, pairs, per-activity strips (capsule + notch variants), idle content choices (nothing/battery/music/AI limits), low-battery red/amber logic, music cover naming/play-pause-on-hover, equaliser following audio, companion.
8. **Sections**: only Music/Timer/Calendar/Downloads exist; the shell must host the 16 sections (Controls, Volume mixer, Now Playing, Clipboard, Recent captures, Files, System, Tools, Calendar, Notifications, Timer, Camera mirror, Downloads, Scratchpad, AI Agents, Watch) with per-section enable/hide/order persistence, availability tied to the features installed, Content-tab options/previews and summaries.
9. **Interaction**: add click-away via low-level mouse hook + foreground-change close rule, pin, Esc layering, keyboard shortcuts (Ctrl+K, Ctrl+,, Ctrl+Alt+key, Ctrl+Tab), Explore wheel stepping, slider scroll rules, island-wide gesture constraints (header-only vertical close, music-only horizontal), global shortcut `notchShortcut`.
10. **Multi-display**: add Automatic / Built-in / Main / Follow pointer (0.2 s dwell) / All displays (mirrors); current code has only primary/cursor.
11. **Chrome**: Black fill only; add translucent (acrylic), outline option, capture exclusion, tray-icon hiding, "Where things open" destinations, privacy switch, Lock-screen card (disabled).
12. **Strings/typography**: use the strings of section 8/9 verbatim (note some English text mentions Mac/macOS; use the Win-adapted copies); island text sizes: 11-13 pt UI, 12 semibold titles, mono digits for readings; Segoe UI Variable.
13. **Settings editors**: Layout editor canvas with drag/drop of quick-access buttons and corner resize grip, silhouette samples, capsule-fit sliders, section list with drag reorder + live scaled preview (wide/narrow modes), toggle-card grids with unavailable-reason links.


---------------------------------------------------------------------------------------------------------------------------------

# PART C - ISLAND PAGES (MUSIC, CONTROLS, MIXER, SYSTEM, CAPTURE/CAMERA, KEEP AWAKE, CALENDAR, TIMER)

## Island media pages: Music, Controls, Mixer, System, Camera, Calendar, Timer

Product: Trayloom. Feature name in user-visible strings: "Dynamic Island" (keep this wording; it is what the source strings say).
Source base: `Sources/Vorssaint/` (Swift). Paths below are relative to it. Current port: `src/Trayloom.Features.Island/*.cs`.
"Notch" in code = the island. Windows has no camera cutout, so every "beside the camera" branch collapses to the **capsule / full-row** branch. Only that branch is specified here.

Conventions used in this file
- pt = DIP in WPF. Colours: `white NN%` = white at alpha NN. Radii "continuous" = plain rounded rectangle on Windows.
- "Icon" lines give SF Symbol -> suggested Segoe Fluent Icons code point (`E768`). Codepoints are best-effort suggestions; confirm each against the font in Character Map during implementation. Where no clean glyph exists the line says "draw".
- Platform words in strings are adapted: `Mac`->`PC`, `macOS`->`Windows`, `System Settings`->`Settings`, `Command`->`Ctrl`, `Finder/Calendar.app` -> the Windows equivalent. Everything else is verbatim English (`Core/*Strings.swift`).
- Reduce Motion = Windows "Animation effects" off (`SPI_GETCLIENTAREAANIMATION` / `UISettings.AnimationsEnabled`). Wherever the source says "reduceMotion ? nil : animation", skip the animation.

---------------------------------------------------------------------------------------------------

### 0. Shared primitives used by every page below

Source: `UI/Notch/NotchComponents.swift`, `UI/Notch/NotchLevelSlider.swift`, `Services/Notch/NotchSliderEditing.swift`.

#### 0.1 Palette (island is always dark)
| Token | Value |
|---|---|
| Text | white 100% ; secondary = white ~55-60% (SwiftUI `.secondary` on dark) ; tertiary white 45% |
| Surface (card/tile) | white 6.5% fill, selected white 12%; radius 18 (cards) / 14 (tile hit) |
| Track (slider/meter) | white 14% ; fill = tint at 92% (disabled 30%) |
| Orange (timer) | #FF9F0A ; Red (mute/record) #FF453A ; Yellow (keep awake) system yellow ; Mint (happening now) system mint #63E6E2 ; Calendar accent (today) rgb(255, 92, 100) = (1.0, 0.36, 0.39) |
| Hairline around art | white 12%, 0.5 pt |

#### 0.2 NotchButtonStyle (all buttons)
- Hover: white 9% wash in a rounded rect of the button's `cornerRadius` (default 10) and scale 1.022 (only when `lifts`).
- Pressed: opacity 0.8, scale 0.965. Disabled: opacity 0.4, no hover.
- Spring feedback: press response 0.28 s damping 0.7; hover response 0.32 s damping 0.75. WPF: 120 ms CubicEase-out ScaleTransform is an acceptable stand-in.

#### 0.3 NotchIconButton
28x28, glyph 12 pt medium, colour white (selected) / white 55%; selected plate white 12%, radius 9; tooltip = title; used for lyrics/queue toggles, sections, chevrons, close, etc.

#### 0.4 NotchPillButton
Text 12 pt (semibold if `prominent`, else medium), colour white / white 70%, horizontal padding 14, height 28, capsule; background white 14% when prominent, none otherwise.

#### 0.5 NotchEmptyView
Used for every "empty / unavailable" page. Two arrangements, first that fits: (a) vertical: glyph tile, label (max width 250, centred), actions; (b) horizontal: glyph tile at left, label (max width 260) + actions at right. Padding 12. Glyph tile: 56x56, radius 18, fill white 5%, symbol 26 pt light, white 65%. Label: 12 pt secondary.

#### 0.6 NotchLevelSlider (volume, brightness, keyboard light, playback position, faders)
- Horizontal bar (value 0..1 or custom range) or vertical fader (fills from bottom). No knob. Track = capsule, radius = thickness/2, bg white 14%, fill = tint (default white) at 92%.
- Thickness: explicit `trackThickness` (8 for the island's level rows, 28 for mixer faders) else 78% of the control's cross size (e.g. height 10 -> 7.8). Hit area is the full control height (24 for level rows, 10 for the playback bar).
- Optional marker: 1 pt line in white 45% across the track (mixer uses it at 100%).
- `easesChanges`: when the value is changed from outside (key, other app) and not while dragging and window visible: animate drawn value over 0.22 s with ease-out cubic `1-(1-p)^3`. Initial display never animates.
- Scroll wheel over a level bar changes it (up/right = raise; one wheel notch = one step like a volume key); NOT on the playback bar (`scrollable:false`).
- Accessibility value defaults to "NN%".

#### 0.7 NotchMeter (read-only)
Capsule, height 5 (6 for the playback-position fallback), track white 14%, fill tint at 90%; minimum fill width = height when value>0.

#### 0.8 NotchMenuButton / NotchMenuItem
A flat control that pops a native menu. Item = title, checked (check mark), optional symbol, enabled, separator (empty title). Windows: custom popup or `ContextMenu` placed under the button; keep checked item with a check glyph (E73E).

#### 0.9 Keyboard / navigation shared by the island
Per-page direct keys (island shell): Controls `c`, Mixer `v`, Music `m`, Clipboard `b`, Captures `s`, Files `f`, System `i`, Tools `t`, Calendar `a`, Notifications `n`, Timer `r`, Camera `w`, Downloads `d`, Scratchpad `p`, Agents `g`, Watch `o`. Page titles: Controls "Controls", Mixer "Volume mixer", Music "Now Playing", System "System", Calendar "Calendar", Timer "Timer", Camera "Camera mirror".
Escape closes the page's inner layer first (lyrics/queue, mixer options, calendar month grid), only then the island (`setPageLayer(page, close:)`).

#### 0.10 Island sizes the pages are built for
Content width = island width - 56 (28 inset each side). Presets: Compact island 480 wide, content height 180; Spacious (default `notchSize`="spacious") 560 wide, content height 264; Custom width 360...600 (default 440), height 260...640 (default 480). Row spacing between page rows = 10; shell spacing 12.

#### 0.11 Gap summary for the shell (details in other specs)
Port window: 480x292 expanded, header 46 (tabs + collapse), content margin 20/4/20/18 (~440x224 usable). Pages must be designed for ~440 wide x ~224 high until the island sizing spec lands; every number below that depends on width uses the real content width passed in.

---------------------------------------------------------------------------------------------------

## PART 1. MUSIC

Sources: `UI/Notch/NotchMusicView.swift`, `NotchMusicStrip.swift`, `NotchQueueView.swift`, `NotchLyricsView.swift`, `NotchEqualizerBars.swift`, `NotchComponents.swift` (artwork), `Services/Notch/NotchMusicService.swift`, `NotchMusicSupport.swift`, `NotchPlayback*.swift`, `NotchPreferredPlayer.swift`, `NotchQueueSupport.swift`, `NotchShuffleService.swift`, `NotchLyrics*.swift`, `NotchAudioLevel*.swift`, `NotchBrowserArtwork*.swift`, `Core/NotchMusicExtrasStrings.swift`.

### 1.1 Strings (English, verbatim)

From `FeatureStrings.radialMenu` / `.notch` / `.notchMusicExtras`:

| Key | Text |
|---|---|
| mediaPlayPause | Play or pause |
| mediaPrevious | Previous track |
| mediaNext | Next track |
| mediaNowPlaying | Now Playing |
| mediaNothingPlaying | Nothing playing |
| notch.musicHint | Your music and playback controls appear here. |
| notch.playbackPosition | Playback position |
| notch.music | Music |
| notch.newTrack | New track |
| notch.sectionsTitle | Explore |
| notch.open | Open Dynamic Island |
| lyrics | Lyrics |
| lyricsDescription | Follow the lyrics of the current song in the Dynamic Island. |
| enableLyrics | Show lyrics |
| online | Find lyrics online |
| onlineHint | While lyrics are open, send the song title, artist, album and duration to lrclib.net. Imported lyrics stay on this Mac. Song titles, artist names and albums may also be checked with musicbrainz.org.  (adapt "this Mac" -> "this PC") |
| importLyrics | Import lyrics… |
| importHint | Choose timed lyrics for the current recording. They are kept only while this song is open. |
| loading | Loading lyrics… |
| unavailable | No matching lyrics for this recording. |
| failed | Could not load the lyrics. |
| retry | Try again |
| instrumental | Instrumental |
| waiting | Waiting for the first verse |
| noPosition | The player does not share its position. |
| offset | Timing |
| earlier / later / reset | Earlier / Later / Reset |
| actionFailed | The player did not switch to this song. |
| playbackFailed | Could not change playback. |
| allowPlayback | Allow playback control |
| automationPermission | Playback automation |
| automationExplanation | Some music apps require permission for their playback controls. Authorize the selected player from Dynamic Island; access is granted separately for each app. |
| queue | Up next |
| queueDescription | See the actual upcoming songs shared by your player. |
| enableQueue | Show upcoming songs |
| queueUnavailable | This player does not share its upcoming songs. |
| queueEmpty | No upcoming songs |
| playNow | Play now |
| refresh | Refresh |
| openPlayer | Open player |
| playbackSource | Playback source |
| automaticSource | Automatic |
| includeOtherPlayers | Automatically include videos and other apps |
| liveEqualizer | Bars follow the music |
| liveEqualizerDescription | Move the Dynamic Island bars with the sound your player makes. |
| liveEqualizerHint | Listens to the player's own audio output to move the bars. macOS asks once to allow system audio recording. Nothing is stored or sent. Without it, the bars keep their usual motion.  (Windows: "Listens to the player's own audio output to move the bars. Nothing is stored or sent. Without it, the bars keep their usual motion.") |
| liveEqualizerUnavailable | Needs macOS 14.4 or later.  (not shown on Windows) |
| shuffle | Shuffle |
| showVolume | Show volume in the player |
| preferredPlayer | Open when nothing is playing |
| preferredPlayerHint | The cover and the playback buttons open this app. Automatic opens the music app that played last, then Spotify or Apple Music.  (Windows: "...then Spotify or Apple Music" -> "then Spotify or Windows Media Player / Media Player") |
| openNamedPlayer | Open %@  (e.g. "Open Spotify") |
| browserCovers | Find covers for Firefox and Zen |
| browserCoversHint | (Firefox session/YouTube thumbnail lookup; see 1.12 - not needed on Windows) |
| radial/settings: playingMusic | Show music while playing |
| sectionsButton title | Explore |

### 1.2 Music page layout (open island, "Now Playing")

Top to bottom, inside the page (width W = content width):

```
[ head row  : cover 56 | title+artist (flex) | right cluster (56 high) ]
14
[ timeline  : "0:00"  [========slider h10========]  "-3:21"           ]   (only when playing item has duration)
10
[ transport : 5 equal slots = W/5 each                                 ]   height 44
8  bottom room
(10 + 28)  optional volume row (setting notchMusicShowVolume)
```
Natural heights (no volume row): with timeline 56+14+14+10+44+8 = **146**; idle (no timeline) 56+14+44+8 = **122**; add 38 for the volume row. The island grows by extras (see 1.5) rather than shrinking the player.

#### Head row (`HStack alignment .bottom spacing 14`, height 56)
- **Cover**: 56x56, radius 19% of side (10.64), 0.5 pt white 12% inner stroke. Button that opens the playing app (`RadialNowPlayingApplication.open`). Tooltip/AX "Now Playing". Scale 0.94 while paused, 1.0 while playing, animation smooth 0.3 s.
  Fallback when no art: if the source app icon is known show it on black with padding 14% of side; else black with `music.note` (E8D6) 30% of side, light, white 45%.
- **Titles** (flex, fixed height 38, VStack spacing 2): title 14 pt semibold 1 line (placeholder "Now Playing"); second line 13 pt secondary 1 line = `artist ?? album ?? "Now Playing"`; when `commandFailed` it shows "Could not change playback." in orange instead.
- **Right cluster** (VStack trailing, height 56):
  - top: 28-high row, spacing 6: lyrics toggle (icon `quote.bubble` -> E90A, title "Lyrics", shown only if setting lyrics on) ; queue toggle (`list.bullet` -> E8FD, title "Up next", shown only if queue setting on); sections button (`square.grid.2x2` -> E80A, title "Explore") only if the shell asks for it. Toggles use NotchIconButton `selected` when their extra is open (exclusive: opening one closes the other).
  - bottom (fills rest, spacing 8): **source menu** (only when >1 source or a source is manually chosen): 18x18 app icon in a 3 pt padded 24x24 hit area, radius 6, menu title "Playback source"; tooltip `"Playback source: <app name>"`; fallback glyph `app` (E71D) 13 pt secondary. Then **mini equalizer** `NotchLiveEqualizerBars(isPlaying, bars:4, barWidth:2.5, height:15, tint: white 50%)`.

#### Timeline (`NotchMusicTimeline`, "timesBeside" mode)
Row height 14, spacing 8, font 10 pt medium monospaced digits, secondary colour:
`[elapsed] [slider h10] [-remaining]`. Each time label reserves the width of the track's longest reading (hidden duplicate) so the bar never changes length. Remaining text = U+2212 "−" + `mm:ss`. Format: `h:mm:ss` if >= 3600 s else `m:ss`; clamp 0...604800.
- Shown only if `duration > 0`. Position = `elapsed + (playing ? (now - sampledAt) * rate : 0)` clamped 0...duration; refresh at most 1 Hz, paused when not playing.
- If `canSeek`: NotchLevelSlider(range 0...duration, tint = accent, label "Playback position", valueLabel = elapsed text, scrollable false). Drag updates a local scrub position; on release send `seek`, keep the released thumb for up to 1 s (or until the player reports a position within 2 s of the target, or the track/context changes) then return to observed position. Disabled while a command is pending.
- If not seekable: NotchMeter(value position/duration, height 6, tint accent), non-interactive.
- `accent` = dominant cover colour (see 1.8); white when the cover is neutral.

#### Transport row
Five equal slots of W/5: `[shuffle][prev][play/pause][next][output]`. An absent button keeps its slot empty so play stays centred.
- prev / next: `backward.fill` (E892), `forward.fill` (E893), 19 pt semibold white, 44x44 round hit area, hidden-but-reserved when the player says it cannot skip (`canSkipPrevious/Next == false`), disabled when the command is unavailable.
- play/pause: `play.fill` (E768) / `pause.fill` (E769), 28 pt semibold white (22 in the compact card variant), cross-fade/replace transition 0.22 s. Tapping flips the glyph at once ("requested" state) for max 1.5 s while the player confirms; keyboard shortcut **Space** (except in a settings preview).
- **shuffle** (`shuffle` -> E8B1): 44x44 hit, 32 plate circle, glyph 16 pt semibold; off = white 55% (30% if disabled), on = accent colour on a white 12% plate; tooltip "Shuffle", or "Allow playback control" if consent needed. Shown only if the player exposes shuffle.
- **output** (right slot): 44x44, glyph 16 pt semibold white 55%; opens a menu of audio output devices (checked = current default) and, if the mixer page is enabled, a separator + "Volume mixer" (speaker E767) item. Glyph by device kind: headphones E7F6, built-in laptop/desktop speakers (laptop `laptopcomputer` / `desktopcomputer`: draw or E770/E7F4), speakers/hardware E7F5, virtual/other AirPlay-like E7F5. Tooltip = device name or the last switch error. Hidden when the mixer feature is off.
- When the player needs consent (macOS-only concept) the transport row is replaced by: play toggle + "Allow playback control" button / "Open System Settings…" + refresh (E72C). **Not used on Windows** (GSMTC controls need no consent).

#### Idle page (no playback)
- Head: cover is a button that opens the preferred player (disabled when none installed; tooltip "Open <App>"); titles: source picker if any sources / chosen source, else "Nothing playing" (14 semibold); below it, when a player exists, "Open <App>" (13 secondary).
- Transport: if a preferred player exists, a transport of three buttons (backward.fill 19, play.fill 28, forward.fill 19 inside the same 5-slot row, no shuffle) where **every button opens the app** (tooltip/hint "Open <App>"); otherwise the centred hint "Your music and playback controls appear here." (12 pt secondary, 2 lines max). Height 44, top padding 14.
- While waiting for the first reading (`awaitingPlayback`) the head is blank space of the same height (no flash of "Nothing playing").
- Output button and optional volume row still appear.

#### Optional volume row
Setting `notchMusicShowVolume` (default false, requires mixer feature): under the transport, 10 pt gap, a 28-high `NotchAudioControls` (see Part 2.5).

### 1.3 Settings (Music)

| Key | Type | Default | Meaning |
|---|---|---|---|
| notchShowPlayingMusic | bool | true | "Show music while playing": closed island shows the music strip while a song plays (also requires idleContent != none, or mascot companion) |
| notchIncludeOtherPlayers | bool | true | "Automatically include videos and other apps": automatic source also follows non-music apps |
| notchMusicShowVolume | bool | false | "Show volume in the player" |
| notchPreferredPlayer | string | "" (Automatic) | App id opened when nothing plays; max 256 bytes; "" = Automatic |
| notchLyricsEnabled | bool | true | "Show lyrics" (feature toggle) |
| notchLyricsOnline | bool | false | "Find lyrics online" (sub-toggle shown only when lyrics on); off = only imported lyrics |
| notchBrowserCovers | bool | false | "Find covers for Firefox and Zen" |
| notchQueueEnabled | bool | true | "Show upcoming songs" (caption: "See the actual upcoming songs shared by your player.") |
| notchLiveEqualizer | bool | false | "Bars follow the music" |
| notchTrackChange | bool | true | Show the "New track" notice on the closed island when the song changes |
| notchIdleContent | enum none/battery/music/agents | music | What the resting island shows when idle (music = cover + 3 bars if playing) |

Settings page order (Music tab): Show music while playing, Automatically include videos and other apps, Show volume in the player, Open when nothing is playing (picker: "Automatic" + installed music apps), Show lyrics (+ indented "Find lyrics online" with its hint caption), Find covers for Firefox and Zen (+hint), Show upcoming songs (+caption), Bars follow the music (+hint). Icons: music.note, play.rectangle, speaker.wave.2, arrow.up.forward.app, text.quote, globe, photo, list.bullet, waveform.

Memory-only (never persisted): chosen source (resets with the process), lyric timing offset, queue, artwork cache, last played preferred app (`NotchPreferredPlayer.lastPlayed`).

### 1.4 Behaviour: playback model and edge cases

- **Playback record**: track (title, artist, album, artwork bytes, app bundle id/pid), isPlaying, elapsed, duration (cap 7 days), rate (clamped 0...16, default 1), sampledAt, canSeek, hasPosition, item identifier, canSkipNext/Previous, command context.
- **Gap grace**: when a reading says "nothing playing", or another player's paused track, or the next song paused, the last song stays for **1.5 s** (`gapGracePeriod`); a new reading replaces it immediately. The deadline never extends. Prevents the page/strip from flickering between songs.
- **Track-change detection**: per-player last song (title, artist; artist optional in comparison). First reading after start/source change only records. A change counts only when the new reading is playing. Emits `trackChanges` (drives the "New track" notice, setting `notchTrackChange`) before the new song is published; `trackEnds` when playback truly ends.
- **Artwork cache**: one decoded cover (thumbnail 160 pt @2x). Same bytes on a new song stay for 1.5 s then expire if the player sends no new art. Dominant colour: average the cover to 1 pixel -> `NotchArtworkTint`: reject if max channel <= 0.05 or (max-min)/max < 0.12 (neutral cover => white); else stretch channels: `c' = clamp((c-min)/(max-min)*0.86 + 0.06)`.
- **Source selection**: sources list (max 16, sorted by pid). Automatic order: (1) explicit choice if still listed; (2) playing music apps; (3) playing others if `includeOtherPlayers`; (4) the previously followed other player while paused; (5) any paused music app. Previous player keeps its place; a paused video never switches to old music. "Music app" = Spotify/Apple Music/iTunes or app category music. Menu: "Automatic" (checked when automatic), separator, one item per source (checked = chosen even while automatic fills a gap). Choosing the already-effective option does nothing. A choice also clears the current playback, sets `awaitingPlayback`, closes the queue.
- **Preferred player resolution** (`NotchPreferredPlayer`): chosen id if installed; else the music app that last played this session; else Spotify, then Apple Music (Windows: Spotify, then the Windows media app). Opening never starts playback.
- **Commands** (toggle/next/previous/seek) are accepted only for the current "command context" (player + recording revision); a command during a gap is refused; a second command while one is pending is ignored. Failure => `commandFailed` for the row that shows "Could not change playback." (orange) until the next successful command.
- **Quick-flip**: toggling shows the new glyph immediately, resolved when the player answers or after 1.5 s.
- **Escape / layers**: Esc closes lyrics/queue first. `onDisappear` hides lyrics, stops queue refresh, stops shuffle polling.

### 1.5 Lyrics and Up-next extras

#### Extras area
Opening lyrics or the queue adds a 216-pt area under the player (`geometry.musicExtrasHeight`; for custom sizes `min(216, available)`). The player keeps its natural height; the island grows. If the island cannot grow (custom size) and the room left (`page - resting - 10`) is < **88**, the extra takes the whole page and the player is replaced by a 28-high row of the extras toggles (+ sections button) aligned right so the user can close it. Extra area is clipped and top aligned, 10 gap from the player.

#### Lyrics view (`NotchLyricsView`)
State machine (`NotchLyricsService.State`: idle, consent, loading, unavailable, failed, ready):
1. `lyrics.instrumental` -> NotchEmptyView(symbol `music.note`, "Instrumental", pill "Import lyrics…").
2. synced lines present and the player gives a position -> **synced view** + timing row.
3. otherwise plain text: if no position, caption "The player does not share its position." (12 secondary); scrollable selectable text 15 pt medium (edge fade); under it borderless "Import lyrics…" (doc.badge.plus E8E5 + text, caption, secondary).
4. `loading` -> centred spinner + "Loading lyrics…" (callout secondary) with pill "Import lyrics…" below.
5. else NotchEmptyView(symbol `exclamationmark.triangle` (E7BA) if failed else `quote.bubble`, message, actions). Message = [ "Could not load the lyrics." if failed, else "No matching lyrics for this recording." if online on ] joined by newline with onlineHint when online is off. Actions: next step + import. Next step: online on => prominent pill "Try again" (retry); online off => prominent pill "Find lyrics online" (sets `notchLyricsOnline=true`). Import = pill "Import lyrics…" or, if too narrow, an icon button (doc.badge.plus).

Synced list: `LazyVStack(spacing 12)`, each line 17 pt **bold**, white 100% when active else 35% (0.28 s smooth), one font size for all lines (no re-wrap), empty line text rendered "♪", vertical padding 6, edge fade length 24. The active line scrolls to **centre** with 0.28 s animation; before the first line a "Waiting for the first verse" caption (12 secondary) anchors the top. Active index = last line with `time <= position - offset`. Highlight updates only at line boundaries (schedule of dates computed from rate/sampledAt; no timer while paused).
Timing row (below the list, 18 high, caption): [import icon] "Timing" (secondary) ... [`−` button 24x18] [offset text `+0.25` format sign always, 2 decimals, monospaced, min width 44, click resets, tooltip "Reset"] [`+` button]. Tooltips "Earlier"/"Later". Step 0.25 s, range -10...+10, rounded to 0.25. Offset is memory-only and resets with the song.

#### Lyrics service (`NotchLyricsService`)
- Active only when `notchLyricsEnabled`, music module present and the lyrics page is open (`visible`). Remembers **one** recording's lyrics + offset; any other recording clears it.
- Online (only when `notchLyricsOnline`): `GET https://lrclib.net/api/get?track_name=…&artist_name=…[&album_name=…]&duration=<rounded seconds>`. Valid only if title, artist non-empty (<=1024 bytes), album optional, duration 1...3600. Response accepted when trackName/artistName equal (case-insensitive trimmed), album compatible, `|duration - track.duration| <= 2`, and `syncedLyrics`/`plainLyrics`/`instrumental` present. Then lenient catalog match, then `GET /api/search?track_name&artist_name` variants (exact credits, primary artist, normalised accents/punctuation), best-scored candidate; then discovery search `?q=` ; then MusicBrainz (`https://musicbrainz.org/ws/2/artist|recording|release-group/?query=…`, >= 1.05 s between requests) to resolve artist aliases. Timeouts 10 s request / 15 s resource; JSON only; no redirect following needed; body caps 128 KB (get) / 1 MB (search). Failed result cached 15 s, "none found" 60 s; artist-alias cache max 32 entries (3600 s found / 600 s none / 15 s failed). User-Agent `Trayloom/<version> (<project url>)`, `Accept: application/json`.
- LRC parser: lines `[mm:ss.xx]text`, multiple time tags per line, `[offset:±ms]` (|v| <= 60000 ms), BOM tolerant; max 2000 lines and 128 KB; lines past the duration are dropped; identical times are merged with newline; an all-empty result is rejected.
- **Import**: file picker (`.lrc` / plain text) titled with `importHint` as message, max 128 KB UTF-8, parsed with the track duration; success => ready, empty => failed state ("Could not load the lyrics."). Picker stays above the island and does not close it.
- Everything is off the network unless the user turned online on; imported lyrics are never uploaded.

#### Queue view (`NotchQueueView`)
Header row: "Up next" (callout semibold) ... spinner (mini) while loading/acting, refresh icon button (E72C, tooltip "Refresh"; disabled while busy). If an action failed: "The player did not switch to this song." (12 caption, orange).
List: scrollable (edge fade), row = cover 34x34 (NotchArtwork radius 19%) + title (callout medium, 1 line) + artist (caption secondary) + play button (11 pt `play.fill`, 28x28 round, tooltip/AX "Play now" / "Play now: <title>") only when the player allows jumping (`queue.canPlay`); row vertical padding 8. Max 20 rows. The current song is excluded. Play is disabled while a queue action is pending or the shown queue belongs to a previous song.
Empty/unavailable: NotchEmptyView(`list.bullet`, message = "This player does not share its upcoming songs." when no queue object, else "No upcoming songs", action prominent pill "Open player"). Top padding 6.
Behaviour: queue requested when the page opens (and on refresh), cancelled when closed or when the setting is off; reply keyed by request id; ignored if it is for another recording.

### 1.6 Closed-island music strip (`NotchMusicStrip`, `NotchCapsuleMusicStrip`)

Windows island is a pill/capsule, so use the capsule variant (`NotchCapsuleMusicStrip`):
- Order left->right: **cover** (circle, side = stripBodyHeight − 6, inset (body − side)/2 so it is concentric with the round end; 0.5 pt white 14% edge; fallback icon/black+`music.note`), **title** (only when named), **bars** (right end).
- Capsule height = strip height (20 pt on a standard bar; port uses 34 — keep the port's height). End padding 8; title font 12 semibold, 1 line, tail truncation; spacing from cover 8.
- Width: `cover + (8 + titleWidth + 8 | 8) + barsWidth` + the capsule's own side radii, clamped to [resting width, 260].
- **Bars**: 7 bars x 1.8 pt wide, gap 0.85 x width (1.53), height `min(16, max(6, bodyHeight - 8))`, tint = cover colour (white if neutral). Total width = 1.8 x (7 + 6 x 0.85) = **21.78**. Colour is the track's `accent` (`NotchArtworkTint`), not green.
- **Naming the song**: the title is shown (a) for **4 s** after a new song starts (`musicTitleDuration`), and (b) while the pointer rests on the cover for **0.3 s** (`namingDelay`), disappearing when the pointer leaves. Otherwise only cover + bars.
- **Bars become a button**: pointer resting on the bars crossfades (0.15 s ease-out) to `pause.fill` (E769) / `play.fill` (E768), size `min(13, max(8, barHeight − 3))` semibold, in the bars' colour, centred on the bars. A click toggles the song in place and flips the glyph at once; paused-from-strip stays visible (held) while the pointer is inside so it can be resumed. Click anywhere else on the strip opens the island on the music page. VoiceOver custom action "Play or pause".
- While a song change is announced by the "New track" notice, the strip keeps the previous song until the notice has shown the new one (held snapshot). A reading with no name keeps the held song (artist/title do not flicker).
- Strip visibility: `notchShowPlayingMusic` AND music playing. Hidden on the resting island when paused (the resting "music" idle content shows cover + 3 bars (width 2, height 10) only if playing).
- Swipe feedback: on a trackpad swipe skip the strip nudges 8 pt (cubic 0.09 s then smooth spring 0.25 s) in the swipe direction (−8 for forward). Windows: only if a horizontal-swipe gesture is implemented (see Gestures spec); optional.
- Tooltip = song title. Accessibility label "title, artist"; hint "Open Dynamic Island".

### 1.7 Equalizer bars: exactly how they follow the music

Files: `UI/Notch/NotchEqualizerBars.swift`, `NotchComponents.swift` (`NotchLiveEqualizerBars`), `NotchDecorativeClock.swift`, `Services/Notch/NotchAudioLevelSupport.swift`, `NotchAudioLevelService.swift`.

#### Number, size, placement of bars (by place)
| Place | bars | barWidth | height | tint |
|---|---|---|---|---|
| Closed strip / capsule | 7 (`compactMusicBarCount`) | 1.8 | min(16, max(6, content height − 8 or −10)) | cover tint or white |
| Open player (head row) | 4 | 2.5 | 15 | white 50% |
| Resting island (idle music) | 3 | 2 | 10 | cover tint or white |
Bar spacing (gap) = 0.85 x barWidth; bar i is centred vertically on the row, x = i·1.85·w + w/2, corner radius = w/2. Frame width = n·w + (n−1)·w·0.85.

#### Two modes
1. **Live mode** (setting `notchLiveEqualizer` on, supported, not Reduce Motion, a player playing, levels available): the service publishes **7 band levels (0...1) at 30 Hz**. Bar i reads band `floor((i + 0.5) / bars × 7)` (clamped 0...6), so 7 bars map 1:1; 4 bars read bands 0, 2, 4, 6 (0.875, 2.625, 4.375, 6.125 floored); 3 bars read bands 1, 3, 5 (1.17, 3.5, 5.83 floored). Height = `max(barWidth, height × (0.1 + 0.9 × clamp(level)))`, set directly (the service already smooths) with no implicit animation.
2. **Synthetic mode** (default; also fallback when live is off/unsupported/silent/while the tap is warming up): each bar swings between a low and high height:
   - `center = (n−1)/2`; `distance = |i − center| / max(1, center)`; `envelope = (1 − distance)^1.5`
   - `low = max(barWidth, height × (0.12 + envelope × 0.25))`, `high = max(barWidth, height × (0.12 + envelope × 0.88))`
   - half-cycle duration `rise_i = π / (5.2 + i × 0.61)` seconds (i=0: 0.604 s ... i=6: 0.355 s); phase lead `0.17 × i` s
   - `phase = (t − origin + lead_i) / rise_i`; `travel(phase)`: `h = phase mod 2 (non-negative)`, `out = h ≤ 1 ? h : 2 − h`, `travel = (1 − cos(π·out)) / 2`; `height = low + (high − low) × travel`.
   - Stepped at **30 fps max** (decorative clock), not 60/120. All copies (island, other displays) share one global time origin so they never jump when windows trade places. Starts only once; metadata/tint/layout changes do not restart it; stops when not visible/occluded.
3. **Paused, hidden, Reduce Motion**: all bars rest at height = barWidth (dots).

#### Live audio-level pipeline (macOS) and the Windows equivalent
| Step | macOS | Windows implementation |
|---|---|---|
| Which audio | Core Audio *process tap* on the playing app and its helper processes (browsers use helper processes), stereo mix-down, muteBehavior unmuted | **Per-process loopback capture**: `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", …)` with `AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType = PROCESS_LOOPBACK, TargetProcessId = pid, ProcessLoopbackMode = INCLUDE_TARGET_PROCESS_TREE }` (Windows 10 build 20348+/Win11). Captures only that app tree, post-mix, unmuted for the user. Fallback if activation fails (older Windows, pid unknown): default render endpoint loopback (`WasapiLoopbackCapture`), applied only when this is the sole playing audio session; else stay synthetic. |
| Finding the pid | `appPID` from the media adapter | GSMTC gives `SourceAppUserModelId`, not a pid. Resolve: enumerate render sessions with `IAudioSessionManager2` on the default endpoint, match `IAudioSessionControl2.GetProcessId()` / session identifier / `GetSessionInstanceIdentifier` containing the exe name from the AUMID (e.g. `Spotify.exe`, `chrome.exe`, `msedge.exe`, `Music.UI.exe`); for packaged apps match package family name in the process image path. Use the parent process of the matched tree so helper processes are included. |
| Format | float32, mixed to mono (average channels) into a ring of 8192 samples; audio thread uses try-lock (drops buffers rather than blocks) | Request `WAVEFORMATEX` float32, 48 kHz, 2 ch (`AUDCLNT_STREAMFLAGS_LOOPBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY`), event-driven, mono-mix into a lock-free ring 8192 floats. |
| Analysis | every 1/30 s: take latest **1024** samples, Hann window, DFT, magnitudes of the first 512 bins scaled by 1/1024 | Same: timer at 30 Hz on a worker thread, last 1024 samples x Hann window, FFT (NAudio `FastFourierTransform` or own radix-2), `|X[k]| / 1024`. |
| Bands | 7 bands, edges Hz `[50, 120, 250, 500, 1000, 2000, 4500, 12000]`; bin = floor(edge / (sampleRate/1024)), lower clamped to ≥1 and ≤ usable−1, upper ≥ lower+1 and ≤ 512; each band keeps ≥ 1 bin; bin 0 (DC) excluded | identical `BandRanges(sampleRate, 1024)` |
| Band level | mean magnitude of bins, `dB = 20·log10(mean)`, `level = clamp((dB + 60) / 60, 0, 1)` (floor −60 dB) | identical |
| Gain compensation | multiplies samples by `levelCompensation` (>1) when the tap on a multichannel output was divided down | not needed (process loopback already gives the stream level). |
| Smoothing | `Smoother`: per update `ceiling = max(maxRaw, ceiling × 0.995, 0.05)`; `gain = 0.92 / ceiling`; `target = clamp(raw × gain, 0, 1)`; `level += (target − level) × rate` with **attack 0.55** when rising, **decay 0.12** when falling. Quiet albums auto-scale to fill the meter. | identical (state persists per reader, reset on a new reader) |
| Heard / silence | Ring tracks `peak`; "heard" once `peak × gain > 0.001`. Levels are published only after sound has been heard. If nothing audible within **8 s** (`silenceGrace`) of starting, the reader gives up for **this play** (identity: title/artist/album/duration/app/item) and bars use synthetic motion; the memory is cleared on pause/stop/feature off/next track. (macOS needs the system-audio permission, a silent tap = denied.) | Same rule keeps working on Windows (no permission prompt, so silence means silent audio or an app that renders on another endpoint). |
| Lifecycle | Reader starts when `playback.isPlaying` with a pid and the feature is enabled (setting on, supported, island enabled, not Reduce Motion). Pause/stop schedules release after **1.5 s** (so a skipped track does not blink); changing player stops the old reader first; a reader id guards stale callbacks. Tap rebuilds when the player's process set grows (keeping history) or restarts when a process leaves (set shrinks) after a 0.25 s settle; default output device or sample-rate change rebuilds the pipeline. | Rebuild on `IMMNotificationClient` default-device change and on the target process tree changing (poll process list at 0.25 s debounce after `Process` start/exit events). |
| Output | `@Published levels: [Double]?` (7 values) or nil | `event Action<double[]?> LevelsChanged`; UI observes only the tiny bars control so the island does not re-render 30 times a second. |

Never store or send audio. Keep the capture only while the player plays and the island is on.

Port gap: `IslandWindow.UpdateBars` runs a 4-bar fixed-period storyboard (0.46/0.62/0.52/0.7 s, ScaleY 0.25-1.0, green, 20 fps) in the compact pill only; no live levels, no other places use bars, no per-cover tint, bar count 4 not 7, bar width 3 not 1.8.

### 1.8 Cover tint
`NotchArtworkTint.color` drives: strip bars, play/pause button in the strip, shuffle active state, slider fill, shuffle plate. Compute on the decoded 160-pt thumbnail. If nil use white.

### 1.9 Shuffle (`NotchShuffleService`)
Shown only when the player exposes a shuffle switch (`isOffered`). Read on page open, on song/player change; poll 1.5 s after a toggle to correct players that answer late. `allowed=false` (e.g. radio) disables the button. Windows: GSMTC `PlaybackInfo.IsShuffleActive` (bool?) + `PlaybackInfo.Controls.IsShuffleEnabled` and `TryChangeShuffleActiveAsync(bool)`; `IsShuffleActive == null` => not offered. Repeat is not in the Mac UI; do not add it.

### 1.10 Source icons (`NotchPlayerApps`)
Cache by pid (max 16 entries) -> (bundle id, name, icon). Windows: resolve from the process (`SourceAppUserModelId` -> exe icon via `SHGetFileInfo`/package logo); cache by AUMID.

### 1.11 Windows mapping for Music

| Concern | Windows approach |
|---|---|
| Now-playing metadata, art, timeline, commands | GSMTC (`GlobalSystemMediaTransportControlsSessionManager`), as `MediaService` already does. Subscribe to `SessionsChanged`/`CurrentSessionChanged`, and **enumerate all sessions** (`GetSessions()`) to build the sources list (each session: AUMID, `GetPlaybackInfo().PlaybackStatus`, has track). Subscribe to `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged` per session. |
| Source selection / automatic | Implement `NotchPlaybackSource.preferred` over the GSMTC sessions. "Music app" set: Spotify (`Spotify.exe` / `SpotifyAB.SpotifyMusic_*`), Apple Music (`AppleInc.AppleMusicWin_*`), Windows Media Player / Media Player (`Microsoft.ZuneMusic_*`, `Microsoft.MediaPlayer*`), iTunes, foobar2000, MusicBee, VLC, AIMP (category-based detection is not available, use a known-list + packaged "music" capability). Browsers/video apps are "other players". Persist nothing; chosen source is memory only. |
| Transport | `TryTogglePlayPauseAsync`, `TrySkipNextAsync`, `TrySkipPreviousAsync`, `TryChangePlaybackPositionAsync(ticks)`. Positions: GSMTC reports `Position`/`StartTime`/`EndTime`/`LastUpdatedTime`; add `StartTime` offset when seeking (port already does). `CanSeek` = `Controls.IsPlaybackPositionEnabled && duration > 0`. Skip availability = `IsNextEnabled/IsPreviousEnabled` (hide the button when false). Command result false => `commandFailed`. |
| Artwork | `props.Thumbnail` stream -> decode at 256 px (port does). Many browsers/Spotify give 300-640 px; no extra network lookup needed. Compute tint on the decoded bitmap (1x1 average). |
| Browser covers (Firefox/Zen session scraping + iTunes lookup) | **Not implemented**: Firefox/Chromium on Windows publish artwork and metadata through Windows media controls (MediaSession API) so the problem does not exist. Hide the setting `notchBrowserCovers` on Windows. |
| Playback "automation permission", "Allow playback control", "Open System Settings…" row | Not applicable (macOS Automation/Apple Events). Never show. Keep the strings only if a future player needs a consent step. |
| Queue ("Up next") | GSMTC exposes no queue. Closest equivalent: always show the specified `queueUnavailable` state ("This player does not share its upcoming songs.") with the "Open player" pill, and keep the toggle visible. Optional later provider: Spotify Web API `/me/player/queue` behind an OAuth sign-in in settings (not in scope for parity). |
| Lyrics | HTTP client to lrclib.net (+ MusicBrainz) exactly as 1.5; import via `OpenFileDialog` (".lrc", "*.txt"). |
| Open player | `Process.Start` of the app by AUMID (`explorer.exe shell:AppsFolder\<AUMID>`) or exe path; detect installed via `Get-StartApps`/registry/`PackageManager`. |
| Output device button / menu | `MMDeviceEnumerator.EnumAudioEndpoints(eRender, ACTIVE)`; set default with the existing `PolicyConfigClient` (`AudioDevices`); device kind from `PKEY_AudioEndpoint_FormFactor` (Headphones=3, Speakers=1, Headset=5, SPDIF/HDMI=…). |
| Reduce Motion | `UISettings.AnimationsEnabled` + `SystemParameters.ClientAreaAnimation`. |
| Space shortcut | Island window active (it is a non-activating overlay; handle Space only when the island is expanded and focused; do not install a global hook). |

### 1.12 Gap vs Trayloom (Music)

`MediaService.cs` (good base): GSMTC single session (current), title/artist/album/source/artwork/duration/position extrapolation/CanPrev/CanNext/CanPlayPause/CanSeek; play/pause, next, previous, seek.
Missing / different:
1. **No source list / picker / automatic logic**; only `GetCurrentSession()`. Need all sessions, per-player grouping, "Automatic" and manual choice, source icons, "Include other players" setting.
2. `MusicSection.cs`: a 120x120 art + 18-pt title layout instead of the 56-pt cover head row; no extras buttons, no queue, no lyrics, no sections button, no source menu, no shuffle, no output button, no volume row, no 5-slot transport, no idle-state "open preferred player" behaviour. Timeline uses `-` ASCII not U+2212 and a 4-pt bar instead of 8-pt NotchLevelSlider; times use fixed 40-px columns (bar length changes); no hover/scale feedback; no Space shortcut.
3. Idle text "Nothing is playing" -> must be "Nothing playing" plus hint "Your music and playback controls appear here." and preferred-player buttons. Title fallback "Unknown title" -> "Now Playing". Source line (small app name under artist) does not exist in the replica (the source is the icon menu).
4. No shuffle (`TryChangeShuffleActiveAsync`), no gap grace (1.5 s), no track-change notice ("New track"), no `commandFailed` "Could not change playback." state, no requested-state optimistic glyph.
5. No cover tint; bars are green and fixed 4-bar storyboard; no 7-bar capsule bars, no live audio levels, no per-place bar sizes, no 30 fps stepping/shared origin, no pause-to-dots.
6. Compact pill: `IslandWindow.RefreshCompact` shows 22x22 rounded-square art (radius 6) + "Title · Artist" always; replica: circular cover concentric with capsule, title only for 4 s / hover-on-cover 0.3 s, bars become a play/pause button on hover, click = toggle, 7 tinted bars. No `notchShowPlayingMusic`, `notchIdleContent` handling or "New track" notice. Port setting `compactTitle` must be replaced by the named-on-hover behaviour (keep as an extra only if desired).
7. No lyrics, no import, no lrclib client; no queue UI; no preferred-player setting; no "Show volume in the player".
8. Settings keys above do not exist in the port (`notchShowPlayingMusic`, `notchIncludeOtherPlayers`, `notchMusicShowVolume`, `notchPreferredPlayer`, `notchLyricsEnabled`, `notchLyricsOnline`, `notchQueueEnabled`, `notchLiveEqualizer`, `notchTrackChange`).

---------------------------------------------------------------------------------------------------

## PART 2. CONTROLS PAGE

Sources: `UI/Notch/NotchControlsView.swift`, `NotchLevelSlider.swift`, `NotchComponents.swift`, `Services/Notch/NotchSupport.swift` (`NotchControlItem`, `NotchLayout.controls`).

### 2.1 What is on the page
The page is a list of **levels** (sliders), one **music card** and a **rail of tiles** (shortcuts). Which items exist is set in Settings -> Dynamic Island -> Controls.

`NotchControlItem` order (all cases): volume, brightness, keyboardLight, music, mixer, keepAwake, timer, calendar, microphone, screenshot, recording, speedTest, panel, commandBar, scratchpad. A user order (`notchControlOrder`, comma-separated raw values) prefixes the list for the tiles (levels and the music card always keep their slots).
Default hidden (`notchHiddenControls`, default string `keyboardLight,microphone,screenshot,recording,speedTest,panel,commandBar,scratchpad`) => **visible by default: volume, brightness, music (card), mixer, keepAwake, timer, calendar**.
An item is also hidden when its feature is off/unavailable (volume/mixer need the Volume mixer feature; brightness/keyboardLight need Brightness; keepAwake/microphone/screenshot/recording/commandBar/scratchpad need their feature; mixer also needs the Mixer module; speedTest needs Network monitor + System module; music/timer/calendar need their module; panel always).
Empty page (no items): NotchEmptyView(`slider.horizontal.3` -> E9E9, "Choose what appears in Dynamic Island settings.").

Settings (Controls tab): four primary toggle cards (Music, Volume, Brightness, Keyboard light) + "Controls and shortcuts" (reorderable grid of toggle cards for the tiles) + "Show “Keep awake” in the closed island" (`notchKeepAwakeActivity`, default false; caption "A running session appears beside the camera with the time it has left, or ∞ when it has no end." -> Windows: "...appears in the island with the time it has left, or ∞ when it has no end.").

### 2.2 Layout (Windows full-row variant)
Order top->bottom, spacing 10:
1. **Level row** (height 28, only if there are levels): levels share the width equally, spacing 12, each min width 76. (On Mac with a roomy header they sit in the island's top row; the replica header on Windows is occupied by the tabs, so use this row.)
2. **Music card** (`NotchMusicControlsView`, see 2.4) height `cardHeight` 80, minimum 68.
3. **Shortcut rail** of tiles, tile cell 76 x 74, spacing 8, row spacing 8 (closes toward 0 gap before it scrolls; rows closed to 6 min). Items fill rows left->right when they all fit (last short row centred); otherwise columns fill top->bottom and the rail scrolls horizontally with edge fade. The layout budget shrinks the card down to 68 and the rail rows before dropping anything.
Whole column: top aligned, max size.

### 2.3 Tiles (`NotchActionTile`)
Button: VStack spacing 6: **icon circle 40x40** (symbol 17 pt medium; fill white 7.5%, glyph white 85%; active: filled with accent, glyph per accent) + **label** (11 pt medium white, 2 lines max centred, min scale 0.85, label slot height 28). Cell 76 x 74, padding horizontal 4, hit radius 14. Tooltip = title; active tile is "selected" for AX. A live **reading** (timer clock, calendar start) replaces the first label line (11 semibold monospaced, min scale 0.7, rolling digits) and the title moves one line down (11 medium white 70%, optional shorter `compactTitle`).
Accent table:
| Accent | Fill | Glyph |
|---|---|---|
| selection | white | black |
| alert | red | white |
| awake | yellow | black |
| timer | orange | black |

Tiles:
| Item | Symbol (Segoe) | Title | Action / active state |
|---|---|---|---|
| keepAwake | cup.and.saucer(.fill) | "Keep awake" | toggle; active=yellow while a session runs; filled cup when active |
| microphone | mic.fill / mic.slash.fill (E720 / slash) | "Mute microphone" / "Unmute microphone" | toggle global mic mute; active=red when muted |
| screenshot | camera.viewfinder (draw a viewfinder, or E722) | "Screenshot" | capture (closes island first via `perform`) |
| recording | record.circle / stop.circle.fill | "Screen recording" | toggle recorder; active red |
| speedTest | speedometer | "Run speed test" | opens System -> network detail |
| panel | bubble.middle.top | "Open app panel" | opens the app panel |
| mixer | speaker.wave.2 (E767) | "Volume mixer" | select Mixer page |
| commandBar | command | "Command Bar" (page title) | opens the Command Bar |
| scratchpad | note.text (E70B) | "Scratchpad" | opens scratchpad |
| timer | timer (E916) | "Timer" / phase name when running | select Timer page; while a session exists: reading = clock, title = phase ("Focus", "Short break" …) with compact "Timer", glyph: `checkmark` (done) / `pause.fill` (paused) / `stopwatch` / `timer`; accent timer |
| calendar | calendar (E787) | next event title or "Calendar" | select Calendar page; reading = next start ("3:00 PM" today, "Sun 3:00 PM" later) |
| music | (card, not a tile) | | |
Screen-capture strings: screenshot title "Screenshot", recorder page title "Screen recording", speed-test run label "Run speed test".

### 2.4 Music card (`NotchMusicControlsView`)
HStack spacing 12, horizontal padding 12, height as given, radius 18 `NotchControlSurface`.
- Cover button (size `max(40, height − 24)` = 56 at 80) -> with nothing playing and a preferred player installed it opens that app; otherwise selects the Music page. Tooltip "Now Playing" or "Open <App>".
- Titles button (selects the Music page): title 13 semibold; subtitle 11 secondary = artist/album, or "Could not change playback." orange, or (idle) the music hint "Your music and playback controls appear here."; while awaiting: title "Now Playing", empty subtitle; idle title "Nothing playing".
- Transport (compact: prev 16, play 22, next 16 semibold, height 36, spacing 12) at the end of the line; if the width is too small (titles would be narrower than 80) the transport drops under the titles (stack spacing 4, artist shown only when height ≥ 84).
- When idle with a preferred player the three buttons open the app (idle transport); when nothing installed show nothing.

### 2.5 Volume level (`NotchAudioControls`) 28 high, spacing 6
`[mute button 18x18][NotchLevelSlider h24 thickness 8, easing][chevron chooser 16x24]`
- Mute: `speaker.wave.2.fill` (E767) white / `speaker.slash.fill` (E74F) **red** when muted; 12 pt medium; tooltips "Mute"/"Unmute"; disabled when unreadable.
- Slider value = muted ? 0 : system volume (0...1); changes call the system volume; the island's own top-row volume indicator stays quiet (`noteOwnLevelAdjustment`: do not show the volume HUD notice for changes made here).
- Chooser (chevron.down E70D, 9 pt semibold secondary): menu of output devices that can be default (checked = current), plus separator and "Volume mixer" if the Mixer module is shown. Tooltip = device name or last switch error ("Could not switch: %@"). When volume unreadable: "Output unavailable" (10 pt secondary).

### 2.6 Brightness level 28 high
`[sun.max.fill (E706) 12 pt medium, 18 wide][slider 8/24][display chooser if > 1 display]`. Display = last display changed by brightness keys, else built-in, else first. Tooltip = display name or "No display found." When the Brightness feature is not enabled: setup row `sun + "Brightness" (12 semibold) + "Settings…" button (11 secondary)`; narrow variant `sun + "Settings…"`.

### 2.7 Keyboard light level
Glyph button (`light.max`/`light.min`, 18x18) toggles the backlight (restores previous level), slider thickness 8; unavailable text: "Keyboard backlight control is unavailable on this Mac." -> "…on this PC.". Title "Keyboard light".

### 2.8 Behaviour
- Levels and tiles update live (volume/brightness events, timer tick every second, calendar every minute).
- A change made with a control on this page does not trigger the volume/brightness HUD.
- `perform { }` closes (or hides) the island before running an action that needs the screen (screenshot, recorder, command bar, speed test opens a page).
- Page entry refreshes brightness and keyboard light.

### 2.9 Windows mapping
- Volume: `IAudioEndpointVolume` (master scalar, mute, `OnNotify` events). Output list: `MMDeviceEnumerator`; default switch via existing `PolicyConfigClient`.
- Brightness: built-in panels WMI `root\wmi:WmiMonitorBrightness/Methods`; external monitors DDC/CI via `dxva2` (`GetMonitorBrightness`/`SetMonitorBrightness`) — reuse `Features.System/Display/DisplayDevices.cs`. Brightness keys have no global notification on Windows; poll or react to WMI events when the island is open.
- Keyboard backlight: no general API; use `Windows.Devices.Lights.Lamp`/`LampArray` when a supported keyboard exposes it, else show the unavailable text and keep the item hidden by default (matches default hidden).
- Mic mute / keep awake / screenshot / recorder / speed test / command bar / scratchpad: wire to the existing Trayloom features (MicMuteFeature, KeepAwakeFeature, Screenshot, Command bar, Scratchpad); tiles are disabled (hidden) when the feature is not installed.
- Rail layout: reusable `RailPanel` with the three functions `railCapacity`, `railRows`, `railGap` from `NotchSupport` (listed in the source) — implement as pure functions: `capacity = max(1, floor((width+spacing)/(item+spacing)))`, `gap = spacing if columns fit, else (width − columns·item)/(columns−1) if it fits at gap ≥ least else scroll`.

### 2.10 Gap vs Trayloom (Controls)
No Controls page at all in `Trayloom.Features.Island` (sections registered: music, timer, calendar, downloads). Everything in Part 2 is new. The port's existing Volume mixer/brightness features exist elsewhere (`Features.System/Audio`, `Display`) and should be reused as data sources.

---------------------------------------------------------------------------------------------------

## PART 3. MIXER PAGE ("Volume mixer")

Source: `UI/Notch/NotchMixerView.swift`, `Services/Audio/MixerRoutingSupport.swift`, `UI/MenuPanel/MixerSection.swift` (`MixerOptionsControls`).

### 3.1 Layout
```
band (header):  leading = output device menu (label "<device> v")     trailing = options icon button
page:  [Output fader 72][Mic fader 72][1pt divider white 12%] [ app faders rail, 96 each, scrolls sideways ]
```
- HStack alignment top, spacing 10. Master column width 72; microphone column 72 only if the input device reports a volume; so left block = 72 or 72·2+10.
- Fader height `H = max(104, page height)`. Fader track height = `min(160, max(32, H − 32 − 28 − 12))`.
- App rail: `NotchRail(rows 1, itemWidth 96, width = pageWidth − leftBlock − 21)`; items centred when they fit, else horizontal scroll with edge fade.
- Header (top row) items: **output device menu** (`NotchBandMenuLabel`: 12 pt medium, white 85%, optional glyph, chevron.down 9 pt, h 28, horizontal padding 7, radius 9; tooltip = device or error) and **options toggle** icon button (`slider.horizontal.3` E9E9 -> `xmark` when open; titles "Options" / "Close"; selected when open).

### 3.2 Master (system output) column (`NotchMasterFader`)
VStack spacing 6, top aligned, height H:
1. mute round button 32x32 (hit radius 14, glyph 15 pt medium; `speaker.wave.2.fill`/`speaker.slash.fill`, red when muted); AX "Mute"/"Unmute".
2. vertical NotchLevelSlider 40 wide x trackHeight, thickness 28, label "Output".
3. editable percent: text "NN%" (12 pt medium monospaced, rolling numeric) 28 high; **click to type** a value (accepts "150" or "150%"; max 100 for output). Editor id per control; only one editor open at a time (`editingVolumeID`), closed when options toggle.
Unavailable: "Output unavailable" (10 pt, centred).

### 3.3 Microphone column
Same shape: round button (`mic.fill` E720 / `mic.slash.fill`; red when muted) calling the global mic mute (plain non-interactive icon when the mic-mute feature is off); slider (label "Microphone") disabled while muted; percent field (max 100).

### 3.4 App column (`NotchAppFader`)
VStack spacing 6, height H:
- Header 32 high: app icon 28x28 (centred); trailing "…" menu button (`ellipsis` E712, 12 pt semibold white 70%, 24x28, tooltip "Actions", AX "Actions: <app>"); leading pin glyph `pin.fill` (E718) 9 pt secondary when pinned (AX "Pin to Front"). Green 7-pt dot with 1.2 pt black ring at the icon's bottom-right while the app is playing. Route badge bottom-left when the app has its own output: 12-pt black circle with `arrow.triangle.branch` (7 pt bold, white; orange `exclamationmark.triangle.fill` if the device is gone), tooltip route description ("Using default until this device returns." / device name / "Output unavailable").
- If the app **manages its own audio** (bypassed): caption "This app manages its own audio." (9 pt secondary, centred) — or "Volume control is off to prevent echo when sharing audio." for call apps; no fader.
- Else: vertical fader 40 x trackHeight, thickness 28, range 0...2 (200%), tint white / **orange when > 100%**, marker at 1.0, label = app name, valueLabel "NN%"; then an editable percent row (28 high; text orange + `bolt.fill` 7 pt bold when boosting) with an overlay left "reset" button (`arrow.counterclockwise` 12 pt, 28x28, secondary/orange, hidden and disabled at 100%, tooltip "Reset to 100%") and right "mute" button (speaker glyph 12 pt, red when volume ≤ 0.001, AX "Mute"/"Unmute").
- Tooltip on the column: `"<app name>\nHold Command and drag to reorder"` -> Windows "Hold Ctrl and drag to reorder"; for call apps the tooltip is "Volume control is off to prevent echo when sharing audio." instead.
- Context menu (also the "…" menu): **Pin to Front / Unpin** (pin / pin.slash), **Move Left**, **Move Right** (disabled at ends); for conferencing apps a toggle **Allow volume control**; divider; submenu/picker **Choose output**: "Default" (system default selection), each device ("<name> (current)" for the default), "Output unavailable" when the saved device is missing, divider + "Choose AirPlay speaker…" if an AirPlay device exists (not on Windows); divider; **Reset to 100%** (disabled at 100%); divider; **Hide from the list**.
- Reorder: Ctrl-drag a column horizontally (pinned and unpinned stay in their own groups); keyboard/AX actions "Move Left"/"Move Right".
- App list = `arrangement.ordered(apps)` then filtered by `shouldShowApp`: when `mixerHideInactiveApps` is on, show only apps that are playing, or whose volume ≠ 100%, or that have a custom output.

### 3.5 Options panel (header toggle)
Replaces the desk with a vertical scroll list (spacing 8, trailing padding 4):
1. Row "Output" (icon `speaker.wave.2`, label 11 pt secondary, device menu width 200, 22 high) — with messages 9.5 pt secondary: "No outputs found" (icon speaker.slash) when none; "Could not switch: %@" (warning) on error.
2. Row "System sounds" (icon `bell`) with its own device menu; message "No outputs found" (bell.slash).
3. Row "Microphone" (icon mic/mic.slash) with menu: "Default" + inputs + "Microphone unavailable" (checked) if the saved one is missing; below it a level row (mute button 18x18, slider h22 label "Microphone", percent field) when volume readable; messages: "No microphones found" / "Using default until this microphone returns." / "Could not switch: %@".
4. Divider white 12%, then the panel options (`MixerOptionsControls`): checkbox "Hide inactive apps" (key `mixerHideInactiveApps`, default false); "Lower volume when headphones disconnect" (key `mixerLowerVolumeOnHeadphonesDisconnect`, default false) with caption "Adjusts output when wired or Bluetooth headphones disconnect." and, when on, stepper "Volume after disconnect" 0...100 step 5 (default 25, key `mixerHeadphonesDisconnectVolumePercent`) with value "NN%"; "Use finer volume steps" (`preciseVolumeRollerEnabled`, default false) caption "Turns volume wheels and keys into smaller system volume steps."; Output switcher (when that feature is on); Audio priority list (when on); and a footer to choose which hidden apps appear ("Apps in the list", "All", "Hidden").

### 3.6 States
- Mixer unsupported (macOS < 14.4): NotchEmptyView(speaker, "Available on macOS 14.4 and later") — **not needed on Windows**.
- Permission missing (macOS audio capture): glyph `waveform.badge.exclamationmark` + "To adjust per-app volume, allow “Screen & System Audio Recording” in System Settings. Audio is never recorded." + button "Open System Settings…" — **not needed on Windows** (no permission); never show.
- No audio apps: NotchEmptyView(speaker.wave.2, "Apps that use audio show up here").
- Persistence: `mixerAppArrangement` (JSON string `{"order":[ids],"pinned":[ids]}`, default ""), per-app saved volumes keyed by persistence id (bundle id on Mac; use exe path / AUMID on Windows), hidden apps list.

### 3.7 Windows mapping
- Sessions: `IAudioSessionManager2.GetSessionEnumerator()` on the default render endpoint; subscribe `IAudioSessionNotification` for new sessions and `IAudioSessionEvents` for volume/state; one row per distinct process (merge helper processes by root/parent exe). Active/"playing" dot = `AudioSessionState.Active` or `IAudioMeterInformation.GetPeakValue() > 0.001`.
- Volume: `ISimpleAudioVolume.SetMasterVolume(0..1)` / `SetMute`. **Impossible: above 100%** — WASAPI session volume is capped at 1.0 and Windows has no per-app gain stage without an audio processing object or re-routing through a custom loopback; so fix `maxVolume = 1.0` (percent field max 100, no orange boost state, marker tick not needed). The fader still has the 100% reset affordance only if a saved volume differs from 100%.
- Per-app output device: undocumented `IAudioPolicyConfigFactory::SetPersistedDefaultAudioEndpoint(pid, flow, role, deviceId)` (used by Windows Settings -> App volume and device preferences). Read with `GetPersistedDefaultAudioEndpoint`. Offer the "Choose output" picker with this; if the COM interface is not available on the OS build hide the picker.
- Icons: exe icon / `IAudioSessionControl2.GetIconPath`. Name: process main window title / FileDescription.
- System-sounds output: Windows has no separate device for system sounds (only default vs default communications). Omit the "System sounds" row or map it to the *default communications device*; recommended: omit.
- AirPlay speaker picker: not applicable, omit. "Screen sharing / call app echo protection" toggle ("Allow volume control"): implement only if an equivalent list of conferencing apps is maintained (Teams, Zoom, Discord, Meet in browsers) — optional.
- Microphone column: `IAudioEndpointVolume` of the default capture endpoint; mute via the existing mic-mute feature.
- Ctrl-drag reorder in WPF via mouse capture; or buttons in the menu only.

### 3.8 Gap vs Trayloom (Mixer)
No mixer section in the island. The existing `MixerWindow.cs` (flyout) + `VolumeMixerFeature.cs` + `AudioDevices.cs` provide sessions/devices: reuse the session model, add the island page with the vertical-fader layout above, the arrangement/pin/hide persistence and the options panel.

---------------------------------------------------------------------------------------------------

## PART 4. SYSTEM PAGE

Source: `UI/Notch/NotchSystemView.swift`, `NotchLayout.systemRowRanges`, `Core/Localization.swift` (labels).

### 4.1 Layout
A grid of metric **cards**; clicking one opens its detail page inside the island (`MetricDetailKind`; detail pages are specified with the monitor feature).
- Card: **72 high**, min width **128**, padding horizontal 12, radius 18, `NotchControlSurface` (white 6.5% fill, hover scale 1.022). VStack spacing 6, left aligned:
  1. Label with icon: 10 pt medium secondary, 1 line (`Label(title, systemImage)`).
  2. Value: 22 pt medium **rounded** monospaced digits (15 pt when a detail line exists), rolling-number transition 0.25 s, 1 line, min scale 0.75; placeholder "…" while unknown.
  3. Either a detail line (11 pt secondary monospaced) or a `NotchMeter` (height 5) in white, **orange when the reading wants attention**.
- Grid: `inset = ceil(width × 0.015) + 1` around the grid (room for the hover scale); per row capacity `max(1, floor((width − 2·inset + 10) / (128 + 10)))`; rows balanced (`count / rows` each, remainder to the first rows); spacing 10 both ways. If total height > page height the grid scrolls vertically.
- Empty (no cards): NotchEmptyView(`gauge.with.dots.needle.50percent` -> E9D9, "Sensors unavailable on this Mac" -> "Sensors unavailable on this PC").

### 4.2 Cards in order (shown only if the matching monitor feature is on)
| Card | Title | Icon (suggest) | Value | Detail / meter | Attention |
|---|---|---|---|---|---|
| CPU | "CPU" | cpu E950 | NN% | meter = usage | usage ≥ 85% |
| GPU | "GPU" | rectangle.connected.to.line.below (draw) | NN% | meter | ≥ 85% |
| Memory | "Memory" | memorychip EEA1 | used/total % | meter | ≥ 85% |
| Battery | "Battery" | battery.100percent(.bolt) E83F / E83E | NN% | meter = charge | not plugged in and ≤ 20% |
| Network | "Network" | network E839 | "↓ <rate>" | detail "↑ <rate>" | n/a |
| Disk | "Available" (capitalised `diskAvailable`) | internaldrive EDA2 | free bytes of the system drive (`MetricFormat.diskBytes`) | meter = free fraction | free < 10% |
| Power | "Power" | powerplug.fill | system watts | detail adapter watts when plugged | n/a |
| Fan | "Fan Control" | fanblades | "<rpm> RPM" (first fan) | — | only if a fan is reported |
| Connected devices | "Connected Devices" | cable.connector | count | — | n/a |
Battery card only if the machine has a battery; Power card needs the Power monitor; Network unit comes from `networkSpeedUnit` (bytes default, or bits).
Percent formatting: `Int(clamp01(v) × 100 rounded) + "%"`.

### 4.3 Windows mapping & gap
- Reuse `Features.System/Monitor/SystemSampler.cs` (PDH counters for CPU/disk/network, `GlobalMemoryStatusEx`, `GetSystemPowerStatus`, GPU via PDH "GPU Engine" counters). Power watts: battery `IOCTL_BATTERY_QUERY_INFORMATION` discharge rate; adapter watts usually unavailable -> omit detail. Fans: not readable on Windows without vendor APIs -> omit card. Connected devices: count of connected USB/Bluetooth devices via `Windows.Devices.Enumeration`/`SetupAPI`.
- Gap: there is no System section in the island registry; the monitor flyout (`MonitorFlyout.cs`) is a separate window. Need the island page + card detail navigation.

---------------------------------------------------------------------------------------------------

## PART 5. CAPTURE CONTROLS, CAMERA MIRROR, OTHER-DISPLAY MIRROR, KEEP AWAKE STRIP

### 5.1 Capture controls (`NotchCaptureControlsView`)
Shown inside the island while the user is choosing what to capture (screen-selection overlay is active). Pure controller UI; the overlay owns the capture.
Layout: VStack spacing 12, top padding = header top.
1. **Header row** (height 28 or the island header height; horizontal padding 28): left title "Screen capture" (12 pt semibold); right button group spacing 6: optional "repeat last region" chip (label "R" with `rectangle.dashed`, 10 pt semibold rounded, secondary, h 26, radius 7, bg white 6%; narrow variant only "R" 28 wide), then `chevron.up` icon button (title "Collapse") and `xmark` icon button (title "Close"). Keys: **R** repeat last region.
2. **Tool tiles** row (spacing 6, horizontal padding 18): one `NotchActionTile` per available tool (only the selected one if the capture menu is hidden): Screenshot `camera.viewfinder` "Screenshot" (key 1), Screen recording `record.circle` "Screen recording" (key 2), Text `text.viewfinder` "Copy text from screen" (key 3), Color `eyedropper` "Color picker" (key 4). Active tile = selected tool.
3. For recording: audio options row (HStack spacing 16, small switches, tint green): "Mac sound" (-> "PC sound") and "Microphone".
Total window height = header top + header + 12 + 74 + 16 (+ audio row). Focus tracking keeps it from collapsing while a control has keyboard focus (`scheduleCaptureControlsCollapse`).
Windows: the capture selection overlay (Screenshot/Recorder/OCR/Color picker features) calls into the island host; show the same panel above the overlay; keys 1-4 and R forwarded; Esc closes. Setting `notchCaptureControls` (default true) "Show screen capture controls in the Dynamic Island". Gap: not present in the port.

### 5.2 Camera mirror page (`NotchCameraView`)
- Not started: HStack (spacing 16) padding 12: tile 56x56 radius 18 white 5% with `web.camera` (E722) 30 pt light secondary; VStack spacing 8: text "Open a live mirror here. The camera stops when you leave this view." (callout secondary) + prominent button "Open camera".
- Started: live preview fills the page: size `min(W, H·16/9) x min(H, W·3/4)` (16:9 crop on wide strips, 4:3 on narrow tall pages), mirrored horizontally (a mirror shows the flipped image).
- Header row (island top row): camera chooser when running and > 1 camera (label = current camera name, menu of cameras, tooltip current; default label "Camera"); stop button "Stop camera" (icon `video.slash` when the row is narrow, otherwise prominent pill).
- Leaving the page (or closing the island) **stops the camera**. Error text "The camera could not start. Try opening it again." Module title "Camera mirror" (key `w`). Setting `notchCameraEnabled` default true.
- Windows: `Windows.Media.Capture.MediaCapture` + `MediaFrameReader` (or the existing `Features.Tools/CameraPreview` pipeline) hosted in an `Image`/D3DImage inside the page; permission: Windows Privacy -> Camera ("ms-settings:privacy-webcam"); light on while running is OS-driven. Gap: camera module is not in the island registry.

### 5.3 Other-display mirror (`NotchMirrorView`)
A second island window per extra display showing the closed island content (activity strip or resting strip/capsule) with no hit testing; a click on it brings the real island there, open. Equalizer bars on all copies share one wave origin; when the island moves between displays the wave pose is handed over. Setting: "Show on all displays". Windows: one transparent topmost click-through window per monitor; clicking activates the primary island on that monitor. Gap: single window only in the port.

### 5.4 Keep Awake closed strip (`NotchKeepAwakeStrip`, `NotchKeepAwakeSupport`)
Setting `notchKeepAwakeActivity` (default false): "Show “Keep awake” in the closed island".
- Left: `cup.and.saucer.fill` in **yellow** (size = strip icon size `min(20, height − 10)`), right: time left for a timed session or `infinity` (∞) for an open-ended one; both open the Controls page where the tile is.
- Reading format: whole minutes rounded **up** (min 1): `<60 min` -> compact "45m" (the timer's narrow-unit format), `≥ 60` -> `"1h35"` (`%dh%02d`). Updates once a minute at the minute boundary (`tickStart = end − minutesLeft·60`).
- Wing hides glyph when wing < 28, reading when wing < 42.
- Accessibility: label "Keep awake", value "Ends in <remaining>" / "Active until you turn it off" (`keepAwakeEndsIn`, `keepAwakeUntilDisabled`) or the automation condition text.
- Windows capsule: cup glyph (E7ED? use the KeepAwake feature's glyph) + reading in yellow, same text rules. `SetThreadExecutionState`/PowerRequest powered by the existing `KeepAwakeFeature`. Gap: not wired to the island compact info (port registers compact info for the timer only; `IslandHub.SetCompact` takes one owner at a time).

---------------------------------------------------------------------------------------------------

## PART 6. CALENDAR

Sources: `UI/Notch/NotchCalendarView.swift`, `NotchCalendarMonthView.swift`, `NotchCalendarStrip.swift`, `Services/Notch/NotchCalendarService.swift`, `NotchCalendarSupport.swift`, `Core/NotchCalendarStrings.swift`.

### 6.1 Strings (English, verbatim)
title "Calendar" ; description "Browse the month and your upcoming appointments in the Dynamic Island." ; permission "Read your calendars to show upcoming appointments. Events stay on this Mac." ("this PC") ; allow "Allow Calendar Access" ; denied "Allow calendar access in System Settings to see your appointments." ("…in Settings…") ; settings "Open System Settings" ("Open Settings") ; empty "No upcoming appointments" ; next "Up next" ; ongoing "Happening now" ; allDay "All day" ; untitled "Untitled event" ; openCalendar "Open Calendar" ; week "Next 7 days" ; today "Today" ; requestFailed "Could not request calendar access. Please try again." ; previousMonth "Previous month" ; nextMonth "Next month" ; previousWeek "Previous week" ; nextWeek "Next week" ; month "Month" ; emptyDay "No appointments on this day" ; hasEvents "Has appointments" ; countdown "Event countdown" ; countdownHint "Show every timed event in the closed island before it starts. To count down to only some, turn this off and right-click them on the island’s Calendar page. Titles may appear in screen captures." ; countdownLead "Time before event" ; addCountdown "Add Countdown" ; removeCountdown "Remove Countdown" ; timeLeft "Time left in current event" ; timeLeftHint "Show a timed event in progress in the closed island during the hour before it ends. Its title may appear in screen captures." ; weekNumbers "Week numbers" ; weekNumber "Week %d" (VoiceOver only) ; calendars "Calendars shown".

### 6.2 Settings
| Key | Type | Default | Meaning |
|---|---|---|---|
| notchCalendarEnabled | bool | true | module on |
| notchCalendarCountdown | bool | false | countdown for every timed event in the closed island |
| notchCalendarCountdownLead | int minutes | 60 | lead time; presets 15, 30, 60, 120; allowed range 5...240 (stored values outside the presets stay selectable) ; menu label formatted as duration (e.g. "1 hr") |
| notchCalendarTimeLeft | bool | false | event under way counts down to its end during the last 60 min |
| notchCalendarWeekNumbers | bool | false | numbers each week row in the grid |
| notchCalendarExcluded | [string] | [] | calendar ids hidden (new calendars start shown) |
| notchCalendarChosenCountdowns | dict key -> end date | {} | events chosen via the context menu; entries are forgotten when the event ends; only identifiers are stored, never text |
Settings tab (Calendar): status line (permission text, or green "Granted"), buttons "Allow Calendar Access" + "Open System Settings", Divider, toggle "Event countdown" (+ hint), menu "Time before event", toggle "Time left in current event" (+hint), toggle "Week numbers", then per-account list "Calendars shown" with a checkbox per calendar (grouped by account/source, accounts and calendars sorted by name, colour dot) when access is granted.

### 6.3 Layout (open island)
Header (top row): left = month title (`MMMM yyyy` wide; falls back to abbreviated `MMM yyyy` if too narrow), 13 pt semibold; if the page is not the tall layout it is a button (tooltip/hint "Month", h 28, padding 7, selected plate white 12% radius 9) that toggles the month grid. Right = navigation: `chevron.left` (E76B) title "Previous week"/"Previous month", **Today** (`circle.circle`: draw a ring with centre dot; title "Today"), `chevron.right` (E76C) "Next week"/"Next month", spacing 0 inside; plus `arrow.up.forward.app` (E8A7) "Open Calendar" when the row has ≥ 4 buttons of room (28 × 4 + 6).

Three layouts chosen by page size (`showsMonth = height ≥ 300 && width ≥ 420`):
1. **Tall**: `HStack(top, spacing 16)`: month grid in a vertical ScrollView, width **196**; 1-pt divider white 12%; agenda column (spacing 12): agenda header + agenda.
2. **Short, strip**: `VStack(spacing 10)`: week strip (height 52) then agenda.
3. **Short, month**: the month grid fills the page (after tapping the title); choosing a day returns to the strip with that day selected; Esc also returns.

#### Month grid (tall)
`LazyVGrid`, 7 flexible columns (plus a fixed 18-pt week-number column when enabled), vertical spacing 4. Header row: 7 very-short weekday names (9 pt medium white 45%, height 18) starting at the system first weekday. Then 42 day cells (6 rows from the first week containing day 1).
Day cell: height 30, VStack spacing 2: 24x24 circle with the day number (11 pt; bold if today or selected). Colours: today = filled accent (255,92,100) with white number; selected (not today) = filled white with black number; selected+today = accent + white 1.5-pt ring (80%); other days white (100% in month / 40% outside). Under it up to **3 dots** (3 pt, spacing 2, distinct event colours in order), dot row height 3. Hit radius 8, no lift. Tooltip/AX = full date "Monday, 4 November 2026"; AX value "Today" / "Has appointments".
Week number cell: 9 pt medium monospaced white 45%, laid out like a day (above the dot row); AX "Week %d". Week number = locale `weekOfYear` (rows start on the first weekday).

#### Week strip (short)
7 cells spacing 4, each h 52: weekday initial (9 medium white 45%), 26 circle number (12 pt; same colour rules), dots row. Tapping the selected day returns to "Next 7 days" (clears selection).
#### Month grid (short)
Weekday row height 14 + 6 rows each `min(30, max(16, floor((H − 14)/6)))`, circle = min(24, row − 3), number `min(11, circle × 0.62)`, radius 6, spacing 0.

#### Agenda header (tall layout only)
`minHeight 32`; text 13 pt semibold up to 2 lines: "Next 7 days" or the selected day (`EEE d MMM` abbreviated); when a day is selected a `calendar.badge.clock` icon button (title "Next 7 days") returns to the week.

#### Agenda (`appointmentList`)
Scroll view. Loading: spinner (small) centred, min height 72. Empty: icon `calendar.badge.checkmark` 26 pt light white 45% + text 12 pt white 65% ("No upcoming appointments" for the week, "No appointments on this day" for a selected day) + pill "Open Calendar" when the nav row has no room; padding horizontal 12, min height 72.
Groups (spacing 14): per day with events (next 7 days from today when nothing is selected, using events whose end > now; or just the selected day): day label (11 pt medium white 50%, 1 line): "Today" (white) or abbreviated weekday, then `d MMM`. Under it event cards (spacing 7).
**Event card**: VStack spacing 4, padding leading 12 + overall 10, radius 11, bg = event colour at 10% (20% when ongoing), border white 5% (16% ongoing; 50% in increased-contrast), 0.75 pt; 3-pt accent bar (radius 2, event colour) at the left edge.
- Badge line (only for ongoing or the single "next" timed event): "Happening now" (mint) or "Up next" (white 70%), 9 pt semibold.
- Title 13 pt semibold (white; 65% when ended); empty -> "Untitled event"; wraps.
- Time line 11 pt monospaced white 75%: all-day -> "All day"; same-day timed -> `h:mm a · h:mm a` (separator " · "); multi-day -> `d MMM h:mm a → d MMM h:mm a`; a `timer` glyph follows when this event has a chosen countdown.
- Calendar name 10 pt white 50%, 1 line. Location line with `mappin` (E81D) 10 pt white 60%, 1 line.
Card is a button (radius 11, no lift) -> opens the event in the calendar app; tooltip "Open Calendar". Context menu (and VoiceOver action) on timed events that have not started, when "countdown for every event" is off: "Add Countdown" / "Remove Countdown".
Selecting the countdown from the closed island opens this page and scrolls the event to the centre once loading ends (`revealing`).

### 6.4 Data rules (`NotchCalendarService/Support`)
- Read interval: today 00:00 -> +7 days; when a month is shown: the full 42-day grid span (extended to cover the next 7 days when it is the current month). A second read of the current week is made when the visible month does not contain the countdown window and countdown/time-left/chosen events exist.
- Event filter: skip cancelled events and events where the current user declined; need start, end, identifier; dedupe by `id = identifier + ":" + start`; order by start, end, id; require end > start.
- `events(on: day)`: overlap with the local day (end exclusive); all-day first, then start, then id.
- `next(events, now)`: first non-all-day with end > now.
- Refresh triggers: store changed, app activated, system clock/time zone changed, day changed, permission change, and a timer at `min(next event start/end, midnight, now+15 min)` (+countdown transition time).
- Refresh when `focus` month changes. `calendar.showMonth(month)` while the page is open; `nil` when leaving.
- Permission states: `fullAccess` shows the page; otherwise the **permission card**: VStack spacing 10: `calendar.badge.clock` 28 pt, message (denied/restricted -> "Allow calendar access in System Settings to see your appointments." else "Read your calendars to show upcoming appointments. Events stay on this Mac."), buttons (bordered): denied -> "Open System Settings" ; otherwise "Allow Calendar Access" (disabled while requesting, spinner) which also opens the page; failure caption orange "Could not request calendar access. Please try again.".

### 6.5 Closed-island countdown strip (`NotchCalendarStrip`)
Visible when `NotchCalendarCountdown` exists: next timed event start within `leadTime` (setting or chosen event) or, with time-left on, an ongoing event in the **last 60 min** (only after it started). Choose the nearest target; ties prefer the ongoing event. Shown/hidden exactly at `start − lead` / `start`, and `max(start, end − 60 min)` / `end` (timer scheduled).
Capsule/full-row look: `[● dot 6pt with white 50% 0.5pt ring][title 11 pt semibold, tail truncation, flexible][clock 13 pt medium monospaced]` (spacing 6; with a companion activity the dot+clock pair sits at the right end). Clock format `m:ss` (minutes can exceed 59, e.g. `75:00`; reserve width of `countdownShape(lead)` = max(2, digits of lead minutes) zeros + ":00"), rolls digits only above the seconds, min scale 0.8; colour mint when ongoing else white. Wide layouts add the time text `· 3:00 PM` (start) or `→ 3:00 PM` (end), 11 pt medium white 55%. Ticks every 1 s. Click opens the island on Calendar and reveals the event. AX: label "Up next: <title>" / "Happening now: <title>", value e.g. "5 minutes, 10 seconds", hint "Open Dynamic Island".

### 6.6 Controls tile
Title = next event title (or "Calendar"), reading = start time (`h:mm a` today, `EEE h:mm a` later) for the first non-all-day event with `start > now` within the week read; `TimelineView(.everyMinute)`; opens the Calendar page.

### 6.7 Windows mapping
Sources of events (implement as providers, union of enabled ones):
1. **Windows calendar store** (`Windows.ApplicationModel.Appointments.AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)`): works for packaged (MSIX) apps; the port already tries this with a 4-s timeout and silently fails for unpackaged apps. For the permission card: "Allow Calendar Access" -> `RequestStoreAsync`; "Open Settings" -> `ms-settings:privacy-calendar`. Fields: Subject, StartTime, Duration, AllDay, Location, Recurrence expanded by `FindAppointmentsAsync` (`FindAppointmentsOptions` with `MaxCount`), calendar name/colour from `AppointmentCalendar.DisplayColor`, `CalendarId`. Declined: `Invitees` where `Role=…`/`ResponseType`.
2. **Outlook (classic) COM**: `Outlook.Application.GetNamespace("MAPI").GetDefaultFolder(olFolderCalendar).Items` with `IncludeRecurrences = true`, `Sort("[Start]")`, `Restrict("[Start] <= '…' AND [End] >= '…'")`; skip `Status`/busy cancelled; colours via category colour map.
3. **ICS files / subscription URLs** (settings list): parse with iCalendar library (recurrence expansion, exdates); poll URLs every 15 min with `If-Modified-Since`.
Open event: Windows store path -> `AppointmentManager.ShowAppointmentDetailsAsync(localId, instanceStart)` (needs window interop); Outlook path -> `AppointmentItem.Display()`; ICS/other -> launch the default calendar (`outlookcal:` URI, or `ms-outlook:`), falling back to opening the ICS file.
"Calendars shown" list = union of provider calendars grouped by source (account) with colour dots.
Locale: month/day names and first weekday from `CultureInfo`/`GetFirstDayOfWeek`; the replica follows the **app language** for names and the region for 12/24 h (port currently uses `CurrentCulture`).
Timer for refresh: `DispatcherTimer`/`System.Threading.Timer` at the computed next date; `SystemEvents.TimeChanged`, `SystemEvents.DisplaySettingsChanged` not needed; day change via midnight timer.

### 6.8 Gap vs Trayloom (Calendar)
`CalendarFeature.cs` is a single layout: month grid (left, flexible) + 150-px right panel (big day number, weekday, month/year, relative text "In N days · week N", up to 3 events with HH:mm, "No events"). Differences to remove/add:
- No 196-px month + agenda split, no week strip/short-month grid, no agenda of the next 7 days, no event cards (colour bar, badges, location, calendar name), no day selection semantics (second tap on selected day), no event dots under days, no week-number column, no navigation set (prev/today/next/open), no header title button, no Escape layer.
- Port settings `weekStart` (auto/monday/sunday) and `showEvents` are not in the replica; replace by the keys in 6.2 (week start follows the system).
- Port reads events once per 5 min for -7...+60 days and gives up for unpackaged apps; replica needs the multi-provider service, live refresh triggers, declined/cancelled filtering, exclusions, chosen countdowns.
- No closed-island countdown (`CompactInfo` for calendar missing), no tile on Controls, no permission card, no empty states with the specified strings.
- Strings: "Today"/"Tomorrow"/"Yesterday" relative text and "No events" are not in the replica; use the strings in 6.1.

---------------------------------------------------------------------------------------------------

## PART 7. TIMER

Sources: `UI/Notch/NotchTimerView.swift`, `NotchTimerStrip.swift`, `NotchTimerRuler.swift`, `Services/Notch/NotchTimerService.swift`, `NotchTimerSupport.swift`, `NotchTimerAlert.swift`, `Core/NotchActivityStrings.swift`.

### 7.1 Strings (English)
Timer "Timer" ; timerDescription "Timers, a stopwatch and focused work sessions in the Dynamic Island." ; Pomodoro "Pomodoro" ; Stopwatch "Stopwatch" ; Focus "Focus" ; Short break "Short break" ; Long break "Long break" ; pomodoroHint "Start each phase when you are ready. The cycle ends after the last focus session." ; totalSessions "Total sessions" ; longBreakInterval "Sessions between long breaks" ; sessionProgress "Session %d of %d" ; pomodoroFinished "Pomodoro complete" ; minutes "Minutes" ; start "Start" ; resume "Resume" ; finished "Time is up" ; soundEnabled "Play sound when time is up" ; hideTimerCountdown "Hide timer countdown" ; Cancel (`mediaCancel`) "Cancel" ; Done (`supportIntroDoneButton`) "Done" ; Pause (`actionPause`) "Pause".
Phase titles: timer "Timer", stopwatch "Stopwatch", focus "Focus", shortBreak "Short break", longBreak "Long break".

### 7.2 Settings
| Key | Type | Default | Notes |
|---|---|---|---|
| notchTimerEnabled | bool | true | module on |
| notchTimerMode | "timer" / "pomodoro" / "stopwatch" | timer | last used mode |
| notchTimerMinutes | int | 15 | timer length; clamped 1...180 |
| notchTimerSoundEnabled | bool | true | "Play sound when time is up" |
| notchHideTimerCountdown | bool | false | "Hide timer countdown": hides the running countdown in the closed island; a finished timer still shows until dismissed; the page remains available |
| notchPomodoroFocusMinutes | int | 25 | 1...180 |
| notchPomodoroShortBreakMinutes | int | 5 | 1...60 |
| notchPomodoroLongBreakMinutes | int | 15 | 1...60 |
| notchPomodoroLongBreakInterval | int | 4 | 1...24 ("Sessions between long breaks") |
| notchPomodoroTotalSessions | int | 4 | 1...24 ("Total sessions") |
Settings tab (Timer): two toggles: "Hide timer countdown" (icon eye.slash) and "Play sound when time is up" (icon speaker.wave.2). Session state is **memory-only** (never restored after restart).

### 7.3 Layout
Top row (island header area): left = mode picker, right = **Start** capsule while setting up; with a session: left = phase reading, right = `xmark` icon button ("Cancel", or "Done" when completed).
- **Mode picker**: three words in a row (spacing 14): "Timer", "Pomodoro", "Stopwatch"; each 12 pt medium, selected white else white 50%, padding horizontal 4, height 24, selected has a 2-pt orange capsule underline (animated spring 0.3 s / matched geometry); fallback (when the leading room is too small) a menu button showing the current mode name + chevron.down 9 pt (min scale 0.7, spacing 3), or just the mode glyph (`timer`/`stopwatch`) when even the name does not fit.
- **Start capsule**: "Start" 12 pt semibold orange on orange 18% capsule, height 28, horizontal padding 10; tooltip = pomodoroHint in Pomodoro mode else "Start".
Page tint orange (#FF9F0A) for every control on it.

#### Setup (no session) — `VStack spacing 8`
Row (height `rulerHeight` = 82; in Pomodoro `min(82, max(56, pageHeight − 36))`): `HStack(spacing 12)`: ruler (flexible) + big clock text **26 pt thin** monospaced, orange, 1 line, min scale 0.5. Stopwatch: clock only on the right (spacer left) showing `00:00`.
- Timer mode ruler label "Timer", value = `notchTimerMinutes`; clock = `mm:ss` of minutes (`15:00`; `1:30:00` when ≥ 60).
- Pomodoro mode ruler label "Focus", value = Focus minutes.
- Pomodoro adds a **28-high readouts row** (spacing 8 above): four readouts (menus) in this order: "Short break", "Long break", "Sessions between long breaks", "Total sessions". Each: VStack spacing 1, name 9.5 pt medium secondary (1 line, tail truncation) over value 12 pt semibold monospaced orange + chevron.down 7 pt secondary; value text = narrow duration for breaks ("5m", "15m", "1h" for 60), plain integer for counts; padding horizontal 4; hit radius 6; the menu lists every integer in the option's range with the current one checked. Row spreads over the width (equal columns) or scrolls horizontally (spacing 14, no indicator) when too narrow.

#### Ruler (`NotchTimerRuler`, 1...180 minutes)
- 1 minute = **14 pt** of horizontal travel (`spacing`). Tick for every minute 1...180: rounded rect **4 wide x 38 high** at y = 26·s (s = height/82), corner radius 2. A label above every 5th minute and minute 1, font 14 pt medium monospaced digits at y 0: `"1"`, `"5"`, `"10"`… `"55"`, then `1h00`, `1h05` … (format `%dh%02d`, i.e. minutes ≥ 60 as "1h05"). Colour: ticks at or below the selection solid orange; above the selection orange at 35% (65% in increased contrast); every tick fades toward the edges over 12% of the width (selected tick never fades). Disabled: x0.4.
- Pointer: orange triangle, apex at (selX, 70·s), base ±6 pt at y 80·s (so total drawn height 80·s).
- Selection position: the selected tick is centred in the control, but near either end the ruler stays filled: `inset = min(24, w/2)`; if the whole 180-tick strip is narrower than `w − 2·inset` centre it; else `x = clamp(inset + (sel−1)·14, w/2, w − inset − (180−sel)·14)`.
- Input: **drag** moves `value −= Δx / 14` (dragging right lowers the time, like pulling a tape); a **click** without drag moves the clicked tick under the pointer; **scroll** wheel/trackpad (precise points as is, wheel notches x14; horizontal if |dx|>|dy|); keys Left/Down −1, Right/Up +1, Home = 1, End = 180 (no Ctrl/Cmd); AX increment/decrement ±1; AX value text e.g. "15m". Changes emit haptic feedback (setting `notchHapticFeedback`, default true; Windows: none/optional).
- Keyboard focus ring: 1-pt orange rounded rect (radius 7) inset 1, only after keyboard use.
- Value clamps 1...180, integer.

#### Active session — `HStack(spacing 16)`, height 82
- Left: **round button 52x52**, orange 28% fill, glyph 23 pt semibold orange: running -> `pause.fill` (title "Pause"), paused -> `play.fill` ("Resume"); when a Pomodoro phase has completed (not cycle finished) -> `play.fill` titled `"Start " + nextPhaseName` (e.g. "Start Short break"). No button after a plain timer completed or the cycle finished.
- Spacer, then the **clock** 62 pt thin monospaced orange, min scale 0.5, rolling digits (digits roll down for countdowns, up for the stopwatch; no roll with Reduce Motion), updated once per second exactly on the second boundary.
- Top row left: phase reading: phase name 13 pt semibold orange (or "Time is up" when completed, "Pomodoro complete" when the cycle finished) + for Pomodoro "Session X of Y" 12 pt monospaced secondary (one line if it fits, else stacked); tooltip joins both with ", ".

### 7.4 Clock rules (`NotchTimerSession`)
- Countdown anchored to a continuous-clock deadline (`deadline = now + duration`), stopwatch to the instant it read zero; pause stores the reading; resume re-anchors. Sleep/UI stalls never drift. Use a monotonic source (`Stopwatch.GetTimestamp()` / QPC), never `DateTime.Now`, so wall-clock changes cannot shift a countdown; re-check the deadline on resume from sleep (`SystemEvents.PowerModeChanged`).
- Clock text: `mm:ss` (`%02d:%02d`) below 1 h, `h:mm:ss` (`%d:%02d:%02d`) from 1 h. Countdowns round **up** (limit 180 min); stopwatch rounds **down**, saturating at 99:59:59.
- Compact (closed island) text: remaining ≥ 1 h -> `"1h35"` (hours, floor minutes), else narrow-duration string ("45m" when ≥ 60 s, "45s" under a minute, ceiling); stopwatch shows `mm:ss` until 1 h then `"1h35"`.
- Done: when `now ≥ deadline` the session becomes `completed` (reading 0, glyph checkmark.circle); a focus completion increments `completedFocuses`.
- Pomodoro: `start` copies the saved configuration into the session (later edits affect the next cycle). Next phase after focus: **long break if `completedFocuses % longBreakInterval == 0`** else short break; after a break -> focus. A new phase **never starts automatically** — the user taps Start. Cycle finished when `completedFocuses ≥ totalSessions` ("Pomodoro complete"). `sessionNumber = min(total, completedFocuses + (phase==focus && !completed ? 1 : 0))`.
- Only one session at a time; `start` is ignored when a session exists; Cancel clears everything (also stops the alarm).

### 7.5 Completion
- A notice in the closed island: title "Time is up" (or "Pomodoro complete"), detail = phase name, symbol `timer`.
- Alarm sound: **Glass** system sound (`/System/Library/Sounds/Glass.aiff`, fallback beep) played immediately and then **every 2 s for at most 5 minutes** (`maximumDuration`), only when `notchTimerSoundEnabled`; stops on Cancel/Done/Start next/dismiss, survives preference toggles without restarting (deadline kept). Windows: `SystemSounds.Asterisk` or a bundled chime, same cadence (2 s, 5 min cap), `PlaySound` async.
- The mascot reacts (separate spec).
- A finished timer stays in the closed island (strip shows `checkmark.circle`) until dismissed even when the countdown is hidden.

### 7.6 Closed-island strip (`NotchTimerStrip`, capsule `NotchCapsuleTimerStrip`)
Left: mark in orange (or the companion's mark — download / agents / calendar clock / music cover); right: reading in orange, 16-ish pt medium monospaced (font = `min(16, height − 6)`; capsule uses 13 pt semibold digits), rolling digits only above the seconds, min scale 0.65. Mark symbol: completed `checkmark.circle`, paused `pause.circle`, stopwatch `stopwatch`, else `timer`; size `min(20, height − 10)`. Left click -> opens Timer page (calendar companion opens the event). Ticks every 1 s aligned to the second boundary. Hidden when `notchHideTimerCountdown` (unless completed). AX: label = phase name, value = clock, hint "Open Dynamic Island".

### 7.7 Windows mapping
- Controls: custom `FrameworkElement` ruler drawn in `OnRender` (DrawingContext rounded rects, `FormattedText` labels) with `MouseDown/Move/Up/Wheel/KeyDown`; opacity edge fade via per-tick alpha. Clock: `Segoe UI Variable Display` Thin/Light (thin 26/62 pt), `TextBlock` with `Typography.NumeralAlignment=Tabular` (or fixed digit width), animated roll by translate+opacity (160-200 ms) when animations are enabled.
- Timing: `DispatcherTimer` re-armed to the next second boundary; completion by `Task.Delay` on the monotonic deadline plus re-check on power resume (`SystemEvents.PowerModeChanged`).
- Notification of completion also goes through the island notice; optional Windows toast is NOT part of the replica.

### 7.8 Gap vs Trayloom (Timer)
`TimerFeature.cs` registers a "Timer" section with three modes titled **Timer / Stopwatch / Focus** (replica: **Timer / Pomodoro / Stopwatch**, order matters, "Focus" is only a Pomodoro phase). Differences:
- No duration ruler (port: preset buttons "−5m −1m +1m +5m" and Reset), no 26/62-pt thin orange clock sizing (46 pt Light white), no Start capsule in the header, no round 52-pt pause/resume button, no phase/"Session X of Y" reading, no readouts row.
- Pomodoro: port has `focusMinutes`, `shortBreakMinutes`, `longBreakMinutes`, `sessionsBeforeLong` (choices) and `autoAdvance` (default true). Replica: keys `notchPomodoro{Focus,ShortBreak,LongBreak}Minutes`, `…LongBreakInterval`, `…TotalSessions`, **no auto-advance** (explicit Start per phase), cycle ends after totalSessions with "Pomodoro complete".
- Port has Laps for the stopwatch and `+/-` adjustments (not in the replica), tenths of seconds display (replica: seconds only).
- Colours: port uses green/blue by phase; replica is orange throughout.
- Alert: port plays `SystemSounds.Asterisk` once and a 6-s pill alert; replica: repeating alarm every 2 s up to 5 min, notice "Time is up"/"Pomodoro complete" + phase detail.
- Compact pill: port shows glyph + text (Formatted `mm:ss`) in the running colour; replica uses the 'checkmark/pause/stopwatch/timer' circle marks, `1h35` / `45m` compact format, and `notchHideTimerCountdown`.
- Settings keys differ (`focusMinutes`, `shortBreakMinutes`, `longBreakMinutes`, `sessionsBeforeLong`, `autoAdvance`, `playSound` -> replace by the table in 7.2, `playSound` -> `notchTimerSoundEnabled` default true; add `notchHideTimerCountdown`).
- Timer persistence: memory-only in both; keep.

---------------------------------------------------------------------------------------------------

## PART 8. CROSS-CUTTING REQUIREMENTS FOR IMPLEMENTERS

1. **Data services first**: `MediaService` (multi-session, sources, gap grace, track change), `AudioLevelService` (process loopback + FFT), `LyricsService`, `CalendarService` (providers), `TimerService`, `KeepAwake`/`Mixer`/`Brightness`/`Monitor` adapters. Pages observe services; services never touch UI.
2. **Compact activity arbitration**: the port's `IslandHub.SetCompact` allows one owner. The replica has an activity picker/companion model (timer + music cover, calendar clock beside timer, keep-awake, agents, downloads) — see island shell spec; the music strip, timer strip, calendar countdown strip and keep-awake strip described above are four separate strips, selectable and combinable (timer is the primary with a companion).
3. **Preferences storage**: persist all keys named in this file in Trayloom's settings store with the specified defaults; unknown/out-of-range values clamp as stated.
4. **Reduce Motion**: no scale/slide/roll/glyph-replace animations; bars static dots; lyrics scroll jumps.
5. **No periodic work when hidden**: tick timers only while visible; bars clock only while a bars control is visible; lyrics schedule only at boundaries; audio capture only while the player plays.
6. **Accessibility**: set `AutomationProperties.Name`/`HelpText` from the labels/hints named above (buttons "Play or pause", "Previous track", "Next track", "Playback position" slider value text, etc.).


---------------------------------------------------------------------------------------------------------------------------------

# PART D - ISLAND EXTENSIONS (DOWNLOADS, FILES, CLIPBOARD, SCRATCHPAD, NOTIFICATIONS, LOCK SCREEN, COMPANION, WATCH, ACCESSORIES)

## Island extensions spec: Downloads, Files, Clipboard, Scratchpad, Notifications, Lock Screen, Companion, Watch, Accessories

Source of truth (all paths relative to `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint`): the island is called "Notch" in code. English strings are quoted verbatim; where a string names a Mac-only thing (Mac, macOS, Finder, Notification Center, Screen Recording) a **Windows wording** is given and must be used instead. Product name in UI: Trayloom. The feature name "Dynamic Island" stays as the user-visible name of the island.

Windows target: the island is a top-center, always-on-top, non-activating black overlay (`IslandWindow`, compact pill 116 x 34 DIP, expanded 480 x 292, section content roughly 430 x 215, corner radius 17 on the bottom corners). There is no camera notch, so every "beside the camera" layout becomes "left end / right end of the pill" (two wings with no centre gap). Glyph code points below are suggestions for Segoe Fluent Icons and must be eyeballed in Character Map.

---------------------------------------------------------------------------------------------------

### 0. Shared building blocks used by every page below

#### 0.1 Island page chrome (all pages)
- Page = a section/tab of the expanded island. Tab titles (English): Downloads, Notifications, Files, Clipboard, Scratchpad, Watch (module titles come from each feature's strings: `notchFiles.downloadsTitle`, `notchNotifications.title`, `notch.files` = "Files", `clipboard.title` = "Clipboard", `scratchpad.pageTitle` = "Scratchpad", `notchWatch.title`).
- "Band" (`notchBand`) = the island's top row. Beside a camera it is split into a leading and trailing slot on either side of the camera; on a full row (Windows pill, no camera) leading content is left-aligned and trailing content is right-aligned in the same 46 DIP header row used for the tab strip. Pages put their actions in the band so the page body can start at the very top.
- `NotchIconButton`: 28 x 28, glyph 12 pt medium, colour white 55% (white 100% + 12% white pill background when `selected`), corner radius 9 (continuous), tooltip + accessibility label = title, hover/press lift.
- `NotchPillButton`: height 28, horizontal padding 14, capsule, text 12 pt (semibold when prominent, else medium), prominent = white 14% fill + white 100% text, non-prominent = no fill + white 70% text.
- `NotchEmptyView(symbol, message)`: glyph 26 pt light, white 65%, in 56 x 56 rounded square (radius 18, white 5% fill); message 12 pt secondary. If the page is tall enough stack vertically (spacing 12, message max width 250, centred); if short, glyph left + text right (spacing 14, text max width 260). 12 pt padding. Centred in the page.
- `NotchMeter(value, height=5, tint=white)`: capsule track white 14%, fill tint 90%, min fill width = height when value > 0, value clamped 0...1.
- Cards: `white 7%` fill (Downloads, Watch) or `white 6%` (Notifications), radius 14 continuous, padding 12 (Downloads/Notifications) or 10 (Watch/Clipboard).
- `NotchControlSurface(cornerRadius)`: white 6.5% fill (12% selected); glass variant 4.5% / 11% with 0.5 pt stroke white 6.5% / 16%.
- `NotchMenuButton`: a SwiftUI label that pops a native menu (items: title, checked, symbol, enabled, separator = empty title). Windows: `ContextMenu`/`Popup` opened under the control, with `HoldOpen()` while open.
- Dialogs asked from the island (`NSAlert.confirmAboveIsland`, `NotchIslandAlert.run`): modal dialog raised one window level above the island, the island keeps open while it is up and takes key back afterwards; Escape = cancel in all languages. Windows: `MessageBox`/custom WPF dialog with `Owner` = null, `Topmost = true`, shown while holding `IslandHub.Controller.HoldOpen()`.

#### 0.2 Island notice (transient strip) - applies to Downloads, Watch, Accessories, Notifications, Clipboard
`NotchEvent` table (`Services/Notch/NotchSupport.swift`): the closed island shows one notice at a time.

| event | pref key (default) | priority | duration (s) |
|---|---|---|---|
| volume, brightness, keyboardLight, microphone, inputSource | notchVolume / notchBrightness / notchKeyboardLight / notchMicrophone / notchInputSource | 3 | 1.6 |
| capture | notchCapture | 2 | 12 |
| timer | notchTimerEnabled | 2 | 6 |
| watch | notchWatchEnabled (true) | 2 | 6 |
| battery | notchBattery | 1 | 4 |
| accessory | notchAccessoriesEnabled (true) | 1 | 4 |
| agents | notchAgentsEnabled | 1 | 5 |
| systemNotification | notchNotificationsEnabled (true) | 1 | 3 |
| track | notchTrackChange | 0 | 3 |
| download | notchDownloadsEnabled (true) | 0 | 6 |
| clipboard | notchClipboard (true) | 0 | 2.5 |

- Replacement rule: an incoming notice replaces the current one if `incoming.priority >= current.priority`; if the current notice is "held" (the pointer rests on it and it expanded), only the same event or a strictly higher priority replaces it.
- A notice is shown only when the island is on, the event's pref is true, the event's own gate passes (download: Downloads installed and the Downloads module visible; watch: `NotchWatchSupport.isEnabled`; accessory: `NotchAccessorySupport.isEnabled`; systemNotification: `NotchNotificationSupport.isEnabled`) and the island is neither expanded, peeking, dragging a file, nor showing capture controls.
- Clicking a notice opens the matching page: download -> Downloads, timer -> Timer, accessory -> System, systemNotification -> Notifications, clipboard -> Clipboard, agents -> AI Agents, track -> Music, microphone -> Mixer, watch -> Watch, else Controls.
- Layout of a plain text notice (`NotchNoticeView`): two wings. Leading wing: 18 pt wide mark (symbol 14 pt medium, tint white; orange for timer; agent tint for agents; warning icons orange) + title 11 pt medium (monospaced digits). Trailing wing: detail 11 pt medium at 80% opacity, right-aligned, one line, truncation tail (middle for accessory). Level notices (volume etc.) show a `NotchMeter(height 5)` trailing. Text notices read from the two ends of the strip. Wing min 36, max 240 (accessory without level: max 160). Wings animate width with a critically damped spring (duration ~0.35).
- The companion (section 7) replaces the symbol in a notice when enabled and the notice carries a reaction.
- Windows mapping: `IslandHub.ShowAlert(IslandAlert(glyph, text, accent, sectionId, duration))` exists but is single-line. Extend `IslandAlert` with: `Title`, `Detail`, `Level?` (0..1), `Reaction?`, `Priority`, `Held` and implement the replacement rule above. Durations in the table are the defaults.
- Gap: Trayloom has one alert with a fixed duration per call, no priority, no wings, no level meter, no held/expanded preview. Only Downloads uses it.

---------------------------------------------------------------------------------------------------

### 1. DOWNLOADS

Sources: `UI/Notch/NotchDownloadsView.swift`, `Services/Notch/NotchDownloadService.swift`, `NotchDownloadSupport.swift`, `NotchDownloadProgressObserver.swift`, `Core/NotchFilesStrings.swift`.

#### 1.1 What it is
A page that watches ONE folder the user picks (the browser's save folder) and lists what arrives, newest first, with live progress for files still downloading. It also feeds a compact "download activity" strip in the closed island, a "Download complete" notice, a lock-screen line and companion reactions. It is an installable island extension (`AppFeature.notchDownloads`, symbol `arrow.down.circle`, needs "Files & Folders" permission on Mac = the user-granted folder).

#### 1.2 Layout
**Settings controls** (`NotchDownloadsSettingsControls`, shown inside Dynamic Island settings under the Downloads row; vertical stack, spacing 10):
1. Toggle (trailing switch) "Downloads" -> key `notchDownloadsEnabled`; disabled if the extension is not installed.
2. Caption (caption font, secondary): "Choose the folder where your browser saves downloads. Only that folder is watched."
3. If the folder is unavailable: orange caption "This folder is unavailable. Choose it again to restore access." Else if a folder is set: label with folder glyph + folder name, 1 line, middle truncation.
4. Row (small controls): button "Choose Folder…"; if a folder is set or unavailable also button "Forget Folder".

**Page** (`NotchDownloadsView`), three mutually exclusive bodies:
- `needsSetup = !enabled || folderName == nil || folderUnavailable` -> **Setup view** (centred, max width 360, padding 12): glyph tile 56 x 56 (radius 18, white 5% fill), glyph `arrow.down.circle` (or `exclamationmark.triangle` when the folder is unavailable) 26 pt light white 65%; copy 12 pt:
  - folder unavailable -> orange "This folder is unavailable. Choose it again to restore access."
  - not installed and off -> "Downloads"
  - enabled -> "Choose the folder where your browser saves downloads. Only that folder is watched."
  - disabled but installed -> "See files arriving in a folder you choose, directly in the Dynamic Island."
  Actions: if disabled+installed a switch "Downloads"; if enabled a capsule button "Choose Folder…" (12 pt semibold, height 28, padding 14, white 14% capsule) and, only when the folder is unavailable, a plain button "Forget Folder" (12 pt medium, white 70%). `ViewThatFits`: vertical (glyph over text over actions, centred) else horizontal (glyph left, spacing 16).
- Folder set and list empty -> `NotchEmptyView(arrow.down.circle, "No files in this folder")`.
- Otherwise a vertical scroll (auto scroll indicators) of cards, spacing 8.
- Band, leading slot (only when not in setup): a menu button titled "Downloads" whose label is a folder glyph + the folder name (middle truncation, tooltip = folder name, accessibility value = name). Menu items: "Choose Folder…", "Forget Folder". Corner radius 9.

**Download card** (padding 12, radius 14, fill white 7%, clipped):
- Row (spacing 8): leading glyph `checkmark.circle.fill` (green) when completed else `arrow.down.circle` (white) + file name 12 pt medium, 1 line, middle truncation, fills width.
  - Completed: the glyph+name area is a button (min height 28, radius 6, no lift) with tooltip/hint "Open" (`shelfActionOpen`); clicking opens the file (beep on failure). Then icon buttons: folder glyph "Show" (`mediaOpenInFinder`; Windows: "Show in folder") reveals it selected; and, if the Shelf extension is installed, a `tray.and.arrow.down` icon button titled "Files" that adds the file to the shelf.
  - In progress: if `fraction` known show a percent label (caption, monospaced digit, 0 decimals).
- Completed: second line caption secondary "Saved".
- In progress: `NotchMeter` when fraction known, else a small indeterminate progress ring when `active`; then a caption2 secondary row: left "Downloading" (only if `active`, else empty) / right the received size (`ByteCountFormatter` file style, e.g. "12.3 MB") when bytes > 0, else "Total size unavailable" when the fraction is unknown.

**Compact strip** (`NotchDownloadStrip`, closed island activity): left wing: `arrow.down.circle.fill` icon (size min(17, contentHeight - 2*gap)), then (if wing >= 64) the file name 11 pt medium (middle truncation; shows the active item's name or "Downloads"); right wing: a 4 pt `NotchMeter` (only if the name is shown and the fraction is known) then the percent in 10 pt medium monospaced digits (min scale 0.75, shown if wing >= 36); an indeterminate mini ring when the fraction is unknown. Icon shown if wing >= 40. Wing width 56 by default; with a name it grows to fit: wing = clamp(ceil(edge inset + max(icon, glyph width) + 6 + name width + 4), 64, 160), and a name is restored only when the side room is >= 94 (otherwise the arrow and percent keep the 56 wing). Click opens the Downloads page. Accessibility label = file name or "Downloads".

#### 1.3 Strings (English, `NotchFilesStrings`)
downloadsTitle "Downloads"; downloadsDescription "See files arriving in a folder you choose, directly in the Dynamic Island."; downloadsHint "Choose the folder where your browser saves downloads. Only that folder is watched."; chooseFolder "Choose Folder…"; folderUnavailable "This folder is unavailable. Choose it again to restore access."; waiting "No files in this folder"; inProgress "Downloading"; totalUnknown "Total size unavailable"; completed "Download complete"; saved "Saved"; clearFolder "Forget Folder". Shared: "Open", "Show" (Windows: "Show in folder"), "Files".
Windows wording: description "…directly in the Dynamic Island." keep; "Total size unavailable" keep.

#### 1.4 Settings
| key | type | default | notes |
|---|---|---|---|
| `notchDownloadsEnabled` | bool | true (registered default; set to false on migrated old profiles; set true when a folder is chosen; set false by "Forget Folder") | master switch of the extension page, strip and notice |
| `notchDownloadsFolderBookmark` | Data (security-scoped bookmark) | none | Windows: plain string path `folder` (no sandbox) |
| notice pref `notchDownloadsEnabled` doubles as the `download` notice pref | | | |
Extension availability key `AppFeature.notchDownloads.availabilityKey` (install state, default installed with the island's initial extensions).

#### 1.5 Behaviour
- Lifecycle (`syncWithPreferences`): runs only if island enabled, extension installed, `notchDownloadsEnabled` true and the Downloads module is visible; otherwise `stop()`. With no stored folder: `folderName = nil`, `folderUnavailable = false` (setup page). With a stored but unresolvable/stale bookmark: `folderUnavailable = true`.
- Choose folder: folder-only open panel starting in the user's Downloads folder, message = downloadsHint. Choosing stores the bookmark, sets `notchDownloadsEnabled = true`, restarts watching and resyncs the island. The panel opened from the island stays above the island (window level +1, does not hide on deactivate), keeps the island open while up (`isChoosingFolder`), and when it closes returns to the Downloads page; if the page went away meanwhile the pending panel is cancelled (`cancelNotchFolderChoice` on disappear; a panel started from Settings stays).
- Forget folder: stop, remove bookmark, `notchDownloadsEnabled = false`, clear name/unavailable.
- Folder watch: a directory-level watcher (write, extend, attrib, rename, delete, revoke). Rename/delete/revoke of the folder itself = stop and `folderUnavailable = true`. Any other event schedules a rescan debounced 0.3 s (coalesced; a scan in flight sets `rescan`).
- Scan (`scanFolder`): non-recursive, skip hidden files, skip symlinks, only regular files or directories; a name with extension `crdownload`, `download`, `part` (case-insensitive) is a **partial** whose expected final name is the name without that extension (Safari `.download` is a package directory: look only at `<partial>/<expected name>` for bytes). At most 32 partials observed (`maximumObservedFiles`). Other entries are **finished files**; list keeps the newest 200 (`maximumListedFiles`) ordered by added/created/modified date then path.
- Merge (`mergedItems`): active (progress-published) items own their destination; folder files and recent completions that are represented by an active item are dropped; de-duplicate by standardized URL; sort by date descending then path.
- Active partial with no published progress: item name = expected name, `fraction = nil`, bytes = payload size, `active` only while the partial was modified in the last 120 s (a refresh timer is scheduled for the moment that expires).
- Native progress (macOS `Progress` file publications, `addSubscriber(forFileURL:)`): up to 32 observed; a publication counts only if its file URL is a direct child of the folder, kind downloading/receiving, not cancelled; fraction = reported fraction if total > 0 and not indeterminate; completed requires: finished, destination file exists, its identity/size/mtime differs from the first observed snapshot, and the name is not a partial. Paused = not active.
- Completion detection (without native progress): a previously seen partial that vanished, whose `expectedURL` now exists as a regular file with the **same file identity (dev:inode) as the partial** and nothing replaced it -> completed item (size, date now). Also the system broadcast `com.apple.DownloadFileFinished` (browsers announce final paths; Safari moves out of `.download` too fast to scan) -> `announcedItem`.
- Failure: partial gone, no partial with the same expected name, neither the partial nor the expected file exists, and the progress observer does not confirm it finished -> after a 2 s grace `onFailure` fires (companion `confused`). Cancelled/failed downloads therefore just disappear.
- `recordCompletion`: keeps the 5 most recent completions in `finished`, fires `onArrival(item)` once per item id, clears `finished` 15 s after the last completion. `onArrival` shows the notice: event `download`, title "Download complete", detail = file name, symbol `arrow.down.circle.fill`, companion reaction `celebrate`, and calls `reactMascot(.celebrate)`.
- Compact activity: Downloads is a closed-island activity while at least one item is `active && !completed`. Island activity order when several are live (automatic): timer, watch, downloads, agents, calendar, music, keep-awake; the user can pin one. Timer may keep a companion on the right and a download beside it.
- Lock screen / session: on lock the service is stopped (unless the lock-screen scene needs it, see section 6) and restarted on unlock; the lock-screen line shows only the percent or "Downloading", never the file name.
- Empty/loading/error states: setup page (no folder / off / unavailable), empty page "No files in this folder", per-card indeterminate state.

#### 1.6 Windows mapping
- Folder choice: `FolderBrowserDialog`/`IFileOpenDialog` with `FOS_PICKFOLDERS`, start in `FOLDERID_Downloads` (`SHGetKnownFolderPath`). Store the path string; "unavailable" = `!Directory.Exists(path)` or watcher `Error`/`Deleted`/drive removed. No security-scope needed.
- Watcher: `FileSystemWatcher` (non-recursive; `NotifyFilter = FileName | LastWrite | Size | CreationTime`, `InternalBufferSize = 64 KB` to avoid overflow; on `Error` rescan). Debounce rescans 300 ms like macOS. Skip hidden/system files (`FileAttributes.Hidden | System`), reparse points (symlinks/junctions), and temp names.
- Partial extensions to recognise (superset of macOS): `.crdownload` (Chrome, Edge, Opera, Brave, Vivaldi), `.part` (Firefox), `.opdownload` (Opera legacy), `.download`, `.partial`, `.!ut`, `.aria2`, `.filepart`. Expected name = drop the final extension. Edge/Chrome also create `Unconfirmed 123456.crdownload` first, then rename to the real `.crdownload`, then rename to the final name: treat the expected-name change as a rename of the same file ID (see below).
- File identity: `GetFileInformationByHandleEx(FileIdInfo)` (volume serial + 128-bit file id) opened with `FILE_SHARE_READ|WRITE|DELETE`, no content access; rename keeps the ID, which gives the same "same file, new name = finished" proof as dev:inode. Compare also `Length` and `LastWriteTime`.
- Progress: Windows has no `Progress` publication API for third-party browsers, so progress is **indeterminate by default** ("Total size unavailable") with the bytes received = partial's `Length`. Optional enrichment: for Chromium browsers read nothing else; do NOT read browser history databases. A fraction is therefore `nil` except when a sibling `.crdownload` size is paired with a total the browser exposes (it does not): keep `fraction = nil`. "Downloading" shown while the partial was modified in the last 120 s.
- Failure detection and 2 s grace: same algorithm using file ID checks; no equivalent of `DownloadFileFinished` on Windows (skip it).
- Open/Show: `Process.Start(path, UseShellExecute)`; reveal = `explorer.exe /select,"path"`; "Add to Files" = existing Shelf feature API (`ShelfFeature`), hide the button when Shelf is not installed. Drag-out is an extra Trayloom nicety that exists in the current port (keep).
- Compact strip: `IslandHub.SetCompact("downloads", CompactInfo(glyph E896, name or percent, accent blue/white, "downloads"))`; since there is no fixed wing width use pill width 116 + name width (clamp 116..320) and show percent only when a fraction exists; otherwise a small spinning ring.
- Session: pause the watcher on `WTS_SESSION_LOCK` only if the lock scene is not used (it is not on Windows, see section 6); keep watching while locked and just do not present.

#### 1.7 Gap vs Trayloom (`DownloadsFeature.cs`)
Has: Known Downloads folder default, optional folder text box + Browse in settings, `FileSystemWatcher`, temp-extension filter (superset), settle/stability check before listing a file (macOS lists partials as in-progress cards instead), 15 items max, 24 h seed list, row with icon, name, size, "x min ago", hover buttons (Show in folder, Remove from list), context menu (Open/Show in folder/Copy path/Remove), drag-out, announce toggle ("Show new downloads in the collapsed island" default true), alert on completion (glyph none, name, green, 4 s), actions "Open the island downloads"/"Open the downloads folder".
Lacks/differs: no setup page, no "Choose Folder…/Forget Folder" band menu or `Forget`; no `unavailable` state; no in-progress cards (partials hidden), no bytes/"Downloading"/"Total size unavailable" text; no "Saved" state; no 200-entry folder list (only the last 24 h/15 items); no compact download strip with name + percent; notice text is not "Download complete" + file name and 6 s (uses 4 s); no failure -> companion; no Add-to-Files; row layout and card style differ (30 px icon tile vs spec's card with meter); strings differ ("Recent in X", "Nothing downloaded yet" vs "No files in this folder"); no `notchDownloadsEnabled` switch (feature on/off is the Features page toggle); setting keys (`folder`, `announce`) should map to `folder` + `enabled`.

---------------------------------------------------------------------------------------------------

### 2. FILES (island tab: shelf, ZIP, media drop)

Sources: `UI/Notch/NotchFilesView.swift`, `Services/Notch/NotchFileToolsService.swift`, `NotchFileToolsSupport.swift`, `Core/NotchFilesStrings.swift`. The Files tab shows the **Shelf** (files parked in the island) plus two tools: Create ZIP and Optimize media. The shelf item model and tile rendering (`ShelfTilesView`, `ShelfService`) belong to the Shelf spec; only the island-specific glue is specified here.

#### 2.1 Layout (`NotchFilesView`, vertical stack spacing 10)
Exactly one body, in priority order:
1. **Drop-destination chooser** (while a file drag hovers the island and Media tools are installed): two equal tiles side by side, spacing 12 (`dropSpacing`), filling the page.
   - Tile 1 symbol `tray.and.arrow.down`, title "Files" (`notch.files`).
   - Tile 2 symbol `wand.and.stars`, title "Optimize media"; if the media tool is busy, an extra caption "Processing" (`mediaRunning`) and the whole tile at 50% opacity.
   - Tile style: vertical stack spacing 12; symbol 28 pt light; title callout medium centred; padding 12; background radius 20, white 14% when selected else 4%; stroke white 70% (1.5 pt) when selected else white 20% (0.75 pt); selected tile = where the drop will land (`service.targetsMediaDrop` false = Files). The right half of the content area is the media drop zone (`mediaDropArea`: x = size.width/2 + 6, width (content width - 12)/2, full content height).
2. **Media workspace** (after "Optimize media"): embedded compact `MediaWorkspaceView` (own close button, tool = image compressor for images, video compressor for one video) with the page height following its content.
3. **Empty shelf**: `NotchEmptyView(tray.and.arrow.down, "Drop files here")` with a dashed rounded rectangle (radius 20, white 18%, 0.75 pt, dash 4/5) over the whole page.
4. **Tiles**: sideways scrolling shelf tiles.
Below the body: **archive status** and a "Return to media" small button when a media session is still open.

Band, leading: while an archive runs a spinner (small) + "Create ZIP · 3/5" (12 pt monospaced digits, secondary; shortens to "3/5" if it does not fit) + an `xmark` icon button titled "Cancel" (disabled once cancelled); otherwise, with tiles present: the shelf hint (10 pt secondary, 2 lines, tooltip) "Click to select. Drag out to use or right-click for more actions." or, with a selection, "%d selected" (12 pt).
Band, trailing (only with tiles):
- `wand.and.stars` icon button "Media" (only if Media tools installed): enabled when files are present and no media/ZIP job runs; opens a 270 pt wide popover (padding 14) with: button "Create ZIP" (`doc.zipper`) + caption "Each selected item is saved as a separate ZIP. Originals stay unchanged." + divider + one borderless button per supported tool: "Video", "GIF", "Image", "Text".
- `square.and.arrow.up` "Share" (system share sheet for the selection/everything).
- Trash: with no selection a menu icon (trash glyph, 28 x 28, white 55%) offering "Clear all" (destructive) and "Cancel"; with a selection a `trash.fill` button "Remove selected". Both disabled while an archive runs.

Archive status (below the page body, caption): running -> nothing (progress is in the band); failure -> orange caption (max 3 lines) with the system error text; success -> row: green check + "Saved", spacer, small button "Show" (Windows: "Show in folder") revealing the outputs; cancelled -> secondary "Cancelled.".

#### 2.2 Strings (English)
archive "Create ZIP"; archiveHint "Each selected item is saved as a separate ZIP. Originals stay unchanged."; saved "Saved"; optimizeMedia "Optimize media"; optimizeDropHint "Drag images or one video onto the island, then drop on Optimize media to choose how to save a copy."; resumeMedia "Return to media"; Shelf/shared: shelfHint above; shelfSelectedFormat "%d selected"; shelfClearAll "Clear all"; shelfRemoveSelected "Remove selected"; shelfActionShare "Share"; mediaName "Media"; mediaToolVideo "Video", mediaToolGIF "GIF", mediaToolImage "Image", mediaToolText "Text"; mediaRunning "Processing"; mediaCancelled "Cancelled."; mediaOpenInFinder "Show"; uninstallerCancel "Cancel"; notch.dropHint "Drop files here".

#### 2.3 Settings / keys
`notchShelf` (bool) = "Open the file shelf in the Dynamic Island" (string `shelfWindow`); `notchDragReveal` = "Show a drop target while dragging" (`dragReveal`); `shelfEnabled` (Shelf feature switch; required for drops and ZIP); Files module visible in the island sections editor. Media tools feature installed (`AppFeature.mediaTools`) gates Optimize media and the ZIP command.

#### 2.4 Behaviour
- ZIP: for every input item (file, folder or symlink) one ZIP is produced with `/usr/bin/ditto -c -k --sequesterRsrc [--keepParent for directories] input output`. One input -> a Save panel (type zip, name = unique "name.zip" next to the input, directory = input's folder, message = archiveHint). Several inputs -> a folder-choose panel; each output name = unique "<input name>.zip" inside that folder (unique = no clobber, numbered suffix). The panel is created above the island (level +1, does not hide on deactivate); the island is kept open while it or the actions popover is up (`keepFileInteractionOpen`). After success the page reopens on Files.
- Output is staged in a temporary file and installed atomically; an existing item is never replaced. Destination must not be inside any input (identity comparison, aliases and case-insensitive volumes considered). Sequential jobs, a failure stops the queue and shows the error; progress "completed/total".
- Cancel: marks cancelled and terminates the child process, SIGKILL after 0.5 s; "Cancelled." status. Stopping the island kills immediately.
- Every produced ZIP is added to the shelf automatically.
- Optimize media: drops of images (any count) or exactly one video offer the second tile; mixed or file-promise payloads fall back to the shelf. `NotchFileToolsSupport.optimizationTool`: image compressor if all inputs are images, else video compressor if one video. Results of a completed media job are added to the shelf. Media session survives page changes ("Return to media" resumes).
- The island closes only when the pointer leaves AND no panel is up; while the media workspace is up the modal panel keeps it open.

#### 2.5 Windows mapping
- ZIP: `System.IO.Compression.ZipFile` / `ZipArchive` on a background task with `CancellationToken`: file -> `ZipFile.Open(temp, Create)` + `CreateEntryFromFile(name)`; directory -> `ZipFile.CreateFromDirectory(dir, temp, CompressionLevel.Optimal, includeBaseDirectory: true)` (= `--keepParent`). Write to `<dest>.tmp-<guid>` in the same folder then `File.Move` (fail if exists, pick next free name "name (2).zip"). Cancel = token + delete temp (no process to kill).
- Save panel: `SaveFileDialog` (`Filter = "ZIP archive|*.zip"`); multi-item: `FolderBrowserDialog`/`IFileOpenDialog` folder mode. Show them with the island's `HoldOpen()`.
- Inside-input check: compare full normalized paths and `FileIdInfo` of the directories (path prefix check with `Path.GetFullPath` + final `\`), plus handle junctions.
- Share: `Windows.ApplicationModel.DataTransfer.DataTransferManager` via `IDataTransferManagerInterop.ShowShareUIForWindow` (needs package identity for full UI; otherwise fall back to the "Open with" / copy-path menu).
- Drop target: the island window must accept `OLE` file drops while collapsed (WPF `AllowDrop` on a 24 px tall hit strip when the "drag reveal" option is on); detect drags with a low-level mouse hook + `GetAsyncKeyState(LBUTTON)` and `DragEnter` hooks (existing `ShelfFeature` auto-show logic can be reused: "Show when dragging near the top edge").
- Media tools: reuse `Trayloom.Features.Tools/MediaTools` if present; Windows has no `ditto` so ZIP is native.

#### 2.6 Gap vs Trayloom
No Files tab in the island at all; the Shelf exists only as a separate floating `ShelfWindow` (auto-show near the top edge, remember contents). Missing: island tab, drop-destination chooser, dashed empty state, band hint/selection count/actions, ZIP command and status, Optimize media handoff, Share and Clear/Remove menu, "Return to media". Strings for the shelf are not the verbatim ones.

---------------------------------------------------------------------------------------------------

### 3. CLIPBOARD (island tab)

Source: `UI/Notch/NotchClipboardView.swift`; strings `ClipboardFeatureStrings.enUS` (`Core/FeatureStrings.swift`), `NotchClipboardSizeStrings`; layout constants in `NotchLayout`. History data/capture is the Clipboard History feature (separate spec); this is the island page.

#### 3.1 Layout
- Page (vertical): [search field, only when "open"] + body; band leading: magnifier icon button "Search copied text" (hidden while the field is open); band trailing: three icon buttons (6 pt apart): filter `line.3.horizontal.decrease.circle` / `.fill` when active, titled "Pinned" (selected state when on; per-opening state, resets when the island reopens); trash "Clear unpinned" (disabled when there are no recent unpinned entries); `arrow.up.forward.app` titled "Clipboard" (opens the full history window; collapses the island first).
- Search field: height 36 (`clipboardSearchHeight`), horizontal padding 12, glyph magnifier (secondary) + plain text field 12 pt, placeholder "Search copied text"; control surface radius 14; when focused a 1 pt white 34% stroke; hidden state = height 0, opacity 0, but **mounted and focused** so typing reaches it from the first key; spacing between field and list 10 only while open.
- Empty states: history off and no entries -> `NotchEmptyView(doc.on.clipboard, "Enable history to start saving copied text.")` + small bordered button "Save clipboard history" (turns the history on). Otherwise empty list -> `NotchEmptyView` with `pin` (pinned filter) or `doc.on.clipboard` and "No saved text" (no query, not pinned) / "No results".
- List: vertical scroll of cards, spacing 8 (`clipboardCardSpacing`), scroll indicators automatic, newest first, pinned entries above recent ones.
- Card shape: radius 14 continuous, control surface (selected look when pinned), 1 pt white 34% outline when it is the keyboard highlight (0 otherwise), clipped. Right-click menu: "Paste" (only when accessibility/paste permission is granted; Windows: always), "Copy", —, "Pin"/"Unpin", "Move up", "Move down" (disabled during a search or at the ends), —, "Delete item" (destructive). VoiceOver actions "Move up"/"Move down".
- **Comfortable size** (`notchClipboardCardSize` = `comfortable`, the default): fixed row height 104 (`clipboardCardHeight`). Card: padding 10 (top 10, bottom 4), preview area fills (text up to 3 lines, 12 pt; image thumbnail radius 6 fit to the card; single image file shows itself; files show a folder glyph + name (middle truncation, 2 lines) or "%d files"), then a 28 pt tall footer: source-app icon 14 x 14 (tooltip = app name) or kind glyph (`text.alignleft` / `photo` / `doc`, 10 pt secondary), date "MMM d, h:mm a" style (9.5 pt tertiary), spacer, "⌘1".."⌘9" hint (9.5 pt medium monospaced tertiary; only for the first 9 rows and while the island holds the keyboard), then buttons: `pencil` "Edit" (images only, when Screenshot is installed), copy `doc.on.doc` -> `checkmark` "Copied" for 1.6 s, `pin`/`pin.fill` "Pin"/"Unpin", `trash` "Delete item".
- **Compact size** (`compact`): row height 52 (`clipboardCompactCardHeight`), padding horizontal 10 vertical 6. Leading column 14 wide (file type icon from the OS, or kind glyph), preview (text 2 lines, image thumbnail max width 72) fills; trailing area shows time (9.5 pt tertiary, `h:mm`) plus pin mark (9 pt) plus "⌘n" hint, or, for the hovered/highlighted row, the four icon buttons (edit/copy/pin/delete). Resting the pointer for 0.7 s (`dwell`) opens the entry: a lifted card drawn over the rows around it (same layout as comfortable but height = text lines (max 8) x 16 + 48, or image: clamp(width / aspect, 80...170) + 48, files 104), opaque `white 9%` (`Color(white: 0.09)`) fill with a 0.5 pt white 14% stroke, 1 pt black backing, soft shadow black 50% radius 10 y 4, offset so it stays inside the list; it closes 150 ms after the pointer leaves (re-entering the same card cancels the close), and keyboard rest of 1.4 s (`keyDwell`) opens it too (only after arrow keys were used). Animations 0.18 s ease-out (none under reduced motion).
- Color text entries show a 12 pt swatch before the text.
- Search highlight: matches drawn bolder (no accent colour).

#### 3.2 Strings (English)
title "Clipboard"; search "Search copied text"; empty "No saved text"; noResults "No results"; disabled "Enable history to start saving copied text."; enable "Save clipboard history"; copy "Copy"; copied "Copied"; pin "Pin"; unpin "Unpin"; pinned "Pinned"; delete "Delete item"; edit "Edit"; moveUp "Move up"; moveDown "Move down"; clearRecent "Clear unpinned"; imageEntryLabel "Image"; fileCountFormat "%d files"; clickRowShortcut "Click row"; clearRecentConfirmFormat "Clear unpinned (%d)?"; clearRecentConfirmMessage "Pinned items stay, and so does anything copied after this. This can’t be undone."; cancel "Cancel"; menuPaste "Paste". Island settings: notchClipboardWindow label `clipboardWindow` "Open clipboard history in the Dynamic Island"; clipboardActivity "Notify when something is copied"; size strings: entrySize "Entry size", compact "Compact", comfortable "Comfortable", hint "Compact entries show one or two lines and open up when the pointer rests on them."

#### 3.3 Settings
| key | type | default |
|---|---|---|
| `notchClipboard` | bool, "Notify when something is copied" (notice) | true |
| `notchClipboardWindow` | bool, history opens in the island instead of the floating window | true |
| `notchClipboardCardSize` | `comfortable` \| `compact` (anything else = comfortable) | comfortable |
| `clipboardHistoryEnabled` | bool (history on/off) | false (registered default; the page offers a "Save clipboard history" button that sets it to true) |
The notice: event `clipboard`, title "Copied", detail "Clipboard", symbol `doc.on.clipboard`, 2.5 s, priority 0.

#### 3.4 Behaviour
- Opening the page highlights the most recent unpinned entry (first non-pinned, else first); typing filters from the first key (the hidden search field already has focus), a typed letter opens the field (`reveal`); Escape closes the field first (page layer) then the island. Query text change clears keyboard-move state and highlights the top result.
- Keys (`clipboardKey`): Up/Down and Ctrl-P/Ctrl-N move the highlight (Shift variants belong to the text field); Return/Enter (no shift) pastes/copies the highlighted entry; ⌘1..⌘9 act on the nth row (beep if none). Other modifier combos are left to their owners. An IME composition passes every key to the field.
- Activate (click or Return): if paste permission is available -> collapse the island and paste into the previous app; else only copy (tooltip "Copy" instead of "Click row"). Copy ignores the second click of a double click (the entry moves to the top after the first copy) and shows the check for 1.6 s; the list scrolls to the moved entry (0.15 s ease-out); failure beeps.
- Scroll-to-highlight rule (`follow`) and opening scroll (`clipboardOpeningScroll`): the list stays at its top while at least half a compact row (or the first line of a comfortable one) is visible, otherwise moves by whole rows.
- Clear unpinned: asks above the island, "Clear unpinned (n)?" with the confirm message, destructive button "Clear unpinned" and "Cancel".
- Reordering is disabled while a query is active.

#### 3.5 Windows mapping
Reuse `ClipboardHistoryStore` (`ClipEntry` text/image, pinned, source, hash, created). Add `kind = files` (CF_HDROP) if absent. Paste: copy to clipboard then `SendInput` Ctrl+V into the previously foreground window (restore focus with `SetForegroundWindow` of the saved HWND; the island is non-activating so focus rarely moves). Source-app icon: `ExtractAssociatedIcon` / `SHGetFileInfo` of the owning process exe from `GetClipboardOwner` -> process. File icons: `SHGetFileInfo(SHGFI_ICON)`. Keyboard: the island window needs to take keyboard focus while the Clipboard page is shown and the pointer is inside (the island is `WS_EX_NOACTIVATE`; use a `WH_KEYBOARD_LL` hook to feed keys, or activate it temporarily while the search field is open). Shortcuts: use Ctrl+1..9 instead of ⌘1..9 (rename hints to "Ctrl 1"). Hover dwell with `DispatcherTimer` 700 ms/150 ms.

#### 3.6 Gap vs Trayloom
No Clipboard tab in the island. Existing `ClipboardHistoryFeature` has its own popup window (`ClipboardPopupWindow`), history limit/retention/captureImages/ignoredApps settings and pin support but no island-tab layout, compact/comfortable sizes, hover-open cards, key handling spec, files entries, filter/clear/ open-window header actions, "Save clipboard history" empty-state button, or the "Copied" notice.

---------------------------------------------------------------------------------------------------

### 4. SCRATCHPAD (island tab)

Source: `UI/Notch/NotchScratchpadView.swift`, `Core/ScratchpadStrings.swift` (`ScratchpadFeatureStrings.enUS`), `Services/QuickTools/ScratchpadSupport.swift`, `ScratchpadService.swift`.

#### 4.1 Layout
- Page body: [format bar, 28 pt tall, only when "Formatting" is toggled] / [save-failed warning] / editor card. Editor card = `NotchControlSurface(cornerRadius 12, non-interactive)`, fills the page; text view with inset 6 x 6, white text, white caret, font size `scratchpadTextSize` (default 13, range 10...22), find bar enabled, auto-continue lists, task-box completion. Placeholder (white 35%, same size, at inset + line padding) "Type anything. It saves by itself." when empty and not previewing. Preview mode replaces the editor with a rendered Markdown view (task checkboxes toggle the underlying text; labels "Done"/"Not done").
- Load failure replaces the whole page with `NotchEmptyView(exclamationmark.triangle, "Your notes could not be opened. They were left unchanged.")`.
- Save failure: inline label (warning glyph + 10.5 pt, white 60%) "Your notes could not be saved. Copy them elsewhere before quitting."
- Band, leading (`padControls`): **beside a camera** (narrow): a menu button titled "Scratchpad actions" labelled with the selected pad's name (`NotchBandMenuLabel`), items = list of pads (checkmark on the selected) + separator + actions below. **Full row** (Windows pill): horizontal tab strip (height 28, spacing 4, auto-scroll to keep the selected tab centred, 0.15 s) + a `plus` icon button titled "New scratchpad" (or "You can keep up to 12 scratchpads" when full; disabled when full).
  - Tab (capsule, height 22, padding left 10 right 10 / 8 for the selected / 5 when showing its close button): label 11 pt medium, 1 line. Selected: white 100% text, white 14% fill, trailing `chevron.down` 8 pt semibold, and clicking it opens the pad actions menu (corner radius 11). Unselected: white 60% text, white 5% fill; click selects; on hover (when more than one pad exists) shows an `xmark` 8 pt button (14 x 14) titled "Close scratchpad". Context menu on a tab: "Rename scratchpad", "Close scratchpad" (destructive, disabled with only one pad).
- Band, trailing: three icon buttons: `textformat` "Formatting" (toggles the format bar; selected when open; disabled in preview), `eye` / `pencil` toggling preview ("Show formatting" / "Edit text"; selected in preview; disabled when empty), `doc.on.doc` "Copy all" -> `checkmark` "Copied" for 1.6 s (disabled when empty).
- Pad actions menu items (in order): [pad list + separator when beside the camera], "New scratchpad" (disabled at 12), "Rename scratchpad", "Close scratchpad" (disabled with 1 pad), separator, "Save as file", "Clear", separator, "Open scratchpad" (opens the floating pad window).
- Rename dialog (above the island): title "Rename scratchpad", 240 x 24 text field prefilled with the name, buttons "Save" / "Cancel". Close dialog (only for non-empty pads): title "Close scratchpad", message `Delete “NAME” and everything in it?`, buttons "Close scratchpad" (destructive) and "Cancel". An empty pad closes without asking.
- Format bar marks (`ScratchpadFormatBar`, style island): Bold, Italic, Strikethrough, Heading, Bulleted list, Numbered list, Quote, Code, Link; each writes the Markdown a user would type.

#### 4.2 Strings (English)
pageTitle "Scratchpad"; hubDescription "Floating pads for short-lived notes"; panelCaption "Quick notes in separate tabs"; openButton "Open scratchpad"; placeholder "Type anything. It saves by itself."; copyAll "Copy all"; copied "Copied"; exportAction "Save as file"; exportFailed "The file could not be saved"; loadFailed "Your notes could not be opened. They were left unchanged."; saveFailed "Your notes could not be saved. Copy them elsewhere before quitting."; clearAction "Clear"; retentionTitle "Clear on its own"; retentionNever "Never"; retentionDay "After a day unused"; retentionWeek "After a week unused"; retentionMonth "After a month unused"; retentionCaption "The pad empties itself once the text goes that long without edits."; closeOnClickOutside "Close when I click outside"; keepOpen "Keep open"; backgroundOpacity "Pad background"; backgroundTranslucent "Translucent"; backgroundOpaque "Opaque"; newPad "New scratchpad"; padActions "Scratchpad actions"; renamePad "Rename scratchpad"; closePad "Close scratchpad"; saveName "Save"; cancel "Cancel"; deletePadMessageFormat "Delete “%@” and everything in it?"; padLimitFormat "You can keep up to %d scratchpads"; previewFormatting "Show formatting"; editText "Edit text"; taskDone "Done"; taskNotDone "Not done"; markBold "Bold"; markItalic "Italic"; markStrikethrough "Strikethrough"; markHeading "Heading"; markBullet "Bulleted list"; markNumbered "Numbered list"; markQuote "Quote"; markCode "Code"; markLink "Link"; formatMarks "Formatting"; textSize "Text size".

#### 4.3 Settings
| key | type | default |
|---|---|---|
| `notchScratchpad` | bool, Scratchpad page shown in the island | true |
| `notchScratchpadControlHidden` | bool, Controls-page shortcut hidden (migration marker) | false |
| `scratchpadDocument` | JSON `ScratchpadDocument` (pads: id, name, text, updated; selectedPadID) | - |
| `scratchpadTextSize` | double 10...22 | 13 |
| `scratchpadRetention` | `never`\|`day`\|`week`\|`month` | never |
| `scratchpadCloseOnClickOutside` | bool (floating pad) | true |
| `scratchpadBackgroundOpacity` | double (floating pad) | 0.0 |
| `scratchpadShortcutEnabled` / `scratchpadShortcut` | bool / shortcut | false / default scratchpad shortcut |
Limits: `maximumPadCount = 12`, `maximumNameLength = 40`; default names "Scratchpad 1", "Scratchpad 2"... (`nextPadName`: base = localized pageTitle, first free number); export file name `"<pad name> yyyy-MM-dd.txt"` (":" and "/" replaced by "-").

#### 4.4 Behaviour
- Autosave debounced 0.8 s and on page disappear (`commitEdits`); retention clears a pad's text when unedited longer than the interval (applied at load). Load for embedding fails if the Scratchpad feature is not installed.
- Selecting/creating a pad puts the caret at the end of its text (after the next layout turn) when the island is key. Preview closes the find bar and drops focus; leaving preview refocuses the editor unless a find bar/Command-G took the keyboard.
- Cmd-T / Cmd-W / Cmd-F / Cmd-G / Cmd-Return (toggle preview) behave as in the floating pad while the island holds the keyboard (Windows: Ctrl+T, Ctrl+W, Ctrl+F, Ctrl+Return).
- Clear: empties the pad through the text view's undo stack (one undo restores). Export: save panel suggested name above, plain text UTF-8. "Open scratchpad": performs `pad.show` in a floating window (`service.perform` collapses the island first).
- While a rename/close dialog, save panel or find bar is up the island does not auto-close.

#### 4.5 Windows mapping
Editor: WPF `TextBox` (AcceptsReturn, undo) or RichTextBox in plain mode; list continuation and task boxes implemented in code; find bar = small inline find row (Ctrl+F). Markdown preview: a lightweight renderer into `FlowDocument` (headings, bold, italic, strike, code, quotes, lists, links, task boxes). Persist to `%APPDATA%\Trayloom\scratchpad\pads.json`; migrate the existing `notes.json`. The island is non-activating: while this page is shown and the pointer is inside, call `SetForegroundWindow`/activate the island window (remove `WS_EX_NOACTIVATE` temporarily) so typing works; restore the previous foreground window on collapse. Export via `SaveFileDialog` (`.txt`).

#### 4.6 Gap vs Trayloom
`ScratchpadFeature` only provides floating `NoteWindow`s (one window per note, hotkeys Ctrl+Alt+N / new note, "Delete this note?" MessageBox). Missing: island tab, pads-as-tabs model with names and 12-pad limit, rename, tab strip/menu, Markdown format bar and preview, copy all, export, clear with undo, retention, text size, load/save-failure states, close confirmation text.

---------------------------------------------------------------------------------------------------

### 5. NOTIFICATIONS (mirror of system banners), NOTICE and ALERTS

Sources: `UI/Notch/NotchNotificationsView.swift`, `NotchNoticeView.swift`, `Services/Notch/NotchNotificationService.swift`, `NotchNotificationReader.swift`, `NotchNotificationReaderCore.swift`, `NotchNotificationSupport.swift`, `Core/NotchNotificationStrings.swift`, `UI/Notch/NotchIslandAlert.swift`, `NotchService.show(_:)`.

#### 5.1 What it does
It mirrors **new, visible** system notification banners into the island: a banner notice in the closed island (app icon + title on one side, message on the other), a Notifications page with an inbox of cards (this unlocked session only, max 50), and optionally hides the original system banner while the island shows it. Nothing is read from any notification database; messages live only in memory and are dropped when the Mac locks or the feature is turned off.

#### 5.2 How notifications are read on macOS (exact mechanism)
1. Permission: **Accessibility** (`AXIsProcessTrusted`). Without it the page shows a permission row (`PermissionRow(kind: .accessibility)`) plus the privacy text; the service does not run.
2. The service finds the running process `com.apple.notificationcenterui` (Notification Center's UI process). It re-attaches when apps launch or terminate (`NSWorkspace.didLaunch/didTerminate`).
3. It creates an `AXObserver` on that process and subscribes to `AXWindowCreated`, `AXLayoutChanged`, `AXUIElementDestroyed`, `AXFocusedWindowChanged`. A window-created event triggers a read 30 ms later (so a hidden original can leave before it slides in); every event schedules a read debounced 120 ms (reads are serialised; one pending rescan).
4. A read (`NotchNotificationReaderCore.read`) runs on a utility queue with a time/node budget and a 0.1 s accessibility messaging timeout per element:
   - If the process has a **focused window**, the Notification Center panel itself is open: nothing is imported and any windows the reader moved are restored (existing items in an opened center are not "new").
   - Otherwise it walks the process's windows (aborts if more than 32), looking for roots with role `AXNotificationCenterBanner` / `AXNotificationCenterAlert` and stacks (`AXNotificationCenterBannerStack` / `AXNotificationCenterAlertStack`) whose cards it unpacks.
   - Text is taken only from `AXStaticText` nodes by `AXIdentifier`: `header` (app name), `title`, `subtitle`, `body` (an unlabelled group of 2-3 text nodes is read as title / [subtitle] / body). More than one title or body means a stack of messages and is not imported as a single message. The app label can also be recovered from the node's `AXAttributedDescription` by removing the exact ", title, subtitle, body" suffix. Each text is capped at 16 KB; controls, widgets and editable fields never become content.
   - Identity: the root's `AXIdentifier` that contains a UUID (`nativeIdentity`); same identity + same content = same notification (keeps its id and received time); duplicated identities mark items as not openable. `canOpen` = the root offers the `AXPress` action.
5. The source app is resolved by display name against running apps, then installed apps (walk refreshed at most every 10 minutes); only an unambiguous match counts. Its icon is cached per app name; unknown -> `bell.badge.fill` glyph in a rounded tile (radius = 24% of size, white 9% fill, glyph 48% of size, white 85%).
6. The inbox (`NotchNotificationInbox`) treats the first read as baseline (banners already there are not arrivals); each later new id is inserted at the top (max 50, oldest removed) and published on `received` -> the island shows a notice. Items no longer live are kept but `canOpen = false`. Dismiss removes one; Clear All empties only the island's inbox (Notification Center keeps its own).
7. Open: performs `AXPress` on the banner root if it is still the same native target with the same content (revalidated); otherwise falls back to launching the source app by bundle id. Results `handedOff | openedApplication | unavailable | uncertain`; unavailable/uncertain shows "This notification can no longer accept this action." (orange caption on the card).
8. Hiding the system banner (option off by default, `notchDismissNativeNotifications`): when a banner arrives and the island shows it (island closed, no capture controls, no drag), the original banner window is **moved off-screen** (macOS 27, where the center keeps banners in one window: `AXPosition` set outside all displays, remembered and restored when the option turns off, the feature stops, the center opens or the app quits); on earlier systems the original is **closed** with the localized `Close` accessibility action after a 1.2 s grace so short sounds finish. Windows that also hold an alert the island could not read stay in sight.
9. Limits: max 50 items; windows <= 32; accessibility timeout 0.1 s; identity string <= 2048 bytes; source labels <= 256 bytes; descriptions <= 50,000 bytes.

#### 5.3 Layout
**Page** (`NotchNotificationsView`):
- No Accessibility permission: vertical stack spacing 10: permission row, then the privacy text (callout, white 70%): "Show new visible banners only. Messages stay in memory and are cleared when you lock this Mac or turn this off."
- Empty inbox: horizontal, spacing 14: glyph tile 56 x 56 (bell 26 pt light, white 60%, radius 18, white 5% fill) + a column with a headline "New notifications will appear here" (if the service is monitoring) or "Waiting for the system notification service", and below it the privacy text (caption, white 65%).
- With items: a rail (`NotchRail`) of cards, item width 240, spacing 8, rows computed from page height (row height 120 minimum), card height = (page height - (rows-1) x 8) / rows.
- Band trailing: trash icon button titled "Clear All" (disabled when empty or while an open is in flight): confirmation above the island, title "Clear All", no message, destructive action "Clear All", plus "Cancel".

**Card** (`NotchNotificationRow`; padding 12, radius 14, white 6% fill, clipped): header row height 22, spacing 6, 11 pt, white 60%: app icon 18 + app name (or "Notifications" when empty) 1 line, spacer, time `h:mm` (monospaced digits), then (when openable) a mini progress ring while opening or the icon button `arrow.up.forward.app` titled "Open", and `xmark` titled "Dismiss". Then title 13 pt semibold (2 lines), subtitle 12 pt (1 line), body 12 pt white 80% (fills the remaining height, selectable). Orange caption (1 line) for "unavailable".

**Closed banner notice** (`NotchNoticeView` with `notification`): leading wing: app icon (22 pt, shrinks to stripHeight - 4) + 8 gap + title (`compactTitle` = title or app name) 11 pt semibold, 1 line; trailing wing: `compactDetail` = subtitle + " · " + body, 11 pt, white 85%, right-aligned, 2 lines if strip height >= 30 else 1. Wings: each side as wide as needed: leading = 22 + 8 + title width; trailing = message width (wrapped to the widest wing when it wraps); + inset 16 + air 6; clamped to 36...190; a replacing banner keeps at least the wings of the one it replaces (no resize flutter). Duration 3 s.
- **Held preview**: if the pointer rests on the banner (hover delay = `notchHoverDelay`, default 0.25 s, range 0.10...1.0, requires `notchOpenOnHover`), the timer stops and the notice expands in place to the whole message (`NotchNotificationPreviewView`): header 28 pt (app icon 26, app name 12 pt semibold white 70%, spacer, time 11 pt white 45%, `xmark` "Dismiss" which also removes it from the inbox), title 14 pt semibold (2 lines), subtitle 13 pt medium white 90% (1 line), body 13 pt white 82% (6 lines, selectable), action row 28 pt: small bordered button "Open" (when openable), ring while opening, spacer, and a count pill (tray.full glyph + number, capsule 24 high, 11 pt semibold, white 80%) that opens the Notifications page when more than one message is stored. Block spacing 8. Leaving closes the preview; a quick pass over the banner restores its full 3 s.
- Clicking the banner = Open (press the native banner and close the notice; if unavailable/uncertain open the Notifications page).

#### 5.4 Strings (English `NotchNotificationStrings`)
title "Notifications"; description "New system notifications in the Dynamic Island."; privacy "Show new visible banners only. Messages stay in memory and are cleared when you lock this Mac or turn this off."; empty "New notifications will appear here"; waiting "Waiting for the system notification service"; open "Open"; dismiss "Dismiss"; clearAll "Clear All"; unavailable "This notification can no longer accept this action."; hideSystemBanner "Hide the system banner"; hideSystemBannerHint "Hide the original while the Dynamic Island shows it."
Windows wording: privacy "Show new visible banners only. Messages stay in memory and are cleared when you lock this PC or turn this off."; waiting "Waiting for the Windows notification service"; permission row "Notification access" with button "Allow" (consent / Settings).

#### 5.5 Settings
| key | type | default |
|---|---|---|
| `notchNotificationsEnabled` | bool; also the Notifications section visibility and the notice pref | true (false on migrated old profiles) |
| `notchDismissNativeNotifications` | bool, "Hide the system banner" | false |
| `notchOpenOnHover` / `notchHoverDelay` | bool / seconds 0.10...1.0 | island-wide (delay default 0.25) |
Extension: `AppFeature.notchNotifications` (symbol `bell`, permission Accessibility). Enabled = island on + extension installed + key true + Notifications module visible. In the sections editor, toggling the Notifications section writes `notchNotificationsEnabled`. Settings tab "Notifications" (under the Dynamic Island page) contains the switch row (icon `bell.slash`, title "Hide the system banner", caption "Hide the original while the Dynamic Island shows it.") and, if enabled without permission, the Accessibility permission row.

#### 5.6 Windows mapping - reading notifications
**Primary: WinRT `Windows.UI.Notifications.Management.UserNotificationListener`.**
- `var listener = UserNotificationListener.Current; var status = await listener.RequestAccessAsync();` (`UserNotificationListenerAccessStatus`: Allowed / Denied / Unspecified). `GetAccessStatus()` reads the current state without prompting. The user can change it in Settings > Privacy & security > Notifications (URI `ms-settings:privacy-notifications`); add a permission row "Notification access" with Allow / Open Settings buttons mirroring the Accessibility row.
- Read: `await listener.GetNotificationsAsync(NotificationKinds.Toast)` returns `UserNotification`s: `Id`, `CreationTime`, `AppInfo.DisplayInfo.DisplayName` (= header/app name), `AppInfo.DisplayInfo.GetLogo(new Size(32,32))` (icon), `AppInfo.AppUserModelId`, `Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric).GetTextElements()` -> first = title; with 3 elements second = subtitle and third = body; with 2 elements second = body (matches the Mac mapping of 2-3 unlabelled text nodes). Remove from Action Center with `listener.RemoveNotification(id)` only if a "Also clear in Action Center" option is added; the Mac "Dismiss" only removes from the island's inbox.
- Live updates: `listener.NotificationChanged` event (args `ChangeKind` Added/Removed, `UserNotificationId`) -> on Added fetch `GetNotification(id)`. Baseline: read the current list once at start and never import it (mirrors "first read is baseline"). Keep the last 50 in memory; clear the inbox on `WTS_SESSION_LOCK` and on stop.
- Open: Windows has no "press the banner" API. Closest: launch the source app by AUMID: `Process.Start("explorer.exe", "shell:AppsFolder\\" + aumid)` (works for packaged apps and Start-menu shortcuts). If the AUMID is unknown show "This notification can no longer accept this action.".
- **Package identity requirement (critical):** the listener needs the `userNotificationListener` capability, which exists only in a package manifest. Trayloom's unpackaged WPF process has no package identity, so `RequestAccessAsync` fails (exception or `Denied`, no consent prompt) and `NotificationChanged` is not delivered to unpackaged desktop apps (Microsoft documents a background trigger for packaged apps; polling `GetNotificationsAsync` is the fallback). To use the API Trayloom must run with identity: (a) ship as MSIX with `<uap3:Capability Name="userNotificationListener"/>` and `runFullTrust`; or (b) keep the current installer and register a **sparse package** (`Add-AppxPackage -Register` with an external location) whose manifest declares the capability. With identity the first `RequestAccessAsync` call shows the system consent dialog (call it from the UI thread; initialise with the window handle). This is a packaging decision for the product owner, not just code. Verify behaviour on the target Windows build before committing.
- **Fallback when no identity: UI Automation of the toast popup** (analogue of the Mac Accessibility method). Windows draws banners in a shell-hosted XAML window (`Windows.UI.Core.CoreWindow` named "New notification" hosted by `ShellExperienceHost.exe` on Windows 10 and early Windows 11; the exact class/title/process changed between builds and must be verified on the target build). Subscribe with UI Automation `WindowOpenedEvent` / `StructureChangedEvent` on the desktop root (or poll every 250 ms while enabled), read the `Name` property and child `Text` elements (often one concatenated Name "App, title, body": split with the same "remove exact suffix" idea), use `InvokePattern`/click on the element to open. Mark it best-effort (language and build dependent) and show a caption in the settings row: "Reading banners works best when Trayloom is installed as an app."
- **Hiding the original banner** (`notchDismissNativeNotifications`): no supported API exists. Optional and fragile: move the toast window off-screen with `SetWindowPos` the instant it appears (and restore on stop/disable with the same discipline as the Mac), or dismiss it through UI Automation. Do not toggle Focus Assist / Do Not Disturb (system setting). If unavailable, disable the switch and keep the hint with a footnote "Not available on this PC".
- Privacy: never read `wpndatabase.db` (the Mac design explicitly reads no notification store).
- Notice delivery: `received` -> extended `IslandHub.ShowAlert` (app logo + title + message wings, 3 s, priority 1, held preview on hover; see 0.2).

#### 5.7 Alerts raised from the island
`NotchIslandAlert.run` / `confirmAboveIsland`: any confirmation started from an island page (Clear All, Clear unpinned, close a non-empty pad, rename pad) is a modal dialog placed above the island window; the island does not collapse while it is up (`keepsSurfaceOnClick`) and takes key back after. Windows: show the dialog `Topmost`, hold the island open via `HoldOpen()`, same monitor as the island, Escape cancels.

#### 5.8 Gap vs Trayloom
No notification mirroring, page, inbox, banner notice, held preview, permission flow or settings. `IslandAlert` shows one line of text with a glyph and colour for a fixed duration; no app icon, title/message wings, count pill or hover-expand. Needs: listener service (packaging decision), `NotificationItem` model, rail/cards UI, richer alert type, settings (`enabled`, `hideSystemBanner`), permission row.

---------------------------------------------------------------------------------------------------

### 6. LOCK SCREEN

Sources: `UI/Notch/NotchLockScreenView.swift`, `Services/Notch/NotchLockScreenService.swift`, `NotchLockScreenSupport.swift`, `Core/NotchLockScreenStrings.swift`, `NotchSessionState` (`NotchSupport.swift`), sound hook in `NotchService` session change.

#### 6.1 What happens on macOS when the Mac locks
The island keeps living on the lock screen. When the session locks (display awake, user on the console, no screen saver: `showsLockScreen`), the service builds up to three borderless panels in a private window-server Space at level 400 (`NotchOverlaySpace(absoluteLevel: 400)`, one step above the lock screen's own 300, the only way to be drawn over the lock screen) and fades them in over 0.35 s:
1. **Island with padlock** around the camera: black notch shape of the camera's exact height, width = camera + 2 wings (the music strip's wing width, at most screen width - 24). A `lock.fill` glyph (13 pt semibold, white, symbol-replace transition with a bounce at speed 1.4) sits in the leading wing where the cover would be; the equalizer bars sit in the trailing wing while a song plays (tinted with the artwork tint). On locking at the Mac the padlock is drawn **open** and closes after 0.35 s (0.25 s smooth animation); coming back from a dark display it is simply closed. On unlock the padlock opens first and the island fades out 0.55 s later (after the real island is back underneath).
2. **Music player pane** (the only part that takes clicks; panel can never be key/main; first click accepted): centred between the clock row and the login controls: frame width 460, pane width 404, minimum height 190, pane = 34 pt radius clear Liquid Glass smoked with black 24% (50% in increased contrast; solid white 12% under Reduce Transparency), padding 26 x 20, white text. Cover 112 (or 84, or a compact 52 beside the title when short), title 18 pt bold (16 compact), artist/album 14 pt medium white 72% (13 compact), a seekable timeline, transport row: previous 22 pt, play/pause 30 pt, next 22 pt (spacing 46, row height 46, each button 48 x 44, radius 14), no permission prompts from here. Shown only while a song is playing or already played during this lock (`playedWhileLocked`, reset on unlock); the pane fades in/out with a 0.96 scale over 0.5 s; the cover scales to 0.92 while paused; a tap on play/pause shows the new icon for up to 1.5 s if the player answers slowly.
3. **Activities line** under the clock (`rowFrame`: width min(screen - 64, 1000), height 30, gap 18 under the clock; clock bottom = max(236, 24.7% of screen height); the player's bottom clears 250 from the screen bottom): items in order timer, calendar, agents, downloads, spacing 26, 15 pt semibold, monospaced digits, white 92%, black 30% shadow radius 8; drops its last items when too wide (tries 4, 3, 2, 1 entries). Entries: timer (orange glyph `timer` / `stopwatch.fill` / `pause.circle.fill` / `checkmark.circle.fill` + clock text, Pomodoro phase prefix; "Time is up" when it ended while locked), calendar event (`calendar` in event colour + title <= 240 wide + countdown white 70%), agents (agent glyphs + names joined by " · " + the reading; **no project names**), downloads (`arrow.down.circle.fill` blue + percent, or "Downloading"; **no file name**).
   - Preferences are frozen when the scene appears (`Gates`): music (Music module visible), timer, agents, downloads (`download` event routed), calendar countdown / time-left.
   - Sources the island stopped on lock (music, downloads, calendar, agents) are restarted for the scene and stopped again when the scene leaves while the Mac still cannot present.
4. The scene rebuilds when a display is attached or rearranged while locked and the computed frames change.
5. Unlock: the player and line fade out over 0.2 s via the window server (`CGSSetWindowListAlpha`, fallback AppKit) so they do not linger a second over the desktop; the companion plays `wakeUp`.
6. Lock/unlock **sounds**: the padlock sound from `SecurityInterface.framework` (`lock.aif` / `unlock.aif`), fallback `Tink.aiff` / `Pop.aiff`, played as a UI sound (plays even with interface sound effects off), only when someone is at the Mac (`hearsLockChange`: not sleeping, on the console, display awake, no screen saver); an unlock always counts as someone at the Mac.
7. Session state: `locked`, `sleeping`, `displaysSleeping`, `onConsole`, `screenSaverRunning`; `canRunTimer = !locked && !sleeping && onConsole`; `canPresent = canRunTimer && !displaysSleeping`; `showsLockScreen = locked && !sleeping && onConsole && !displaysSleeping && !screenSaverRunning`. The island, notification mirror and timers stop when it is false (a timer that reaches its end while locked is shown as finished on resume).

#### 6.2 Layout / strings (Settings > Dynamic Island > Lock Screen card, below Feedback)
Card title "Lock Screen". Row 1: icon `lock.display`, label "Show on the Lock Screen", caption "Music and the Dynamic Island’s activities, like a timer, AI agents, downloads and your next event, appear above the password field. Anyone who can see your screen can read them.", switch. Row 2: icon `speaker.wave.2`, label "Lock and unlock sounds", caption "Play the macOS padlock sound when your Mac locks and unlocks.", switch. `working` = "Working" (accessibility word beside agent names). Windows wording for row 2: "Play a padlock sound when your PC locks and unlocks."

#### 6.3 Settings
| key | type | default |
|---|---|---|
| `notchLockScreen` | bool | false |
| `notchLockSounds` | bool | false |
Enabled = island enabled + key. Constants: space level 400, fade in 0.35 s, padlock closes after 0.35 s, scene fade-out 0.2 s, island fade-out delay 0.55 s, activities row gap 18 / height 30, login clearance 250, player width 460, pane width 404, minimum player height 190.

#### 6.4 Windows mapping (what is and is not possible)
**Impossible:** a normal desktop process cannot draw on the Windows lock screen or sign-in screen. When the session locks, the interactive desktop switches to the secure `Winlogon` desktop; windows of the user's desktop (including topmost, layered and even `UIAccess` ones) are not shown there, and only SYSTEM-owned shell components render. Windows lock-screen widgets and "lock screen status" are Microsoft-only/limited and not scriptable; credential providers must not be abused for status. Trayloom must not try to replace or imitate the lock screen (security risk, unsupported). The lock notification also arrives after the desktop switched, so nothing can be drawn over the user's desktop "just before" a Win+L / idle lock.

**Closest equivalents to implement:**
1. Session notifications: `WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION)` -> `WM_WTSSESSION_CHANGE` with `WTS_SESSION_LOCK` (7), `WTS_SESSION_UNLOCK` (8), `WTS_CONSOLE_CONNECT/DISCONNECT` (1/2), `WTS_REMOTE_CONNECT/DISCONNECT` (3/4); or `Microsoft.Win32.SystemEvents.SessionSwitch` (SessionLock / SessionUnlock / ConsoleDisconnect). Sleep/resume: `SystemEvents.PowerModeChanged` (Suspend / Resume). Display on/off: `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)` (0 off, 1 on, 2 dimmed). Screen saver: `SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)`. Map them to the `NotchSessionState` fields exactly as above and gate the island, timers, notification mirror, Watch and presentation with `canPresent` / `canRunTimer`.
2. **Unlock greeting:** on `WTS_SESSION_UNLOCK` (after ~300 ms settle) the island shows a short greeting notice: padlock glyph E72E replaced by E785 with a bounce (0.25 s) in the leading wing for about 1.2 s, then the normal island; companion `wakeUp`; if a timer finished while locked show "Time is up". This replaces the Mac's island-on-the-lock-screen with an island-on-unlock moment.
3. **Lock moment:** only when the lock is started by Trayloom itself (tray/hotkey action calling `LockWorkStation()`), show the padlock closing for ~350 ms first, then lock. Win+L, idle lock and Dynamic Lock have no hook.
4. **Music on the lock screen already exists natively:** Windows shows System Media Transport Controls (SMTC) on the lock screen for any app that publishes them. Do not replicate the player pane; Trayloom keeps reading SMTC sessions (existing `MediaService`).
5. **Lock sounds:** audio from the user session keeps playing over the lock screen, so sounds are implementable: on `WTS_SESSION_LOCK` play the lock sound, on `WTS_SESSION_UNLOCK` the unlock sound. Windows has no padlock sound: bundle two short WAV assets (`lock.wav`, `unlock.wav`) or fall back to `C:\Windows\Media\Windows Lock.wav` / `Windows Unlock.wav` when present, else `SystemSounds.Asterisk`. Honour `hearsLockChange`. The lock sound will play just after the lock.
6. On lock: clear the in-memory notification inbox, suspend the Watch loop (captures are blocked), keep Downloads watching without presenting, and compute timers from absolute deadlines.
7. Settings UI: keep the "Lock and unlock sounds" row fully functional. Show the "Show on the Lock Screen" row disabled with the caption "Windows doesn't allow apps to draw on the lock screen. Trayloom shows a greeting when you unlock instead." (or hide the row if the owner prefers); reuse key `notchLockScreen` (default false) to enable the unlock greeting.

#### 6.5 Gap vs Trayloom
Nothing exists: no session-state service (the island reacts only to full-screen foreground via WinEvent and `SystemEvents.DisplaySettingsChanged`), no padlock animation, sounds, activities line, `NotchSessionState`, or the two settings. `IslandFeature` has no suspend/resume handling.

---------------------------------------------------------------------------------------------------

### 7. MASCOT / COMPANION

Sources: `UI/Notch/NotchMascotView.swift`, `Services/Notch/NotchMascotSupport.swift`, `UI/Settings/NotchMascotSettings.swift`, `Core/NotchMascotStrings.swift`, hooks in `NotchService.swift` (visits, reactions, tuck/hide, notice bridge), `NotchNoticeView.swift`. In the UI it is called "Companion"; in code "mascot" (the word "companion" in code also names a second activity sharing the closed island).

#### 7.1 What it is
A small friend that lives in the closed island: a coloured shape with two ink eyes and no mouth (style **Minimal**) or a little **Robot**. It rests beside the camera (here: in the pill) when the island has nothing else to show, strolls through now and then ("visits"), comes out over music/activities to react to events ("reactions"), can hide inside the island after 30 s of quiet, is petted by resting the pointer on it, stands in for the symbol in notices, and is the face of the Command Bar when the bar comes out of the island. It is an optional extension (`AppFeature.notchMascot`, symbol `face.smiling`, no permission, off by default, installed separately from the island's initial set). It never reads anything from the system (no permission).

#### 7.2 Looks
- **Style** `NotchMascotStyle`: `minimal`, `robot`. Default minimal.
- **Shape** (minimal only) `NotchMascotShape`: `ball`, `egg`, `squircle`, `pill`. Default ball. Body outlines in a unit square scaled by the companion size S (origin top-left): ball = ellipse in rect (0.08, 0.12, 0.84, 0.84); egg = cubic path from (0.5,0.10) via (0.71,0.10)(0.86,0.36)->(0.86,0.60), (0.86,0.80)(0.70,0.96)->(0.5,0.96), (0.30,0.96)(0.14,0.80)->(0.14,0.60), (0.14,0.36)(0.29,0.10)->(0.5,0.10); squircle = 72-point superellipse x = 0.5 + 0.43*sgn(cos t)*|cos t|^(2/3.4), y = 0.55 + 0.40*sgn(sin t)*|sin t|^(2/3.4); pill = rounded rect (0.05, 0.28, 0.90, 0.60) with corner radius 0.30 S.
- **Robot** parts (rects as x, y, w, h shares of S, radii shares): head (0.12, 0.26, 0.76, 0.68) r 0.20; ears (0.05,0.50,0.08,0.20) and (0.87,0.50,0.08,0.20) r 0.035; antenna (0.475,0.12,0.05,0.16) r 0.025; bulb ellipse (0.43,0.04,0.14,0.14); visor (0.20,0.44,0.60,0.38) r 0.15.
- **Palette** `NotchMascotPalette` (order in settings: pearl, lemon, peach, rose, lilac, sky, mint; the strings list them as pearl, mint, peach, lilac, lemon, rose, sky). Default pearl. sRGB (light top -> shade bottom, vertical linear gradient, start (0.5,0), end (0.5,1)):
  | palette | light | shade |
  |---|---|---|
  | pearl | 0.99, 0.98, 0.96 | 0.84, 0.85, 0.90 |
  | lemon | 1.00, 0.96, 0.64 | 0.97, 0.83, 0.30 |
  | peach | 1.00, 0.86, 0.74 | 0.98, 0.64, 0.50 |
  | rose | 1.00, 0.80, 0.87 | 0.95, 0.54, 0.68 |
  | lilac | 0.88, 0.83, 1.00 | 0.69, 0.59, 0.96 |
  | sky | 0.77, 0.90, 1.00 | 0.43, 0.67, 0.97 |
  | mint | 0.72, 0.97, 0.86 | 0.40, 0.83, 0.66 |
  Minimal: eyes `ink` (0.07, 0.07, 0.08); a white highlight ("shine", alpha 0.55) as a small rotated ellipse. Robot: body gradient on the head, ears/antenna in `shade`, bulb = palette light (shade while sleepy), bulb glow shadow radius max(0.8, 0.09 S) in light colour, **lit eyes** in palette light with glow (shadow opacity 0.9, radius max(0.6, 0.04 S)) on the visor `(0.09, 0.09, 0.11)`; no shine.
- **Face layout** (shares of S; eye width x height; shine centre; shine radii; shine angle rad):
  | look | left eye | right eye | eye size | shine centre | shine radii | angle |
  |---|---|---|---|---|---|---|
  | robot | (0.36,0.63) | (0.64,0.63) | 0.085 x 0.16 | (0.27,0.34) | 0.07 x 0.04 | -0.3 |
  | ball | (0.36,0.50) | (0.64,0.50) | 0.10 x 0.20 | (0.31,0.29) | 0.085 x 0.055 | -0.5 |
  | egg | (0.375,0.55) | (0.625,0.55) | 0.095 x 0.19 | (0.33,0.31) | 0.075 x 0.05 | -0.6 |
  | squircle | (0.35,0.53) | (0.65,0.53) | 0.10 x 0.20 | (0.24,0.28) | 0.08 x 0.05 | -0.55 |
  | pill | (0.37,0.57) | (0.63,0.57) | 0.09 x 0.19 | (0.20,0.42) | 0.07 x 0.045 | -0.45 |
  Each eye lives in a 0.4 S square centred on its position. The figure's visual middle is offset from the box middle (`figureOffset`) so it is centred on its body, not on the antenna's room.
- **Eye shapes** (`NotchMascotEye`), all as closed outlines resampled to 24 points so any can morph into any in 0.22 s: `open` (stadium w x h), `closed` (stadium 1.5w x 0.45w, y +0.12h), `squint` (stadium w x 0.55h, y +0.08h), `wide` (circle radius 0.8w, y -0.02h), `happy` (smiling arch: span 2.1w, rise 0.42h, thickness 0.55w, top -0.18h), `heart` (classic heart curve, 2.0w x 1.8w), `sleepy` (flat lid on top, round bottom: width 1.25w, depth 0.38h), `determined` (open eye cut by a slanted line from (-w/2, -0.30h) to (w/2, +0.02h); the right eye mirrors). Right eye = mirror of left.
- **Moods** (`NotchMascotMood`) and their expressions (left eye, right eye, gaze share of S, head tilt radians clockwise): idle (open, open, 0,0, 0); happy (happy,happy, (0,-0.02), 0); wink (open,happy, 0, -0.08); love (heart,heart); sleepy (sleepy,sleepy,(0,0.03), 0.05); determined (determined,determined); surprised (wide,wide,(0,-0.02)); searching (open,open,(0.05,0)); thinking (squint,open,(0.06,-0.06), -0.10); confused (open,squint,(-0.03,0.01), 0.14); alert (wide,wide) used while Keep Awake holds the PC awake. Greetings (random): happy, wink, love.
- Size: in a closed strip of height H: beside a camera `max(12, min(20, H - 12))`; in a capsule (no camera; the Windows pill) `max(10, min(18, bodyHeight - 6))` (= 18 for the 34 DIP pill). In a notice: 18 (`noticeSize`). Settings preview tiles: style tile 30, shape tile 24, island samples 12.

#### 7.3 Where it lives / rest
- Resting place beside the camera: inside the wing, gap to camera `max(4, min(8, wing - size - 6))`; side `left` (default) or `right` mirrors every stroll. A capsule (no camera, i.e. Trayloom) has no sides: it rests in the **middle** of the pill; the Side setting is hidden.
- It rests only when the closed island has no other content: `idleContent == none`, no activity/notice, not in the Command Bar, not tucked. Settings "Resting" choice "Nothing" is labelled "Companion" when the companion is on and not set to hide when idle.
- With "Hide when idle" on (default), after 30 s (`hideDelay`) with nothing going on (retry 3 s if busy or under the pointer) it yawns (`linger(.yawn)` visit) and hops into the island (behind the camera, or out at a capsule's far end); the island then returns to its usual size. Visits and reactions bring it back out; the pointer coming back wakes it with a stretch.
- Eyes follow the pointer while it rests (`pointerGaze`: dx = (pointer.x - centre.x)/(3S), dy = (pointer.y - centre.y)/(2S), each clamped to +-1 then x 0.08 / x 0.05 of S); moves only occur while the pointer moves. Returning to the face 0.25 s after the pointer leaves.
- Petting: pointer resting on the figure's box (+-(S/2 + 3)) for 1 s triggers `love` (hearts), at most once per 30 s (`repeatInterval`), with a soft haptic tick on supporting hardware (Windows: none).
- Blink: next blink after a random 2.6...6.4 s (5.2...12.8 s in battery-saver/Low Power Mode). Single blink = y scale 1 -> 0.1 -> 1 over 0.17 s (key times 0, 0.45, 1); 18% are double blinks over 0.44 s (key times 0, 0.2, 0.4, 0.58, 0.78, 1). 25% of single blinks are preceded (1.1 s earlier) by a glance: eyes slide 0.12 S to a random side over 1.3 s (key times 0, 0.2, 0.8, 1), only if nothing drew the eyes in the last 3 s. After 36 blinks at rest (12 between 22:00 and 06:00) eyes grow heavy: mood becomes `sleepy` ("dozes off", robot bulb goes dark over 0.6 s) until anything happens (wake: mood idle with a `pop`, bulb relights over 0.25 s). Reduce motion: blink is a discrete close/open; no walks.
- Mood change: eye path morph 0.22 s with control points (0.2, 0.8, 0.3, 1); gaze: spring (damping 16, stiffness 260); tilt: spring (damping 12, stiffness 200); a new look at the same size cross-fades 0.24 s. Entering `surprised` plays `pop`; `confused` a `wobble` (rotation 0, 0.12, -0.1, 0.06, 0 rad over 0.5 s); `love` a `heartbeat` (eye scale 1, 1.3, 1, 1.22, 1 over 0.7 s, key times 0, .2, .45, .65, 1); idle after sleepy a `pop`.
- Squash helper: `squashed(a)`: scale x = 1 + 0.8a, y = 1 - a, translated so the bottom (0.42 S below centre) stays put. `pop` values a = 0, -0.14, 0.06, 0 at times 0, .35, .7, 1 over 0.34 s.

#### 7.4 Reactions (`NotchMascotReaction`) - what plays and for how long
| reaction | triggered by | length (s) | face / motion |
|---|---|---|---|
| celebrate | download complete, agent task finished, file dropped on the island, settled/shows/reached watch result, look change in settings | 1.0 | happy 1.0 s + hop (height 0.28 S, 0.62 s, squash 0,-.12,0,.14,0,.06,0) |
| love | plugged in / charged, petted | 1.4 | love 1.2-1.4 s + heartbeat starting 0.22 s in |
| surprised | plain timer rings | 0.9 | surprised 0.8-0.9 s, hop at 0.7 x lift |
| flash | screenshot taken | 0.5 | hard blink (0.45 s) / surprised flash |
| confused | download failed, watched window closed | 1.2 | confused 1.1-1.2 s with wobble |
| wakeUp | Mac/PC unlocked | 1.6 | sleepy 0.7 s, stretch (0.6 s, begins +0.35 s), then happy 0.9 s at +0.7 s; robot bulb relights after 0.35 s |
| yawn | Keep Awake let go, low battery, hiding | 1.2 | sleepy 1.1-1.2 s + stretch (0.9 s, from +0.1 s) |
| perk | Keep Awake took hold, microphone opened, watch "changed" | 1.0 | alert (wide eyes) 1.0 s + hop 0.55 x lift |
| bounce | countdown event began | 1.0 | alert for the whole length, two quick hops: rebound path 0.95 s with squash key times 0, .04, .14, .32, .36, .46, .63, .70, .80, .88 |
| hush | microphone muted | 1.0 | sleepy/eyes shut (0.95 s) + duck squash (0, .1, .1, -.03, 0) |
| ready | timer started, AI agent got to work, break over | 1.0 | determined 1.0 s + nod (squash 0, .1, -.06, 0 over 0.5 s) |
| groove | music started | 1.6 | happy for the whole length; bob on every beat (hop 0.45 x lift, lean +-0.11 rad, landing squash 0.07), sway |
Timer reactions (`timerReaction`): focus phase ended -> celebrate; short/long break ended -> ready; timer or stopwatch ring -> surprised. Power (`powerReaction`): plugged in -> love, charged -> celebrate, low -> yawn. Glow: the robot's bulb lights up with soft glow for the reaction's length (shadow opacity 0,1,1,0 at 0, .12, .7, 1).
- Reaction gate (`NotchMascotReactionGate`): no two reactions within 2.5 s; the same reaction not within 30 s; a reaction that cannot be shown waits at most 8 s (`patience`). Music: at most once per 600 s (`grooveInterval`), 1.2 s after the music starts (`grooveDelay`). AI agent start: at most once per 600 s. Event began: only if the countdown just ended within 60 s window and the event is new or ongoing.
- Reactions require the "React to what happens" setting (`reacts()`), play over the face the companion keeps, and with Reduce Motion only change face. A notice on screen that already shows the companion with that reaction plays it there and does not repeat it.

#### 7.5 Visits (strolls) `NotchMascotVisit.Kind`
Durations (`NotchMascotMotion`): `lap` 3.6 s (rest -> hop behind camera -> walk -> hop out to the far spot, hello 1.0 s, two idle bounces, walk off right (0.40 s), reappear left, hop home 0.52 s; greeting window 1.30...2.30 s, or 0.78...1.78 in a capsule); `pass` 3.4 s (in at the near end, hello at 0.55...1.55 s, behind camera, out at far end); `home` and `arrive` 0.62 s (hop from behind the camera to its place); `cameo(reaction)` = 0.5 + (reaction length + 0.3) + 0.32 s; `linger(reaction)` = (reaction length + 0.3) + 0.32; `countdown(total)` = total + 0.1 + 0.32 (it comes out over a timer's mark `countdownLead` = 5 s before the end, hops with every second); `retreat` 0.32; `farewell` 0.78 (glad hop then away); `cross` 1.24 (moving to the camera's other side: behind it, across, wink). Restyle/moment hops in the preview use `lap`.
- Next visit: random in the frequency range: rare 600...1200 s, normal 240...540 s (default), frequent 90...180 s. Turned on: first hello after 1.5 s (`welcomeDelay`) with a wink. Visits are skipped (rescheduled) while busy/hidden/out of room, and in Low Power Mode (battery saver) except when Settings asks. With Reduce Motion a stroll over an activity is replaced by a cameo (`celebrate`).
- Hop height scale `track.hop(share)`; hop lift never exceeds room below the island top.

#### 7.6 Settings (Dynamic Island settings > Companion tab; also shown as its own page)
**Header:** title "Companion" (title2 bold), hint text (callout secondary): "A little friend who lives in the Dynamic Island. It rests there when nothing else is showing, and comes out over music and activities to say hello and react to what happens.", master switch (`notchMascotEnabled`).
If the island is off/not installed: note card (radius 14, quaternary fill 35%): glyph tile + "The companion lives in the Dynamic Island." (+ reason when not installed) + button "Enable Dynamic Island" / "Open Features".
**Stage card** (radius 16, quaternary 35%, padding 16): a 104-pt tall preview of a menu bar with the island and the companion at rest (scaled to fit; desktop tinted with the accent colour 30% dark / 20% light; click it to act out the next reaction; hover pets it); below: headline "See how it reacts" with a small button "Say Hi" (icon `hand.wave`, tooltip "Plays a visit in the Dynamic Island now."; disabled unless the companion is on and the island is on), then wrapped chips (`FlowLayoutLite`, spacing 6) of moments (disabled when off): in order A visit, Timer started, Time is up, Now Playing, AI Agents, Happening now, Download complete, Download failed, Screenshot, Mic Mute on, Keep awake, Charging, Low battery, Mac unlocked (chip icon per moment: figure.walk, timer, alarm, music.note, sparkles, calendar, arrow.down.circle, exclamationmark.triangle, camera.viewfinder, mic.slash, cup.and.saucer, bolt, battery.25percent, lock.open). Caption row (caption, secondary, icon `hand.point.up.left`): "Click to see it react. Rest the pointer on it to pet it. Left alone, it dozes off until you come back." (dim 45% when off). Behaviours: turning on = wake-up stretch, off = yawn and doze; changing side = `cross` visit; turning visits on = immediate visit; turning reactions on = perk; turning hide on = shows it hiding then returning; new look = celebrate after 0.35 s.
**Appearance card** (title "Appearance"): row "Style" (icon `paintpalette`) with two tiles ("Minimal", "Robot"; each shows the figure 30 pt on a black capsule 64 x 42); if Minimal: row "Shape" (icon `circle.square`, value text) + four tiles (24 pt figure on black rounded rect 58 x 40 radius 10, 2 pt accent ring when selected): Ball, Egg, Rounded square, Pill; row "Color" (icon `swatchpalette`, value text) + seven round swatches (22 pt gradient circle with a 0.5 pt stroke, 3 pt padding, accent ring 2 pt when selected): Pearl, Lemon, Peach, Rose, Lilac, Sky, Mint. Selection animation 0.15 s.
**Behavior card** (title "Behavior"): if the island rests beside a camera (a real notch or the Notch silhouette; in a capsule it is hidden): row "Side of the camera" with tiles "Left" / "Right" (sample figure at that side); switch row "Hide when idle" (icon `eye.slash`) caption "After half a minute with nothing going on, it hops into the island and the island goes back to its usual size. Visits and reactions still bring it out."; switch row "Appear now and then" (icon `figure.walk`) caption "Every few minutes it passes through the island with a short animation."; when on a choice row "How often" (icon `clock`): Rarely / Sometimes / Often; switch row "React to what happens" (icon `theatermasks`) caption "It comes out to react to music, timers, downloads, screenshots, the microphone, Keep awake and more."
**Command Bar card** (title = the Command Bar page title): feature switch row (icon `command`) "Command Bar in the island" caption "With the shortcut, the Command Bar comes out of the island with the companion as its face. It reacts while you search."; when on and the Command Bar is installed: row "Opens as" (icon `drop`) with tiles "Drop" and "Open island".

#### 7.7 Strings (English `NotchMascotStrings`)
title "Companion"; hint (above); visits "Appear now and then"; visitsHint "Every few minutes it passes through the island with a short animation."; style "Style"; minimal "Minimal"; robot "Robot"; shape "Shape"; ball "Ball"; egg "Egg"; squircle "Rounded square"; pill "Pill"; color "Color"; pearl "Pearl"; mint "Mint"; peach "Peach"; lilac "Lilac"; lemon "Lemon"; rose "Rose"; sky "Sky"; commandBar "Command Bar in the island"; commandBarHint (above); opensAs "Opens as"; droplet "Drop"; openIsland "Open island"; preview "Companion preview"; previewHint "Click to see it react."; side "Side of the camera"; left "Left"; right "Right"; frequency "How often"; rare "Rarely"; normal "Sometimes"; frequent "Often"; hubDescription "A little friend in the Dynamic Island who says hello now and then and reacts to what happens on your Mac."; appearance "Appearance"; behavior "Behavior"; reactions "React to what happens"; reactionsHint (above); hidesWhenIdle "Hide when idle"; hidesWhenIdleHint (above); sayHi "Say Hi"; sayHiHint "Plays a visit in the Dynamic Island now."; momentsTitle "See how it reacts"; visitMoment "A visit"; timerStarted "Timer started"; downloadFailed "Download failed"; unlocked "Mac unlocked"; petTip "Rest the pointer on it to pet it. Left alone, it dozes off until you come back."; livesInIsland "The companion lives in the Dynamic Island."; keywords "mascot pet buddy character". Moment names borrowed from other strings: timeIsUp "Time is up", music "Now Playing", agents "AI Agents", eventStarts "Happening now", downloadFinished "Download complete", screenshot "Screenshot", micMuted "Mic Mute on", keepAwake "Keep awake", charging "Charging", lowBattery "Low battery". Windows wording: hubDescription "...on your PC.", unlocked "PC unlocked".

#### 7.8 Settings keys
| key | type | default |
|---|---|---|
| `notchMascotEnabled` | bool | false |
| `notchMascotVisits` | bool | true |
| `notchMascotReactions` | bool | true |
| `notchMascotStyle` | `minimal`\|`robot` | minimal |
| `notchMascotShape` | `ball`\|`egg`\|`squircle`\|`pill` | ball |
| `notchMascotPalette` | `pearl`\|`lemon`\|`peach`\|`rose`\|`lilac`\|`sky`\|`mint` | pearl |
| `notchMascotSide` | `left`\|`right` | left |
| `notchMascotVisitFrequency` | `rare`\|`normal`\|`frequent` | normal |
| `notchMascotHidesWhenIdle` | bool | true |
| `notchCommandBar` | bool | true |
| `notchCommandBarStyle` | `droplet`\|`island` | droplet |
| `notchMascotBetaInstalled` | bool marker | - (on beta builds with Command Bar + island the companion is installed and enabled once) |
`NotchMascotSupport.isEnabled` = extension installed + island on + `notchMascotEnabled`. Visits = enabled + visits; reactions = enabled + reactions. Command Bar style is active only if enabled, Command Bar installed and `notchCommandBar` true. Command Bar face moods: empty query -> idle; searching -> thinking; results -> idle; no results -> confused; the eyes look along the typed text (gaze x = 0.07 + 0.04 min(1, chars/28), y 0.01); a hop of celebration when results appear after thinking/confused.

#### 7.9 Windows mapping
- Render with WPF vector shapes (`Path` + `PathGeometry`) in a 1:1 square hosted in the pill; gradients via `LinearGradientBrush`; eye morphs via `PathFigure` point animation (24 points each, Catmull-style cubic like `smoothClosedPath`: control points = current +- (next - previous)/6). Use one `Storyboard` per reaction; hops = `TranslateTransform.Y` keyframes with ease-out/in; squash = `ScaleTransform` with `RenderTransformOrigin` at the bottom (0.5, 0.92). Tilt = `RotateTransform`. Gaze = `TranslateTransform` on the face group. No timers except one pending blink/visit callback (keep CPU near zero; stop animations when the pill is hidden/suppressed or the display is off).
- Capsule geometry (Trayloom has no camera): rest in the pill's middle; strolls: `pass`/`lap` enter at the left end, hop to the right end, leave, as the capsule variants in the Swift track (`floats == true`: rest = width/2, hidden = nil, baseline = height/2, ceiling = (height - body)/2). Hide = walk out the far end.
- Reduce motion: `SystemParameters.ClientAreaAnimation == false` -> faces only. Battery saver: `SystemInformation.PowerStatus.BatteryLifePercent`/`SystemParametersInfo`/`Windows.System.Power.PowerManager.EnergySaverStatus` -> blink 5.2...12.8 s and no visits.
- Reaction sources to wire: download complete/failed (section 1), timer start/finish by phase (Timer feature), music start (MediaService playing transition, 10-minute limiter), agents (if installed), calendar event began (Calendar feature countdown), screenshot (Screenshot feature), mic mute/unmute (MicMute feature: hush/perk), Keep Awake on/off (perk/yawn; alert mood while active), battery plugged/charged/low (`SystemEvents.PowerModeChanged` + `PowerStatus`), unlock (`WTS_SESSION_UNLOCK`: wakeUp, see section 6), watch outcomes (section 8), file dropped on the island (celebrate, delayed up to 30 s patience), Command Bar typing (moods above).
- Notice integration: when enabled and the notice carries a reaction, replace the notice symbol by the companion (size 18, offset y +0.5) which plays the reaction; keyed by event+title+detail+reaction so a different notice swaps it at once (no crossfade between two companions).
- Settings tab "Companion" is a new settings page with the stage preview (reuse the same rendering control at 3-4x size in a 104 pt tall card); strings above. Command Bar integration uses the existing `CommandBarFeature`; implement `droplet` (small drop falls from the pill's lower edge and expands into the bar below) and `island` (bar inside the expanded island) as presentation modes.

#### 7.10 Gap vs Trayloom
Nothing exists: no companion rendering, moods, reactions, visits, hide-when-idle, petting, settings page or Command Bar integration; `IslandAlert` cannot host a companion; there is no reaction bus. `CommandBarFeature` exists but shows only its own window.

---------------------------------------------------------------------------------------------------

### 8. WATCH

Sources: `UI/Notch/NotchWatchView.swift`, `Services/Notch/NotchWatchService.swift`, `NotchWatchSupport.swift`, `Core/NotchWatchStrings.swift`.

#### 8.1 What "Watch" is
Watch turns **any part of any window** (or a fixed part of a display) into a live activity in the island. The user drags a rectangle over something on screen - a progress bar, a build status, a score, a download dialog. The app then looks at that area every second or two (on the PC itself, nothing leaves it), reads its text with OCR (or a coarse picture when it has no text), shows the reading in the closed island (an eye mark on one side, the reading on the other) and speaks up - sound, notice, companion reaction, and a system notification if the island cannot be shown - once a rule the user picked is met. The window is followed wherever it goes: moved, covered, minimised-to-hidden state or on another Space/desktop; if it closes the watch ends.

Extension: `AppFeature.notchWatch` (symbol `eye`, permissions Screen Recording + Notifications). Installed by default with the island unless the user left the island out. `monitorsPermissionChanges = false` (Watch asks only when an area is chosen and checks on every read).

#### 8.2 Layout
**Settings controls** (`NotchWatchSettingsControls`, under Dynamic Island settings, row "Watch", spacing 10, trailing switches): Toggle "Watch" (`notchWatchEnabled`, disabled if not installed); caption (the description); when enabled: Toggle "Play a sound" (`notchWatchSound`) and the Screen Recording permission row.

**Page** (`NotchWatchView`), two bodies:
1. **Setup** (when disabled, nothing watched, or in the settings preview): centred, max width 360, padding 12, `ViewThatFits` vertical (glyph over copy over actions) else horizontal (glyph + column, spacing 16). Glyph tile 56 x 56 radius 18 white 5% with `eye` 26 pt light white 65%. Copy 12 pt white 65%: disabled -> description "Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for."; enabled with Screen Recording -> setupHint "Choose any part of a window, like a progress bar, a build status or a score. The island reads it for you and tells you when it changes."; enabled without permission -> permissionHint "Watch reads the area you choose on your Mac, so it needs Screen Recording. Nothing leaves your Mac." Actions: disabled -> Toggle "Watch" (switch); no permission -> prominent pill "Allow Screen Recording"; else prominent pill "Choose Area". The page follows a permission granted in system settings while it is visible (polling only while shown).
2. **Watching** (vertical stack spacing 10): area card (fills), rule row, footer. Band leading: the watched app's icon (16) + app name semibold, and, when the window has a different title, ` · window title` in secondary, 12 pt, white 85%, 1 line middle truncation (tooltip "App · Title"); band trailing: icon buttons `viewfinder` titled "Choose Another Area" and `xmark` titled "Stop Watching".
   - **Area card** (padding 10, radius 14, white 7%): the live picture (aspect fit, radius 8; opacity 35% when hidden; a spinner before the first read), and, over a hidden picture, a label (caption, centred, on black 60% radius 8): `lock` + permission hint if the permission is missing, else `eye.slash` + "The window is hidden. Watching continues when it returns." Below: when finished a green check (grey xmark when the window closed) + outcome text 13 pt semibold; else if no text "No text" 15 pt semibold secondary; else the **headline menu**: the current reading 15 pt semibold monospaced digits + chevron; the menu has a section "Show in the island" with "Automatic" and up to 12 detected lines (check on the chosen one). Below it, if the full text differs from the headline: the whole text on one line, caption secondary, 2 lines, selectable.
   - **Rule row**: caption "Tell me" + a small borderless menu of the four conditions (each with its glyph) + for "When it shows" a text field (placeholder "Text, like Done") or for "When the number reaches" a field ("Number, like 100"); field: 12 pt, height 24, max width 180, padding 8, radius 7, fill white 8%, focus stroke white 34% 1 pt. For "When it stops changing" a caption2 line "After it changes, it must stay still for 30 seconds." Typed values apply on Return or when the field loses focus (so typing "1" on the way to 100 cannot end the watch early).
   - **Footer**: when finished: prominent pill "Watch Again" (only if the window still exists / region capture is alive) and a pill "Choose Another Area" (prominent when Watch Again is unavailable); else caption2 secondary "Watching since 3:45 PM" (`sinceFormat` with a short time).

**Closed-island strip** (`NotchWatchStrip`): left wing: purple (`Color.purple`; eye tint = system purple at 75%) `eye` glyph that "breathes" like a working agent's mark (slashed `eye.slash`, still, while the window is hidden), shown when the wing >= 28; right wing (>= 36): the thumbnail of the area (40 wide, height = strip - 6, min 8, radius 3, aspect fit) when the area has no text, else the headline in purple 10-11 pt medium monospaced digits (min scale 0.7, 1 line, tail truncation), or a mini spinner while it has no reading (nothing when hidden). Wing range 56...150. Click opens the Watch page. Accessibility: label "Watch", value = headline, hint "Open Dynamic Island".

#### 8.3 Strings (English `NotchWatchStrings`)
title "Watch"; description (above); setupHint (above); choose "Choose Area"; chooseAgain "Choose Another Area"; purpose "Choose what to watch" (the selector's prompt); stop "Stop Watching"; watchAgain "Watch Again"; permissionHint (above); allowAccess "Allow Screen Recording"; tellMe "Tell me"; changes "When it changes"; settles "When it stops changing"; contains "When it shows"; reaches "When the number reaches"; settlesHint "After it changes, it must stay still for 30 seconds."; textPlaceholder "Text, like Done"; numberPlaceholder "Number, like 100"; noText "No text"; inIsland "Show in the island"; automatic "Automatic"; hidden "The window is hidden. Watching continues when it returns."; changedNotice "It changed"; settledNotice "It stopped changing"; showsFormat "It shows %@"; reachedFormat "It reached %@"; closedNotice "The window closed"; sinceFormat "Watching since %@"; sound "Play a sound".
Windows wording: permissionHint "Watch reads the area you choose on your PC. Nothing leaves your PC." (no permission needed, see 8.6), so `allowAccess` is not shown.

#### 8.4 Settings
| key | type | default |
|---|---|---|
| `notchWatchEnabled` | bool (also the `watch` notice pref and Watch section visibility) | true |
| `notchWatchSound` | bool "Play a sound" | true |
| `notchWatchCondition` | `changes`\|`settles`\|`contains`\|`reaches` | changes |
Not persisted: the target area, typed text/number, chosen headline line (per session). Enabled = island on + extension installed + `notchWatchEnabled` + Watch module visible.

#### 8.5 Behaviour
- Choose area (`chooseArea`): requires enabled, no other selection in progress and Screen Recording access (`CGPreflightScreenCaptureAccess`, otherwise requests it and stops). Runs the screenshot region selector in "geometry" mode (no freeze, no pointer, last region not shown; the app's own windows and the island's protected windows are excluded) with the prompt "Choose what to watch". When the user picks a region: if the selector reports a clicked window (`region.windowID`) the crop is that window's full bounds; otherwise the window under the **middle** of the dragged rectangle is found in front-to-back order (`windowCrop`) and the rectangle is intersected with it (>= 8 x 8 pt); if no window is there (desktop/menu bar) a **display region** is captured instead (`prepareDisplayRegion`, island kept out of the picture). The page then opens on Watch without feedback. Choosing again replaces the target; headline line resets to automatic. A stale choice (user stopped/cancelled meanwhile) is ignored.
- State machine `NotchWatchState`: `idle` -> `watching` -> (`hidden` while the window cannot be read: minimised, cloaked or no image returned, or permission missing; a sleeping display just skips the pass silently and keeps the last state) -> `finished(outcome)` (changed, settled, shows(text), reached(number), closed).
- Loop: every **1 s** while the Watch page is visible and the island is expanded (`visibleInterval`), otherwise every **2 s** (`backgroundInterval`). Each pass: window gone -> `finished(.closed)`; captures paused (global pause while permission checks are slow) -> keep last reading; permission lost -> `hidden` + permissionMissing; sleeping display -> skip; capture the window image via the window server (whole window, rejected if transformed, e.g. Stage Manager, or if it does not cover the window size) and fall back to ScreenKit window capture if on screen; crop with `pixelCrop` (points -> pixels by image/window size ratio, rounded outward, min 4 x 4 px). No image: `hidden`.
- Change detection: a **16 x 16 grey fingerprint** (each cell value/16), "same" if no cell moved more than 1 level and at most 3 cells differ at all. If the fingerprint is unchanged and the last full read was less than **10 s** ago (`rereadInterval`) the area is not recognised again; the tracker is still fed so time can pass and a pending change can be confirmed.
- Recognition: OCR (Vision text recognition) on the area, downscaled to at most **900 px** on its longest side, languages derived from the app language; QR codes ignored; empty = no text. Signature = normalized text (case/diacritic/width-insensitive, whitespace collapsed) or, with no text, `image:` + the fingerprint cells.
- Conditions (`NotchWatchTracker`):
  - `changes`: first signature is the baseline; a different signature must **keep differing for 3 s** (`changeConfirmation`) before it ends the watch (so a caret blink or half-drawn frame does not count). Outcome `changed`.
  - `settles`: after at least one change, the signature must stay unchanged for **30 s** (`settleInterval`). Outcome `settled`.
  - `contains`: normalized reading contains the normalized typed text. Outcome `shows(text)`. Needs non-empty text.
  - `reaches`: the first number in the reading (a percentage first) is compared with the typed target; direction from the first reading (counting up: >= target, counting down: <= target; equal at start fires immediately). Outcome `reached(headline)`. Number parsing understands both decimal marks and thousands groups (`1.234.567`, `1,234`, `10 000` where the region groups with spaces; "0,125" is a decimal), a leading `-`/`+`/`-` (U+2212) sign, percent with or without a space.
  - Changing the rule resets the tracker to what the area shows now (may finish immediately).
- Headline (what the strip shows): max 22 characters. Automatic pick order: a percentage anywhere (spaces removed), the first line if <= 22 chars, a clock `h:mm(:ss)`, a number with up to 3 letter unit, else the first line cut to 21 chars + "…". The user can pick any of the first 12 lines instead (cut the same way); a missing line falls back to automatic.
- Finish (`finish`): stops the loop, state `finished`; if "Play a sound" is on plays the system "Glass" sound (beep fallback); shows an island notice (event `watch`, priority 2, 6 s) with title = outcome text (e.g. "It changed", "It shows Done", "It reached 100%", "The window closed"), detail = app name, symbol `eye`, companion reaction (changed -> perk; settled/shows/reached -> celebrate; closed -> confused) - unless the island is already open on the Watch page; also posts the reaction to the companion. If the island cannot present (full-screen app, locked) it posts a system notification (title = outcome, body = window title or app name). The first watch asks the system for notification permission once (`Notifier.requestPermission`).
- Watch Again: re-begins on the same target (needs the window to still exist or the display region to be alive).
- Priority in the closed island: timer, then watch, then downloads... (`compactActivities` order timer, watch, downloads, agents, calendar, music, keepAwake).
- Stop (page X or turning the feature off): cancels selection, loop and clears everything.
- Lifecycle: `syncWithPreferences` stops everything if disabled; the loop restarts if active and not running.

#### 8.6 Windows mapping
- **Permission:** Windows has no Screen Recording consent for classic desktop apps, so the permission row, "Allow Screen Recording" button and `permissionMissing` state do not apply (always treat as granted; keep `hidden` for minimised/cloaked windows). The setup copy to use is `setupHint` whenever enabled. Windows 11 shows a yellow capture border for Windows.Graphics.Capture on windows; the border can be disabled only by packaged apps with `graphicsCaptureWithoutBorder` (Windows 11 22H2+); otherwise prefer `PrintWindow` (below) for windows to avoid the border.
- **Area selection:** reuse the existing drag-a-region overlay from Screenshot/OCR (`FrozenScreenWindow` / `ScreenGrab`; non-frozen mode so live content stays live). Find the owning window of the region midpoint front-to-back: `EnumWindows` in z-order (`GW_HWNDNEXT` from `GetTopWindow`/`GetWindow`) skipping Trayloom's windows, cloaked windows (`DwmGetWindowAttribute(DWMWA_CLOAKED)`), tool/invisible windows; bounds with `DWMWA_EXTENDED_FRAME_BOUNDS`; clicking a window = whole window; dragging = crop in points relative to the window's top-left (convert to physical pixels with the monitor DPI). No window (desktop) -> display region (DXGI/GDI region of that monitor).
- **Window capture that follows the window even when covered:** (1) `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` (flag 0x2) for most apps (works when covered and for DWM-composed XAML/Chromium windows); (2) `Windows.Graphics.Capture` (`GraphicsCaptureItem` via `IGraphicsCaptureItemInterop.CreateForWindow`, `Direct3D11CaptureFramePool`, one frame per read) as a fallback or when the window is GPU-rendered; (3) minimised windows return nothing: state `hidden` (check `IsIconic`, `DWMWA_CLOAKED` for other virtual desktops: capturing a window on another virtual desktop works with Graphics Capture, `PrintWindow` may be blank - treat blank/black frames as `hidden`). Display regions: Graphics Capture of the monitor (`CreateForMonitor`) or `BitBlt` from the screen DC cropped to the rect, hiding Trayloom windows via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the island so the island never reads itself.
- **Window liveness:** `IsWindow(hwnd)` false = `closed`. Window title via `GetWindowText`; app name and icon via the owning process (`GetWindowThreadProcessId`, `Process.MainModule.FileName`, `Icon.ExtractAssociatedIcon`; `FileVersionInfo.FileDescription`).
- **OCR:** `Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()` (or `TryCreateFromLanguage`) on a `SoftwareBitmap` (from the crop; scale to at most 900 px on the longest side first); reuse `ScreenOcrFeature`'s engine code. `OcrResult.Text` / `Lines` give the headline lines. Run on a background thread; a loop of `Task.Delay(1000/2000)` with a generation token replaces the Swift `Task` loop.
- **Fingerprint:** downscale the crop to 16 x 16 grey (e.g. `Bitmap` with `InterpolationMode.HighQualityBilinear` then luminance / 16); same-ness rule as above. Hash normalized text with `string.Normalize(NormalizationForm.FormKD)` + `ToLowerInvariant` + remove diacritics (`CompareOptions.IgnoreNonSpace | IgnoreWidth | IgnoreCase`).
- **Number locale:** `CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator` / `NumberGroupSeparator` (space groups = U+00A0, U+202F or ' ').
- **Sound:** `System.Media.SystemSounds.Asterisk` or a bundled "Glass"-like wav (do not ship Apple's file); respect `notchWatchSound`.
- **Fallback notification:** `INotifier` (tray balloon already exists) or a toast (`Microsoft.Toolkit.Uwp.Notifications` requires package identity for actions; the balloon works unpackaged). Posted when the island is suppressed (full-screen app: `SetSuppressed`) or the session is locked.
- **Strip:** `IslandHub.SetCompact("watch", CompactInfo(glyph E7B3 eye or ED1A hide, headline, purple accent, "watch", dimmed: hidden))`; the thumbnail variant needs a richer `CompactInfo` (image). Add an eye "breathing" animation (opacity/scale 0.9-1.0, ~1.6 s sine, stopped when not visible/reduced motion).
- Session: stop the loop while the session is locked or the display is off (captures return black), resume afterwards; do not treat that as a change.

#### 8.7 Gap vs Trayloom
Nothing exists: no Watch feature, no region-follow capture, OCR loop, tracker/conditions, number parsing, page, strip, settings or notices. Reusable: `ScreenOcrFeature` (OCR), `ScreenGrab`/`FrozenScreenWindow` (region selection), Island sections/compact info, tray balloons.

---------------------------------------------------------------------------------------------------

### 9. ACCESSORY ALERTS

Sources: `Services/Notch/NotchAccessoryService.swift`, `NotchAccessorySupport.swift`, `NotchAccessoryConnectionMonitor.swift`, `Core/NotchActivityStrings.swift` (`accessories`, `accessoryDescription`, `connected`, `lowBattery`), `Services/Metrics/PeripheralBatterySupport.swift`, `PeripheralBatterySampler.swift`.

#### 9.1 What it is
"Accessory alerts" are short notices in the closed island about **Bluetooth accessories** (headphones/AirPods, keyboards, mice, trackpads, speakers, game controllers, watches, etc.):
1. **Connected**: when an accessory connects, the island briefly shows its icon + "Connected" + its name.
2. **Low battery**: when an accessory's battery falls to 20% or less, the island warns once: icon + device name, "Low battery · 18%" and a battery meter.
No page, no permission (Bluetooth connection notifications and the existing battery sampler; no Bluetooth access dialog is requested by the feature).

#### 9.2 Layout
- Connected notice: leading wing: accessory symbol (14 pt medium) + "Connected" (11 pt medium); trailing wing: device name (11 pt, 80%, right-aligned, one line, middle truncation, wing up to 160). Event `accessory`, 4 s, priority 1.
- Low battery notice (level notice): leading wing: symbol + device name; text under it is `detail` = "Low battery · NN%"; level = percent / 100 drawn as a `NotchMeter(height 5)` on the trailing side; if the level notice is for an accessory the wings use the accessory width rule (max 160, not the level rule). 4 s.
- Settings: a toggle card (symbol `headphones`) titled "Accessory alerts" in the Feedback card of the Dynamic Island settings (grid of toggle cards, min width 116), with caption under the grid when on: "Show connected accessories and warn once when their battery falls to 20%." Unavailable (greyed with reason and a link) when the Power monitor feature (`AppFeature.monitorPower`) is not installed/enabled, because battery readings come from it.

#### 9.3 Strings (English)
accessories "Accessory alerts"; accessoryDescription "Show connected accessories and warn once when their battery falls to 20%."; connected "Connected"; lowBattery "Low battery" (detail = "Low battery · " + percent + "%").

#### 9.4 Settings
| key | type | default |
|---|---|---|
| `notchAccessoriesEnabled` | bool | true (false on migrated old profiles) |
Enabled = island on + extension `notchAccessories` installed + Power monitor available + key true. Constants: `lowThreshold = 20`, `recoveryThreshold = 25`, queue max 8 notices, each expires 30 s after being queued.

#### 9.5 Behaviour
- Battery source: the system monitor's peripheral battery sampler (HID/IORegistry + Bluetooth system profiler); fast cache 15 s, Bluetooth refresh cache 300 s; sampling is turned on only while Accessory alerts are active (`setNotchAccessoryMonitoring`). `PeripheralBatteryDevice` = id, name, percent 0...100, kind (audio, keyboard, mouse, trackpad, device).
- Silent baseline: devices already low when the feature turns on do not alert. Only **fresh** readings count (observed after activation and later than the last reading of the same device identity `kind:lowercased name`). First sighting of a device only records whether it is low. A device alerts when it crosses to <= 20 from "not low"; it re-arms only after a real reading >= 25. Missing readings, disconnects or recharging do not reset the episode. History capped at 128 devices. Alerts sorted by `PeripheralBatterySupport.sorted`.
- A queued low-battery notice is dropped if that device is later read at >= 25.
- Connection events (IOBluetooth): on start the paired devices that are connected form a silent baseline (also rebuilt silently after lock/sleep). A connect notification for a device not in the baseline set produces `connected(name, majorClass, minorClass)` only if it really is connected, has a non-empty name and its Bluetooth **major class is not 1-3** (computers, phones, network access points: their links come and go without user action). A device that reports no class still announces. Disconnect removes it from the set (re-connect later announces again) and drops its pending notice.
- Presenting: `pending` queue (max 8, oldest dropped); every `presentNext` removes expired entries, and shows the first one only when the island is **not expanded** and no capture controls are showing (and the notice replacement rule allows); after showing, the next attempt runs after `4 s + 0.1 s`, so notices never overlap; if nothing could be shown it retries on the same cadence.
- Symbols: by kind and name: AirPods Max `airpodsmax`, AirPods Pro `airpodspro`, AirPods `airpods`, other audio `headphones`, `keyboard`, `computermouse`, trackpad `rectangle.and.hand.point.up.left`, other `dot.radiowaves.left.and.right`. For connections, if the name does not identify the kind, the Bluetooth class of device decides (major, minor): (4,4) `mic`; (4,5)/(4,7)/(4,10) `hifispeaker`; (4,8) `car`; (4,14)/(4,15) `tv`; (4,18) `gamecontroller`; (4,1)/(4,2)/(4,6) headphones; (5, minor&0x0F in 1,2) `gamecontroller`; (5, 3) `av.remote`; (5, pointing bits 0x20) trackpad if minor&0x0F == 5 else mouse; (5, keyboard bit 0x10) keyboard; (6, bit 0x20) `printer`; (7,1) `applewatch`; (7,5) `eyeglasses`; (8,*) `gamecontroller`; default `dot.radiowaves.left.and.right`. Name rules (case-insensitive contains): "keyboard", "mouse", "trackpad"/"track pad", "airpods"/"headphone"/"headset"/"buds" (=audio).
- Lifecycle: starts in `syncWithPreferences` (when enabled), `stop()` resets the battery history, `suspend()` (e.g. lock/sleep) stops the monitors but keeps battery episodes. Bluetooth monitoring runs on a dedicated utility thread with its own run loop; start/stop never block the main thread.

#### 9.6 Windows mapping
- **Connections (WinRT):** `Windows.Devices.Bluetooth.BluetoothDevice` + `Windows.Devices.Enumeration.DeviceWatcher`: create `DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true), new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress" }, DeviceInformationKind.AssociationEndpoint)` (classic) and the BLE equivalent `BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)`. The initial enumeration (`EnumerationCompleted`) sets the silent baseline of devices whose `IsConnected` is true; `Updated` events carrying `System.Devices.Aep.IsConnected` flipping to true announce a connection; flipping to false removes it from the connected set. Alternative: `BluetoothDevice.FromIdAsync(id)` then subscribe to `ConnectionStatusChanged`. Class of device: `BluetoothDevice.ClassOfDevice.MajorClass` (`BluetoothMajorClass`: Miscellaneous 0, Computer 1, Phone 2, NetworkAccessPoint 3, AudioVideo 4, Peripheral 5, Imaging 6, Wearable 7, Toy 8, Health 9) and `MinorClass` / `RawValue`: the same (major, minor) table as above applies. BLE-only devices report no class: use name rules and GATT appearance if available. Also USB/wireless-dongle accessories (non-Bluetooth) are out of scope as on macOS.
- **Battery:** BLE devices: GATT Battery Service (UUID 0x180F, characteristic 0x2A19 "Battery Level", read/notify) via `GattDeviceService.FromIdAsync` / `BluetoothLEDevice.GetGattServicesForUuidAsync(GattServiceUuids.Battery)`; subscribe to value changes or poll (suggested: read on connect, then every 5 min; keep 15 s minimum between reads). Classic Bluetooth HFP/AVRCP battery (most headsets/AirPods on Windows): not exposed through a documented WinRT API; Windows Settings reads it from a PnP device property, commonly reported as property key `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}` pid 2 (Byte, percent) on the Bluetooth device node - undocumented, verify per device with `DeviceInformation.CreateFromIdAsync(id, new[] { "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2" })`. Treat missing as "no reading" (never a disconnection). Mouse/keyboard HID over Bluetooth LE use the same Battery Service. `Windows.Devices.Power.Battery` is only for the system's internal batteries, not peripherals.
- Reuse the existing monitor infrastructure (`Trayloom.Features.System/Monitor`, `SystemSampler`) to host a `PeripheralBatterySampler` with the same cache intervals (15 s fast, 300 s Bluetooth refresh) and a `PeripheralBatterySample { Devices, ObservedAt }` so the freshness rule can be applied; turn it on only while the feature is on.
- Glyphs: headphones E7F6, keyboard E765, mouse E962, trackpad E962/EFA5 (approx.), speaker E7F5, gamecontroller E7FC, printer E749, watch EC48 (approx.), tv E7F4, car E804 (approx.), generic Bluetooth E702, mic E720, remote EA9F (approx.), glasses (generic device). Verify in Character Map.
- Notices: `IslandHub.ShowAlert` with the extended alert type; a queue (max 8, 30 s expiry, one per 4.1 s, only when the island is collapsed).
- Session: suspend the monitors on `WTS_SESSION_LOCK`/sleep and rebuild the connection baseline silently on resume (keep battery low-episodes).
- Settings: new toggle "Accessory alerts" (default on) with the caption above; requires the system monitor/Power feature, otherwise show the reason and a link to Features.

#### 9.7 Gap vs Trayloom
No accessory feature at all (no Bluetooth connection or peripheral battery code; `Trayloom.Features.System/Audio/DeviceWatcher.cs` watches audio endpoints, not Bluetooth accessories). `IslandAlert` has no level meter, title/detail wings, priority, queue or expiry.

---------------------------------------------------------------------------------------------------

### 10. CROSS-CUTTING SUMMARY

#### 10.1 Persistence keys (all in this document)
| area | key | default |
|---|---|---|
| Downloads | `notchDownloadsEnabled` | true |
| Downloads | `notchDownloadsFolderBookmark` (Windows: `folder` path) | none |
| Files | `notchShelf`, `notchDragReveal`, `shelfEnabled` (Shelf spec) | per Shelf spec |
| Clipboard | `notchClipboard` | true |
| Clipboard | `notchClipboardWindow` | true |
| Clipboard | `notchClipboardCardSize` | comfortable |
| Clipboard | `clipboardHistoryEnabled` | false |
| Scratchpad | `notchScratchpad` | true |
| Scratchpad | `scratchpadDocument`, `scratchpadTextSize` (13, 10...22), `scratchpadRetention` (never) | see 4.3 |
| Notifications | `notchNotificationsEnabled` | true |
| Notifications | `notchDismissNativeNotifications` | false |
| Lock screen | `notchLockScreen` | false |
| Lock screen | `notchLockSounds` | false |
| Companion | `notchMascotEnabled` (false), `notchMascotVisits` (true), `notchMascotReactions` (true), `notchMascotStyle` (minimal), `notchMascotShape` (ball), `notchMascotPalette` (pearl), `notchMascotSide` (left), `notchMascotVisitFrequency` (normal), `notchMascotHidesWhenIdle` (true), `notchCommandBar` (true), `notchCommandBarStyle` (droplet) | see 7.8 |
| Watch | `notchWatchEnabled` (true), `notchWatchSound` (true), `notchWatchCondition` (changes) | see 8.4 |
| Accessories | `notchAccessoriesEnabled` | true |
Windows storage: one scope per feature in `SettingsStore` (`IFeatureSettings.Get/Set`); keep the Mac key names in camelCase (without the `notch` prefix is acceptable: `downloads.enabled`, `watch.sound`, `mascot.style`...) but be consistent and migrate the current `folder`/`announce` keys of the Downloads feature.

#### 10.2 Session / power service (needed by sections 1, 5, 6, 8, 9)
Implement one `IslandSessionState` fed by `WTSRegisterSessionNotification`, `SystemEvents.PowerModeChanged`, `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)` and the screen-saver check, exposing `Locked`, `Sleeping`, `DisplaysSleeping`, `OnConsole`, `ScreenSaverRunning`, `CanRunTimer`, `CanPresent`, `HearsLockChange` and an event for each transition; every extension above subscribes to it for pause/resume. The island hides itself when `CanPresent` is false (same as `SetSuppressed`).

#### 10.3 Extra data the island needs from its host
- Richer `IslandAlert` (title/detail wings, optional logo/level/reaction, priority, held preview, queue), `CompactInfo` with image/progress, `IIslandController.HoldOpen()` already exists and is the right tool for dialogs; a `ReactionBus` for the companion; a hover-hold API for banners; a `Priority`-ordered list of closed-island activities (timer, watch, downloads, agents, calendar, music, keepAwake) with a user choice; keyboard focus handling for the Clipboard/Scratchpad pages.
- Dynamic Island settings pages to add: Notifications (hide system banner), Downloads (switch + folder), Watch (switch, sound, permission-free caption), Companion (full page), Feedback toggle cards (Clipboard, Accessory alerts, ...), Lock Screen card.
