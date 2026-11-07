// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// What outlives a rebuild of the "Start modes automatically" card: the rule being added or edited. Settings rebuilds its page whenever a
/// switch changes which rows exist, and the old text boxes go with it, so every field writes here as it changes.
/// </summary>
internal static class ModesAutoForm
{
    /// <summary>The rule being edited; null while the list shows.</summary>
    public static ModeRule? Draft { get; set; }

    /// <summary>True when <see cref="Draft"/> is not in the list yet.</summary>
    public static bool IsNew { get; set; }

    /// <summary>The last validation message of the form; empty for none.</summary>
    public static string Error { get; set; } = string.Empty;

    /// <summary>The id of the rule whose "Delete this rule?" is up.</summary>
    public static string? ConfirmingDelete { get; set; }
}

/// <summary>
/// Quick tools page, "Start modes automatically" card (spec 10 package P2): the master switch, the badge switch, the list of rules and the
/// inline form that adds or edits one. A rule starts a mode when an app comes to the front or at a time of day.
/// </summary>
public sealed class ModesAutoSettingsSection : ISettingsSection
{
    public string? FeatureId => ModesKeys.FeatureId;

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "modes-auto";

    public string? Title => ModesAutoStrings.Title;

    public IReadOnlyList<string> Keywords => ["modes", "automatic", "rule", "schedule", "app opens", "badge", "island", "time"];

    // Right after the Modes card (85), before Away mode (90).
    public int Order => 86;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        ModesKeys.Register(c.App);
        ModesAutoKeys.Register(c.App);
        var app = c.App;
        var card = new SettingsCard(ModesAutoStrings.Title);

        var master = new MacSwitch(app.Get<bool>(ModesAutoKeys.Enabled), SwitchSize.Regular, on =>
        {
            app.Set(ModesAutoKeys.Enabled, on);
            c.Ui.BeginInvoke(c.RequestRebuild, DispatcherPriority.Background);
        });
        AutomationProperties.SetAutomationId(master, "Modes.Auto.Enabled");
        AutomationProperties.SetName(master, ModesAutoStrings.Title);
        card.Add(new SettingsRow("", ModesAutoStrings.Title, ModesAutoStrings.MasterCaption, master));

        var badge = new MacSwitch(app.Get<bool>(ModesAutoKeys.ShowInIsland), SwitchSize.Regular, on => app.Set(ModesAutoKeys.ShowInIsland, on));
        AutomationProperties.SetAutomationId(badge, "Modes.Auto.ShowInIsland");
        AutomationProperties.SetName(badge, ModesAutoStrings.ShowInIsland);
        card.Add(new SettingsRow("", ModesAutoStrings.ShowInIsland, ModesAutoStrings.ShowInIslandCaption, badge));

        var runtime = c.Services.Get<ModesRuntime>();
        var runner = c.Services.Get<ModesAutoRunner>();
        if (runtime is null || runner is null || !app.Get<bool>(ModesKeys.Enabled) || !app.Get<bool>(ModesAutoKeys.Enabled))
        {
            return card;
        }

        card.AddDivider();
        var rules = runner.Rules;
        var modes = runtime.Modes;
        if (rules.Count == 0 && ModesAutoForm.Draft is null)
        {
            card.AddCaption(ModesAutoStrings.NoRules, SettingsRow.TextInset);
        }

        var list = new StackPanel();
        for (var i = 0; i < rules.Count; i++)
        {
            list.AddSpaced(RuleRow(c, runner, rules, rules[i], modes), i == 0 ? 0 : 6);
        }

        if (rules.Count > 0)
        {
            card.Add(list);
        }

        if (ModesAutoForm.Draft is not null)
        {
            card.Add(Form(c, runner, runtime, modes, rules));
        }
        else
        {
            var add = UiKit.Button(ModesAutoStrings.AddRule, ButtonKind.Prominent, 12, Glyphs.Add, () =>
            {
                ModesAutoForm.Draft = new ModeRule { Id = ModesAutoMath.NewRuleId(), ModeId = modes.FirstOrDefault()?.Id ?? string.Empty };
                ModesAutoForm.IsNew = true;
                ModesAutoForm.Error = string.Empty;
                c.RequestRebuild();
            });
            add.Margin = new Thickness(0, 6, 0, 4);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.IsEnabled = modes.Count > 0 && rules.Count < ModesAutoMath.MaxRules;
            AutomationProperties.SetAutomationId(add, "Modes.Auto.Add");
            AutomationProperties.SetName(add, ModesAutoStrings.AddRule);
            card.Add(add);
            if (modes.Count == 0)
            {
                card.AddCaption(ModesUiStrings.NoModesYet);
            }
        }

        if (runtime.Elevated)
        {
            var elevated = UiKit.Text(ModesStrings.RefusedElevated, 11.5, FontWeights.Normal, ThemeKeys.Orange, wrap: true);
            AutomationProperties.SetAutomationId(elevated, "Modes.Auto.Elevated");
            card.Add(elevated);
        }

        Wire(card, c, runtime);
        return card;
    }

    // ------------------------------------------------------------------------------------------ the list

    private static FrameworkElement RuleRow(ISettingsContext c, ModesAutoRunner runner, IReadOnlyList<ModeRule> rules, ModeRule rule, IReadOnlyList<Mode> modes)
    {
        var mode = modes.FirstOrDefault(m => m.Id == rule.ModeId);
        var host = new StackPanel();
        var buttons = new WrapPanel();
        var row = new SettingsRow("", mode?.Name ?? ModesAutoStrings.Mode, mode is null ? ModesStrings.RefusedMissing : Caption(rule), buttons);

        Button Text(string text, string idPart, Action click, ButtonKind kind = ButtonKind.Bordered)
        {
            var button = UiKit.Button(text, kind, 11.5, null, click);
            button.Margin = new Thickness(0, 0, 6, 4);
            button.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(button, "Modes.Auto." + idPart + "." + rule.Id);
            AutomationProperties.SetName(button, text);
            return button;
        }

        void ShowNormal()
        {
            ModesAutoForm.ConfirmingDelete = null;
            host.Children.Clear();
            host.Children.Add(row);
        }

        void ShowConfirm()
        {
            ModesAutoForm.ConfirmingDelete = rule.Id;
            host.Children.Clear();
            var confirm = new StackPanel { Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
            confirm.Children.Add(UiKit.Text(ModesAutoStrings.DeleteConfirm, 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true));
            var yesNo = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var yes = UiKit.Button(ModesAutoStrings.Delete, ButtonKind.Destructive, 11.5, null, () =>
            {
                ModesAutoForm.ConfirmingDelete = null;
                runner.Save(rules.Where(r => r.Id != rule.Id));
                c.RequestRebuild();
            });
            yes.Margin = new Thickness(0, 0, 8, 4);
            AutomationProperties.SetAutomationId(yes, "Modes.Auto.DeleteConfirm." + rule.Id);
            var no = UiKit.Button(ModesAutoStrings.Cancel, ButtonKind.Bordered, 11.5, null, ShowNormal);
            no.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetAutomationId(no, "Modes.Auto.DeleteCancel." + rule.Id);
            yesNo.Children.Add(yes);
            yesNo.Children.Add(no);
            confirm.Children.Add(yesNo);
            host.Children.Add(confirm);
        }

        buttons.Children.Add(Text(ModesAutoStrings.Edit, "Edit", () =>
        {
            ModesAutoForm.Draft = rule.Clone();
            ModesAutoForm.IsNew = false;
            ModesAutoForm.Error = string.Empty;
            c.RequestRebuild();
        }));
        buttons.Children.Add(Text(ModesAutoStrings.Delete, "Delete", ShowConfirm));
        if (ModesAutoForm.ConfirmingDelete == rule.Id)
        {
            ShowConfirm();
        }
        else
        {
            ShowNormal();
        }

        return host;
    }

    /// <summary>"When Code opens · Ask first", or "Weekdays at 9:00 AM · Starts right away · Leaves at 5:00 PM".</summary>
    private static string Caption(ModeRule rule)
    {
        var text = rule.Kind == "app"
            ? ModesAutoStrings.WhenOpens(AppLabel(rule))
            : ModesAutoStrings.DaysAt(ModesAutoMath.DaysText(rule.Days), ModesAutoStrings.TimeText(rule.At));
        text += rule.Ask ? ModesAutoStrings.AskSuffix : ModesAutoStrings.RightAwaySuffix;
        if (!string.IsNullOrEmpty(rule.Until))
        {
            text += ModesAutoStrings.LeavesAt(ModesAutoStrings.TimeText(rule.Until));
        }

        return rule.Enabled ? text : text + ModesAutoStrings.Disabled;
    }

    /// <summary>The name shown for an app rule: the saved display name, or the file name without ".exe".</summary>
    private static string AppLabel(ModeRule rule) =>
        !string.IsNullOrWhiteSpace(rule.AppName) ? rule.AppName : Path.GetFileNameWithoutExtension(rule.App);

    // ------------------------------------------------------------------------------------------ the form

    private static FrameworkElement Form(ISettingsContext c, ModesAutoRunner runner, ModesRuntime runtime, IReadOnlyList<Mode> modes, IReadOnlyList<ModeRule> rules)
    {
        var draft = ModesAutoForm.Draft!;

        // A rule whose mode was deleted shows the first mode in the picker, so that is what it keeps unless the person picks another.
        if (modes.Count > 0 && modes.All(m => m.Id != draft.ModeId))
        {
            draft.ModeId = modes[0].Id;
        }

        var form = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(form, "Modes.Auto.Form");

        // Mode
        var modeItems = modes.Select(m => (m.Id, m.Name)).ToList();
        form.Children.Add(new PickerRow<string>(null, ModesAutoStrings.Mode, null, modeItems, draft.ModeId, id =>
        {
            draft.ModeId = id;
        }));

        // Start when
        IReadOnlyList<(string Value, string Label)> kinds = [("app", ModesAutoStrings.AppOpens), ("time", ModesAutoStrings.AtTime)];
        form.Children.Add(new PickerRow<string>(null, ModesAutoStrings.StartWhen, null, kinds, draft.Kind, kind =>
        {
            if (kind != draft.Kind)
            {
                draft.Kind = kind;
                ModesAutoForm.Error = string.Empty;
                c.RequestRebuild();
            }
        }));

        var note = ModesDialogKit.Note();
        TextBox? timeBox = null;
        TextBox? untilBox = null;
        if (draft.Kind == "app")
        {
            AddAppRows(c, form, runtime, draft);
        }
        else
        {
            form.Children.Add(Labelled(ModesAutoStrings.Time, null, out timeBox, draft.At, ModesAutoStrings.TimeHint, t => draft.At = t));
            form.Children.Add(Days(draft));
            form.Children.Add(Labelled(ModesAutoStrings.LeaveAt, ModesAutoStrings.LeaveAtCaption, out untilBox, draft.Until, ModesAutoStrings.TimeHint, t => draft.Until = t));
        }

        // Ask before starting
        var ask = new MacSwitch(draft.Ask, SwitchSize.Regular, on => draft.Ask = on);
        AutomationProperties.SetAutomationId(ask, "Modes.Auto.Ask");
        AutomationProperties.SetName(ask, ModesAutoStrings.AskFirst);
        form.Children.Add(new SettingsRow("", ModesAutoStrings.AskFirst, ModesAutoStrings.AskFirstCaption, ask));

        ModesDialogKit.Say(note, ModesAutoForm.Error);
        AutomationProperties.SetAutomationId(note, "Modes.Auto.Error");
        form.Children.Add(note);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        var save = UiKit.Button(ModesAutoStrings.Save, ButtonKind.Prominent, 12, null, () =>
        {
            var error = Validate(draft, timeBox?.Text ?? draft.At, untilBox?.Text ?? draft.Until);
            if (error is null)
            {
                var next = rules.Where(r => r.Id != draft.Id).ToList();
                var index = rules.ToList().FindIndex(r => r.Id == draft.Id);
                var clean = draft.Clone();
                clean.Enabled = true;
                if (index >= 0)
                {
                    next.Insert(Math.Min(index, next.Count), clean);
                }
                else
                {
                    next.Add(clean);
                }

                if (runner.Save(next))
                {
                    ModesAutoForm.Draft = null;
                    ModesAutoForm.Error = string.Empty;
                    c.RequestRebuild();
                    return;
                }

                error = ModesAutoStrings.ErrorSave;
            }

            ModesAutoForm.Error = error;
            ModesDialogKit.Say(note, error);
        });
        save.Margin = new Thickness(0, 0, 8, 0);
        AutomationProperties.SetAutomationId(save, "Modes.Auto.Save");
        AutomationProperties.SetName(save, ModesAutoStrings.Save);
        var cancel = UiKit.Button(ModesAutoStrings.Cancel, ButtonKind.Bordered, 12, null, () =>
        {
            ModesAutoForm.Draft = null;
            ModesAutoForm.Error = string.Empty;
            c.RequestRebuild();
        });
        AutomationProperties.SetAutomationId(cancel, "Modes.Auto.Cancel");
        AutomationProperties.SetName(cancel, ModesAutoStrings.Cancel);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        form.Children.Add(buttons);
        return form;
    }

    /// <summary>The message for the first thing wrong with the form, or null when the rule can be saved. Fills the rule's normalised times.</summary>
    private static string? Validate(ModeRule draft, string timeText, string untilText)
    {
        if (draft.Kind == "app")
        {
            draft.Until = string.Empty;
            return ModesAutoMath.NormalizeExe(draft.App).Length == 0 ? ModesAutoStrings.ErrorApp : null;
        }

        if (!ModesAutoMath.TryParseTime(timeText, out var at))
        {
            return ModesAutoStrings.ErrorTime;
        }

        draft.At = ModesAutoMath.FormatTime(at);
        draft.Until = string.Empty;
        if (!string.IsNullOrWhiteSpace(untilText))
        {
            if (!ModesAutoMath.TryParseTime(untilText, out var until))
            {
                return ModesAutoStrings.ErrorTime;
            }

            if (until <= at)
            {
                return ModesAutoStrings.ErrorLeaveAt;
            }

            draft.Until = ModesAutoMath.FormatTime(until);
        }

        return (draft.Days & 0b1111111) == 0 ? ModesAutoStrings.ErrorDays : null;
    }

    /// <summary>A labelled text box that writes every change to the draft.</summary>
    private static FrameworkElement Labelled(string label, string? caption, out TextBox box, string text, string hint, Action<string> changed)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(ModesDialogKit.Label(label, 0));
        var field = ModesDialogKit.Field(out var textBox, text, hint);
        textBox.TextChanged += (_, _) => changed(textBox.Text);
        textBox.MaxLength = 5;
        textBox.Width = 120;
        textBox.HorizontalAlignment = HorizontalAlignment.Left;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(textBox, "Modes.Auto." + (caption is null ? "Time" : "LeaveAt"));
        AutomationProperties.SetName(textBox, label);
        panel.Children.Add(field);
        if (caption is not null)
        {
            panel.Children.Add(ModesDialogKit.Caption(caption));
        }

        box = textBox;
        return panel;
    }

    /// <summary>Seven day chips, Monday first; a click flips the chip in place.</summary>
    private static FrameworkElement Days(ModeRule draft)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(ModesDialogKit.Label(ModesAutoStrings.Days, 0));
        var row = new WrapPanel();
        for (var i = 0; i < 7; i++)
        {
            var bit = 1 << i;
            var label = UiKit.Text(ModesAutoStrings.DayShort(i), 12, FontWeights.Medium);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Child = label,
            };

            void Paint()
            {
                var on = (draft.Days & bit) != 0;
                chip.Themed(Border.BackgroundProperty, on ? ThemeKeys.AccentFill12 : ThemeKeys.SettingsControl);
                label.Themed(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
                AutomationProperties.SetItemStatus(chip, on ? "on" : "off");
            }

            chip.Clickable(() =>
            {
                draft.Days ^= bit;
                Paint();
            }, ModesAutoStrings.DayShort(i));
            AutomationProperties.SetAutomationId(chip, "Modes.Auto.Day." + i);
            Paint();
            row.Children.Add(chip);
        }

        panel.Children.Add(row);
        return panel;
    }

    /// <summary>The app picker: the apps of the chosen mode, the apps open now and "Browse…". Only the file name is kept.</summary>
    private static void AddAppRows(ISettingsContext c, StackPanel form, ModesRuntime runtime, ModeRule draft)
    {
        var choices = new List<(string Value, string Label)> { (string.Empty, ModesAutoStrings.ChooseApp) };
        void Add(string? path)
        {
            var exe = ModesAutoMath.NormalizeExe(path);
            if (exe.Length > 0 && choices.All(x => x.Value != exe))
            {
                choices.Add((exe, ProcessFacts.DisplayName(path)));
            }
        }

        if (runtime.Modes.FirstOrDefault(m => m.Id == draft.ModeId) is { } mode)
        {
            foreach (var item in mode.Items.Where(i => i.Kind == ModeItemKind.App && !string.IsNullOrEmpty(i.Exe)))
            {
                Add(item.Exe);
            }
        }

        foreach (var path in RunningWindowedApps())
        {
            Add(path);
        }

        // A rule that was saved earlier keeps its app in the list even when the app is not running now.
        var current = ModesAutoMath.NormalizeExe(draft.App);
        if (current.Length > 0 && choices.All(x => x.Value != current))
        {
            choices.Add((current, string.IsNullOrWhiteSpace(draft.AppName) ? Path.GetFileNameWithoutExtension(current) : draft.AppName));
        }

        var picker = new PickerRow<string>(null, ModesAutoStrings.App, null, choices, current, exe =>
        {
            draft.App = exe;
            draft.AppName = choices.FirstOrDefault(x => x.Value == exe).Label ?? string.Empty;
            if (exe.Length == 0)
            {
                draft.AppName = string.Empty;
            }
        }, 220);
        AutomationProperties.SetAutomationId(picker, "Modes.Auto.App");
        form.Children.Add(picker);

        var browse = UiKit.Button(ModesAutoStrings.Browse, ButtonKind.Bordered, 11.5, null, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = ModesAutoStrings.ChooseApp,
                Filter = "Apps (*.exe)|*.exe",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() == true)
            {
                // The path is only read for the name; the rule keeps the file name.
                draft.App = ModesAutoMath.NormalizeExe(dialog.FileName);
                draft.AppName = ProcessFacts.DisplayName(dialog.FileName);
                c.RequestRebuild();
            }
        });
        browse.Margin = new Thickness(SettingsRow.TextInset, 4, 0, 0);
        browse.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(browse, "Modes.Auto.Browse");
        AutomationProperties.SetName(browse, ModesAutoStrings.Browse);
        form.Children.Add(browse);
    }

    /// <summary>Image paths of the programs that have a window now. Only reads; nothing is opened or closed.</summary>
    private static List<string> RunningWindowedApps()
    {
        var paths = new List<string>();
        Process[] processes = [];
        try
        {
            processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != 0 && process.Id != Environment.ProcessId && ProcessFacts.Read(process.Id) is { } facts)
                    {
                        paths.Add(facts.Path);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // a process that ended or cannot be read is simply not offered
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return [.. paths.OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase)];
    }

    // ------------------------------------------------------------------------------------------ life of the card

    /// <summary>Redraws the list when the modes change (a renamed or deleted mode), but not while a form is open: it would lose the keyboard focus.</summary>
    private static void Wire(SettingsCard card, ISettingsContext c, ModesRuntime runtime)
    {
        var pending = false;

        void OnChanged()
        {
            if (pending || ModesAutoForm.Draft is not null)
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

        card.Loaded += (_, _) => runtime.ModesEdited += OnChanged;
        card.Unloaded += (_, _) => runtime.ModesEdited -= OnChanged;
    }
}
