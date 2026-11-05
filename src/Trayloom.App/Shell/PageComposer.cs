// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>One "Search settings" result.</summary>
internal sealed record SearchEntry(string Title, string Glyph, string Destination, string? FeatureId, bool IsPage, bool Dimmed, string Page, int Rank, string? Description = null, bool CanInstall = false);

/// <summary>The settings search index and matcher (spec 02-settings 1.4).</summary>
internal static class SettingsSearch
{
    private static Dictionary<string, string[]> PageKeywords => _loc_PageKeywords.Value;

    private static readonly LocValue<Dictionary<string, string[]>> _loc_PageKeywords = new(() => new(StringComparer.Ordinal)
    {
        ["general"] = [Loc.T("Launch at login"), "Language", "Appearance", "System", "Light", "Dark", "Feedback", Loc.T("Send feedback"), Loc.T("Global shortcut")],
        ["tray"] = [Loc.T("Tray icon"), Loc.T("Show tray icon"), "Panel", "Tabs", Loc.T("Notification area"), Loc.T("Menu bar"), "Reorder"],
        ["features"] = ["Install", "Uninstall", "Permissions", "Bundle", "Essentials", Loc.T("Install all"), Loc.T("Uninstall all")],
        ["shortcuts"] = ["Shortcut", "Keyboard", "Hotkey", "Active", "Inactive", "Conflict"],
        ["advanced"] = ["Backup", "Export settings", "Import settings", "Reset", "Uninstall Trayloom", Loc.T("Clear all permissions")],
        ["about"] = ["Version", "Check for updates", "Update available", "Beta", Loc.T("Review introduction"), "GitHub", "Updates"],
        ["whats-new"] = ["Changelog", "Release notes", "Added", "Fixed", "Changed"],
        ["support"] = ["Star", "GitHub", "Donate", "Help"],
    });

    public static List<SearchEntry> Search(ShellServices s, string query)
    {
        var all = new List<SearchEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // (a) sidebar pages
        foreach (var section in SidebarModel.All)
        {
            foreach (var row in section.Rows.Where(r => r.FeatureId is null))
            {
                var keywords = PageKeywords.GetValueOrDefault(row.Id) ?? [];
                var rank = Rank(query, row.Title, keywords);
                if (rank >= 0 && s.Runtime.AnyAvailable(row.Gates))
                {
                    all.Add(new SearchEntry(row.Title, row.Glyph, row.Destination, null, true, false, SettingsPages.Split(row.Destination).Page, rank));
                }
            }
        }

        // (b) features
        foreach (var e in FeatureCatalog.All)
        {
            var rank = Rank(query, e.Title, [e.Description]);
            if (rank < 0 || !seen.Add("f:" + e.Id))
            {
                continue;
            }

            var installed = s.Runtime.IsAvailable(e.Id);
            if (!installed && !e.CanInstall)
            {
                continue;
            }

            all.Add(new SearchEntry(e.Title, e.Glyph, e.Destination, e.Id, false, !installed, installed ? e.Page : SettingsPages.Features, rank, e.Description, !installed && e.CanInstall));
        }

        // (c) contributed sections
        foreach (var section in s.Contributions.SettingsSections())
        {
            if (section.Title is not { } title)
            {
                continue;
            }

            var rank = Rank(query, title, section.Keywords);
            if (all.Any(x => x.Title == title && x.FeatureId == section.FeatureId))
            {
                continue;
            }

            if (rank >= 0 && seen.Add($"s:{section.PageId}#{section.Anchor}:{title}"))
            {
                all.Add(new SearchEntry(title, "", SettingsPages.Join(section.PageId, section.Anchor), section.FeatureId, false, false, section.PageId, rank));
            }
        }

        // Group by page: page row first (only when it matches), children indented.
        var groups = all.GroupBy(x => x.Page).OrderBy(g => g.Min(x => x.Rank)).ToList();
        var ordered = new List<SearchEntry>();
        foreach (var g in groups)
        {
            ordered.AddRange(g.OrderByDescending(x => x.IsPage).ThenBy(x => x.Rank).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase));
        }

        return ordered;
    }

    private static int Rank(string query, string title, IEnumerable<string> keywords)
    {
        var info = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        if (info.Compare(title, query, options) == 0)
        {
            return 0;
        }

        if (info.IndexOf(title, query, options) >= 0)
        {
            return 1;
        }

        return keywords.Any(k => info.IndexOf(k, query, options) >= 0) ? 2 : -1;
    }
}

/// <summary>Builds the detail content of a Settings page from app-owned parts and contributed sections.</summary>
internal static class PageComposer
{
    public static FrameworkElement Build(ShellServices s, Visit visit, FeaturesPageState hubState, SettingsRouter router, Action rebuild, out List<AnchorHost> anchors)
    {
        anchors = [];
        var stack = new StackPanel();
        var page = visit.Page;
        var rt = s.Runtime;
        var context = (string? anchor) => new SettingsContext(s.Host, page, anchor, rebuild);

        switch (page)
        {
            case SettingsPages.Features:
                return FeaturesPage.Build(s, hubState, router, rebuild);
            case SettingsPages.Shortcuts:
                return ShortcutsPage.Build(s, rebuild);
            case SettingsPages.Advanced:
                return AdvancedPage.Build(s);
            case SettingsPages.About:
                return AboutPage.Build(s);
            case SettingsPages.WhatsNew:
                return WhatsNewPage.Build();
            case SettingsPages.Support:
                return SupportPage.Build();
        }

        var sections = s.Contributions.SettingsSections(page).ToList();
        var focusPage = page is SettingsPages.General or SettingsPages.Energy;

        if (page == SettingsPages.General)
        {
            if (visit.Anchor == SettingsPages.PanelConfigurationAnchor)
            {
                var host = new AnchorHost(visit.Anchor, PanelLayoutEditor.Build(s, rebuild));
                anchors.Add(host);
                stack.AddSpaced(host, 20);
                return stack;
            }

            if (visit.Anchor is null)
            {
                stack.AddSpaced(new SettingsPageHeader(Loc.T("General")), 20);
                stack.AddSpaced(GeneralPage.Build(s, rebuild), 20);
            }
        }
        else if (page == SettingsPages.Energy)
        {
            stack.AddSpaced(new SettingsPageHeader(Loc.T("Energy")), 20);
        }
        else
        {
            stack.AddSpaced(new SettingsPageHeader(SettingsPages.TitleOf(page), PageDescription(page)), 20);
        }

        IEnumerable<ISettingsSection> shown = sections;
        if (focusPage)
        {
            shown = visit.Anchor is null
                ? (page == SettingsPages.General ? sections.Where(x => x.Anchor is null) : sections.Take(1))
                : sections.Where(x => x.Anchor == visit.Anchor);
        }

        var count = 0;
        foreach (var section in shown)
        {
            FrameworkElement view;
            try
            {
                // RequestedAnchor is the anchor the router asked for (not each section's own anchor: that made the last tool section of
                // Screen capture always win the tool picker).
                view = section.CreateView(context(visit.Anchor));
            }
            catch (Exception ex)
            {
                s.Log.Error($"Settings section {section.GetType().Name} failed", ex);
                view = new SettingsCard().Add(new SettingsRow("", Loc.T("This section could not be shown"), ex.Message));
            }

            if (view.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var host = new AnchorHost(section.Anchor, view);
            anchors.Add(host);
            stack.AddSpaced(host, 20);
            count++;
        }

        if (count == 0 && !(page == SettingsPages.General && visit.Anchor is null))
        {
            var note = UiKit.Text(Loc.T("Nothing to configure here yet."), 13, FontWeights.Normal, ThemeKeys.TextSecondary);
            stack.AddSpaced(note, 20);
        }

        _ = rt;
        return stack;
    }

    private static string? PageDescription(string page) => page switch
    {
        SettingsPages.Monitor => Loc.T("Choose what the system monitor shows in the panel and on the taskbar."),
        _ => null,
    };
}
