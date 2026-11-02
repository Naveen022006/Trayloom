// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>The layout editor (<c>NotchLayoutEditor</c>): a scaled island with draggable quick-access buttons (spec 02d section 7).</summary>
internal sealed class LayoutEditor : Grid
{
    private readonly ISettingsContext _ctx;
    private readonly IslandCore? _core;
    private readonly Canvas _canvas = new() { Height = 400, ClipToBounds = false };
    private QuickAccessLayout _layout;
    private Rect _island;
    private double _scale = 0.5;
    private FrameworkElement? _dragging;
    private QuickButton? _dragButton;
    private Point _dragStart;
    private Point _dragOrigin;
    private bool _dragMoved;
    private Popup? _popup;

    public LayoutEditor(ISettingsContext ctx)
    {
        _ctx = ctx;
        _core = IslandCore.Current;
        _layout = QuickAccessLayout.Load(ctx.App);
        var stack = new StackPanel();
        var bg = new Border { CornerRadius = new CornerRadius(22), Child = _canvas, ClipToBounds = false };
        bg.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        stack.Children.Add(bg);
        var hint = UiKit.Text(Loc.T("Click + to add a button. Drag buttons around the island. Click a button to edit it."), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, true);
        hint.Margin = new Thickness(2, 10, 0, 0);
        stack.Children.Add(hint);
        Children.Add(stack);
        SizeChanged += (_, _) => Rebuild();
        ctx.App.Changed += OnSetting;
        Unloaded += (_, _) => ctx.App.Changed -= OnSetting;
    }

    private void OnSetting(string key)
    {
        if (key is K.Size or K.CustomWidth or K.CustomHeight or K.Silhouette or K.Outline or K.HiddenControls or K.ControlOrder or K.HiddenModules or K.QuickAccessLayout)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _layout = QuickAccessLayout.Load(_ctx.App);
                Rebuild();
            });
        }
    }

    private void Save() => _layout.Save(_ctx.App);

    private void Rebuild()
    {
        _canvas.Children.Clear();
        var cw = Math.Max(300, ActualWidth);
        var app = _ctx.App;
        var preset = app.Get<string>(K.Size);
        double w = preset switch { "compact" => 480, "custom" => Math.Clamp(app.Get<double>(K.CustomWidth), 360, 600), _ => 560 };
        var capsule = app.Get<string>(K.Silhouette) != "notch";
        // page + height
        IslandPage? page = _core is null ? null : new ControlsPage(_core, null) { IsPreview = true };
        var contentW = w - 56;
        var budget = preset == "compact" ? 180 : (preset == "custom" ? Math.Clamp(app.Get<double>(K.CustomHeight), 260, 640) - 76 : 264);
        var ch = page?.Height(contentW, budget) ?? 140;
        var h = preset == "custom" ? Math.Clamp(app.Get<double>(K.CustomHeight), 260, 640) : (capsule ? 12 : 0) + 64 + ch;
        _scale = Math.Min(0.5, Math.Max(0.2, (cw - 112) / w));
        var iw = w * _scale;
        var ih = h * _scale;
        _island = new Rect((cw - iw) / 2, 20, iw, ih);
        // the island itself (real controls page scaled)
        var surface = new Border
        {
            Width = w,
            Height = h,
            Background = Brushes.Black,
            CornerRadius = capsule ? new CornerRadius(28) : new CornerRadius(0, 0, 28, 28),
            BorderBrush = app.Get<bool>(K.Outline) ? new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)) : Brushes.Transparent,
            BorderThickness = new Thickness(app.Get<bool>(K.Outline) ? 2 : 0),
            ClipToBounds = true,
            RenderTransform = new ScaleTransform(_scale, _scale),
            RenderTransformOrigin = new Point(0, 0),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Content"),
        };
        var content = new Grid();
        if (page is not null)
        {
            var topInset = capsule ? 12.0 : 0.0;
            if (page.BandFill is { } fill)
            {
                fill.Margin = new Thickness(28, topInset + 6, 28, 0);
                fill.VerticalAlignment = VerticalAlignment.Top;
                fill.IsHitTestVisible = false;
                content.Children.Add(fill);
            }

            var host = new Grid { Margin = new Thickness(28, topInset + 48, 28, 16), IsHitTestVisible = false, ClipToBounds = true };
            host.Children.Add(page.View);
            content.Children.Add(host);
        }
        else
        {
            content.Children.Add(D.EmptyView(G.Controls, Strings.Empty));
        }

        surface.Child = content;
        Canvas.SetLeft(surface, _island.X);
        Canvas.SetTop(surface, _island.Y);
        surface.MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragMoved && e.OriginalSource is not null)
            {
                _ctx.Shell.OpenSettings(SettingsPages.Join(SettingsPages.Island, "content-controls"));
            }
        };
        _canvas.Children.Add(surface);

        // bubbles
        foreach (var side in new[] { QuickSide.Left, QuickSide.Right, QuickSide.Bottom })
        {
            var list = _layout.On(side).ToList();
            var total = list.Count + (_layout.HasRoom(side) ? 1 : 0);
            for (var i = 0; i < list.Count; i++)
            {
                AddBubble(list[i], Slot(side, i, total));
            }

            if (_layout.HasRoom(side))
            {
                AddPlus(side, Slot(side, list.Count, total));
            }
        }

        AddGrip();
    }

    private Point Slot(QuickSide side, int index, int count) => side switch
    {
        QuickSide.Left => new Point(_island.Left - 18, _island.Top + 30 + (index * 40)),
        QuickSide.Right => new Point(_island.Right + 18, _island.Top + 30 + (index * 40)),
        _ => new Point(_island.Left + (_island.Width / 2) + ((index - ((count - 1) / 2.0)) * 40), _island.Bottom + 24),
    };

    private void AddBubble(QuickButton b, Point center)
    {
        var (glyph, title, available) = Describe(b);
        var el = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
            BorderThickness = new Thickness(1.5),
            Opacity = available ? 1 : 0.4,
            Cursor = Cursors.Hand,
            Child = D.I(glyph, 12, Brushes.White),
            ToolTip = available ? title : title + ", Hidden from the island",
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 2, Opacity = 0.12 },
        };
        Canvas.SetLeft(el, center.X - 15);
        Canvas.SetTop(el, center.Y - 15);
        el.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = el;
            _dragButton = b;
            _dragStart = e.GetPosition(_canvas);
            _dragOrigin = new Point(Canvas.GetLeft(el), Canvas.GetTop(el));
            _dragMoved = false;
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (_dragging != el)
            {
                return;
            }

            var p = e.GetPosition(_canvas);
            if (!_dragMoved && (p - _dragStart).Length < 5)
            {
                return;
            }

            _dragMoved = true;
            _popup?.SetCurrentValue(Popup.IsOpenProperty, false);
            Canvas.SetLeft(el, _dragOrigin.X + p.X - _dragStart.X);
            Canvas.SetTop(el, _dragOrigin.Y + p.Y - _dragStart.Y);
            Panel.SetZIndex(el, 10);
        };
        el.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragging != el)
            {
                return;
            }

            el.ReleaseMouseCapture();
            _dragging = null;
            var p = e.GetPosition(_canvas);
            if (!_dragMoved)
            {
                ShowEdit(el, b);
            }
            else
            {
                DropButton(b, p);
            }

            e.Handled = true;
        };
        _canvas.Children.Add(el);
    }

    private void DropButton(QuickButton b, Point p)
    {
        var zones = new (QuickSide Side, Rect Rect)[]
        {
            (QuickSide.Left, new Rect(_island.Left - 48, _island.Top, 62, _island.Height)),
            (QuickSide.Right, new Rect(_island.Right - 14, _island.Top, 62, _island.Height)),
            (QuickSide.Bottom, new Rect(_island.Left, _island.Bottom, _island.Width, 58)),
        };
        foreach (var (side, rect) in zones)
        {
            if (!rect.Contains(p))
            {
                continue;
            }

            var siblings = _layout.On(side).Where(x => x.Id != b.Id).ToList();
            QuickButton? target = null;
            for (var i = 0; i < siblings.Count; i++)
            {
                var slot = Slot(side, i, siblings.Count);
                if (side == QuickSide.Bottom ? slot.X > p.X : slot.Y > p.Y)
                {
                    target = siblings[i];
                    break;
                }
            }

            if (_layout.Move(b.Id, side, target?.Id))
            {
                Save();
            }

            break;
        }

        Rebuild();
    }

    private void AddPlus(QuickSide side, Point center)
    {
        var el = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Add button"),
            BorderThickness = new Thickness(1),
            Child = UiKit.Icon("", 15, ThemeKeys.Accent, FontWeights.SemiBold),
        };
        el.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        el.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        if (el.Child is TextBlock t)
        {
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
        }

        Canvas.SetLeft(el, center.X - 15);
        Canvas.SetTop(el, center.Y - 15);
        el.MouseLeftButtonUp += (_, _) => ShowAdd(el, side);
        _canvas.Children.Add(el);
    }

    private void AddGrip()
    {
        var el = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Cursor = Cursors.SizeNWSE, ToolTip = Loc.T("Size"), BorderThickness = new Thickness(1), Child = UiKit.Icon("", 10, ThemeKeys.Accent, FontWeights.SemiBold) };
        el.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        el.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        if (el.Child is TextBlock t)
        {
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
        }

        Canvas.SetLeft(el, _island.Right - 3 - 12);
        Canvas.SetTop(el, Math.Max(_island.Bottom - 3, _island.Top + 139) - 12);
        double startW = 0, startH = 0;
        Point start = default;
        var active = false;
        el.MouseLeftButtonDown += (_, e) =>
        {
            active = true;
            start = e.GetPosition(_canvas);
            startW = Math.Clamp(_ctx.App.Get<double>(K.CustomWidth), 360, 600);
            startH = Math.Clamp(_ctx.App.Get<double>(K.CustomHeight), 260, 640);
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (!active)
            {
                return;
            }

            var p = e.GetPosition(_canvas);
            if ((p - start).Length < 3)
            {
                return;
            }

            _ctx.App.Set(K.Size, "custom");
            _ctx.App.Set(K.CustomWidth, Math.Clamp(Math.Round((startW + ((p.X - start.X) / _scale)) / 10) * 10, 360, 600));
            _ctx.App.Set(K.CustomHeight, Math.Clamp(Math.Round((startH + ((p.Y - start.Y) / _scale)) / 10) * 10, 260, 640));
        };
        el.MouseLeftButtonUp += (_, _) =>
        {
            active = false;
            el.ReleaseMouseCapture();
        };
        _canvas.Children.Add(el);
    }

    // ---------------------------------------------------------------- action model

    private (string Glyph, string Title, bool Available) Describe(QuickButton b)
    {
        var id = b.ActionId;
        switch (id)
        {
            case "explore":
                return (G.Explore, Loc.T("Explore"), true);
            case "settings":
                return (G.Gear, Strings.SettingsEllipsis, true);
            case "pin":
                return (G.Pin, Strings.Pin, true);
        }

        var title = string.IsNullOrEmpty(b.Label) ? null : b.Label;
        if (id.StartsWith("control.", StringComparison.Ordinal))
        {
            var c = IslandCatalog.Control(id[8..]);
            return (c?.Glyph ?? G.Dot, title ?? c?.Title ?? id, c is not null && ControlAvailable(c));
        }

        var m = IslandCatalog.Get(id);
        return (m?.Glyph ?? G.Dot, title ?? m?.Title ?? id, _core?.ModuleVisible(id) ?? false);
    }

    private bool ControlAvailable(ControlDef c)
    {
        var rt = _ctx.Runtime;
        if (c.Features.Length > 0 && !rt.AnyAvailable(c.Features))
        {
            return false;
        }

        return c.Id switch
        {
            "music" or "mixer" or "timer" or "calendar" => _core?.ModuleVisible(c.Id) ?? false,
            "speedTest" => (_core?.ModuleVisible("system") ?? false) && rt.IsAvailable("monitor-network"),
            _ => true,
        };
    }

    private IEnumerable<(string Id, string Glyph, string Title, bool Available, bool Section)> Actions()
    {
        yield return ("explore", G.Explore, Loc.T("Explore"), true, true);
        yield return ("settings", G.Gear, Strings.SettingsEllipsis, true, true);
        foreach (var m in IslandCatalog.Modules)
        {
            yield return (m.Id, m.Glyph, m.Title, _core?.ModuleVisible(m.Id) ?? false, true);
        }

        yield return ("pin", G.Pin, Strings.Pin, true, false);
        foreach (var c in IslandCatalog.Controls.Where(c => !c.IsLevel))
        {
            yield return ("control." + c.Id, c.Glyph, c.Title, ControlAvailable(c), false);
        }
    }

    private FrameworkElement Chooser(string? selectedAction, Action<string> picked)
    {
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 8), Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
        var host = new StackPanel();
        host.Children.Add(box);
        var listHost = new ScrollViewer { Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        host.Children.Add(listHost);
        void Fill()
        {
            var q = WatchService.Normalise(box.Text);
            var stack = new StackPanel();
            foreach (var (title, isSection) in new[] { (Loc.T("Open a section"), true), (Loc.T("Quick actions"), false) })
            {
                var items = Actions().Where(a => a.Section == isSection && a.Available && (q.Length == 0 || WatchService.Normalise(a.Title + " " + a.Id).Contains(q))).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                var head = UiKit.Text(title, 11, FontWeights.Medium, ThemeKeys.TextSecondary);
                head.Margin = new Thickness(0, 4, 0, 4);
                stack.Children.Add(head);
                var grid = new UniformGrid { Columns = 2 };
                foreach (var a in items)
                {
                    var id = a.Id;
                    var cell = new Border { MinHeight = 38, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 0, 8, 0), Cursor = Cursors.Hand };
                    cell.SetResourceReference(Border.BackgroundProperty, id == selectedAction ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
                    var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(UiKit.Icon(a.Glyph, 12, ThemeKeys.TextPrimary));
                    var tt = UiKit.Text(a.Title, 11, FontWeights.Normal, ThemeKeys.TextPrimary, true);
                    tt.Margin = new Thickness(6, 0, 0, 0);
                    tt.MaxWidth = 100;
                    row.Children.Add(tt);
                    cell.Child = row;
                    cell.MouseLeftButtonUp += (_, _) => picked(id);
                    grid.Children.Add(cell);
                }

                stack.Children.Add(grid);
            }

            if (stack.Children.Count == 0)
            {
                var none = UiKit.Text(Loc.T("No results"), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
                none.HorizontalAlignment = HorizontalAlignment.Center;
                none.Margin = new Thickness(0, 50, 0, 0);
                stack.Children.Add(none);
            }

            listHost.Content = stack;
        }

        box.TextChanged += (_, _) => Fill();
        Fill();
        return host;
    }

    private void ShowPopup(FrameworkElement target, FrameworkElement content, PlacementMode mode)
    {
        _popup?.SetCurrentValue(Popup.IsOpenProperty, false);
        var card = new Border { Width = 340, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = content, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.3 }, Margin = new Thickness(12) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        _popup = new Popup { PlacementTarget = target, Placement = mode, StaysOpen = false, AllowsTransparency = true, Child = card, IsOpen = true };
    }

    private void ShowAdd(FrameworkElement target, QuickSide side)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(Loc.T("Add button"), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        stack.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 10));
        stack.Children.Add(Chooser(null, id =>
        {
            if (_layout.HasRoom(side))
            {
                _layout.Buttons.Add(new QuickButton { ActionId = id, SideValue = side });
                Save();
            }

            _popup!.IsOpen = false;
            Rebuild();
        }));
        ShowPopup(target, stack, side == QuickSide.Left ? PlacementMode.Left : (side == QuickSide.Right ? PlacementMode.Right : PlacementMode.Bottom));
    }

    private void ShowEdit(FrameworkElement target, QuickButton b)
    {
        var stack = new StackPanel();
        var heading = UiKit.Text(Loc.T("Edit button"), 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        heading.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(heading);
        var name = new TextBox { Text = b.Label, Height = 26, MaxLength = 40, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 0, 10) };
        name.TextChanged += (_, _) =>
        {
            b.Label = name.Text;
            Save();
        };
        stack.Children.Add(UiKit.Text(Loc.T("Name"), 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        stack.Children.Add(name);
        var pos = new SegmentedControl([Loc.T("Left"), Loc.T("Right"), Loc.T("Bottom")], (int)b.SideValue, true, 12);
        pos.SelectionChanged += i =>
        {
            var side = (QuickSide)i;
            if (side != b.SideValue && _layout.Move(b.Id, side))
            {
                Save();
                _popup!.IsOpen = false;
                Rebuild();
            }
        };
        stack.Children.Add(pos);
        var moves = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
        moves.Children.Add(UiKit.Button(Loc.T("Move up"), ButtonKind.Bordered, 11.5, null, () => { if (_layout.Reorder(b.Id, -1)) { Save(); Rebuild(); } }));
        var down = UiKit.Button(Loc.T("Move down"), ButtonKind.Bordered, 11.5, null, () => { if (_layout.Reorder(b.Id, 1)) { Save(); Rebuild(); } });
        down.Margin = new Thickness(8, 0, 0, 0);
        moves.Children.Add(down);
        stack.Children.Add(moves);
        stack.Children.Add(Chooser(b.ActionId, id =>
        {
            b.ActionId = id;
            Save();
            _popup!.IsOpen = false;
            Rebuild();
        }));
        var remove = UiKit.Button(Loc.T("Remove button"), ButtonKind.Destructive, 11.5, null, () =>
        {
            _layout.Buttons.Remove(b);
            Save();
            _popup!.IsOpen = false;
            Rebuild();
        });
        remove.Margin = new Thickness(0, 10, 0, 0);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(remove);
        ShowPopup(target, stack, b.SideValue == QuickSide.Left ? PlacementMode.Left : (b.SideValue == QuickSide.Right ? PlacementMode.Right : PlacementMode.Top));
    }
}
