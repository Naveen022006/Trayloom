// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Quick tools page, "Away mode" card (spec 06 section 4.10).</summary>
public sealed class AwaySettingsSection : ISettingsSection
{
    private static FrameworkElement? _owner;
    private static Window? _ownerWindow;

    public string? FeatureId => "away-mode";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "awayMode";

    public string? Title => AwayStrings.Title;

    public IReadOnlyList<string> Keywords => ["privacy", "idle", "face", "camera", "minimize", "lock", "presence", "close", "browser", "tabs", "reopen"];

    public int Order => 90;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        AwayKeys.Register(c.App);
        var app = c.App;
        var controller = c.Services.Get<AwayController>();
        var lockOn = app.Get<bool>(AwayKeys.LockPc);
        var faceOn = app.Get<bool>(AwayKeys.Face);
        var faceSupported = AwayFaceWatcher.IsSupported;
        var card = new SettingsCard(AwayStrings.Title);

        if (controller is { RecoveryCount: > 0 } recovering && !controller.IsAway)
        {
            card.Add(RecoveryCard(recovering, c.RequestRebuild));
        }

        card.Add(Switch("", AwayStrings.Title, AwayStrings.Description, AwayKeys.Enabled, "AwayMode.Enabled", app));

        var idle = new PickerRow<int>(null, AwayStrings.AwayAfter, null,
        [
            (2, AwayStrings.Minutes2), (5, AwayStrings.Minutes5), (10, AwayStrings.Minutes10),
            (15, AwayStrings.Minutes15), (30, AwayStrings.Minutes30), (60, AwayStrings.Hour1),
        ], app.Get<int>(AwayKeys.IdleMinutes), value => app.Set(AwayKeys.IdleMinutes, value), 230);
        Identify(idle, "AwayMode.IdleMinutes", AwayStrings.AwayAfter);
        idle.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        card.Add(idle);

        var status = UiKit.Text(StatusText(controller), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        status.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        AutomationProperties.SetAutomationId(status, "AwayMode.Status");
        card.Add(status);

        card.AddDivider();
        card.Add(Switch("", AwayStrings.LockPc, AwayStrings.LockPcCaption, AwayKeys.LockPc, "AwayMode.Lock", app, onToggled: _ => c.RequestRebuild()));
        var cover = Switch("", AwayStrings.Cover, AwayStrings.CoverCaption, AwayKeys.Cover, "AwayMode.Cover", app);
        cover.IsEnabled = !lockOn;
        card.Add(cover);

        card.AddDivider();
        var faceCaption = lockOn ? AwayStrings.FaceNotNeeded : !faceSupported ? AwayStrings.FaceUnsupported : AwayStrings.FaceCaption;
        var face = Switch("", AwayStrings.Face, faceCaption, AwayKeys.Face, "AwayMode.Face", app, onToggled: _ => c.RequestRebuild());
        face.IsEnabled = !lockOn && faceSupported;
        card.Add(face);

        if (faceOn && !lockOn)
        {
            var cameraHost = new StackPanel();
            card.Add(cameraHost);
            AwayGuard.FireAndLog(() => FillCamerasAsync(cameraHost, app, c.Log), c.Log, "camera list");

            var stop = new PickerRow<int>(null, AwayStrings.StopLooking, null,
            [
                (30, AwayStrings.Stop30), (60, AwayStrings.Stop60), (120, AwayStrings.Stop120), (240, AwayStrings.Stop240), (0, AwayStrings.StopNever),
            ], app.Get<int>(AwayKeys.FaceStopMinutes), value => app.Set(AwayKeys.FaceStopMinutes, value), 160);
            Identify(stop, "AwayMode.FaceStop", AwayStrings.StopLooking);
            stop.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
            card.Add(stop);

            if (AwayFaceWatcher.AccessDenied)
            {
                var denied = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
                denied.Children.Add(UiKit.Text(AwayStrings.CameraDenied, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true));
                var open = UiKit.Button(AwayStrings.OpenCameraSettings, ButtonKind.Bordered, 11.5, onClick: OpenCameraSettings);
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 6, 0, 0);
                denied.Children.Add(open);
                card.Add(denied);
            }

            if (app.Get<bool>(AwayKeys.CameraFailed))
            {
                var failed = UiKit.Text(AwayStrings.CameraFailed, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
                failed.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
                card.Add(failed);
            }
        }

        AddCloseBlock(card, c, controller);

        card.AddDivider();
        card.Add(UiKit.Text(AwayStrings.StayHereWhen, 12, FontWeights.SemiBold));
        card.Add(Inset(Switch(null, AwayStrings.SkipMedia, null, AwayKeys.SkipMedia, "AwayMode.SkipMedia", app)));
        card.Add(Inset(Switch(null, AwayStrings.SkipCalls, null, AwayKeys.SkipCalls, "AwayMode.SkipCalls", app)));
        card.Add(Inset(Switch(null, AwayStrings.SkipFullScreen, null, AwayKeys.SkipFullScreen, "AwayMode.SkipFullScreen", app)));

        var ignored = new AppBundleList(AwayStrings.IgnoredTitle, AwayStrings.IgnoredCaption,
            () => [.. app.Get<List<string>>(AwayKeys.IgnoredApps)],
            list => app.Set(AwayKeys.IgnoredApps, list),
            AwayStrings.IgnoredAdd);

        // A plain panel is not part of the UI Automation tree, so the list sits in a small landmark that carries the id.
        var landmark = new Landmark { Content = ignored, Focusable = false, IsTabStop = false };
        AutomationProperties.SetAutomationId(landmark, "AwayMode.Ignored");
        AutomationProperties.SetName(landmark, AwayStrings.IgnoredTitle);
        card.Add(landmark);

        card.AddDivider();
        var now = UiKit.Button(controller?.IsAway == true ? AwayStrings.RestoreWindows : AwayStrings.GoAwayNow, ButtonKind.Prominent, 13, onClick: () =>
        {
            if (controller is null)
            {
                return;
            }

            if (controller.IsAway)
            {
                controller.Return("manual");
            }
            else
            {
                controller.GoAway("manual");
            }
        });
        now.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(now, "AwayMode.Now");
        AutomationProperties.SetName(now, now.Content as string ?? AwayStrings.GoAwayNow);
        card.Add(now);
        var closing = app.Get<bool>(AwayKeys.CloseApps) && app.Get<int>(AwayKeys.CloseAfterMinutes) == 0 && !(controller?.Elevated ?? false);
        var lookingNow = faceOn && !lockOn;
        card.AddCaption(closing
            ? (lookingNow ? AwayStrings.NowCaptionCloseFace : AwayStrings.NowCaptionClose)
            : (lookingNow ? AwayStrings.NowCaptionFace : AwayStrings.NowCaption));

        Wire(card, controller, status, now, c.RequestRebuild);
        return card;
    }

    /// <summary>The "Close apps while I’m away" switch, the delay, the list of apps and what happened last time (spec 07 section 9.1).</summary>
    private static void AddCloseBlock(SettingsCard card, ISettingsContext c, AwayController? controller)
    {
        var app = c.App;
        var elevated = controller?.Elevated ?? AppIdentity.IsAdmin();
        var closeOn = app.Get<bool>(AwayKeys.CloseApps) && !elevated;

        card.AddDivider();
        var toggle = new MacSwitch(closeOn, SwitchSize.Regular, on =>
        {
            if (on)
            {
                SeedCloseList(app);
            }

            app.Set(AwayKeys.CloseApps, on);
            c.RequestRebuild();
        });
        AutomationProperties.SetAutomationId(toggle, "AwayMode.CloseApps");
        AutomationProperties.SetName(toggle, AwayStrings.CloseApps);
        var row = new SettingsRow("", AwayStrings.CloseApps, elevated ? AwayStrings.CloseAppsElevated : AwayStrings.CloseAppsCaption, toggle)
        {
            IsEnabled = !elevated,
        };
        card.Add(row);
        if (!closeOn)
        {
            AddLastTime(card, controller);
            return;
        }

        var after = new PickerRow<int>(null, AwayStrings.CloseThem, null,
        [
            (0, AwayStrings.CloseNow), (5, AwayStrings.CloseAfter5), (15, AwayStrings.CloseAfter15),
            (30, AwayStrings.CloseAfter30), (60, AwayStrings.CloseAfter60),
        ], app.Get<int>(AwayKeys.CloseAfterMinutes), value =>
        {
            app.Set(AwayKeys.CloseAfterMinutes, value);
            c.RequestRebuild();
        }, 230);
        Identify(after, "AwayMode.CloseAfter", AwayStrings.CloseThem);
        after.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        card.Add(after);

        var list = new AppBundleList(AwayStrings.CloseListTitle, AwayStrings.CloseListCaption,
            () => [.. app.Get<List<string>>(AwayKeys.CloseList)],
            items => app.Set(AwayKeys.CloseList, items),
            AwayStrings.CloseListAdd,
            accessory: Accessory);
        var landmark = new Landmark { Content = list, Focusable = false, IsTabStop = false, Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0) };
        AutomationProperties.SetAutomationId(landmark, "AwayMode.CloseList");
        AutomationProperties.SetName(landmark, AwayStrings.CloseListTitle);
        card.Add(landmark);

        var note = UiKit.Text(AwayStrings.CloseNote, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        note.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        card.Add(note);
        AddLastTime(card, controller);
    }

    /// <summary>What will happen to a listed app, as a small caption at the end of its row.</summary>
    private static UIElement? Accessory(string path)
    {
        var kind = AwayApps.CaptionKind(path);
        var text = kind switch
        {
            "browser" => AwayStrings.AccessoryBrowser,
            "browser-min" => AwayStrings.AccessoryBrowserMin,
            "never" => AwayStrings.AccessoryNever,
            _ => AwayStrings.AccessoryApp,
        };
        var block = UiKit.Text(text, 11, FontWeights.Normal, kind == "never" ? ThemeKeys.Orange : ThemeKeys.TextSecondary, wrap: true);
        block.MaxWidth = 190;
        block.TextAlignment = TextAlignment.Right;
        return block;
    }

    /// <summary>The first time the switch is turned on, the installed browsers Trayloom can reopen with their tabs are added once.</summary>
    private static void SeedCloseList(IAppSettings app)
    {
        if (app.Get<bool>(AwayKeys.CloseListSeeded))
        {
            return;
        }

        var list = app.Get<List<string>>(AwayKeys.CloseList);
        foreach (var browser in AwayApps.InstalledClosableBrowsers())
        {
            if (!list.Contains(browser))
            {
                list.Add(browser);
            }
        }

        app.Set(AwayKeys.CloseList, list);
        app.Set(AwayKeys.CloseListSeeded, true);
    }

    /// <summary>"Last time you were away": apps that stayed open or did not open again, with the reason.</summary>
    private static void AddLastTime(SettingsCard card, AwayController? controller)
    {
        if (controller is null || controller.IsAway || controller.LastResult.Count == 0)
        {
            return;
        }

        var stack = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 8, 0, 0) };
        var title = UiKit.Text(AwayStrings.LastTime, 12, FontWeights.SemiBold);
        stack.Children.Add(title);
        foreach (var (name, reason) in controller.LastResult)
        {
            var line = UiKit.Text(AwayStrings.LastLine(name, AwayStrings.Reason(reason)), 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            line.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(line);
        }

        var landmark = new Landmark { Content = stack, Focusable = false, IsTabStop = false };
        AutomationProperties.SetAutomationId(landmark, "AwayMode.LastTime");
        AutomationProperties.SetName(landmark, AwayStrings.LastTime);
        card.Add(landmark);
    }

    /// <summary>The card that offers apps closed by an earlier run (or that did not open at the last return): open them or forget them.</summary>
    private static FrameworkElement RecoveryCard(AwayController controller, Action rebuild)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Icon("", 14, ThemeKeys.Orange));
        var title = UiKit.Text(AwayStrings.RecoveryTitle, 12.5, FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(title);
        stack.Children.Add(head);

        var count = controller.RecoveryCount;
        var text = controller.RecoveryFailedAtReturn
            ? AwayStrings.RecoveryFailed(count)
            : AwayStrings.RecoveryText(count, controller.RecoverySince.ToString("g"));
        var body = UiKit.Text(text, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        body.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(body);
        foreach (var name in controller.RecoveryNames)
        {
            var line = UiKit.Text(name, 11.5, FontWeights.Normal, ThemeKeys.TextPrimary);
            line.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(line);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var reopen = UiKit.Button(AwayStrings.OpenThemAgain, ButtonKind.Prominent, 12, onClick: () =>
        {
            controller.ReopenRecovery();
            rebuild();
        });
        AutomationProperties.SetAutomationId(reopen, "AwayMode.Reopen");
        AutomationProperties.SetName(reopen, AwayStrings.OpenThemAgain);
        var forget = UiKit.Button(AwayStrings.ForgetThem, ButtonKind.Bordered, 12, onClick: () =>
        {
            controller.ForgetRecovery();
            rebuild();
        });
        forget.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetAutomationId(forget, "AwayMode.Forget");
        AutomationProperties.SetName(forget, AwayStrings.ForgetThem);
        buttons.Children.Add(reopen);
        buttons.Children.Add(forget);
        stack.Children.Add(buttons);

        var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = stack };
        box.Themed(Border.BorderBrushProperty, ThemeKeys.Orange);
        return box;
    }

    /// <summary>The card reports "in use" to the idle check while it is loaded in an active window (the person is configuring).</summary>
    private static void Wire(SettingsCard card, AwayController? controller, TextBlock status, Button now, Action rebuild)
    {
        // The recovery card and the "Last time" lines are part of the layout: when they change, the card is built again.
        var shown = (controller?.RecoveryCount ?? 0, controller?.LastResult.Count ?? 0, controller?.IsAway ?? false);

        void Refresh()
        {
            var current = (controller?.RecoveryCount ?? 0, controller?.LastResult.Count ?? 0, controller?.IsAway ?? false);
            if (current != shown)
            {
                shown = current;
                rebuild();
                return;
            }

            status.Text = StatusText(controller);
            var label = controller?.IsAway == true ? AwayStrings.RestoreWindows : AwayStrings.GoAwayNow;
            now.Content = label;
            AutomationProperties.SetName(now, label);
        }

        card.Loaded += (_, _) =>
        {
            _owner = card;
            _ownerWindow = Window.GetWindow(card);
            AwayPresence.SettingsCardInUse = () => _owner is { IsLoaded: true } && _ownerWindow is { IsActive: true };
            if (controller is not null)
            {
                controller.Changed += Refresh;
            }

            Refresh();
        };
        card.Unloaded += (_, _) =>
        {
            if (ReferenceEquals(_owner, card))
            {
                _owner = null;
                _ownerWindow = null;
            }

            if (controller is not null)
            {
                controller.Changed -= Refresh;
            }
        };
    }

    private static string StatusText(AwayController? controller)
    {
        if (controller is null)
        {
            return AwayStrings.Here;
        }

        if (controller.ReopeningCount > 0)
        {
            return AwayStrings.Opening(controller.ReopeningCount);
        }

        if (controller.IsAway)
        {
            var time = controller.Since.ToString("t");
            if (controller.CloseJobState == AwayCloseJobState.Closing)
            {
                return AwayStrings.Closing;
            }

            if (controller.ClosedAppCount > 0)
            {
                return AwayStrings.AwaySinceClosed(time, controller.WindowCount, controller.ClosedAppCount);
            }

            return controller.CloseJobState == AwayCloseJobState.Waiting
                ? AwayStrings.AwaySinceWaiting(time, controller.WindowCount, controller.CloseAt.ToString("t"))
                : AwayStrings.AwaySince(time, controller.WindowCount);
        }

        return controller.Suppression == AwaySuppression.None ? AwayStrings.Here : AwayStrings.Waiting(AwayPresence.Describe(controller.Suppression));
    }

    private static SettingsRow Switch(string? glyph, string title, string? caption, string key, string automationId, IAppSettings app, Action<bool>? onToggled = null)
    {
        var toggle = new MacSwitch(app.Get<bool>(key), SwitchSize.Regular, on =>
        {
            app.Set(key, on);
            onToggled?.Invoke(on);
        });
        AutomationProperties.SetAutomationId(toggle, automationId);
        AutomationProperties.SetName(toggle, title);
        return new SettingsRow(glyph, title, caption, toggle);
    }

    private static FrameworkElement Inset(FrameworkElement row)
    {
        row.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        return row;
    }

    /// <summary>Puts the automation id and name on the combo box inside a picker row (the row itself is only a layout grid).</summary>
    private static void Identify(DependencyObject root, string id, string name)
    {
        if (Find<ComboBox>(root) is { } combo)
        {
            AutomationProperties.SetAutomationId(combo, id);
            AutomationProperties.SetName(combo, name);
        }
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T hit)
            {
                return hit;
            }

            if (child is DependencyObject d && Find<T>(d) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static async Task FillCamerasAsync(StackPanel host, IAppSettings app, ILog log)
    {
        IReadOnlyList<(string Id, string Name)> cameras;
        try
        {
            cameras = await AwayFaceWatcher.ListCamerasAsync();
        }
        catch (Exception ex)
        {
            log.Info($"[away] camera list: {ex.GetType().Name} 0x{ex.HResult:X8}");
            return;
        }

        if (cameras.Count < 2)
        {
            return;
        }

        var current = app.Get<string>(AwayKeys.CameraId) ?? string.Empty;
        var row = new PickerRow<string>(null, AwayStrings.Camera, null, [.. cameras.Select(c => (c.Id, c.Name))],
            cameras.Any(c => c.Id == current) ? current : cameras[0].Id, value => app.Set(AwayKeys.CameraId, value), 230);
        Identify(row, "AwayMode.Camera", AwayStrings.Camera);
        row.Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0);
        host.Children.Add(row);
    }

    /// <summary>A content control that shows up in the UI Automation tree, so a test can find the list inside.</summary>
    private sealed class Landmark : ContentControl
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
            new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
    }

    private static void OpenCameraSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-webcam") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Settings could not be opened; the line above the button already explains what to do
        }
    }
}
