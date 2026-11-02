// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Input;

/// <summary>
/// Text snippets: typing a trigger such as <c>;sig</c> replaces it with the stored text. The keyboard hook keeps a short
/// in-memory buffer (64 characters) of what was typed since the last delimiter, click, focus change or shortcut; it is
/// never logged or saved. Each snippet expands "Right away" (the last trigger key is swallowed) or "After space, Tab or
/// Return" (the delimiter is held back and typed after the replacement). The longest matching trigger wins; case is
/// ignored only for snippets that ask for it. A worker thread erases the typed trigger with an exact number of
/// Backspaces and inserts the text (typed, or pasted for long / multi-line text) after expanding the variables
/// <c>{{date}}</c>, <c>{{time}}</c>, <c>{{datetime}}</c> and <c>{{clipboard}}</c>. An optional sound plays afterwards.
/// The second part of the feature is the Quick snippet menu (<see cref="SnippetLibraryWindow"/>), opened by the
/// <c>snippetLibrary</c> shortcut role.
/// </summary>
public sealed class TextSnippetsFeature : InputFeatureBase, ISnippetsService
{
    private const int BufferSize = 64;

    private readonly char[] _buffer = new char[BufferSize];
    private readonly char[] _chars = new char[8];
    private readonly byte[] _state = new byte[256];
    private int _length;
    private nint _lastForeground;
    private bool _caps;
    private int _swallowUpVk = -1;

    private volatile SnippetItem[] _immediate = [];
    private volatile SnippetItem[] _delimited = [];
    private volatile bool _sound;
    private volatile string _soundName = string.Empty;

    private BlockingCollection<Job>? _jobs;
    private Thread? _worker;
    private IDisposable? _keySub;
    private IDisposable? _mouseSub;
    private IDisposable? _libraryBind;
    private SnippetLibraryWindow? _library;

    private readonly record struct Job(int Backspaces, string Text, string? Trailing, bool Expansion);

    public override string Id => "text-snippets";

    // ---------------------------------------------------------------------------------------------- ISnippetsService

    IReadOnlyList<SnippetInfo> ISnippetsService.Snippets =>
        Context is null ? [] : [.. SnippetStore.Load(App).Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.DisplayTitle)).Select(s => new SnippetInfo(s.Id, s.DisplayTitle, s.Replacement))];

    bool ISnippetsService.Insert(string id)
    {
        var context = Context;
        if (context is null || SnippetStore.Load(App).FirstOrDefault(s => s.Id == id) is not { } snippet)
        {
            return false;
        }

        var text = SnippetVariables.Expand(snippet.Replacement, () => SnippetTyper.ClipboardText(context.Ui));
        SnippetTyper.Insert(text, context.Ui, context.Log);
        return true;
    }

    protected override void OnInstalled() => Context?.Services.Provide<ISnippetsService>(this);

    public override void Stop()
    {
        Context?.Services.Withdraw<ISnippetsService>(this);
        base.Stop();
    }

    protected override bool IsEnabled() => App.Get<bool>(K.SnippetsEnabled) || App.Get<bool>(K.LibraryEnabled);

    protected override IEnumerable<FeatureAction> ExtraActions
    {
        get
        {
            if (Context is null || !App.Get<bool>(K.LibraryEnabled))
            {
                return [];
            }

            return [new FeatureAction("snippetLibrary", Strings.QuickSnippetMenu, () => ShowLibrary(), "")];
        }
    }

    protected override void Engage()
    {
        _jobs = new BlockingCollection<Job>();
        var jobs = _jobs;
        _worker = new Thread(() => Work(jobs)) { IsBackground = true, Name = "WinKnife.Snippets" };
        _worker.Start();
        SyncParts();
    }

    protected override void Disengage()
    {
        _keySub?.Dispose();
        _mouseSub?.Dispose();
        _libraryBind?.Dispose();
        _keySub = _mouseSub = _libraryBind = null;
        _jobs?.CompleteAdding();
        _worker?.Join(1000);
        _worker = null;
        _jobs = null;
        _length = 0;
        var library = _library;
        _library = null;
        if (library is not null)
        {
            Context?.Ui.BeginInvoke(library.Close);
        }
    }

    protected override void OnSettingChanged(string key)
    {
        if (key is K.SnippetsEnabled or K.LibraryEnabled)
        {
            SyncParts();
        }
    }

    protected override void LoadSettings()
    {
        var items = App.Get<bool>(K.SnippetsEnabled)
            ? SnippetStore.Load(App).Where(x => x.Enabled && x.Trigger.Length >= SnippetStore.MinTrigger && x.Trigger.Length <= SnippetStore.MaxTrigger && x.Replacement.Length > 0).ToList()
            : [];
        _immediate = [.. items.Where(x => x.IsImmediate).OrderByDescending(x => x.Trigger.Length)];
        _delimited = [.. items.Where(x => !x.IsImmediate).OrderByDescending(x => x.Trigger.Length)];
        _sound = App.Get<bool>(K.SoundEnabled);
        _soundName = App.Get<string>(K.SoundName) ?? string.Empty;
    }

    // Hooks only while "Expand snippets while typing" is on; the shortcut only while the quick menu is on.
    private void SyncParts()
    {
        if (Context is null)
        {
            return;
        }

        var expand = App.Get<bool>(K.SnippetsEnabled);
        if (expand && _keySub is null)
        {
            _length = 0;
            _swallowUpVk = -1;
            _caps = (NativeMethods.GetKeyState(Win32.VK_CAPITAL) & 1) != 0;
            _keySub = Context.Hooks.SubscribeKeyboard(OnKey, HookPriorities.TextSnippets);
            _mouseSub = Context.Hooks.SubscribeMouse(OnMouse, HookPriorities.SnippetMouse);
        }
        else if (!expand && _keySub is not null)
        {
            _keySub.Dispose();
            _mouseSub?.Dispose();
            _keySub = _mouseSub = null;
            _length = 0;
        }

        var library = App.Get<bool>(K.LibraryEnabled);
        if (library && _libraryBind is null)
        {
            _libraryBind = Context.Shortcuts.Bind("snippetLibrary", () => ShowLibrary());
        }
        else if (!library && _libraryBind is not null)
        {
            _libraryBind.Dispose();
            _libraryBind = null;
        }
    }

    // ------------------------------------------------------------------------------------------ quick menu

    /// <summary>Opens the Quick snippet menu (UI thread).</summary>
    public void ShowLibrary()
    {
        var context = Context;
        if (context is null)
        {
            return;
        }

        context.Ui.BeginInvoke(() =>
        {
            if (_library is { IsVisible: true })
            {
                _library.Activate();
                return;
            }

            var previous = NativeMethods.GetForegroundWindow();
            _library = new SnippetLibraryWindow(context, previous, snippet => InsertFromMenu(snippet, previous));
            _library.Show();
        });
    }

    private void InsertFromMenu(SnippetItem snippet, nint previous)
    {
        var context = Context;
        if (context is null)
        {
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try
            {
                if (previous != 0 && NativeMethods.GetForegroundWindow() != previous)
                {
                    NativeMethods.SetForegroundWindow(previous);
                    for (var i = 0; i < 20 && NativeMethods.GetForegroundWindow() != previous; i++)
                    {
                        Thread.Sleep(15);
                    }
                }

                SnippetTyper.WaitForModifiersReleased();
                var text = SnippetVariables.Expand(snippet.Replacement, () => SnippetTyper.ClipboardText(context.Ui));
                SnippetTyper.Insert(text, context.Ui, context.Log);
            }
            catch (Exception ex)
            {
                context.Log.Error("Inserting a snippet failed", ex);
            }
        }, null);
    }

    // ------------------------------------------------------------------------------------------ expansion

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsButtonDown)
        {
            _length = 0;
        }

        return false;
    }

    private static bool IsDelimiter(int vk) => vk is 0x20 or 0x09 or 0x0D;

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        if (e.IsKeyUp)
        {
            if (_swallowUpVk == e.VkCode)
            {
                _swallowUpVk = -1;
                return true;
            }

            return false;
        }

        var vk = e.VkCode;
        if (vk == Win32.VK_CAPITAL)
        {
            _caps = !_caps;
            return false;
        }

        // Pure modifier / lock keys do not affect the typed text.
        if (vk is >= 0x10 and <= 0x12 or 0x13 or 0x5B or 0x5C or 0x90 or 0x91 or >= 0xA0 and <= 0xA5)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _length = 0;
        }

        var mods = e.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        if (mods.HasFlag(ModifierKeys.Windows) || ctrl != alt)
        {
            _length = 0; // shortcut, not text (Ctrl+Alt together is AltGr, which types characters)
            return false;
        }

        if (vk == Win32.VK_BACK)
        {
            if (_length > 0)
            {
                _length--;
            }

            return false;
        }

        if (IsDelimiter(vk))
        {
            return OnDelimiter(vk, foreground);
        }

        if (!TryTranslate(e, foreground, ctrl && alt, out var ch))
        {
            return false;
        }

        if (ch == '\0')
        {
            _length = 0;
            return false;
        }

        if (_length == BufferSize)
        {
            Array.Copy(_buffer, BufferSize / 2, _buffer, 0, BufferSize / 2);
            _length = BufferSize / 2;
        }

        _buffer[_length++] = ch;

        var snippets = _immediate;
        if (snippets.Length == 0 || IsPasswordField(foreground))
        {
            return false;
        }

        var typed = new ReadOnlySpan<char>(_buffer, 0, _length);
        foreach (var snippet in snippets)
        {
            if (!EndsWith(typed, snippet))
            {
                continue;
            }

            // The earlier characters of the trigger already reached the app; the last one is held back.
            _length = 0;
            _swallowUpVk = vk;
            _jobs?.Add(new Job(snippet.Trigger.Length - 1, snippet.Replacement, null, true));
            return true;
        }

        return false;
    }

    private bool OnDelimiter(int vk, nint foreground)
    {
        var typed = new ReadOnlySpan<char>(_buffer, 0, _length);
        var snippets = _delimited;
        SnippetItem? match = null;
        if (snippets.Length > 0 && _length > 0 && !IsPasswordField(foreground))
        {
            foreach (var snippet in snippets)
            {
                if (EndsWith(typed, snippet))
                {
                    match = snippet;
                    break;
                }
            }
        }

        _length = 0;
        if (match is null)
        {
            return false;
        }

        // Hold the delimiter back: erase the trigger, insert the text, then type the delimiter after it.
        _swallowUpVk = vk;
        var trailing = vk switch { 0x09 => "\t", 0x0D => "\n", _ => " " };
        _jobs?.Add(new Job(match.Trigger.Length, match.Replacement, trailing, true));
        return true;
    }

    private static bool EndsWith(ReadOnlySpan<char> typed, SnippetItem snippet)
    {
        var trigger = snippet.Trigger;
        if (trigger.Length > typed.Length)
        {
            return false;
        }

        var comparison = snippet.IgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return MemoryExtensions.Equals(typed[^trigger.Length..], trigger, comparison);
    }

    // Returns false when the key is a dead key (nothing to record, buffer unchanged); ch == '\0' means the key
    // produced no text and the buffer must be cleared.
    private bool TryTranslate(KeyboardHookEvent e, nint foreground, bool altGr, out char ch)
    {
        ch = '\0';
        Array.Clear(_state);
        var mods = e.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift))
        {
            _state[0x10] = 0x80;
        }

        if (altGr)
        {
            _state[0x11] = 0x80;
            _state[0x12] = 0x80;
            _state[0xA2] = 0x80;
            _state[0xA5] = 0x80;
        }

        if (_caps)
        {
            _state[Win32.VK_CAPITAL] = 1;
        }

        var thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var layout = Win32.GetKeyboardLayout(thread);
        // Flag 4: do not change the keyboard's dead-key state.
        var count = Win32.ToUnicodeEx((uint)e.VkCode, (uint)e.ScanCode, _state, _chars, _chars.Length, 4, layout);
        if (count < 0)
        {
            return false; // dead key
        }

        if (count >= 1 && !char.IsControl(_chars[count - 1]))
        {
            ch = _chars[count - 1];
        }

        return true;
    }

    private static bool IsPasswordField(nint foreground)
    {
        var thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!Win32.GetGUIThreadInfo(thread, ref info) || info.hwndFocus == 0)
        {
            return false;
        }

        // Classic Edit controls flag password boxes with ES_PASSWORD (0x20); other toolkits are not detectable here.
        var style = NativeMethods.GetWindowLongPtr(info.hwndFocus, Win32.GWL_STYLE).ToInt64();
        return (style & 0x20) != 0 && Win32.ClassName(info.hwndFocus).Equals("Edit", StringComparison.OrdinalIgnoreCase);
    }

    private void Work(BlockingCollection<Job> jobs)
    {
        try
        {
            foreach (var job in jobs.GetConsumingEnumerable())
            {
                try
                {
                    var context = Context;
                    if (context is null)
                    {
                        continue;
                    }

                    SnippetTyper.WaitForModifiersReleased();
                    var text = SnippetVariables.Expand(job.Text, () => SnippetTyper.ClipboardText(context.Ui));
                    SnippetTyper.Backspaces(job.Backspaces);
                    SnippetTyper.Insert(text, context.Ui, context.Log);
                    if (job.Trailing is { } trailing)
                    {
                        if (trailing == "\n")
                        {
                            KeySender.Tap((ushort)Win32.VK_RETURN);
                        }
                        else if (trailing == "\t")
                        {
                            KeySender.Tap((ushort)Win32.VK_TAB);
                        }
                        else
                        {
                            InputSender.SendText(trailing);
                        }
                    }

                    if (_sound)
                    {
                        SnippetSounds.Play(_soundName);
                    }
                }
                catch (Exception ex)
                {
                    Context?.Log.Error("Expanding a snippet failed", ex);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // collection completed
        }
    }
}

/// <summary>System sounds for "Play a sound when a typed trigger expands" (the .wav files of the Windows Media folder).</summary>
internal static class SnippetSounds
{
    private static string MediaFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>Names (file names without extension) of the available sounds, sorted.</summary>
    public static List<string> Names()
    {
        try
        {
            if (Directory.Exists(MediaFolder))
            {
                return [.. Directory.EnumerateFiles(MediaFolder, "*.wav").Select(System.IO.Path.GetFileNameWithoutExtension).Where(x => x is not null).Select(x => x!).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)];
            }
        }
        catch
        {
            // fall through
        }

        return [];
    }

    /// <summary>The default sound: the first of the quiet notification sounds that exists.</summary>
    public static string DefaultName()
    {
        var names = Names();
        foreach (var preferred in new[] { "Windows Notify System Generic", "Windows Ding", "Windows Notify" })
        {
            if (names.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }

        return names.FirstOrDefault() ?? string.Empty;
    }

    public static bool Exists(string name) => name.Length > 0 && File.Exists(System.IO.Path.Combine(MediaFolder, name + ".wav"));

    public static void Play(string name)
    {
        try
        {
            if (!Exists(name))
            {
                name = DefaultName();
            }

            if (name.Length == 0)
            {
                return;
            }

            using var player = new System.Media.SoundPlayer(System.IO.Path.Combine(MediaFolder, name + ".wav"));
            player.PlaySync();
        }
        catch
        {
            // a missing sound is not an error
        }
    }
}
