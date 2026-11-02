// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Per-app output routing through the undocumented <c>IAudioPolicyConfigFactory</c> (the same call Settings > Sound > Volume mixer makes,
/// and the technique EarTrumpet uses). Only the interface layout of Windows 11 21H2 - 24H2 (builds 21390 - 26200) is implemented; on any other build
/// <see cref="IsSupported"/> is false and the row picker is hidden.
/// </summary>
internal static class AppRouting
{
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d4}";
    private const string Prefix = @"\\?\SWD#MMDEVAPI#";
    private static readonly Guid FactoryIid = new("ab3d4648-e242-459f-b02f-541c70306324");

    private delegate int SetFn(nint self, uint pid, int flow, int role, nint hstring);

    private delegate int GetFn(nint self, uint pid, int flow, int role, out nint hstring);

    /// <summary>HRESULTs of the last <see cref="Set"/> call (diagnostics).</summary>
    internal static string LastResult { get; private set; } = string.Empty;

    private static readonly object Gate = new();
    private static nint _factory;
    private static SetFn? _set;
    private static GetFn? _get;
    private static bool _initialised;
    private static bool _supported;
    private static int _slot = 25;

    /// <summary>Diagnostic: re-initialises with another vtable slot for SetPersistedDefaultAudioEndpoint.</summary>
    internal static void Configure(int slot)
    {
        lock (Gate)
        {
            _slot = slot;
            _initialised = false;
            _supported = false;
        }
    }

    /// <summary>True when per-app routing can be used on this machine.</summary>
    public static bool IsSupported
    {
        get
        {
            Init();
            return _supported;
        }
    }

    /// <summary>The device id the app's render streams are pinned to (null = follows the default device).</summary>
    public static string? Get(uint processId)
    {
        Init();
        if (!_supported || _get is null)
        {
            return null;
        }

        lock (Gate)
        {
            try
            {
                foreach (var role in new[] { 1, 0 })
                {
                    if (_get(_factory, processId, 0, role, out var h) == 0 && h != 0)
                    {
                        var text = FromHString(h);
                        WindowsDeleteString(h);
                        if (!string.IsNullOrEmpty(text))
                        {
                            return ToEndpointId(text);
                        }
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Pins the app's render streams to <paramref name="endpointId"/> (null clears the choice). Streams already open follow after they restart.</summary>
    public static bool Set(uint processId, string? endpointId) => SetRaw(processId, endpointId is null ? null : Prefix + endpointId + RenderSuffix);

    /// <summary>Diagnostic: sets the persisted endpoint with an arbitrary device string.</summary>
    internal static bool SetRaw(uint processId, string? deviceString)
    {
        Init();
        if (!_supported || _set is null)
        {
            return false;
        }

        lock (Gate)
        {
            var h = (nint)0;
            try
            {
                if (!string.IsNullOrEmpty(deviceString))
                {
                    var text = deviceString;
                    if (WindowsCreateString(text, text.Length, out h) != 0)
                    {
                        return false;
                    }
                }

                var ok = true;
                var results = new List<string>();
                foreach (var role in new[] { 0, 1, 2 })
                {
                    var hr = _set(_factory, processId, 0, role, h);
                    results.Add($"role{role}=0x{hr:X8}");
                    // Console and multimedia decide where the app's media streams go; the communications role is best effort.
                    ok &= hr >= 0 || role == 2;
                }

                LastResult = string.Join(' ', results);
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (h != 0)
                {
                    WindowsDeleteString(h);
                }
            }
        }
    }

    private static void Init()
    {
        lock (Gate)
        {
            if (_initialised)
            {
                return;
            }

            _initialised = true;
            try
            {
                // The vtable layout is verified for Windows 11 21H2 .. 24H2 (builds 21390-26200). Newer pre-release builds changed it
                // (calls crash or fail), so routing is switched off there instead of risking a fault.
                var build = Environment.OSVersion.Version.Build;
                if (build < 21390 || build > 26200)
                {
                    return;
                }

                const string className = "Windows.Media.Internal.AudioPolicyConfig";
                if (WindowsCreateString(className, className.Length, out var cls) != 0)
                {
                    return;
                }

                var iid = FactoryIid;
                var hr = RoGetActivationFactory(cls, ref iid, out _factory);
                WindowsDeleteString(cls);
                if (hr != 0 || _factory == 0)
                {
                    return;
                }

                var vtable = Marshal.ReadIntPtr(_factory);
                // IInspectable occupies slots 0-5, then 19 methods precede SetPersistedDefaultAudioEndpoint (slot 25) / GetPersisted (slot 26).
                _set = Marshal.GetDelegateForFunctionPointer<SetFn>(Marshal.ReadIntPtr(vtable, _slot * nint.Size));
                _get = Marshal.GetDelegateForFunctionPointer<GetFn>(Marshal.ReadIntPtr(vtable, (_slot + 1) * nint.Size));

                _supported = true;
            }
            catch (Exception)
            {
                _supported = false;
            }
        }
    }

    private static string ToEndpointId(string full)
    {
        var text = full;
        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[Prefix.Length..];
        }

        var hash = text.LastIndexOf("#{", StringComparison.Ordinal);
        if (hash > 0)
        {
            text = text[..hash];
        }

        return text;
    }

    private static string FromHString(nint h)
    {
        var buffer = WindowsGetStringRawBuffer(h, out var length);
        return buffer == 0 ? string.Empty : Marshal.PtrToStringUni(buffer, (int)length) ?? string.Empty;
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern nint WindowsGetStringRawBuffer(nint hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);
}
