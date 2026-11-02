// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Captures a region, a window or the whole desktop, then annotates, copies or saves it.</summary>
public sealed class ScreenshotFeature : ToolFeatureBase
{
    private readonly List<ScreenshotEditorWindow> _editors = new();
    private bool _busy;

    public override string Id => "screenshot";

    public override string Name => "Screenshot";

    public override string Description => "Capture a region, window or screen, mark it up with arrows, boxes, text and blur, then copy or save it.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("region", "Screenshot: region or window", () => Safe(() => _ = CaptureRegionAsync()), ""),
        new FeatureAction("screen", "Screenshot: all screens", () => Safe(CaptureScreen), ""),
        new FeatureAction("active-window", "Screenshot: active window", () => Safe(CaptureActiveWindow), ""),
    ];

    protected override void OnStart()
    {
        BindHotkey("hotkeyRegion", Hotkey.Parse("Ctrl+Alt+S"), () => _ = CaptureRegionAsync());
        BindHotkey("hotkeyScreen", Hotkey.Parse("Ctrl+Alt+Shift+S"), CaptureScreen);
        BindHotkey("hotkeyWindow", null, CaptureActiveWindow);
    }

    protected override void OnStop()
    {
        foreach (var editor in _editors.ToList())
        {
            editor.Close();
        }

        _editors.Clear();
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        return new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkeyRegion", "Capture region or window", Hotkey.Parse("Ctrl+Alt+S"), "Drag for a region, or click a window. Esc cancels.")
            .AddHotkey("hotkeyScreen", "Capture all screens", Hotkey.Parse("Ctrl+Alt+Shift+S"))
            .AddHotkey("hotkeyWindow", "Capture the active window", null)
            .AddToggle("openEditor", "Open the editor after capturing", true, "Annotate with arrows, boxes, pen, text, blur and crop.")
            .AddToggle("copyOnCapture", "Copy to the clipboard right away", false)
            .AddToggle("saveOnCapture", "Save to the folder right away", false)
            .AddText("folder", "Save folder", BitmapUtil.ScreenshotsFolder(), "PNG files named WinKnife-date-time.png.")
            .Build();
    }

    internal string SaveFolder()
    {
        var folder = Ctx.Settings.Get("folder", BitmapUtil.ScreenshotsFolder());
        return string.IsNullOrWhiteSpace(folder) ? BitmapUtil.ScreenshotsFolder() : Environment.ExpandEnvironmentVariables(folder);
    }

    private async Task CaptureRegionAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var windows = ScreenGrab.TopLevelWindows();
            var frame = ScreenGrab.CaptureVirtualScreen();
            var selector = new RegionSelectorWindow(frame, "Drag to capture a region, or click a window - Esc to cancel", windows);
            var rect = await selector.PickAsync();
            if (rect is { } r && IsRunning)
            {
                Deliver(frame.Crop(r));
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void CaptureScreen()
    {
        var frame = ScreenGrab.CaptureVirtualScreen();
        Deliver(frame.Bitmap);
    }

    private void CaptureActiveWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return;
        }

        var bounds = ScreenGrab.TopLevelWindows().FirstOrDefault(w => w.Handle == hwnd).Bounds;
        if (bounds.Width == 0)
        {
            Ctx.Notifier.Show(Name, "The active window cannot be captured.", NotificationLevel.Warning);
            return;
        }

        var virt = ScreenGrab.VirtualScreen();
        var x1 = Math.Max(bounds.X, virt.X);
        var y1 = Math.Max(bounds.Y, virt.Y);
        var x2 = Math.Min(bounds.X + bounds.Width, virt.X + virt.Width);
        var y2 = Math.Min(bounds.Y + bounds.Height, virt.Y + virt.Height);
        if (x2 <= x1 || y2 <= y1)
        {
            return;
        }

        Deliver(ScreenGrab.CaptureRect(new Int32Rect(x1, y1, x2 - x1, y2 - y1)));
    }

    private void Deliver(BitmapSource image)
    {
        var s = Ctx.Settings;
        var openEditor = s.Get("openEditor", true);
        var copy = s.Get("copyOnCapture", false);
        var save = s.Get("saveOnCapture", false);
        if (!openEditor && !copy && !save)
        {
            copy = true; // never drop a capture on the floor
        }

        var messages = new List<string>();
        if (copy)
        {
            BitmapUtil.CopyToClipboard(image);
            messages.Add("copied to the clipboard");
        }

        if (save)
        {
            var path = SaveToFolder(image);
            messages.Add("saved to " + path);
        }

        if (openEditor)
        {
            var editor = new ScreenshotEditorWindow(image, this);
            _editors.Add(editor);
            editor.Closed += (_, _) => _editors.Remove(editor);
            editor.Show();
            editor.Activate();
        }
        else
        {
            Ctx.Notifier.Show(Name, "Screenshot " + string.Join(" and ", messages));
        }
    }

    internal string SaveToFolder(BitmapSource image)
    {
        var path = Path.Combine(SaveFolder(), $"WinKnife-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        BitmapUtil.SavePng(image, path);
        return path;
    }

    internal void Tell(string message, NotificationLevel level = NotificationLevel.Info) => Ctx.Notifier.Show(Name, message, level);
}
