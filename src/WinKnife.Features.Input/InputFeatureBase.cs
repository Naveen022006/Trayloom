// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Hook priorities used by the Input features (higher runs first; the first handler that swallows an event
/// hides it from the rest). The order is deliberate:
/// <list type="bullet">
/// <item>Keyboard: key-debounce (500) filters chatter first, then super-key (400) turns its key into modifiers,
/// then close-protection (300) judges the (possibly modified) chord, and text-snippets (100) only observes.</item>
/// <item>Mouse buttons: click-debounce (500) drops bounces before anyone reacts to them, then
/// mouse-button-shortcuts (300), then side-buttons (200). A button mapped in both is handled by
/// mouse-button-shortcuts.</item>
/// <item>Wheel: horizontal-scroll, invert-scroll, linear-scroll and smooth-scroll all share ONE hook handler
/// chain (see <see cref="WheelPipeline"/>) so they compose in a fixed order: horizontal, invert, linear, smooth.</item>
/// </list>
/// Everything WinKnife injects is tagged (<c>IsFromWinKnife</c>) and ignored by every handler here, which prevents
/// feedback loops; non-WinKnife injected input is left alone by the filters (debounce, remappers).
/// </summary>
internal static class HookPriorities
{
    public const int KeyDebounce = 500;
    public const int SuperKey = 400;
    public const int CloseProtection = 300;
    public const int TextSnippets = 100;

    public const int ClickDebounce = 500;
    public const int MouseButtonShortcuts = 300;
    public const int SideButtons = 200;
    public const int SnippetMouse = 50;

    public const int Wheel = 100;
}

/// <summary>
/// Common plumbing for the Input features: remembers the context, tracks hook subscriptions (disposed on Stop),
/// reloads settings when they change and offers a Pause/Resume action.
/// </summary>
public abstract class InputFeatureBase : IFeature
{
    private readonly List<IDisposable> _tracked = [];
    private volatile bool _paused;

    protected IFeatureContext? Context { get; private set; }

    /// <summary>True while the user paused the feature through its action (not persisted).</summary>
    protected bool Paused => _paused;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public virtual FeatureGroup Group => FeatureGroup.MouseKeyboard;

    /// <summary>Whether a Pause/Resume action is offered.</summary>
    protected virtual bool SupportsPause => true;

    protected virtual IEnumerable<FeatureAction> ExtraActions => [];

    public IReadOnlyList<FeatureAction> Actions
    {
        get
        {
            var list = new List<FeatureAction>();
            if (SupportsPause)
            {
                var paused = _paused;
                list.Add(new FeatureAction(
                    "toggle-pause",
                    paused ? $"Resume: {Name}" : $"Pause: {Name}",
                    () => SetPaused(!_paused),
                    paused ? "" : ""));
            }

            list.AddRange(ExtraActions);
            return list;
        }
    }

    public void Start(IFeatureContext context)
    {
        Context = context;
        _paused = false;
        context.Settings.Changed += HandleSettingChanged;
        LoadSettings();
        OnStart();
    }

    public void Stop()
    {
        var context = Context;
        if (context is not null)
        {
            context.Settings.Changed -= HandleSettingChanged;
        }

        try
        {
            OnStop();
        }
        finally
        {
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
            Context = null;
        }
    }

    public virtual FrameworkElement? CreateSettingsView()
    {
        if (Context is null)
        {
            return null;
        }

        var builder = new SettingsViewBuilder(Context.Settings);
        BuildSettings(builder);
        return builder.Build();
    }

    /// <summary>Subscribe to hooks / start timers here. <see cref="Context"/> is set.</summary>
    protected abstract void OnStart();

    /// <summary>Undo everything not covered by <see cref="Track"/>.</summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Read settings into volatile fields. Called on start and on every settings change (any thread).</summary>
    protected virtual void LoadSettings()
    {
    }

    /// <summary>Called after <see cref="LoadSettings"/> for a changed key (any thread).</summary>
    protected virtual void OnSettingChanged(string key)
    {
    }

    protected virtual void OnPausedChanged(bool paused)
    {
    }

    protected virtual void BuildSettings(SettingsViewBuilder builder)
    {
    }

    protected void Track(IDisposable disposable) => _tracked.Add(disposable);

    protected void SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority) =>
        Track(Context!.Hooks.SubscribeMouse(handler, priority));

    protected void SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority) =>
        Track(Context!.Hooks.SubscribeKeyboard(handler, priority));

    protected void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        OnPausedChanged(paused);
    }

    private void HandleSettingChanged(string key)
    {
        if (Context is null)
        {
            return;
        }

        LoadSettings();
        OnSettingChanged(key);
    }
}
