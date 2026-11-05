// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.RegularExpressions;

namespace Trayloom.Features.GitHub;

/// <summary>
/// Reads the repository hint out of the title of an editor or terminal window. Pure: no window, file or process is touched.
/// Titles are never logged; they can carry file names and paths.
/// </summary>
internal static class CodingTitles
{
    private static readonly string[] CodeSuffixes =
    [
        " - Visual Studio Code - Insiders",
        " - Visual Studio Code",
        " - Cursor",
        " - Windsurf",
        " - VSCodium",
    ];

    private static readonly string[] RemoteMarkers = ["[WSL:", "[SSH:", "[Dev Container", "[Codespaces", "[Tunnel"];

    private static readonly HashSet<string> CodeImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code.exe", "Code - Insiders.exe", "Cursor.exe", "Windsurf.exe", "VSCodium.exe",
    };

    private static readonly HashSet<string> JetBrainsImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "idea64.exe", "rider64.exe", "pycharm64.exe", "webstorm64.exe", "goland64.exe", "clion64.exe", "rustrover64.exe", "phpstorm64.exe", "datagrip64.exe",
    };

    private static readonly HashSet<string> TerminalImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "mintty.exe", "bash.exe",
        "wezterm-gui.exe", "alacritty.exe", "Tabby.exe", "Hyper.exe", "claude.exe",
    };

    private static readonly Regex DrivePath = new(@"(?<![A-Za-z])[A-Za-z]:\\[^<>""|?*\r\n]*", RegexOptions.Compiled);
    private static readonly Regex MingwPath = new(@"MINGW(?:32|64)?:/([a-zA-Z])/(.*)", RegexOptions.Compiled);
    private static readonly Regex UnixPath = new(@"^/([a-zA-Z])/(.*)", RegexOptions.Compiled);
    private static readonly Regex Groups = new(@"\s*\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Brackets = new(@"\s*\[[^\]]*\]", RegexOptions.Compiled);

    /// <summary>True for an executable that is an editor, an IDE or a terminal (the file name, any case).</summary>
    public static bool IsCodingApp(string image)
    {
        var name = Path.GetFileName(image);
        return CodeImages.Contains(name) || JetBrainsImages.Contains(name) || TerminalImages.Contains(name)
            || name.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The hint for a window of <paramref name="image"/> titled <paramref name="title"/>.</summary>
    public static TitleHint Parse(string image, string title)
    {
        var name = Path.GetFileName(image ?? "");
        title ??= "";
        if (CodeImages.Contains(name))
        {
            return ParseCode(title);
        }

        if (name.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVisualStudio(title);
        }

        if (JetBrainsImages.Contains(name))
        {
            return ParseJetBrains(title);
        }

        return TerminalImages.Contains(name) ? ParseTerminal(title) : TitleHint.Nothing;
    }

    private static TitleHint ParseCode(string title)
    {
        var text = title.StartsWith("● ", StringComparison.Ordinal) ? title[2..] : title;
        var cut = false;
        foreach (var suffix in CodeSuffixes)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                text = text[..^suffix.Length];
                cut = true;
                break;
            }
        }

        // Without the product suffix the title was customised or is not a project window, so nothing can be read from it.
        if (!cut || RemoteMarkers.Any(m => text.Contains(m, StringComparison.Ordinal)))
        {
            return TitleHint.Nothing;
        }

        var candidates = text.Split(" - ")
            .Reverse()
            .Select(s => s.Replace(" (Workspace)", "", StringComparison.Ordinal).Trim())
            .Where(s => s.Length > 0)
            .ToList();
        return candidates.Count == 0 ? TitleHint.Nothing : new TitleHint(TitleHintKind.Folder, candidates);
    }

    private static TitleHint ParseVisualStudio(string title)
    {
        const string suffix = " - Microsoft Visual Studio";
        var text = title.EndsWith(suffix, StringComparison.Ordinal) ? title[..^suffix.Length] : title;
        text = Groups.Replace(text, "").Trim();
        return text.Length == 0 ? TitleHint.Nothing : new TitleHint(TitleHintKind.Folder, [text]);
    }

    private static TitleHint ParseJetBrains(string title)
    {
        var first = title.Split(" – ")[0];

        // A project in a sub-folder is titled "name [path]"; the bracket is dropped because only the name is matched.
        first = Brackets.Replace(first, "").Trim();
        return first.Length == 0 ? TitleHint.Nothing : new TitleHint(TitleHintKind.Folder, [first]);
    }

    private static TitleHint ParseTerminal(string title)
    {
        if (DrivePath.Match(title) is { Success: true } drive)
        {
            var path = drive.Value.Trim().TrimEnd('>', ' ').TrimEnd();
            return path.Length >= 3 ? new TitleHint(TitleHintKind.Path, [path]) : TitleHint.Nothing;
        }

        var unix = MingwPath.Match(title);
        if (!unix.Success)
        {
            unix = UnixPath.Match(title);
        }

        if (unix.Success)
        {
            var rest = unix.Groups[2].Value.Replace('/', '\\').TrimEnd();
            return new TitleHint(TitleHintKind.Path, [char.ToUpperInvariant(unix.Groups[1].Value[0]) + @":\" + rest]);
        }

        return TitleHint.Nothing;
    }
}
