// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>One app that is capturing from a microphone right now.</summary>
internal sealed record MicUser(string Key, string Name, ImageSource? Icon);

/// <summary>
/// "Microphone in use" (the privacy indicator Windows draws in the tray): reads the same source, the capability consent store
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone</c>. Packaged apps are
/// subkeys directly below it, desktop apps are below <c>NonPackaged</c> (their path with '#' for '\'); each has
/// <c>LastUsedTimeStart</c> and <c>LastUsedTimeStop</c> (FILETIME). An app uses the microphone while its stop time is 0 or older
/// than its start time. A background thread waits on <c>RegNotifyChangeKeyValue</c> (subtree) with a slow poll as fallback, and
/// reports on the UI thread only when the set of apps changes.
/// </summary>
internal sealed class MicUseService : IDisposable
{
    public const string ConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private const uint NotifyChangeName = 0x1;
    private const uint NotifyChangeLastSet = 0x4;

    private readonly Dispatcher _ui;
    private readonly ILog? _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Name, ImageSource? Icon)> _identities = new(StringComparer.OrdinalIgnoreCase);
    private Thread? _thread;
    private ManualResetEvent? _stop;
    private IReadOnlyList<MicUser> _users = [];
    private bool _firstScanDone;
    private int _generation;

    public MicUseService(Dispatcher ui, ILog? log = null)
    {
        _ui = ui;
        _log = log;
    }

    /// <summary>Apps capturing from the microphone now (UI thread; replaced as a whole).</summary>
    public IReadOnlyList<MicUser> Users => _users;

    public bool Active => _users.Count > 0;

    public bool Running => _thread is not null;

    /// <summary>The registry key that is watched (always the Windows consent store; a test points it at a scratch copy).</summary>
    public string KeyPath { get; init; } = ConsentPath;

    /// <summary>WinKnife's own capture (recorder, tests) is not an "app using the microphone"; only a test turns this off.</summary>
    public bool ExcludeSelf { get; set; } = true;

    /// <summary>The set of apps changed (UI thread).</summary>
    public event Action? Changed;

    /// <summary>An app that was not capturing a moment ago started to (UI thread). Never raised for what was already running at start.</summary>
    public event Action<MicUser>? Started;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        var stop = new ManualResetEvent(false);
        _stop = stop;
        _firstScanDone = false;
        lock (_gate)
        {
            _lastPublished = null;
        }

        var generation = ++_generation;
        _thread = new Thread(() => Run(stop, generation)) { IsBackground = true, Name = "WinKnife microphone watch", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Stop()
    {
        var thread = _thread;
        var stop = _stop;
        _thread = null;
        _stop = null;
        _generation++;
        if (thread is not null && stop is not null)
        {
            stop.Set();
            thread.Join(1500);
            stop.Dispose();
        }

        if (_users.Count > 0)
        {
            _users = [];
            Changed?.Invoke();
        }
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- watching

    private void Run(ManualResetEvent stop, int generation)
    {
        using var changed = new AutoResetEvent(false);
        RegistryKey? key = null;
        var armed = false;
        try
        {
            while (!stop.WaitOne(0))
            {
                if (key is null)
                {
                    try
                    {
                        key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                    }
                    catch (Exception ex)
                    {
                        _log?.Warn($"Microphone watch could not open the consent store: {ex.Message}");
                    }

                    armed = false;
                }

                // arm first, scan second: a change between the two is not lost
                if (key is not null && !armed)
                {
                    var rc = RegNotifyChangeKeyValue(key.Handle, true, NotifyChangeName | NotifyChangeLastSet, changed.SafeWaitHandle, true);
                    if (rc == 0)
                    {
                        armed = true;
                    }
                    else
                    {
                        key.Dispose();
                        key = null;
                    }
                }

                Evaluate(generation);

                // the audio-session check has no change notification: look once a second
                var index = WaitHandle.WaitAny([stop, changed], 1000);
                if (index == 0)
                {
                    break;
                }

                if (index == 1)
                {
                    armed = false;

                    // answer at once, then let a burst of writes settle (Windows writes start and stop in two steps) and look again
                    Evaluate(generation);
                    if (stop.WaitOne(300))
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Warn($"Microphone watch stopped: {ex.Message}");
        }
        finally
        {
            key?.Dispose();
        }
    }

    private void Evaluate(int generation)
    {
        List<MicUser> now;
        try
        {
            now = Scan();
        }
        catch (Exception ex)
        {
            _log?.Warn($"Microphone scan failed: {ex.Message}");
            return;
        }

        if (generation != _generation)
        {
            return;
        }

        lock (_gate)
        {
            var before = _lastPublished;
            if (before is not null && before.Count == now.Count && before.Zip(now).All(p => p.First.Key == p.Second.Key))
            {
                return;
            }

            _lastPublished = now;
        }

        _ui.BeginInvoke(() =>
        {
            if (generation != _generation)
            {
                return;
            }

            var old = _users;
            _users = now;
            var first = !_firstScanDone;
            _firstScanDone = true;
            if (old.Count == now.Count && old.Select(u => u.Key).SequenceEqual(now.Select(u => u.Key)))
            {
                return;
            }

            foreach (var u in now)
            {
                if (!first && !old.Any(o => o.Key == u.Key))
                {
                    Started?.Invoke(u);
                }
            }

            Changed?.Invoke();
        });
    }

    private List<MicUser>? _lastPublished;

    // ---------------------------------------------------------------- reading the consent store

    internal sealed record Entry(string Key, bool Packaged, string Path, long Start, long Stop)
    {
        public bool InUse => IsInUse(Start, Stop);
    }

    /// <summary>The Windows rule: capturing while the stop time is 0 or the start time is newer than the stop time.</summary>
    public static bool IsInUse(long start, long stop) => start > 0 && (stop == 0 || start > stop);

    /// <summary>Every app the consent store knows (read only).</summary>
    internal static List<Entry> ReadEntries(string path = ConsentPath)
    {
        var list = new List<Entry>();
        using var root = Registry.CurrentUser.OpenSubKey(path, false);
        if (root is null)
        {
            return list;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                using var np = root.OpenSubKey(name, false);
                if (np is null)
                {
                    continue;
                }

                foreach (var sub in np.GetSubKeyNames())
                {
                    using var k = np.OpenSubKey(sub, false);
                    if (k is not null)
                    {
                        list.Add(new Entry(sub, false, sub.Replace('#', '\\'), Read(k, "LastUsedTimeStart"), Read(k, "LastUsedTimeStop")));
                    }
                }
            }
            else
            {
                using var k = root.OpenSubKey(name, false);
                if (k is not null)
                {
                    list.Add(new Entry(name, true, name, Read(k, "LastUsedTimeStart"), Read(k, "LastUsedTimeStop")));
                }
            }
        }

        return list;
    }

    private static long Read(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) switch
            {
                long l => l,
                int i => i,
                byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Both sources: the consent store (what Windows' own tray indicator shows) and the live capture sessions on every active
    /// microphone. Windows does not record every desktop app in the consent store, so the sessions catch the rest.
    /// </summary>
    private List<MicUser> Scan()
    {
        var result = ScanConsentStore();
        if (KeyPath != ConsentPath)
        {
            return result;
        }

        foreach (var user in ScanCaptureSessions())
        {
            if (!result.Any(r => string.Equals(r.Name, user.Name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(user);
            }
        }

        return result;
    }

    /// <summary>Processes with an active capture session on any microphone right now (WinKnife and the system session excluded).</summary>
    private List<MicUser> ScanCaptureSessions()
    {
        var result = new List<MicUser>();
        var seen = new HashSet<int>();
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive || session.IsSystemSoundsSession)
                        {
                            continue;
                        }

                        var pid = (int)session.GetProcessID;
                        if (pid <= 0 || (ExcludeSelf && pid == Environment.ProcessId) || !seen.Add(pid))
                        {
                            continue;
                        }

                        string? path = null;
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            path = process.MainModule?.FileName;
                        }
                        catch
                        {
                        }

                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        var key = "session:" + path;
                        (string Name, ImageSource? Icon) identity;
                        lock (_gate)
                        {
                            _identities.TryGetValue(key, out identity);
                        }

                        if (identity.Name is null)
                        {
                            identity = Desktop(path);
                            lock (_gate)
                            {
                                _identities[key] = identity;
                            }
                        }

                        result.Add(new MicUser(key, identity.Name, identity.Icon));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Warn($"Microphone session scan failed: {ex.Message}");
        }

        return result;
    }

    private List<MicUser> ScanConsentStore()
    {
        var inUse = ReadEntries(KeyPath).Where(e => e.InUse).ToList();
        var result = new List<MicUser>();
        if (inUse.Count == 0)
        {
            return result;
        }

        HashSet<string>? running = null;
        var self = Environment.ProcessPath ?? "";
        foreach (var e in inUse.OrderBy(x => x.Start))
        {
            if (!e.Packaged)
            {
                if (ExcludeSelf && string.Equals(e.Path, self, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // an app that crashed or was killed while capturing never writes its stop time: no process, no indicator
                running ??= RunningImageNames();
                if (!running.Contains(Path.GetFileName(e.Path)))
                {
                    continue;
                }
            }

            var (name, icon) = Identity(e);
            result.Add(new MicUser(e.Key, name, icon));
        }

        return result;
    }

    private static HashSet<string> RunningImageNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                set.Add(p.ProcessName + ".exe");
            }
            catch
            {
            }
            finally
            {
                p.Dispose();
            }
        }

        return set;
    }

    // ---------------------------------------------------------------- friendly names and icons

    private (string Name, ImageSource? Icon) Identity(Entry e)
    {
        lock (_gate)
        {
            if (_identities.TryGetValue(e.Key, out var cached))
            {
                return cached;
            }
        }

        var made = e.Packaged ? Packaged(e.Key) : Desktop(e.Path);
        lock (_gate)
        {
            _identities[e.Key] = made;
        }

        return made;
    }

    private static (string, ImageSource?) Desktop(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        ImageSource? icon = null;
        try
        {
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                {
                    name = info.FileDescription.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(info.ProductName))
                {
                    name = info.ProductName.Trim();
                }

                icon = IconOfFile(path);
            }
        }
        catch
        {
        }

        return (name, icon);
    }

    private static ImageSource? IconOfFile(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static (string, ImageSource?) Packaged(string family)
    {
        string? name = null;
        ImageSource? icon = null;
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var package = manager.FindPackagesForUser(string.Empty, family).FirstOrDefault();
            if (package is not null)
            {
                try
                {
                    var entry = package.GetAppListEntries().FirstOrDefault();
                    if (entry is not null)
                    {
                        name = entry.DisplayInfo.DisplayName;
                        icon = LogoOf(entry);
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    name = package.DisplayName;
                }
            }
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            name = Prettify(family);
        }

        return (name.Trim(), icon);
    }

    private static ImageSource? LogoOf(Windows.ApplicationModel.Core.AppListEntry entry)
    {
        try
        {
            var reference = entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var winrt = reference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            if (winrt.Size is 0 or > 4 * 1024 * 1024)
            {
                return null;
            }

            using var input = WindowsRuntimeStreamExtensions.AsStreamForRead(winrt);
            var memory = new MemoryStream();
            input.CopyTo(memory);
            memory.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = memory;
            bmp.EndInit();
            bmp.Freeze();
            return Trimmed(bmp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>App logos come with transparent padding (and a plate); crop to the drawn part so the mark reads at 16 px.</summary>
    private static ImageSource Trimmed(BitmapSource source)
    {
        try
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (pixels[(y * stride) + (x * 4) + 3] > 24)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (maxX < 0)
            {
                return source;
            }

            var side = Math.Max(maxX - minX + 1, maxY - minY + 1);
            var left = Math.Clamp(((minX + maxX + 1) / 2) - (side / 2), 0, Math.Max(0, w - side));
            var top = Math.Clamp(((minY + maxY + 1) / 2) - (side / 2), 0, Math.Max(0, h - side));
            side = Math.Min(side, Math.Min(w - left, h - top));
            if (side >= w - 2 && side >= h - 2)
            {
                return source;
            }

            var cropped = new CroppedBitmap(converted, new System.Windows.Int32Rect(left, top, side, side));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    /// <summary>"Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe" becomes "Windows Sound Recorder".</summary>
    internal static string Prettify(string family)
    {
        var id = family.Split('_')[0];
        foreach (var prefix in new[] { "Microsoft.Windows.", "Microsoft.", "MicrosoftWindows." })
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && id.Length > prefix.Length)
            {
                id = id[prefix.Length..];
                break;
            }
        }

        var sb = new StringBuilder();
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (c is '.' or '-')
            {
                sb.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(c);
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? family : text;
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint filter, SafeWaitHandle eventHandle, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
