// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Microsoft.Win32;
using Trayloom.Core;
using Windows.Media.Control;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Why Away mode stays out of the way right now.</summary>
internal enum AwaySuppression
{
    None,
    Media,
    Calls,
    FullScreen,
    Recording,
    Remote,
    Configuring,
}

/// <summary>
/// The "not away while..." checks (spec 06 section 4.3): full screen or presenting, a media session playing, any app using the
/// microphone or a camera, Trayloom recording the screen, a remote session, or the Away mode settings card being used.
/// </summary>
internal sealed class AwayPresence
{
    private const string ConsentRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    private readonly IContributionServices _services;
    private GlobalSystemMediaTransportControlsSessionManager? _media;
    private bool _mediaRequested;

    public AwayPresence(IContributionServices services)
    {
        _services = services;
    }

    /// <summary>Set by the settings card while it is loaded and its window is active.</summary>
    public static Func<bool>? SettingsCardInUse { get; set; }

    /// <summary>Asks Windows for the media session manager once (the playback status is then read synchronously).</summary>
    public async Task InitializeAsync()
    {
        if (_mediaRequested)
        {
            return;
        }

        _mediaRequested = true;
        try
        {
            _media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        }
        catch (Exception ex)
        {
            _services.Log.Info($"[away] media sessions unavailable: {ex.GetType().Name} 0x{ex.HResult:X8}");
        }
    }

    /// <summary>
    /// The first reason that applies, or <see cref="AwaySuppression.None"/>. Call on the UI thread. While Trayloom's own presence watch
    /// has the camera open, <paramref name="ownCameraOpen"/> keeps Trayloom's own use from looking like a call.
    /// </summary>
    public AwaySuppression Current(bool ownCameraOpen = false)
    {
        var app = _services.App;
        if (System.Windows.Forms.SystemInformation.TerminalServerSession)
        {
            return AwaySuppression.Remote;
        }

        if (SettingsCardInUse?.Invoke() == true)
        {
            return AwaySuppression.Configuring;
        }

        if (app.Get<bool>("awayModeSkipFullScreen") && AwayNative.IsFullScreenOrPresenting())
        {
            return AwaySuppression.FullScreen;
        }

        if (_services.Services.Get<IRecordingState>()?.IsRecording == true)
        {
            return AwaySuppression.Recording;
        }

        if (app.Get<bool>("awayModeSkipMedia") && MediaPlaying())
        {
            return AwaySuppression.Media;
        }

        if (app.Get<bool>("awayModeSkipCalls") && (InUse("microphone") || InUse("webcam", ownCameraOpen)))
        {
            return AwaySuppression.Calls;
        }

        return AwaySuppression.None;
    }

    /// <summary>
    /// True while an app other than Trayloom uses a camera (the face watcher does not start the camera then). Trayloom itself is
    /// skipped because the consent store lists it while its own watch is running.
    /// </summary>
    public bool OtherAppUsesCamera() => InUse("webcam", skipSelf: true);

    /// <summary>True while battery saver is on: the camera watch stays off to save power.</summary>
    public static bool EnergySaverOn()
    {
        try
        {
            return Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The settings-card text of a reason.</summary>
    public static string Describe(AwaySuppression reason) => reason switch
    {
        AwaySuppression.Media => AwayStrings.WaitMedia,
        AwaySuppression.Calls => AwayStrings.WaitCalls,
        AwaySuppression.FullScreen => AwayStrings.WaitFullScreen,
        AwaySuppression.Recording => AwayStrings.WaitRecording,
        _ => string.Empty,
    };

    private bool MediaPlaying()
    {
        try
        {
            var sessions = _media?.GetSessions();
            if (sessions is null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // a session that vanished while it was read is not playing
        }

        return false;
    }

    /// <summary>
    /// Reads the consent store: packaged apps are sub keys, desktop apps sit under <c>NonPackaged</c>; an app is using the device while
    /// <c>LastUsedTimeStop</c> is 0 or older than <c>LastUsedTimeStart</c>.
    /// </summary>
    private static bool InUse(string capability, bool skipSelf = false)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentRoot + capability);
            if (root is null)
            {
                return false;
            }

            return AnyInUse(root, nested: true, skipSelf ? SelfKeyName() : null);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The name the consent store gives a desktop app: its exe path with every backslash replaced by a hash sign.</summary>
    private static string? SelfKeyName() => Environment.ProcessPath?.Replace('\\', '#');

    private static bool AnyInUse(RegistryKey key, bool nested, string? skipName)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is null)
            {
                continue;
            }

            if (!nested && skipName is not null && string.Equals(name, skipName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (nested && name == "NonPackaged")
            {
                if (AnyInUse(child, nested: false, skipName))
                {
                    return true;
                }

                continue;
            }

            if (child.GetValue("LastUsedTimeStart") is long start && start > 0 && child.GetValue("LastUsedTimeStop") is long stop && (stop == 0 || stop < start))
            {
                return true;
            }
        }

        return false;
    }
}
