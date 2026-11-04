// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the island's own controls that need WPF but no window: the automation peer of <see cref="HoverButton"/> (screen readers and UI
/// Automation see a named, invokable Button) and the artwork decoder (a picture that cannot be decoded is "no picture", cached, never thrown).
/// </summary>
internal static class IslandUiChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        ArtworkFailures();
        RunSta(ButtonPeer);
    }

    private static void Check(bool ok, string what) => _check(ok, "island ui: " + what);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            Check(false, "an STA check threw " + failure.GetType().Name + ": " + failure.Message);
        }
    }

    /// <summary>Lets a queued BeginInvoke run (the peer invokes asynchronously).</summary>
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void ButtonPeer()
    {
        var clicks = 0;
        var button = new HoverButton(() => clicks++) { Width = 28, Height = 28 };
        AutomationProperties.SetAutomationId(button, "Island.Test.Button");
        AutomationProperties.SetName(button, "Search");
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Check(peer is not null && peer.GetAutomationControlType() == AutomationControlType.Button, "a HoverButton is a Button for UI Automation");
        Check(peer?.GetName() == "Search", "its name is the automation name");
        Check(peer?.GetAutomationId() == "Island.Test.Button", "an automation id set on it is exposed");
        Check(peer?.IsControlElement() == true && peer.IsContentElement(), "it is in the control and content views");
        var invoke = peer?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Check(invoke is not null, "it offers the Invoke pattern");
        invoke?.Invoke();
        Pump();
        Check(clicks == 1, "Invoke runs the same action as a click");

        button.IsEnabled = false;
        var refused = false;
        try
        {
            invoke?.Invoke();
        }
        catch (ElementNotEnabledException)
        {
            refused = true;
        }

        Pump();
        Check(refused && clicks == 1, "a disabled button refuses Invoke and does nothing");

        // the name falls back to the tooltip, then to the text inside; the icon glyph is never a name
        var tipped = new HoverButton(() => { }) { ToolTip = "Clear" };
        Check(UIElementAutomationPeer.CreatePeerForElement(tipped).GetName() == "Clear", "no automation name: the tooltip is the name");
        var pill = new HoverButton(() => { }) { Child = new TextBlock { Text = "Show all (12)" } };
        pill.Measure(new System.Windows.Size(200, 30));
        pill.Arrange(new System.Windows.Rect(0, 0, 200, 30));
        Check(UIElementAutomationPeer.CreatePeerForElement(pill).GetName() == "Show all (12)", "no name and no tooltip: the button's text is the name");
        var glyph = new TextBlock { Text = "" };
        var icon = new HoverButton(() => { }) { Child = glyph, Icon = glyph };
        Check(UIElementAutomationPeer.CreatePeerForElement(icon).GetName() == "", "an icon glyph alone is not a name");
    }

    private static void ArtworkFailures()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trayloom-harness-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "broken.jpg");
            File.WriteAllBytes(bad, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            var truncated = Path.Combine(dir, "truncated.png");
            File.WriteAllBytes(truncated, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13]);
            var empty = Path.Combine(dir, "empty.jpg");
            File.WriteAllBytes(empty, []);
            var missing = Path.Combine(dir, "gone.jpg");
            foreach (var path in new[] { bad, truncated, empty, missing })
            {
                var name = Path.GetFileName(path);
                Check(!Threw(() => AmArtwork.Decode(path)) && AmArtwork.Decode(path) is null, $"artwork: '{name}' decodes to no picture without throwing");
                var first = AmArtwork.GetForPathAsync(path).GetAwaiter().GetResult();
                Check(first is null && AmArtwork.TryGetCached(path, out var cached) && cached is null, $"artwork: '{name}' failure is cached as no picture");
                Check(AmArtwork.GetForPathAsync(path).GetAwaiter().GetResult() is null, $"artwork: '{name}' is not decoded again");
            }

            // a picture Apple Music has open for writing can still be read
            var shared = Path.Combine(dir, "shared.jpg");
            using (var writer = new FileStream(shared, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write([9, 9, 9]);
                writer.Flush();
                Check(!Threw(() => AmArtwork.Decode(shared)), "artwork: a file another process has open for writing is read without an exception");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool Threw(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
