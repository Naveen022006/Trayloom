// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>The master volume of the default output through Core Audio. Only the device's master level is touched, never the volume of one app.</summary>
internal static class ModeVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);

        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);

        [PreserveSig] int UnregisterControlChangeNotify(nint notify);

        [PreserveSig] int GetChannelCount(out uint count);

        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);

        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig] int GetMasterVolumeLevel(out float level);

        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IAudioEndpointVolume? DefaultRender()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0)
            {
                return null;
            }

            var iid = typeof(IAudioEndpointVolume).GUID;
            return device.Activate(ref iid, 23, 0, out var obj) == 0 ? obj as IAudioEndpointVolume : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets the master volume (0 to 1). A level of 0 also mutes, and any other level unmutes, because "Mute" in the mode editor means
    /// silence and a mode that asks for 50 % expects to be heard. False when there is no output device or it refused.
    /// </summary>
    public static bool Set(float level)
    {
        var volume = DefaultRender();
        if (volume is null)
        {
            return false;
        }

        var scalar = Math.Clamp(level, 0f, 1f);
        var context = Guid.Empty;
        var ok = volume.SetMasterVolumeLevelScalar(scalar, ref context) == 0;
        context = Guid.Empty;
        return volume.SetMute(scalar <= 0f, ref context) == 0 && ok;
    }
}
