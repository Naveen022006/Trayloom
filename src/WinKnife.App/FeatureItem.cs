// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using WinKnife.Core;

namespace WinKnife.App;

/// <summary>View model for one row of the Features hub.</summary>
internal sealed class FeatureItem : INotifyPropertyChanged
{
    private const string IslandFeatureId = "island";

    private readonly FeatureRegistry _registry;

    public FeatureItem(IFeature feature, FeatureRegistry registry)
    {
        Feature = feature;
        _registry = registry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IFeature Feature { get; }

    public string Name => Feature.Name;

    public string Description => Feature.Description;

    public string GroupName => Feature.Group.DisplayName();

    public int GroupOrder => (int)Feature.Group;

    public bool IsInstalled
    {
        get => _registry.IsInstalled(Feature.Id);
        set
        {
            if (value != IsInstalled)
            {
                _registry.SetInstalled(Feature.Id, value);
                Refresh();
            }
        }
    }

    public bool IsRunning => _registry.IsRunning(Feature.Id);

    public string Status
    {
        get
        {
            if (_registry.GetError(Feature.Id) is { } error)
            {
                return $"Failed to start: {error}";
            }

            return IsRunning ? "Running" : IsInstalled ? "Installed" : "Not installed";
        }
    }

    /// <summary>Extra line under the description: island extensions need the Dynamic Island feature itself to be visible.</summary>
    public string? Notice
    {
        get
        {
            if (Feature.Group != FeatureGroup.Island || Feature.Id == IslandFeatureId)
            {
                return null;
            }

            return _registry.IsInstalled(IslandFeatureId)
                ? "Shows up as a section of the Dynamic Island."
                : "Requires the Dynamic Island feature. Install it too, otherwise nothing will be shown.";
        }
    }

    public bool HasError => _registry.GetError(Feature.Id) is not null;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
    }
}
