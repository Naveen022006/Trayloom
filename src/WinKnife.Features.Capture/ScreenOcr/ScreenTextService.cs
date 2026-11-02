// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Media.Imaging;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Copy text from screen: QR wins over text, a stale-clipboard guard, "Text copied" / "No text found" HUDs.</summary>
internal sealed class ScreenTextService
{
    private readonly IFeatureContext _ctx;
    private int _generation;

    public ScreenTextService(IFeatureContext ctx)
    {
        _ctx = ctx;
    }

    public void Capture(bool fromShortcut = false) => ScreenCapture.Start(CaptureTool.Text, fromShortcut);

    public void Recognize(BitmapSource image, bool detectQr) => _ = RecognizeAsyncCore(image, detectQr, _ctx.App.Get<bool>(K.OcrRemoveBreaks));

    /// <summary>Entry used by the screenshot preview and Clipboard history: no QR, honours the line-break setting.</summary>
    public Task RecognizeToClipboardAsync(BitmapSource image, bool qr) => RecognizeAsyncCore(image, qr, _ctx.App.Get<bool>(K.OcrRemoveBreaks));

    private async Task RecognizeAsyncCore(BitmapSource image, bool detectQr, bool removeBreaks)
    {
        var gen = ++_generation;
        var clipboardMark = N.GetClipboardSequenceNumber();
        try
        {
            if (detectQr)
            {
                var qr = await Task.Run(() => OcrSupport.ReadQr(image));
                if (qr is not null)
                {
                    if (gen == _generation && _ctx.Runtime.IsAvailable("screen-ocr"))
                    {
                        QrResultWindow.Show(qr, null);
                    }

                    return;
                }
            }

            var lines = await OcrSupport.RecognizeAsync(image);
            if (gen != _generation || !_ctx.Runtime.IsAvailable("screen-ocr"))
            {
                return;
            }

            if (lines is null)
            {
                QuickToolHud.Show(G.TextScan, Strings.Ocr.NoLanguage);
                return;
            }

            var text = OcrSupport.JoinLines(lines, image.PixelHeight, image.PixelWidth, removeBreaks);
            if (string.IsNullOrWhiteSpace(text))
            {
                QuickToolHud.Show(G.TextScan, Strings.Ocr.NoText);
                return;
            }

            // a later copy by the user is never overwritten by an earlier recognition
            if (N.GetClipboardSequenceNumber() != clipboardMark)
            {
                return;
            }

            if (Output.CopyText(text))
            {
                QuickToolHud.Show(G.TextScan, Strings.Ocr.Copied);
            }
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("text recognition failed", ex);
            QuickToolHud.Show(G.TextScan, Strings.CaptureFailed);
        }
    }

    public void Cancel() => _generation++;
}
