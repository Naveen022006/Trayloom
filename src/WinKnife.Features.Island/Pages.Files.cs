// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>
/// The files parked in the island: the island is a second window onto the Shelf feature (<see cref="IShelfService"/>);
/// without the shelf installed it shows an empty page and nothing is kept.
/// </summary>
internal static class IslandShelf
{
    private static IShelfService? _hooked;

    private static IShelfService? Service => IslandCore.Current?.Ctx.Services.Get<IShelfService>();

    public static bool Available => Service is not null;

    public static event Action? Changed;

    public static IReadOnlyList<string> Items
    {
        get
        {
            var service = Service;
            if (!ReferenceEquals(service, _hooked))
            {
                if (_hooked is not null)
                {
                    _hooked.Changed -= Forward;
                }

                _hooked = service;
                if (service is not null)
                {
                    service.Changed += Forward;
                }
            }

            return service?.Paths ?? [];
        }
    }

    private static void Forward() => Changed?.Invoke();

    public static void Add(IEnumerable<string> files) => Service?.Add(files);

    public static void Remove(IEnumerable<string> files) => Service?.Remove(files);

    public static void Clear() => Service?.Clear();
}

internal sealed class FilesPage : IslandPage
{
    private readonly Grid _view = new();
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };
    private readonly HashSet<string> _selected = [];
    private string _status = "";
    private bool _statusFailed;
    private bool _zipping;
    private CancellationTokenSource? _cts;
    private (int Done, int Total) _progress;

    public FilesPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
    }

    public override string ModuleId => "files";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override FrameworkElement? Trailing => _trailing;

    public override double Height(double width, double budget)
    {
        Build();
        return budget;
    }

    public override void Shown() => IslandShelf.Changed += OnChanged;

    public override void Hidden()
    {
        IslandShelf.Changed -= OnChanged;
        _cts?.Cancel();
    }

    private void OnChanged() => _view.Dispatcher.BeginInvoke(Build);

    private void Build()
    {
        _view.Children.Clear();
        _leading.Children.Clear();
        _trailing.Children.Clear();
        var items = IslandShelf.Items;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (_zipping)
        {
            var sp = PageUi.Spinner(14);
            _leading.Children.Add(sp);
            var txt = D.Digits($"Create ZIP · {_progress.Done}/{_progress.Total}", 12, null, D.Secondary);
            txt.Margin = new Thickness(8, 0, 6, 0);
            _leading.Children.Add(txt);
            _leading.Children.Add(D.IconButton(G.Close, Loc.T("Cancel"), () => _cts?.Cancel(), size: 28));
        }
        else if (items.Count > 0)
        {
            var hint = D.T(_selected.Count > 0 ? Loc.F("{0} selected", _selected.Count) : Loc.T("Click to select. Drag out to use or right-click for more actions."), _selected.Count > 0 ? 12 : 10, null, D.Secondary, true);
            hint.MaxWidth = 220;
            hint.MaxHeight = 28;
            _leading.Children.Add(hint);
        }

        if (items.Count > 0)
        {
            var mediaService = Core.Ctx.Services.Get<IMediaToolsService>();
            var media = D.IconButton("", Loc.T("Media"), () =>
            {
                var chosen = _selected.Count > 0 ? _selected.ToList() : IslandShelf.Items.ToList();
                mediaService?.OpenWith(chosen);
            }, size: 28);
            media.IsEnabled = mediaService is not null;
            media.Visibility = Core.Ctx.Runtime.IsAvailable("media-tools") ? Visibility.Visible : Visibility.Collapsed;
            _trailing.Children.Add(media);
            HoverButton? zip = null;
            zip = D.IconButton(G.Zip, Loc.T("Create ZIP"), () => IslandMenu.Show(zip!, [new IslandMenu.Item(Loc.T("Create ZIP"), StartZip, Enabled: !_zipping, Glyph: G.Zip)], () => HasWindow ? Window.HoldOpen() : null), size: 28);
            _trailing.Children.Add(zip);
            HoverButton? trash = null;
            trash = D.IconButton(G.Trash, _selected.Count > 0 ? Loc.T("Remove selected") : Loc.T("Clear all"), () =>
            {
                if (_selected.Count > 0)
                {
                    IslandShelf.Remove(_selected.ToList());
                    _selected.Clear();
                }
                else
                {
                    IslandMenu.Show(trash!, [new IslandMenu.Item(Loc.T("Clear all"), IslandShelf.Clear, Destructive: true), new IslandMenu.Item(Loc.T("Cancel"), null)], () => HasWindow ? Window.HoldOpen() : null);
                }
            }, size: 28);
            trash.Margin = new Thickness(6, 0, 0, 0);
            trash.IsEnabled = !_zipping;
            _trailing.Children.Add(trash);
        }

        FrameworkElement body;
        if (items.Count == 0)
        {
            var grid = new Grid();
            var dash = new System.Windows.Shapes.Rectangle { RadiusX = 20, RadiusY = 20, Stroke = D.White(0.18), StrokeThickness = 0.75, StrokeDashArray = [4, 5] };
            grid.Children.Add(dash);
            grid.Children.Add(D.EmptyView(G.Files, Strings.DropHint));
            body = grid;
        }
        else
        {
            var wrap = new WrapPanel { Orientation = Orientation.Vertical, Height = 150 };
            foreach (var path in items)
            {
                wrap.Children.Add(Tile(path));
            }

            var sc = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = wrap };
            sc.PreviewMouseWheel += (s, e) => { sc.ScrollToHorizontalOffset(sc.HorizontalOffset - e.Delta); e.Handled = true; };
            body = sc;
        }

        root.Children.Add(body);
        if (_status.Length > 0)
        {
            var st = D.T(_status, 11, null, _statusFailed ? D.Orange : D.Secondary, true);
            st.MaxHeight = 48;
            st.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(st, 1);
            root.Children.Add(st);
        }

        _view.Children.Add(root);
    }

    private FrameworkElement Tile(string path)
    {
        var selected = _selected.Contains(path);
        var tile = new HoverButton(() => { if (!_selected.Remove(path)) { _selected.Add(path); } Build(); }) { Width = 84, Height = 72, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 6), Fill = selected ? D.White(0.14) : D.White(0.06) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = PlaybackService.IconOf(path);
        FrameworkElement ic = icon is not null ? new Image { Source = icon, Width = 32, Height = 32 } : D.I(G.Files, 26, D.Text);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(ic);
        var name = D.T(Path.GetFileName(path), 10, FontWeights.Medium, D.Text, false, TextTrimming.CharacterEllipsis);
        name.MaxWidth = 76;
        name.Margin = new Thickness(0, 6, 0, 0);
        name.TextAlignment = TextAlignment.Center;
        stack.Children.Add(name);
        tile.Child = stack;
        D.Tip(tile, path);
        Point? start = null;
        tile.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(tile);
        tile.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && start is { } s && (e.GetPosition(tile) - s).Length > 8)
            {
                start = null;
                using var hold = HasWindow ? Window.HoldOpen() : null;
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            IslandMenu.Show(tile,
            [
                new IslandMenu.Item(Loc.T("Open"), () => DownloadsPage.OpenPath(path)),
                new IslandMenu.Item(Loc.T("Show in folder"), () => DownloadsPage.Reveal(path)),
                new IslandMenu.Item(Loc.T("Remove selected"), () => IslandShelf.Remove([path])),
            ], () => HasWindow ? Window.HoldOpen() : null);
            e.Handled = true;
        };
        return tile;
    }

    private async void StartZip()
    {
        var inputs = _selected.Count > 0 ? _selected.ToList() : IslandShelf.Items.ToList();
        if (inputs.Count == 0)
        {
            return;
        }

        using var hold = HasWindow ? Window.HoldOpen() : null;
        string? outFolder = null;
        string? single = null;
        if (inputs.Count == 1)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "ZIP archive|*.zip", FileName = Unique(Path.GetDirectoryName(inputs[0]) ?? "", Path.GetFileName(inputs[0]) + ".zip"), InitialDirectory = Path.GetDirectoryName(inputs[0]) };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            single = dlg.FileName;
        }
        else
        {
            using var f = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.T("Each selected item is saved as a separate ZIP. Originals stay unchanged."), UseDescriptionForTitle = true };
            if (f.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            outFolder = f.SelectedPath;
        }

        _zipping = true;
        _status = "";
        _progress = (0, inputs.Count);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Build();
        var outputs = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                foreach (var input in inputs)
                {
                    token.ThrowIfCancellationRequested();
                    var dest = single ?? Unique(outFolder!, Path.GetFileName(input) + ".zip");
                    var temp = dest + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
                    try
                    {
                        if (Directory.Exists(input))
                        {
                            if (Path.GetFullPath(dest).StartsWith(Path.GetFullPath(input).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new IOException("The destination is inside the folder being archived.");
                            }

                            ZipFile.CreateFromDirectory(input, temp, CompressionLevel.Optimal, true);
                        }
                        else
                        {
                            using var zip = ZipFile.Open(temp, ZipArchiveMode.Create);
                            zip.CreateEntryFromFile(input, Path.GetFileName(input));
                        }

                        token.ThrowIfCancellationRequested();
                        if (File.Exists(dest))
                        {
                            dest = Unique(Path.GetDirectoryName(dest)!, Path.GetFileName(dest));
                        }

                        File.Move(temp, dest);
                        outputs.Add(dest);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                        {
                            File.Delete(temp);
                        }
                    }

                    _progress = (outputs.Count, inputs.Count);
                    _view.Dispatcher.BeginInvoke(Build);
                }
            }, token);
            _status = Loc.T("Saved");
            _statusFailed = false;
            IslandShelf.Add(outputs);
        }
        catch (OperationCanceledException)
        {
            _status = Loc.T("Cancelled.");
            _statusFailed = false;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _statusFailed = true;
        }

        _zipping = false;
        Build();
    }

    private static string Unique(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }
}
