// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

internal sealed class ScratchPad
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Scratchpad 1";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("updated")]
    public DateTime Updated { get; set; } = DateTime.Now;
}

internal sealed class ScratchDocument
{
    public const int MaxPads = 12;

    [JsonPropertyName("pads")]
    public List<ScratchPad> Pads { get; set; } = [];

    [JsonPropertyName("selectedPadID")]
    public string? SelectedPadId { get; set; }

    public static ScratchDocument Load(string json)
    {
        try
        {
            var doc = string.IsNullOrWhiteSpace(json) ? new ScratchDocument() : JsonSerializer.Deserialize<ScratchDocument>(json) ?? new ScratchDocument();
            if (doc.Pads.Count == 0)
            {
                doc.Pads.Add(new ScratchPad { Name = "Scratchpad 1" });
            }

            doc.SelectedPadId ??= doc.Pads[0].Id;
            return doc;
        }
        catch
        {
            var d = new ScratchDocument();
            d.Pads.Add(new ScratchPad());
            d.SelectedPadId = d.Pads[0].Id;
            return d;
        }
    }

    public string Save() => JsonSerializer.Serialize(this);

    /// <summary>A snapshot of the Scratchpad feature's document (the island edits it and pushes it back with <see cref="Push"/>).</summary>
    public static ScratchDocument FromService(IScratchpadService service)
    {
        var doc = new ScratchDocument { SelectedPadId = service.SelectedId };
        foreach (var p in service.Pads)
        {
            doc.Pads.Add(new ScratchPad { Id = p.Id, Name = p.Name, Text = p.Text, Updated = p.Updated });
        }

        if (doc.Pads.Count == 0)
        {
            doc.Pads.Add(new ScratchPad { Name = "Scratchpad 1" });
        }

        doc.SelectedPadId ??= doc.Pads[0].Id;
        return doc;
    }

    /// <summary>Writes the snapshot back to the Scratchpad feature (one storage for the island and the floating pad).</summary>
    public void Push(IScratchpadService service) =>
        service.Replace([.. Pads.Select(p => new ScratchpadPadInfo(p.Id, p.Name, p.Text, p.Updated))], SelectedPadId);

    public ScratchPad Selected => Pads.FirstOrDefault(p => p.Id == SelectedPadId) ?? Pads[0];

    public string NextName()
    {
        for (var i = 1; ; i++)
        {
            var n = $"Scratchpad {i}";
            if (Pads.All(p => p.Name != n))
            {
                return n;
            }
        }
    }
}

internal sealed class ScratchpadPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private ScratchDocument _doc;
    private readonly TextBox _editor = PageUi.FlatTextBox(13);
    private readonly DispatcherTimer _save = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _formatting;
    private bool _preview;
    private bool _copied;
    private bool _loading;

    public ScratchpadPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        _doc = LoadDocument();
        _save.Tick += (_, _) => Commit();
        _editor.AcceptsReturn = true;
        _editor.AcceptsTab = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.TextChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            var pad = _doc.Selected;
            pad.Text = _editor.Text;
            pad.Updated = DateTime.Now;
            _save.Stop();
            _save.Start();
        };
    }

    public override string ModuleId => "scratchpad";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override bool WantsKeyboard => true;

    public override double Height(double width, double budget)
    {
        Build();
        return budget;
    }

    private IScratchpadService? Service => Core.Ctx.Services.Get<IScratchpadService>();

    private ScratchDocument LoadDocument() =>
        Core.Ctx.Services.Get<IScratchpadService>() is { } service ? ScratchDocument.FromService(service) : ScratchDocument.Load(Core.App.Get<string>(K.ScratchpadDocument));

    public override void Shown()
    {
        if (Service is not null)
        {
            // The floating pad may have edited the document meanwhile: always start from its current state.
            _doc = LoadDocument();
            _view.Dispatcher.BeginInvoke(Build);
        }

        if (HasWindow && !IsPreview)
        {
            Window.EnsureKeyboard();
            _view.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { _editor.Focus(); _editor.CaretIndex = _editor.Text.Length; });
        }
    }

    public override void Hidden() => Commit();

    private void Commit()
    {
        _save.Stop();
        if (Service is { } service)
        {
            _doc.Push(service);
            return;
        }

        Core.App.Set(K.ScratchpadDocument, _doc.Save());
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.T:
                NewPad();
                return true;
            case Key.W:
                ClosePad(_doc.Selected);
                return true;
            case Key.Return:
                _preview = !_preview;
                Build();
                return true;
        }

        return false;
    }

    private void Build()
    {
        Commit2();
        _view.Children.Clear();
        _leading.Children.Clear();
        _trailing.Children.Clear();
        var pad = _doc.Selected;
        // tabs
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in _doc.Pads)
        {
            var selected = p.Id == pad.Id;
            var pp = p;
            var tab = new HoverButton(() => { _doc.SelectedPadId = pp.Id; Build(); }) { Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 0, selected ? 8 : 10, 0), Margin = new Thickness(0, 0, 4, 0), Fill = selected ? D.White(0.14) : D.White(0.05), Lifts = false };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(D.T(p.Name, 11, FontWeights.Medium, selected ? D.Text : D.White(0.6), false, TextTrimming.CharacterEllipsis));
            ((TextBlock)row.Children[0]).MaxWidth = 90;
            if (selected)
            {
                var chev = D.I(G.ChevronDown, 8, D.Text);
                chev.Margin = new Thickness(5, 0, 0, 0);
                row.Children.Add(chev);
            }

            tab.Child = row;
            if (selected)
            {
                tab.MouseLeftButtonUp += (_, ev) => { ShowActions(tab); ev.Handled = true; };
            }

            tab.MouseRightButtonUp += (_, ev) =>
            {
                IslandMenu.Show(tab, [new IslandMenu.Item(Loc.T("Rename scratchpad"), () => Rename(pp)), new IslandMenu.Item(Loc.T("Close scratchpad"), () => ClosePad(pp), Enabled: _doc.Pads.Count > 1, Destructive: true)], () => HasWindow ? Window.HoldOpen() : null);
                ev.Handled = true;
            };
            tabs.Children.Add(tab);
        }

        var scrollTabs = new SmoothScrollViewer { ScrollOrientation = ScrollAxis.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = tabs, MaxWidth = 250 };
        _leading.Children.Add(scrollTabs);
        var full = _doc.Pads.Count >= ScratchDocument.MaxPads;
        var plus = D.IconButton(G.Plus, full ? Loc.F("You can keep up to {0} scratchpads", ScratchDocument.MaxPads) : Loc.T("New scratchpad"), NewPad);
        plus.IsEnabled = !full;
        _leading.Children.Add(plus);
        _trailing.Children.Add(D.IconButton("", Loc.T("Formatting"), () => { _formatting = !_formatting; Build(); }, _formatting));
        var eye = D.IconButton(_preview ? G.Edit : G.Eye, _preview ? Loc.T("Edit text") : Loc.T("Show formatting"), () => { _preview = !_preview; Build(); }, _preview);
        eye.Margin = new Thickness(6, 0, 0, 0);
        eye.IsEnabled = pad.Text.Length > 0;
        _trailing.Children.Add(eye);
        var copy = D.IconButton(_copied ? G.Check : G.Copy, _copied ? Loc.T("Copied") : Loc.T("Copy all"), () => { try { Clipboard.SetText(pad.Text); } catch { } _copied = true; Build(); PageUi.After(_view.Dispatcher, 1.6, () => { _copied = false; Build(); }); });
        copy.Margin = new Thickness(6, 0, 0, 0);
        copy.IsEnabled = pad.Text.Length > 0;
        _trailing.Children.Add(copy);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (_formatting && !_preview)
        {
            root.Children.Add(FormatBar());
        }

        var card = new Border { CornerRadius = new CornerRadius(12), Background = D.White(0.065), Padding = new Thickness(6), Margin = new Thickness(0, _formatting && !_preview ? 6 : 0, 0, 0) };
        Grid.SetRow(card, 1);
        if (_preview)
        {
            card.Child = PageUi.VScroll(Render(pad.Text));
        }
        else
        {
            _loading = true;
            if (_editor.Parent is Panel pnl)
            {
                pnl.Children.Remove(_editor);
            }
            else if (_editor.Parent is Border b)
            {
                b.Child = null;
            }

            _editor.Text = pad.Text;
            _editor.FontSize = Math.Clamp(Core.App.Get<double>(K.ScratchpadTextSize), 10, 22);
            _loading = false;
            var holder = new Grid();
            holder.Children.Add(_editor);
            var ph = D.T(Loc.T("Type anything. It saves by itself."), _editor.FontSize, null, D.White(0.35));
            ph.IsHitTestVisible = false;
            ph.Visibility = pad.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _editor.TextChanged += (_, _) => ph.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            holder.Children.Add(ph);
            card.Child = holder;
        }

        root.Children.Add(card);
        _view.Children.Add(root);
    }

    private void Commit2() => _save.Stop();

    private FrameworkElement FormatBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Height = 28 };
        (string Glyph, string Name, string Prefix, string Suffix)[] marks =
        [
            (G.Bold, Loc.T("Bold"), "**", "**"), (G.Italic, Loc.T("Italic"), "*", "*"), (G.Strike, Loc.T("Strikethrough"), "~~", "~~"), (G.Heading, Loc.T("Heading"), "# ", ""),
            (G.Bullets, Loc.T("Bulleted list"), "- ", ""), (G.Numbers, Loc.T("Numbered list"), "1. ", ""), (G.Quote, Loc.T("Quote"), "> ", ""), (G.Code, Loc.T("Code"), "`", "`"), (G.Link, Loc.T("Link"), "[", "](url)"),
        ];
        foreach (var (g, name, pre, suf) in marks)
        {
            var b = D.IconButton(g, name, () =>
            {
                var sel = _editor.SelectedText;
                var start = _editor.SelectionStart;
                _editor.SelectedText = pre + sel + suf;
                _editor.SelectionStart = start + pre.Length;
                _editor.SelectionLength = sel.Length;
                _editor.Focus();
            }, size: 28);
            bar.Children.Add(b);
        }

        return bar;
    }

    private static FrameworkElement Render(string md)
    {
        var stack = new StackPanel();
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var line = raw;
            double size = 13;
            var weight = FontWeights.Normal;
            var prefix = "";
            Brush brush = D.Text;
            if (line.StartsWith("# "))
            {
                line = line[2..];
                size = 18;
                weight = FontWeights.Bold;
            }
            else if (line.StartsWith("## "))
            {
                line = line[3..];
                size = 16;
                weight = FontWeights.Bold;
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                line = line[2..];
                prefix = "•  ";
            }
            else if (line.StartsWith("> "))
            {
                line = line[2..];
                brush = D.White(0.65);
                prefix = "│ ";
            }
            else if (line.StartsWith("- [ ] "))
            {
                prefix = "☐  ";
                line = line[6..];
            }

            line = line.Replace("**", "").Replace("~~", "").Replace("`", "");
            var t = D.T(prefix + line, size, weight, brush, true);
            t.Margin = new Thickness(0, 2, 0, 2);
            stack.Children.Add(t);
        }

        return stack;
    }

    private void ShowActions(FrameworkElement anchor)
    {
        var pad = _doc.Selected;
        IslandMenu.Show(anchor,
        [
            new IslandMenu.Item(Loc.T("New scratchpad"), NewPad, Enabled: _doc.Pads.Count < ScratchDocument.MaxPads),
            new IslandMenu.Item(Loc.T("Rename scratchpad"), () => Rename(pad)),
            new IslandMenu.Item(Loc.T("Close scratchpad"), () => ClosePad(pad), Enabled: _doc.Pads.Count > 1),
            IslandMenu.Sep(),
            new IslandMenu.Item(Loc.T("Save as file"), () => SaveFile(pad)),
            new IslandMenu.Item(Loc.T("Clear"), () => { pad.Text = ""; Commit(); Build(); }),
            IslandMenu.Sep(),
            new IslandMenu.Item(Loc.T("Open scratchpad"), () => Core.Perform(() => Core.InvokeFeatureAction("scratchpad", "open", "new", "show"))),
        ], () => HasWindow ? Window.HoldOpen() : null);
    }

    private void NewPad()
    {
        if (_doc.Pads.Count >= ScratchDocument.MaxPads)
        {
            return;
        }

        var p = new ScratchPad { Name = _doc.NextName() };
        _doc.Pads.Add(p);
        _doc.SelectedPadId = p.Id;
        Commit();
        Build();
        _editor.Focus();
    }

    private void Rename(ScratchPad pad)
    {
        using var hold = HasWindow ? Window.HoldOpen() : null;
        var dlg = new Window { Title = Loc.T("Rename scratchpad"), Width = 300, Height = 130, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)) };
        var box = new TextBox { Text = pad.Name, Width = 240, Height = 24, Margin = new Thickness(0, 12, 0, 8), MaxLength = 40 };
        var ok = new Button { Content = Loc.T("Save"), Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.T("Cancel"), Width = 70, IsCancel = true };
        ok.Click += (_, _) => dlg.DialogResult = true;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(box);
        stack.Children.Add(row);
        dlg.Content = stack;
        if (dlg.ShowDialog() == true && box.Text.Trim().Length > 0)
        {
            pad.Name = box.Text.Trim();
            Commit();
            Build();
        }
    }

    private void ClosePad(ScratchPad pad)
    {
        if (_doc.Pads.Count <= 1)
        {
            if (HasWindow)
            {
                Window.Collapse();
            }

            return;
        }

        if (pad.Text.Length > 0)
        {
            using var hold = HasWindow ? Window.HoldOpen() : null;
            var r = MessageBox.Show(Loc.F("Delete “{0}” and everything in it?", pad.Name), Loc.T("Close scratchpad"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK)
            {
                return;
            }
        }

        var i = _doc.Pads.IndexOf(pad);
        _doc.Pads.Remove(pad);
        _doc.SelectedPadId = _doc.Pads[Math.Clamp(i, 0, _doc.Pads.Count - 1)].Id;
        Commit();
        Build();
    }

    private void SaveFile(ScratchPad pad)
    {
        using var hold = HasWindow ? Window.HoldOpen() : null;
        var name = $"{pad.Name} {DateTime.Now:yyyy-MM-dd}.txt".Replace(':', '-').Replace('/', '-');
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = name, Filter = "Text|*.txt" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, pad.Text);
            }
            catch
            {
                MessageBox.Show(Loc.T("The file could not be saved"), Loc.T("Scratchpad"));
            }
        }
    }
}
