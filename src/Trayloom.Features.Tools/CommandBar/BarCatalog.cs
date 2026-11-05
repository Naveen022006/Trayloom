// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Tools;

/// <summary>Builds the rows of the Command Bar (spec 1.5 / 1.11).</summary>
internal static class BarCatalog
{
    private static string G(int code) => char.ConvertFromUtf32(code);

    private static readonly string Tools = FeatureGroup.Tools.DisplayName();

    /// <summary>Subtitle of a feature row: the hub title unless it equals the row title, then the group title.</summary>
    private static string Area(string featureId, string rowTitle)
    {
        var entry = FeatureCatalog.Get(featureId);
        if (entry is null)
        {
            return Tools;
        }

        return string.Equals(entry.Title, rowTitle, StringComparison.OrdinalIgnoreCase) ? entry.Group.DisplayName() : entry.Title;
    }

    private static FeatureAction? Exact(IContributionServices ctx, string featureId, params string[] ids)
    {
        var mine = ctx.Actions.All.Where(a => FeatureCatalog.Resolve(a.Feature.Id) == featureId).Select(a => a.Action).ToList();
        foreach (var id in ids)
        {
            var hit = mine.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The fixed catalog: action rows, feature toggles, machine answers, Settings pages, snippets, your shortcuts.</summary>
    public static List<BarEntry> Build(BarService s)
    {
        var ctx = s.Ctx;
        var rows = new List<BarEntry>();
        bool Has(string id) => ctx.Runtime.IsAvailable(id);

        void Tool(string id, string tool, string title, int glyph, string feature, string? role, int beat = 150, string? keywords = null)
        {
            if (!Has(feature))
            {
                return;
            }

            rows.Add(new BarEntry
            {
                Id = id,
                Title = title,
                EnglishTitle = title,
                Subtitle = Area(feature, title),
                Glyph = G(glyph),
                Keywords = keywords ?? "",
                Shortcut = role is null ? null : ShortcutText.Of(ctx, role),
                BeatMs = beat,
                FeatureKey = feature,
                FeatureTurn = 1,
                Run = _ => ToolActions.Run(ctx, tool),
            });
        }

        // ---- action rows ----
        Tool("action.screenshot", "screenshot", Loc.T("Screenshot"), 0xE722, "screenshot", "screenshot");
        if (Has("screenshot") && Exact(ctx, "screenshot", "scrolling", "scrolling-screenshot") is { } scrolling)
        {
            rows.Add(Simple("action.scrollingScreenshot", Loc.T("Scrolling screenshot"), Area("screenshot", Loc.T("Scrolling screenshot")), 0xE8A9, scrolling.Invoke, 150));
        }

        Tool("action.screenRecorder", "screenRecorder", Loc.T("Screen recording"), 0xE7C8, "screen-recorder", "screenRecorder");
        if (Has("screenshot") && Exact(ctx, "screenshot", "recent", "history", "recent-captures") is { } recent)
        {
            rows.Add(Simple("action.recentCaptures", Loc.T("Recent captures"), Tools, 0xE81C, recent.Invoke, 100, Loc.T("Screenshot Recording Recent captures")));
        }

        Tool("action.screenOCR", "screenOCR", Loc.T("Copy text from screen"), 0xE8D2, "screen-ocr", "screenOCR");
        Tool("action.colorPicker", "colorPicker", Loc.T("Color picker"), 0xEF3C, "color-picker", "colorPicker");

        if (Has("clipboard-history"))
        {
            var enabled = ctx.App.Get<bool>("clipboardHistoryEnabled");
            var count = BarProviders.Clips().Count;
            var needs = !enabled && count == 0;
            rows.Add(new BarEntry
            {
                Id = "action.clipboardWindow",
                Title = Loc.T("Clipboard"),
                EnglishTitle = Loc.T("Clipboard"),
                Subtitle = Area("clipboard-history", Loc.T("Clipboard")),
                Glyph = G(0xE77F),
                Shortcut = ShortcutText.Of(ctx, "clipboardHistory"),
                NeedsSetupFeature = needs ? Loc.T("Clipboard") : null,
                NeedsSetupDestination = needs ? SettingsPages.Clipboard : null,
                BeatMs = 100,
                FeatureKey = "clipboard-history",
                FeatureTurn = 1,
                Run = _ => ToolActions.Run(ctx, "clipboardHistory"),
            });
            var unpinned = BarProviders.Clips().Count(c => !c.Pinned);
            if (Exact(ctx, "clipboard-history", "clear") is { } clear)
            {
                rows.Add(new BarEntry
                {
                    Id = "action.clipboardClearRecent",
                    Title = Loc.T("Clear unpinned"),
                    EnglishTitle = Loc.T("Clear unpinned"),
                    Subtitle = Loc.T("Clipboard"),
                    Glyph = G(0xE74D),
                    Keywords = "Clipboard Clipboard Clear unpinned Recent Recent Clear recent Clear recent",
                    NeedsSetupFeature = needs ? Loc.T("Clipboard") : null,
                    NeedsSetupDestination = needs ? SettingsPages.Clipboard : null,
                    ConfirmPrompt = Loc.F("Clear unpinned ({0})?", unpinned),
                    Run = _ => clear.Invoke(),
                });
            }
        }

        if (Has("text-snippets"))
        {
            var lib = ctx.Actions.All.FirstOrDefault(a => FeatureCatalog.Resolve(a.Feature.Id) == "text-snippets" && (a.Action.Id.Contains("librar", StringComparison.OrdinalIgnoreCase) || a.Action.Id.Contains("menu", StringComparison.OrdinalIgnoreCase))).Action;
            if (lib is not null)
            {
                rows.Add(Simple("action.snippetLibrary", Loc.T("Quick snippet menu"), Area("text-snippets", Loc.T("Quick snippet menu")), 0xE8C4, lib.Invoke, 100));
            }
        }

        Tool("action.scratchpad", "scratchpad", Loc.T("Scratchpad"), 0xE70B, "scratchpad", "scratchpad");
        if (Has("notch-screen-time") && Exact(ctx, "notch-screen-time", "today") is { } screenTime)
        {
            rows.Add(Simple("action.screenTimeToday", screenTime.Title, Area("notch-screen-time", Loc.T("Screen time")), 0xE916, screenTime.Invoke, 100, Loc.T("Screen time usage today apps")));
        }
        Tool("action.cameraPreview", "cameraPreview", Loc.T("Camera preview"), 0xE722, "camera-preview", "cameraPreview");
        Tool("action.shelf", "shelf", Loc.T("Shelf"), 0xE7B8, "shelf", "shelf");
        if (Has("paste-plain") && Exact(ctx, "paste-plain", "paste-plain") is { } plain)
        {
            rows.Add(Simple("action.pastePlain", Loc.T("Paste as plain text"), Area("paste-plain", Loc.T("Paste as plain text")), 0xE8E4, plain.Invoke, 150, null, ShortcutText.Of(ctx, "pastePlain")));
        }

        Tool("action.cleaningMode", "cleaningMode", Loc.T("Cleaning Mode"), 0xE765, "cleaning-mode", null, 100);

        if (Has("away-mode") && Exact(ctx, "away-mode", "away-now") is { } awayNow)
        {
            rows.Add(Simple("action.awayNow", Loc.T("Go away now"), Loc.T("Away mode"), 0xE7E8, awayNow.Invoke, 200, Loc.T("Away mode privacy minimize windows")));
        }

        if (Has("away-mode") && Exact(ctx, "away-mode", "restore") is { } awayRestore)
        {
            rows.Add(Simple("action.awayRestore", Loc.T("Restore windows"), Loc.T("Away mode"), 0xE777, awayRestore.Invoke, 100, Loc.T("Away mode back")));
        }

        if (Has("away-mode") && Exact(ctx, "away-mode", "reopen") is { } awayReopen)
        {
            rows.Add(Simple("action.awayReopen", awayReopen.Title, Loc.T("Away mode"), 0xE8A7, awayReopen.Invoke, 150, Loc.T("Away mode reopen closed apps browser tabs")));
        }

        // Modes: one row per saved mode, "Leave" while one is active, and the capture shortcut. The actions are evaluated live, so a
        // renamed, added or deleted mode shows up the next time the bar opens.
        if (Has("modes"))
        {
            foreach (var (_, a) in ctx.Actions.All.Where(x => FeatureCatalog.Resolve(x.Feature.Id) == "modes"))
            {
                if (a.Id.StartsWith("mode.", StringComparison.Ordinal))
                {
                    rows.Add(Simple("action.mode." + a.Id[5..], a.Title, Loc.T("Modes"), 0xE7C4, a.Invoke, 200, Loc.T("Mode workspace switch open apps tabs coding study")));
                }
                else if (a.Id == "leave")
                {
                    rows.Add(Simple("action.modeLeave", a.Title, Loc.T("Modes"), 0xE89F, a.Invoke, 200, Loc.T("Mode leave workspace")));
                }
                else if (a.Id == "save-current")
                {
                    rows.Add(Simple("action.modeSave", a.Title, Loc.T("Modes"), 0xE74E, a.Invoke, 150, Loc.T("Mode save workspace capture tabs")));
                }
            }
        }

        // keep awake
        if (Has("keep-awake"))
        {
            var keep = ctx.Services.Get<IKeepAwakeService>();
            var on = keep?.IsActive ?? false;
            rows.Add(new BarEntry
            {
                Id = "action.keepAwake",
                Title = on ? Loc.T("Disable keep awake") : Loc.T("Enable keep awake"),
                EnglishTitle = on ? Loc.T("Disable keep awake") : Loc.T("Enable keep awake"),
                Subtitle = Loc.T("Keep awake"),
                Keywords = "Keep awake",
                Glyph = G(0xE945),
                IsActive = on,
                Shortcut = ShortcutText.Of(ctx, "keepAwake"),
                FeatureKey = "keep-awake",
                FeatureTurn = 0,
                Run = _ =>
                {
                    if (keep is not null)
                    {
                        if (keep.IsActive)
                        {
                            keep.Deactivate();
                        }
                        else
                        {
                            keep.Activate(null);
                        }
                    }
                    else
                    {
                        ToolActions.Run(ctx, "keepAwake");
                    }
                },
            });
            foreach (var (minutes, label) in new[] { (15, Loc.T("15 minutes")), (30, Loc.T("30 minutes")), (60, Loc.T("1 hour")), (120, Loc.T("2 hours")), (240, Loc.T("4 hours")), (480, Loc.T("8 hours")) })
            {
                var m = minutes;
                rows.Add(new BarEntry
                {
                    Id = "action.keepAwake." + m,
                    Title = Loc.F("Keep awake for {0}", label),
                    EnglishTitle = Loc.F("Keep awake for {0}", label),
                    Subtitle = Loc.T("Keep awake"),
                    Keywords = "Keep awake",
                    Glyph = G(0xE916),
                    FeatureKey = "keep-awake",
                    FeatureTurn = 1,
                    Run = _ => keep?.Activate(TimeSpan.FromMinutes(m)),
                });
            }
        }

        // microphone
        if (Has("mic-mute") && Exact(ctx, "mic-mute", "toggle") is { } mic)
        {
            var muted = ctx.Services.Get<IMicMuteState>()?.IsMuted ?? false;
            rows.Add(new BarEntry
            {
                Id = "action.micMute",
                Title = muted ? Loc.T("Unmute microphone") : Loc.T("Mute microphone"),
                EnglishTitle = muted ? Loc.T("Unmute microphone") : Loc.T("Mute microphone"),
                Subtitle = Loc.T("Mute microphone"),
                Glyph = G(muted ? 0xF781 : 0xE720),
                IsActive = muted,
                Shortcut = ShortcutText.Of(ctx, "micMute"),
                Run = _ => mic.Invoke(),
            });
        }

        // brightness
        if (Has("brightness"))
        {
            var enabled = ctx.App.Get<bool>("brightnessControlEnabled");
            rows.Add(new BarEntry
            {
                Id = "action.brightness",
                Title = Loc.T("Display brightness"),
                EnglishTitle = Loc.T("Display brightness"),
                Subtitle = enabled ? Loc.T("0 to 100") : Loc.T("Displays"),
                Keywords = "Displays screen",
                Glyph = G(0xE706),
                NeedsSetupFeature = enabled ? null : Loc.T("Displays"),
                NeedsSetupDestination = enabled ? null : "energy#brightness",
                NumericRange = enabled ? (0, 100) : null,
                Run = v =>
                {
                    if (v is { } value)
                    {
                        Task.Run(() => BarSystem.SetBrightness(value)).ContinueWith(t =>
                        {
                            if (!t.Result)
                            {
                                s.Ui.BeginInvoke(() => System.Media.SystemSounds.Beep.Play());
                            }
                        });
                    }
                },
            });
        }

        // sound
        rows.Add(new BarEntry
        {
            Id = "action.volume",
            Title = Loc.T("Volume"),
            EnglishTitle = Loc.T("Volume"),
            Subtitle = Loc.T("0 to 100"),
            Glyph = G(0xE767),
            NumericRange = (0, 100),
            Run = v =>
            {
                if (v is { } value)
                {
                    if (BarSystem.SetVolume(value))
                    {
                        ToolHud.Show(G(0xE767), Loc.F("Volume {0}%", value));
                    }
                    else
                    {
                        System.Media.SystemSounds.Beep.Play();
                    }
                }
            },
        });
        if (BarSystem.Muted() is { } soundMuted)
        {
            rows.Add(new BarEntry
            {
                Id = "action.soundMute",
                Title = soundMuted ? Loc.T("Turn the sound back on") : Loc.T("Mute the sound"),
                EnglishTitle = soundMuted ? Loc.T("Turn the sound back on") : Loc.T("Mute the sound"),
                Subtitle = Loc.T("Volume"),
                Glyph = G(soundMuted ? 0xE74F : 0xE767),
                IsActive = soundMuted,
                Run = _ => BarSystem.SetMuted(!soundMuted),
            });
        }

        if (Has("sound-output-switcher") && Exact(ctx, "sound-output-switcher", "cycle") is { } cycle)
        {
            rows.Add(Simple("action.soundOutput", Loc.T("Switch sound output"), Loc.T("Sound"), 0xE7F6, cycle.Invoke, 100, Loc.T("Switch sound output"), ShortcutText.Of(ctx, "soundOutputSwitcher")));
        }

        // quick toggles
        foreach (var id in new[] { "darkMode", "lockScreen", "displayOff", "screenSaver", "ejectDisks", "hiddenFiles", "desktopIcons", "emptyTrash" })
        {
            if (!QuickToggleOps.Available(ctx, id))
            {
                continue;
            }

            var toggleId = id;
            var title = QuickToggleOps.Title(id);
            rows.Add(new BarEntry
            {
                Id = id switch
                {
                    "darkMode" => "action.darkMode",
                    "lockScreen" => "action.lockScreen",
                    "displayOff" => "action.displayOff",
                    "screenSaver" => "action.screenSaver",
                    "ejectDisks" => "action.ejectDisks",
                    "hiddenFiles" => "action.hiddenFiles",
                    "desktopIcons" => "action.desktopIcons",
                    _ => "action.emptyTrash",
                },
                Title = title,
                EnglishTitle = title,
                Subtitle = Loc.T("Quick toggles"),
                Keywords = id == "darkMode" ? "Dark mode Light mode appearance theme" : "",
                Glyph = QuickToggleOps.Glyph(id),
                BeatMs = id is "lockScreen" or "displayOff" or "screenSaver" ? 150 : 100,
                Run = _ => QuickToggleOps.Run(ctx, toggleId),
            });
        }

        if (QuitAppsOps.Available(ctx))
        {
            rows.Add(new BarEntry
            {
                Id = "action.quitAll",
                Title = Loc.T("Quit all apps"),
                EnglishTitle = Loc.T("Quit all apps"),
                Subtitle = Loc.T("Quit apps"),
                Glyph = G(0xE711),
                Shortcut = ShortcutText.Of(ctx, "quitAll"),
                BeatMs = 100,
                Run = _ => QuitAppsOps.Show(ctx, false),
            });
            rows.Add(new BarEntry
            {
                Id = "action.quitOthers",
                Title = Loc.T("Quit all apps except the front one"),
                EnglishTitle = Loc.T("Quit all apps except the front one"),
                Subtitle = Loc.T("Quit apps"),
                Glyph = G(0xE711),
                Shortcut = ShortcutText.Of(ctx, "quitAllExceptFront"),
                BeatMs = 100,
                Run = _ => QuitAppsOps.Show(ctx, true),
            });
        }

        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "toggle-radio") is { } wifiRadio)
        {
            rows.Add(Simple("action.wifiRadio", wifiRadio.Title, Loc.T("Wi-Fi"), 0xE701, wifiRadio.Invoke, 100, Loc.T("Wi-Fi wireless radio on off")));
        }

        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "disconnect") is { } wifiDisconnect)
        {
            rows.Add(Simple("action.wifiDisconnect", wifiDisconnect.Title, Loc.T("Wi-Fi"), 0xE701, wifiDisconnect.Invoke, 100, Loc.T("Wi-Fi network disconnect")));
        }

        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "settings") is { } wifiSettings)
        {
            rows.Add(Simple("action.wifiSettings", wifiSettings.Title, Loc.T("Wi-Fi"), 0xE701, wifiSettings.Invoke, 100, Loc.T("Wi-Fi network settings")));
        }

        if (Has("url-cleaner") && Exact(ctx, "url-cleaner", "clean-clipboard") is { } clean)
        {
            rows.Add(Simple("action.cleanURL", Loc.T("Clean the copied link"), Loc.T("Clean URL"), 0xE71B, clean.Invoke, 100, Loc.T("Clean URL")));
        }

        if (Has("window-layout"))
        {
            foreach (var (raw, title) in BarData.LayoutActions)
            {
                var action = Exact(ctx, "window-layout", "layout." + raw, raw == "marginMaximize" ? "layout.maximizeWithMargin" : raw);
                if (action is null)
                {
                    continue;
                }

                var role = "windowLayout." + (raw == "marginMaximize" ? "maximizeWithMargin" : raw switch { "leftHalf" => "left", "rightHalf" => "right", "topHalf" => "top", "bottomHalf" => "bottom", _ => raw });
                rows.Add(new BarEntry
                {
                    Id = "action.layout." + raw,
                    Title = title,
                    EnglishTitle = title,
                    Subtitle = Loc.T("Window layout"),
                    Keywords = "Window layout" + (raw == "maximize" ? " Full Screen" : raw == "fullScreen" ? " Maximize" : ""),
                    Glyph = G(0xE8A0),
                    Shortcut = ShortcutText.Of(ctx, role),
                    BeatMs = 150,
                    Run = _ => action.Invoke(),
                });
            }
        }

        if (Has("app-updates"))
        {
            rows.Add(new BarEntry
            {
                Id = "action.appUpdates",
                Title = Loc.T("Check for app updates"),
                EnglishTitle = Loc.T("Check for app updates"),
                Subtitle = Loc.T("App updates"),
                Keywords = "App updates",
                Glyph = G(0xE896),
                Run = _ => ToolActions.Run(ctx, "appUpdates"),
            });
        }

        if (Has("cleaner"))
        {
            rows.Add(Simple("action.cleaner", Loc.T("Cleaner"), Tools, 0xE9CE, () => ctx.Shell.OpenSettings(SettingsPages.Cleaner), 100));
        }

        if (Has("uninstaller"))
        {
            rows.Add(Simple("action.uninstaller", Loc.T("Uninstaller"), Tools, 0xE74D, () => ctx.Shell.OpenSettings(SettingsPages.Uninstaller), 100));
        }

        Tool("action.quickLauncher", "quickLauncher", Loc.T("Quick panel"), 0xE80A, "quick-launcher", "quickLauncher");

        if (Has("notch-github") && Exact(ctx, "notch-github", "refresh") is { } githubRefresh)
        {
            rows.Add(Simple("action.githubRefresh", githubRefresh.Title, Loc.T("GitHub"), 0xE72C, githubRefresh.Invoke, 100, Loc.T("GitHub pull requests checks notifications")));
        }

        if (Has("notch-github") && Exact(ctx, "notch-github", "open-notifications") is { } githubNotifications)
        {
            rows.Add(Simple("action.githubNotifications", githubNotifications.Title, Loc.T("GitHub"), 0xE7ED, githubNotifications.Invoke, 100, Loc.T("GitHub notifications inbox")));
        }

        rows.Add(new BarEntry
        {
            Id = "emoji.browse",
            Title = Loc.T("Emoji"),
            EnglishTitle = Loc.T("Emoji"),
            Subtitle = Loc.T("Command Bar"),
            Keywords = "Emoji",
            Glyph = G(0xE76E),
            KeepsBarOpen = true,
            CountsUsage = true,
            Run = _ => s.SetCategory("emoji"),
        });

        if (Has("kill-process") && ctx.App.Get<bool>("killProcessCommandBarEnabled"))
        {
            rows.Add(new BarEntry
            {
                Id = "kill.browse",
                Title = Loc.T("Kill Process"),
                EnglishTitle = Loc.T("Kill Process"),
                Subtitle = Loc.T("Browse & Kill"),
                Glyph = G(0xEA39),
                KeepsBarOpen = true,
                CountsUsage = false,
                Run = _ =>
                {
                    s.SetQueryDirect("");
                    s.SetCategory("killProcess");
                },
            });
        }

        rows.Add(Simple("action.feedback.bug", Loc.T("Report a bug"), Loc.T("Send feedback"), 0xEBE8, () => FeedbackWindow.Open(ctx, "bug"), 150, Loc.T("Send feedback")));
        rows.Add(Simple("action.feedback.feature", Loc.T("Suggest a feature"), Loc.T("Send feedback"), 0xEA80, () => FeedbackWindow.Open(ctx, "feature"), 150, Loc.T("Send feedback")));
        rows.Add(Simple("action.restartApp", Loc.T("Restart Trayloom"), Loc.T("Trayloom actions"), 0xE72C, () => ctx.Shell.Restart(), 100));

        // power
        rows.Add(Simple("action.power.sleep", Loc.T("Sleep"), Loc.T("General"), 0xE708, BarSystem.Sleep, 150));
        rows.Add(Confirm("action.power.restart", Loc.T("Restart"), Loc.T("Restart the PC?"), 0xE777, () => BarSystem.Shutdown("/r")));
        rows.Add(Confirm("action.power.shutDown", Loc.T("Shut down"), Loc.T("Shut down the PC?"), 0xE7E8, () => BarSystem.Shutdown("/s")));
        rows.Add(Confirm("action.power.logOut", Loc.T("Log out"), Loc.T("Log out?"), 0xF3B1, () => BarSystem.Shutdown("/l")));

        if (s.WifiOn is { } wifi)
        {
            rows.Add(new BarEntry
            {
                Id = "action.wifi",
                Title = wifi ? Loc.T("Turn Wi-Fi off") : Loc.T("Turn Wi-Fi on"),
                EnglishTitle = wifi ? Loc.T("Turn Wi-Fi off") : Loc.T("Turn Wi-Fi on"),
                Subtitle = Loc.T("General"),
                Keywords = "wifi wi-fi",
                Glyph = G(wifi ? 0xE701 : 0xF384),
                IsActive = wifi,
                Run = v =>
                {
                    _ = BarSystem.SetWifiAsync(!wifi).ContinueWith(t =>
                    {
                        if (!t.Result)
                        {
                            s.Ui.BeginInvoke(() => System.Media.SystemSounds.Beep.Play());
                        }
                    });
                },
            });
        }

        // folders
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (name, path) in new[]
        {
            (Loc.T("Downloads"), Path.Combine(home, "Downloads")),
            (Loc.T("Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            (Loc.T("Videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            (Loc.T("Music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            (Path.GetFileName(home), home),
            (Loc.T("Applications"), "shell:AppsFolder"),
        })
        {
            var folderPath = path;
            rows.Add(new BarEntry
            {
                Id = "folder." + folderPath,
                Title = name,
                EnglishTitle = name,
                Subtitle = BarStrings.KindFolder,
                Keywords = "Folder " + name,
                Glyph = G(0xE8B7),
                IconLoader = () => Plat.ShellImage(folderPath, 32),
                RevealPath = folderPath.StartsWith("shell:", StringComparison.Ordinal) ? null : folderPath,
                Run = _ => Plat.Open(folderPath.StartsWith("shell:", StringComparison.Ordinal) ? "explorer.exe" : folderPath is { } p && Directory.Exists(p) ? p : "explorer.exe"),
            });
        }

        // answers
        if (BarSystem.Battery() is { } battery)
        {
            rows.Add(new BarEntry
            {
                Id = "answer.battery",
                Title = Loc.T("Battery"),
                EnglishTitle = Loc.T("Battery"),
                Subtitle = battery.State,
                Glyph = G(battery.State == "charging" ? 0xE83E : 0xE83F),
                AnswerValue = battery.Percent + "%",
                CountsUsage = false,
                Run = _ => CopyAnswer(battery.Percent + "%"),
            });
        }

        if (BarSystem.Memory() is { } mem)
        {
            rows.Add(new BarEntry
            {
                Id = "answer.memory",
                Title = Loc.T("Memory"),
                EnglishTitle = Loc.T("Memory"),
                Subtitle = Loc.F("{0} of {1} in use", BarSystem.Bytes(mem.Used), BarSystem.Bytes(mem.Total)),
                Glyph = G(0xE950),
                AnswerValue = mem.Percent + "%",
                CountsUsage = false,
                Run = _ => CopyAnswer($"{BarSystem.Bytes(mem.Used)} / {BarSystem.Bytes(mem.Total)}"),
            });
        }

        if (BarSystem.Storage() is { } storage)
        {
            var pct = storage.Total == 0 ? 0 : (int)Math.Round(100.0 * storage.Free / storage.Total);
            rows.Add(new BarEntry
            {
                Id = "answer.storage",
                Title = Loc.T("Storage"),
                EnglishTitle = Loc.T("Storage"),
                Subtitle = Loc.F("{0} available of {1}", BarSystem.Bytes(storage.Free), BarSystem.Bytes(storage.Total)),
                Glyph = G(0xEDA2),
                AnswerValue = pct + "%",
                CountsUsage = false,
                Run = _ => CopyAnswer(BarSystem.Bytes(storage.Free)),
            });
        }

        var now = DateTime.Now;
        rows.Add(new BarEntry
        {
            Id = "answer.date",
            Title = Loc.T("Today"),
            EnglishTitle = Loc.T("Today"),
            Subtitle = now.ToString("D", CultureInfo.CurrentCulture),
            Glyph = G(0xE787),
            AnswerValue = now.ToString("d", CultureInfo.CurrentCulture),
            CountsUsage = false,
            Run = _ => CopyAnswer(now.ToString("d", CultureInfo.CurrentCulture)),
        });
        rows.Add(new BarEntry
        {
            Id = "answer.time",
            Title = Loc.T("Time now"),
            EnglishTitle = Loc.T("Time now"),
            Subtitle = now.ToString("T", CultureInfo.CurrentCulture),
            Glyph = G(0xE121),
            AnswerValue = now.ToString("T", CultureInfo.CurrentCulture),
            CountsUsage = false,
            Run = _ => CopyAnswer(now.ToString("T", CultureInfo.CurrentCulture)),
        });
        rows.Add(Simple("action.openSettings", Loc.T("Open Settings"), BarStrings.SettingsSubtitle, 0xE713, () => ctx.Shell.OpenSettings(SettingsPages.General), 100));

        // ---- feature toggles ----
        foreach (var entry in FeatureCatalog.All)
        {
            if (!entry.CanInstall || !ctx.Runtime.IsAvailable(entry.Id))
            {
                continue;
            }

            if (entry.Id == "scroll-inverter")
            {
                rows.Add(Toggle(s, entry, "scrollInverterEnabled", Loc.T("Invert vertical scrolling"), ".vertical"));
                rows.Add(Toggle(s, entry, "scrollInverterHorizontalEnabled", Loc.T("Invert horizontal scrolling"), ".horizontal"));
                continue;
            }

            if (entry.EnabledKeys.Count == 1)
            {
                rows.Add(Toggle(s, entry, entry.EnabledKeys[0], null, ""));
            }
        }

        // ---- Settings pages ----
        var covered = new HashSet<string> { SettingsPages.General, SettingsPages.Cleaner, SettingsPages.Uninstaller, SettingsPages.AppUpdates, SettingsPages.Features };
        foreach (var page in SettingsPages.All.Where(p => !covered.Contains(p)))
        {
            var features = FeatureCatalog.All.Where(f => f.HasSettingsPage && f.Page == page).ToList();
            if (features.Count > 0 && !features.Any(f => ctx.Runtime.IsAvailable(f.Id)))
            {
                continue;
            }

            var pageId = page;
            rows.Add(new BarEntry
            {
                Id = "settings." + page,
                Title = SettingsPages.TitleOf(page),
                EnglishTitle = SettingsPages.TitleOf(page),
                Subtitle = BarStrings.SettingsSubtitle,
                Keywords = string.Join(' ', features.Select(f => f.Title)),
                Glyph = G(0xE713),
                BeatMs = 100,
                Run = _ => ctx.Shell.OpenSettings(pageId),
            });
        }

        foreach (var entry in FeatureCatalog.All.Where(f => f.CanInstall && f.HasSettingsPage && ctx.Runtime.IsAvailable(f.Id)))
        {
            var e = entry;
            if (covered.Contains(entry.Page) || string.Equals(SettingsPages.TitleOf(entry.Page), entry.Title, StringComparison.OrdinalIgnoreCase))
            {
                // Its page row above already says the same thing (a second "Quick panel" row would be a duplicate).
                continue;
            }

            rows.Add(new BarEntry
            {
                Id = "settings.feature." + entry.Id,
                Title = entry.Title,
                EnglishTitle = entry.Title,
                Subtitle = BarStrings.SettingsSubtitle,
                Keywords = entry.Description,
                Glyph = string.IsNullOrEmpty(entry.Glyph) ? G(0xE713) : entry.Glyph,
                BeatMs = 100,
                FeatureKey = entry.Id,
                FeatureTurn = 2,
                Run = _ => ctx.Shell.OpenSettings(e.Destination, e.Id),
            });
        }

        // ---- snippets ----
        if (ctx.Runtime.IsAvailable("text-snippets"))
        {
            foreach (var snippet in BarProviders.Snippets(ctx.App))
            {
                var sn = snippet;
                rows.Add(new BarEntry
                {
                    Id = "snippet." + sn.Id,
                    Title = sn.Name,
                    EnglishTitle = sn.Name,
                    Subtitle = BarStrings.KindSnippet,
                    Keywords = sn.Trigger + " " + sn.Folder,
                    Glyph = G(0xE8D2),
                    BeatMs = 100,
                    Run = _ => BarProviders.TypeAtCaret(s.Ui, s.Target, () =>
                    {
                        if (BarProviders.Hub?.Get<ISnippetsService>() is { } snippets && snippets.Insert(sn.Id))
                        {
                            return;
                        }

                        InputSender.SendText(sn.Replacement);
                    }),
                });
            }
        }

        // ---- your shortcuts ----
        foreach (var link in s.Prefs.Links())
        {
            rows.Add(LinkEntry(s, link));
        }

        return rows;
    }

    private static BarEntry Simple(string id, string title, string subtitle, int glyph, Action run, int beat, string? keywords = null, string? shortcut = null) => new()
    {
        Id = id,
        Title = title,
        EnglishTitle = title,
        Subtitle = subtitle,
        Glyph = char.ConvertFromUtf32(glyph),
        Keywords = keywords ?? "",
        Shortcut = shortcut,
        BeatMs = beat,
        Run = _ => run(),
    };

    private static BarEntry Confirm(string id, string title, string prompt, int glyph, Action run) => new()
    {
        Id = id,
        Title = title,
        EnglishTitle = title,
        Subtitle = Loc.T("General"),
        Glyph = char.ConvertFromUtf32(glyph),
        ConfirmPrompt = prompt,
        Destructive = true,
        BeatMs = 150,
        Run = _ => run(),
    };

    private static void CopyAnswer(string value)
    {
        try
        {
            Clipboard.SetText(value);
            ToolHud.Show("", value);
        }
        catch
        {
            ToolHud.Show("", Loc.T("Couldn’t copy"));
        }
    }

    private static BarEntry Toggle(BarService s, FeatureCatalogEntry entry, string key, string? overrideTitle, string suffix)
    {
        var on = s.Ctx.App.Get<bool>(key);
        var name = overrideTitle ?? entry.Title;
        var title = overrideTitle is null ? (on ? Loc.F("Turn off {0}", name) : Loc.F("Turn on {0}", name)) : (on ? $"Turn off: {name}" : name);
        var glyph = string.IsNullOrEmpty(entry.Glyph) ? "" : entry.Glyph;
        return new BarEntry
        {
            Id = "toggle." + entry.Id + suffix,
            Title = title,
            EnglishTitle = title,
            Subtitle = entry.Group.DisplayName(),
            Keywords = name,
            Glyph = glyph,
            IsActive = on,
            FeatureKey = entry.Id,
            FeatureTurn = 0,
            BeatMs = 50,
            Run = _ =>
            {
                s.Ctx.App.Set(key, !on);
                ToolHud.Show(glyph, name);
            },
        };
    }

    // ------------------------------------------------------------------ links

    public static BarEntry LinkEntry(BarService s, CommandBarLink link)
    {
        var glyph = link.Kind switch { "place" => "", "script" => "", _ => "" };
        var subtitle = link.Kind == "script"
            ? (link.RunsWithoutArgument ? Loc.T("Runs on its own, or type what to send") : Loc.T("Type what to send after the name"))
            : link.TakesQuery ? Loc.T("Type what to look for after the name") : BarStrings.KindLink;
        return new BarEntry
        {
            Id = "link." + link.Id,
            Title = link.Name,
            EnglishTitle = link.Name,
            Subtitle = subtitle,
            Glyph = glyph,
            TakesArgument = link.TakesArgument,
            KeepsBarOpen = link.TakesArgument,
            RankingTitle = link.Name,
            RevealPath = link.Kind == "place" && !link.Destination.Contains('{') ? Expand(link.Destination) : null,
            Run = _ => s.RunLink(link),
        };
    }

    public static string Expand(string path)
    {
        var p = path.Trim();
        if (p.StartsWith('~'))
        {
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.TrimStart('~').TrimStart('\\', '/'));
        }

        return Environment.ExpandEnvironmentVariables(p);
    }

    // ------------------------------------------------------------------ other pools

    public static List<BarEntry> AppEntries(BarService s)
    {
        var running = BarProviders.RunningNames();
        return
        [
            .. BarProviders.Apps.Select(app => new BarEntry
            {
                Id = app.StableKey,
                Title = app.Name,
                EnglishTitle = app.Name,
                Subtitle = BarStrings.KindApp,
                Keywords = app.Keywords,
                IconLoader = () => Plat.ShellImage(app.Launch, 32),
                Glyph = "",
                IsActive = running.Contains(BarSearch.Normalize(app.Name)) || running.Any(r => r.Length > 3 && BarSearch.Normalize(app.Name) == r),
                Run = _ =>
                {
                    if (!s.HideAppInFront(app))
                    {
                        BarProviders.Launch(app);
                    }
                },
            }),
        ];
    }

    public static List<BarEntry> PaneEntries(BarService s) =>
    [
        .. BarData.SettingsPanes.Select(p => new BarEntry
        {
            Id = "macsettings." + p.Uri,
            Title = p.Name,
            EnglishTitle = p.Name,
            Subtitle = BarStrings.PanesSubtitle,
            Keywords = p.Keywords,
            Glyph = "",
            BeatMs = 100,
            Run = _ => Plat.Open(p.Uri),
        }),
    ];

    public static List<BarEntry> WindowEntries(BarService s) =>
    [
        .. BarProviders.Windows().Select(w => new BarEntry
        {
            Id = "window." + w.Handle.ToString("x"),
            Title = w.Title,
            EnglishTitle = w.Title,
            Subtitle = w.AppName,
            Keywords = "Window " + w.AppName,
            Glyph = "",
            IconLoader = () => w.ExePath is null ? null : Plat.ShellImage(w.ExePath, 32),
            CountsUsage = false,
            BeatMs = 100,
            Run = _ => Plat.Activate(w.Handle),
        }),
    ];

    public static List<BarEntry> QuitEntries(BarService s)
    {
        var result = new List<BarEntry>();
        var seen = new HashSet<int>();
        foreach (var w in BarProviders.Windows())
        {
            var pid = Plat.ProcessOf(w.Handle);
            if (!seen.Add(pid) || w.ExePath is null || string.Equals(Path.GetFileName(w.ExePath), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = w.AppName;
            var processId = pid;
            result.Add(new BarEntry
            {
                Id = "quit." + pid,
                Title = Loc.F("Quit {0}", name),
                EnglishTitle = Loc.F("Quit {0}", name),
                Subtitle = BarStrings.KindApp,
                Keywords = $"Quit {name}",
                Glyph = "",
                IconLoader = () => Plat.ShellImage(w.ExePath, 32),
                ConfirmPrompt = Loc.F("Quit {0}?", name),
                BeatMs = 120,
                Run = _ =>
                {
                    try
                    {
                        using var p = Process.GetProcessById(processId);
                        p.CloseMainWindow();
                    }
                    catch
                    {
                        System.Media.SystemSounds.Beep.Play();
                    }
                },
            });
        }

        return result;
    }

    public static List<BarEntry> MenuEntries(BarService s, IEnumerable<BarMenuItem> items) =>
    [
        .. items.Select((m, i) => new BarEntry
        {
            Id = $"menu.{i}.{m.Title}",
            Title = m.Title,
            EnglishTitle = m.Title,
            Subtitle = string.Join(" › ", new[] { s.TargetAppName }.Concat(m.Path).Where(x => !string.IsNullOrEmpty(x))),
            Keywords = $"Menu command {s.TargetAppName} {string.Join(' ', m.Path)}",
            Glyph = "",
            MenuShortcut = m.Shortcut,
            CountsUsage = false,
            BeatMs = 100,
            Run = _ =>
            {
                if (!BarProviders.PressMenu(m))
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            },
        }),
    ];

    public static List<BarEntry> ClipboardEntries(BarService s, int limit) =>
    [
        .. BarProviders.Clips().Take(limit).Select(c => new BarEntry
        {
            Id = "clipboard." + c.Id,
            Title = c.Kind == "image" ? Loc.T("Image") : c.Kind == "files" ? string.Join(", ", c.Files.Select(Path.GetFileName)) : Preview(c.Text),
            EnglishTitle = "",
            Subtitle = BarStrings.KindClipboard,
            Glyph = c.Kind == "image" ? "" : c.Kind == "files" ? "" : "",
            CountsUsage = false,
            BeatMs = 100,
            Run = _ => BarProviders.PasteClip(s.Ui, s.Target, c),
        }),
    ];

    private static string Preview(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 80 ? flat[..80] + "…" : flat;
    }

    public static List<BarEntry> EmojiEntries(BarService s) =>
    [
        .. CommandBarEmoji.All.Select(e => new BarEntry
        {
            Id = "emoji." + e.Glyph,
            Title = e.Glyph + "  " + e.Name,
            EnglishTitle = e.Name,
            MatchTitle = e.Name,
            Subtitle = BarStrings.KindEmoji,
            Keywords = e.Name + " " + e.Keywords + " Emoji",
            Glyph = e.Glyph,
            BeatMs = 100,
            ExtraActions = () => e.CanTone ? EmojiToneActions(s, e) : [],
            Run = _ => BarProviders.TypeAtCaret(s.Ui, s.Target, () => InputSender.SendText(CommandBarEmoji.ApplyTone(e, s.Prefs.SkinTone))),
        }),
    ];

    private static IReadOnlyList<BarAction> EmojiToneActions(BarService s, EmojiEntry e)
    {
        var list = new List<BarAction>();
        for (var i = 0; i < BarData.SkinTones.Length; i++)
        {
            var tone = BarData.SkinTones[i];
            if (tone == s.Prefs.SkinTone)
            {
                continue;
            }

            var toned = CommandBarEmoji.ApplyTone(e, tone);
            list.Add(new BarAction
            {
                Id = "tone." + tone,
                Title = toned,
                Glyph = "",
                Run = () => BarProviders.TypeAtCaret(s.Ui, s.Target, () => InputSender.SendText(toned)),
            });
        }

        return list;
    }

    public static List<BarEntry> FileEntries(BarService s, IEnumerable<string> paths)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            .. paths.Select(p => new BarEntry
            {
                Id = "file." + p,
                Title = Path.GetFileName(p),
                EnglishTitle = Path.GetFileName(p),
                Subtitle = p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + p[home.Length..] : p,
                Keywords = "Files",
                Glyph = "",
                IconLoader = () => Plat.ShellImage(p, 32),
                CountsUsage = false,
                RevealPath = p,
                Run = _ =>
                {
                    if (!File.Exists(p) && !Directory.Exists(p) || !Plat.Open(p))
                    {
                        ToolHud.Show("", Path.GetFileName(p));
                    }
                },
            }),
        ];
    }

    public static List<BarEntry> KillEntries()
    {
        var result = new List<BarEntry>();
        if (BarProviders.Hub?.Get<IProcessKillService>() is { } kill)
        {
            // Kill Process owns the list (protected processes are never offered) and the identity-verified kill.
            foreach (var e in kill.Browse(string.Empty, 400))
            {
                var pid = e.Pid;
                result.Add(new BarEntry
                {
                    Id = "kill." + pid,
                    Title = e.Name,
                    EnglishTitle = e.Name,
                    Subtitle = Loc.F("PID {0}", pid),
                    Keywords = "",
                    Glyph = "",
                    CountsUsage = false,
                    ConfirmPrompt = Loc.F("Kill {0}?", e.Name),
                    Destructive = true,
                    Run = _ =>
                    {
                        if (!kill.Kill(pid))
                        {
                            System.Media.SystemSounds.Beep.Play();
                        }
                    },
                });
            }

            return [.. result.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)];
        }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4 || p.Id == Environment.ProcessId)
                {
                    continue;
                }

                var pid = p.Id;
                var name = p.ProcessName;
                result.Add(new BarEntry
                {
                    Id = "kill." + pid,
                    Title = name,
                    EnglishTitle = name,
                    Subtitle = Loc.F("PID {0}", pid),
                    Keywords = "",
                    Glyph = "",
                    CountsUsage = false,
                    ConfirmPrompt = Loc.F("Kill {0}?", name),
                    Destructive = true,
                    Run = _ =>
                    {
                        try
                        {
                            using var target = Process.GetProcessById(pid);
                            target.Kill();
                        }
                        catch
                        {
                            System.Media.SystemSounds.Beep.Play();
                        }
                    },
                });
            }
            catch
            {
                // skip
            }
            finally
            {
                p.Dispose();
            }
        }

        return [.. result.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static List<BarEntry> SelectionEntries(BarService s)
    {
        var text = s.SelectionText;
        var result = new List<BarEntry>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        BarEntry Row(string id, string title, string subtitle, Action run, bool keepOpen = false, string? chip = null) => new()
        {
            Id = "selection." + id,
            Title = title,
            EnglishTitle = title,
            Subtitle = subtitle,
            Glyph = "",
            CountsUsage = false,
            KeepsBarOpen = keepOpen,
            AnswerValue = chip,
            BeatMs = 100,
            Run = _ => run(),
        };

        result.Add(Row("copy", Loc.T("Copy it"), BarStrings.KindSelection, () => CopyAnswer(text)));
        result.Add(Row("search", Loc.T("Use it in the search"), BarStrings.KindSelection, () => s.SetQueryDirect(text), true));
        if (s.Ctx.Runtime.IsAvailable("url-cleaner") && Uri.TryCreate(text, UriKind.Absolute, out _) && ActionBridge.Find(s.Ctx, "url-cleaner", "clean-clipboard") is { } clean)
        {
            result.Add(Row("cleanLink", Loc.T("Clean the copied link"), BarStrings.KindSelection, () =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                    return;
                }

                clean.Invoke();
            }));
        }

        if (s.SelectionTypesOver && text.Length <= 5000)
        {
            var info = CultureInfo.CurrentCulture.TextInfo;
            foreach (var (id, title, transformed) in new[]
            {
                ("upper", Loc.T("UPPERCASE"), text.ToUpper(CultureInfo.CurrentCulture)),
                ("lower", Loc.T("lowercase"), text.ToLower(CultureInfo.CurrentCulture)),
                ("titleCase", Loc.T("Title Case"), info.ToTitleCase(text.ToLower(CultureInfo.CurrentCulture))),
            })
            {
                if (transformed != text)
                {
                    var t = transformed;
                    result.Add(Row(id, title, BarStrings.KindSelection, () => BarProviders.TypeAtCaret(s.Ui, s.Target, () => InputSender.SendText(t))));
                }
            }
        }

        if (s.Ctx.Runtime.IsAvailable("shelf") && s.Ctx.App.Get<bool>("shelfEnabled"))
        {
            result.Add(Row("shelf", Loc.T("Keep it on the shelf"), BarStrings.KindSelection, () =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                    // ignored
                }

                ToolActions.Run(s.Ctx, "shelf");
            }));
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var chars = new StringInfo(text).LengthInTextElements;
        var count = Row("count", Loc.T("Count it"), Loc.F("Words: {0}, characters: {1}", words, chars), () => CopyAnswer(Loc.F("Words: {0}, characters: {1}", words, chars)), false, chars.ToString(CultureInfo.CurrentCulture));
        result.Add(count);
        return result;
    }
}
