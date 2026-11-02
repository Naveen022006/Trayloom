// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Core;

// Well-known cross-project services. The interfaces live here; the owning feature project publishes the implementation
// in Start() (context.Services.Provide<T>(this)) and withdraws it in Stop(). Consumers call context.Services.Get<T>() and
// fall back gracefully (hide the row, show an empty state) when it is null because the owning feature is not installed.

/// <summary>One entry of the clipboard history (owned by the Clipboard feature).</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Kind"><c>text</c>, <c>image</c> or <c>files</c>.</param>
/// <param name="Text">The text (text entries).</param>
/// <param name="Files">The paths (file entries).</param>
/// <param name="ImagePath">Stored PNG (image entries).</param>
/// <param name="CopiedAt">Local time of the last copy.</param>
/// <param name="Pinned">Pinned entries sort first.</param>
/// <param name="SourceExe">Executable name of the app the content was copied from.</param>
/// <param name="Width">Image width in pixels (image entries).</param>
/// <param name="Height">Image height in pixels (image entries).</param>
public sealed record ClipboardHistoryItem(string Id, string Kind, string Text, IReadOnlyList<string> Files, string? ImagePath, DateTime CopiedAt, bool Pinned, string? SourceExe, int Width, int Height)
{
    /// <summary>True for image entries.</summary>
    public bool IsImage => Kind == "image";

    /// <summary>True for file entries.</summary>
    public bool IsFiles => Kind == "files";
}

/// <summary>Clipboard history (Files project). Consumers: Dynamic Island clipboard page, Command Bar "Paste from history".</summary>
public interface IClipboardHistoryService
{
    /// <summary>True while the history records new copies.</summary>
    bool IsRecording { get; }

    /// <summary>Entries: pinned first, then newest first.</summary>
    IReadOnlyList<ClipboardHistoryItem> Items { get; }

    /// <summary>Raised (UI thread) when the entries changed.</summary>
    event Action? Changed;

    /// <summary>Puts the entry on the system clipboard (touches it so it moves to the top).</summary>
    void Copy(string id);

    /// <summary>Shows the clipboard quick window.</summary>
    void Open();

    /// <summary>Removes every unpinned entry (no confirmation).</summary>
    void ClearUnpinned();

    /// <summary>Opens the screenshot editor on an image entry (no-op when the Screenshot feature is missing).</summary>
    void EditImage(string id);
}

/// <summary>One text snippet.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Title shown in lists.</param>
/// <param name="Text">The expansion.</param>
public sealed record SnippetInfo(string Id, string Name, string Text);

/// <summary>Text snippets (Input project). Consumer: Command Bar "Insert snippet".</summary>
public interface ISnippetsService
{
    /// <summary>The enabled snippets.</summary>
    IReadOnlyList<SnippetInfo> Snippets { get; }

    /// <summary>Types the snippet at the caret of the window that is in front (expanding its placeholders).</summary>
    bool Insert(string id);
}

/// <summary>One pad of the scratchpad.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Tab title.</param>
/// <param name="Text">The note.</param>
/// <param name="Updated">Last edit (local time).</param>
public sealed record ScratchpadPadInfo(string Id, string Name, string Text, DateTime Updated);

/// <summary>Scratchpad documents (Tools project owns the storage). Consumer: Dynamic Island Scratchpad page.</summary>
public interface IScratchpadService
{
    /// <summary>The pads in tab order.</summary>
    IReadOnlyList<ScratchpadPadInfo> Pads { get; }

    /// <summary>Id of the selected pad.</summary>
    string? SelectedId { get; }

    /// <summary>Selects a pad.</summary>
    void Select(string id);

    /// <summary>Replaces the text of a pad (saved immediately).</summary>
    void SetText(string id, string text);

    /// <summary>Creates a pad and returns its id.</summary>
    string AddPad();

    /// <summary>Raised when pads or the selection changed (any thread).</summary>
    event Action? Changed;

    /// <summary>
    /// Replaces the whole document (pads in order, at most 12; ids of existing pads keep their identity) and saves it.
    /// Used by the Dynamic Island page, which edits a snapshot.
    /// </summary>
    void Replace(IReadOnlyList<ScratchpadPadInfo> pads, string? selectedId);

    /// <summary>Opens the scratchpad window.</summary>
    void Open();
}

/// <summary>Screen recording state (Capture project). Consumers: Quick panel recording tile, Utilities rows.</summary>
public interface IRecordingState
{
    /// <summary>True while recording.</summary>
    bool IsRecording { get; }

    /// <summary>Elapsed recording time.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Starts or stops recording.</summary>
    void Toggle();

    /// <summary>Raised when the state changed (any thread).</summary>
    event Action? Changed;
}

/// <summary>A running process offered by <see cref="IProcessKillService"/>.</summary>
/// <param name="Pid">Process id.</param>
/// <param name="Name">Process name.</param>
/// <param name="Title">Main window title (may be empty).</param>
/// <param name="MemoryBytes">Working set.</param>
public sealed record ProcessEntry(int Pid, string Name, string? Title, long MemoryBytes);

/// <summary>Kill process (Maintenance project). Consumer: Command Bar "kill" browse and <c>kill.&lt;pid&gt;</c> entries.</summary>
public interface IProcessKillService
{
    /// <summary>Running user processes matching <paramref name="query"/> (empty = all), biggest first. Never lists protected processes.</summary>
    IReadOnlyList<ProcessEntry> Browse(string query, int max);

    /// <summary>Ends the process (the host may ask for confirmation first). Returns false when refused or failed.</summary>
    bool Kill(int pid);
}

/// <summary>Uninstaller (Maintenance project). Consumer: Command Bar "Uninstall…" entries.</summary>
public interface IUninstallService
{
    /// <summary>Opens the uninstaller and starts the leftovers scan for the program at <paramref name="path"/> (exe or install folder).</summary>
    Task SelectAsync(string path);
}

/// <summary>Media tools (Maintenance project). Consumers: Command Bar, island Files page ("Media").</summary>
public interface IMediaToolsService
{
    /// <summary>Opens Media with these files loaded.</summary>
    void OpenWith(IReadOnlyList<string> paths);
}

/// <summary>Whether the tray shows live metrics next to the icon (System project). The App hides its own icon accordingly (<c>menuBarHideIconWithMetrics</c>).</summary>
public interface ITrayMetricsState
{
    /// <summary>True while at least one metric item is shown in the tray.</summary>
    bool HasMetrics { get; }

    /// <summary>Raised when <see cref="HasMetrics"/> changed (any thread).</summary>
    event Action? Changed;
}

/// <summary>Screenshot editing entry points (Capture project). Consumer: clipboard history "Edit" button.</summary>
public interface IScreenshotEditorService
{
    /// <summary>Opens the editor on the image currently on the clipboard.</summary>
    void EditClipboardImage();

    /// <summary>Opens the editor on an image file.</summary>
    void EditFile(string path);
}
