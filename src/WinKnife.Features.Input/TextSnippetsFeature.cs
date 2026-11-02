// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>One trigger -> expansion pair stored in the feature settings.</summary>
public sealed class Snippet
{
    public string Trigger { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Text expander: typing a trigger such as <c>;sig</c> replaces it with the stored text. The keyboard hook keeps a short
/// in-memory buffer of the characters typed since the last cursor/mouse/shortcut interruption (never logged or saved,
/// cleared on window changes, clicks, navigation keys, shortcuts and password boxes). When the buffer ends with a
/// trigger, the last key is swallowed and a worker thread sends Backspaces for the characters that already reached
/// the app, then types the expansion as Unicode key events (line breaks as Enter).
/// </summary>
public sealed class TextSnippetsFeature : InputFeatureBase
{
    private const int BufferSize = 64;

    private static readonly Snippet[] Defaults =
    [
        new() { Trigger = ";sig", Text = "Best regards," },
        new() { Trigger = ";shrug", Text = "¯\\_(ツ)_/¯" },
    ];

    private readonly char[] _buffer = new char[BufferSize];
    private readonly char[] _chars = new char[8];
    private readonly byte[] _state = new byte[256];
    private int _length;
    private nint _lastForeground;
    private bool _caps;
    private int _swallowUpVk = -1;

    private volatile Snippet[] _snippets = [];
    private volatile bool _ignoreCase;
    private volatile bool _needBoundary;

    private BlockingCollection<(int Backspaces, string Text)>? _jobs;
    private Thread? _worker;

    public override string Id => "text-snippets";

    public override string Name => "Text snippets";

    public override string Description => "Type a short trigger like ;sig and have it expand into longer text anywhere.";

    protected override void OnStart()
    {
        _length = 0;
        _swallowUpVk = -1;
        _caps = (NativeMethods.GetKeyState(Win32.VK_CAPITAL) & 1) != 0;
        _jobs = new BlockingCollection<(int, string)>();
        var jobs = _jobs;
        _worker = new Thread(() => Work(jobs)) { IsBackground = true, Name = "WinKnife.Snippets" };
        _worker.Start();
        SubscribeKeyboard(OnKey, HookPriorities.TextSnippets);
        SubscribeMouse(OnMouse, HookPriorities.SnippetMouse);
    }

    protected override void OnStop()
    {
        _jobs?.CompleteAdding();
        _worker?.Join(1000);
        _worker = null;
        _jobs = null;
        _length = 0;
    }

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        var s = Context.Settings;
        _ignoreCase = s.Get("ignoreCase", false);
        _needBoundary = s.Get("needBoundary", false);
        var stored = s.Get<List<Snippet>?>("snippets", null) ?? [.. Defaults];
        _snippets =
        [
            .. stored
                .Where(x => x is { Trigger.Length: >= 2 and <= 32 } && !string.IsNullOrEmpty(x.Text) && x.Trigger.All(c => !char.IsControl(c)))
                .GroupBy(x => x.Trigger, _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderByDescending(x => x.Trigger.Length),
        ];
    }

    protected override void BuildSettings(SettingsViewBuilder builder)
    {
        builder
            .AddToggle("ignoreCase", "Ignore capitalisation of triggers", false)
            .AddToggle("needBoundary", "Only expand at the start of a word", false,
                "The trigger must not directly follow a letter or digit.")
            .AddNote("Triggers need 2 to 32 characters; start them with a symbol such as ; or / so normal typing never matches. Expansions are typed as text (line breaks become Enter).")
            .Add(new SnippetEditor(Context!.Settings, [.. Defaults]));
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsButtonDown)
        {
            _length = 0;
        }

        return false;
    }

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

        if (Paused)
        {
            _length = 0;
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

        var snippets = _snippets;
        if (snippets.Length == 0 || IsPasswordField(foreground))
        {
            return false;
        }

        var typed = new ReadOnlySpan<char>(_buffer, 0, _length);
        var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var snippet in snippets)
        {
            var trigger = snippet.Trigger;
            if (trigger.Length > typed.Length || !MemoryExtensions.Equals(typed[^trigger.Length..], trigger, comparison))
            {
                continue;
            }

            if (_needBoundary && typed.Length > trigger.Length && char.IsLetterOrDigit(typed[typed.Length - trigger.Length - 1]))
            {
                continue;
            }

            // The earlier characters of the trigger already reached the app; the last one is held back.
            _length = 0;
            _swallowUpVk = vk;
            _jobs?.Add((trigger.Length - 1, snippet.Text));
            return true;
        }

        return false;
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

        // Classic Edit controls flag password boxes with ES_PASSWORD (0x20); other toolkits are not detectable.
        var style = NativeMethods.GetWindowLongPtr(info.hwndFocus, Win32.GWL_STYLE).ToInt64();
        return (style & 0x20) != 0 && Win32.ClassName(info.hwndFocus).Equals("Edit", StringComparison.OrdinalIgnoreCase);
    }

    private void Work(BlockingCollection<(int Backspaces, string Text)> jobs)
    {
        try
        {
            foreach (var (backspaces, text) in jobs.GetConsumingEnumerable())
            {
                try
                {
                    for (var i = 0; i < backspaces; i++)
                    {
                        KeySender.Tap(Win32.VK_BACK);
                    }

                    KeySender.TypeText(text);
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

    /// <summary>Settings editor: one row per snippet with trigger, text and a remove button, plus an add button.</summary>
    private sealed class SnippetEditor : StackPanel
    {
        private readonly IFeatureSettings _settings;
        private readonly StackPanel _rows = new();
        private bool _loading;

        public SnippetEditor(IFeatureSettings settings, List<Snippet> defaults)
        {
            _settings = settings;
            Margin = new Thickness(0, 8, 0, 0);

            _loading = true;
            foreach (var snippet in settings.Get<List<Snippet>?>("snippets", null) ?? defaults)
            {
                AddRow(snippet.Trigger, snippet.Text);
            }

            _loading = false;

            Children.Add(_rows);
            var add = new Button { Content = "Add snippet", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(14, 6, 14, 6) };
            add.SetResourceReference(StyleProperty, ThemeKeys.AccentButtonStyle);
            add.Click += (_, _) =>
            {
                AddRow(string.Empty, string.Empty);
                Save();
            };
            Children.Add(add);
        }

        private void AddRow(string trigger, string text)
        {
            var triggerBox = new TextBox { Text = trigger, Width = 110, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Trigger, e.g. ;sig" };
            var textBox = new TextBox
            {
                Text = text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 30,
                MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(8, 0, 8, 0),
                ToolTip = "Text to type",
            };
            var remove = new Button
            {
                Content = "",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Width = 32,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Remove",
            };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textBox, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(triggerBox);
            grid.Children.Add(textBox);
            grid.Children.Add(remove);

            triggerBox.TextChanged += (_, _) => Save();
            textBox.TextChanged += (_, _) => Save();
            remove.Click += (_, _) =>
            {
                _rows.Children.Remove(grid);
                Save();
            };

            _rows.Children.Add(grid);
        }

        private void Save()
        {
            if (_loading)
            {
                return;
            }

            var list = new List<Snippet>();
            foreach (Grid row in _rows.Children)
            {
                list.Add(new Snippet { Trigger = ((TextBox)row.Children[0]).Text, Text = ((TextBox)row.Children[1]).Text.Replace("\r\n", "\n") });
            }

            _settings.Set("snippets", list);
        }
    }
}
