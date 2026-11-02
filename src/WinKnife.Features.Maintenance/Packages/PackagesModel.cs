// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>State of the package manager page / panel (search, installed list, updates, selection, details).</summary>
internal sealed class PackagesModel
{
    private int _searchGeneration;
    private int _detailsGeneration;
    private int _refreshGeneration;
    private readonly Dictionary<string, bool> _toolIds = new(StringComparer.OrdinalIgnoreCase);
    private OperationRequest? _lastRequest;

    public PackagesModel()
    {
        WingetOperations.Shared.Finished += OnOperationFinished;
    }

    public static PackagesModel Shared { get; } = new();

    public event Action? Changed;

    public List<PackageEntry> Installed { get; private set; } = [];

    public Dictionary<string, PackageEntry> Outdated { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PackageEntry> Results { get; private set; } = [];

    public bool LoadingInstalled { get; private set; }

    public bool LoadingOutdated { get; private set; }

    public bool Searching { get; private set; }

    public bool LoadingDetails { get; private set; }

    public bool HasLoaded { get; private set; }

    public string Query { get; set; } = string.Empty;

    /// <summary>0 = winget, 1 = Microsoft Store.</summary>
    public int SourceIndex { get; set; }

    /// <summary>0 = All, 1 = Apps, 2 = Tools.</summary>
    public int Filter { get; set; }

    public PackageEntry? Selected { get; private set; }

    public PackageDetails? Details { get; private set; }

    public string? Error { get; set; }

    /// <summary>Source that asked for its agreements (trust card).</summary>
    public string? AgreementSource { get; private set; }

    public string? SearchedQuery { get; private set; }

    public string? SourceName => SourceIndex == 1 ? "msstore" : "winget";

    public bool IsBusy => WingetOperations.Shared.IsBusy;

    public bool IsTool(PackageEntry entry) => _toolIds.TryGetValue(entry.Id, out var tool) && tool;

    public int AppCount => Installed.Count(e => !IsTool(e));

    public int ToolCount => Installed.Count(IsTool);

    public int OutdatedCount => Outdated.Count;

    public IEnumerable<PackageEntry> FilteredInstalled
    {
        get
        {
            var list = Filter switch
            {
                1 => Installed.Where(e => !IsTool(e)),
                2 => Installed.Where(IsTool),
                _ => Installed,
            };
            return list.OrderBy(e => HasUpdate(e) ? 0 : 1).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        }
    }

    public bool HasUpdate(PackageEntry entry) => Outdated.ContainsKey(entry.Id);

    public bool IsInstalled(PackageEntry entry) => Installed.Any(e => e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));

    public PackageEntry? InstalledEntry(string id) => Installed.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void Raise() => V.Post(() => Changed?.Invoke());

    public void Clear()
    {
        Selected = null;
        Details = null;
        Raise();
    }

    /// <summary>Loads the installed list and the available updates.</summary>
    public async Task RefreshInstalledAsync(bool clearError = true)
    {
        if (!WingetCli.IsAvailable)
        {
            Raise();
            return;
        }

        var generation = ++_refreshGeneration;
        LoadingInstalled = true;
        LoadingOutdated = true;
        if (clearError)
        {
            Error = null;
        }

        Raise();
        var listTask = WingetCli.ListAsync();
        var upgradeTask = WingetCli.UpgradesAsync();
        var (list, rows) = await listTask;
        if (generation != _refreshGeneration)
        {
            return;
        }

        if (list.Output.Length > 0 && WingetCli.NeedsSourceAgreement(list.Output))
        {
            AgreementSource = "winget";
        }

        if (rows.Count > 0 || list.Ok)
        {
            var known = rows.Where(r => r.Source.Length > 0 && !r.IsOther).ToList();
            Installed = known;
            HasLoaded = true;
        }
        else if (clearError)
        {
            Error = WingetOperations.VisibleError(list.Output);
        }

        LoadingInstalled = false;
        Raise();

        var (upgrade, upgradeRows) = await upgradeTask;
        if (generation != _refreshGeneration)
        {
            return;
        }

        Outdated = upgradeRows.Where(r => r.HasUpdate && r.Source.Length > 0).GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        LoadingOutdated = false;
        if (Selected is not null)
        {
            Selected = Installed.FirstOrDefault(e => e.Id.Equals(Selected.Id, StringComparison.OrdinalIgnoreCase)) ?? Selected;
        }

        Raise();
    }

    public async Task SearchAsync()
    {
        var query = Query.Trim();
        if (query.Length == 0 || IsBusy || !WingetCli.IsAvailable)
        {
            return;
        }

        var generation = ++_searchGeneration;
        Searching = true;
        Results = [];
        SearchedQuery = query;
        Error = null;
        ClearSelectionQuiet();
        Raise();
        var (result, rows) = await WingetCli.SearchAsync(query, SourceName);
        if (generation != _searchGeneration)
        {
            return;
        }

        Results = rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (WingetCli.NeedsSourceAgreement(result.Output))
        {
            AgreementSource = SourceName;
        }
        else if (rows.Count == 0 && !result.Ok && result.ExitCode != WingetCli.NoPackageFound && !result.Output.Contains("No package found", StringComparison.OrdinalIgnoreCase))
        {
            Error = WingetOperations.VisibleError(result.Output);
        }

        Searching = false;
        Raise();
    }

    public void ClearSelectionQuiet()
    {
        Selected = null;
        Details = null;
        LoadingDetails = false;
        _detailsGeneration++;
    }

    public async Task SelectAsync(PackageEntry entry)
    {
        Selected = entry;
        Details = null;
        Error = null;
        LoadingDetails = true;
        var generation = ++_detailsGeneration;
        Raise();
        var details = await WingetCli.ShowAsync(entry.Id, entry.Source.Length > 0 ? entry.Source : null);
        if (generation != _detailsGeneration)
        {
            return;
        }

        Details = details;
        if (details is not null && details.InstallerType.Length > 0)
        {
            _toolIds[entry.Id] = details.IsTool;
        }

        LoadingDetails = false;
        Raise();
    }

    public void AcceptAgreements()
    {
        Host.App.Set("agreementsAccepted", true);
        AgreementSource = null;
        Raise();
        if (_lastRequest is { } request)
        {
            WingetOperations.Shared.Clear();
            WingetOperations.Shared.Start(request);
        }
        else
        {
            _ = RefreshInstalledAsync();
        }
    }

    public bool Run(PkgAction action, PackageEntry? package)
    {
        if (IsBusy)
        {
            return false;
        }

        OperationRequest request;
        switch (action)
        {
            case PkgAction.Install when package is not null:
                request = new(action, package.Name, package.Id, [WingetOperations.InstallArgs(package.Id, package.Source.Length > 0 ? package.Source : SourceName)]);
                break;
            case PkgAction.Uninstall when package is not null:
                request = new(action, package.Name, package.Id, [WingetOperations.UninstallArgs(package.Id, package.Source.Length > 0 ? package.Source : null)]);
                break;
            case PkgAction.Upgrade when package is not null:
                request = new(action, package.Name, package.Id, [WingetOperations.UpgradeArgs(package.Id, package.Source.Length > 0 ? package.Source : null)]);
                break;
            case PkgAction.UpgradeAll:
                request = new(action, Loc.T("packages"), null, [WingetOperations.UpgradeAllArgs(false)]);
                break;
            case PkgAction.UpdateSources:
                request = new(action, Loc.T("winget"), null, [WingetOperations.SourceUpdateArgs()]);
                break;
            default:
                return false;
        }

        _lastRequest = request;
        Error = null;
        return WingetOperations.Shared.Start(request);
    }

    private void OnOperationFinished(OperationRequest request, OpState state)
    {
        if (state == OpState.Succeeded && request.Action == PkgAction.Uninstall && Selected is { } selected
            && selected.Id.Equals(request.TargetId, StringComparison.OrdinalIgnoreCase) && Query.Trim().Length == 0)
        {
            ClearSelectionQuiet();
        }

        var status = WingetOperations.Shared.Status;
        if (status?.PendingAgreementSource is { } source)
        {
            AgreementSource = source;
        }
        else if (state == OpState.Failed && status?.Error is { Length: > 0 } error)
        {
            Error = error;
        }

        if (HasLoaded)
        {
            _ = RefreshInstalledAsync(clearError: false).ContinueWith(_ =>
            {
                if (Selected is { } s)
                {
                    V.Post(() => _ = SelectAsync(s));
                }
            }, TaskScheduler.Default);
        }
    }
}
