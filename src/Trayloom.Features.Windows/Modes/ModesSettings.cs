// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// Quick tools page, "Modes" card (spec 08 section 2.6): the on/off switch, the list of modes with Open, Edit, reorder and Delete (with an
/// inline confirmation), "New mode" and "Save what's open now…", and a shortcut recorder per mode. The editor and the capture dialog
/// are separate windows (<see cref="ModeEditorWindow"/>, <see cref="ModeCaptureWindow"/>).
/// </summary>
public sealed class ModesSettingsSection : ISettingsSection
{
    public string? FeatureId => ModesKeys.FeatureId;

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "modes";

    public string? Title => ModesStrings.Title;

    public IReadOnlyList<string> Keywords => ["workspace", "coding", "study", "tabs", "browser", "apps", "switch", "focus", "mode", "shortcut"];

    // Before Away mode (90), so the two cards of related tools sit in the order a person sets them up.
    public int Order => 85;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        ModesKeys.Register(c.App);
        var app = c.App;
        var card = new SettingsCard(ModesStrings.Title);

        var toggle = new MacSwitch(app.Get<bool>(ModesKeys.Enabled), SwitchSize.Regular, on =>
        {
            app.Set(ModesKeys.Enabled, on);

            // The feature starts or stops in response to the setting; asking a moment later finds the service in its new state.
            c.Ui.BeginInvoke(c.RequestRebuild, DispatcherPriority.Background);
        });
        AutomationProperties.SetAutomationId(toggle, "Modes.Enabled");
        AutomationProperties.SetName(toggle, ModesStrings.Title);
        card.Add(new SettingsRow("", ModesStrings.Title, ModesUiStrings.Caption, toggle));

        var runtime = c.Services.Get<ModesRuntime>();
        if (runtime is null || !app.Get<bool>(ModesKeys.Enabled))
        {
            return card;
        }

        var modes = runtime.Modes;
        var active = runtime.Active;
        var captions = new Dictionary<string, (SettingsRow Row, Mode Mode)>();

        string CaptionOf(Mode mode)
        {
            var text = mode.Id == active?.Id ? ModesStrings.Active : mode.Items.Count == 0 ? ModesStrings.Empty : ModesStrings.ItemCount(mode.Items.Count);
            return ShortcutTextOf(c, mode.Id) is { Length: > 0 } shortcut ? text + ModesUiStrings.Dot + shortcut : text;
        }

        if (modes.Count == 0)
        {
            card.AddCaption(ModesUiStrings.NoModesYet, SettingsRow.TextInset);
        }

        var list = new StackPanel();
        for (var i = 0; i < modes.Count; i++)
        {
            var mode = modes[i];
            var row = ModeRow(c, card, runtime, mode, i, modes.Count, CaptionOf(mode), out var settingsRow);
            captions[mode.Id] = (settingsRow, mode);
            list.AddSpaced(row, i == 0 ? 0 : 6);
        }

        if (modes.Count > 0)
        {
            card.Add(list);
        }

        var buttons = new WrapPanel();
        var create = UiKit.Button(ModesUiStrings.NewMode, ButtonKind.Prominent, 12, Glyphs.Add, () =>
            ModesDialogKit.ShowOver(new ModeEditorWindow(runtime, c, null), card));
        create.Margin = new Thickness(0, 0, 8, 4);
        create.IsEnabled = runtime.CanAdd;
        AutomationProperties.SetAutomationId(create, "Modes.New");
        AutomationProperties.SetName(create, ModesUiStrings.NewMode);
        buttons.Children.Add(create);

        var capture = UiKit.Button(ModesStrings.SaveCurrent, ButtonKind.Bordered, 12, "", () =>
            ModesDialogKit.ShowOver(new ModeCaptureWindow(runtime, c), card));
        capture.Margin = new Thickness(0, 0, 8, 4);
        capture.IsEnabled = runtime.CanAdd;
        AutomationProperties.SetAutomationId(capture, "Modes.SaveCurrent");
        AutomationProperties.SetName(capture, ModesStrings.SaveCurrent);
        buttons.Children.Add(capture);
        card.Add(buttons);
        if (!runtime.CanAdd)
        {
            card.AddCaption(ModesUiStrings.LimitReached);
        }

        if (modes.Count > 0)
        {
            AddShortcuts(card, c, modes, captions, CaptionOf);
        }

        card.AddCaption(ModesUiStrings.NeverCloses);
        if (runtime.Elevated)
        {
            var elevated = UiKit.Text(ModesStrings.RefusedElevated, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
            AutomationProperties.SetAutomationId(elevated, "Modes.Elevated");
            card.Add(elevated);
        }

        Wire(card, c, runtime);
        return card;
    }

    // ------------------------------------------------------------------------------------------ a mode row

    /// <summary>One row: colour and icon, name and caption, and the buttons. Delete turns the buttons into a confirmation in place.</summary>
    private static FrameworkElement ModeRow(ISettingsContext c, FrameworkElement anchor, ModesRuntime runtime, Mode mode, int index, int count, string caption, out SettingsRow settingsRow)
    {
        var id = mode.Id;
        var host = new StackPanel();
        var inner = new Grid();
        var buttons = new WrapPanel();

        // The row has a glyph column without a tile, so the coloured circle can sit there.
        var row = new SettingsRow("", mode.Name, caption, buttons);
        settingsRow = row;
        var circle = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
            Background = ModesLook.Of(ModesLook.ColorOf(mode.Color)),
        };
        var glyph = UiKit.Icon(mode.Glyph, 13, ThemeKeys.TextPrimary);
        glyph.ClearValue(TextBlock.ForegroundProperty);
        glyph.Foreground = Brushes.White;
        circle.Child = glyph;
        inner.Children.Add(row);
        inner.Children.Add(circle);

        Button Text(string text, string idPart, Action click, ButtonKind kind = ButtonKind.Bordered)
        {
            var button = UiKit.Button(text, kind, 11.5, null, click);
            button.Margin = new Thickness(0, 0, 6, 4);
            button.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(button, "Modes." + idPart + "." + id);
            AutomationProperties.SetName(button, text);
            return button;
        }

        Button Small(string glyphCode, string tooltip, string idPart, Action click, bool enabled)
        {
            var button = ShortcutUi.SmallIconButton(glyphCode, tooltip);
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1 : 0.4;
            button.Margin = new Thickness(0, 0, 2, 4);
            button.Click += (_, _) => click();
            AutomationProperties.SetAutomationId(button, "Modes." + idPart + "." + id);
            return button;
        }

        void ShowNormal()
        {
            host.Children.Clear();
            host.Children.Add(inner);
        }

        void ShowConfirm()
        {
            host.Children.Clear();
            var confirm = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
            confirm.Children.Add(UiKit.Text(ModesUiStrings.DeleteConfirm(mode.Name), 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true));
            var yesNo = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var yes = UiKit.Button(ModesUiStrings.Delete, ButtonKind.Destructive, 11.5, null, () => runtime.Delete(id));
            yes.Margin = new Thickness(0, 0, 8, 4);
            AutomationProperties.SetAutomationId(yes, "Modes.DeleteConfirm." + id);
            var no = UiKit.Button(ModesUiStrings.Cancel, ButtonKind.Bordered, 11.5, null, ShowNormal);
            no.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetAutomationId(no, "Modes.DeleteCancel." + id);
            yesNo.Children.Add(yes);
            yesNo.Children.Add(no);
            confirm.Children.Add(yesNo);
            host.Children.Add(confirm);
        }

        buttons.Children.Add(Text(ModesUiStrings.Open, "Enter", () => runtime.Enter(id, "settings")));
        buttons.Children.Add(Text(ModesUiStrings.Edit, "Edit", () => ModesDialogKit.ShowOver(new ModeEditorWindow(runtime, c, runtime.Modes.FirstOrDefault(m => m.Id == id) ?? mode), anchor)));
        buttons.Children.Add(Small("", ModesUiStrings.MoveUp, "Up", () => runtime.Move(id, -1), index > 0));
        buttons.Children.Add(Small("", ModesUiStrings.MoveDown, "Down", () => runtime.Move(id, 1), index < count - 1));
        buttons.Children.Add(Text(ModesUiStrings.Delete, "Delete", ShowConfirm));

        ShowNormal();
        return ModesLandmark.Wrap(host, "Modes.Row." + id, mode.Name);
    }

    // ------------------------------------------------------------------------------------------ shortcuts

    private static string? ShortcutTextOf(IContributionServices c, string modeId)
    {
        var role = ModesRuntime.RoleId(modeId);
        if (c.Shortcuts.Role(role) is null)
        {
            return null;
        }

        var state = c.Shortcuts.GetState(role);
        return (state.Effective ?? state.Configured)?.ToString();
    }

    /// <summary>One recorder per mode (the role exists while Modes runs); a change refreshes the shortcut in the mode's caption.</summary>
    private static void AddShortcuts(SettingsCard card, ISettingsContext c, IReadOnlyList<Mode> modes, Dictionary<string, (SettingsRow Row, Mode Mode)> captions, Func<Mode, string> captionOf)
    {
        var withRole = modes.Where(m => c.Shortcuts.Role(ModesRuntime.RoleId(m.Id)) is not null).ToList();
        if (withRole.Count == 0)
        {
            return;
        }

        card.AddDivider();
        card.Add(UiKit.Text(ModesUiStrings.ShortcutsTitle, 12, FontWeights.SemiBold));
        foreach (var mode in withRole)
        {
            var id = mode.Id;
            var accessory = ShortcutUi.Accessory(c.Shortcuts, ModesRuntime.RoleId(id), clearable: true, enabled: true, out var note, () =>
            {
                if (captions.TryGetValue(id, out var entry))
                {
                    entry.Row.Caption = captionOf(entry.Mode);
                }
            });
            var stack = new StackPanel();
            stack.Children.Add(new SettingsRow("", mode.Name, null, accessory));
            note.Margin = new Thickness(SettingsRow.TextInset, 4, 0, 0);
            stack.Children.Add(note);
            card.Add(ModesLandmark.Wrap(stack, "Modes.Shortcut." + id, mode.Name));
        }
    }

    // ------------------------------------------------------------------------------------------ life of the card

    /// <summary>Rebuilds the page when the modes or the active mode change, and opens the capture dialog once when the action asked for it.</summary>
    private static void Wire(SettingsCard card, ISettingsContext c, ModesRuntime runtime)
    {
        var pending = false;

        void OnChanged()
        {
            // Several changes arrive together (a switch starts and ends, an edit saves and reorders); one rebuild covers them.
            if (pending)
            {
                return;
            }

            pending = true;
            card.Dispatcher.BeginInvoke(() =>
            {
                pending = false;
                if (card.IsLoaded)
                {
                    c.RequestRebuild();
                }
            }, DispatcherPriority.Background);
        }

        card.Loaded += (_, _) =>
        {
            runtime.Changed += OnChanged;

            // "Save what's open now" from the Command Bar or the island opens Settings here and sets this request.
            if (ModesRequests.TakeCapture())
            {
                card.Dispatcher.BeginInvoke(() =>
                {
                    if (card.IsLoaded && runtime.CanAdd)
                    {
                        ModesDialogKit.ShowOver(new ModeCaptureWindow(runtime, c), card);
                    }
                }, DispatcherPriority.ContextIdle);
            }
        };
        card.Unloaded += (_, _) => runtime.Changed -= OnChanged;
    }
}
