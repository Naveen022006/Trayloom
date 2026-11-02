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
    private DispatcherTimer? _debounce;
    private IClipboardHistoryService? _service;
    private DateTime _lastNotice = DateTime.MinValue;
    private string _lastText = "";

    public ClipboardSource(IslandCore core) => _core = core;

    public IReadOnlyList<ClipItem> Items { get; private set; } = [];

    public nint PasteTarget { get; set; }

    public event Action? Changed;

    /// <summary>True while the Clipboard history feature is installed and recording.</summary>
    public bool HistoryOn => _service?.IsRecording ?? _core.App.Get<bool>(K.ClipboardHistoryEnabled);

    public void Start()
    {
        _core.Ctx.Services.Changed += Rehook;
        Rehook();
        var parms = new HwndSourceParameters("WinKnifeIslandClip") { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(parms);
        _source.AddHook(Hook);
        AddClipboardFormatListener(_source.Handle);
    }

    /// <summary>Follows the published service (Files starts independently of the island and can be installed later).</summary>
    private void Rehook()
    {
        var service = _core.Ctx.Services.Get<IClipboardHistoryService>();
        if (ReferenceEquals(service, _service))
        {
            return;
        }

        if (_service is not null)
        {
            _service.Changed -= Schedule;
        }

        _service = service;
        if (service is not null)
        {
            service.Changed += Schedule;
        }

        Schedule();
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
        if (_service is { } service)
        {
            foreach (var e in service.Items)
            {
                list.Add(new ClipItem
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    Text = e.IsFiles ? string.Join(Environment.NewLine, e.Files.Select(Path.GetFileName)) : e.Text,
                    ImagePath = e.ImagePath,
                    ThumbPath = e.ImagePath,
                    Created = e.CopiedAt,
                    Pinned = e.Pinned,
                    Source = e.SourceExe,
                    Width = e.Width,
                    Height = e.Height,
                });
            }
        }

        Items = list;
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
        if (!_core.IslandOn || _service is null || !HistoryOn)
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
        if (_service is null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        _service.Copy(item.Id);
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
        _core.Ctx.Services.Changed -= Rehook;
        if (_service is not null)
        {
            _service.Changed -= Schedule;
        }

        _debounce?.Stop();
        if (_source is not null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
        }
    }
}
