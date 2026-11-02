// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>Remembers copied text and images locally and brings them back from a searchable popup.</summary>
public sealed class ClipboardHistoryFeature : IFeature
{
    private const string DefaultIgnoredApps = "KeePass, KeePassXC, 1Password, Bitwarden, Dashlane, LastPass, NordPass, Enpass, RoboForm";
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.H);

    private IFeatureContext? _context;
    private HotkeyBindings? _bindings;
    private IDisposable? _subscription;
    private ClipboardHistoryStore? _store;
    private DispatcherTimer? _debounce;
    private ClipboardPopupWindow? _popup;
    private nint _previousWindow;
    private nint _lastOwnerWindow;

    public string Id => "clipboard-history";

    public string Name => "Clipboard history";

    public string Description => "Keep what you copied (text and images) on this PC and paste any of it again from a searchable popup.";

    public FeatureGroup Group => FeatureGroup.ClipboardFiles;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Clipboard history", () => TogglePopup(), ""),
        new FeatureAction("clear", "Clear clipboard history", ClearHistory, ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _store = new ClipboardHistoryStore(context.DataDirectory, context.Log, context.Ui);
        _store.Prune(Limit(), RetentionDays());

        _debounce = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Capture();
        };

        _subscription = ClipboardMonitor.Subscribe(OnClipboardChanged);
        _bindings = new HotkeyBindings(context,
        [
            new HotkeyBinding("hotkey", "Clipboard history", DefaultHotkey, () => TogglePopup()),
        ]);
    }

    public void Stop()
    {
        _bindings?.Dispose();
        _bindings = null;
        _subscription?.Dispose();
        _subscription = null;
        _debounce?.Stop();
        _debounce = null;
        var popup = _popup;
        _popup = null;
        if (popup is not null)
        {
            try
            {
                popup.Close();
            }
            catch (InvalidOperationException)
            {
                // already closing
            }
        }

        _store?.Dispose();
        _store = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddHotkey("hotkey", "Open history", DefaultHotkey, "Opens the popup near the mouse pointer. Type to search, Enter to paste.")
            .AddIntSlider("limit", "Items to keep", 10, 300, 50, "Pinned items do not count and are never removed.")
            .AddIntSlider("retentionDays", "Forget items after (days, 0 = never)", 0, 90, 0)
            .AddToggle("captureImages", "Remember images", true)
            .AddText("ignoredApps", "Ignore copies from these apps", DefaultIgnoredApps, "Comma separated process names, for example: KeePass, 1Password.")
            .AddNote("Content that marks itself as sensitive (password managers use ExcludeClipboardContentFromMonitorProcessing or CanIncludeInClipboardHistory) is never stored. History is kept in this app's data folder only.")
            .Build();
    }

    private int Limit() => Math.Clamp(_context?.Settings.Get("limit", 50) ?? 50, 5, 1000);

    private int RetentionDays() => Math.Max(0, _context?.Settings.Get("retentionDays", 0) ?? 0);

    private void OnClipboardChanged()
    {
        if (_debounce is null)
        {
            return;
        }

        _lastOwnerWindow = WinApi.GetClipboardOwner();
        _debounce.Stop();
        _debounce.Start();
    }

    private void Capture()
    {
        var context = _context;
        var store = _store;
        if (context is null || store is null)
        {
            return;
        }

        try
        {
            if (ClipboardIO.IsOwnWrite() || ClipboardIO.IsSensitive())
            {
                return;
            }

            var source = OwnerName();
            if (IsIgnored(source))
            {
                return;
            }

            var text = ClipboardIO.ReadText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                store.AddText(text, string.IsNullOrEmpty(source) ? null : source, Limit(), RetentionDays());
                return;
            }

            if (context.Settings.Get("captureImages", true))
            {
                var png = ClipboardIO.ReadImagePng();
                if (png is not null)
                {
                    store.AddImage(png, string.IsNullOrEmpty(source) ? null : source, Limit(), RetentionDays());
                }
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not record clipboard content", ex);
        }
    }

    private string OwnerName()
    {
        try
        {
            if (_lastOwnerWindow == 0)
            {
                return string.Empty;
            }

            NativeMethods.GetWindowThreadProcessId(_lastOwnerWindow, out var pid);
            if (pid == 0)
            {
                return string.Empty;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private bool IsIgnored(string processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        var list = _context?.Settings.Get("ignoredApps", DefaultIgnoredApps) ?? string.Empty;
        return list.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), processName, StringComparison.OrdinalIgnoreCase));
    }

    private void TogglePopup()
    {
        var context = _context;
        var store = _store;
        if (context is null || store is null)
        {
            return;
        }

        if (_popup is not null)
        {
            var existing = _popup;
            _popup = null;
            try
            {
                existing.Close();
            }
            catch (InvalidOperationException)
            {
                // already closing
            }

            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        _previousWindow = pid == (uint)Environment.ProcessId ? 0 : foreground;

        var popup = new ClipboardPopupWindow(store, PasteEntry);
        _popup = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup))
            {
                _popup = null;
            }
        };

        var cursor = Screens.CursorPosition();
        try
        {
            popup.ShowNear((int)cursor.X, (int)cursor.Y);
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not show the clipboard popup", ex);
            _popup = null;
        }
    }

    private async void PasteEntry(ClipEntry entry)
    {
        var context = _context;
        var store = _store;
        if (context is null || store is null)
        {
            return;
        }

        try
        {
            var target = _previousWindow;
            var written = entry.IsImage
                ? File.Exists(store.ImagePath(entry)) && ClipboardIO.WriteImage(await File.ReadAllBytesAsync(store.ImagePath(entry)))
                : ClipboardIO.WriteText(entry.Text ?? string.Empty);
            if (!written)
            {
                context.Notifier.Show("Clipboard history", "Could not put the item on the clipboard.", NotificationLevel.Warning);
                return;
            }

            store.Bump(entry);
            if (target == 0 || !WinApi.IsWindow(target))
            {
                context.Notifier.Show("Clipboard history", "Copied. Press Ctrl+V to paste.");
                return;
            }

            await ModifierHelper.ReleaseAsync();
            WinUtil.Activate(target);
            // Give the target window time to take the focus back before the keystroke arrives.
            for (var i = 0; i < 20 && NativeMethods.GetForegroundWindow() != target; i++)
            {
                await Task.Delay(15);
            }

            await Task.Delay(60);
            InputSender.SendChord((ushort)VK_CONTROL, (ushort)'V');
        }
        catch (Exception ex)
        {
            context.Log.Error("Pasting from clipboard history failed", ex);
        }
    }

    private void ClearHistory()
    {
        _store?.ClearUnpinned();
        _context?.Notifier.Show("Clipboard history", "Cleared (pinned items were kept).");
    }
}
