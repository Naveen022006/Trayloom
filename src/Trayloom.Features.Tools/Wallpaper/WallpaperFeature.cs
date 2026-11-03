// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>Wallpaper (catalog id <c>wallpaper</c>): a still-image gallery in the tray panel that sets the desktop picture with one click.</summary>
public sealed class WallpaperFeature : ToolsFeatureBase
{
    private WallpaperService? _service;

    /// <inheritdoc />
    public override string Id => "wallpaper";

    /// <inheritdoc />
    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", Loc.T("Wallpaper"), () => Safe(() => Ctx.Shell.OpenPanel(PanelTabs.Wallpaper)), ""),
    ];

    /// <inheritdoc />
    protected override void RegisterDefaults(IAppSettings app) => ToolsDefaults.Register(app);

    /// <inheritdoc />
    protected override void OnStart()
    {
        _service = new WallpaperService(Ctx);
        WallpaperService.Instance = _service;
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        _service?.Clear();
        WallpaperService.Instance = null;
        _service = null;
    }
}

/// <summary>Quick tools page card "Wallpaper" (02c 5.6).</summary>
public sealed class WallpaperSettingsSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "wallpaper";

    /// <inheritdoc />
    public string PageId => SettingsPages.QuickTools;

    /// <inheritdoc />
    public string? Anchor => "wallpaper";

    /// <inheritdoc />
    public string? Title => Loc.T("Wallpaper");

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Show on all displays", "Open Wallpaper settings", "background", "desktop picture"];

    /// <inheritdoc />
    public int Order => 60;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext c)
    {
        ToolsDefaults.Register(c.App);
        var card = new SettingsCard(Loc.T("Wallpaper"));
        card.Add(new SettingsRow(null, Loc.T("Show on all displays"), null, new MacSwitch(c.App.Get("wallpaperApplyAllDisplays", true), SwitchSize.Regular, on => c.App.Set("wallpaperApplyAllDisplays", on))));
        var open = UiKit.Button(Loc.T("Open Wallpaper settings"), ButtonKind.Bordered, 13, "", () => Plat.Open("ms-settings:personalization-background"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        return card;
    }
}

/// <summary>The Wallpaper tab of the tray panel (spec 01 5.11).</summary>
public sealed class WallpaperPanelSection : IPanelSection
{
    /// <inheritdoc />
    public string? FeatureId => "wallpaper";

    /// <inheritdoc />
    public string TabId => PanelTabs.Wallpaper;

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public FrameworkElement CreateView(IPanelContext context)
    {
        var service = WallpaperService.Instance;
        return service is null ? new StackPanel() : new WallpaperGalleryView(service, context);
    }
}

/// <summary>Filter, switch, source buttons, thumbnail grid (3 x 8 per page), pager and status line.</summary>
internal sealed class WallpaperGalleryView : StackPanel
{
    private readonly WallpaperService _service;
    private readonly IPanelContext _ctx;
    private int _page = 1;
    private bool _removeMode;
    private int _generation;

    public WallpaperGalleryView(WallpaperService service, IPanelContext ctx)
    {
        _service = service;
        _ctx = ctx;
        Loaded += (_, _) =>
        {
            _service.Changed += OnChanged;
            _service.ViewerAdded();
            _ctx.SetKeepOpen(true);
            Build();
        };
        Unloaded += (_, _) =>
        {
            _service.Changed -= OnChanged;
            _service.ViewerRemoved();
            _ctx.SetKeepOpen(false);
        };
        Build();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Build);

    private void Build()
    {
        Children.Clear();
        _generation = _service.Generation;
        var entries = _service.Filtered.ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)WallpaperService.PageSize));
        _page = Math.Clamp(_page, 1, pages);

        var controls = new StackPanel();
        controls.Children.Add(BuildFilter());
        controls.AddSpaced(BuildAllDisplaysRow(), 8);
        controls.AddSpaced(BuildButtons(), 8);
        Children.Add(controls);

        if (_removeMode && _service.Sources.Count > 0)
        {
            var strip = BuildRemoveStrip();
            strip.Margin = new Thickness(0, 10, 0, 0);
            Children.Add(strip);
        }

        var gallery = BuildGallery(entries, pages);
        gallery.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(gallery);

        if (true)
        {
            var pager = BuildPager(pages);
            pager.Margin = new Thickness(0, 10, 0, 0);
            Children.Add(pager);
        }

        if (_service.Downloading)
        {
            Children.Add(Status(Loc.T("Downloading…"), ThemeKeys.TextSecondary));
        }
        else if (_service.Error is { } error)
        {
            Children.Add(Status(error, ThemeKeys.Orange));
        }
    }

    private static TextBlock Status(string text, string key)
    {
        var block = UiKit.Text(text, 10.5, FontWeights.Normal, key, wrap: true);
        block.Margin = new Thickness(0, 10, 0, 0);
        return block;
    }

    private FrameworkElement BuildFilter()
    {
        var combo = new ComboBox { FontSize = 12, ItemsSource = new[] { Loc.T("All"), Loc.T("Your pictures"), Loc.T("Windows") } };
        combo.SelectedIndex = _service.Filter switch { "own" => 1, "apple" => 2, _ => 0 };
        combo.SelectionChanged += (_, _) =>
        {
            var value = combo.SelectedIndex switch { 1 => "own", 2 => "apple", _ => "all" };
            if (value != _service.Filter)
            {
                _service.Filter = value;
                _page = 1;
                Build();
            }
        };
        return combo;
    }

    private FrameworkElement BuildAllDisplaysRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(Loc.T("Show on all displays"), 11.5, FontWeights.Medium);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var toggle = new MacSwitch(_ctx.App.Get("wallpaperApplyAllDisplays", true), SwitchSize.Small, on => _ctx.App.Set("wallpaperApplyAllDisplays", on));
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    private FrameworkElement BuildButtons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var addImage = PanelButtons.Mini(Loc.T("Add image"), null, AddImages);
        var addFolder = PanelButtons.Mini(Loc.T("Add folder"), null, AddFolder);
        addImage.IsEnabled = addFolder.IsEnabled = !_removeMode;
        row.Children.Add(addImage);
        addFolder.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(addFolder);
        if (_service.Sources.Count > 0 || _removeMode)
        {
            var remove = PanelButtons.Mini(_removeMode ? Loc.T("Done") : Loc.T("Remove"), null, () =>
            {
                _removeMode = !_removeMode;
                Build();
            });
            remove.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(remove);
        }

        return row;
    }

    private void AddImages()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = Loc.T("Choose images to keep in Trayloom’s wallpaper list"),
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() == true)
        {
            _service.AddSources(dialog.FileNames);
        }
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose a folder of images to keep in Trayloom’s wallpaper list"), InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        if (dialog.ShowDialog() == true)
        {
            _service.AddSources([dialog.FolderName]);
        }
    }

    private FrameworkElement BuildRemoveStrip()
    {
        var panel = new WrapPanel();
        foreach (var source in _service.Sources)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x26, 128, 128, 128)),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(UiKit.Icon(source.Unreachable ? "" : source.IsFolder ? "" : "", 11, ThemeKeys.TextSecondary, FontWeights.SemiBold));
            var title = UiKit.Text(source.Unreachable ? Loc.T("Unavailable") : System.IO.Path.GetFileName(source.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : source.Path, 10.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            title.MaxWidth = 130;
            title.ToolTip = source.Path;
            title.Margin = new Thickness(6, 0, 6, 0);
            row.Children.Add(title);
            var x = XBadge(14);
            x.ToolTip = Loc.T("Remove");
            x.MouseLeftButtonUp += (_, _) => _service.RemoveSource(source.Path);
            row.Children.Add(x);
            chip.Child = row;
            panel.Children.Add(chip);
        }

        return panel;
    }

    private static Border XBadge(double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
        Cursor = Cursors.Hand,
        Child = new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = size * 0.55, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private FrameworkElement BuildGallery(List<WallpaperEntry> entries, int pages)
    {
        var host = new Grid();
        var pageEntries = entries.Skip((_page - 1) * WallpaperService.PageSize).Take(WallpaperService.PageSize).ToList();
        var rows = pages > 1 ? 8 : Math.Max(2, (int)Math.Ceiling(pageEntries.Count / 3.0));
        host.Height = rows * 54 + (rows - 1) * 8;
        if (pageEntries.Count == 0)
        {
            if (_service.Loading)
            {
                host.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 80, Height = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            }
            else
            {
                var text = _service.Filter switch { "own" => Loc.T("No pictures added yet"), "apple" => "No Windows wallpapers found", _ => Loc.T("No wallpapers found") };
                var empty = UiKit.Text(text, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.VerticalAlignment = VerticalAlignment.Center;
                host.Children.Add(empty);
            }

            return host;
        }

        var grid = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-4, -4, -4, 0) };
        foreach (var entry in pageEntries)
        {
            grid.Children.Add(BuildCell(entry));
        }

        host.Children.Add(grid);
        // prefetch the next page
        foreach (var next in entries.Skip(_page * WallpaperService.PageSize).Take(WallpaperService.PageSize))
        {
            _ = _service.ThumbnailAsync(next.Path, _generation);
        }

        return host;
    }

    private FrameworkElement BuildCell(WallpaperEntry entry)
    {
        var applied = _service.AppliedPath is { } a && a.Equals(entry.Path, StringComparison.OrdinalIgnoreCase);
        var cell = new Grid { Height = 54, Margin = new Thickness(4), Cursor = Cursors.Hand, ToolTip = entry.Title };
        var inner = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x26, 128, 128, 128)) };
        inner.Children.Add(UiKit.Icon("", 16, ThemeKeys.TextSecondary));
        var image = new Image { Stretch = Stretch.UniformToFill };
        inner.Children.Add(image);
        cell.SizeChanged += (_, e) => inner.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 6, 6);
        cell.Children.Add(inner);
        _ = LoadThumb(image, entry.Path);
        if (applied)
        {
            var border = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
            border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            cell.Children.Add(border);
        }

        if (_removeMode && entry.IsOwn)
        {
            var x = XBadge(16);
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.VerticalAlignment = VerticalAlignment.Top;
            x.Margin = new Thickness(0, -4, -4, 0);
            x.ToolTip = Loc.T("Remove");
            x.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _service.RemoveImage(entry.Path);
            };
            cell.Children.Add(x);
        }

        cell.MouseLeftButtonUp += async (_, _) =>
        {
            if (_removeMode || _service.Downloading)
            {
                return;
            }

            await _service.ApplyAsync(entry);
        };
        return cell;
    }

    private async Task LoadThumb(Image image, string path)
    {
        var source = await _service.ThumbnailAsync(path, _generation);
        if (source is not null)
        {
            image.Source = source;
        }
    }

    private FrameworkElement BuildPager(int pages)
    {
        var grid = new Grid { Height = 22 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (pages > 1)
        {
            left.Children.Add(PagerButton("", Loc.T("Previous"), _page > 1, () => { _page--; Build(); }));
            var label = UiKit.Text($"{_page} / {pages}", 10.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            label.FontFamily = UiKit.DisplayFont;
            label.Margin = new Thickness(12, 0, 12, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(label);
            left.Children.Add(PagerButton("", Loc.T("Next"), _page < pages, () => { _page++; Build(); }));
        }

        grid.Children.Add(left);
        var link = UiKit.Text(Loc.T("Open Wallpaper settings"), 10, FontWeights.Normal, ThemeKeys.TextSecondary);
        link.HorizontalAlignment = HorizontalAlignment.Right;
        link.VerticalAlignment = VerticalAlignment.Center;
        link.Cursor = Cursors.Hand;
        link.MouseLeftButtonUp += (_, _) => Plat.Open("ms-settings:personalization-background");
        Grid.SetColumn(link, 1);
        grid.Children.Add(link);
        return grid;
    }

    private static Border PagerButton(string glyph, string tip, bool enabled, Action click)
    {
        var button = new Border { Width = 22, Height = 22, Background = Brushes.Transparent, ToolTip = tip, Opacity = enabled ? 1 : 0.35, Cursor = enabled ? Cursors.Hand : Cursors.Arrow, Child = UiKit.Icon(glyph, 11, ThemeKeys.TextSecondary, FontWeights.SemiBold) };
        if (enabled)
        {
            button.MouseLeftButtonUp += (_, _) => click();
        }

        return button;
    }
}
