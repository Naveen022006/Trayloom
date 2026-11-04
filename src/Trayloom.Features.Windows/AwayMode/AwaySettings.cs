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

    public IReadOnlyList<string> Keywords => ["privacy", "idle", "face", "camera", "minimize", "lock", "presence"];

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
        card.AddCaption(faceOn && !lockOn ? AwayStrings.NowCaptionFace : AwayStrings.NowCaption);

        Wire(card, controller, status, now);
        return card;
    }

    /// <summary>The card reports "in use" to the idle check while it is loaded in an active window (the person is configuring).</summary>
    private static void Wire(SettingsCard card, AwayController? controller, TextBlock status, Button now)
    {
        void Refresh()
        {
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

        if (controller.IsAway)
        {
            return AwayStrings.AwaySince(controller.Since.ToString("t"), controller.WindowCount);
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
