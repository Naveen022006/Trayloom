// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WinKnife.Features.Capture;

internal sealed partial class ScreenshotEditorWindow
{
    private ToolId ActiveKind => _selected?.Kind ?? _tool;

    private void RebuildBar()
    {
        _bar.Children.Clear();
        if (_tool == ToolId.Crop && _cropDraft is not null)
        {
            _bar.Children.Add(new EdButton(null, "Cancel", "Esc", () => SelectTool(ToolId.Select), height: 24));
            var crop = new EdButton(null, "Crop", "Enter", ApplyCrop, prominent: true, height: 24) { Margin = new Thickness(8, 0, 0, 0) };
            _bar.Children.Add(crop);
            return;
        }

        if (_tool == ToolId.ExpandCanvas)
        {
            BuildExpandBar();
            return;
        }

        var kind = ActiveKind;
        var first = true;
        void Group(params UIElement[] items)
        {
            if (!first)
            {
                _bar.Children.Add(Ed.Divider());
            }

            first = false;
            foreach (var i in items)
            {
                _bar.Children.Add(i);
            }
        }

        if (kind == ToolId.Arrow)
        {
            Group(ArrowStyleMenu());
        }

        if (kind == ToolId.Sticker)
        {
            Group(StickerMenu());
        }

        if (kind == ToolId.Pixelate)
        {
            var items = new List<UIElement> { BlurStyleMenu(), BlurTextOnlyToggle() };
            if ((_selected?.BlurStyle ?? _blurStyle) != "erase")
            {
                items.Add(BlurStrength());
            }

            Group([.. items]);
        }

        if (kind == ToolId.Spotlight)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = "Dim outside" };
            row.Children.Add(Ed.Icon("", 11, Ed.FgDim));
            var slider = Ed.NewSlider(0, 1, _doc.SpotlightDim, 84, v =>
            {
                _doc.SpotlightDim = Math.Round(v * 20) / 20;
                _app.Set(K.SpotlightDim, _doc.SpotlightDim);
                MarkChanged();
            });
            slider.Margin = new Thickness(6, 0, 6, 0);
            row.Children.Add(slider);
            row.Children.Add(Ed.Icon("", 11, Ed.FgDim));
            Group(row);
        }

        var colorKinds = new[] { ToolId.Arrow, ToolId.Line, ToolId.Rect, ToolId.Ellipse, ToolId.Freehand, ToolId.Highlight, ToolId.Text, ToolId.Counter, ToolId.Redact };
        if (colorKinds.Contains(kind))
        {
            Group(ColorDots());
        }

        if (kind is ToolId.Arrow or ToolId.Line or ToolId.Rect or ToolId.Ellipse or ToolId.Freehand)
        {
            Group(ThicknessPicker());
        }

        if (kind == ToolId.Text)
        {
            Group(FontSizeControls());
        }

        if (_doc.Marks.Count >= 2)
        {
            var idx = _selected is null ? -1 : _doc.Marks.IndexOf(_selected);
            var back = new EdButton("", null, "Send backward", () => MoveLayer(-1), iconSize: 12, width: 24) { IsEnabled = idx > 0 };
            var fwd = new EdButton("", null, "Bring forward", () => MoveLayer(1), iconSize: 12, width: 24) { IsEnabled = idx >= 0 && idx < _doc.Marks.Count - 1 };
            Group(back, fwd);
        }

        var shadows = new EdButton("", null, Strings.Screenshot.ShadowsLabel, null, iconSize: 12, width: 24) { Active = _doc.Shadows };
        shadows.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _doc.Shadows = !_doc.Shadows;
            _app.Set(K.Shadows, _doc.Shadows);
            shadows.Active = _doc.Shadows;
            MarkChanged();
        };
        Group(shadows);

        var bd = new EdButton(_doc.Backdrop.IsNone ? "" : G.Image, Strings.Screenshot.BackgroundLabel, null, null, iconSize: 12, height: 24);
        bd.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var pop = new Popup { PlacementTarget = bd, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
            pop.Child = Wrap(new BackdropPopover(_doc.Backdrop, _app, style =>
            {
                _doc.Backdrop = style;
                _app.Set(K.BackdropStyle, style.ToJson());
                _doc.Dirty = true;
                RefreshImage();
                if (_autoFit)
                {
                    FitToWindow();
                }
            }, showSliders: true));
            pop.IsOpen = true;
        };
        var wm = new EdButton(G.Signature, Strings.Screenshot.WatermarkLabel, null, null, iconSize: 12, height: 24) { Active = !_doc.Watermark.IsNone };
        wm.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var pop = new Popup { PlacementTarget = wm, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
            pop.Child = Wrap(new WatermarkPopover(_doc.Watermark, _app, style =>
            {
                _doc.Watermark = style;
                _app.Set(K.WatermarkStyle, style.ToJson());
                _doc.Dirty = true;
                RefreshImage();
                wm.Active = !style.IsNone;
            }));
            pop.IsOpen = true;
        };
        Group(bd, wm);
    }

    private static Border Wrap(UIElement child) => new()
    {
        Child = child, Background = Ed.Plate, BorderBrush = Ed.PlateStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(8),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.35, Direction = 270 },
    };

    private void BuildExpandBar()
    {
        _bar.Children.Add(Ed.Label("Color", 11.5, FontWeights.Medium, Ed.FgDim));
        var combo = new ComboBox { Width = 130, Margin = new Thickness(8, 0, 8, 0), Height = 24 };
        combo.Items.Add("White");
        combo.Items.Add("Transparent");
        combo.Items.Add("Custom color");
        combo.SelectedIndex = _expandFill switch { "transparent" => 1, "custom" => 2, _ => 0 };
        combo.SelectionChanged += (_, _) =>
        {
            _expandFill = combo.SelectedIndex switch { 1 => "transparent", 2 => "custom", _ => "white" };
            if (_expandFill == "custom")
            {
                using var dlg = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(_expandCustom.R, _expandCustom.G, _expandCustom.B), FullOpen = true };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _expandCustom = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                }
            }

            _overlay.InvalidateVisual();
        };
        _bar.Children.Add(combo);
        _bar.Children.Add(Ed.Label($"{(int)Math.Round(_expandRect.Width)} × {(int)Math.Round(_expandRect.Height)} px", 11, FontWeights.Medium, Ed.FgDim));
    }

    private void MoveLayer(int dir)
    {
        if (_selected is null)
        {
            return;
        }

        var i = _doc.Marks.IndexOf(_selected);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= _doc.Marks.Count)
        {
            return;
        }

        _doc.Push();
        _doc.Marks.RemoveAt(i);
        _doc.Marks.Insert(j, _selected);
        RefreshAll();
    }

    // ---- style controls -----------------------------------------------------------------------------------------------------

    private UIElement ColorDots()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var current = _selected?.Color ?? _color;
        foreach (var (name, label, c) in Palette.Colors)
        {
            var dot = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(8.5), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1) };
            var cell = new Grid { Width = 24, Height = 24, Cursor = Cursors.Hand, ToolTip = label, Background = Brushes.Transparent };
            if (name == current)
            {
                cell.Children.Add(new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(11.5), BorderBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), BorderThickness = new Thickness(1.5) });
            }

            cell.Children.Add(dot);
            var captured = name;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _color = captured;
                _app.Set(K.LastColor, captured);
                if (_selected is { } s && s.Kind is not (ToolId.Sticker or ToolId.Image or ToolId.Pixelate or ToolId.Spotlight))
                {
                    s.Color = captured;
                    MarkChanged();
                }

                RebuildBar();
            };
            row.Children.Add(cell);
        }

        return row;
    }

    private UIElement ThicknessPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = Strings.Screenshot.ThicknessLabel };
        var current = _selected?.Stroke ?? _stroke;
        foreach (var (name, h) in new[] { ("small", 1.8), ("medium", 3.4), ("large", 5.4) })
        {
            var sel = name == current;
            var bar = new Border { Width = 15, Height = h, CornerRadius = new CornerRadius(h / 2), Background = sel ? Ed.Accent : Ed.FgDim };
            var cell = new Border { Width = 25, Height = 24, CornerRadius = new CornerRadius(6), Background = sel ? Ed.AccentFill : Brushes.Transparent, Child = bar, Cursor = Cursors.Hand };
            var captured = name;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _stroke = captured;
                _app.Set(K.LastStroke, captured);
                if (_selected is { } s)
                {
                    s.Stroke = captured;
                    MarkChanged();
                }

                RebuildBar();
            };
            row.Children.Add(cell);
        }

        return row;
    }

    private UIElement FontSizeControls()
    {
        var presets = new[] { 10, 12, 14, 16, 19, 24, 28, 36, 48, 64, 72, 96 };
        var current = _selected?.TextSize ?? _textSize;
        void Apply(int size)
        {
            size = Math.Clamp(size, 8, 200);
            _textSize = size;
            _app.Set(K.LastTextSize, size);
            if (_selected is { Kind: ToolId.Text } s)
            {
                s.TextSize = size;
                MarkChanged();
            }

            RebuildBar();
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = Strings.Screenshot.FontSizeLabel };
        row.Children.Add(new EdButton("", null, null, () => Apply(Step(presets, current, -1)), iconSize: 11, width: 22));
        var label = new EdButton(null, $"{current} pt", null, null, height: 24);
        label.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            foreach (var p in presets)
            {
                var size = p;
                var item = new MenuItem { Header = $"{p} pt", IsChecked = p == current };
                item.Click += (_, _) => Apply(size);
                menu.Items.Add(item);
            }

            menu.PlacementTarget = label;
            menu.IsOpen = true;
        };
        row.Children.Add(label);
        row.Children.Add(new EdButton("", null, null, () => Apply(Step(presets, current, 1)), iconSize: 11, width: 22));
        return row;
    }

    private static int Step(int[] presets, int current, int dir)
    {
        if (dir < 0)
        {
            return presets.LastOrDefault(p => p < current, presets[0]);
        }

        return presets.FirstOrDefault(p => p > current, presets[^1]);
    }

    private UIElement ArrowStyleMenu()
    {
        var current = _selected?.ArrowStyle ?? _arrowStyle;
        var styles = new[] { ("filled", "Solid"), ("outline", "Outline"), ("open", "Open"), ("doubleEnded", "Double-ended"), ("scribbly", "Scribbly") };
        var label = styles.First(s => s.Item1 == current).Item2;
        var btn = new EdButton("", label, "Arrow style", null, iconSize: 12, height: 24);
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            foreach (var (id, title) in styles)
            {
                var item = new MenuItem { Header = title, IsChecked = id == current };
                var captured = id;
                item.Click += (_, _) =>
                {
                    _arrowStyle = captured;
                    _app.Set(K.LastArrowStyle, captured);
                    if (_selected is { Kind: ToolId.Arrow } s)
                    {
                        s.ArrowStyle = captured;
                        MarkChanged();
                    }

                    RebuildBar();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        };
        return btn;
    }

    private UIElement StickerMenu()
    {
        var current = _selected?.Sticker ?? _sticker;
        var btn = new EdButton(null, "Sticker", null, null, height: 24);
        var glyph = Ed.Label(Palette.StickerGlyph(current), 16);
        glyph.FontFamily = new FontFamily("Segoe UI Emoji");
        glyph.Margin = new Thickness(0, 0, 6, 0);
        if (btn.Child is StackPanel sp)
        {
            sp.Children.Insert(0, glyph);
        }

        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            foreach (var (id, g) in Palette.Stickers)
            {
                var item = new MenuItem { Header = new TextBlock { Text = g, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 18 }, IsChecked = id == current };
                var captured = id;
                item.Click += (_, _) =>
                {
                    _sticker = captured;
                    _app.Set(K.LastSticker, captured);
                    if (_selected is { Kind: ToolId.Sticker } s)
                    {
                        s.Sticker = captured;
                        MarkChanged();
                    }

                    RebuildBar();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        };
        return btn;
    }

    private UIElement BlurStyleMenu()
    {
        var current = _selected?.BlurStyle ?? _blurStyle;
        var styles = new[] { ("pixelate", "Pixelate"), ("blur", "Blur"), ("erase", "Erase") };
        var btn = new EdButton("", styles.First(s => s.Item1 == current).Item2, "Blur style", null, iconSize: 12, height: 24);
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            foreach (var (id, title) in styles)
            {
                var item = new MenuItem { Header = title, IsChecked = id == current };
                var captured = id;
                item.Click += (_, _) =>
                {
                    _blurStyle = captured;
                    _app.Set(K.LastBlurStyle, captured);
                    if (_selected is { Kind: ToolId.Pixelate } s)
                    {
                        s.BlurStyle = captured;
                        MarkChanged();
                    }

                    RebuildBar();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        };
        return btn;
    }

    private UIElement BlurTextOnlyToggle()
    {
        var on = _selected?.TextOnly ?? _blurTextOnly;
        var btn = new EdButton("", null, "Text only", null, iconSize: 12, width: 26) { Active = on };
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var value = !on;
            _blurTextOnly = value;
            _app.Set(K.LastBlurTextOnly, value);
            if (_selected is { Kind: ToolId.Pixelate } s)
            {
                s.TextOnly = value;
                MarkChanged();
            }

            RebuildBar();
        };
        return btn;
    }

    private UIElement BlurStrength()
    {
        var current = _selected?.BlurLevel ?? _blurLevel;
        var row = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = "Blur strength" };
        row.Children.Add(Ed.Icon("", 10, Ed.FgDim));
        var slider = Ed.NewSlider(1, 5, current, 84, v =>
        {
            var level = (int)Math.Round(v);
            _blurLevel = level;
            _app.Set(K.LastBlurLevel, level);
            if (_selected is { Kind: ToolId.Pixelate } s && s.BlurLevel != level)
            {
                s.BlurLevel = level;
                MarkChanged();
            }
        }, 1);
        slider.Margin = new Thickness(6, 0, 6, 0);
        row.Children.Add(slider);
        row.Children.Add(Ed.Icon("", 14, Ed.FgDim));
        return row;
    }
}
