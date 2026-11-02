// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinKnife.Core;

namespace WinKnife.Features.Agents;

public enum HookKind
{
    Permission,
    Input,
    Other,
}

/// <summary>What a Claude Code hook reported: a session needs the person.</summary>
public sealed record HookEvent(string SessionId, DateTime At, string? Tool, string Message, HookKind Kind, string? Cwd);

/// <summary>The outcome of installing or removing the hook, and what to tell the person.</summary>
public sealed record HookResult(bool Ok, string Message, string? BackupPath = null);

/// <summary>
/// The optional, opt-in Claude Code hook that makes "Claude needs your permission" instant and exact. WinKnife adds two entries
/// (<c>Notification</c> and <c>PermissionRequest</c>) to the person's <c>settings.json</c>; each runs a tiny PowerShell script that
/// writes one small JSON file per event into WinKnife's own folder. The script prints nothing and always exits 0, so it never
/// changes what Claude Code decides. Everything else in <c>settings.json</c> is left as it was, and a backup is made first.
/// </summary>
public static class AgentHooks
{
    public const string ScriptName = "agent-hook.ps1";
    public static readonly string[] Events = ["Notification", "PermissionRequest"];

    /// <summary>Where the script drops events: <c>%LOCALAPPDATA%\WinKnife\agent-events</c>.</summary>
    public static string EventsFolder => Path.Combine(AgentLogRoot.LocalRoot, "agent-events");

    public static string ScriptPath => Path.Combine(AgentLogRoot.LocalRoot, ScriptName);

    public static string BackupFolder => Path.Combine(AgentLogRoot.LocalRoot, "agent-backups");

    /// <summary>The command Claude Code runs. Forward slashes and no shell syntax, so it reads the same in bash, PowerShell and cmd.</summary>
    public static string Command => "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ScriptPath.Replace('\\', '/') + "\"";

    public const string Script = """
        # WinKnife: records that a Claude Code session needs you (Notification / PermissionRequest hooks).
        # Writes one small JSON file per event to %LOCALAPPDATA%\WinKnife\agent-events. Prints nothing; always exits 0, so it can
        # never change what Claude Code decides. Only the session id, folder, event, tool name and a short hint are kept.
        try {
            [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
            $raw = [Console]::In.ReadToEnd()
            if ($raw) {
                $p = $raw | ConvertFrom-Json
                $hint = $null
                if ($p.tool_input) {
                    if ($p.tool_input.file_path) { $hint = Split-Path -Leaf $p.tool_input.file_path }
                    elseif ($p.tool_input.command) { $hint = ($p.tool_input.command -split '\s+')[0] }
                }
                $o = [ordered]@{
                    receivedAt = (Get-Date).ToUniversalTime().ToString('o')
                    session_id = $p.session_id
                    cwd = $p.cwd
                    event = $p.hook_event_name
                    message = $p.message
                    notification_type = $p.notification_type
                    tool_name = $p.tool_name
                    tool_hint = $hint
                }
                $base = if ($env:WINKNIFE_LOCALDATA) { $env:WINKNIFE_LOCALDATA } else { Join-Path $env:LOCALAPPDATA 'WinKnife' }
                $dir = Join-Path $base 'agent-events'
                New-Item -ItemType Directory -Force -Path $dir | Out-Null
                $file = Join-Path $dir ('{0}-{1}.json' -f [DateTime]::UtcNow.Ticks, [Guid]::NewGuid().ToString('N'))
                [IO.File]::WriteAllText($file, ($o | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))
            }
        } catch { }
        exit 0
        """;

    // ------------------------------------------------------------------------------------------ reading events

    private static readonly Dictionary<string, HookEvent?> Seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest event of each session from the last quarter hour; older files are deleted.</summary>
    public static Dictionary<string, HookEvent> ReadEvents(string folder, DateTime now)
    {
        var result = new Dictionary<string, HookEvent>(StringComparer.OrdinalIgnoreCase);
        if (folder.Length == 0)
        {
            return result;
        }

        try
        {
            if (!Directory.Exists(folder))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                if (!Seen.TryGetValue(file, out var parsed))
                {
                    parsed = Parse(file, now);
                    Seen[file] = parsed;
                }

                if (parsed is null || now - parsed.At > TimeSpan.FromMinutes(15))
                {
                    if (parsed is null || now - parsed.At > TimeSpan.FromHours(1))
                    {
                        TryDelete(file);
                        Seen.Remove(file);
                    }

                    continue;
                }

                if (parsed.Kind != HookKind.Other && (!result.TryGetValue(parsed.SessionId, out var existing) || existing.At <= parsed.At))
                {
                    result[parsed.SessionId] = parsed;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return result;
    }

    private static HookEvent? Parse(string file, DateTime now)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1 << 16)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.Str("session_id") is not { Length: > 0 } id)
            {
                return null;
            }

            var at = root.Prop("receivedAt").Timestamp() ?? File.GetLastWriteTimeUtc(file);
            var message = root.Str("message") ?? "";
            var type = root.Str("notification_type") ?? "";
            var ev = root.Str("event") ?? "";
            var kind = ev == "PermissionRequest" || type == "permission_prompt" || message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                ? HookKind.Permission
                : type == "idle_prompt" || message.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase) ? HookKind.Input : HookKind.Other;
            var tool = root.Str("tool_name") is { Length: > 0 } t ? (root.Str("tool_hint") is { Length: > 0 } hint ? t + " " + hint : t) : null;
            return new HookEvent(id, at, tool, message, kind, root.Str("cwd"));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------------------------------ settings.json

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string SettingsPath(string home) => Path.Combine(new ClaudeAccountProfile().ConfigurationFolder(home), "settings.json");

    /// <summary>Whether the hook entries are present (both of them), and anything that stops them being managed.</summary>
    public static (bool Installed, string? Problem) Status(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return (false, null);
            }

            if (Load(settingsPath) is not { } root)
            {
                return (false, Loc.T("settings.json has comments or is not plain JSON, so WinKnife will not edit it. Add the entries shown below by hand."));
            }

            return (Events.All(e => Ours(root, e).Count > 0), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, Loc.T("settings.json cannot be read."));
        }
    }

    /// <summary>The JSON that installing adds, exactly as it will appear (merged into <c>hooks</c>, existing entries kept).</summary>
    public static string Preview()
    {
        var hooks = new JsonObject();
        foreach (var e in Events)
        {
            hooks[e] = new JsonArray(Entry());
        }

        return new JsonObject { ["hooks"] = hooks }.ToJsonString(Write);
    }

    public static HookResult Install(string settingsPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            Directory.CreateDirectory(EventsFolder);
            JsonObject root;
            string? backup = null;
            if (File.Exists(settingsPath))
            {
                if (Load(settingsPath) is not { } loaded)
                {
                    return new HookResult(false, "settings.json has comments or is not plain JSON. Nothing was changed; add the entries by hand.");
                }

                root = loaded;
                backup = Backup(settingsPath);
            }
            else
            {
                root = [];
            }

            if (root["hooks"] is not null and not JsonObject)
            {
                return new HookResult(false, "The \"hooks\" entry of settings.json is not an object. Nothing was changed.");
            }

            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            foreach (var e in Events)
            {
                if (hooks[e] is not null and not JsonArray)
                {
                    return new HookResult(false, $"hooks.{e} in settings.json is not a list. Nothing was changed.", backup);
                }

                var list = hooks[e] as JsonArray ?? [];
                if (Ours(root, e).Count == 0)
                {
                    list.Add(Entry());
                }

                hooks[e] = list;
            }

            root["hooks"] = hooks;
            File.WriteAllText(ScriptPath, Script.Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(true));
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, root.ToJsonString(Write) + "\n", new System.Text.UTF8Encoding(false));
            return new HookResult(true, "Installed. Claude Code reads settings when a session starts, so restart running sessions to use it.", backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HookResult(false, "Could not update settings.json: " + e.Message);
        }
    }

    public static HookResult Remove(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath) || Load(settingsPath) is not { } root)
            {
                return new HookResult(File.Exists(settingsPath) is false, File.Exists(settingsPath) ? "settings.json has comments or is not plain JSON. Nothing was changed." : "Nothing to remove.");
            }

            var backup = Backup(settingsPath);
            if (root["hooks"] is JsonObject hooks)
            {
                foreach (var e in Events)
                {
                    if (hooks[e] is not JsonArray list)
                    {
                        continue;
                    }

                    foreach (var group in list.OfType<JsonObject>().ToList())
                    {
                        if (group["hooks"] is JsonArray inner)
                        {
                            foreach (var h in inner.OfType<JsonObject>().Where(IsOurs).ToList())
                            {
                                inner.Remove(h);
                            }

                            if (inner.Count == 0)
                            {
                                list.Remove(group);
                            }
                        }
                    }

                    if (list.Count == 0)
                    {
                        hooks.Remove(e);
                    }
                }

                if (hooks.Count == 0)
                {
                    root.Remove("hooks");
                }
            }

            File.WriteAllText(settingsPath, root.ToJsonString(Write) + "\n", new System.Text.UTF8Encoding(false));
            TryDelete(ScriptPath);
            return new HookResult(true, "Removed. Your other settings were left as they were.", backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HookResult(false, "Could not update settings.json: " + e.Message);
        }
    }

    private static JsonObject Entry() => new()
    {
        ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command }),
    };

    private static bool IsOurs(JsonObject h) => h["command"]?.GetValue<string>() is { } c && c.Contains(ScriptName, StringComparison.OrdinalIgnoreCase);

    private static List<JsonObject> Ours(JsonObject root, string ev)
    {
        var found = new List<JsonObject>();
        if (root["hooks"] is JsonObject hooks && hooks[ev] is JsonArray list)
        {
            foreach (var group in list.OfType<JsonObject>())
            {
                if (group["hooks"] is JsonArray inner)
                {
                    found.AddRange(inner.OfType<JsonObject>().Where(IsOurs));
                }
            }
        }

        return found;
    }

    /// <summary>Strict JSON only: a file with comments or trailing commas would lose them on rewrite, so it is left alone.</summary>
    private static JsonObject? Load(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), null, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Backup(string settingsPath)
    {
        Directory.CreateDirectory(BackupFolder);
        var target = Path.Combine(BackupFolder, "settings.json." + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        File.Copy(settingsPath, target, true);
        return target;
    }
}
