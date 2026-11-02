# Things only a person can test

Everything else (every tray tab, all settings pages, the Features hub, the first-run guide, the Dynamic Island pages, the
Command Bar, clipboard window, shelf, scratchpad, screenshot, text from screen, color picker, recording and export, the
cleaner scan, the lists in Packages / App updates / Kill Process / Port Manager, German and Japanese) was driven on a
test copy of the app. The items below need real keys, real mouse gestures or other programs, so please try them yourself.

Tip: turn the feature on in **Settings > Features** first. If something grabs a key you need, uninstall that feature there.

| Feature | How to try it | What should happen |
| --- | --- | --- |
| Alt+Tab switcher | Install *Window switcher*, open three apps, press Alt+Tab, keep Alt held and tap Tab, then let go. | A strip of window previews replaces the Windows one; the chosen window comes to the front. Esc cancels. Releasing Alt never leaves Alt "stuck". |
| Switcher: windows of one app | Press Alt+` (backtick) with two Explorer windows open. | Only the windows of the front app are listed. |
| Taskbar hover previews | Install *Taskbar Preview*, rest the pointer on an open app's taskbar icon. | A preview of its windows appears after the open delay; clicking a preview raises that window. |
| Taskbar clicks | Install *Taskbar clicks*, click a running app's icon while it is in front. | It minimizes (or hides, or cycles windows, depending on the setting). Clicking again brings it back. |
| Window edge snapping | Install *Window layout*, turn on "Snap windows at screen edges", drag a window by its title bar to the left edge. | A highlighted area shows; letting go snaps the window to that half. Corners give quarters. |
| Move / resize by dragging | Hold Ctrl+Alt and drag anywhere inside a window (add Shift to resize). | The window moves (or resizes) without grabbing its title bar. |
| Window layout shortcuts | Ctrl+Alt+Left / Right / Enter / C on a normal window. | Left half, right half, maximize, center. Ctrl+Alt+R restores. |
| Quit on close | Install *Quit on close*, close the last window of Notepad with the X. | Notepad quits instead of staying in the tray. Apps in the Exceptions list stay open. |
| Quit & close protection | Install it, press Alt+F4 once on a window, then press and hold it. | A short hold is needed; a quick tap does nothing; the hold shows a progress ring and then closes. |
| Super key | Install *Super key*, hold Caps Lock and press H (or any key). | The key acts as Shift+Ctrl+Alt+Win+H. A quick tap of Caps Lock does what "A tap on its own" says. Uninstall and check Caps Lock works normally again. |
| Text snippets | Add a snippet (trigger `;sig`), type `;sig` in Notepad. | The trigger is replaced by the full text. Ctrl+Alt+Win+L opens the quick snippet menu. |
| Side and extra mouse buttons | With a mouse that has side buttons, install *Side buttons* and click Back / Forward in a browser. | Pages go back and forward. *Mouse button shortcuts* sends the key combination you set. |
| Focus follows mouse | Install it, move the pointer across two windows without clicking. | The window under the pointer gets focus after the delay. |
| Smooth / linear scrolling, invert scrolling | Scroll a long web page with the wheel. | Smooth: animated. Linear: same distance every notch. Invert: the opposite direction. Uninstall and check the wheel is normal again. |
| Radial menu | Press Ctrl+Alt+Win+Space (or use the mouse button you set). | A wheel of actions opens at the pointer; moving into a slice and releasing runs it. |
| Recording with real clicks | Press Ctrl+Alt+Win+5, drag a region, click around and type for 5 seconds, stop. | The editor opens; the click ring and pointer track are visible; auto-zoom follows clicks; Save writes an .mp4 that plays. |
| Permission alert (AI Agents) | Turn on **AI Agents** in the Dynamic Island, then start a Claude Code session that asks for a permission (for example run a shell command that needs approval). | The island pops out to say that session needs you (you must first install the hook from the AI Agents settings, which edits `~\.claude\settings.json` after making a backup). The permission itself is still answered in Claude Code. |
| Clipboard history capture | Install *Clipboard*, copy text in three different apps, press Ctrl+Alt+Win+V. | Each copy is listed (newest first); Enter or a click pastes it into the previous app; pinned items survive "Clear unpinned". |
| Paste as plain text | Copy bold text from a web page, press Alt+Shift+Win+V in Word or Notepad. | The text arrives without formatting. |
| Explorer cut and paste | Install *Cut & paste*, select a file in File Explorer, press Ctrl+X, open another folder, press Ctrl+V. | The file moves (it does not copy); a small floating panel shows what is cut. In a text box Ctrl+X / Ctrl+V still work as normal. |
| Explorer rename | Select a file in File Explorer and press F2 (with the rename shortcut installed). | The name goes into edit mode; typing and Enter renames it. Not active inside text fields. |
| Shelf | Drag a file out of Explorer while shaking the mouse (or press Ctrl+Alt+Win+D) and drop it on the shelf. | The file is held; dragging it out of the shelf drops it in another folder. |
| Mute microphone | Press Ctrl+Alt+Win+M twice. | The tray icon shows muted, then back. Uninstalling the feature while muted unmutes. |
| Cleaning Mode | Start it (only with the keyboard and mouse free to lock) and wait for the timer. | Keyboard and mouse are locked; they work again when the timer ends. |
| Install offer | Run the downloaded `WinKnife.exe` from the zip folder. | The "Install WinKnife" window appears. Install copies to your account, adds a Start menu shortcut and starts the installed copy. |

After testing, open Settings > Features, check which input features are still installed, and uninstall any you do not want.
