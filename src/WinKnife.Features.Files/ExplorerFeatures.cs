// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Files;

/// <summary>
/// Cut &amp; paste in File Explorer: Explorer already moves cut files, so this passive feature adds Vorssaint's floating panel
/// (what is cut, progress of a move that leaves its volume, "Moved!"), and "Paste copied images as files". It never swallows
/// Ctrl+X / Ctrl+V while a rename box, the address bar or the search box has focus.
/// </summary>
public sealed class ExplorerCutPasteFeature : IFeature
{
    private IFeatureContext? _context;
    private IDisposable? _clipboard;
    private IDisposable? _keyboard;
    private ForegroundTracker? _tracker;
    private CutFeedbackWindow? _hud;
    private DispatcherTimer? _poll;
    private DispatcherTimer? _resultTimer;
    private List<CutItem> _marked = [];
    private uint _markedSequence;
    private uint _ignoreSequence;
    private volatile bool _hasMarks;
    private volatile bool _hookCutEnabled;
    private volatile bool _hookImageEnabled;
    private bool _awaitingPaste;
    private DateTime _pasteStarted;
    private string? _pasteDestination;
    private long _totalBytes;
    private bool _indeterminate;
    private bool _moving;

    public string Id => "finder-cut-paste";

    public bool IsRunning => _context is not null && _context.App.Get<bool>("finderCutPasteEnabled");

    internal static ExplorerCutPasteFeature? Current { get; private set; }

    internal static void RegisterDefaults(IAppSettings app)
    {
        app.Register("finderCutPasteEnabled", false);
        app.Register("finderCutPasteShowHUD", true);
        app.Register("finderPasteImageAsFile", false);
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        RegisterDefaults(context.App);
        Hub.Settings = context.App;
        context.App.Changed += OnSettingChanged;
        Current = this;
        Apply();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSettingChanged;
        }

        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        Teardown();
        _context = null;
    }

    private void OnSettingChanged(string key)
    {
        if (key is "finderCutPasteEnabled" or "finderCutPasteShowHUD" or "finderPasteImageAsFile")
        {
            _context?.Ui.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var cut = context.App.Get<bool>("finderCutPasteEnabled");
        var image = context.App.Get<bool>("finderPasteImageAsFile");
        _hookCutEnabled = cut;
        _hookImageEnabled = image;
        if (cut && _clipboard is null)
        {
            _clipboard = ClipboardMonitor.Subscribe(OnClipboard);
            _tracker = new ForegroundTracker();
            _tracker.ForegroundChanged += _ => UpdateHud();
        }
        else if (!cut)
        {
            _clipboard?.Dispose();
            _clipboard = null;
            ClearMarks();
            _tracker?.Dispose();
            _tracker = null;
        }

        if ((cut || image) && _keyboard is null)
        {
            _keyboard = context.Hooks.SubscribeKeyboard(OnKey, priority: 20);
        }
        else if (!cut && !image && _keyboard is not null)
        {
            _keyboard.Dispose();
            _keyboard = null;
        }

        if (!cut)
        {
            _hud?.HideHud();
        }
        else
        {
            UpdateHud();
        }
    }

    private void Teardown()
    {
        _keyboard?.Dispose();
        _keyboard = null;
        _clipboard?.Dispose();
        _clipboard = null;
        _tracker?.Dispose();
        _tracker = null;
        _poll?.Stop();
        _poll = null;
        _resultTimer?.Stop();
        _resultTimer = null;
        var hud = _hud;
        _hud = null;
        if (hud is not null)
        {
            hud.Close();
        }

        _marked = [];
        _hasMarks = false;
    }

    // ---------------------------------------------------------------------------------------------- keyboard (hook thread)

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife || !e.IsKeyDown)
        {
            return false;
        }

        if (e.VkCode == 0x1B && e.Modifiers == ModifierKeys.None && _hasMarks)
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (ExplorerShell.IsExplorerWindow(fg) && ExplorerShell.FileListFocused(fg))
            {
                _context?.Ui.BeginInvoke(CancelPendingCut);
            }

            return false;
        }

        if (e.VkCode != 'V' || e.Modifiers != ModifierKeys.Control)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (!ExplorerShell.IsExplorerWindow(foreground) || !ExplorerShell.FileListFocused(foreground))
        {
            return false;
        }

        if (_hookImageEnabled && !ClipboardIO.IsAvailable(ClipFormats.HDrop)
            && (ClipboardIO.IsAvailable(ClipFormats.Png) || ClipboardIO.IsAvailable(ClipFormats.DibV5) || ClipboardIO.IsAvailable(ClipFormats.Dib)))
        {
            _context?.Ui.BeginInvoke(() => PasteImage(foreground));
            return true;
        }

        if (_hookCutEnabled && _hasMarks)
        {
            _context?.Ui.BeginInvoke(() => OnPaste(foreground));
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------- marking

    private async void OnClipboard()
    {
        var context = _context;
        if (context is null || !context.App.Get<bool>("finderCutPasteEnabled"))
        {
            return;
        }

        var sequence = WinApi.GetClipboardSequenceNumber();
        if (sequence == _ignoreSequence)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var fromExplorer = ExplorerShell.IsExplorerWindow(foreground);
        if (_awaitingPaste)
        {
            return; // the paste watcher decides
        }

        List<string>? files = null;
        uint effect = 0;
        if (ClipboardIO.IsAvailable(ClipFormats.HDrop))
        {
            (files, effect) = await Task.Run(() => (ClipboardIO.ReadFiles(10_000), ClipboardIO.ReadDropEffect()));
        }

        if (_context is null || WinApi.GetClipboardSequenceNumber() != sequence)
        {
            return;
        }

        if (files is { Count: > 0 } && effect == WinApi.DROPEFFECT_MOVE && fromExplorer)
        {
            Mark(files, sequence);
        }
        else if (_marked.Count > 0 && sequence != _markedSequence)
        {
            // Something else was copied: the cut is off.
            ClearMarks();
            UpdateHud();
        }
    }

    private void Mark(List<string> files, uint sequence)
    {
        _marked = [.. files.Select(p => new CutItem(p, Path.GetFileName(p.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : p, ShellIcons.ForPath(p, small: true)))];
        _markedSequence = sequence;
        _hasMarks = true;
        _awaitingPaste = false;
        _moving = false;
        _resultTimer?.Stop();
        UpdateHud();
    }

    private void ClearMarks()
    {
        _marked = [];
        _hasMarks = false;
        _awaitingPaste = false;
        _moving = false;
        _poll?.Stop();
    }

    /// <summary>The panel's cancel button and Esc: rewrites the clipboard as a copy (files untouched) and clears the marks.</summary>
    internal void CancelPendingCut()
    {
        if (_marked.Count == 0)
        {
            return;
        }

        if (WinApi.GetClipboardSequenceNumber() == _markedSequence && ClipboardIO.RewriteAsCopy())
        {
            _ignoreSequence = WinApi.GetClipboardSequenceNumber();
        }

        ClearMarks();
        UpdateHud();
    }

    private bool HudWanted()
    {
        var context = _context;
        if (context is null || !context.App.Get<bool>("finderCutPasteShowHUD"))
        {
            return false;
        }

        return ExplorerShell.IsExplorerWindow(NativeMethods.GetForegroundWindow());
    }

    private void UpdateHud()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        if (_resultTimer is { IsEnabled: true })
        {
            return; // a result is on screen
        }

        if (_marked.Count == 0 || !context.App.Get<bool>("finderCutPasteEnabled"))
        {
            if (!_moving)
            {
                _hud?.HideHud();
            }

            return;
        }

        if (!HudWanted() && !_moving)
        {
            _hud?.HideHud();
            return;
        }

        _hud ??= new CutFeedbackWindow(CancelPendingCut);
        if (_moving)
        {
            return;
        }

        _hud.ShowMarked(_marked);
    }

    // ---------------------------------------------------------------------------------------------- paste

    private void OnPaste(nint foreground)
    {
        if (_marked.Count == 0 || _awaitingPaste)
        {
            return;
        }

        _awaitingPaste = true;
        _pasteStarted = DateTime.UtcNow;
        _pasteDestination = ExplorerShell.CurrentFolder(foreground);
        _moving = false;
        _totalBytes = 0;
        _indeterminate = false;
        _poll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, OnPoll, _context!.Ui);
        _poll.Start();
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        if (!_awaitingPaste || _context is null)
        {
            _poll?.Stop();
            return;
        }

        var elapsed = DateTime.UtcNow - _pasteStarted;
        var gone = _marked.Count(m => !File.Exists(m.Path) && !Directory.Exists(m.Path));
        var clipboardCleared = !ClipboardIO.IsAvailable(ClipFormats.HDrop);
        var finished = gone == _marked.Count || (clipboardCleared && gone > 0 && elapsed > TimeSpan.FromMilliseconds(400));

        if (finished)
        {
            _poll?.Stop();
            var moved = gone;
            var failed = Math.Max(0, _marked.Count - moved);
            ShowResult(moved, failed);
            return;
        }

        // A copy still running: only a move that leaves its volume takes long enough to need progress.
        var destinationHasSome = _pasteDestination is not null && _marked.Any(m =>
        {
            var target = Path.Combine(_pasteDestination, m.Name);
            return File.Exists(target) || Directory.Exists(target);
        });
        if (!destinationHasSome && !clipboardCleared)
        {
            if (elapsed > TimeSpan.FromSeconds(2))
            {
                // Nothing happened (pasted somewhere that cannot take it): keep the marks.
                _awaitingPaste = false;
                _poll?.Stop();
            }

            return;
        }

        if (!CrossVolume())
        {
            if (elapsed > TimeSpan.FromSeconds(30))
            {
                _awaitingPaste = false;
                _poll?.Stop();
            }

            return;
        }

        if (!_moving && elapsed > TimeSpan.FromMilliseconds(500))
        {
            _moving = true;
            ComputeTotals();
            _hud ??= new CutFeedbackWindow(CancelPendingCut);
        }

        if (_moving)
        {
            long finishedBytes = 0;
            long currentBytes = 0;
            var completed = 0;
            string current = _marked[0].Name;
            var foundCurrent = false;
            foreach (var item in _marked)
            {
                var exists = File.Exists(item.Path) || Directory.Exists(item.Path);
                if (!exists)
                {
                    completed++;
                    finishedBytes += _sizes.GetValueOrDefault(item.Path);
                    continue;
                }

                if (!foundCurrent)
                {
                    foundCurrent = true;
                    current = item.Name;
                    try
                    {
                        var target = _pasteDestination is null ? null : Path.Combine(_pasteDestination, item.Name);
                        if (target is not null && File.Exists(target))
                        {
                            currentBytes = new FileInfo(target).Length;
                        }
                    }
                    catch (Exception)
                    {
                        currentBytes = 0;
                    }
                }
            }

            double? fraction = _indeterminate || _totalBytes <= 0 ? null : Math.Min(1.0, (finishedBytes + currentBytes) / (double)_totalBytes);
            _hud!.ShowMoving(current, completed, _marked.Count, fraction);
        }
    }

    private readonly Dictionary<string, long> _sizes = [];

    private bool CrossVolume()
    {
        if (_pasteDestination is null)
        {
            return false;
        }

        var destinationRoot = Path.GetPathRoot(_pasteDestination);
        return _marked.Any(m => !string.Equals(Path.GetPathRoot(m.Path), destinationRoot, StringComparison.OrdinalIgnoreCase));
    }

    private void ComputeTotals()
    {
        _sizes.Clear();
        _totalBytes = 0;
        foreach (var item in _marked)
        {
            if (Directory.Exists(item.Path))
            {
                _indeterminate = true; // a folder makes the total unknown
                continue;
            }

            try
            {
                var length = new FileInfo(item.Path).Length;
                _sizes[item.Path] = length;
                _totalBytes += length;
            }
            catch (Exception)
            {
                _indeterminate = true;
            }
        }
    }

    private void ShowResult(int moved, int failed)
    {
        _awaitingPaste = false;
        _moving = false;
        _marked = [];
        _hasMarks = false;
        if (!(_context?.App.Get<bool>("finderCutPasteShowHUD") ?? false))
        {
            return;
        }

        _hud ??= new CutFeedbackWindow(CancelPendingCut);
        _hud.ShowResult(moved, failed);
        _resultTimer?.Stop();
        _resultTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.8), DispatcherPriority.Background, (_, _) =>
        {
            _resultTimer?.Stop();
            _hud?.HideHud();
        }, _context.Ui);
        _resultTimer.Start();
    }

    // ---------------------------------------------------------------------------------------------- paste image as file

    private async void PasteImage(nint foreground)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        try
        {
            var folder = ExplorerShell.CurrentFolder(foreground);
            byte[]? png = folder is null ? null : await Task.Run(ClipboardIO.ReadImagePng);
            if (folder is not null && png is { Length: > 0 })
            {
                var name = $"Pasted_Image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var target = Path.Combine(folder, name);
                for (var n = 2; File.Exists(target); n++)
                {
                    target = Path.Combine(folder, $"Pasted_Image_{DateTime.Now:yyyyMMdd_HHmmss} {n}.png");
                }

                await File.WriteAllBytesAsync(target, png);
                return;
            }
        }
        catch (Exception ex)
        {
            context.Log.Warn($"Paste image as file failed: {ex.Message}");
        }

        // Anything else is handed back to Explorer as an ordinary paste.
        InputSender.SendChord((ushort)VK_CONTROL, (ushort)'V');
    }
}

/// <summary>
/// Rename shortcut: a key of your choice acts as F2 in File Explorer (and the desktop) when a file list has focus; text fields
/// are left alone. With the default (F2) nothing extra is needed.
/// </summary>
public sealed class ExplorerRenameFeature : IFeature
{
    private IFeatureContext? _context;
    private IDisposable? _hook;
    private volatile Hotkey? _hotkey;

    public string Id => "finder-rename";

    public void Start(IFeatureContext context)
    {
        _context = context;
        context.App.Register("finderRenameEnabled", false);
        context.App.Changed += OnSettingChanged;
        context.Shortcuts.Changed += OnShortcutChanged;
        Refresh();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSettingChanged;
            _context.Shortcuts.Changed -= OnShortcutChanged;
        }

        _hook?.Dispose();
        _hook = null;
        _context = null;
    }

    private void OnSettingChanged(string key)
    {
        if (key == "finderRenameEnabled")
        {
            _context?.Ui.BeginInvoke(Refresh);
        }
    }

    private void OnShortcutChanged(string roleId)
    {
        if (roleId == "finderRename")
        {
            _context?.Ui.BeginInvoke(Refresh);
        }
    }

    private void Refresh()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var enabled = context.App.Get<bool>("finderRenameEnabled");
        _hotkey = enabled ? context.Shortcuts.GetHotkey("finderRename") : null;
        var needsHook = enabled && _hotkey is { } key && !(key.Key == Key.F2 && key.Modifiers == HotkeyModifiers.None);
        if (needsHook && _hook is null)
        {
            _hook = context.Hooks.SubscribeKeyboard(OnKey, priority: 20);
        }
        else if (!needsHook && _hook is not null)
        {
            _hook.Dispose();
            _hook = null;
        }
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        var hotkey = _hotkey;
        if (hotkey is null || e.IsFromWinKnife)
        {
            return false;
        }

        var wanted = (ushort)hotkey.VirtualKey;
        if (e.VkCode != wanted)
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            modifiers |= ModifierKeys.Windows;
        }

        if (e.Modifiers != modifiers)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (!ExplorerShell.IsExplorerWindow(foreground, includeDialogs: false) || !ExplorerShell.FileListFocused(foreground))
        {
            return false;
        }

        // Swallow the key (both the press and its release) and give Explorer an F2.
        if (e.IsKeyDown)
        {
            InputSender.SendKey(0x71); // VK_F2 down
            InputSender.SendKey(0x71, keyUp: true);
        }

        return true;
    }
}
