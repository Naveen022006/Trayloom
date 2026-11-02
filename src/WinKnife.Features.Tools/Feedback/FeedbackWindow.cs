// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Strings of the feedback window (Vorssaint <c>FeedbackStrings</c>, enUS; GitHub wording where a server was involved).</summary>
internal static class FeedbackStrings
{
    public static  string SectionTitle => Loc.T("Feedback");
    public static  string SectionCaption => Loc.T("Send a bug report or feature idea directly to the person who maintains WinKnife.");
    public static  string OpenButton => Loc.T("Send feedback");
    public static  string WindowTitle => Loc.T("Send feedback");
    public static  string BugTitle => Loc.T("Bug");
    public static  string FeatureTitle => Loc.T("Feature idea");
    public static  string MessageLabel => Loc.T("What would you like to share?");
    public static  string BugPlaceholder => Loc.T("Tell me what happened and what you expected.");
    public static  string FeaturePlaceholder => Loc.T("Describe the idea and how it would help.");
    public static  string IncludeDiagnostics => Loc.T("Include technical details");
    public static  string IncludeDiagnosticsCaption => Loc.T("Adds only the technical details shown below. It does not include logs.");
    public static  string WhatSentTitle => Loc.T("What will be sent");
    public static  string WhatSentBasic => Loc.T("Your chosen category and the text above.");
    public static  string WhatSentDiagnostics => Loc.T("The technical details listed below.");
    public static  string PrivacyNote => Loc.T("No name, account, email, device identifier, logs, screenshots, files or clipboard content are included. The text goes to GitHub only when you submit the issue draft in your browser.");
    public static  string RetentionNote => Loc.T("Nothing is sent by WinKnife itself. Issues on GitHub are public, so do not include private information.");
    public static  string SendButton => Loc.T("Send feedback");
    public static  string SentTitle => Loc.T("Feedback sent");
    public static  string SentCaption => Loc.T("Your browser opened a new GitHub issue draft. Submit it there to send your feedback.");
    public static  string GenericError => Loc.T("Could not send the feedback right now.");
    public static  string Done => Loc.T("Done");
    public static  string CommandBug => Loc.T("Report a bug");
    public static  string CommandFeature => Loc.T("Suggest a feature");
    public static  string CommandSubtitle => Loc.T("Send feedback");

    public static string Characters(int count) => Loc.F("{0} of 2000 characters", count);
}

/// <summary>Builds the GitHub issue URL and the technical details of a feedback message.</summary>
internal static class FeedbackIssue
{
    public const string Repository = "https://github.com/winknife/winknife";
    public const int MaxMessage = 2000;
    private const int MaxUrl = 7900;

    /// <summary>Technical detail rows (label, value) listed in the "What will be sent" card.</summary>
    public static IReadOnlyList<(string Label, string Value)> Diagnostics()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(FeedbackIssue).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0";
        var plus = info.IndexOf('+');
        var version = plus > 0 ? info[..plus] : info;
        var build = assembly.GetName().Version?.Revision.ToString(CultureInfo.InvariantCulture) ?? "0";
        var beta = version.Contains("-beta", StringComparison.OrdinalIgnoreCase) || version.Contains("-rc", StringComparison.OrdinalIgnoreCase) || version.Contains("-alpha", StringComparison.OrdinalIgnoreCase);
        var rows = new List<(string, string)>
        {
            ("WinKnife", $"{version} ({build})"),
            (Loc.T("Windows"), Environment.OSVersion.Version.ToString()),
        };
        if (Model() is { Length: > 0 } model)
        {
            rows.Add(("PC", model));
        }

        rows.Add((Loc.T("Language"), CultureInfo.CurrentUICulture.Name));
        rows.Add((Loc.T("Beta"), beta ? "✓" : "○"));
        rows.Add((Loc.T("Update channel"), beta ? Loc.T("Beta") : "Stable"));
        return rows;
    }

    private static string? Model()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return key?.GetValue("SystemProductName") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The issue URL (below 8000 characters; the message is truncated with a note when needed).</summary>
    public static string BuildUrl(string kind, string message, bool diagnostics)
    {
        var trimmed = message.Trim();
        var label = kind == "feature" ? "enhancement" : "bug";
        var prefix = kind == "feature" ? "Feature idea: " : "Bug: ";
        var firstLine = trimmed.Split('\n', '\r')[0].Trim();
        var title = prefix + (firstLine.Length > 70 ? firstLine[..70] + "…" : firstLine);
        var details = new StringBuilder();
        if (diagnostics)
        {
            details.Append("\n\n---\n**Technical details**\n");
            foreach (var (name, value) in Diagnostics())
            {
                details.Append("- ").Append(name).Append(": ").Append(value).Append('\n');
            }
        }

        var body = trimmed + details;
        string Url(string b) => $"{Repository}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(b)}&labels={label}";
        var url = Url(body);
        if (url.Length <= MaxUrl)
        {
            return url;
        }

        var note = "\n\n(The message was shortened to fit in a link.)";
        var keep = trimmed.Length;
        while (keep > 20 && Url(trimmed[..keep] + note + details).Length > MaxUrl)
        {
            keep -= 100;
        }

        return Url(trimmed[..Math.Max(20, keep)] + note + details);
    }
}

/// <summary>The "Send feedback" window (600 x 650, fixed). Opens a pre-filled GitHub issue; nothing is sent to any server by WinKnife.</summary>
internal sealed class FeedbackWindow : Window
{
    private static FeedbackWindow? _instance;

    private readonly TextBox _editor = new();
    private readonly TextBlock _placeholder;
    private readonly TextBlock _counter;
    private readonly SegmentedControl _kindPicker;
    private readonly MacSwitch _diagnosticsSwitch;
    private readonly StackPanel _sentCard = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _error;
    private readonly Button _send;
    private readonly Grid _formRoot = new();
    private readonly Grid _content = new();
    private readonly FrameworkElement _form;
    private readonly FrameworkElement _sent;
    private string _kind = "bug";

    /// <summary>Opens (or re-uses) the window with the given kind: "bug" or "feature".</summary>
    public static void Open(IContributionServices ctx, string kind)
    {
        ctx.Shell.ClosePanel();
        if (_instance is { IsLoaded: true })
        {
            _instance.Reset(kind);
            _instance.Show();
            _instance.Activate();
            return;
        }

        _instance = new FeedbackWindow(ctx);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Reset(kind);
        _instance.Show();
        _instance.Activate();
    }

    private FeedbackWindow(IContributionServices ctx)
    {
        Title = FeedbackStrings.WindowTitle;
        Width = 600;
        Height = 650;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(this);
        Topmost = false;

        _kindPicker = new SegmentedControl([FeedbackStrings.BugTitle, FeedbackStrings.FeatureTitle], 0, stretch: true);
        _kindPicker.SelectionChanged += i =>
        {
            _kind = i == 0 ? "bug" : "feature";
            _placeholder!.Text = i == 0 ? FeedbackStrings.BugPlaceholder : FeedbackStrings.FeaturePlaceholder;
        };
        _placeholder = UiKit.Text(FeedbackStrings.BugPlaceholder, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        _counter = UiKit.Text(FeedbackStrings.Characters(0), 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        _counter.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _counter.HorizontalAlignment = HorizontalAlignment.Right;
        _diagnosticsSwitch = new MacSwitch(false, SwitchSize.Regular, _ => RebuildDetails());
        _error = UiKit.Text(string.Empty, 11, FontWeights.Normal, ThemeKeys.Red, wrap: true);
        _error.VerticalAlignment = VerticalAlignment.Center;
        _send = UiKit.Button(FeedbackStrings.SendButton, ButtonKind.Prominent, 14, onClick: () => Send(ctx));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildHeader());
        var divider = UiKit.Divider();
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);
        _form = BuildForm();
        _sent = BuildSent();
        _content.Children.Add(_form);
        _content.Children.Add(_sent);
        Grid.SetRow(_content, 2);
        root.Children.Add(_content);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.Control && _send.IsEnabled && _form.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                Send(ctx);
            }
        };
    }

    private UIElement BuildHeader()
    {
        var row = new Grid { Margin = new Thickness(24, 18, 24, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon("", 22, ThemeKeys.Accent);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);
        var title = UiKit.Text(FeedbackStrings.WindowTitle, 20, FontWeights.SemiBold);
        title.Margin = new Thickness(12, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        var done = UiKit.Button(FeedbackStrings.Done, ButtonKind.Bordered, 13, onClick: Close);
        Grid.SetColumn(done, 2);
        row.Children.Add(done);
        return row;
    }

    private FrameworkElement BuildForm()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(_kindPicker);
        var headline = UiKit.Text(FeedbackStrings.MessageLabel, 13, FontWeights.SemiBold);
        headline.Margin = new Thickness(0, 18, 0, 8);
        stack.Children.Add(headline);

        _editor.AcceptsReturn = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.MinHeight = 145;
        _editor.MaxHeight = 220;
        _editor.MaxLength = FeedbackIssue.MaxMessage;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.Padding = new Thickness(6);
        _editor.FontSize = 13;
        _editor.BorderThickness = new Thickness(0);
        _editor.Background = Brushes.Transparent;
        _editor.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        _editor.Template = PlainTextTemplate();
        _editor.TextChanged += (_, _) =>
        {
            _error.Text = string.Empty;
            _placeholder.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var n = _editor.Text.Length;
            _counter.Text = FeedbackStrings.Characters(n);
            _counter.SetResourceReference(TextBlock.ForegroundProperty, n > FeedbackIssue.MaxMessage ? ThemeKeys.Red : ThemeKeys.TextSecondary);
            _send.IsEnabled = CanSend;
        };
        var box = new Grid();
        box.Children.Add(_editor);
        _placeholder.Margin = new Thickness(11, 6, 0, 0);
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Top;
        box.Children.Add(_placeholder);
        var frame = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = box };
        frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        stack.Children.Add(frame);
        _counter.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_counter);

        var diagnostics = new SettingsRow(null, FeedbackStrings.IncludeDiagnostics, FeedbackStrings.IncludeDiagnosticsCaption, _diagnosticsSwitch);
        diagnostics.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(diagnostics);

        _details.Margin = new Thickness(0);
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 18, 0, 0), Child = _details };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.CardFill);
        stack.Children.Add(card);
        RebuildDetails();

        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grid.Children.Add(scroll);

        var bar = new StackPanel();
        bar.Children.Add(UiKit.Divider());
        var barRow = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _error.Margin = new Thickness(0, 0, 12, 0);
        barRow.Children.Add(_error);
        _send.Padding = new Thickness(18, 8, 18, 8);
        _send.IsEnabled = false;
        Grid.SetColumn(_send, 1);
        barRow.Children.Add(_send);
        bar.Children.Add(barRow);
        Grid.SetRow(bar, 1);
        grid.Children.Add(bar);
        return grid;
    }

    private static ControlTemplate PlainTextTemplate()
    {
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(UIElement.FocusableProperty, false);
        template.VisualTree = host;
        return template;
    }

    private FrameworkElement BuildSent()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40) };
        stack.Children.Add(UiKit.Icon("", 54, ThemeKeys.Green));
        var title = UiKit.Text(FeedbackStrings.SentTitle, 20, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(title);
        var caption = UiKit.Text(FeedbackStrings.SentCaption, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.TextAlignment = TextAlignment.Center;
        caption.MaxWidth = 400;
        caption.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(caption);
        var done = UiKit.Button(FeedbackStrings.Done, ButtonKind.Prominent, 14, onClick: Close);
        done.Margin = new Thickness(0, 22, 0, 0);
        done.Padding = new Thickness(22, 8, 22, 8);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(done);
        stack.Visibility = Visibility.Collapsed;
        return stack;
    }

    private void RebuildDetails()
    {
        _details.Children.Clear();
        void Line(string glyph, string text, bool header = false)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, _details.Children.Count == 0 ? 0 : 12, 0, 0) };
            row.Children.Add(UiKit.Icon(glyph, 13, header ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary));
            var label = UiKit.Text(text, header ? 13 : 12, header ? FontWeights.SemiBold : FontWeights.Normal, header ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary, wrap: true);
            label.Margin = new Thickness(10, 0, 0, 0);
            label.MaxWidth = 470;
            row.Children.Add(label);
            _details.Children.Add(row);
        }

        Line("", FeedbackStrings.WhatSentTitle, header: true);
        Line("", FeedbackStrings.WhatSentBasic);
        if (_diagnosticsSwitch.IsOn)
        {
            Line("", FeedbackStrings.WhatSentDiagnostics);
            var grid = new Grid { Margin = new Thickness(26, 8, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mono = new FontFamily("Cascadia Mono, Consolas");
            var rows = FeedbackIssue.Diagnostics();
            for (var i = 0; i < rows.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = UiKit.Text(rows[i].Label, 11, FontWeights.Normal, ThemeKeys.TextTertiary);
                l.FontFamily = mono;
                l.Margin = new Thickness(0, 0, 14, 5);
                Grid.SetRow(l, i);
                var v = UiKit.Text(rows[i].Value, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
                v.FontFamily = mono;
                v.Margin = new Thickness(0, 0, 0, 5);
                Grid.SetRow(v, i);
                Grid.SetColumn(v, 1);
                grid.Children.Add(l);
                grid.Children.Add(v);
            }

            _details.Children.Add(grid);
        }

        _details.Children.Add(UiKit.Divider());
        ((FrameworkElement)_details.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
        Line("", FeedbackStrings.PrivacyNote);
        Line("", FeedbackStrings.RetentionNote);
    }

    private bool CanSend
    {
        get
        {
            var n = _editor.Text.Trim().Length;
            return n >= 10 && n <= FeedbackIssue.MaxMessage;
        }
    }

    private void Reset(string kind)
    {
        _form.Visibility = Visibility.Visible;
        _sent.Visibility = Visibility.Collapsed;
        _kindPicker.SelectedIndex = kind == "feature" ? 1 : 0;
        _kind = kind == "feature" ? "feature" : "bug";
        _placeholder.Text = _kind == "bug" ? FeedbackStrings.BugPlaceholder : FeedbackStrings.FeaturePlaceholder;
        _editor.Text = string.Empty;
        _diagnosticsSwitch.IsOn = false;
        _error.Text = string.Empty;
        RebuildDetails();
        _editor.Focus();
    }

    private void Send(IContributionServices ctx)
    {
        if (!CanSend)
        {
            return;
        }

        var url = FeedbackIssue.BuildUrl(_kind, _editor.Text, _diagnosticsSwitch.IsOn);
        if (!Plat.Open(url))
        {
            _error.Text = FeedbackStrings.GenericError;
            ctx.Log.Warn("Feedback: the browser could not be opened");
            return;
        }

        _editor.Text = string.Empty;
        _form.Visibility = Visibility.Collapsed;
        _sent.Visibility = Visibility.Visible;
    }
}

/// <summary>General page card: "Feedback" with a "Send feedback" button.</summary>
public sealed class FeedbackSettingsSection : ISettingsSection
{
    public string PageId => SettingsPages.General;

    public string? Title => FeedbackStrings.SectionTitle;

    public IReadOnlyList<string> Keywords => ["bug", "feature idea", "report"];

    public int Order => 900;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var card = new SettingsCard();
        var button = UiKit.Button(FeedbackStrings.OpenButton, ButtonKind.Bordered, 13, onClick: () => FeedbackWindow.Open(context, "bug"));
        card.Add(new SettingsRow("", FeedbackStrings.SectionTitle, FeedbackStrings.SectionCaption, button));
        return card;
    }
}
