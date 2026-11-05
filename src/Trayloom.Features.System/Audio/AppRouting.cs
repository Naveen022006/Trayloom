// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>
/// Per-app output routing through the undocumented <c>IAudioPolicyConfigFactory</c> (the same call Settings > Sound > Volume mixer makes,
/// and the technique EarTrumpet uses). The interface layout is the "21H2" variant EarTrumpet still ships; it was re-verified by disassembling
/// AudioSes.dll on build 26300 (slot 25 takes pid, flow, role, HSTRING and forwards to the implementation at +0xC8, slot 26 the same with an
/// out HSTRING at +0xD0, slot 27 takes no argument at +0xD8). Builds older than 21390 use another layout, so <see cref="IsSupported"/> is false
/// there and the row picker is hidden. Because the layout is undocumented, the first use on every OS build runs a self-test (see
/// <see cref="SelfTest"/>): the app routes its own silent test stream to the current default device and reads the choice back; when the
/// read-back differs the route picker stays off for that build and the verdict is remembered.
/// </summary>
internal static class AppRouting
{
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
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
    private static bool _ready;
    private static bool _verdictOk;
    private static Task? _selfTestTask;
    private const int Slot = 25;

    /// <summary>Where self-test results and failures are logged (set by the Volume mixer feature).</summary>
    internal static ILog? Log { get; set; }

    /// <summary>True when per-app routing can be used on this machine.</summary>
    public static bool IsSupported
    {
        get
        {
            Init();
            if (!_ready)
            {
                return false;
            }

            var task = _selfTestTask;
            if (task is not null && !task.IsCompleted)
            {
                // Only the very first use on a build waits (about a quarter of a second); never from the test's own thread.
                try
                {
                    task.Wait(TimeSpan.FromSeconds(4));
                }
                catch (Exception)
                {
                }
            }

            return _verdictOk;
        }
    }

    /// <summary>Starts the build self-test early (does nothing when this build already has a verdict).</summary>
    public static void WarmUp() => Init();

    /// <summary>The device id the app's render streams are pinned to (null = follows the default device).</summary>
    public static string? Get(uint processId) => IsSupported ? GetCore(processId) : null;

    private static string? GetCore(uint processId)
    {
        if (!_ready || _get is null)
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
    public static bool Set(uint processId, string? endpointId) => IsSupported && SetCore(processId, endpointId);

    private static bool SetCore(uint processId, string? endpointId)
    {
        var deviceString = endpointId is null ? null : Prefix + endpointId + RenderSuffix;
        if (!_ready || _set is null)
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
                // Verified layout: Windows 11 21H2 and every later build checked so far, including the 26300 Insider build (see the class summary).
                if (Environment.OSVersion.Version.Build < 21390)
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
                _set = Marshal.GetDelegateForFunctionPointer<SetFn>(Marshal.ReadIntPtr(vtable, Slot * nint.Size));
                _get = Marshal.GetDelegateForFunctionPointer<GetFn>(Marshal.ReadIntPtr(vtable, (Slot + 1) * nint.Size));

                _ready = true;
                BeginVerdict();
            }
            catch (Exception)
            {
                _ready = false;
            }
        }
    }

    private static string BuildKey()
    {
        // The audio policy code lives in AudioSes.dll / combase.dll, so the cumulative update number is part of "the build".
        var ubr = 0;
        try
        {
            ubr = Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", 0) ?? 0);
        }
        catch (Exception)
        {
        }

        return $"{Environment.OSVersion.Version.Build}.{ubr}";
    }

    private static string VerdictFile => Path.Combine(AppPaths.Root, "audio-routing-selftest.json");

    private sealed record Verdict(string Build, bool Ok, string Detail, string Checked);

    /// <summary>The folder of the in-progress marker (the mixer's data folder).</summary>
    private static string MarkerDir => AppPaths.FeatureData("mixer");

    private static void SaveVerdict(string key, bool ok, string detail)
    {
        try
        {
            AtomicFile.WriteAllText(VerdictFile, JsonSerializer.Serialize(new Verdict(key, ok, detail, DateTime.Now.ToString("s"))));
        }
        catch (Exception)
        {
            // Not persisted: the test just runs again next start.
        }
    }

    private static void BeginVerdict()
    {
        var key = BuildKey();
        try
        {
            if (File.Exists(VerdictFile) && JsonSerializer.Deserialize<Verdict>(File.ReadAllText(VerdictFile)) is { } saved && saved.Build == key)
            {
                _verdictOk = saved.Ok;
                if (!saved.Ok)
                {
                    Log?.Info($"Per-app output routing stays off on build {key}: {saved.Detail} (self-test from {saved.Checked}).");
                }

                return;
            }
        }
        catch (Exception)
        {
            // Unreadable verdict: test again.
        }

        // A marker without a verdict means an earlier self-test never finished: the raw call crashed or hung the process. Do not run it
        // again on this build; route picking stays off.
        string markerDir;
        try
        {
            markerDir = MarkerDir;
        }
        catch (Exception)
        {
            markerDir = string.Empty;
        }

        if (markerDir.Length > 0 && RoutingSelfTestMarker.IsPending(markerDir, key))
        {
            const string interrupted = "an earlier self-test did not finish";
            _verdictOk = false;
            SaveVerdict(key, false, interrupted);
            RoutingSelfTestMarker.Clear(markerDir, key);
            Log?.Warn($"Per-app output routing is turned off on build {key}: {interrupted}.");
            return;
        }

        _selfTestTask = Task.Run(() => SelfTest(key));
    }

    /// <summary>
    /// Self-test, once per OS build: opens this app's own silent playback stream on the current default output, pins this process (and nothing
    /// else) to that same default device, reads the choice back with the policy Get call and then puts this process back the way it was. The
    /// route is trusted only when the read-back matches the device id exactly; otherwise the picker is disabled for the build.
    /// </summary>
    private static void SelfTest(string key)
    {
        var pid = (uint)Environment.ProcessId;
        string detail;
        bool ok;
        WasapiOut? player = null;
        try
        {
            var deviceId = DefaultRenderId();
            if (deviceId is null)
            {
                // No output device right now: cannot judge, so stay off for this run and test again next time.
                Log?.Info("Per-app output routing self-test skipped: no default output device. The route picker stays off until it can run.");
                _verdictOk = false;
                return;
            }

            try
            {
                // A short, silent stream so that the process has a real render session while the route is written.
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(deviceId);
                player = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
                var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                player.Init(silence);
                player.Play();
            }
            catch (Exception ex)
            {
                // The stream is optional: the policy call is keyed by process id.
                Log?.Info("Per-app output routing self-test: no silent stream (" + ex.Message + "), testing the policy call alone.");
                player?.Dispose();
                player = null;
            }

            // Marked before the first raw call: if the process dies inside it, the next start skips the test.
            RoutingSelfTestMarker.Begin(MarkerDir, key);
            var before = GetCore(pid);
            var set = SetCore(pid, deviceId);
            var back = GetCore(pid);
            ok = set && string.Equals(back, deviceId, StringComparison.Ordinal);
            if (!ok && set && string.Equals(back, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                // Same GUID spelled with another letter case: the same device.
                ok = true;
            }

            detail = ok ? "round trip matched" : $"set={(set ? "ok" : "failed")} ({LastResult}), wrote '{deviceId}', read back '{back ?? "(nothing)"}'";
            SetCore(pid, before);
            var after = GetCore(pid);
            if (!string.Equals(after, before, StringComparison.OrdinalIgnoreCase))
            {
                Log?.Warn($"Per-app output routing self-test could not restore this process's own route (was '{before ?? "default"}', now '{after ?? "default"}').");
            }
        }
        catch (Exception ex)
        {
            ok = false;
            detail = "self-test threw " + ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            try
            {
                player?.Stop();
                player?.Dispose();
            }
            catch (Exception)
            {
            }
        }

        _verdictOk = ok;
        if (ok)
        {
            Log?.Info($"Per-app output routing self-test passed on build {key}.");
        }
        else
        {
            Log?.Warn($"Per-app output routing is turned off on build {key}: {detail}.");
        }

        SaveVerdict(key, ok, detail);
        try
        {
            RoutingSelfTestMarker.Clear(MarkerDir, key);
        }
        catch (Exception)
        {
        }
    }

    private static string? DefaultRenderId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
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

/// <summary>
/// The "self-test in progress" marker of the audio routing check (<c>audio-routing-selftest-&lt;build&gt;.inprogress</c>), kept apart from
/// the native calls so the decision can be tested.
/// </summary>
internal static class RoutingSelfTestMarker
{
    public static string PathFor(string dir, string build) => Path.Combine(dir, $"audio-routing-selftest-{build}.inprogress");

    /// <summary>True when a self-test for this build started and never reported a verdict.</summary>
    public static bool IsPending(string dir, string build) => File.Exists(PathFor(dir, build));

    /// <summary>Records that the self-test is about to make its first raw call.</summary>
    public static void Begin(string dir, string build)
    {
        try
        {
            AtomicFile.WriteAllText(PathFor(dir, build), DateTime.Now.ToString("s"));
        }
        catch (Exception)
        {
            // Without the marker the test still runs; only the crash protection is lost.
        }
    }

    /// <summary>Removes the marker once the verdict is saved.</summary>
    public static void Clear(string dir, string build)
    {
        try
        {
            File.Delete(PathFor(dir, build));
        }
        catch (Exception)
        {
        }
    }
}
