// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>
/// Shared plumbing of the Tools features (R2): flat <see cref="IAppSettings"/> access, shortcut-role binding through
/// <see cref="IShortcutService"/>, safe invocation and teardown. Catalog name / description / group come from the catalog.
/// </summary>
public abstract class ToolsFeatureBase : IFeature
{
    private readonly List<IDisposable> _binds = [];

    /// <summary>The feature context (valid between Start and Stop).</summary>
    protected IFeatureContext Ctx { get; private set; } = null!;

    /// <summary>The flat settings store.</summary>
    protected IAppSettings App => Ctx.App;

    /// <summary>True between Start and Stop.</summary>
    protected bool IsRunning { get; private set; }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<FeatureAction> Actions => Array.Empty<FeatureAction>();

    /// <inheritdoc />
    public void Start(IFeatureContext context)
    {
        Ctx = context;
        IsRunning = true;
        RegisterDefaults(context.App);
        context.App.Changed += OnAppChanged;
        try
        {
            OnStart();
        }
        catch (Exception ex)
        {
            context.Log.Error(Id + " failed to start", ex);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        IsRunning = false;
        if (Ctx is not null)
        {
            Ctx.App.Changed -= OnAppChanged;
        }

        try
        {
            OnStop();
        }
        catch (Exception ex)
        {
            Ctx?.Log.Error(Id + " failed to stop", ex);
        }
        finally
        {
            foreach (var bind in _binds)
            {
                bind.Dispose();
            }

            _binds.Clear();
        }
    }

    /// <summary>Registers the Vorssaint default values of the keys this feature reads.</summary>
    protected virtual void RegisterDefaults(IAppSettings app)
    {
    }

    /// <summary>Starts the feature (hotkeys, hooks, windows).</summary>
    protected abstract void OnStart();

    /// <summary>Undoes every side effect.</summary>
    protected abstract void OnStop();

    /// <summary>A flat setting changed (called on the UI thread).</summary>
    protected virtual void OnSettingChanged(string key)
    {
    }

    /// <summary>Binds a shortcut role (registered while the role is active); disposed on Stop.</summary>
    protected void BindRole(string roleId, Action callback) => _binds.Add(Ctx.Shortcuts.Bind(roleId, () => Safe(callback)));

    /// <summary>Runs an action, logging (and notifying) instead of throwing.</summary>
    protected void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Ctx.Log.Error(Id + " action failed", ex);
        }
    }

    /// <summary>Starts a feature-owned subscription that is disposed on Stop.</summary>
    protected T Own<T>(T disposable) where T : IDisposable
    {
        _binds.Add(disposable);
        return disposable;
    }

    private void OnAppChanged(string key)
    {
        var ui = Ctx.Ui;
        if (ui.CheckAccess())
        {
            Dispatch(key);
        }
        else
        {
            ui.BeginInvoke(() => Dispatch(key));
        }
    }

    private void Dispatch(string key)
    {
        if (IsRunning)
        {
            OnSettingChanged(key);
        }
    }
}

/// <summary>Helpers for talking to features of other projects through the action catalog.</summary>
internal static class ActionBridge
{
    /// <summary>Finds the action of a feature (catalog id); prefers <paramref name="actionIds"/> in order, else the first action.</summary>
    public static FeatureAction? Find(IContributionServices ctx, string catalogId, params string[] actionIds)
    {
        var mine = ctx.Actions.All.Where(a => FeatureCatalog.Resolve(a.Feature.Id) == catalogId).Select(a => a.Action).ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        foreach (var id in actionIds)
        {
            var hit = mine.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        return actionIds.Length == 0 ? mine[0] : mine[0];
    }

    /// <summary>Invokes the action; false when the feature is not running.</summary>
    public static bool Invoke(IContributionServices ctx, string catalogId, params string[] actionIds)
    {
        var action = Find(ctx, catalogId, actionIds);
        if (action is null)
        {
            return false;
        }

        try
        {
            action.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ctx.Log.Error($"{catalogId}/{action.Id} failed", ex);
            return false;
        }
    }
}

/// <summary>Text of the shortcut currently registered for a role (null while it does not fire).</summary>
internal static class ShortcutText
{
    public static string? Of(IContributionServices ctx, string roleId)
    {
        var state = ctx.Shortcuts.GetState(roleId);
        return state.IsActive && (state.Effective ?? state.Configured) is { } hotkey ? hotkey.ToString() : null;
    }

    /// <summary>The configured shortcut text regardless of activation (footers print it).</summary>
    public static string Configured(IContributionServices ctx, string roleId)
    {
        var state = ctx.Shortcuts.GetState(roleId);
        return (state.Effective ?? state.Configured)?.ToString() ?? string.Empty;
    }
}

/// <summary>Registers every default of the flat keys the Tools project owns (Vorssaint values).</summary>
internal static class ToolsDefaults
{
    public static void Register(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            ["quickLauncherShortcutEnabled"] = true,
            ["quickLauncherItemOrder"] = "",
            ["quickLauncherHiddenItems"] = "",
            ["commandBarShortcutEnabled"] = false,
            ["commandBarCompactMode"] = false,
            ["commandBarASCIILayoutEnabled"] = false,
            ["commandBarEmojiSkinTone"] = "",
            ["commandBarDisabledSources"] = "",
            ["commandBarAliases"] = "",
            ["commandBarPins"] = "",
            ["commandBarHidden"] = "",
            ["commandBarLinks"] = "",
            ["commandBarRowShortcuts"] = "",
            ["commandBarPositionOffset"] = "",
            ["commandBarFileScopes"] = "",
            ["commandBarFileIgnores"] = "",
            ["panelUtilityCommandBar"] = true,
            ["panelToggleOrder"] = "",
            ["panelToggleDarkMode"] = true,
            ["panelToggleKeyboardLight"] = true,
            ["panelUtilityMicMute"] = true,
            ["panelToggleEmptyTrash"] = true,
            ["panelToggleQuitAllApps"] = true,
            ["panelToggleEjectDisks"] = true,
            ["panelToggleHiddenFiles"] = true,
            ["panelToggleDesktopIcons"] = true,
            ["panelToggleLockScreen"] = true,
            ["panelToggleDisplayOff"] = true,
            ["panelToggleScreenSaver"] = true,
            ["quitAppsShortcutEnabled"] = false,
            ["quitAppsExceptFrontShortcutEnabled"] = false,
            ["wallpaperApplyAllDisplays"] = true,
            ["wallpaperFilter"] = "all",
            ["panelShowWallpaper"] = true,
            ["scratchpadShortcutEnabled"] = false,
            ["scratchpadRetention"] = "never",
            ["scratchpadCloseOnClickOutside"] = true,
            ["scratchpadBackgroundOpacity"] = 0.0,
            ["scratchpadTextSize"] = 13.0,
            ["panelUtilityScratchpad"] = true,
            ["cameraPreviewShortcutEnabled"] = false,
            ["panelUtilityCameraPreview"] = true,
            ["cleaningModeKeepScreenVisible"] = false,
            ["panelUtilityCleaning"] = true,
            ["radialMenuEnabled"] = false,
            ["radialMenuAtPointer"] = true,
            ["radialMenuActivationMode"] = "pressOrHold",
            ["panelControlRadialMenu"] = true,
            ["colorPickerFormat"] = "hex",
            ["colorPickerBareHex"] = false,
            ["micMuteMenuBarIndicator"] = true,
            ["clipboardHistoryLimit"] = 50,
        });
    }
}
