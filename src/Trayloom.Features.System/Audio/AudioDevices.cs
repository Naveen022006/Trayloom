// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Trayloom.Features.SystemTools;

/// <summary>A persisted reference to an audio endpoint (id plus the last known name, for display while it is unplugged).</summary>
public sealed class DevicePref
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>An active audio endpoint (<see cref="FormFactor"/> is the Windows EndpointFormFactor: 3 headphones, 5 headset...).</summary>
internal sealed record AudioEndpoint(string Id, string Name, int FormFactor = 10)
{
    /// <summary>Headphones or headset (wired or Bluetooth) - the endpoint the "lower volume on disconnect" guard cares about.</summary>
    public bool IsHeadphones => FormFactor is 3 or 5;
}

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
                    result.Add(new AudioEndpoint(device.ID, SafeName(device), FormFactorOf(device)));
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

    /// <summary>Reads the endpoint form factor (10 = unknown).</summary>
    private static int FormFactorOf(MMDevice device)
    {
        try
        {
            var value = device.Properties[new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0)].Value;
            return value is uint u ? (int)u : 10;
        }
        catch (Exception)
        {
            return 10;
        }
    }

    /// <summary>The endpoint's form factor by id (10 = unknown / missing).</summary>
    public static int FormFactor(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            return FormFactorOf(device);
        }
        catch (Exception)
        {
            return 10;
        }
    }

    /// <summary>Makes the endpoint the default for the given roles only (0 console, 1 multimedia, 2 communications).</summary>
    public static bool SetDefaultFor(string id, params int[] roles)
    {
        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            var ok = true;
            foreach (var role in roles)
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

    /// <summary>Master volume of the endpoint (0..1) and mute state; null when it cannot be read.</summary>
    public static (float Volume, bool Muted)? ReadVolume(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            var v = device.AudioEndpointVolume;
            return (v.MasterVolumeLevelScalar, v.Mute);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Sets the endpoint master volume (0..1).</summary>
    public static bool WriteVolume(string id, float scalar)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f);
            return true;
        }
        catch (Exception)
        {
            return false;
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
