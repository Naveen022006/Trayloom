// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Shared plumbing for the Tools features: context handling, re-bindable hotkeys and safe teardown.</summary>
public abstract class ToolFeatureBase : IFeature
{
    private readonly Dictionary<string, IDisposable> _hotkeys = new();
    private readonly Dictionary<string, (Hotkey? Fallback, Action Callback)> _bindings = new();
    private bool _warned;

    protected IFeatureContext Ctx { get; private set; } = null!;

    protected bool IsRunning { get; private set; }

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public FeatureGroup Group => FeatureGroup.Tools;

    public virtual bool InstalledByDefault => false;

    public virtual IReadOnlyList<FeatureAction> Actions => Array.Empty<FeatureAction>();

    public void Start(IFeatureContext context)
    {
        Ctx = context;
        IsRunning = true;
        context.Settings.Changed += OnSettingChanged;
        OnStart();
    }

    public void Stop()
    {
        IsRunning = false;
        if (Ctx is not null)
        {
            Ctx.Settings.Changed -= OnSettingChanged;
        }

        try
        {
            OnStop();
        }
        finally
        {
            foreach (var hk in _hotkeys.Values)
            {
                hk.Dispose();
            }

            _hotkeys.Clear();
            _bindings.Clear();
        }
    }

    public virtual FrameworkElement? CreateSettingsView() => null;

    protected abstract void OnStart();

    protected abstract void OnStop();

    /// <summary>Hook for subclasses to react to a setting change (called on the UI thread).</summary>
    protected virtual void OnSettingChangedUi(string key)
    {
    }

    /// <summary>Registers a global hotkey stored under a setting key; re-registers when the setting changes.</summary>
    protected void BindHotkey(string key, Hotkey? fallback, Action callback)
    {
        _bindings[key] = (fallback, callback);
        Rebind(key);
    }

    private void Rebind(string key)
    {
        if (!_bindings.TryGetValue(key, out var binding))
        {
            return;
        }

        if (_hotkeys.Remove(key, out var old))
        {
            old.Dispose();
        }

        var hotkey = Ctx.Settings.Get<Hotkey?>(key, binding.Fallback);
        if (hotkey is null)
        {
            return;
        }

        if (Ctx.Hotkeys.TryRegister(hotkey, () => Safe(binding.Callback), out var registration))
        {
            _hotkeys[key] = registration;
        }
        else if (!_warned)
        {
            _warned = true;
            Ctx.Notifier.Show(Name, $"The shortcut {hotkey} is already used by another program. Choose another one in the feature settings.", NotificationLevel.Warning);
        }
    }

    protected void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Ctx.Log.Error($"{Id} action failed", ex);
            Ctx.Notifier.Show(Name, ex.Message, NotificationLevel.Error);
        }
    }

    private void OnSettingChanged(string key)
    {
        Ctx.Ui.BeginInvoke(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            if (_bindings.ContainsKey(key))
            {
                _warned = false;
                Rebind(key);
            }

            OnSettingChangedUi(key);
        });
    }
}
