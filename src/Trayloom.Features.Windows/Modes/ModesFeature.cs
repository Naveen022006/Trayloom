// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>The settings keys of Modes and their defaults (spec 08 section 2.2). The modes themselves live in <c>modes.json</c>, not here.</summary>
internal static class ModesKeys
{
    public const string FeatureId = "modes";

    /// <summary>The catalog enable key.</summary>
    public const string Enabled = "modesEnabled";

    /// <summary>The id of the mode the person is in; empty for none.</summary>
    public const string ActiveId = "modesActiveId";

    /// <summary>Visibility of the hosted utility row of the Quick panel (<see cref="PanelKeys.Utility"/>).</summary>
    public const string PanelUtility = "panelUtilityModes";

    /// <summary>Prefix of the shortcut storage key of a mode: <c>modesShortcut.&lt;modeId&gt;</c>.</summary>
    public const string ShortcutPrefix = "modesShortcut.";

    public static void Register(IAppSettings app)
    {
        app.Register(Enabled, true);
        app.Register(ActiveId, string.Empty);

        // Modes already has a row on the Quick toggles tab, so the hosted utility is off by default: it would show twice.
        app.Register(PanelUtility, false);
    }
}

/// <summary>
/// A request that crosses from the "Save what's open now" action to the Settings section: the action opens Settings at the Modes
/// card and sets the flag, and the section takes it when it is built and opens the capture window.
/// </summary>
internal static class ModesRequests
{
    private static volatile bool s_capture;

    /// <summary>Asks the Modes settings section to open the capture window the next time it is built.</summary>
    public static void RequestCapture() => s_capture = true;

    /// <summary>True once after <see cref="RequestCapture"/>.</summary>
    public static bool TakeCapture()
    {
        var requested = s_capture;
        s_capture = false;
        return requested;
    }
}

/// <summary>
/// Modes: one-click workspaces that open the apps, folders and browser tabs of a task and put the others away (spec 08 part 2). The
/// feature publishes the runtime for the island page, the tray panel and the other surfaces, keeps one global shortcut role per mode,
/// and on stop never minimizes or closes anything.
/// </summary>
public sealed class ModesFeature : IFeature
{
    private readonly Dictionary<string, IDisposable> _bindings = [];
    private IFeatureContext? _context;
    private ModesRuntime? _runtime;
    private ModesAutoRunner? _auto;

    public string Id => ModesKeys.FeatureId;

    private static bool DevActions => Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1";

    public void Start(IFeatureContext context)
    {
        _context = context;
        ModesKeys.Register(context.App);
        var runtime = new ModesRuntime(context, new ModesStore(context.DataDirectory, context.Log));
        _runtime = runtime;
        context.Services.Provide(runtime);
        context.Services.Provide<IAwayWorkspace>(runtime);
        runtime.ModesEdited += SyncShortcuts;
        SyncShortcuts();

        // The cross-project face of Modes (the island's Focus session uses it) and the automatic starts.
        context.Services.Provide<IModesService>(runtime);
        ModesAutoKeys.Register(context.App);
        var auto = new ModesAutoRunner(context, runtime, new ModesRulesStore(context.DataDirectory, context.Log));
        _auto = auto;
        context.Services.Provide(auto);
        auto.Start();
        context.Log.Info($"[modes] started: {runtime.Modes.Count} modes");
    }

    public void Stop()
    {
        var context = _context;
        var runtime = _runtime;
        if (context is null || runtime is null)
        {
            return;
        }

        // The automatic starts go first: they unhook and start nothing, and nobody can ask for a mode through a withdrawn service.
        if (_auto is { } auto)
        {
            auto.Stop();
            context.Services.Withdraw(auto);
            _auto = null;
        }

        context.Services.Withdraw<IModesService>(runtime);
        runtime.ModesEdited -= SyncShortcuts;
        context.Services.Withdraw<IAwayWorkspace>(runtime);
        context.Services.Withdraw(runtime);
        foreach (var (modeId, binding) in _bindings)
        {
            binding.Dispose();
            context.Shortcuts.RemoveRole(ModesRuntime.RoleId(modeId));
        }

        _bindings.Clear();
        runtime.Stop();
        context.Log.Info("[modes] stopped");
        _runtime = null;
        _context = null;
    }

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            if (_runtime is not { } runtime || _context is not { } context)
            {
                return [];
            }

            var list = new List<FeatureAction>();
            foreach (var mode in runtime.Modes)
            {
                var id = mode.Id;
                list.Add(new FeatureAction("mode." + id, ModesStrings.ActionMode(mode.Name), () => runtime.Enter(id, "command-bar"), mode.Glyph));
            }

            if (runtime.Active is { } active)
            {
                list.Add(new FeatureAction("leave", ModesStrings.ActionLeave(active.Name), () => runtime.Leave("command-bar"), ""));
            }

            list.Add(new FeatureAction("save-current", ModesStrings.ActionSaveCurrent, () =>
            {
                ModesRequests.RequestCapture();
                context.Shell.OpenSettings("quick-tools#modes");
            }, ""));

            if (DevActions)
            {
                list.Add(new FeatureAction("dump", "Modes: dump state (counts only)", runtime.Dump));
                list.Add(new FeatureAction("dev-capture", "Modes: capture what is open (counts only)", () => LogCapture(runtime, context.Log)));
            }

            return list;
        }
    }

    /// <summary>One shortcut role and one binding per mode; roles and bindings of deleted modes go away, and renamed modes get their new title.</summary>
    private void SyncShortcuts()
    {
        if (_context is not { } context || _runtime is not { } runtime)
        {
            return;
        }

        var modes = runtime.Modes;
        foreach (var mode in modes)
        {
            var id = mode.Id;
            var roleId = ModesRuntime.RoleId(id);
            context.Shortcuts.DefineRole(new ShortcutRole(roleId, ModesKeys.ShortcutPrefix + id, ModesStrings.ActionMode(mode.Name), ModesKeys.FeatureId,
                FeatureGroup.Tools, null, [ModesKeys.Enabled], Section: ModesStrings.ShortcutSection));
            if (!_bindings.ContainsKey(id))
            {
                _bindings[id] = context.Shortcuts.Bind(roleId, () => runtime.Enter(id, "shortcut"));
            }
        }

        foreach (var gone in _bindings.Keys.Where(k => modes.All(m => m.Id != k)).ToList())
        {
            _bindings[gone].Dispose();
            _bindings.Remove(gone);
            context.Shortcuts.RemoveRole(ModesRuntime.RoleId(gone));
            context.App.Reset(ModesKeys.ShortcutPrefix + gone);
        }
    }

    /// <summary>Dev action: captures what is open and logs only how many items of each kind were found.</summary>
    private static async void LogCapture(ModesRuntime runtime, ILog log)
    {
        try
        {
            var capture = await runtime.CaptureAsync();
            var apps = capture.Items.Count(c => c.Item.Kind == ModeItemKind.App);
            var web = capture.Items.Count(c => c.Item.Kind == ModeItemKind.Web);
            var paths = capture.Items.Count(c => c.Item.Kind == ModeItemKind.Path);
            log.Info($"[modes] dev-capture: {apps} apps, {web} browser windows, {paths} folders, {capture.Warnings.Count} warnings");
        }
        catch (Exception ex)
        {
            log.Error("[modes] dev-capture failed", ex);
        }
    }
}
