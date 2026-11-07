// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>A question the automatic starts put to the person: "Start {mode}?". It lives for two minutes.</summary>
/// <param name="RuleId">The rule that asked.</param>
/// <param name="ModeId">The mode it would start.</param>
/// <param name="Text">The question as the island shows it.</param>
/// <param name="Expires">Local time after which it is forgotten.</param>
internal sealed record ModeSuggestion(string RuleId, string ModeId, string Text, DateTime Expires);

/// <summary>
/// The cross-project face of the Modes runtime (<see cref="IModesService"/>) and the suggestion the automatic starts use. Enter, Leave,
/// IsSwitching and Changed are the runtime's own public members, which satisfy the interface as they are.
/// </summary>
internal sealed partial class ModesRuntime : IModesService
{
    /// <summary>How long a suggestion waits for an answer.</summary>
    public static readonly TimeSpan SuggestionLife = TimeSpan.FromMinutes(2);

    private ModeSuggestion? _suggestion;

    IReadOnlyList<ModeSummary> IModesService.List => [.. _file.Modes.Select(m => new ModeSummary(m.Id, m.Name, m.Glyph, HexOf(m.Color)))];

    string? IModesService.ActiveId => _activeId;

    /// <summary>Raised on the UI thread with the rule and mode ids just before a suggestion the person accepted starts its mode.</summary>
    public event Action<string, string>? SuggestionAccepted;

    private static string HexOf(string? colorName)
    {
        var color = ModesLook.ColorOf(colorName);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>The open question, or null when there is none, it expired or a mode is active already.</summary>
    public ModeSuggestion? PendingSuggestion
    {
        get
        {
            if (_suggestion is not { } suggestion)
            {
                return null;
            }

            if (DateTime.Now >= suggestion.Expires || _activeId is not null)
            {
                _suggestion = null;
                return null;
            }

            return suggestion;
        }
    }

    /// <summary>
    /// Asks "Start {mode}?" on the island when it is presenting, and with a toast when it is not. The island card keeps the question
    /// for two minutes, so a toast the person misses can still be answered from the Modes page.
    /// </summary>
    public void Suggest(string ruleId, string modeId, string islandText, string toastText)
    {
        if (_stopped)
        {
            return;
        }

        _suggestion = new ModeSuggestion(ruleId, modeId, islandText, DateTime.Now + SuggestionLife);
        var island = IslandContext;
        var presenting = _context.Runtime.IsRunning("notch") && _context.App.Get<bool>("notchEnabled") && island is not null;
        if (presenting)
        {
            _notice = islandText;
            island!.Invalidate();
        }
        else
        {
            _context.Notifier.Show(ModesStrings.Title, toastText);
        }

        Changed?.Invoke();
    }

    /// <summary>Forgets the open question ("Not now").</summary>
    public void DismissSuggestion()
    {
        if (_suggestion is null)
        {
            return;
        }

        _suggestion = null;
        Changed?.Invoke();
    }

    /// <summary>Answers "Start {mode}?" with yes: the rule is told first, so it can claim the mode as its own, then the mode starts.</summary>
    public bool AcceptSuggestion()
    {
        if (PendingSuggestion is not { } suggestion)
        {
            return false;
        }

        _suggestion = null;
        SuggestionAccepted?.Invoke(suggestion.RuleId, suggestion.ModeId);
        var started = Enter(suggestion.ModeId, "auto");
        Changed?.Invoke();
        return started;
    }
}
