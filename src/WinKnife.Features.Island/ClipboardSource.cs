// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Island;

internal sealed class ClipItem
{
    public string Id { get; init; } = "";

    public string Kind { get; init; } = "text";

    public string Text { get; init; } = "";

    public string? ImagePath { get; init; }

    public string? ThumbPath { get; init; }

    public DateTime Created { get; init; }

    public bool Pinned { get; init; }

    public string? Source { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool IsImage => Kind == "image";
}

/// <summary>
/// Clipboard history as the island needs it: reads the Clipboard history feature's index (read only), listens for clipboard
/// updates for the "Copied" notice, copies / pastes entries.
/// </summary>
internal sealed class ClipboardSource : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    private const int WmClipboardUpdate = 0x031D;

    private readonly IslandCore _core;
    private HwndSource? _source;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DateTime _lastNotice = DateTime.MinValue;
    private string _lastText = "";
    private string _file = "";
    private string _imageDir = "";

    public ClipboardSource(IslandCore core) => _core = core;

    public IReadOnlyList<ClipItem> Items { get; private set; } = [];

    public nint PasteTarget { get; set; }

    public event Action? Changed;

    public bool HistoryOn => _core.App.Get<bool>(K.ClipboardHistoryEnabled);

    public void Start()
    {
        try
        {
            var root = Path.GetDirectoryName(_core.Ctx.DataDirectory.TrimEnd('\\', '/')) ?? _core.Ctx.DataDirectory;
            var dir = Path.Combine(root, "clipboard-history");
            _file = Path.Combine(dir, "history.json");
            _imageDir = Path.Combine(dir, "images");
            Reload();
            if (Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir, "history.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
                _watcher.Changed += (_, _) => Schedule();
                _watcher.Created += (_, _) => Schedule();
            }
        }
        catch
        {
        }

        var parms = new HwndSourceParameters("WinKnifeIslandClip") { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(parms);
        _source.AddHook(Hook);
        AddClipboardFormatListener(_source.Handle);
    }

    private void Schedule()
    {
        _core.Ui.BeginInvoke(() =>
        {
            _debounce ??= new DispatcherTimer(DispatcherPriority.Background, _core.Ui) { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Reload();
    }

    public void Reload()
    {
        var list = new List<ClipItem>();
        try
        {
            if (File.Exists(_file))
            {
                using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    string Str(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    int Int(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                    var img = Str("ImageFile");
                    var thumb = Str("ThumbFile");
                    list.Add(new ClipItem
                    {
                        Id = Str("Id"),
                        Kind = Str("Kind") is { Length: > 0 } k ? k : "text",
                        Text = Str("Text"),
                        ImagePath = img.Length > 0 ? Path.Combine(_imageDir, img) : null,
                        ThumbPath = thumb.Length > 0 ? Path.Combine(_imageDir, thumb) : (img.Length > 0 ? Path.Combine(_imageDir, img) : null),
                        Created = DateTime.TryParse(Str("CreatedUtc"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : DateTime.Now,
                        Pinned = e.TryGetProperty("Pinned", out var p) && p.ValueKind == JsonValueKind.True,
                        Source = Str("Source") is { Length: > 0 } s ? s : null,
                        Width = Int("Width"),
                        Height = Int("Height"),
                    });
                }
            }
        }
        catch
        {
        }

        Items = [.. list.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Created)];
        Changed?.Invoke();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            _core.Ui.BeginInvoke(OnClipboard);
        }

        return 0;
    }

    private void OnClipboard()
    {
        if (!_core.IslandOn || !_core.Ctx.Runtime.IsAvailable("clipboard-history") || !HistoryOn)
        {
            return;
        }

        string text;
        try
        {
            text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : (System.Windows.Clipboard.ContainsImage() ? "\u0001image" : (System.Windows.Clipboard.ContainsFileDropList() ? "\u0001files" : ""));
        }
        catch
        {
            return;
        }

        if (text.Length == 0 || ((DateTime.UtcNow - _lastNotice).TotalMilliseconds < 400 && text == _lastText))
        {
            return;
        }

        _lastText = text;
        _lastNotice = DateTime.UtcNow;
        Schedule();
        if (!_core.ModuleVisible("clipboard") || !_core.App.Get<bool>(K.ClipboardWindow))
        {
            return;
        }

        _core.ShowNotice(new Notice { Event = NoticeEvents.ClipboardCopy, Glyph = G.Clipboard, Title = "Copied", Detail = "Clipboard" });
    }

    public void Copy(ClipItem item)
    {
        try
        {
            if (item.IsImage && item.ImagePath is not null && File.Exists(item.ImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(item.ImagePath);
                bmp.EndInit();
                System.Windows.Clipboard.SetImage(bmp);
            }
            else
            {
                System.Windows.Clipboard.SetText(item.Text);
            }
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void Paste(ClipItem item)
    {
        Copy(item);
        var target = PasteTarget;
        _core.Perform(() =>
        {
            if (target != 0)
            {
                NativeMethods.SetForegroundWindow(target);
            }

            InputSender.SendChord(0x11, 0x56);
        });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Stop();
        if (_source is not null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
        }
    }
}
