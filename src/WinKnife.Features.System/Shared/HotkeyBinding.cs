// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>Registers a configurable global hotkey (a <see cref="Hotkey"/> setting) and re-registers it when the setting changes.</summary>
internal sealed class HotkeyBinding : IDisposable
{
    private readonly IFeatureContext _context;
    private readonly string _key;
    private readonly Hotkey? _fallback;
    private readonly Action _callback;
    private IDisposable? _registration;
    private bool _disposed;

    public HotkeyBinding(IFeatureContext context, string settingKey, Hotkey? fallback, Action callback)
    {
        _context = context;
        _key = settingKey;
        _fallback = fallback;
        _callback = callback;
        _context.Settings.Changed += OnChanged;
        Apply();
    }

    public void Dispose()
    {
        _disposed = true;
        _context.Settings.Changed -= OnChanged;
        _registration?.Dispose();
        _registration = null;
    }

    private void OnChanged(string key)
    {
        if (key == _key)
        {
            _context.Ui.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        _registration?.Dispose();
        _registration = null;
        var hotkey = _context.Settings.Get<Hotkey?>(_key, _fallback);
        if (hotkey is null)
        {
            return;
        }

        if (!_context.Hotkeys.TryRegister(hotkey, _callback, out _registration))
        {
            _context.Log.Warn($"Hotkey {hotkey} for '{_key}' is already in use.");
        }
    }
}
