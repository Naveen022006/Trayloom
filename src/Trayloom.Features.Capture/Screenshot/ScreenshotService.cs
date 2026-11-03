// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Screenshot pipeline: entry points, post-capture routing (copy / save / preview / editor), latest-capture store, pins.</summary>
internal sealed class ScreenshotService
{
    private readonly IFeatureContext _ctx;
    private readonly List<ScreenshotEditorWindow> _editors = new();
    private QuickPreviewWindow? _preview;
    private ScrollingCaptureSession? _scrolling;
    private int _latestToken;
    private bool _busyDirect;

    public ScreenshotService(IFeatureContext ctx)
    {
        _ctx = ctx;
    }

    public IAppSettings App => _ctx.App;

    private static string CacheDir => Path.Combine(Output.DataRoot(), "Cache");

    private static string LatestPath => Path.Combine(CacheDir, "LatestScreenshot.png");

    // ---- entry points --------------------------------------------------------------------------------------

    public void CaptureNow(bool fromShortcut = false)
    {
        if (_scrolling is not null)
        {
            _scrolling.Finish();
            return;
        }

        ScreenCapture.Start(CaptureTool.Screenshot, fromShortcut);
    }

    public void CaptureScrolling()
    {
        if (_scrolling is not null)
        {
            _scrolling.Finish();
            return;
        }

        ScreenCapture.Start(CaptureTool.Screenshot, false, scrollingOnly: true);
    }

    /// <summary>Captures the display under the pointer without showing the overlay (hotkey).</summary>
    public void CaptureFullScreen()
    {
        if (_busyDirect || CaptureSession.IsActive || CaptureHost.SessionOnScreen)
        {
            ScreenCapture.CancelCountdown();
            return;
        }

        var delay = Math.Max(0, App.Get<int>(K.Delay));
        void Grab()
        {
            _busyDirect = true;
            try
            {
                var mon = ScreenGrab.MonitorUnderCursor();
                var image = ScreenGrab.CaptureRect(mon.Bounds, App.Get<bool>(K.IncludePointer));
                Route(new Capture(image, mon.Scale, mon.Bounds));
            }
            catch (Exception ex)
            {
                CaptureHost.Log("full screen capture failed", ex);
                QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
            }
            finally
            {
                _busyDirect = false;
            }
        }

        if (delay > 0)
        {
            ScreenCapture.RunCountdown(delay, Grab);
        }
        else
        {
            Grab();
        }
    }

    public void EditLatest()
    {
        if (File.Exists(LatestPath) && Output.LoadImage(LatestPath) is { } image)
        {
            OpenEditor(new Capture(image, Output.ScaleOf(image), new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)), false);
            return;
        }

        QuickToolHud.Show(G.Camera, Strings.Screenshot.TakeFirst);
    }

    public void EditClipboard()
    {
        BitmapSource? image = null;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count == 1 && files[0] is { } f && Output.LoadImage(f) is { } fromFile)
                {
                    image = fromFile;
                }
            }

            if (image is null && Clipboard.ContainsImage())
            {
                image = Clipboard.GetImage();
            }
        }
        catch
        {
            // clipboard busy
        }

        if (image is null)
        {
            QuickToolHud.Show(G.Camera, Strings.Screenshot.CopyImageFirst);
            return;
        }

        var px = Pixels.Get(image);
        var bmp = Pixels.Make(px, image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY);
        OpenEditor(new Capture(bmp, Output.ScaleOf(bmp), new Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight)), false);
    }

    public void EditFile(string path)
    {
        if (Output.LoadImage(path) is { } image)
        {
            OpenEditor(new Capture(image, Output.ScaleOf(image), new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)), false);
        }
        else
        {
            CaptureHost.Beep();
        }
    }

    // ---- post-capture --------------------------------------------------------------------------------------

    public void Route(Capture capture)
    {
        BeginLatest(capture);
        _preview?.Close();
        CaptureHost.Recent?.RecordScreenshot(capture);
        var action = App.Get<string>(K.DefaultAction) ?? string.Empty;
        var copiesByAction = action is "copy" or "saveAndCopy";
        var autoCopy = App.Get<bool>(K.CopyToClipboard);
        var (outImage, outScale) = Output.ApplyDownscale(App, capture.Image, capture.Scale);
        var imageOnly = App.Get<bool>(K.CopyImageOnly);

        if (action == "edit")
        {
            if (autoCopy && !Output.CopyImage(outImage, outScale, imageOnly))
            {
                CaptureHost.Beep();
            }

            ShelfAdd(capture, null);
            OpenEditor(capture, false);
            return;
        }

        string? savedPath = null;
        var copied = false;
        var saveFailed = false;
        var copyFailed = false;
        if (action is "save" or "saveAndCopy")
        {
            savedPath = SaveCapture(capture);
            saveFailed = savedPath is null;
        }

        if (copiesByAction || autoCopy)
        {
            copied = Output.CopyImage(outImage, outScale, imageOnly);
            copyFailed = !copied;
        }

        ShelfAdd(capture, savedPath);

        if (action.Length > 0)
        {
            var folder = savedPath is null ? null : Path.GetFileName(Path.GetDirectoryName(savedPath)) ?? Strings.Desktop;
            if (action == "copy" && copied)
            {
                QuickToolHud.Show(G.Camera, Strings.Screenshot.Copied);
            }
            else if (savedPath is not null && copied)
            {
                QuickToolHud.Show(G.Camera, string.Format(Strings.Screenshot.SavedAndCopiedFormat, folder));
            }
            else if (savedPath is not null)
            {
                QuickToolHud.Show(G.Camera, string.Format(Strings.Screenshot.SavedFormat, folder));
            }
            else
            {
                QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
                CaptureHost.Beep();
            }
        }

        var success = action.Length > 0 && !saveFailed && !(copiesByAction && copyFailed);
        if (action.Length == 0)
        {
            ShowPreview(capture, savedPath, copied, 12, recovery: true);
        }
        else if (success)
        {
            if (App.Get<bool>(K.PreviewEnabled))
            {
                ShowPreview(capture, savedPath, copied, ValidDuration(App.Get<int>(K.PreviewDuration)), recovery: false);
            }
        }
        else
        {
            ShowPreview(capture, savedPath, copied, 12, recovery: true);
        }
    }

    /// <summary>"Add to the shelf automatically": hands the capture (the saved file, or a cached PNG) to the shelf while it is on.</summary>
    private void ShelfAdd(Capture capture, string? savedPath)
    {
        if (!App.Get<bool>(K.AddToShelf) || !App.Get<bool>("shelfEnabled") || CaptureHost.Services.Services.Get<IShelfService>() is not { } shelf)
        {
            return;
        }

        var path = savedPath;
        if (path is null)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                path = Path.Combine(CacheDir, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                File.WriteAllBytes(path, Output.ToPng(capture.Image, capture.Scale));
            }
            catch (Exception ex)
            {
                CaptureHost.Log("adding the capture to the shelf failed", ex);
                return;
            }
        }

        shelf.Add([path]);
    }

    private static int ValidDuration(int v) => v is 1 or 2 or 3 or 5 or 10 or 0 ? v : 3;

    public string? SaveCapture(Capture capture)
    {
        try
        {
            var (path, _, _) = Output.NextScreenshotPath(App, DateTime.Now);
            var (img, scale) = Output.ApplyDownscale(App, capture.Image, capture.Scale);
            Output.SavePng(img, path, scale);
            return path;
        }
        catch (Exception ex)
        {
            CaptureHost.Log("saving screenshot failed", ex);
            return null;
        }
    }

    private void ShowPreview(Capture capture, string? savedPath, bool copied, int seconds, bool recovery)
    {
        _preview?.Close();
        var preview = new QuickPreviewWindow(this, capture, savedPath, copied, seconds, afterAction: !recovery || savedPath is not null || copied);
        _preview = preview;
        preview.Closed += (_, _) =>
        {
            if (ReferenceEquals(_preview, preview))
            {
                _preview = null;
            }
        };
        preview.Present();
    }

    public void RestorePreview(Capture capture)
    {
        ShowPreview(capture, null, false, 12, recovery: true);
    }

    // ---- latest capture ---------------------------------------------------------------------------------------

    private void BeginLatest(Capture capture)
    {
        _latestToken++;
        if (!App.Get<bool>(K.LastCaptureShortcutEnabled) && !App.Get<bool>(K.UploadShortcutEnabled))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(LatestPath, Output.ToPng(capture.Image, capture.Scale));
            File.Delete(LatestPath + ".withheld");
        }
        catch
        {
            // best effort
        }
    }

    public void WithholdLatest()
    {
        try
        {
            File.WriteAllText(LatestPath + ".withheld", "1");
        }
        catch
        {
            // best effort
        }
    }

    // ---- editor / pins ----------------------------------------------------------------------------------------

    public void OpenEditor(Capture capture, bool fromHistory)
    {
        WithholdLatest();
        var editor = new ScreenshotEditorWindow(this, capture);
        _editors.Add(editor);
        editor.Closed += (_, _) => _editors.Remove(editor);
        editor.Show();
        editor.Activate();
    }

    public void Pin(BitmapSource image, double scale)
    {
        PinWindow.Create(this, image, scale);
    }

    public bool HideOwnWindows => App.Get<bool>(K.HideOwn);

    /// <summary>Excludes an editor / pin / preview from captures when "Hide Trayloom windows" is on.</summary>
    public void ApplyAffinity(Window window)
    {
        void Apply() => N.ExcludeFromCapture(new WindowInteropHelper(window).Handle, HideOwnWindows);
        if (new WindowInteropHelper(window).Handle != 0)
        {
            Apply();
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply();
        }
    }

    // ---- scrolling ------------------------------------------------------------------------------------------------

    public void StartScrolling(RegionPick pick)
    {
        var session = new ScrollingCaptureSession(pick);
        _scrolling = session;
        CaptureHost.SessionOnScreen = true;
        session.Completed += result =>
        {
            _scrolling = null;
            CaptureHost.SessionOnScreen = false;
            switch (result.Status)
            {
                case ScrollStatus.Cancelled:
                    QuickToolHud.Show(G.Close, Strings.Screenshot.Cancelled);
                    return;
                case ScrollStatus.Failed:
                    QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
                    return;
                case ScrollStatus.Limited:
                    QuickToolHud.Show(G.Camera, Strings.Screenshot.ScrollingLimit);
                    break;
                case ScrollStatus.Partial:
                    QuickToolHud.Show(G.Camera, Strings.Screenshot.ScrollingPartial);
                    break;
            }

            if (result.Image is { } image)
            {
                Route(new Capture(image, pick.Monitor.Scale, pick.Screen));
            }
        };
        session.Start();
    }

    // ---- lifetime -----------------------------------------------------------------------------------------------------

    public void Stop()
    {
        ScreenCapture.StopAll();
        _scrolling?.Cancel();
        _scrolling = null;
        _preview?.Close();
        foreach (var e in _editors.ToList())
        {
            e.ForceClose();
        }

        _editors.Clear();
        PinWindow.CloseAll();
    }
}
