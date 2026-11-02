// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WinKnife.Features.Maintenance;

/// <summary>One row of a winget table.</summary>
internal sealed record PackageEntry(string Name, string Id, string Version, string Available, string Source, bool Explicit = false)
{
    /// <summary>True for Microsoft Store products (12 character ids, source msstore).</summary>
    public bool IsStore => Source.Equals("msstore", StringComparison.OrdinalIgnoreCase) || (Id.Length == 12 && Id.All(char.IsLetterOrDigit) && Source.Length == 0 && Id.StartsWith('9'));

    /// <summary>True when winget cannot upgrade this row by id (ARP / MSIX entries without a manifest).</summary>
    public bool IsOther => Id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) || Id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase);

    public bool HasUpdate => Available.Length > 0 && !Available.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Details of one package (winget show).</summary>
internal sealed class PackageDetails
{
    public string Name { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Homepage { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string InstallerType { get; set; } = string.Empty;

    public bool IsTool => InstallerType.Equals("portable", StringComparison.OrdinalIgnoreCase) || InstallerType.Equals("zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Result of a one-shot winget call.</summary>
internal readonly record struct CliResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Ok => ExitCode == 0 && !TimedOut;
}

/// <summary>The Windows Package Manager command line: detection, one-shot reads and table parsing (spec 7.4.7).</summary>
internal static class WingetCli
{
    public const int NoApplicableUpdate = unchecked((int)0x8A15002B);
    public const int NoPackageFound = unchecked((int)0x8A150014);

    private static string? _path;
    private static bool _probed;
    private static readonly Regex Ansi = new(@"\u001B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._+@/-]*$", RegexOptions.Compiled);

    /// <summary>Full path of winget.exe or null when missing.</summary>
    public static string? Path
    {
        get
        {
            if (!_probed)
            {
                Probe();
            }

            return _path;
        }
    }

    public static bool IsAvailable => Path is not null;

    /// <summary>Forgets the detection result (Refresh button).</summary>
    public static void Reset()
    {
        _probed = false;
        _path = null;
    }

    /// <summary>True when winget exists in WindowsApps but that folder is not on the user PATH.</summary>
    public static bool NeedsPathSetup
    {
        get
        {
            var folder = WindowsAppsFolder;
            var exe = System.IO.Path.Combine(folder, "winget.exe");
            if (!File.Exists(exe))
            {
                return false;
            }

            var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
            return !path.Any(p => string.Equals(p.Trim().TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string WindowsAppsFolder => System.IO.Path.Combine(FileOps.LocalAppData, "Microsoft", "WindowsApps");

    private static void Probe()
    {
        _probed = true;
        try
        {
            var exe = System.IO.Path.Combine(WindowsAppsFolder, "winget.exe");
            if (File.Exists(exe))
            {
                _path = exe;
                return;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = System.IO.Path.Combine(dir.Trim(), "winget.exe");
                if (File.Exists(candidate))
                {
                    _path = candidate;
                    return;
                }
            }
        }
        catch
        {
            // treated as missing
        }
    }

    /// <summary>Package ids must satisfy the spec's token rule.</summary>
    public static bool ValidToken(string? token) =>
        !string.IsNullOrEmpty(token) && TokenRegex.IsMatch(token) && !token.Contains("..") && !token.Contains("//");

    public static ProcessStartInfo Start(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Path ?? "winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>Runs winget to completion with a timeout (read-only commands: 30 s).</summary>
    public static async Task<CliResult> RunAsync(IEnumerable<string> arguments, int timeoutMs = 30_000, CancellationToken ct = default)
    {
        if (Path is null)
        {
            return new CliResult(-1, string.Empty, false);
        }

        try
        {
            using var process = Process.Start(Start(arguments));
            if (process is null)
            {
                return new CliResult(-1, string.Empty, false);
            }

            process.StandardInput.Close();
            var output = new StringBuilder();
            var gate = new object();
            void Pump(StreamReader reader)
            {
                var buffer = new char[4096];
                int n;
                while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    lock (gate)
                    {
                        output.Append(buffer, 0, n);
                    }
                }
            }

            var readers = new[] { Task.Run(() => Pump(process.StandardOutput)), Task.Run(() => Pump(process.StandardError)) };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // already gone
                }

                return new CliResult(-1, output.ToString(), !ct.IsCancellationRequested);
            }

            await Task.WhenAll(readers).ConfigureAwait(false);
            return new CliResult(process.ExitCode, output.ToString(), false);
        }
        catch
        {
            return new CliResult(-1, string.Empty, false);
        }
    }

    /// <summary>Source agreements are accepted on the command line only after the user accepted them once (the trust card).</summary>
    public static string[] Common => Accepted ? ["--accept-source-agreements", "--disable-interactivity"] : ["--disable-interactivity"];

    public static bool Accepted => Host.IsAttached && Host.App.Get<bool>("agreementsAccepted");

    /// <summary>winget list (optionally one source).</summary>
    public static async Task<(CliResult Result, List<PackageEntry> Rows)> ListAsync(string? source = null, CancellationToken ct = default)
    {
        var args = new List<string> { "list" };
        if (source is not null)
        {
            args.AddRange(["--source", source]);
        }

        args.AddRange(Common);
        var result = await RunAsync(args, 60_000, ct).ConfigureAwait(false);
        return (result, ParseTable(result.Output, TableMode.List));
    }

    /// <summary>winget upgrade (optionally including unknown versions / one source).</summary>
    public static async Task<(CliResult Result, List<PackageEntry> Rows)> UpgradesAsync(bool includeUnknown = false, string? source = null, CancellationToken ct = default)
    {
        var args = new List<string> { "upgrade" };
        if (source is not null)
        {
            args.AddRange(["--source", source]);
        }

        if (includeUnknown)
        {
            args.Add("--include-unknown");
        }

        args.AddRange(Common);
        var result = await RunAsync(args, 90_000, ct).ConfigureAwait(false);
        return (result, ParseTable(result.Output, TableMode.Upgrade));
    }

    /// <summary>winget search.</summary>
    public static async Task<(CliResult Result, List<PackageEntry> Rows)> SearchAsync(string query, string? source, CancellationToken ct = default)
    {
        var args = new List<string> { "search", query, "--count", "50" };
        if (source is not null)
        {
            args.AddRange(["--source", source]);
        }

        args.AddRange(Common);
        var result = await RunAsync(args, 45_000, ct).ConfigureAwait(false);
        return (result, ParseTable(result.Output, TableMode.Search));
    }

    /// <summary>winget show for one id.</summary>
    public static async Task<PackageDetails?> ShowAsync(string id, string? source, CancellationToken ct = default)
    {
        if (!ValidToken(id))
        {
            return null;
        }

        var args = new List<string> { "show", "--id", id, "--exact" };
        if (!string.IsNullOrEmpty(source))
        {
            args.AddRange(["--source", source]);
        }

        args.AddRange(Common);
        var result = await RunAsync(args, 30_000, ct).ConfigureAwait(false);
        return result.Ok || result.Output.Length > 0 ? ParseDetails(result.Output, id) : null;
    }

    /// <summary>Strips ANSI escapes and keeps the text after the last carriage return of every line.</summary>
    public static IEnumerable<string> CleanLines(string output)
    {
        var text = Ansi.Replace(output, string.Empty).Replace("\r\n", "\n");
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Contains('\r') ? raw[(raw.LastIndexOf('\r') + 1)..] : raw;
            yield return line.TrimEnd();
        }
    }

    public enum TableMode
    {
        List,
        Upgrade,
        Search,
    }

    /// <summary>Terminal cells of a character (East Asian wide and emoji = 2).</summary>
    private static int Cells(string text, int index, out int length)
    {
        length = 1;
        var ch = text[index];
        if (char.IsHighSurrogate(ch) && index + 1 < text.Length)
        {
            length = 2;
            return 2;
        }

        var c = (int)ch;
        var wide = (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3)
            || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);
        return wide ? 2 : 1;
    }

    private static string Slice(string line, int startCell, int endCell)
    {
        var builder = new StringBuilder();
        var cell = 0;
        var i = 0;
        while (i < line.Length)
        {
            var width = Cells(line, i, out var length);
            if (cell >= endCell)
            {
                break;
            }

            if (cell >= startCell)
            {
                builder.Append(line, i, length);
            }

            cell += width;
            i += length;
        }

        return builder.ToString().Trim();
    }

    private static int CellWidth(string line)
    {
        var cell = 0;
        var i = 0;
        while (i < line.Length)
        {
            cell += Cells(line, i, out var length);
            i += length;
        }

        return cell;
    }

    /// <summary>Parses the fixed-width table(s) printed by list / upgrade / search.</summary>
    public static List<PackageEntry> ParseTable(string output, TableMode mode)
    {
        var lines = CleanLines(output).ToList();
        var rows = new List<PackageEntry>();
        var explicitTable = false;
        for (var s = 0; s < lines.Count; s++)
        {
            if (!Regex.IsMatch(lines[s].Trim(), "^-{5,}$") || s < 1)
            {
                continue;
            }

            var header = lines[s - 1];
            var starts = new List<int>();
            foreach (Match m in Regex.Matches(header, @"\S+(?: \S+)*"))
            {
                starts.Add(CellWidth(header[..m.Index]));
            }

            if (starts.Count < 3)
            {
                continue;
            }

            for (var i = s + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || Regex.IsMatch(line.Trim(), @"^\d+\s+\S+") && !Regex.IsMatch(line, @"\S\s{2,}\S"))
                {
                    break;
                }

                if (Regex.IsMatch(line.Trim(), @"^\d+\s+(upgrades?|packages?)\b", RegexOptions.IgnoreCase))
                {
                    break;
                }

                string Cell(int column)
                {
                    if (column >= starts.Count)
                    {
                        return string.Empty;
                    }

                    var end = column + 1 < starts.Count ? starts[column + 1] : int.MaxValue;
                    return Slice(line, starts[column], end);
                }

                var id = Cell(1);
                if (id.Length == 0)
                {
                    continue;
                }

                var version = Cell(2).TrimStart('<', '>').Trim();
                string available;
                string source;
                if (starts.Count >= 5)
                {
                    available = mode == TableMode.Search ? string.Empty : Cell(3);
                    source = Cell(4);
                }
                else
                {
                    available = string.Empty;
                    source = Cell(3);
                }

                rows.Add(new PackageEntry(Cell(0), id, version, available, source, explicitTable));
            }

            explicitTable = true;
        }

        return rows;
    }

    private static readonly Dictionary<string, string> KeyAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Version"] = "version", ["Publisher"] = "publisher", ["Description"] = "description", ["Homepage"] = "homepage",
        ["License"] = "license", ["Installer Type"] = "installerType", ["Type"] = "installerType",
    };

    /// <summary>Parses the "Key: value" listing of winget show.</summary>
    public static PackageDetails ParseDetails(string output, string id)
    {
        var details = new PackageDetails { Id = id };
        var inInstaller = false;
        string? lastKey = null;
        foreach (var line in CleanLines(output))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var found = Regex.Match(line, @"^\S.*?\s\[(?<id>[^\]]+)\]\s*$");
            if (found.Success && details.Name.Length == 0)
            {
                var prefix = line[..line.LastIndexOf('[')].Trim();
                var space = prefix.IndexOf(' ');
                details.Name = space > 0 && prefix.Split(' ').Length > 1 && !prefix.Contains(':') ? prefix[(space + 1)..].Trim() : prefix;
                continue;
            }

            if (char.IsWhiteSpace(line[0]) && lastKey == "description")
            {
                details.Description += (details.Description.Length > 0 ? " " : string.Empty) + line.Trim();
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0]) && !inInstaller)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!char.IsWhiteSpace(line[0]) && !key.Equals("Installer", StringComparison.OrdinalIgnoreCase))
            {
                inInstaller = false;
            }

            if (key.Equals("Installer", StringComparison.OrdinalIgnoreCase) && value.Length == 0)
            {
                inInstaller = true;
                continue;
            }

            if (!KeyAlias.TryGetValue(key, out var field))
            {
                lastKey = null;
                continue;
            }

            lastKey = field;
            switch (field)
            {
                case "version" when details.Version.Length == 0 && !inInstaller:
                    details.Version = value;
                    break;
                case "publisher":
                    details.Publisher = value;
                    break;
                case "description":
                    details.Description = value;
                    break;
                case "homepage":
                    details.Homepage = value;
                    break;
                case "license":
                    details.License = value;
                    break;
                case "installerType" when details.InstallerType.Length == 0:
                    details.InstallerType = value;
                    break;
            }
        }

        if (details.Name.Length == 0)
        {
            details.Name = id;
        }

        return details;
    }

    /// <summary>True when the output asks for source agreements.</summary>
    public static bool NeedsSourceAgreement(string output) =>
        output.Contains("source requires that you view the following agreements", StringComparison.OrdinalIgnoreCase)
        || output.Contains("agree to all the source agreements", StringComparison.OrdinalIgnoreCase);

    /// <summary>Does the failure look like a missing elevation?</summary>
    public static bool NeedsAdministrator(string output)
    {
        var lower = output.ToLowerInvariant();
        return lower.Contains("administrator privileges") || lower.Contains("requires elevation") || lower.Contains("sudo:")
            || lower.Contains("a terminal is required") || lower.Contains("password is required") || lower.Contains("run as administrator");
    }

    /// <summary>Appends the user-PATH entry for the WindowsApps folder and broadcasts the change.</summary>
    public static bool AddToUserPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment", true);
            if (key is null)
            {
                return false;
            }

            var current = key.GetValue("Path", string.Empty, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
            var folder = WindowsAppsFolder;
            if (!current.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => string.Equals(p.Trim().TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                key.SetValue("Path", (current.TrimEnd(';') + ";" + folder).TrimStart(';'), Microsoft.Win32.RegistryValueKind.ExpandString);
            }

            Win32.SendMessageTimeoutW(0xFFFF, Win32.WM_SETTINGCHANGE, 0, "Environment", 2, 1000, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
