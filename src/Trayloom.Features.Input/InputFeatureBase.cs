// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Input;

/// <summary>
/// Hook priorities used by the Input features (higher runs first; the first handler that swallows an event
/// hides it from the rest). The order is deliberate (spec 05 G1):
/// <list type="bullet">
/// <item>Keyboard: super key (600) turns its key into modifiers, key-debounce (500) filters chatter, then
/// quit &amp; close protection (300) judges the chord and text-snippets (100) only observes.</item>
/// <item>Mouse buttons: click-debounce (500) drops bounces before anyone reacts to them, then
/// mouse-button-shortcuts (300), then side-buttons (200).</item>
/// <item>Wheel: zoom, horizontal-scroll, invert-scroll, linear-scroll and smooth-scroll share ONE hook handler
/// chain (see <see cref="WheelPipeline"/>) so they compose in a fixed order.</item>
/// </list>
/// Everything Trayloom injects is tagged (<c>IsFromTrayloom</c>) and ignored by every handler here.
/// </summary>
internal static class HookPriorities
{
    public const int SuperKey = 600;
    public const int KeyDebounce = 500;
    public const int CloseProtection = 300;
    public const int TextSnippets = 100;

    public const int ClickDebounce = 500;
    public const int MouseButtonShortcuts = 300;
    public const int SideButtons = 200;
    public const int FocusFollows = 20;
    public const int SnippetMouse = 50;

    public const int Wheel = 100;
}

/// <summary>
/// Common plumbing for the Input features. A feature is "installed" (<see cref="Start"/> called) and "enabled" (its own
/// switch in the flat settings); it only holds hooks and timers while both are true (<see cref="Engage"/> /
/// <see cref="Disengage"/>), and re-reads its settings on every change.
/// </summary>
public abstract class InputFeatureBase : IFeature
{
    private readonly object _sync = new();
    private readonly List<IDisposable> _tracked = [];
    private bool _engaged;

    protected IFeatureContext? Context { get; private set; }

    protected IAppSettings App => Context!.App;

    /// <summary>True while the feature holds its hooks (installed and enabled).</summary>
    public bool IsEngaged => _engaged;

    public abstract string Id { get; }

    /// <summary>True when the feature's own switch is on.</summary>
    protected abstract bool IsEnabled();

    /// <summary>Subscribe to hooks / start timers here (installed and enabled). <see cref="Context"/> is set.</summary>
    protected abstract void Engage();

    /// <summary>Undo everything not covered by <see cref="Track"/> (called when switched off or stopped).</summary>
    protected virtual void Disengage()
    {
    }

    /// <summary>Called once at install (before the first <see cref="LoadSettings"/>).</summary>
    protected virtual void OnInstalled()
    {
    }

    /// <summary>Read settings into volatile fields. Called on start and on every settings change (any thread).</summary>
    protected virtual void LoadSettings()
    {
    }

    /// <summary>Called after <see cref="LoadSettings"/> for a changed key while engaged (any thread).</summary>
    protected virtual void OnSettingChanged(string key)
    {
    }

    /// <summary>Extra actions for the Command Bar / Quick panel.</summary>
    protected virtual IEnumerable<FeatureAction> ExtraActions => [];

    public IReadOnlyList<FeatureAction> Actions => [.. ExtraActions];

    public void Start(IFeatureContext context)
    {
        Context = context;
        K.RegisterDefaults(context.App);
        InputHost.App = context.App;
        InputHost.Runtime = context.Runtime;
        OnInstalled();
        LoadSettings();
        context.App.Changed += HandleSettingChanged;
        Reevaluate();
    }

    public virtual void Stop()
    {
        var context = Context;
        if (context is not null)
        {
            context.App.Changed -= HandleSettingChanged;
        }

        lock (_sync)
        {
            DisengageLocked();
        }

        Context = null;
    }

    protected void Track(IDisposable disposable)
    {
        lock (_sync)
        {
            _tracked.Add(disposable);
        }
    }

    protected void SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority) =>
        Track(Context!.Hooks.SubscribeMouse(handler, priority));

    protected void SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority) =>
        Track(Context!.Hooks.SubscribeKeyboard(handler, priority));

    /// <summary>Re-checks <see cref="IsEnabled"/> and engages / disengages accordingly.</summary>
    protected void Reevaluate()
    {
        lock (_sync)
        {
            if (Context is null)
            {
                return;
            }

            var want = false;
            try
            {
                want = IsEnabled();
            }
            catch (Exception ex)
            {
                Context.Log.Error($"{Id}: enable check failed", ex);
            }

            if (want && !_engaged)
            {
                _engaged = true;
                try
                {
                    Engage();
                }
                catch (Exception ex)
                {
                    Context.Log.Error($"{Id}: starting failed", ex);
                    DisengageLocked();
                }
            }
            else if (!want && _engaged)
            {
                DisengageLocked();
            }
        }
    }

    private void DisengageLocked()
    {
        if (!_engaged)
        {
            return;
        }

        _engaged = false;
        try
        {
            Disengage();
        }
        catch (Exception ex)
        {
            Context?.Log.Error($"{Id}: stopping failed", ex);
        }

        foreach (var item in _tracked)
        {
            try
            {
                item.Dispose();
            }
            catch
            {
                // best effort
            }
        }

        _tracked.Clear();
    }

    private void HandleSettingChanged(string key)
    {
        if (Context is null)
        {
            return;
        }

        LoadSettings();
        Reevaluate();
        if (_engaged)
        {
            OnSettingChanged(key);
        }
    }
}
