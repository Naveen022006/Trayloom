// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinKnife.Core;

namespace WinKnife.App;

/// <summary>The Features hub: grouped, searchable list of every feature with install toggles and settings.</summary>
public partial class FeaturesWindow : Window
{
    private readonly FeatureRegistry _registry;
    private readonly ILog _log;
    private readonly ObservableCollection<FeatureItem> _items = [];
    private readonly ICollectionView _view;

    internal FeaturesWindow(FeatureRegistry registry, ILog log)
    {
        InitializeComponent();
        _registry = registry;
        _log = log;

        foreach (var feature in registry.Features)
        {
            _items.Add(new FeatureItem(feature, registry));
        }

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FeatureItem.GroupName)));
        _view.SortDescriptions.Add(new SortDescription(nameof(FeatureItem.GroupOrder), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(FeatureItem.Name), ListSortDirection.Ascending));
        _view.Filter = Matches;
        FeatureList.ItemsSource = _view;

        _registry.FeatureStateChanged += OnFeatureStateChanged;
        Closed += (_, _) =>
        {
            _registry.FeatureStateChanged -= OnFeatureStateChanged;
            SettingsHost.Content = null;
        };

        if (_items.Count > 0)
        {
            FeatureList.SelectedItem = _view.Cast<FeatureItem>().FirstOrDefault();
        }
    }

    private FeatureItem? Selected => FeatureList.SelectedItem as FeatureItem;

    private bool Matches(object obj)
    {
        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query) || obj is not FeatureItem item)
        {
            return true;
        }

        return item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || item.GroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            item.IsInstalled = !item.IsInstalled;
        }
    }

    private void OnFeatureStateChanged(IFeature feature)
    {
        var item = _items.FirstOrDefault(i => i.Feature.Id == feature.Id);
        item?.Refresh();
        if (feature.Id == "island" && Selected is { } selected && selected.Feature.Group == FeatureGroup.Island)
        {
            ShowDetails();
        }

        if (item is not null && ReferenceEquals(item, Selected))
        {
            ShowDetails();
        }
    }

    private void ShowDetails()
    {
        SettingsHost.Content = null;
        if (Selected is not { } item)
        {
            EmptyHint.Visibility = Visibility.Visible;
            DetailsScroll.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        DetailsScroll.Visibility = Visibility.Visible;
        DetailName.Text = item.Name;
        DetailGroup.Text = item.GroupName;
        DetailDescription.Text = item.Description;
        DetailNotice.Text = item.Notice ?? string.Empty;
        DetailNotice.Visibility = item.Notice is null ? Visibility.Collapsed : Visibility.Visible;
        DetailStatus.Text = item.Status;
        InstallButton.Content = item.IsInstalled ? "Uninstall" : "Install";

        if (!item.IsRunning)
        {
            SettingsHint.Text = item.IsInstalled ? "Settings are available once the feature is running." : "Install this feature to configure it.";
            SettingsHint.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var view = item.Feature.CreateSettingsView();
            SettingsHost.Content = view;
            SettingsHint.Text = "This feature has no settings.";
            SettingsHint.Visibility = view is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _log.Error($"Settings view of {item.Feature.Id} failed", ex);
            SettingsHint.Text = "The settings could not be loaded.";
            SettingsHint.Visibility = Visibility.Visible;
        }
    }
}
