// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Select a region of the screen and copy the text in it using the Windows OCR engine.</summary>
public sealed class ScreenOcrFeature : ToolFeatureBase
{
    private bool _busy;

    public override string Id => "screen-ocr";

    public override string Name => "Copy text from screen";

    public override string Description => "Draw a box around any text on your screen and copy it, even from images and videos.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("capture", "Copy text from screen", () => Safe(() => _ = RunAsync()), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", Hotkey.Parse("Ctrl+Alt+Shift+T"), () => _ = RunAsync());

    protected override void OnStop()
    {
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var languages = OcrEngine.AvailableRecognizerLanguages.Select(l => l.DisplayName).ToList();
        return new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Copy text shortcut", Hotkey.Parse("Ctrl+Alt+Shift+T"), "Drag a box over the text; the recognised text is copied to the clipboard.")
            .AddToggle("keepLineBreaks", "Keep line breaks", true, "Turn off to join the recognised lines into a single paragraph.")
            .AddNote(languages.Count == 0
                ? "No OCR language is installed. Add a language with its optional OCR feature in Windows Settings > Time & language."
                : "Recognises text in your Windows languages: " + string.Join(", ", languages) + ".")
            .Build();
    }

    private async Task RunAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                Ctx.Notifier.Show(Name, "No OCR language pack is installed for your Windows languages.", NotificationLevel.Warning);
                return;
            }

            var frame = ScreenGrab.CaptureVirtualScreen();
            var selector = new RegionSelectorWindow(frame, "Drag over the text you want to copy - Esc to cancel");
            var rect = await selector.PickAsync();
            if (rect is not { } region || !IsRunning)
            {
                return;
            }

            var image = frame.Crop(region);
            var text = await RecognizeAsync(engine, image);
            if (string.IsNullOrWhiteSpace(text))
            {
                Ctx.Notifier.Show(Name, "No text was found in that area.", NotificationLevel.Info);
                return;
            }

            if (!Ctx.Settings.Get("keepLineBreaks", true))
            {
                text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            Clipboard.SetText(text);
            var preview = text.Length > 80 ? text[..80] + "..." : text;
            Ctx.Notifier.Show(Name, $"Copied {text.Length} characters: {preview.Replace('\n', ' ')}");
        }
        catch (Exception ex)
        {
            Ctx.Log.Error("OCR failed", ex);
            Ctx.Notifier.Show(Name, "Text recognition failed: " + ex.Message, NotificationLevel.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, BitmapSource image)
    {
        // The engine reads small text poorly; scale up tiny selections.
        BitmapSource source = image;
        var factor = image.PixelHeight < 60 ? 3 : image.PixelHeight < 140 ? 2 : 1;
        if (factor > 1)
        {
            var scaled = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(factor, factor));
            scaled.Freeze();
            source = scaled;
        }

        var max = (int)OcrEngine.MaxImageDimension;
        if (source.PixelWidth > max || source.PixelHeight > max)
        {
            var ratio = Math.Min((double)max / source.PixelWidth, (double)max / source.PixelHeight);
            source = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(ratio, ratio));
        }

        using var stream = new MemoryStream(BitmapUtil.ToPng(source));
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
}
