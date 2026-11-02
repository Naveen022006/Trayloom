// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace WinKnife.Features.Capture;

/// <summary>
/// Hides the yellow capture border where Windows allows it: the access request (GraphicsCaptureAccess.RequestAccessAsync with
/// Borderless) followed by IGraphicsCaptureSession3.IsBorderRequired = false (Windows 11 and Windows 10 2104+). When Windows refuses
/// (policy, older build) the border stays: it is drawn by the OS outside the captured pixels, so the recording itself is unaffected.
/// </summary>
internal static class CaptureBorder
{
    private static readonly Guid IidAccessStatics = new("743ed370-06ec-5040-a58a-901f0f757095");
    private static readonly Guid IidSession3 = new("f2cdd966-22ae-5ea1-9596-3a289344c3be");

    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint hstring);

    /// <summary>What happened on the last attempt (for the log and for tests): "hidden", "access denied: ...", "unsupported" ...</summary>
    public static string LastResult { get; private set; } = "not tried";

    /// <summary>True when the border was successfully switched off for this session.</summary>
    public static bool TrySuppress(GraphicsCaptureSession session)
    {
        try
        {
            var access = Task.Run(RequestBorderlessAccess).GetAwaiter().GetResult(); // off the UI thread: the request is a WinRT async call
            var ok = SetBorderRequired(session, false, out var hr);
            LastResult = ok ? $"hidden (access: {access})" : $"IsBorderRequired refused: 0x{hr:X8} (access: {access})";
            return ok;
        }
        catch (Exception ex)
        {
            LastResult = "unsupported: " + ex.Message;
            return false;
        }
    }

    private static string RequestBorderlessAccess()
    {
        const string cls = "Windows.Graphics.Capture.GraphicsCaptureAccess";
        WindowsCreateString(cls, cls.Length, out var hs);
        var iid = IidAccessStatics;
        var hr = RoGetActivationFactory(hs, ref iid, out var factory);
        WindowsDeleteString(hs);
        if (hr < 0)
        {
            return $"factory 0x{hr:X8}";
        }

        try
        {
            unsafe
            {
                var vtbl = *(nint**)factory;
                var request = (delegate* unmanaged[Stdcall]<nint, int, out nint, int>)vtbl[6];
                hr = request(factory, 0 /* Borderless */, out var op);
                if (hr < 0 || op == 0)
                {
                    return $"request 0x{hr:X8}";
                }

                var managed = WinRT.MarshalInterface<IAsyncOperation<AppCapabilityAccessStatus>>.FromAbi(op);
                var task = managed.AsTask();
                return task.Wait(3000) ? task.Result.ToString() : "timeout";
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    private static bool SetBorderRequired(GraphicsCaptureSession session, bool required, out int hr)
    {
        var abi = WinRT.MarshalInspectable<GraphicsCaptureSession>.FromManaged(session);
        try
        {
            var iid = IidSession3;
            hr = Marshal.QueryInterface(abi, in iid, out var s3);
            if (hr < 0)
            {
                return false;
            }

            try
            {
                unsafe
                {
                    var vtbl = *(nint**)s3;
                    var put = (delegate* unmanaged[Stdcall]<nint, byte, int>)vtbl[7];
                    hr = put(s3, required ? (byte)1 : (byte)0);
                    return hr >= 0;
                }
            }
            finally
            {
                Marshal.Release(s3);
            }
        }
        finally
        {
            Marshal.Release(abi);
        }
    }
}
