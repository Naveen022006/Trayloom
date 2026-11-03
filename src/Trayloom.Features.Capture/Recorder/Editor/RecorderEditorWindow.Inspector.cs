// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Path = System.IO.Path;

namespace Trayloom.Features.Capture;

internal sealed partial class RecorderEditorWindow
{
    private int _tab;
    private Action? _flushText;

    private static readonly Brush CardFill = new SolidColorBrush(Color.FromArgb(9, 255, 255, 255));
    private static readonly Brush DimText = new SolidColorBrush(Color.FromArgb(158, 255, 255, 255));

    // ---- small builders -------------------------------------------------------------------------------------------------

    private static TextBlock Heading(string text)
    {
        var t = Ed.Label(text.ToUpperInvariant(), 11, FontWeights.SemiBold, new SolidColorBrush(Color.FromArgb(158, 255, 255, 255)));
        t.Margin = new Thickness(0, 0, 0, 8);
        System.Windows.Documents.Typography.SetCapitals(t, FontCapitals.Normal);
        return t;
    }

    private static TextBlock Caption(string text, double top = 4)
    {
        var t = Ed.Label(text, 10.5, FontWeights.Normal, Ed.FgDim);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(0, top, 0, 0);
        return t;
    }

    private static Border Rule() => new() { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x59, 0x40, 0x40, 0x40)), Margin = new Thickness(0, 14, 0, 14) };

    private static UIElement SwitchRow(string label, bool on, Action<bool> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Ed.Label(label, 12);
        l.TextWrapping = TextWrapping.Wrap;
        grid.Children.Add(l);
        var sw = new EdSwitch(on, changed) { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(sw, 1);
        grid.Children.Add(sw);
        return grid;
    }

    /// <summary>Title and value above a slider; a double click resets it. The change callback fires while dragging.</summary>
    private static UIElement ValueSlider(string title, double min, double max, double value, double reset, string format, Action<double> changed, double step = 0, double titleSize = 12)
    {
        var host = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ed.Label(title, titleSize));
        var readout = Ed.Label(string.Format(CultureInfo.CurrentCulture, format, value), 10.5, FontWeights.Medium, Ed.FgDim);
        readout.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Grid.SetColumn(readout, 1);
        head.Children.Add(readout);
        host.Children.Add(head);
        Slider? slider = null;
        slider = Ed.NewSlider(min, max, value, double.NaN, v =>
        {
            readout.Text = string.Format(CultureInfo.CurrentCulture, format, v);
            changed(v);
        }, step);
        slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        slider.Margin = new Thickness(0, 3, 0, 0);
        slider.MouseDoubleClick += (_, e) =>
        {
            slider.Value = reset;
            e.Handled = true;
        };
        host.Children.Add(slider);
        return host;
    }

    private static UIElement Segmented(string[] items, int selected, Action<int> changed)
    {
        var host = new Border { CornerRadius = new CornerRadius(7), Background = Ed.Faint, Padding = new Thickness(2) };
        var row = new UniformGrid { Rows = 1 };
        for (var i = 0; i < items.Length; i++)
        {
            var idx = i;
            var t = Ed.Label(items[i], 11.5, FontWeights.Medium, i == selected ? Brushes.White : Ed.FgDim);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            var cell = new Border { Child = t, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 3, 6, 3), Background = i == selected ? Ed.Accent : Brushes.Transparent, Cursor = Cursors.Hand };
            cell.MouseLeftButtonUp += (_, _) => changed(idx);
            row.Children.Add(cell);
        }

        host.Child = row;
        return host;
    }

    /// <summary>A label with a menu button showing the current value.</summary>
    private static UIElement MenuRow(string label, string[] items, int selected, Action<int> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Ed.Label(label, 12));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ed.Label(items[Math.Clamp(selected, 0, items.Length - 1)], 12, FontWeights.Medium));
        var chevron = Ed.Icon(G.ChevronDown, 8, Ed.FgDim);
        chevron.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(chevron);
        var button = new Border { Child = row, Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        button.MouseLeftButtonUp += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            for (var i = 0; i < items.Length; i++)
            {
                var idx = i;
                var mi = new MenuItem { Header = items[i], IsCheckable = true, IsChecked = i == selected };
                mi.Click += (_, _) => changed(idx);
                menu.Items.Add(mi);
            }

            menu.IsOpen = true;
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private UIElement BackLink()
    {
        var back = new EdButton(null, "‹ " + RS.BackToOptions, "Esc", () => Select(SelKind.None, null), height: 22);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(-10, 0, 0, 8);
        if (back.Label is { } l)
        {
            l.Foreground = Ed.Accent;
            l.FontWeight = FontWeights.Medium;
        }

        return back;
    }

    private static UIElement PositionGrid(string current, Action<string> changed)
    {
        var grid = new UniformGrid { Columns = 3, Rows = 3 };
        foreach (var a in Anchors.All)
        {
            var anchor = a;
            var cell = new Border { Height = 20, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, Background = anchor == current ? new SolidColorBrush(Color.FromArgb(217, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) };
            cell.MouseLeftButtonUp += (_, _) => changed(anchor);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private UIElement RemoveButton(Guid id)
    {
        var b = new EdButton(null, RS.Remove, null, () => RemoveSelected(id), height: 26);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.Margin = new Thickness(0, 14, 0, 0);
        b.Background = Ed.Hover;
        if (b.Label is { } l)
        {
            l.HorizontalAlignment = HorizontalAlignment.Center;
        }

        return b;
    }

    // ---- inspector ----------------------------------------------------------------------------------------------------------

    private void BuildInspector()
    {
        _flushText?.Invoke();
        _flushText = null;
        _inspector.Children.Clear();
        if (SelectedText is { } text)
        {
            OneText(text);
        }
        else if (SelectedImage is { } image)
        {
            OneImage(image);
        }
        else if (SelectedBlur is { } blur)
        {
            OneBlur(blur);
        }
        else if (SelectedZoom is { } zoom)
        {
            OneZoom(zoom);
        }
        else
        {
            Tabs();
        }
    }

    private void Tabs()
    {
        var tabs = Segmented([RS.Look, RS.Pointer, RS.Zoom], _tab, i =>
        {
            _tab = i;
            BuildInspector();
        });
        ((FrameworkElement)tabs).Margin = new Thickness(0, 0, 0, 16);
        _inspector.Children.Add(tabs);
        switch (_tab)
        {
            case 0: LookTab(); break;
            case 1: PointerTab(); break;
            default: ZoomTab(); break;
        }
    }

    // ---- Look ---------------------------------------------------------------------------------------------------------------

    private void LookTab()
    {
        _inspector.Children.Add(Heading(RS.Look));
        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (id, title) in new[] { ("raw", RS.LookRaw), ("clean", RS.LookClean), ("studio", RS.LookStudio) })
        {
            cards.Children.Add(LookCard(id, title));
        }

        _inspector.Children.Add(cards);
        _inspector.Children.Add(Rule());
        _inspector.Children.Add(Heading(RS.Background));
        var style = _doc.ResolvedBackdrop;
        var swatch = BackdropSwatch(style);
        _inspector.Children.Add(swatch);
        var aspects = new[] { "original", "wide", "square", "vertical" };
        _inspector.Children.Add(MenuRow(RS.Shape, [RS.ShapeOriginal, RS.ShapeWide, RS.ShapeSquare, RS.ShapeVertical], Math.Max(0, Array.IndexOf(aspects, _doc.ResolvedAspect)), i => Mutate(d => d.Aspect = aspects[i], inspector: true)));
        if (!style.IsNone)
        {
            var panel = new Border { CornerRadius = new CornerRadius(10), Background = CardFill, Padding = new Thickness(9), Margin = new Thickness(0, 6, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(ValueSlider(RS.Margin, 0, 1, style.Padding, 0.5, "{0:P0}", v => SetBackdrop(s => s.Padding = v), titleSize: 10.5));
            stack.Children.Add(ValueSlider(RS.Corners, 0, 1, style.CornerRadius, 0, "{0:P0}", v => SetBackdrop(s => s.CornerRadius = v), titleSize: 10.5));
            stack.Children.Add(ValueSlider(RS.Blur, 0, 1, style.Blur, 0, "{0:P0}", v => SetBackdrop(s => s.Blur = v), titleSize: 10.5));
            panel.Child = stack;
            _inspector.Children.Add(panel);
        }
    }

    private void SetBackdrop(Action<BackdropStyle> edit)
    {
        var style = _doc.ResolvedBackdrop;
        edit(style);
        Mutate(d => d.Backdrop = style.IsNone ? string.Empty : style.ToJson());
    }

    private UIElement LookCard(string id, string title)
    {
        var selected = _doc.MatchesLook(id);
        var preview = new Border { Width = 48, Height = 36, CornerRadius = new CornerRadius(7), BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), BorderThickness = new Thickness(1), ClipToBounds = true };
        preview.Background = id == "studio"
            ? new LinearGradientBrush(Color.FromArgb(230, 0x5E, 0x5C, 0xE6), Color.FromArgb(140, 0xBF, 0x5A, 0xF2), new Point(0, 0), new Point(1, 1))
            : new SolidColorBrush(Color.FromArgb(id == "raw" ? (byte)209 : (byte)158, 0, 0, 0));
        var grey = id == "raw" ? (byte)140 : (byte)199;
        var win = new Border { Width = 31, Height = 20, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(209, grey, grey, grey)), Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.35 } };
        var inner = new Grid();
        inner.Children.Add(win);
        var glyph = Ed.Icon(id == "raw" ? "" : id == "clean" ? "" : "", 7, Brushes.Black);
        glyph.HorizontalAlignment = HorizontalAlignment.Right;
        glyph.VerticalAlignment = VerticalAlignment.Bottom;
        glyph.Margin = new Thickness(0, 0, 7, 5);
        inner.Children.Add(glyph);
        preview.Child = inner;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(preview);
        var label = Ed.Label(title, 12.5, FontWeights.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(label);
        var card = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 8, 8, 6), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Child = stack,
            Background = selected ? new SolidColorBrush(Color.FromArgb(26, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)),
            BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(184, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), BorderThickness = new Thickness(selected ? 1.25 : 1),
        };
        var mark = Ed.Icon(selected ? G.Check : "", 9, selected ? Ed.Accent : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
        var host = new Grid();
        host.Children.Add(card);
        mark.HorizontalAlignment = HorizontalAlignment.Right;
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 6, 10, 0);
        mark.IsHitTestVisible = false;
        host.Children.Add(mark);
        card.MouseLeftButtonUp += (_, _) => Mutate(d =>
        {
            d.ApplyLook(id);
            d.RestoreAutomaticZooms(Take.Pointer.Clicks, Take.TypingTimes, Duration);
        }, inspector: true);
        return host;
    }

    private UIElement BackdropSwatch(BackdropStyle style)
    {
        var chip = new Border { Width = 26, Height = 20, CornerRadius = new CornerRadius(5), BorderBrush = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)), BorderThickness = new Thickness(1), Background = style.CreateBrush(26, 20) ?? Ed.Hover };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(chip);
        var name = Ed.Label(style.IsNone ? RS.None : RS.Background, 12, FontWeights.Medium);
        name.Margin = new Thickness(8, 0, 8, 0);
        row.Children.Add(name);
        row.Children.Add(Ed.Icon("", 8, Ed.FgDim));
        var button = new Border { Child = row, Padding = new Thickness(6, 4, 8, 4), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        button.MouseLeftButtonUp += (_, _) =>
        {
            var pop = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            var content = new BackdropPopover(_doc.ResolvedBackdrop, CaptureHost.App, s => Mutate(d => d.Backdrop = s.IsNone ? string.Empty : s.ToJson(), inspector: true), false);
            pop.Child = Ed.Capsule(content, 12, new Thickness(14));
            pop.IsOpen = true;
        };
        return button;
    }

    // ---- Pointer ------------------------------------------------------------------------------------------------------------

    private void PointerTab()
    {
        if (Take.Pointer.IsEmpty)
        {
            _inspector.Children.Add(Caption(RS.NoPointerNote, 0));
            return;
        }

        _inspector.Children.Add(Heading(RS.Pointer));
        _inspector.Children.Add(SwitchRow(RS.PointerShow, _doc.ShowsPointer, on => Mutate(d => d.ShowsPointer = on, inspector: true)));
        if (!_doc.ShowsPointer)
        {
            return;
        }

        var smoothing = new[] { PointerSmoothing.Off, PointerSmoothing.Light, PointerSmoothing.Smooth, PointerSmoothing.Cinematic };
        _inspector.Children.Add(MenuRow(RS.PointerSmoothing, [RS.SmoothingOff, RS.SmoothingLight, RS.SmoothingSmooth, RS.SmoothingCinematic], Array.IndexOf(smoothing, _doc.ResolvedSmoothing), i => Mutate(d => d.PointerSmoothing = RecorderMotion.SmoothingName(smoothing[i]), inspector: true)));
        _inspector.Children.Add(ValueSlider(RS.PointerSize, 0.5, 2.0, _doc.PointerSize, 1, "{0:0.00}×", v => Mutate(d => d.PointerSize = v)));
        _inspector.Children.Add(SwitchRow(RS.ClickRing, _doc.ShowsClickRing, on => Mutate(d => d.ShowsClickRing = on)));
    }

    // ---- Zoom ---------------------------------------------------------------------------------------------------------------

    private void ZoomTab()
    {
        _inspector.Children.Add(Heading(RS.Zoom));
        var hasPointer = !Take.Pointer.IsEmpty;
        if (hasPointer)
        {
            _inspector.Children.Add(SwitchRow(RS.ZoomToggle, _doc.ZoomEnabled, on => Mutate(d =>
            {
                d.ZoomEnabled = on;
                if (on)
                {
                    d.RestoreAutomaticZooms(Take.Pointer.Clicks, Take.TypingTimes, Duration);
                }
            }, inspector: true)));
        }

        if (!_doc.ZoomEnabled)
        {
            return;
        }

        var typing = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        typing.Children.Add(SwitchRow(RS.TypingZoomToggle, _doc.ZoomsOnTyping, on => Mutate(d =>
        {
            d.ZoomsOnTyping = on;
            d.ZoomSegments = ZoomTimeline.Generated(Take.Pointer.Clicks, on ? Take.TypingTimes : Array.Empty<double>(), Duration, ZoomTimeline.SanitizedAmount(d.ZoomAmount));
            d.ZoomEnabled = true;
            d.ZoomsGenerated = true;
        }, inspector: true)));
        typing.Children.Add(Caption(RS.TypingZoomCaption, 0));
        _inspector.Children.Add(typing);
        if (_doc.ZoomSegments.Count == 0)
        {
            ZoomEmptyState();
            return;
        }

        var panel = new Border { CornerRadius = new CornerRadius(11), Background = CardFill, Padding = new Thickness(11) };
        var stack = new StackPanel();
        stack.Children.Add(ValueSlider(RS.ZoomAmount, 1.2, 3.0, ZoomTimeline.SanitizedAmount(_doc.ZoomAmount), 1.8, "{0:0.0}×", v => Mutate(d => d.ZoomAmount = v)));
        if (hasPointer)
        {
            var link = new EdButton(null, RS.RegenerateZooms, null, () => Mutate(d =>
            {
                d.ZoomSegments = ZoomTimeline.Generated(Take.Pointer.Clicks, d.ZoomsOnTyping ? Take.TypingTimes : Array.Empty<double>(), Duration, ZoomTimeline.SanitizedAmount(d.ZoomAmount));
                d.ZoomEnabled = true;
                d.ZoomsGenerated = true;
            }, inspector: true), height: 22) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-10, 4, 0, 0) };
            if (link.Label is { } l)
            {
                l.Foreground = Ed.Accent;
                l.FontSize = 10.5;
                l.FontWeight = FontWeights.Medium;
            }

            stack.Children.Add(link);
        }

        panel.Child = stack;
        _inspector.Children.Add(panel);
    }

    private void ZoomEmptyState()
    {
        var canCreate = ZoomTimeline.Generated(Take.Pointer.Clicks, _doc.ZoomsOnTyping ? Take.TypingTimes : Array.Empty<double>(), Duration, _doc.ZoomAmount).Count > 0;
        var card = new Border { CornerRadius = new CornerRadius(12), Background = CardFill, BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(1), Padding = new Thickness(14) };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = new SolidColorBrush(Color.FromArgb(31, 0x0A, 0x84, 0xFF)), Child = Ed.Icon("", 16, Ed.Accent), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(circle);
        var title = Ed.Label(RS.ZoomEmptyTitle, 12, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        var cap = Caption(RS.ZoomEmptyCaption, 4);
        cap.TextAlignment = TextAlignment.Center;
        stack.Children.Add(cap);
        var action = new EdButton(null, canCreate ? RS.CreateAutomaticZooms : RS.AddZoom, null, () =>
        {
            if (canCreate)
            {
                Mutate(d =>
                {
                    d.ZoomSegments = ZoomTimeline.Generated(Take.Pointer.Clicks, d.ZoomsOnTyping ? Take.TypingTimes : Array.Empty<double>(), Duration, ZoomTimeline.SanitizedAmount(d.ZoomAmount));
                    d.ZoomEnabled = true;
                    d.ZoomsGenerated = true;
                }, inspector: true);
            }
            else
            {
                AddZoomAt(_src);
            }
        }, prominent: true, height: 24) { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(action);
        card.Child = stack;
        _inspector.Children.Add(card);
    }

    // ---- one item -------------------------------------------------------------------------------------------------------------

    private void OneZoom(ZoomSegment z)
    {
        _inspector.Children.Add(BackLink());
        _inspector.Children.Add(Heading(RS.ThisZoom));
        _inspector.Children.Add(ValueSlider(RS.ZoomAmount, 1.2, 3.0, ZoomTimeline.SanitizedAmount(z.Amount), 1.8, "{0:0.0}×", v =>
        {
            PushUndo();
            z.Amount = ZoomTimeline.SanitizedAmount(v);
            _lastZoomAmount = z.Amount;
            Commit(inspector: false, timeline: false);
        }));
        var where = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        where.Children.Add(Ed.Label(RS.ZoomWhere, 12));
        var seg = Segmented([RS.ZoomFollows, RS.ZoomPickSpot], z.FollowsPointer ? 0 : 1, i =>
        {
            if (i == 1)
            {
                BeginAim();
            }
            else
            {
                PushUndo();
                z.FocusX = z.FocusY = null;
                EndAim();
                Commit(inspector: true, timeline: false);
            }
        });
        ((FrameworkElement)seg).Margin = new Thickness(0, 6, 0, 0);
        where.Children.Add(seg);
        if (!z.FollowsPointer)
        {
            var link = new EdButton(null, RS.ZoomPickSpotHint, null, BeginAim, height: 22) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-10, 4, 0, 0) };
            if (link.Label is { } l)
            {
                l.Foreground = Ed.Accent;
                l.FontSize = 10.5;
            }

            where.Children.Add(link);
        }

        _inspector.Children.Add(where);
        _inspector.Children.Add(RemoveButton(z.Id));
    }

    private void OneText(TextOverlay t)
    {
        _inspector.Children.Add(BackLink());
        _inspector.Children.Add(Heading(RS.ThisText));
        var box = new TextBox
        {
            Text = t.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 26, MaxHeight = 76, FontSize = 12, Padding = new Thickness(6, 4, 6, 4), MaxLength = 200,
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), Foreground = Ed.Fg, BorderBrush = Ed.PlateStroke, CaretBrush = Ed.Fg, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var hint = Ed.Label(RS.TextPlaceholder, 12, FontWeights.Normal, Ed.FgFaint);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(9, 5, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Top;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var committed = t.Text;
        void Flush()
        {
            if (box.Text != committed)
            {
                committed = box.Text;
                Mutate(d => t.Text = box.Text);
                RefreshLanes();
            }
        }

        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.LostKeyboardFocus += (_, _) => Flush();
        _flushText = Flush;
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(hint);
        _inspector.Children.Add(field);
        _inspector.Children.Add(ValueSlider(RS.Size, 3, 16, t.Size * 100, 6, "{0:0}%", v => Mutate(d => t.Size = Math.Clamp(v / 100, 0.03, 0.16)), step: 1));
        _inspector.Children.Add(Ed.Label(RS.Position, 12));
        _inspector.Children.Add(PositionGrid(t.Anchor, a => Mutate(d => t.Anchor = a, inspector: true)));
        var colour = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        colour.Children.Add(Ed.Label(RS.Colour, 12));
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var p in TextPalette.All)
        {
            var (r, g, b) = TextPalette.Components(p);
            var palette = p;
            var on = t.Palette == p;
            var dot = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 7, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255))),
                BorderBrush = on ? Ed.Accent : new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), BorderThickness = new Thickness(on ? 2.5 : 1),
            };
            dot.MouseLeftButtonUp += (_, _) => Mutate(d => t.Palette = palette, inspector: true);
            dots.Children.Add(dot);
        }

        colour.Children.Add(dots);
        _inspector.Children.Add(colour);
        _inspector.Children.Add(RemoveButton(t.Id));
    }

    private void OneImage(ImageOverlay img)
    {
        _inspector.Children.Add(BackLink());
        _inspector.Children.Add(Heading(RS.ThisImage));
        var name = Ed.Label(Path.GetFileName(img.Path), 11, FontWeights.Normal, Ed.FgDim);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 0, 0, 6);
        _inspector.Children.Add(name);
        _inspector.Children.Add(ValueSlider(RS.ImageSize, 4, 60, img.Size * 100, 18, "{0:0}%", v => Mutate(d => img.Size = Math.Clamp(v / 100, 0.04, 0.6)), step: 1));
        _inspector.Children.Add(ValueSlider(RS.ImageOpacity, 5, 100, img.Opacity * 100, 100, "{0:0}%", v => Mutate(d => img.Opacity = Math.Clamp(v / 100, 0.05, 1)), step: 1));
        _inspector.Children.Add(Ed.Label(RS.Position, 12));
        _inspector.Children.Add(PositionGrid(img.Anchor, a => Mutate(d => img.Anchor = a, inspector: true)));
        _inspector.Children.Add(RemoveButton(img.Id));
    }

    private void OneBlur(BlurRegion blur)
    {
        _inspector.Children.Add(BackLink());
        _inspector.Children.Add(Heading(RS.ThisBlur));
        var choose = new EdButton(null, RS.BlurPickArea, null, () => StartPick(blur), prominent: true, height: 28) { IsEnabled = !_pickingBlur };
        EdIcons.Prepend(choose, EdIcons.Mosaic(12, Brushes.White));
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        _inspector.Children.Add(choose);
        _inspector.Children.Add(Caption(_pickingBlur ? RS.BlurPickAreaHint : RS.BlurCaption, 6));
        var strength = ValueSlider(RS.BlurStrength, 1, 5, blur.Strength, 3, "{0:0}", v => Mutate(d => blur.Strength = (int)Math.Round(v)), step: 1);
        ((FrameworkElement)strength).Margin = new Thickness(0, 12, 0, 0);
        _inspector.Children.Add(strength);
        _inspector.Children.Add(RemoveButton(blur.Id));
    }

    // ---- presets and export speed --------------------------------------------------------------------------------------

    private UIElement BuildPresetsButton()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ed.Label(RS.PresetsButton, 12, FontWeights.Medium));
        var icon = Ed.Icon(G.Sliders, 12, Ed.Fg);
        icon.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(icon);
        var button = new Border { Child = row, Height = 30, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(10, 0, 0, 0), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        button.MouseLeftButtonUp += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            void Look(string header, string id)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => Mutate(d =>
                {
                    d.ApplyLook(id);
                    d.RestoreAutomaticZooms(Take.Pointer.Clicks, Take.TypingTimes, Duration);
                }, inspector: true);
                menu.Items.Add(mi);
            }

            Look(RS.LookRaw, "raw");
            Look(RS.LookClean, "clean");
            Look(RS.LookStudio, "studio");
            var presets = PresetStore.Load(CaptureHost.App);
            if (presets.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var p in presets)
                {
                    var preset = p;
                    var mi = new MenuItem { Header = preset.Name };
                    mi.Click += (_, _) => Mutate(d =>
                    {
                        preset.ApplyTo(d, Take);
                        d.RestoreAutomaticZooms(Take.Pointer.Clicks, Take.TypingTimes, Duration);
                        d.Sanitize(Duration);
                    }, inspector: true);
                    menu.Items.Add(mi);
                }

                var remove = new MenuItem { Header = RS.RemovePreset };
                foreach (var p in presets)
                {
                    var preset = p;
                    var mi = new MenuItem { Header = preset.Name };
                    mi.Click += (_, _) => PresetStore.Remove(CaptureHost.App, preset.Id);
                    remove.Items.Add(mi);
                }

                menu.Items.Add(remove);
            }

            menu.Items.Add(new Separator());
            var save = new MenuItem { Header = RS.SavePreset };
            save.Click += (_, _) =>
            {
                if (TextPrompt.Ask(this, RS.SavePreset, RS.PresetNamePlaceholder) is { } name)
                {
                    PresetStore.Save(CaptureHost.App, name, _doc);
                }
            };
            menu.Items.Add(save);
            menu.IsOpen = true;
        };
        return button;
    }

    private UIElement BuildSpeedPopover(Popup pop)
    {
        var draft = Math.Clamp(_doc.ExportSpeed, 0.25, 4);
        var host = new StackPanel { Width = 320 };
        var inner = new StackPanel { Margin = new Thickness(18) };
        host.Children.Add(inner);
        inner.Children.Add(Ed.Label(RS.ExportSpeed, 14, FontWeights.SemiBold));
        var valueLabel = Ed.Label(string.Empty, 12, FontWeights.Medium);
        var durationLabel = Ed.Label(string.Empty, 12, FontWeights.Medium);
        Slider? slider = null;
        var buttons = new List<(Border Cell, TextBlock Text, double Value)>();
        void Refresh(bool fromSlider)
        {
            valueLabel.Text = Fmt.Speed(draft) + "×";
            durationLabel.Text = RecorderSupport.ElapsedLabel(Math.Round(_doc.OutputDuration(Duration) / draft));
            if (!fromSlider && slider is not null && Math.Abs(slider.Value - draft) > 0.0001)
            {
                slider.Value = draft;
            }

            foreach (var (cell, text, v) in buttons)
            {
                var on = Math.Abs(v - draft) < 0.0001;
                text.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
                cell.Background = on ? new SolidColorBrush(Color.FromArgb(46, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            }
        }

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 12) };
        foreach (var v in new[] { 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4 })
        {
            var value = v;
            var text = Ed.Label(Fmt.Speed(v) + "×", 12);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border { Child = text, Height = 28, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand };
            text.VerticalAlignment = VerticalAlignment.Center;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                draft = value;
                Refresh(false);
            };
            buttons.Add((cell, text, v));
            grid.Children.Add(cell);
        }

        inner.Children.Add(grid);
        var stepper = new Grid();
        stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stepper.Children.Add(Ed.Label(RS.CustomSpeed, 12));
        var step = new StackPanel { Orientation = Orientation.Horizontal };
        step.Children.Add(valueLabel);
        void Nudge(double delta)
        {
            draft = Math.Clamp(Math.Round(draft + delta, 2), 0.25, 4);
            Refresh(false);
        }

        var minus = new EdButton("", null, null, () => Nudge(-0.01), iconSize: 10, width: 22, height: 22) { Margin = new Thickness(8, 0, 0, 0) };
        var plus = new EdButton("", null, null, () => Nudge(0.01), iconSize: 10, width: 22, height: 22);
        step.Children.Add(minus);
        step.Children.Add(plus);
        Grid.SetColumn(step, 1);
        stepper.Children.Add(step);
        inner.Children.Add(stepper);
        slider = Ed.NewSlider(0.25, 4, draft, double.NaN, v =>
        {
            draft = Math.Round(v, 2);
            Refresh(true);
        }, 0.01);
        slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        slider.Margin = new Thickness(0, 8, 0, 0);
        inner.Children.Add(slider);
        var ends = new Grid();
        ends.Children.Add(Ed.Label("0.25×", 10.5, FontWeights.Normal, Ed.FgDim));
        var right = Ed.Label("4×", 10.5, FontWeights.Normal, Ed.FgDim);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        ends.Children.Add(right);
        inner.Children.Add(ends);
        var dur = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        dur.Children.Add(Ed.Label(RS.ExportDuration, 12));
        durationLabel.HorizontalAlignment = HorizontalAlignment.Right;
        dur.Children.Add(durationLabel);
        inner.Children.Add(dur);
        inner.Children.Add(Caption(RS.SpeedNote, 10));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(new EdButton(null, RS.Cancel, null, () => pop.IsOpen = false, height: 26));
        row.Children.Add(new Border { Width = 8 });
        row.Children.Add(new EdButton(null, RS.Done, null, () =>
        {
            var value = Math.Round(draft * 100) / 100;
            pop.IsOpen = false;
            Mutate(d => d.ExportSpeed = value, then: BuildTransport);
        }, prominent: true, height: 26));
        inner.Children.Add(row);
        Refresh(false);
        return host;
    }
}
