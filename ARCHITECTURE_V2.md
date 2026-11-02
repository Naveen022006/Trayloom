# WinKnife v2 — exact Vorssaint replica (architecture decisions)

v1 (`ARCHITECTURE.md`) produced working features behind a generic hub. v2 makes WinKnife an
exact replica of Vorssaint's structure, screens, strings, defaults and behaviour on Windows.
The specs in `specs/` are the source of truth (`specs/SPEC_RULES.md` explains them):

| Spec | Covers |
|---|---|
| `01-menu-panel.md` | tray icon(s), tray flyout panel (tabs, sections, hosted utility panels), visual tokens |
| `02-settings.md` (+ `02a`..`02e`) | Settings window, sidebar, every page, Features hub, Permissions, onboarding, catalog, app behaviour |
| `03-island-agents.md` | Dynamic Island shell, all pages/extensions, AI Agents (Claude/Codex/OpenCode/Copilot) |
| `04-tools.md` | command bar, quick panel, toggles, capture, recorder, small tools, maintenance tools |
| `05-services.md` | windows/taskbar, mouse & keyboard, clipboard & files, sound, energy & display, monitor |

Where specs disagree, `02-settings.md` wins for structure/catalog and the feature's own spec wins
for behaviour. When the README screenshots and the Swift code disagree, the code wins.

## Fixed decisions

- **Branding**: product name "WinKnife", own icon; never the Vorssaint name/saturn logo. Replace
  "Mac" with "PC", "menu bar" with "notification area"/"tray" where the spec marks `[WIN]`.
- **Strings**: English strings verbatim from the specs, wrapped in `Loc.T` / `Loc.F` (`WinKnife.Core/Localization`); the 15
  Vorssaint languages come from tables harvested out of Vorssaint (see `specs/L10N_TODO.md`).
- **Hotkeys**: macOS ⌘ maps to **Win** in multi-modifier global hotkeys (⌃⌥⌘ → Ctrl+Alt+Win,
  ⌃⌘ → Ctrl+Win, ⇧⌥⌘ → Shift+Alt+Win), ⌥ → Alt, ⌃ → Ctrl. Shortcuts intercepted inside the
  front app use Windows conventions (⌘Q → Alt+F4, ⌘W → Ctrl+W, ⌘Tab → Alt+Tab). If a default
  is reserved by Windows (e.g. Ctrl+Win+Left/Right switch desktops, Win+Shift+S) or fails to
  register, fall back by replacing Win with Shift, and surface the conflict in the settings row.
- **Settings storage**: a flat app-wide key/value store (`IAppSettings`) using Vorssaint's
  `DefaultsKey` names and defaults from the specs, persisted in `%APPDATA%\WinKnife\settings.json`
  (`WINKNIFE_DATA` override stays). Old per-feature scoped settings remain readable but new code uses
  the flat keys so pages that span several features work.
- **Catalog**: Core holds the complete 73-entry feature catalog (ids, group, title, description,
  glyph, energy badge, permissions, default-installed, settings destination `page#anchor`, Windows
  availability: supported / adapted / unavailable-with-reason). `IFeature` implementations bind to a
  catalog id. Hub/sidebar/tabs order comes from the catalog, never from reflection order.
  Unavailable entries use the hardware-gate mechanism (row disabled, tooltip reason, never counted).
- **Composition by contribution** (discovered by reflection in `WinKnife.Features.*`):
  - `ISettingsSection` — (PageId, Anchor, Order, FeatureId?) → view. A settings page = page header +
    its contributed sections in order. Pages span projects (e.g. General hosts mixer sections from
    System; Quick tools hosts micMute from System and scratchpad from Tools).
  - `IPanelSection` — (TabId, Order, FeatureId?) → view, for the tray flyout tabs; plus
    `IPanelUtility` rows (Utilities tab) that open hosted utility panels.
  - `IIslandSection` / `IIslandStrip` — island pages and compact-strip items, so projects other than
    Island (e.g. the AI Agents project) can contribute.
  The App owns the shells (tray flyout, settings window, sidebar, router, hub, permissions,
  onboarding) and the static layout tables (tab order, sidebar order) taken from the specs.
- **Projects**: existing five feature projects plus `WinKnife.Features.Capture` (screenshot,
  recorder, OCR, color picker) and `WinKnife.Features.Agents` (AI Agents data + island pages).
  Feature projects still reference only Core.
- **Packages allowed**: NAudio (System), ZXing.Net (QR in Capture/Tools), Markdig (Scratchpad),
  Microsoft.Data.Sqlite (Agents: OpenCode db), ZstdSharp only if needed. No bundled ffmpeg; video
  via Media Foundation (SinkWriter) and Windows.Graphics.Capture. Ask before anything else.
- **Network**: WinKnife never talks to Vorssaint servers. Share links hidden; Feedback opens a
  GitHub issue URL; agent price list ships bundled (`agent-prices.json` copied from Vorssaint
  resources), no auto-update by default.
- **Secrets**: never open `.claude\.credentials.json`, `.codex\auth.json`, OpenCode `auth.json`.
  Plan limits come from the `claude` / `codex` CLIs exactly as the spec describes. Never consume a
  Codex reset credit except from an explicit user click.
- **Cleaner**: caches/temp/logs/crash dumps deleted permanently after confirmation; leftovers,
  startup items, backups, screenshots go to the Recycle Bin.
- **Window sizing bug** (demo): never call `SetWindowPos` with placeholder sizes before `Show()`;
  size WPF windows via WPF properties or after `SourceInitialized` with the real size.

## Build rules for parallel agents

Same as `PHASE2_RULES.md`: each agent edits only its own project(s), builds only its own project with
`-p:BuildProjectReferences=false`, never Core/App/sln unless it is the shell agent. Visual tokens and
shared controls come from Core (`WinKnife.Core.Ui`) — use them, don't restyle locally.
