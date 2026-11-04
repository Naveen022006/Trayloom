// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>Settings &gt; Dynamic Island &gt; "Screen time" (spec 06 section 1.7).</summary>
public sealed class ScreenTimeSettingsSection : ISettingsSection
{
    public string? FeatureId => ScreenTimeKeys.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => ScreenTimeKeys.ModuleId;

    public string? Title => ScreenTimeStrings.Title;

    public IReadOnlyList<string> Keywords => ["usage", "time", "apps", "today", "history"];

    public int Order => 170;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        ScreenTimeKeys.Register(app);
        var runtime = context.Services.Get<ScreenTimeRuntime>();
        var enabled = app.Get(ScreenTimeKeys.Enabled, true);
        var card = new SettingsCard(ScreenTimeStrings.Title);

        var toggle = new MacSwitch(enabled, SwitchSize.Regular, on =>
        {
            app.Set(ScreenTimeKeys.Enabled, on);
            context.RequestRebuild();
        });
        ScreenTimeLook.Name(toggle, "ScreenTime.Enabled", ScreenTimeStrings.CountTitle);
        card.Add(new SettingsRow("", ScreenTimeStrings.CountTitle, ScreenTimeStrings.CountCaption, toggle));

        if (enabled)
        {
            var idle = new PickerRow<int>(null, ScreenTimeStrings.StopAfter, null,
                [(1, ScreenTimeStrings.OneMinute), (2, ScreenTimeStrings.TwoMinutes), (5, ScreenTimeStrings.FiveMinutes)],
                app.Get(ScreenTimeKeys.IdleMinutes, 2), v => app.Set(ScreenTimeKeys.IdleMinutes, v), 230);
            card.Add(Indent(ScreenTimeLook.Expose(idle, "ScreenTime.IdleMinutes", ScreenTimeStrings.StopAfter)));

            var media = new MacSwitch(app.Get(ScreenTimeKeys.CountMedia, true), SwitchSize.Regular, on => app.Set(ScreenTimeKeys.CountMedia, on));
            ScreenTimeLook.Name(media, "ScreenTime.CountMedia", ScreenTimeStrings.CountMediaTitle);
            card.Add(Indent(new SettingsRow(null, ScreenTimeStrings.CountMediaTitle, ScreenTimeStrings.CountMediaCaption, media)));

            var keep = new PickerRow<int>(null, ScreenTimeStrings.KeepFor, null,
                [(1, ScreenTimeStrings.OneDay), (7, ScreenTimeStrings.SevenDays), (30, ScreenTimeStrings.ThirtyDays)],
                app.Get(ScreenTimeKeys.KeepDays, 7), v => app.Set(ScreenTimeKeys.KeepDays, v), 230);
            card.Add(Indent(ScreenTimeLook.Expose(keep, "ScreenTime.KeepDays", ScreenTimeStrings.KeepFor)));
        }

        card.AddDivider();
        var total = runtime?.Today.Total ?? 0;
        var caption = total >= 1 && runtime is not null
            ? ScreenTimeStrings.TodayCaption(ScreenTimeFormat.Long(total), runtime.TopApps(3))
            : ScreenTimeStrings.Empty;
        var clear = UiKit.Button(ScreenTimeStrings.ClearButton, ButtonKind.Bordered, 12, onClick: () =>
        {
            var answer = MessageBox.Show(ScreenTimeStrings.ClearBody, ScreenTimeStrings.ClearTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.OK)
            {
                runtime?.Clear();
                context.RequestRebuild();
            }
        });
        ScreenTimeLook.Name(clear, "ScreenTime.Clear", ScreenTimeStrings.ClearButton);
        var today = new SettingsRow("", ScreenTimeStrings.Today, caption, clear);
        // The row is a plain grid that UI Automation skips, so the id goes on the caption text itself.
        if (CaptionOf(today, caption) is { } captionBlock)
        {
            ScreenTimeLook.Name(captionBlock, "ScreenTime.Today", caption);
        }

        card.Add(today);
        return card;
    }

    private static TextBlock? CaptionOf(DependencyObject parent, string text)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is TextBlock block && block.Text == text)
            {
                return block;
            }

            if (child is DependencyObject next && CaptionOf(next, text) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static FrameworkElement Indent(UIElement child) => new Border { Child = child, Margin = new Thickness(SettingsRow.TextInset, 0, 0, 0) };
}
