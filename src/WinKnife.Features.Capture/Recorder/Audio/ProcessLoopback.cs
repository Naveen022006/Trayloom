// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace WinKnife.Features.Capture;

/// <summary>Which process tree a process-loopback capture follows: only that app (a window recording) or everything but it.</summary>
internal readonly record struct ProcessLoopbackTarget(int ProcessId, bool IncludeTree);

/// <summary>
/// Process loopback (Windows 10 2004+ / Windows 11): an <c>IAudioClient</c> activated on the virtual device <c>VAD\Process_Loopback</c>
/// through <c>ActivateAudioInterfaceAsync</c> with AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK. The window recording follows the
/// picture (only that app and its child processes are heard); a whole-screen recording can exclude WinKnife's own sound.
/// </summary>
internal static class ProcessLoopbackActivator
{
    private const string VirtualDevice = "VAD\\Process_Loopback";
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, nint activationParams, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation operation);

    [ComVisible(true)]
    private sealed class Handler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public int Result = unchecked((int)0x80004005);
        public nint ClientPtr;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                var hr = operation.GetActivateResult(out var activate, out var iface);
                Result = hr < 0 ? hr : activate;
                // keep the raw pointer: the object is agile, so the caller's apartment can wrap it directly (no cross-apartment cast)
                ClientPtr = iface is null ? 0 : Marshal.GetIUnknownForObject(iface);
            }
            catch (Exception ex)
            {
                Result = ex.HResult;
            }

            Done.Set();
            return 0;
        }
    }

    /// <summary>True on builds that can host process loopback (Windows 10 2004, build 19041, and later).</summary>
    public static bool IsAvailable => Environment.OSVersion.Version.Build >= 19041;

    /// <summary>Activates the loopback client; returns the IAudioClient RCW. Throws on failure (callers fall back to endpoint loopback).</summary>
    public static object Activate(ProcessLoopbackTarget target)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { DWORD type = PROCESS_LOOPBACK (1); DWORD pid; DWORD mode (0 include tree, 1 exclude tree) }
        var paramsPtr = Marshal.AllocHGlobal(12);
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(paramsPtr, 0, 1);
            Marshal.WriteInt32(paramsPtr, 4, target.ProcessId);
            Marshal.WriteInt32(paramsPtr, 8, target.IncludeTree ? 0 : 1);
            for (var i = 0; i < 24; i += 8)
            {
                Marshal.WriteInt64(variant, i, 0);
            }

            Marshal.WriteInt16(variant, 0, 65); // VT_BLOB
            Marshal.WriteInt32(variant, 8, 12); // cbSize
            Marshal.WriteIntPtr(variant, 16, paramsPtr);
            var handler = new Handler();
            var iid = IidAudioClient;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(VirtualDevice, ref iid, variant, handler, out var op));
            if (!handler.Done.Wait(5000))
            {
                throw new TimeoutException("Process loopback activation timed out");
            }

            GC.KeepAlive(op);
            Marshal.ThrowExceptionForHR(handler.Result);
            if (handler.ClientPtr == 0)
            {
                throw new InvalidOperationException("Process loopback returned no client");
            }

            var client = Marshal.GetObjectForIUnknown(handler.ClientPtr);
            Marshal.Release(handler.ClientPtr);
            return client;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }
}
