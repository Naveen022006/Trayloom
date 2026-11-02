// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Floating pad for short-lived notes with up to 12 tabs (Vorssaint scratchpad).</summary>
public sealed class ScratchpadFeature : ToolsFeatureBase
{
    private PadStore? _store;
    private ScratchpadWindow? _window;

    public override string Id => "scratchpad";

    public override IReadOnlyList<FeatureAction> Actions =>
        [new FeatureAction("show", PadStrings.OpenButton, () => Safe(Toggle), "")];

    protected override void RegisterDefaults(IAppSettings app)
    {
        app.Register("scratchpadShortcutEnabled", false);
        app.Register("scratchpadRetention", "never");
        app.Register("scratchpadCloseOnClickOutside", true);
        app.Register("scratchpadBackgroundOpacity", 0.0);
        app.Register("scratchpadTextSize", 13.0);
        app.Register("panelUtilityScratchpad", true);
    }

    protected override void OnStart()
    {
        PadServices.Ctx = Ctx;
        _store = new PadStore(Ctx.Ui, Path.Combine(AppPaths.Root, "Scratchpad.json"), Path.Combine(Ctx.DataDirectory, "notes.json"), () => App.Get<string>("scratchpadRetention"));
        BindRole("scratchpad", Toggle);
    }

    protected override void OnStop()
    {
        _store?.Flush();
        if (_window is not null)
        {
            _window.Dismiss();
            _window.Close();
        }

        _window = null;
        _store = null;
    }

    protected override void OnSettingChanged(string key)
    {
        if (key is "scratchpadTextSize" or "scratchpadBackgroundOpacity" or "scratchpadCloseOnClickOutside" && _window is { IsVisible: true } window)
        {
            window.ApplySettings(TextSize(), Opacity(), App.Get<bool>("scratchpadCloseOnClickOutside"));
        }
    }

    private double TextSize()
    {
        var size = App.Get<double>("scratchpadTextSize");
        return double.IsFinite(size) ? Math.Clamp(Math.Round(size), 10, 22) : 13;
    }

    private double Opacity()
    {
        var value = App.Get<double>("scratchpadBackgroundOpacity");
        return double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    }

    /// <summary>The shortcut is a strict toggle only while the pad has the keyboard.</summary>
    private void Toggle()
    {
        if (!IsRunning || _store is null)
        {
            return;
        }

        if (_window is { IsVisible: true } window)
        {
            if (window.IsModal)
            {
                return;
            }

            if (window.HasKeyboard)
            {
                window.Dismiss();
            }
            else
            {
                window.Activate();
            }

            return;
        }

        _window ??= new ScratchpadWindow(Ctx, _store);
        _window.Present(TextSize(), Opacity(), App.Get<bool>("scratchpadCloseOnClickOutside"));
    }
}

/// <summary>Quick tools page, "Scratchpad" card.</summary>
public sealed class ScratchpadSettingsSection : ISettingsSection
{
    public string? FeatureId => "scratchpad";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "scratchpad";

    public string? Title => PadStrings.Title;

    public IReadOnlyList<string> Keywords => ["notes", "retention", "text size", "pad background", "close when I click outside"];

    public int Order => 70;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        var app = c.App;
        app.Register("scratchpadShortcutEnabled", false);
        app.Register("scratchpadRetention", "never");
        app.Register("scratchpadCloseOnClickOutside", true);
        app.Register("scratchpadBackgroundOpacity", 0.0);
        app.Register("scratchpadTextSize", 13.0);

        var card = new SettingsCard(PadStrings.Title);
        var open = UiKit.Button(PadStrings.OpenButton, ButtonKind.Bordered, 13, "", () => ActionBridge.Invoke(c, "scratchpad", "show"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        card.AddCaption(PadStrings.PanelCaption);

        IReadOnlyList<(string, string)> items = [("never", "Never"), ("day", "After a day unused"), ("week", "After a week unused"), ("month", "After a month unused")];
        var current = app.Get<string>("scratchpadRetention");
        card.Add(new PickerRow<string>(null, PadStrings.RetentionTitle, null, items, items.Any(i => i.Item1 == current) ? current : "never", v => app.Set("scratchpadRetention", v), 170));
        card.AddCaption(PadStrings.RetentionCaption);
        card.Add(new SettingsRow(null, PadStrings.CloseOnClickOutside, null,
            new MacSwitch(app.Get<bool>("scratchpadCloseOnClickOutside"), SwitchSize.Regular, on => app.Set("scratchpadCloseOnClickOutside", on))));
        card.Add(new SliderRow(null, PadStrings.TextSize, null, 10, 22, Math.Clamp(app.Get<double>("scratchpadTextSize"), 10, 22), "0", v => app.Set("scratchpadTextSize", Math.Round(v)), 1));
        card.Add(new SliderRow(null, PadStrings.BackgroundOpacity, null, 0, 1, Math.Clamp(app.Get<double>("scratchpadBackgroundOpacity"), 0, 1), "0%", v => app.Set("scratchpadBackgroundOpacity", Math.Round(v / 0.05) * 0.05), 0.05));
        var ends = new Grid { Margin = new Thickness(0, -4, 0, 0) };
        ends.Children.Add(UiKit.Text(PadStrings.Translucent, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        var opaque = UiKit.Text(PadStrings.Opaque, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        opaque.HorizontalAlignment = HorizontalAlignment.Right;
        ends.Children.Add(opaque);
        card.Add(ends);
        ShortcutSettingsRows.AddGlobalShortcut(card, c, "scratchpad", "scratchpadShortcutEnabled");
        return card;
    }
}

/// <summary>Utilities tab row.</summary>
public sealed class ScratchpadUtility : IPanelUtility
{
    public string? FeatureId => "scratchpad";

    public string Id => "scratchpad";

    public string Title => PadStrings.Title;

    public string Glyph => "";

    public string? Caption => PadStrings.PanelCaption;

    public string? ShortcutText => PadServices.ShortcutOf("scratchpad");

    public int Order => 150;

    public string? SettingsDestination => "quick-tools#scratchpad";

    public void Invoke(IPanelContext context) => ActionBridge.Invoke(context, "scratchpad", "show");
}
