// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using WinKnife.Core;
using WinKnife.Core.Native;

namespace WinKnife.Features.Input;

/// <summary>
/// "Disable mouse acceleration": turns "Enhance pointer precision" off while the switch is on and puts back the exact
/// previous SPI_GETMOUSE triple when it is turned off, the feature is uninstalled or WinKnife quits. The change is
/// session-only (no SPIF_UPDATEINIFILE), and the original triple is journaled in the feature data folder before
/// anything is changed so the next launch restores it after an unclean exit. Pointer speed is never changed.
/// </summary>
public sealed class MouseAccelerationFeature : InputFeatureBase
{
    private const string JournalFile = "mouse-acceleration.json";

    private int[]? _original;

    public override string Id => "mouse-acceleration";

    protected override bool IsEnabled() => App.Get<bool>(K.AccelDisabled);

    protected override IEnumerable<FeatureAction> ExtraActions => [];

    protected override void OnInstalled() => RecoverFromCrash();

    protected override void Engage()
    {
        var current = new int[3];
        if (!NativeMethods.SystemParametersInfoW(NativeConstants.SPI_GETMOUSE, 0, current, 0))
        {
            Context!.Log.Error("SPI_GETMOUSE failed; mouse acceleration is left unchanged");
            return;
        }

        _original = (int[])current.Clone();
        WriteJournal(_original);
        Apply(disabled: true);
    }

    protected override void Disengage()
    {
        if (_original is null)
        {
            return;
        }

        Apply(disabled: false);
        DeleteJournal();
        _original = null;
    }

    private string JournalPath => System.IO.Path.Combine(Context!.DataDirectory, JournalFile);

    // After a crash the journal still holds the real original while the live values are still "off".
    private void RecoverFromCrash()
    {
        try
        {
            if (!File.Exists(JournalPath))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<int[]>(File.ReadAllText(JournalPath));
            if (saved is { Length: 3 })
            {
                NativeMethods.SystemParametersInfoW(NativeConstants.SPI_SETMOUSE, 0, saved, NativeConstants.SPIF_SENDCHANGE);
                Context!.Log.Info("Pointer acceleration restored from the recovery journal");
            }

            DeleteJournal();
        }
        catch (Exception ex)
        {
            Context?.Log.Error("Reading the mouse acceleration journal failed", ex);
        }
    }

    private void WriteJournal(int[] values)
    {
        try
        {
            File.WriteAllText(JournalPath, JsonSerializer.Serialize(values));
        }
        catch (Exception ex)
        {
            Context?.Log.Error("Writing the mouse acceleration journal failed", ex);
        }
    }

    private void DeleteJournal()
    {
        try
        {
            if (File.Exists(JournalPath))
            {
                File.Delete(JournalPath);
            }
        }
        catch
        {
            // best effort
        }
    }

    private void Apply(bool disabled)
    {
        if (_original is null || Context is null)
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
            Context.Log.Info(disabled
                ? "Pointer acceleration disabled"
                : $"Pointer acceleration restored ({values[0]}, {values[1]}, {values[2]})");
        }
        else
        {
            Context.Log.Error($"SPI_SETMOUSE failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }
    }
}
