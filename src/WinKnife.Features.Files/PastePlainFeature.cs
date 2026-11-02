// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>Pastes the clipboard text without any formatting, then puts the original clipboard back.</summary>
public sealed class PastePlainFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.V);

    private IFeatureContext? _context;
    private HotkeyBindings? _bindings;
    private bool _busy;

    public string Id => "paste-plain";

    public string Name => "Paste as plain text";

    public string Description => "Paste the clipboard as plain text, without fonts, colors or links, with one shortcut.";

    public FeatureGroup Group => FeatureGroup.ClipboardFiles;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("paste-plain", "Paste as plain text", () => Run(), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _bindings = new HotkeyBindings(context,
        [
            new HotkeyBinding("hotkey", "Paste as plain text", DefaultHotkey, () => Run()),
        ]);
    }

    public void Stop()
    {
        _bindings?.Dispose();
        _bindings = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddHotkey("hotkey", "Shortcut", DefaultHotkey)
            .AddToggle("trim", "Trim spaces and line breaks around the text", false)
            .AddIntSlider("restoreDelay", "Restore clipboard after (ms)", 200, 2000, 500, "How long the plain text stays on the clipboard so the target app can read it before your original content returns.")
            .AddNote("Your original clipboard (including images, files and rich text) is put back after pasting.")
            .Build();
    }

    private async void Run()
    {
        var context = _context;
        if (context is null || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var text = ClipboardIO.ReadText();
            if (string.IsNullOrEmpty(text))
            {
                context.Notifier.Show("Paste as plain text", "The clipboard does not contain text.");
                return;
            }

            if (context.Settings.Get("trim", false))
            {
                text = text.Trim();
            }

            // The shortcut's own modifier keys are still down; Ctrl+Alt+V would not paste. Wait for them.
            await ModifierHelper.ReleaseAsync(1500);
            if (_context is null)
            {
                return;
            }

            if (ForegroundHelper.HandBackFromOwnWindow())
            {
                await Task.Delay(150);
            }

            var snapshot = ClipboardSnapshot.Capture();
            if (snapshot.HasFileData)
            {
                context.Notifier.Show("Paste as plain text", "This content (file transfer) cannot be reduced to plain text safely.");
                return;
            }

            if (!ClipboardIO.WriteText(text, excludeFromHistory: true))
            {
                context.Notifier.Show("Paste as plain text", "The clipboard is busy. Try again.", NotificationLevel.Warning);
                return;
            }

            var sequence = WinApi.GetClipboardSequenceNumber();
            InputSender.SendChord((ushort)VK_CONTROL, (ushort)'V');

            await Task.Delay(Math.Clamp(context.Settings.Get("restoreDelay", 500), 100, 5000));
            if (WinApi.GetClipboardSequenceNumber() == sequence)
            {
                // Nothing else was copied meanwhile: bring back the original formats.
                snapshot.Restore();
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Paste as plain text failed", ex);
        }
        finally
        {
            _busy = false;
        }
    }
}
