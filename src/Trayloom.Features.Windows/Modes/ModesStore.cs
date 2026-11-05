// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Reads an item kind leniently: a name this version does not know becomes <see cref="ModeItemKind.Unknown"/> instead of failing the whole file.</summary>
internal sealed class ModeItemKindConverter : JsonConverter<ModeItemKind>
{
    public override ModeItemKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return text?.ToLowerInvariant() switch
        {
            "app" => ModeItemKind.App,
            "web" => ModeItemKind.Web,
            "path" => ModeItemKind.Path,
            _ => ModeItemKind.Unknown,
        };
    }

    public override void Write(Utf8JsonWriter writer, ModeItemKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ModeItemKind.App => "app",
            ModeItemKind.Web => "web",
            _ => "path",
        });
    }
}

/// <summary>
/// <c>modes.json</c> (spec 08 section 2.2): a separate file because URL lists are large and personal and must not go through the
/// debounced flat settings store. Writes are atomic and immediate. A file that cannot be read is renamed aside, never overwritten, so
/// the person's modes are not silently dropped.
/// </summary>
internal sealed class ModesStore
{
    public const string FileName = "modes.json";

    /// <summary>The JSON options of the file: camel-case names, enum values as camel-case text.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new ModeItemKindConverter() },
    };

    private readonly string _directory;
    private readonly ILog _log;

    public ModesStore(string directory, ILog log)
    {
        _directory = directory;
        _log = log;
    }

    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>The saved modes, sanitised; empty when there is no file or it could not be read (then it is kept aside as <c>.bad-yyyyMMddHHmmss</c>).</summary>
    public ModesFile Load()
    {
        if (!File.Exists(FilePath))
        {
            return new ModesFile();
        }

        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var file = JsonSerializer.Deserialize<ModesFile>(stream, Json);
            if (file is null)
            {
                throw new JsonException("empty document");
            }

            return ModesMath.Sanitize(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            KeepAside();
            return new ModesFile();
        }
    }

    /// <summary>Writes the file through a temporary one, so a crash leaves either the old or the new content. False when it could not be written.</summary>
    public bool Save(ModesFile file)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Json), new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("[modes] modes.json could not be written", ex);
            return false;
        }
    }

    private void KeepAside()
    {
        try
        {
            File.Move(FilePath, FilePath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (Exception)
        {
            // the log line below still tells the person what happened
        }

        _log.Warn("[modes] modes.json unreadable: kept aside");
    }
}
