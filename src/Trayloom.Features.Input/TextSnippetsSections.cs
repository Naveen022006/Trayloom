// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>The first card of the "Text snippets" page: expansion switch, sound.</summary>
public sealed class SnippetsGeneralSection : ISettingsSection
{
    public string PageId => SettingsPages.TextSnippets;

    public string? FeatureId => "text-snippets";

    public int Order => 0;

    public string? Title => Strings.SnippetsTitle;

    public IReadOnlyList<string> Keywords => ["Expand snippets while typing", "Play a sound when a typed trigger expands", "Sound", "text expander"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, null, Strings.ExpandWhileTyping, Strings.ExpandWhileTypingCaption, K.SnippetsEnabled));

        var sound = new StackPanel();
        sound.Children.Add(Rows.Toggle(app, null, Strings.PlaySound, Strings.PlaySoundCaption, K.SoundEnabled));
        var picker = live.Show(SoundPicker(app), () => app.Get<bool>(K.SoundEnabled));
        picker.Margin = new Thickness(0, 13, 0, 0);
        sound.Children.Add(picker);
        card.Add(live.Show(sound, () => app.Get<bool>(K.SnippetsEnabled)));
        return live.Attach(card);
    }

    private static FrameworkElement SoundPicker(IAppSettings app)
    {
        var names = SnippetSounds.Names();
        var stored = app.Get<string>(K.SoundName) ?? string.Empty;
        if (stored.Length == 0)
        {
            stored = SnippetSounds.DefaultName();
        }

        var items = names.Select(n => (n, n)).ToList();
        if (stored.Length > 0 && !names.Contains(stored, StringComparer.OrdinalIgnoreCase))
        {
            items.Add((stored, Strings.SoundUnavailable));
        }

        var combo = InputUi.Combo(items, stored, value =>
        {
            app.Set(K.SoundName, value);
            Task.Run(() => SnippetSounds.Play(value));
        }, 240);
        return InputUi.LabelLine(Strings.Sound, combo);
    }
}

/// <summary>The "Quick snippet menu" card: switch and shortcut.</summary>
public sealed class SnippetsQuickMenuSection : ISettingsSection
{
    public string PageId => SettingsPages.TextSnippets;

    public string? FeatureId => "text-snippets";

    public int Order => 10;

    public string? Title => Strings.QuickSnippetMenu;

    public IReadOnlyList<string> Keywords => ["Open snippets from a menu", "snippet library", "shortcut"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var live = new LiveView(c);
        var card = new SettingsCard();
        card.Add(Rows.Toggle(app, null, Strings.OpenFromMenu, Strings.OpenFromMenuCaption, K.LibraryEnabled));

        var block = new StackPanel();
        var error = UiKit.Text(string.Empty, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
        error.Margin = new Thickness(0, 6, 0, 0);
        error.Visibility = Visibility.Collapsed;

        var recorder = new HotkeyRecorder { Hotkey = c.Shortcuts.GetHotkey("snippetLibrary"), Width = 150, MinWidth = 150 };
        var reset = UiKit.Button(Strings.Reset, ButtonKind.Bordered, 12, null);
        void ShowProblem(string? text)
        {
            error.Text = text ?? string.Empty;
            error.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void Sync()
        {
            recorder.Hotkey = c.Shortcuts.GetHotkey("snippetLibrary");
            reset.IsEnabled = !c.Shortcuts.IsDefault("snippetLibrary");
            ShowProblem(c.Shortcuts.GetState("snippetLibrary").Problem);
        }

        recorder.HotkeyChanged += (_, hotkey) =>
        {
            var result = c.Shortcuts.TrySet("snippetLibrary", hotkey);
            if (!result.Ok)
            {
                recorder.Hotkey = c.Shortcuts.GetHotkey("snippetLibrary");
                ShowProblem(result.Error);
                return;
            }

            Sync();
        };
        reset.Click += (_, _) =>
        {
            c.Shortcuts.Reset("snippetLibrary");
            Sync();
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text(Strings.Shortcut, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        Grid.SetColumn(recorder, 1);
        row.Children.Add(recorder);
        reset.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(reset, 2);
        row.Children.Add(reset);
        block.Children.Add(row);
        block.Children.Add(error);
        Sync();
        c.Shortcuts.Changed += OnShortcutsChanged;
        void OnShortcutsChanged(string id)
        {
            if (id == "snippetLibrary")
            {
                c.Ui.BeginInvoke(Sync);
            }
        }

        card.Add(live.Show(block, () => app.Get<bool>(K.LibraryEnabled)));
        card.Unloaded += (_, _) => c.Shortcuts.Changed -= OnShortcutsChanged;
        return live.Attach(InputUi.FormSection(Strings.QuickSnippetMenu, card));
    }
}

/// <summary>The snippet list: rows, "Add snippet" and the variables footer; opens the editor sheet.</summary>
public sealed class SnippetsListSection : ISettingsSection
{
    public string PageId => SettingsPages.TextSnippets;

    public string? FeatureId => "text-snippets";

    public int Order => 20;

    public string? Title => Strings.SnippetsList;

    public IReadOnlyList<string> Keywords => ["Add snippet", "Variables", "{{date}}", "{{clipboard}}", "folder", "trigger"];

    public FrameworkElement CreateView(ISettingsContext c)
    {
        K.RegisterDefaults(c.App);
        var app = c.App;
        var card = new SettingsCard();
        var list = new StackPanel();
        card.Add(list);
        var add = UiKit.Button(Strings.AddSnippet, ButtonKind.Bordered, 12, "", () => Edit(null));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(add);
        var footer = new StackPanel();
        footer.Children.Add(InputUi.Caption(Strings.VariablesHint));
        var second = InputUi.Caption(Strings.VariablesHint2);
        second.Margin = new Thickness(0, 2, 0, 0);
        footer.Children.Add(second);
        var third = InputUi.Caption(Strings.EditorFormatCaption2);
        third.Margin = new Thickness(0, 2, 0, 0);
        footer.Children.Add(third);
        card.Add(footer);

        void Render()
        {
            list.Children.Clear();
            var all = SnippetStore.Load(app);
            if (all.Count == 0)
            {
                list.Children.Add(InputUi.Caption(Strings.NoSnippetsYet));
                return;
            }

            var first = true;
            foreach (var snippet in all.OrderBy(x => x.DisplayTitle, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!first)
                {
                    var divider = UiKit.Divider();
                    divider.Margin = new Thickness(0, 8, 0, 8);
                    list.Children.Add(divider);
                }

                first = false;
                list.Children.Add(Row(snippet));
            }
        }

        FrameworkElement Row(SnippetItem snippet)
        {
            var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(UiKit.Text(snippet.DisplayTitle, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
            var chipText = UiKit.Text(snippet.Trigger, 11.5, FontWeights.Normal, ThemeKeys.TextPrimary);
            chipText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            head.Children.Add(new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(8, 0, 0, 0), Child = chipText, Background = InputUi.Tinted(ThemeKeys.TextPrimary, 0.07), VerticalAlignment = VerticalAlignment.Center });
            if (snippet.Folder.Trim().Length > 0)
            {
                var folder = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                folder.Children.Add(UiKit.Icon("", 10, ThemeKeys.TextSecondary));
                var name = UiKit.Text(snippet.Folder.Trim(), 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                name.Margin = new Thickness(4, 0, 0, 0);
                folder.Children.Add(name);
                head.Children.Add(folder);
            }

            left.Children.Add(head);
            var preview = snippet.Replacement.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            var caption = UiKit.Text(preview, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
            caption.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(caption);
            left.MouseLeftButtonUp += (_, _) => Edit(snippet);
            grid.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var mode = UiKit.Text(snippet.IsImmediate ? Strings.RightAway : Strings.AfterDelimiter, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary);
            mode.VerticalAlignment = VerticalAlignment.Center;
            mode.Margin = new Thickness(0, 0, 10, 0);
            right.Children.Add(mode);
            var toggle = new MacSwitch(snippet.Enabled, SwitchSize.Small, on =>
            {
                var all = SnippetStore.Load(app);
                var target = all.FirstOrDefault(x => x.Id == snippet.Id);
                if (target is not null)
                {
                    target.Enabled = on;
                    SnippetStore.Save(app, all);
                }
            });
            right.Children.Add(toggle);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            return grid;
        }

        void Edit(SnippetItem? existing)
        {
            var all = SnippetStore.Load(app);
            var editor = new SnippetEditorWindow(all, existing);
            if (InputUi.ActiveWindow() is { } owner && !ReferenceEquals(owner, editor))
            {
                editor.Owner = owner;
            }

            if (editor.ShowDialog() != true)
            {
                return;
            }

            if (editor.Deleted && existing is not null)
            {
                all.RemoveAll(x => x.Id == existing.Id);
            }
            else if (editor.Result is { } result)
            {
                var index = all.FindIndex(x => x.Id == result.Id);
                if (index >= 0)
                {
                    all[index] = result;
                }
                else
                {
                    all.Add(result);
                }
            }

            SnippetStore.Save(app, all);
            Render();
        }

        Render();
        return card;
    }
}
