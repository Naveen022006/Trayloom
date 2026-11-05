// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core.Ui;
using Trayloom.Core;

namespace Trayloom.Features.Maintenance;

/// <summary>
/// The operation status card shared by the Packages page, App updates and the Uninstaller (spec 7.4.2): status disc, title,
/// phase and elapsed time, progress bar, last activity, terminal fallback, technical details.
/// </summary>
/// <summary>Frame drawn around the status.</summary>
internal enum OpChrome
{
    None,
    Divider,
    Card,
}

internal sealed class OperationStatusView : ContentControl
{
    private readonly bool _compact;
    private readonly OpChrome _chrome;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TextBlock? _sub;
    private bool _details;

    public OperationStatusView(bool compact = false, OpChrome chrome = OpChrome.None)
    {
        _compact = compact;
        _chrome = chrome;
        Rebuild();
        Loaded += (_, _) =>
        {
            WingetOperations.Shared.Changed += OnChanged;
            _timer.Start();
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            WingetOperations.Shared.Changed -= OnChanged;
            _timer.Stop();
        };
        _timer.Tick += (_, _) => UpdateSub();
    }

    private void OnChanged() => Rebuild();

    private void UpdateSub()
    {
        var status = WingetOperations.Shared.Status;
        if (status is null || _sub is null)
        {
            return;
        }

        _sub.Text = SubText(status);
    }

    private static string SubText(OperationStatus status)
    {
        var elapsed = Loc.F("{0} elapsed", Fmt.Elapsed((status.Finished ?? DateTime.Now) - status.Started));
        return status.IsRunning ? $"{status.PhaseText} | {elapsed}" : elapsed;
    }

    private void Rebuild()
    {
        var status = WingetOperations.Shared.Status;
        if (status is null)
        {
            Content = null;
            Visibility = Visibility.Collapsed;
            _sub = null;
            return;
        }

        Visibility = Visibility.Visible;
        var s = _compact ? 7.0 : 9.0;
        var column = new StackPanel();

        var (glyph, brush) = status.State switch
        {
            OpState.Succeeded => (G.CheckCircle, ThemeKeys.Green),
            OpState.Failed => (G.Warning, ThemeKeys.Orange),
            OpState.Cancelled => (G.CancelCircle, ThemeKeys.TextSecondary),
            OpState.NeedsTerminal => (G.Terminal, ThemeKeys.Orange),
            _ => (status.Action switch
            {
                PkgAction.Install => G.Download,
                PkgAction.Uninstall => G.Trash,
                PkgAction.UpdateSources => G.Refresh,
                _ => G.ArrowUpCircle,
            }, ThemeKeys.Accent),
        };

        var disc = V.Disc(glyph, brush, _compact ? 22 : 28, _compact ? 10 : 12);
        var title = UiKit.Text(status.Title, _compact ? 11.5 : 13, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true);
        title.MaxHeight = (_compact ? 11.5 : 13) * 2.8;
        _sub = UiKit.Text(SubText(status), _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        var texts = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        texts.Children.Add(title);
        _sub.Margin = new Thickness(0, 2, 0, 0);
        texts.Children.Add(_sub);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(disc);
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        var action = status.IsRunning
            ? PanelButtons.Mini(Loc.T("Cancel"), null, () => WingetOperations.Shared.Cancel())
            : PanelButtons.Mini(Loc.T("Clear log"), null, () => WingetOperations.Shared.Clear());
        action.VerticalAlignment = VerticalAlignment.Top;
        action.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(action, 2);
        head.Children.Add(action);
        column.Children.Add(head);

        if (status.IsRunning || status.State == OpState.Succeeded)
        {
            var bar = new ProgressStrip(status.State == OpState.Succeeded ? 1 : status.Fraction) { Margin = new Thickness(0, s, 0, 0) };
            column.Children.Add(bar);
            if (status.IsRunning && status.Fraction is null)
            {
                var caption = UiKit.Text(Loc.T("winget has not reported a percentage yet."), 10, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
                caption.Margin = new Thickness(0, 4, 0, 0);
                column.Children.Add(caption);
            }
        }

        if (!string.IsNullOrWhiteSpace(status.Activity) && status.IsRunning)
        {
            var activity = UiKit.Text(status.Activity!, _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
            activity.MaxHeight = (_compact ? 9.5 : 11) * 1.5 * (_compact ? 2 : 3);
            activity.Margin = new Thickness(0, s, 0, 0);
            column.Children.Add(activity);
        }

        if (status.State == OpState.Failed && !string.IsNullOrWhiteSpace(status.Error))
        {
            var error = V.Orange(status.Error!, _compact ? 9.5 : 11);
            error.Margin = new Thickness(0, s, 0, 0);
            column.Children.Add(error);
        }

        if (status.State == OpState.NeedsTerminal)
        {
            var note = V.Label(G.Terminal, Loc.T("This operation needs administrator approval. Trayloom does not capture passwords."), _compact ? 9.5 : 11, FontWeights.Normal, ThemeKeys.TextSecondary, ThemeKeys.TextSecondary);
            foreach (var child in note.Children.OfType<TextBlock>())
            {
                child.TextWrapping = TextWrapping.Wrap;
            }

            note.Margin = new Thickness(0, s, 0, 0);
            column.Children.Add(note);
            if (!string.IsNullOrEmpty(status.Command))
            {
                var buttons = V.Row(8, PanelButtons.Mini(Loc.T("Open terminal"), null, () => OpenTerminal(status.CommandArgs)), PanelButtons.Mini(Loc.T("Copy"), G.Copy, () => TryCopy(status.Command!)));
                buttons.Margin = new Thickness(0, 6, 0, 0);
                column.Children.Add(buttons);
            }
        }

        if (status.Log.Length > 0)
        {
            var toggle = UiKit.Button(_details ? Loc.T("Hide details") : Loc.T("Show details"), ButtonKind.Plain, 11, _details ? G.ChevronUp : G.ChevronDown, () =>
            {
                _details = !_details;
                Rebuild();
            });
            toggle.Padding = new Thickness(0, 2, 0, 2);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0, s - 2, 0, 0);
            column.Children.Add(toggle);
            if (_details)
            {
                var header = V.Split(UiKit.Text(Loc.T("Technical details"), 11, FontWeights.SemiBold, ThemeKeys.TextTertiary), PanelButtons.Mini(Loc.T("Copy"), G.Copy, () => TryCopy(status.Log.ToString())));
                header.Margin = new Thickness(0, 2, 0, 4);
                column.Children.Add(header);
                var log = new TextBox
                {
                    Text = status.Log.ToString(),
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = _compact ? 9 : 10,
                    Height = _compact ? 58 : 96,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6),
                };
                log.SetResourceReference(Control.BackgroundProperty, ThemeKeys.ControlFill);
                log.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
                log.ScrollToEnd();
                column.Children.Add(log);
            }
        }

        if (_chrome == OpChrome.Card)
        {
            Content = new PanelCard(column);
        }
        else if (_chrome == OpChrome.Divider)
        {
            column.Margin = new Thickness(12);
            var framed = new StackPanel();
            framed.Children.Add(UiKit.Divider());
            framed.Children.Add(column);
            Content = framed;
        }
        else
        {
            Content = column;
        }
    }

    private static void TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // clipboard busy
        }
    }

    /// <summary>
    /// Opens an administrator terminal running the winget command. The command is rebuilt from the argument arrays (every argument must be a
    /// plain flag or pass <see cref="WingetCli.ValidToken"/>) and handed over as <c>-EncodedCommand</c>, so no text can add a second command.
    /// </summary>
    internal static string? BuildTerminalScript(IReadOnlyList<string[]> commands, string? wingetPath = null)
    {
        if (commands.Count == 0)
        {
            return null;
        }

        var exe = string.IsNullOrEmpty(wingetPath) ? "winget" : wingetPath.Replace("'", "''");
        var lines = new List<string>();
        foreach (var command in commands)
        {
            if (command.Length == 0 || !command.All(a => WingetCli.ValidToken(a) || System.Text.RegularExpressions.Regex.IsMatch(a, @"^--?[A-Za-z][A-Za-z-]*$")))
            {
                return null;
            }

            lines.Add($"& '{exe}' " + string.Join(' ', command.Select(a => $"'{a}'")));
        }

        return string.Join("; ", lines);
    }

    private static void OpenTerminal(IReadOnlyList<string[]> commands)
    {
        try
        {
            var script = BuildTerminalScript(commands, WingetCli.Path);
            if (script is null)
            {
                return;
            }

            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var powershell = System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            Process.Start(new ProcessStartInfo(powershell, $"-NoExit -EncodedCommand {encoded}") { UseShellExecute = true, Verb = "runas" });
        }
        catch
        {
            // prompt declined
        }
    }
}
