// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>
/// Process-wide access point of the capture tools. The <see cref="IFeature"/> classes attach their context when they start;
/// the (stateless, separately instantiated) settings sections and panel utilities reach the services through it.
/// </summary>
internal static class CaptureHost
{
    private static IContributionServices? _services;

    public static IContributionServices Services => _services ?? throw new InvalidOperationException("Capture tools are not started.");

    public static bool IsAttached => _services is not null;

    public static IAppSettings App => Services.App;

    public static Dispatcher Ui => _services?.Ui ?? System.Windows.Application.Current.Dispatcher;

    public static ScreenshotService? Screenshot { get; set; }

    public static RecentCaptureService Recent => RecentCaptureService.Instance;

    public static RecorderService? Recorder { get; set; }

    public static ScreenTextService? Text { get; set; }

    public static ColorSamplerService? ColorSampler { get; set; }

    public static void Attach(IContributionServices services)
    {
        _services = services;
        K.RegisterDefaults(services.App);
    }

    public static bool Installed(string featureId) => _services?.Runtime.IsAvailable(featureId) == true;

    /// <summary>True while a selection overlay, a countdown, a scrolling capture or another capture is running.</summary>
    public static bool SessionOnScreen { get; set; }

    public static string? ShortcutText(string roleId)
    {
        if (_services is null)
        {
            return null;
        }

        var state = _services.Shortcuts.GetState(roleId);
        return state.IsActive && state.Effective is { } hk ? hk.ToString() : null;
    }

    public static void Log(string message, Exception? ex = null)
    {
        if (_services is null)
        {
            return;
        }

        if (ex is null)
        {
            _services.Log.Info(message);
        }
        else
        {
            _services.Log.Error(message, ex);
        }
    }

    public static void Beep() => System.Media.SystemSounds.Beep.Play();

    public static void OpenImageInEditor(System.Windows.Media.Imaging.BitmapSource image, double scale)
    {
        CaptureHost.Screenshot?.OpenEditor(new Capture(image, scale, new System.Windows.Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)), fromHistory: false);
    }
}
