// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.Files;

/// <summary>What the clipboard history offers to its windows, settings and panel views.</summary>
internal interface IClipboardHost
{
    ClipboardHistoryStore Store { get; }

    IContributionServices Services { get; }

    bool HistoryEnabled { get; }

    int Limit { get; }

    int RetentionDays { get; }

    /// <summary>Text of the currently registered history shortcut, or null when it is off.</summary>
    string? ShortcutText { get; }

    /// <summary>Id of the text entry open in the preview editor (exempt from the retention clock).</summary>
    string? EditingEntryId { get; set; }

    bool CanEditImages { get; }

    void ShowWindow();

    void ToggleWindow();

    void HideWindow();

    /// <summary>Applies a changed capture preference (start / stop watching).</summary>
    void Sync();

    /// <summary>Puts the entries on the clipboard and optionally pastes them into the previous app.</summary>
    void PasteEntries(IReadOnlyList<ClipEntry> entries, bool paste, Action<bool>? completed = null);

    void EditImage(ClipEntry entry);

    Task<bool> CopyImageText(ClipEntry entry);

    /// <summary>Raised when capture state, layout or the shortcut changed.</summary>
    event Action? StateChanged;
}

/// <summary>Remembers copied text, images and files locally and brings them back from a searchable quick window.</summary>
public sealed class ClipboardHistoryFeature : IFeature, IClipboardHost, IClipboardHistoryService
{
    private static readonly int[] AllowedLimits = [20, 50, 100, 250, 500, 1000, 10000, 0];
    private static readonly int[] AllowedRetention = [1, 7, 30, 90, 365, 0];

    private IFeatureContext? _context;
    private ClipboardHistoryStore? _store;
    private IDisposable? _subscription;
    private ForegroundTracker? _tracker;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _retention;
    private ClipboardQuickWindow? _window;
    private ClipboardAutoClear? _autoClear;
    private ClipboardTrayPill? _pill;
    private nint _pasteTarget;
    private nint _lastOwner;
    private (string Exe, string? Path)? _lastExternalSource;
    private uint _capturedSequence;
    private bool _capturing;
    private bool _readInFlight;

    public string Id => "clipboard-history";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Clipboard history"), () => ToggleWindow(), ""),
        new FeatureAction("clear", Loc.T("Clear unpinned"), ClearUnpinnedWithConfirmation, ""),
    ];

    public event Action? StateChanged;

    ClipboardHistoryStore IClipboardHost.Store => _store!;

    IContributionServices IClipboardHost.Services => _context!;

    public bool HistoryEnabled => _context?.App.Get<bool>("clipboardHistoryEnabled") ?? false;

    public int Limit
    {
        get
        {
            var value = _context?.App.Get<int>("clipboardHistoryLimit") ?? 50;
            return AllowedLimits.Contains(value) ? value : 50;
        }
    }

    public int RetentionDays
    {
        get
        {
            var value = _context?.App.Get<int>("clipboardHistoryRetentionDays") ?? 0;
            return AllowedRetention.Contains(value) ? value : 0;
        }
    }

    public string? ShortcutText
    {
        get
        {
            if (_context is null)
            {
                return null;
            }

            var state = _context.Shortcuts.GetState("clipboardHistory");
            return state is { IsActive: true, Effective: { } hotkey } ? hotkey.ToString() : null;
        }
    }

    public string? EditingEntryId { get; set; }

    public bool CanEditImages => _context?.Services.Get<IScreenshotEditorService>() is not null || FindScreenshotAction("clipboard image") is not null || FindScreenshotAction("edit") is not null;

    // ---------------------------------------------------------------------------------------------- IClipboardHistoryService

    bool IClipboardHistoryService.IsRecording => _capturing;

    IReadOnlyList<ClipboardHistoryItem> IClipboardHistoryService.Items
    {
        get
        {
            var store = _store;
            if (store is null)
            {
                return [];
            }

            ClipboardHistoryItem Map(ClipEntry e) => new(
                e.Id,
                e.Kind,
                e.Text,
                e.FilePaths,
                e.ImageFile is null ? null : store.ImagePath(e),
                e.CopiedAt.ToLocalTime(),
                e.IsPinned,
                e.SourceExe,
                e.ImageWidth,
                e.ImageHeight);
            return [.. store.Pinned.Select(Map), .. store.Recent.OrderByDescending(e => e.CopiedAt).Select(Map)];
        }
    }

    event Action? IClipboardHistoryService.Changed
    {
        add => StateChanged += value;
        remove => StateChanged -= value;
    }

    void IClipboardHistoryService.Copy(string id)
    {
        if (_store?.Find(id) is { } entry)
        {
            ((IClipboardHost)this).PasteEntries([entry], paste: false);
        }
    }

    void IClipboardHistoryService.Open() => ShowWindow();

    void IClipboardHistoryService.ClearUnpinned() => _store?.ClearUnpinned();

    void IClipboardHistoryService.EditImage(string id)
    {
        if (_store?.Find(id) is { IsImage: true } entry)
        {
            ((IClipboardHost)this).EditImage(entry);
        }
    }

    /// <summary>Registers the defaults of every key the clipboard page and panel read.</summary>
    internal static void RegisterDefaults(IAppSettings app)
    {
        app.Register("clipboardHistoryEnabled", false);
        app.Register("clipboardHistoryShortcutEnabled", true);
        app.Register("clipboardHistoryLayout", "list");
        app.Register("clipboardHistoryShowSourceAppIcons", false);
        app.Register("clipboardHistoryMenuBarPreview", false);
        app.Register("clipboardHistoryMenuBarPreviewLength", 20);
        app.Register("clipboardHistoryIncludeImagesFiles", true);
        app.Register("clipboardHistoryFilterSensitiveText", false);
        app.Register("clipboardHistoryIgnoredApps", new List<string>());
        app.Register("clipboardHistoryLimit", 50);
        app.Register("clipboardHistoryRetentionDays", 0);
        app.Register("clipboardHistoryWindowWidth", 0.0);
        app.Register("clipboardHistoryWindowHeight", 0.0);
        app.Register("clipboardHistoryQuickPreview", false);
        app.Register("panelUtilityClipboard", true);
        app.Register("clipboardAutoClearOnDelay", false);
        app.Register("clipboardAutoClearDelaySeconds", 20);
        app.Register("clipboardAutoClearOnSleep", false);
        app.Register("clipboardAutoClearOnDisplaySleep", false);
        app.Register("clipboardAutoClearOnScreenLock", false);
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        RegisterDefaults(context.App);
        _store = new ClipboardHistoryStore(context.DataDirectory, context.Log, context.Ui);
        _store.Trim(Limit, RetentionDays, null);
        _store.Changed += OnStoreChanged;
        _tracker = new ForegroundTracker();

        _debounce = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Capture();
        };
        _retention = new DispatcherTimer(TimeSpan.FromMinutes(30), DispatcherPriority.Background, (_, _) => TrimNow(), context.Ui);
        _retention.Start();

        _autoClear = new ClipboardAutoClear(context, () => _pill?.Refresh());
        _pill = new ClipboardTrayPill(this);
        _subscription = ClipboardMonitor.Subscribe(OnClipboardChanged);
        context.Shortcuts.Bind("clipboardHistory", () => ToggleWindow());
        context.Services.Provide<IClipboardHistoryService>(this);
        context.App.Changed += OnSettingChanged;
        StateChanged += ClipboardUtility.Raise;
        Hub.Clipboard = this;
        Hub.Settings = context.App;
        Sync();
        _pill.Refresh();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSettingChanged;
        }

        StateChanged -= ClipboardUtility.Raise;
        _context?.Services.Withdraw<IClipboardHistoryService>(this);
        if (ReferenceEquals(Hub.Clipboard, this))
        {
            Hub.Clipboard = null;
        }

        _subscription?.Dispose();
        _subscription = null;
        _debounce?.Stop();
        _debounce = null;
        _retention?.Stop();
        _retention = null;
        _autoClear?.Dispose();
        _autoClear = null;
        _pill?.Dispose();
        _pill = null;
        var window = _window;
        _window = null;
        if (window is not null)
        {
            try
            {
                window.ForceClose();
            }
            catch (InvalidOperationException)
            {
                // already closing
            }
        }

        _tracker?.Dispose();
        _tracker = null;
        if (_store is not null)
        {
            _store.Changed -= OnStoreChanged;
            _store.Dispose();
            _store = null;
        }

        _context = null;
    }

    // ---------------------------------------------------------------------------------------------- settings

    private void OnSettingChanged(string key)
    {
        if (!key.StartsWith("clipboardHistory", StringComparison.Ordinal) && !key.StartsWith("clipboardAutoClear", StringComparison.Ordinal))
        {
            return;
        }

        _context?.Ui.BeginInvoke(() =>
        {
            if (_context is null)
            {
                return;
            }

            switch (key)
            {
                case "clipboardHistoryEnabled":
                    Sync();
                    break;
                case "clipboardHistoryLimit":
                case "clipboardHistoryRetentionDays":
                    TrimNow();
                    break;
                case "clipboardHistoryLayout":
                    _window?.ApplyLayout();
                    break;
            }

            _pill?.Refresh();
            _autoClear?.Sync();
            StateChanged?.Invoke();
        });
    }

    public void Sync()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var wasCapturing = _capturing;
        _capturing = HistoryEnabled;
        if (_capturing)
        {
            // Baseline: whatever is on the clipboard now was copied before; it is not recorded until it changes.
            _capturedSequence = WinApi.GetClipboardSequenceNumber();
            if (!wasCapturing)
            {
                MatchBaseline();
            }
        }

        StateChanged?.Invoke();
        _pill?.Refresh();
    }

    /// <summary>The saved entry (if any) whose content is what the clipboard holds right now becomes the "latest copy".</summary>
    private async void MatchBaseline()
    {
        var store = _store;
        if (store is null || _context is null)
        {
            return;
        }

        try
        {
            var includeImagesFiles = _context.App.Get<bool>("clipboardHistoryIncludeImagesFiles");
            var content = await Task.Run(() => ReadClipboard(includeImagesFiles));
            if (_store is null || content is null || content.Skip)
            {
                return;
            }

            ClipEntry? match = content.Kind switch
            {
                ClipKinds.Files => store.Entries.FirstOrDefault(e => e.IsFiles && e.FilePaths.SequenceEqual(content.Files!, StringComparer.OrdinalIgnoreCase)),
                ClipKinds.Image => store.Entries.FirstOrDefault(e => e.IsImage && e.ImageHash == ClipboardHistoryStore.Sha256(content.Png!)),
                _ => store.Entries.FirstOrDefault(e => e.IsText && e.Text == content.Text),
            };
            if (match is not null)
            {
                store.LatestPasteboardEntryId = match.Id;
                _pill?.Refresh();
            }
        }
        catch (Exception)
        {
            // a busy clipboard: the preview stays blank until the next copy
        }
    }

    private void TrimNow() => _store?.Trim(Limit, RetentionDays, EditingEntryId is null ? null : new HashSet<string> { EditingEntryId });

    private void OnStoreChanged()
    {
        _pill?.Refresh();
        StateChanged?.Invoke();
    }

    // ---------------------------------------------------------------------------------------------- capture

    private void OnClipboardChanged()
    {
        if (_debounce is null || !_capturing)
        {
            return;
        }

        var owner = WinApi.GetClipboardOwner();
        if (owner != 0)
        {
            _lastOwner = owner;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private sealed record Captured(string Kind, string? Text, byte[]? Png, List<string>? Files, bool Skip);

    private async void Capture()
    {
        var context = _context;
        var store = _store;
        if (context is null || store is null || !_capturing || _readInFlight)
        {
            return;
        }

        var sequence = WinApi.GetClipboardSequenceNumber();
        if (sequence == _capturedSequence)
        {
            return;
        }

        _readInFlight = true;
        _capturedSequence = sequence;
        try
        {
            var includeImagesFiles = context.App.Get<bool>("clipboardHistoryIncludeImagesFiles");
            var owner = _lastOwner;
            _lastOwner = 0;
            var ownerPath = owner == 0 ? null : WinApi.GetWindowProcessPath(owner, out _);
            var recent = _tracker?.Recent(TimeSpan.FromSeconds(1)) ?? [];

            Captured? content;
            try
            {
                content = await Task.Run(() => ReadClipboard(includeImagesFiles));
            }
            catch (Exception ex)
            {
                context.Log.Warn($"Clipboard read failed: {ex.Message}");
                content = null;
            }

            if (_context is null || _store is null)
            {
                return;
            }

            _capturedSequence = sequence;
            if (content is null || content.Skip)
            {
                if (content is null)
                {
                    _store.LatestPasteboardEntryId = null;
                    _pill?.Refresh();
                }

                return;
            }

            // Who copied? The clipboard owner is the exact source; the apps that were in front just before are the fallback.
            var sources = new List<(string Exe, string? Path)>();
            if (!string.IsNullOrEmpty(ownerPath))
            {
                var exe = Path.GetFileName(ownerPath).ToLowerInvariant();
                if (!OwnProcess(ownerPath))
                {
                    _lastExternalSource = (exe, ownerPath);
                }

                sources.Add((exe, ownerPath));
            }

            sources.AddRange(recent);
            var ignored = context.App.Get<List<string>>("clipboardHistoryIgnoredApps") ?? [];
            if (sources.Any(s => Lists.Contains(ignored, s.Exe, s.Path)))
            {
                _store.LatestPasteboardEntryId = null;
                _pill?.Refresh();
                return;
            }

            (string Exe, string? Path)? source = !string.IsNullOrEmpty(ownerPath) && !OwnProcess(ownerPath)
                ? (Path.GetFileName(ownerPath).ToLowerInvariant(), ownerPath)
                : _lastExternalSource ?? (recent.Count > 0 ? recent[^1] : null);

            switch (content.Kind)
            {
                case ClipKinds.Files:
                    _store.PromoteFiles(content.Files!, source?.Exe, source?.Path, Limit, RetentionDays);
                    break;
                case ClipKinds.Image:
                    _store.PromoteImage(content.Png!, source?.Exe, source?.Path, Limit, RetentionDays);
                    break;
                default:
                    var text = content.Text!;
                    if (text.Length > ClipLimits.MaxCharacters)
                    {
                        return;
                    }

                    if (context.App.Get<bool>("clipboardHistoryFilterSensitiveText") && SensitiveText.Looks(text))
                    {
                        _store.LatestPasteboardEntryId = null;
                        _pill?.Refresh();
                        return;
                    }

                    _store.PromoteText(text, source?.Exe, source?.Path, Limit, RetentionDays);
                    break;
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not record clipboard content", ex);
        }
        finally
        {
            _readInFlight = false;
            if (_capturing && _debounce is not null && WinApi.GetClipboardSequenceNumber() != _capturedSequence)
            {
                // Something else was copied while this read was running.
                _debounce.Start();
            }
        }
    }

    private static bool OwnProcess(string path)
    {
        try
        {
            return string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Runs on a background thread. Returns null when nothing recordable is on the clipboard.</summary>
    private static Captured? ReadClipboard(bool includeImagesFiles)
    {
        if (ClipboardIO.IsOwnWrite())
        {
            return new Captured(ClipKinds.Text, null, null, null, Skip: true);
        }

        if (ClipboardIO.IsSensitive())
        {
            return null;
        }

        if (includeImagesFiles)
        {
            var files = ClipboardIO.ReadFiles(ClipLimits.MaxFiles + 1);
            if (files is { Count: > 0 and <= ClipLimits.MaxFiles })
            {
                return new Captured(ClipKinds.Files, null, null, files, false);
            }
        }

        var text = ClipboardIO.ReadText()?.Trim();
        if (includeImagesFiles)
        {
            var hasPng = ClipboardIO.IsAvailable(ClipFormats.Png);
            var hasDib = ClipboardIO.IsAvailable(ClipFormats.DibV5) || ClipboardIO.IsAvailable(ClipFormats.Dib);
            if (hasPng || (hasDib && string.IsNullOrEmpty(text)))
            {
                var png = ClipboardIO.ReadImagePng();
                if (png is { Length: > 0 })
                {
                    return new Captured(ClipKinds.Image, null, png, null, false);
                }
            }
        }

        return string.IsNullOrEmpty(text) ? null : new Captured(ClipKinds.Text, text, null, null, false);
    }

    // ---------------------------------------------------------------------------------------------- window

    public void ShowWindow()
    {
        var context = _context;
        if (context is null || _store is null)
        {
            return;
        }

        try
        {
            _pasteTarget = WinUtil.ForeignForeground();
            _window ??= new ClipboardQuickWindow(this);
            _window.Present();
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not show the clipboard history", ex);
        }
    }

    public void ToggleWindow()
    {
        if (_window is { IsVisible: true })
        {
            HideWindow();
        }
        else
        {
            ShowWindow();
        }
    }

    public void HideWindow() => _window?.Dismiss();

    // ---------------------------------------------------------------------------------------------- paste

    async void IClipboardHost.PasteEntries(IReadOnlyList<ClipEntry> entries, bool paste, Action<bool>? completed)
    {
        var context = _context;
        var store = _store;
        if (context is null || store is null || entries.Count == 0)
        {
            completed?.Invoke(false);
            return;
        }

        var target = _pasteTarget;
        if (paste)
        {
            _pasteTarget = 0;
            HideWindow();
        }

        try
        {
            var written = await Task.Run(() => WriteEntries(entries));
            if (!written)
            {
                completed?.Invoke(false);
                if (paste)
                {
                    context.Notifier.Show(Loc.T("Clipboard"), "Could not put the item on the clipboard.", NotificationLevel.Warning);
                }

                return;
            }

            store.Touch([.. entries.Select(e => e.Id)]);
            store.LatestPasteboardEntryId = entries.Count == 1 ? entries[0].Id : null;
            _capturedSequence = WinApi.GetClipboardSequenceNumber();
            _pill?.Refresh();
            completed?.Invoke(true);
            if (!paste)
            {
                return;
            }

            if (target == 0 || !WinApi.IsWindow(target))
            {
                context.Notifier.Show(Loc.T("Clipboard"), "Copied. Press Ctrl+V to paste.");
                return;
            }

            await ModifierHelper.ReleaseAsync();
            WinUtil.Activate(target);
            // Give the target window time to take the focus back before the keystroke arrives.
            for (var i = 0; i < 25 && NativeMethods.GetForegroundWindow() != target; i++)
            {
                await Task.Delay(15);
            }

            await Task.Delay(60);
            InputSender.SendChord((ushort)VK_CONTROL, (ushort)'V');
        }
        catch (Exception ex)
        {
            context.Log.Error("Pasting from clipboard history failed", ex);
            completed?.Invoke(false);
        }
    }

    /// <summary>Resolves what to put on the clipboard (aborting with the clipboard untouched when a payload is gone) and writes it.</summary>
    private bool WriteEntries(IReadOnlyList<ClipEntry> list)
    {
        var store = _store!;
        if (list.Count == 1)
        {
            var entry = list[0];
            switch (entry.Kind)
            {
                case ClipKinds.Text:
                    return ClipboardIO.WriteText(entry.Text);
                case ClipKinds.Image:
                    var path = store.ImagePath(entry);
                    return File.Exists(path) && ClipboardIO.WriteImage(File.ReadAllBytes(path));
                default:
                    var existing = entry.FilePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                    return existing.Count > 0 && ClipboardIO.WriteFiles(existing);
            }
        }

        if (list.All(e => e.IsFiles))
        {
            var paths = list.SelectMany(e => e.FilePaths).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            return paths.Count > 0 && ClipboardIO.WriteFiles(paths);
        }

        if (list.Any(e => e.IsImage))
        {
            var plain = new StringBuilder();
            var html = new StringBuilder();
            byte[]? firstPng = null;
            foreach (var entry in list)
            {
                switch (entry.Kind)
                {
                    case ClipKinds.Image:
                        var path = store.ImagePath(entry);
                        if (!File.Exists(path))
                        {
                            return false; // a missing payload aborts the whole write
                        }

                        var png = File.ReadAllBytes(path);
                        firstPng ??= png;
                        html.Append("<p><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(png)).Append("\"></p>");
                        break;
                    default:
                        var text = entry.IsFiles ? string.Join('\n', entry.FilePaths) : entry.Text;
                        if (plain.Length > 0)
                        {
                            plain.Append('\n');
                        }

                        plain.Append(text);
                        html.Append("<p style=\"white-space:pre-wrap\">").Append(System.Net.WebUtility.HtmlEncode(text)).Append("</p>");
                        break;
                }
            }

            return ClipboardIO.WriteRich(plain.ToString(), html.ToString(), plain.Length == 0 ? firstPng : null);
        }

        var combined = string.Join('\n', list.Select(e => e.IsFiles ? string.Join('\n', e.FilePaths) : e.Text));
        return ClipboardIO.WriteText(combined);
    }

    // ---------------------------------------------------------------------------------------------- image actions

    private FeatureAction? FindScreenshotAction(string titlePart)
    {
        var context = _context;
        if (context is null || !context.Runtime.IsAvailable("screenshot"))
        {
            return null;
        }

        try
        {
            foreach (var (feature, action) in context.Actions.All)
            {
                if (feature.CatalogId == "screenshot" && action.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }
            }
        }
        catch (Exception)
        {
            // not available yet
        }

        return null;
    }

    void IClipboardHost.EditImage(ClipEntry entry)
    {
        var editor = _context?.Services.Get<IScreenshotEditorService>();
        var action = editor is null ? FindScreenshotAction("clipboard image") ?? FindScreenshotAction("edit") : null;
        var path = _store is null ? null : _store.ImagePath(entry);
        if ((editor is null && action is null) || path is null || !File.Exists(path) || !ClipboardIO.WriteImage(File.ReadAllBytes(path)))
        {
            return;
        }

        HideWindow();
        _context?.Ui.BeginInvoke(() =>
        {
            if (editor is not null)
            {
                editor.EditClipboardImage();
            }
            else
            {
                action!.Invoke();
            }
        }, DispatcherPriority.Background);
    }

    async Task<bool> IClipboardHost.CopyImageText(ClipEntry entry)
    {
        var path = _store?.ImagePath(entry);
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = await ImageOcr.RecognizeAsync(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ClipboardIO.WriteText(text);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ClearUnpinnedWithConfirmation()
    {
        var store = _store;
        if (store is null)
        {
            return;
        }

        var snapshot = store.RecentSnapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            Strings.Clipboard.ClearRecentConfirmMessage,
            string.Format(Strings.Clipboard.ClearRecentConfirmFormat, snapshot.Count),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
        {
            store.ClearRecent(snapshot);
        }
    }
}

/// <summary>Windows OCR for a stored image (Copy text in the preview).</summary>
internal static class ImageOcr
{
    public static async Task<string?> RecognizeAsync(string path)
    {
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return null;
        }

        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(bitmap);
        return result.Text;
    }
}
