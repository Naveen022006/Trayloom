# 10e: Dev servers in the island (v0.8.0, package P5)

Implements "P5: Dev servers in the island" of `10-next-batch-0.8.md`. Everything lives in `Trayloom.Features.Maintenance`.

## What the person sees
- **Island page "Dev servers"** (id `devServers`, feature `port-manager`, order 130, glyph Network). One row per local server: `localhost:5173`, the process name underneath, **Open** and **Stop**. Empty state: "No local servers are running." with "Servers you start, like npm run dev, appear here." Footer pill "All ports…" opens Settings, Port Manager. If a read fails the last list stays and "Couldn’t read the port list." shows above it.
- **Notice strip** `devServers.notice`: "localhost:5173 is up", once, when a new server appears (island enabled, setting on).
- **Settings, Port Manager page**: two switches above the port list: "Tell me when a local server starts" (`devServersNotice`, default on) and "Show every listening port of your apps" (`devServersShowAll`, default off).

## Which sockets count (`DevServerRules`)
Same session as Trayloom, not protected (`KillService.IsProtected`), port >= 1024, address loopback or any interface; then a runtime name (node, deno, bun, python…) or a common port (3000-3010, 4000, 4173, 4200, 5000-5010, 5173-5180, 5500, 6006, 7000, 8000-8010, 8080-8090, 8443, 8888, 9000; never 9229), unless the name is in the not-a-server list (code, chrome, discord…). With `devServersShowAll` every such listener of the person's apps counts. A port reachable over IPv4 and IPv6 is one row, IPv4 preferred, then lower pid.

## Open and Stop
- **Open** calls `UiKit.OpenWebUrl(entry.BrowserUrl)` (an any-interface listener opens `http://localhost:port`).
- **Stop** never acts at once. The row turns into "Stop {0} on port {1}?" with "It ends the process like Task Manager. Unsaved work in it is lost.", **Stop** and **Cancel**. The island is held open (`HoldOpen`) while the question is up; Esc and Back cancel it (`IIslandLayerHost`). Confirming re-checks the row (not protected, same session, same pid and creation time) and calls `KillService.Shared.KillAsync([target], force: false, interactive: false)`.
- With `interactive: false` `KillAsync` never shows a dialog and never escalates: NeedsAdmin counts as failed. The island then waits 600 ms and checks that the process is really gone (a close request to a window is not an exit) and shows the result inline for 5 s: "Stopped", "Couldn’t stop it. It may run as administrator." or, for a row that is protected or in another session, "Run by Windows or another account; Trayloom won’t stop it."
- System processes, services and elevated processes are never listed (an unreadable creation time makes a process protected), so Stop is never offered for them.

## Watcher (`DevServerWatcher`)
Started by `PortManagerFeature` while `devServersNotice` is on (it follows the switch live). Ticks every 10 s but reads the port list only while `Runtime.IsRunning("notch")` and `notchEnabled` are on. The first read after that is the silent baseline; later reads queue one notice per 10 s for the lowest new port and call the captured `IIslandContext.Invalidate()`. Notices older than a minute are dropped; at most 3 wait. Process names are cached per (pid, creation time) in `PortList`.

## Logs
Counts only: "Dev servers: N new", "Dev servers: stopped one" / "could not stop one". No names, ports or paths.

## Files
New: `PortManager/DevServers.cs`, `DevServersIsland.cs`, `DevServerWatcher.cs`, harness `DevServerChecks.cs`. Changed: `PortManager.cs` (feature start/stop, settings card, name cache), `KillProcess/KillService.cs` (`interactive` parameter), `Support/Host.cs` (the two defaults), harness `Program.cs`.

## Harness
`DevServerChecks` covers the filter (node on 5173/loopback, another session, protected, port 443, `code` on 3000, unknown names with and without showAll, 9229), `NewPorts`, one row per port, and the browser URLs. It never opens, lists or ends a process.

## Manual tests
1. `npx vite`: "localhost:5173 is up", then the page lists it.
2. Open goes to the browser. Stop, confirm: the process ends and the terminal shows it exited.
3. An elevated `python -m http.server 8000`: "Couldn’t stop it. It may run as administrator."
4. VS Code's language-server ports are not listed.
