// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Tools;

/// <summary>Static access of the stateless settings / panel contributions to the running Command Bar.</summary>
internal static class BarHost
{
    public static BarFeature? Feature { get; set; }

    public static BarService? Service => Feature?.Service;

    /// <summary>Row shortcuts the OS refused (stable keys).</summary>
    public static HashSet<string> RefusedRowShortcuts { get; } = [];
}

/// <summary>The Command Bar feature: one field that finds and runs everything the app does (spec 1).</summary>
public sealed class BarFeature : ToolsFeatureBase
{
    private BarService? _service;
    private BarWindow? _window;
    private readonly Dictionary<string, IDisposable> _rowHotkeys = [];

    public override string Id => "command-bar";

    internal BarService? Service => _service;

    internal IContributionServices? Services => IsRunning ? Ctx : null;

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Open the Command Bar"), () => Safe(() => Show()), ""),
    ];

    protected override void RegisterDefaults(IAppSettings app)
    {
        ToolsDefaults.Register(app);
        app.Register("killProcessCommandBarEnabled", true);
        app.Register("uninstallerCommandBarEnabled", false);
    }

    protected override void OnStart()
    {
        BarHost.Feature = this;
        BarProviders.Hub = Ctx.Services;
        _service = new BarService(Ctx);
        _service.RowShortcutAssigned += (_, _) => SyncRowShortcuts();
        _service.CaptureStarted += capturing =>
        {
            _window?.SetCapturing(capturing);
            if (capturing)
            {
                DisposeRowHotkeys();
            }
            else
            {
                SyncRowShortcuts();
            }
        };
        BindRole("commandBar", Toggle);
        SyncRowShortcuts();
        BarProviders.RescanApps();
    }

    protected override void OnStop()
    {
        DisposeRowHotkeys();
        if (_window is not null)
        {
            try
            {
                _window.HideBar();
                _window.Close();
            }
            catch
            {
                // already closed
            }
        }

        _window = null;
        _service = null;
        BarProviders.Hub = null;
        BarHost.Feature = null;
    }

    protected override void OnSettingChanged(string key)
    {
        if (key == "commandBarRowShortcuts")
        {
            SyncRowShortcuts();
        }
    }

    /// <summary>Shows the bar (or hides it when it is already visible).</summary>
    public void Toggle()
    {
        if (_window is { IsVisible: true })
        {
            _service?.Hide();
            return;
        }

        Show();
    }

    /// <summary>Opens the bar; <paramref name="promptKey"/> runs a row through the normal path (row shortcut that needs a prompt).</summary>
    public void Show(string? promptKey = null)
    {
        if (!IsRunning || _service is null)
        {
            return;
        }

        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        var target = Plat.Foreground();
        if (target != 0 && Plat.ProcessOf(target) == Environment.ProcessId)
        {
            target = 0;
        }

        _service.SelectionText = null;
        _service.SelectionTypesOver = false;
        if (target != 0 && _service.Prefs.IsSourceEnabled("selection"))
        {
            if (BarProviders.ReadSelection() is { } sel)
            {
                _service.SelectionText = sel.Text;
                _service.SelectionTypesOver = sel.TypesOver;
            }
        }

        _window ??= new BarWindow(_service);
        _service.Begin(target);
        _window.Present();
        if (promptKey is not null)
        {
            _service.RunPrompted(promptKey);
        }
    }

    // ------------------------------------------------------------------ per-row global shortcuts (1.7.7)

    private void DisposeRowHotkeys()
    {
        foreach (var d in _rowHotkeys.Values)
        {
            d.Dispose();
        }

        _rowHotkeys.Clear();
    }

    private void SyncRowShortcuts()
    {
        DisposeRowHotkeys();
        BarHost.RefusedRowShortcuts.Clear();
        if (_service is null)
        {
            return;
        }

        foreach (var (key, hotkey) in _service.Prefs.RowShortcuts().OrderBy(p => p.Key, StringComparer.Ordinal).Take(BarPreferences.MaxRowShortcuts))
        {
            var rowKey = key;
            if (Ctx.Hotkeys.TryRegister(hotkey, () => Safe(() => RunRow(rowKey)), out var registration))
            {
                _rowHotkeys[key] = registration;
            }
            else
            {
                BarHost.RefusedRowShortcuts.Add(key);
            }
        }
    }

    private void RunRow(string key)
    {
        if (_service is null)
        {
            return;
        }

        if (_window is { IsVisible: true })
        {
            _service.RunPrompted(key);
            return;
        }

        var target = Plat.Foreground();
        _service.Target = target;
        if (_service.RunRowByKey(key))
        {
            Show(key);
        }
    }
}

/// <summary>Utilities-tab row of the Command Bar.</summary>
public sealed class CommandBarUtility : IPanelUtility
{

    public string? FeatureId => "command-bar";

    public int Order => 160;

    public string Id => "commandBar";

    public string Title => Loc.T("Command Bar");

    public string Glyph => "";

    public string? Caption => Loc.T("Type to find and run anything");

    public string? ShortcutText => BarHost.Feature?.Services is { } s ? global::Trayloom.Features.Tools.ShortcutText.Of(s, "commandBar") : null;

    public int InvokeDelayMs => 150;

    public void Invoke(IPanelContext context)
    {
        BarHost.Feature?.Show();
    }

    public string? SettingsDestination => SettingsPages.CommandBar;
}
