// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Tools;

/// <summary>Persisted state of one note (position and size are physical pixels).</summary>
public sealed class NoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = string.Empty;

    public int X { get; set; } = int.MinValue;

    public int Y { get; set; } = int.MinValue;

    public int Width { get; set; } = 360;

    public int Height { get; set; } = 300;

    public bool OnTop { get; set; } = true;
}

/// <summary>Floating always-on-top note pads that autosave to disk.</summary>
public sealed class ScratchpadFeature : ToolFeatureBase
{
    private readonly List<NoteWindow> _windows = new();
    private List<NoteData> _notes = new();
    private DispatcherTimer? _saveTimer;
    private string NotesFile => Path.Combine(Ctx.DataDirectory, "notes.json");

    public override string Id => "scratchpad";

    public override string Name => "Scratchpad";

    public override string Description => "Keep quick notes in floating pads that stay on top and save themselves.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", "Show or hide scratchpads", () => Safe(ToggleAll), ""),
        new FeatureAction("new", "New scratchpad", () => Safe(() => NewNote(true)), ""),
    ];

    protected override void OnStart()
    {
        Load();
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => Flush(), Ctx.Ui);
        BindHotkey("hotkey", Hotkey.Parse("Ctrl+Alt+N"), ToggleAll);
        BindHotkey("hotkeyNew", null, () => NewNote(true));
    }

    protected override void OnStop()
    {
        Flush();
        _saveTimer?.Stop();
        _saveTimer = null;
        foreach (var w in _windows.ToList())
        {
            w.Dispose();
        }

        _windows.Clear();
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Show or hide all notes", Hotkey.Parse("Ctrl+Alt+N"), "Creates a first note when you have none.")
            .AddHotkey("hotkeyNew", "Create a new note", null)
            .AddNote("Notes are stored in " + Ctx.DataDirectory + " and survive restarts. Closing a note only hides it; use the trash button to delete one.");
        return builder.Build();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(NotesFile))
            {
                _notes = JsonSerializer.Deserialize<List<NoteData>>(File.ReadAllText(NotesFile)) ?? new();
            }
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn("Notes could not be read: " + ex.Message);
            _notes = new();
        }

        foreach (var note in _notes)
        {
            var window = new NoteWindow(note, this);
            _windows.Add(window);
        }
    }

    internal void ScheduleSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    internal void Flush()
    {
        _saveTimer?.Stop();
        try
        {
            foreach (var window in _windows)
            {
                window.Capture();
            }

            var tmp = NotesFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, NotesFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn("Notes could not be saved: " + ex.Message);
        }
    }

    private void ToggleAll()
    {
        if (_windows.Count == 0)
        {
            NewNote(true);
            return;
        }

        if (_windows.Any(w => w.IsVisible))
        {
            foreach (var w in _windows)
            {
                w.Hide();
            }

            Flush();
        }
        else
        {
            foreach (var w in _windows)
            {
                w.ShowNote();
            }
        }
    }

    internal void NewNote(bool focus)
    {
        var note = new NoteData();
        var area = Screens.UnderCursor();
        var offset = 36 * (_windows.Count % 8);
        note.X = (int)(area.WorkArea.X + area.WorkArea.Width / 2 - note.Width / 2 + offset);
        note.Y = (int)(area.WorkArea.Y + area.WorkArea.Height / 3 + offset);
        _notes.Add(note);
        var window = new NoteWindow(note, this);
        _windows.Add(window);
        window.ShowNote();
        if (focus)
        {
            window.FocusEditor();
        }

        ScheduleSave();
    }

    internal void DeleteNote(NoteWindow window)
    {
        _notes.Remove(window.Data);
        _windows.Remove(window);
        window.Dispose();
        Flush();
    }
}

internal sealed class NoteWindow : Window
{
    private readonly ScratchpadFeature _owner;
    private readonly TextBox _editor = new();
    private readonly Button _pin;
    private bool _disposed;
    private bool _placed;

    public NoteWindow(NoteData data, ScratchpadFeature owner)
    {
        Data = data;
        _owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = data.OnTop;
        Title = "Note";
        MinWidth = 220;
        MinHeight = 140;
        Width = 360;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);

        _editor.Text = data.Text;
        _editor.AcceptsReturn = true;
        _editor.AcceptsTab = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.BorderThickness = new Thickness(0);
        _editor.Background = Brushes.Transparent;
        _editor.Padding = new Thickness(12, 8, 12, 12);
        _editor.FontSize = 14;
        _editor.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        _editor.TextChanged += (_, _) =>
        {
            Data.Text = _editor.Text;
            owner.ScheduleSave();
        };

        _pin = HeaderButton(PinGlyph(), "Keep on top", TogglePin);
        var header = new Grid { Height = 32, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = UiKit.Label("Scratchpad", ThemeKeys.TextMuted, 12);
        grip.Margin = new Thickness(12, 0, 0, 0);
        header.Children.Add(grip);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(HeaderButton("", "New note", () => owner.NewNote(true)));
        buttons.Children.Add(_pin);
        buttons.Children.Add(HeaderButton("", "Delete this note", Delete));
        buttons.Children.Add(HeaderButton("", "Hide", () =>
        {
            Hide();
            owner.Flush();
        }));
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // mouse released during the call
                }
            }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_editor);
        var frame = new Border { BorderThickness = new Thickness(1), Child = layout };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        Content = frame;

        LocationChanged += (_, _) => owner.ScheduleSave();
        SizeChanged += (_, _) => owner.ScheduleSave();
        Closing += (s, e) =>
        {
            if (!_disposed)
            {
                e.Cancel = true;
                Hide();
                owner.Flush();
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Hide();
                owner.Flush();
            }
        };
    }

    public NoteData Data { get; }

    private string PinGlyph() => Topmost ? "" : "";

    private static Button HeaderButton(string glyph, string tooltip, Action click)
    {
        var button = new Button
        {
            Content = UiKit.Glyph(glyph, 12),
            ToolTip = tooltip,
            Width = 30,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void TogglePin()
    {
        Topmost = !Topmost;
        Data.OnTop = Topmost;
        _pin.Content = UiKit.Glyph(PinGlyph(), 12);
        _owner.ScheduleSave();
    }

    private void Delete()
    {
        if (_editor.Text.Length > 0 && MessageBox.Show(this, "Delete this note?", "Scratchpad", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _owner.DeleteNote(this);
    }

    public void ShowNote()
    {
        if (!_placed)
        {
            _placed = true;
            var handle = new WindowInteropHelper(this).EnsureHandle();
            if (Data.X != int.MinValue)
            {
                NativeMethods.SetWindowPos(handle, Topmost ? HWND_TOPMOST : 0, Data.X, Data.Y, Data.Width, Data.Height, SWP_NOACTIVATE);
            }
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    public void FocusEditor()
    {
        Activate();
        _editor.Focus();
    }

    /// <summary>Copies the current window rectangle into the data object.</summary>
    public void Capture()
    {
        if (!IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0 && NativeMethods.GetWindowRect(handle, out var r))
        {
            Data.X = r.Left;
            Data.Y = r.Top;
            Data.Width = r.Width;
            Data.Height = r.Height;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}
