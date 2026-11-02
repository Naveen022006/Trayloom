// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace WinKnife.Features.SystemTools;

/// <summary>A persisted reference to an audio endpoint (id plus the last known name, for display while it is unplugged).</summary>
public sealed class DevicePref
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>An active audio endpoint.</summary>
internal sealed record AudioEndpoint(string Id, string Name);

/// <summary>Standard (undocumented but stable since Vista) COM interface used by the Sound control panel to set default endpoints.</summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out nint format);

    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, out nint format);

    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint endpointFormat, nint mixFormat);

    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, nint defaultPeriod, nint minimumPeriod);

    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint period);

    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, nint key, nint value);

    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, nint key, nint value);

    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

/// <summary>Helpers around Core Audio device enumeration and default-endpoint switching. All methods are safe to call from any thread.</summary>
internal static class AudioDevices
{
    public static List<AudioEndpoint> List(DataFlow flow)
    {
        var result = new List<AudioEndpoint>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    result.Add(new AudioEndpoint(device.ID, SafeName(device)));
                }
            }
        }
        catch (Exception)
        {
            // No audio service / no devices.
        }

        return result;
    }

    public static string? DefaultId(DataFlow flow, Role role = Role.Multimedia)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(flow, role))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string NameOf(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            return SafeName(device);
        }
        catch (Exception)
        {
            return id;
        }
    }

    /// <summary>Makes the endpoint the default for the console, multimedia and communications roles.</summary>
    public static bool SetDefault(string id)
    {
        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            var ok = true;
            for (var role = 0; role < 3; role++)
            {
                ok &= policy.SetDefaultEndpoint(id, role) >= 0;
            }

            return ok;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (policy is not null)
            {
                Marshal.ReleaseComObject(policy);
            }
        }
    }

    private static string SafeName(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch (Exception)
        {
            return device.ID;
        }
    }
}
