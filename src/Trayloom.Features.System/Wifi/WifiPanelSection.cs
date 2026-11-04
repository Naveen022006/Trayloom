// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>The Wi-Fi card at the bottom of the tray's Network tab (spec 06 section 3.9). Data is read when the tab becomes active, never while the flyout is closed.</summary>
public sealed class WifiPanelSection : IPanelSection
{
    public string? FeatureId => WifiKeys.FeatureId;

    public string TabId => PanelTabs.Network;

    public int Order => 900;

    public FrameworkElement CreateView(IPanelContext context)
    {
        if (context.Services.Get<WifiService>() is not { } service)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        return new WifiPanelCard(service, context);
    }
}

/// <summary>Content of <see cref="WifiPanelSection"/>; redrawn from the service snapshot.</summary>
internal sealed class WifiPanelCard : Border
{
    private readonly WifiService _service;
    private readonly IPanelContext _context;
    private bool _attached;

    public WifiPanelCard(WifiService service, IPanelContext context)
    {
        _service = service;
        _context = context;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Rebuild();
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _service.Changed += OnChanged;
        _context.ActiveChanged += OnActive;
        OnActive();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _service.Changed -= OnChanged;
        _context.ActiveChanged -= OnActive;
    }

    private void OnActive()
    {
        if (_context.IsActive)
        {
            WifiGuard.FireAndLog(() => _service.LoadAsync(false), _context.Log, "tray card load");
        }
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        Rebuild();
        _context.RequestRelayout();
    });

    private void Rebuild()
    {
        var s = _service.Snapshot;
        // No Wi-Fi hardware: the card hides once Windows said so.
        if (s.Loaded && !s.HasAdapter)
        {
            Visibility = Visibility.Collapsed;
            Child = null;
            return;
        }

        Visibility = Visibility.Visible;
        var group = new PanelRowGroup();
        var on = s.Radio == WifiRadioState.On;
        var caption = s.Radio switch
        {
            WifiRadioState.Off => WifiStrings.Off,
            WifiRadioState.Disabled => WifiStrings.HardwareOff,
            _ when s.Connection is { } c => c.Ssid,
            _ when s.ConnectedWithoutName => WifiStrings.Connected,
            _ => WifiStrings.NotConnected,
        };
        var glyph = WifiRules.SignalGlyph(s.Connection?.Bars ?? (s.Connected ? 4 : 0), s.Radio != WifiRadioState.On);
        var toggle = new PanelToggleRow(glyph, WifiStrings.Title, caption, on, value => WifiFeature.ToggleRadio(_service, _context, value), true)
        {
            IsEnabled = s.Radio is WifiRadioState.On or WifiRadioState.Off,
        };
        toggle.Loaded += (_, _) => NameSwitch(toggle);
        group.Add(toggle);
        group.Add(new PanelActionRow("", WifiStrings.NetworksEllipsis, null, () => _context.InvokeAfterClose(() => WifiKeys.OpenUri("ms-availablenetworks:"))));
        if (s.LocationNeeded)
        {
            group.Add(new PanelActionRow("", WifiStrings.AllowLocation, WifiLocation.Caption(), () => _context.InvokeAfterClose(() => WifiKeys.OpenUri("ms-settings:privacy-location")), null, false, true));
        }

        Child = group;
    }

    /// <summary>Gives the row's switch its UI Automation id so it can be read (its toggle state) without being operated.</summary>
    private static void NameSwitch(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is MacSwitch sw)
            {
                WifiLook.Automate(sw, "Wifi.PanelRadio", WifiStrings.Title);
                return;
            }

            NameSwitch(child);
        }
    }
}
