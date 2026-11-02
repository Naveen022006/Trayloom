// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Turns "Enhance pointer precision" (mouse acceleration) off while the feature runs and puts back the exact
/// previous SPI_GETMOUSE values on Stop. The change is session-only (no SPIF_UPDATEINIFILE), so a crash or log-off
/// can never leave it disabled permanently; the original values are also kept in the feature's settings
/// (<c>savedMouse</c>) so the next start can restore them after an unclean exit.
/// </summary>
public sealed class MouseAccelerationFeature : IFeature
{
    private const string BackupKey = "savedMouse";

    private IFeatureContext? _context;
    private int[]? _original;
    private bool _disabled;

    public string Id => "mouse-acceleration";

    public string Name => "Disable mouse acceleration";

    public string Description => "Turn off Windows pointer acceleration so the cursor moves the same distance at any speed.";

    public FeatureGroup Group => FeatureGroup.MouseKeyboard;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction(
            "toggle",
            _disabled ? "Restore pointer acceleration" : "Disable pointer acceleration",
            () => SetDisabled(!_disabled),
            ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        var current = new int[3];
        if (!NativeMethods.SystemParametersInfoW(NativeConstants.SPI_GETMOUSE, 0, current, 0))
        {
            context.Log.Error("SPI_GETMOUSE failed; mouse acceleration is left unchanged");
            return;
        }

        // After a crash the saved copy holds the real original while the live values are still "off".
        var backup = context.Settings.Get<int[]?>(BackupKey, null);
        _original = backup is { Length: 3 } && current[2] == 0 ? backup : current;
        context.Settings.Set<int[]?>(BackupKey, _original);
        SetDisabled(true);
    }

    public void Stop()
    {
        if (_original is not null)
        {
            SetDisabled(false);
            _context?.Settings.Set<int[]?>(BackupKey, null);
        }

        _original = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddNote("While this feature is installed, \"Enhance pointer precision\" is switched off, and switched back to its previous state when the feature is removed or WinKnife quits. Pointer speed itself is not changed; adjust it in Windows mouse settings.")
            .Build();
    }

    private void SetDisabled(bool disabled)
    {
        if (_original is null || _context is null)
        {
            return;
        }

        var values = (int[])_original.Clone();
        if (disabled)
        {
            values[2] = 0;
        }

        if (NativeMethods.SystemParametersInfoW(NativeConstants.SPI_SETMOUSE, 0, values, NativeConstants.SPIF_SENDCHANGE))
        {
            _disabled = disabled;
            _context.Log.Info(disabled
                ? "Pointer acceleration disabled"
                : $"Pointer acceleration restored ({values[0]}, {values[1]}, {values[2]})");
        }
        else
        {
            _context.Log.Error($"SPI_SETMOUSE failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }
    }
}
