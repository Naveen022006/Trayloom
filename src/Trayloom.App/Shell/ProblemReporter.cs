// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>
/// Collects start-up problems (a feature that failed to start, a DLL that did not load) and shows ONE clear dialog with an
/// "Open log" button. When Windows' Smart App Control is the cause, the dialog explains it in plain words and lists the user's
/// options; Trayloom never changes that protection itself.
/// </summary>
internal sealed class ProblemReporter
{
    private readonly ILog _log;
    private readonly List<StartupProblem> _pending = [];
    private readonly DispatcherTimer _timer;
    private Window? _open;

    public ProblemReporter(ILog log)
    {
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(1200) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Flush();
        };
    }

    /// <summary>Queues a problem (any thread); the dialog appears once start-up has settled.</summary>
    public void Add(StartupProblem problem)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            if (_pending.Any(p => p.Subject == problem.Subject))
            {
                return;
            }

            _pending.Add(problem);
            _log.Warn($"Problem queued for the user: {problem.Subject}: {problem.Message}");
            _timer.Stop();
            _timer.Start();
        });
    }

    private void Flush()
    {
        if (_pending.Count == 0 || _open is not null)
        {
            if (_pending.Count > 0)
            {
                _timer.Start();
            }

            return;
        }

        var problems = _pending.ToList();
        _pending.Clear();
        var sac = SmartAppControl.GetState();
        var blocked = problems.Where(p => p.BlockedByPolicy || (sac == SmartAppControlState.Enforcing && ChainHas<FileLoadException>(p.Exception))).ToList();
        _open = Build(problems, blocked, sac);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private static bool ChainHas<T>(Exception? exception) where T : Exception
    {
        for (var depth = 0; exception is not null && depth < 8; depth++)
        {
            if (exception is T)
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    private static Window Build(List<StartupProblem> problems, List<StartupProblem> blocked, SmartAppControlState sac)
    {
        var isBlock = blocked.Count > 0;
        var window = UiParts.PlainWindow(isBlock ? Loc.T("Windows blocked part of Trayloom") : Loc.T("Trayloom could not start everything"), 520);
        window.Topmost = true;
        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Icon(Glyphs.Warning, 20, ThemeKeys.Orange));
        var title = UiKit.Text(isBlock ? Loc.T("Windows blocked part of Trayloom") : Loc.T("Some features could not start"), 16, FontWeights.Bold, ThemeKeys.TextPrimary);
        title.Margin = new Thickness(10, 0, 0, 0);
        head.Children.Add(title);
        body.Children.Add(head);

        if (isBlock)
        {
            var file = blocked.Select(p => SmartAppControl.BlockedFile(p.Exception)).FirstOrDefault(f => f is not null) ?? blocked[0].Subject;
            var text = UiParts.Paragraph(SmartAppControl.Explain(file), 12.5, ThemeKeys.TextSecondary);
            text.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(text);
            var state = UiKit.Text(SmartAppControl.Describe(sac), 11.5, FontWeights.Normal, ThemeKeys.TextTertiary);
            state.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(state);
        }
        else
        {
            var text = UiParts.Paragraph(Loc.T("The rest of Trayloom keeps working. The log file has the technical details; send it along if you report the problem."), 12.5, ThemeKeys.TextSecondary);
            text.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(text);
        }

        var list = new SettingsCard();
        foreach (var problem in problems.Take(6))
        {
            list.Add(new SettingsRow(null, problem.Subject, Shorten(problem.Message)));
        }

        if (problems.Count > 6)
        {
            list.AddCaption(Loc.F("…and {0} more. See the log.", problems.Count - 6));
        }

        var listHost = new Border { Margin = new Thickness(0, 14, 0, 0), Child = list };
        body.Children.Add(listHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var open = UiKit.Button(Loc.T("Open log"), ButtonKind.Bordered, 13, onClick: UiParts.OpenLogFolder);
        open.Padding = new Thickness(14, 6, 14, 6);
        buttons.Children.Add(open);
        if (isBlock)
        {
            var security = UiKit.Button(Loc.T("Open Windows Security"), ButtonKind.Bordered, 13, onClick: () => UiKit.OpenUrl("windowsdefender://smartscreenpua"));
            security.Padding = new Thickness(14, 6, 14, 6);
            security.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(security);
        }

        var ok = UiKit.Button(Loc.T("OK"), ButtonKind.Prominent, 13, onClick: window.Close);
        ok.Padding = new Thickness(22, 6, 22, 6);
        ok.Margin = new Thickness(8, 0, 0, 0);
        ok.IsDefault = true;
        ok.IsCancel = true;
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        var background = new Border { Child = body };
        background.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsWindow);
        window.Content = background;
        return window;
    }

    private static string Shorten(string message)
    {
        var line = message.ReplaceLineEndings(" ").Trim();
        return line.Length > 170 ? line[..170] + "…" : line;
    }
}
