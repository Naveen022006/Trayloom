// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

internal enum MediaTool
{
    Video,
    Gif,
    Image,
    Text,
}

internal static class MediaToolIds
{
    public static string IdOf(MediaTool tool) => tool switch { MediaTool.Gif => "gifMaker", MediaTool.Image => "imageCompressor", MediaTool.Text => "textExtractor", _ => "videoCompressor" };

    public static MediaTool Parse(string? id) => id switch { "gifMaker" => MediaTool.Gif, "imageCompressor" => MediaTool.Image, "textExtractor" => MediaTool.Text, _ => MediaTool.Video };
}

/// <summary>Failure kinds of the media service with their user-facing text (spec 7.5.5).</summary>
internal enum MediaFailureKind
{
    NoInput,
    NoVideoTrack,
    SameOutput,
    Unsupported,
    ImageTooLarge,
    GifTooLong,
    TargetTooSmall,
    WatermarkUnavailable,
    Cancelled,
    Message,
}

internal sealed class MediaException : Exception
{
    public MediaException(MediaFailureKind kind, string? message = null, int maxSeconds = 0)
        : base(message ?? kind.ToString())
    {
        Kind = kind;
        MaxSeconds = maxSeconds;
    }

    public MediaFailureKind Kind { get; }

    public int MaxSeconds { get; }

    public string Text => Kind switch
    {
        MediaFailureKind.NoInput => Loc.T("Choose a file first."),
        MediaFailureKind.NoVideoTrack => Loc.T("This file has no video track."),
        MediaFailureKind.SameOutput => Loc.T("Choose a destination different from the original file."),
        MediaFailureKind.Unsupported => Loc.T("Format not supported by Windows."),
        MediaFailureKind.ImageTooLarge => Loc.T("These dimensions are too large to process safely. Choose a smaller size."),
        MediaFailureKind.GifTooLong => Loc.F("A GIF can be up to {0} seconds long", MaxSeconds),
        MediaFailureKind.TargetTooSmall => Loc.T("Target size too small for this clip. Trim it or raise the limit."),
        MediaFailureKind.WatermarkUnavailable => Loc.T("No logo"),
        MediaFailureKind.Cancelled => Loc.T("Cancelled."),
        _ => string.IsNullOrWhiteSpace(Message) ? Loc.T("Format not supported by Windows.") : Message,
    };
}

/// <summary>One processed image of a batch.</summary>
internal sealed record ImageBatchItem(string Input, string? Output, string? Error, long OriginalBytes, long OutputBytes);

internal sealed class MediaResult
{
    public MediaTool Tool { get; init; }

    public string Input { get; init; } = string.Empty;

    public List<string> Outputs { get; init; } = [];

    public long OriginalBytes { get; init; }

    public long OutputBytes { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string? Text { get; init; }

    public List<ImageBatchItem> Batch { get; init; } = [];

    public bool OutputGrew => OriginalBytes > 0 && OutputBytes > OriginalBytes;
}

internal enum MediaStateKind
{
    Idle,
    Ready,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Probe result of a video file.</summary>
internal sealed record VideoInfo(double Duration, int Width, int Height, double Fps, bool HasAudio, bool HasVideo, long Bitrate);

/// <summary>Settings accessors with the Vorssaint key names and ranges.</summary>
internal static class Ms
{
    private static Core.IAppSettings App => Host.App;

    public static MediaTool LastTool
    {
        get => MediaToolIds.Parse(App.Get<string>("mediaLastTool"));
        set => App.Set("mediaLastTool", MediaToolIds.IdOf(value));
    }

    public static double GetD(string key, double min, double max) => Math.Clamp(App.Get<double>(key), min, max);

    public static int GetI(string key, int min, int max) => Math.Clamp(App.Get<int>(key), min, max);

    public static string GetS(string key, params string[] allowed)
    {
        var value = App.Get<string>(key);
        return allowed.Contains(value) ? value : allowed[0];
    }

    public static bool GetB(string key) => App.Get<bool>(key);

    public static void Set<T>(string key, T value) => App.Set(key, value);

    /// <summary>The compression levels: Low 0.88, Medium 0.68, High 0.28 (the stored value picks the nearest).</summary>
    public static readonly double[] Levels = [0.88, 0.68, 0.28];

    public static int NearestLevel(double quality)
    {
        var best = 0;
        for (var i = 1; i < Levels.Length; i++)
        {
            if (Math.Abs(Levels[i] - quality) < Math.Abs(Levels[best] - quality))
            {
                best = i;
            }
        }

        return best;
    }
}

/// <summary>Output naming: defaults, uniqueness, rename tokens (spec 7.5.5).</summary>
internal static class MediaNaming
{
    private static readonly Regex IndexToken = new(@"\{(index|counter):0([2-6])\}", RegexOptions.Compiled);

    public static string BaseName(string input)
    {
        var name = Path.GetFileNameWithoutExtension(input).Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(name) ? Loc.T("Output") : name;
    }

    /// <summary>Appends " 2", " 3"... to the base until the file does not exist and is not reserved.</summary>
    public static string Unique(string directory, string baseName, string extension, ISet<string>? reserved = null)
    {
        var safe = FileOps.SanitizeName(baseName, 240 - extension.Length);
        var candidate = Path.Combine(directory, safe + extension);
        var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate) || (reserved?.Contains(candidate) ?? false))
        {
            candidate = Path.Combine(directory, $"{safe} {n}{extension}");
            n++;
        }

        reserved?.Add(candidate);
        return candidate;
    }

    public static string ExtensionOf(string format) => format switch { "png" => ".png", "heic" => ".heic", "pdf" => ".pdf", _ => ".jpg" };

    /// <summary>Expands {name} {date} {time} {datetime} {width} {height} {format} {index} {counter} (UTC, POSIX).</summary>
    public static string ExpandPattern(string pattern, string input, int index, int width, int height, string format)
    {
        var text = string.IsNullOrWhiteSpace(pattern) ? "{name}" : pattern;
        var now = DateTime.UtcNow;
        text = text.Replace("{name}", BaseName(input), StringComparison.Ordinal);
        text = text.Replace("{datetime}", now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text = text.Replace("{date}", now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text = text.Replace("{time}", now.ToString("HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text = text.Replace("{width}", width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text = text.Replace("{height}", height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text = text.Replace("{format}", ExtensionOf(format).TrimStart('.'), StringComparison.Ordinal);
        text = text.Replace("{ext}", ExtensionOf(format).TrimStart('.'), StringComparison.Ordinal);
        text = IndexToken.Replace(text, m => index.ToString("D" + m.Groups[2].Value, CultureInfo.InvariantCulture));
        text = text.Replace("{index}", index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{counter}", index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return text;
    }

    /// <summary>True when both paths name the same file (identity, or case-insensitive path when neither exists).</summary>
    public static bool SameFile(string a, string b)
    {
        var ia = Win32.FileIdentity(a);
        var ib = Win32.FileIdentity(b);
        if (ia is not null && ib is not null)
        {
            return ia == ib;
        }

        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    public static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".3gp", ".ts", ".mts", ".m2ts"];

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".jxr", ".ico"];

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string VideoFilter => "Videos|" + string.Join(';', VideoExtensions.Select(e => "*" + e)) + "|All files|*.*";

    public static string ImageFilter => "Images|" + string.Join(';', ImageExtensions.Select(e => "*" + e)) + "|All files|*.*";
}

/// <summary>The media service: one operation at a time, state machine, staged output (spec 7.5.5).</summary>
internal sealed class MediaService
{
    private CancellationTokenSource? _cts;
    private int _operation;

    public static MediaService Shared { get; } = new();

    public event Action? Changed;

    public MediaStateKind State { get; private set; } = MediaStateKind.Idle;

    public double Progress { get; private set; }

    public string? Message { get; private set; }

    public MediaResult? Result { get; private set; }

    public bool IsRunning => State == MediaStateKind.Running;

    private void Raise() => V.Post(() => Changed?.Invoke());

    public void Reset()
    {
        Cancel();
        State = MediaStateKind.Idle;
        Message = null;
        Result = null;
        Progress = 0;
        Raise();
    }

    public void MarkReady()
    {
        if (!IsRunning)
        {
            State = MediaStateKind.Ready;
            Message = null;
            Result = null;
            Raise();
        }
    }

    public void Fail(MediaException error)
    {
        State = MediaStateKind.Failed;
        Message = error.Text;
        Raise();
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>Runs <paramref name="work"/> as the current operation; stale callbacks are ignored.</summary>
    public async Task RunAsync(Func<Action<double>, CancellationToken, Task<MediaResult>> work)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var id = Interlocked.Increment(ref _operation);
        State = MediaStateKind.Running;
        Progress = 0;
        Message = null;
        Result = null;
        Raise();
        var started = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => work(p =>
            {
                if (id == _operation && !cts.IsCancellationRequested)
                {
                    Progress = Math.Clamp(p, 0, 1);
                    Raise();
                }
            }, cts.Token), cts.Token).ConfigureAwait(true);
            if (id != _operation)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                State = MediaStateKind.Cancelled;
            }
            else
            {
                State = MediaStateKind.Completed;
                Result = result;
            }
        }
        catch (OperationCanceledException)
        {
            if (id == _operation)
            {
                State = MediaStateKind.Cancelled;
            }
        }
        catch (MediaException ex)
        {
            if (id == _operation)
            {
                State = ex.Kind == MediaFailureKind.Cancelled ? MediaStateKind.Cancelled : MediaStateKind.Failed;
                Message = ex.Text;
            }
        }
        catch (Exception ex)
        {
            if (id == _operation)
            {
                State = MediaStateKind.Failed;
                Message = string.IsNullOrWhiteSpace(ex.Message) ? Loc.T("Format not supported by Windows.") : ex.Message;
                Host.Log("Media operation failed", ex);
            }
        }

        if (id == _operation)
        {
            Raise();
        }
    }
}

/// <summary>Atomic output: write to a temp file next to the destination, then move it into place.</summary>
internal static class StagedOutput
{
    public static string TempFor(string destination) => destination + ".winknife-tmp";

    public static void Commit(string temp, string destination, bool overwrite, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            TryDelete(temp);
            throw new OperationCanceledException(ct);
        }

        File.Move(temp, destination, overwrite);
        try
        {
            var attrs = File.GetAttributes(destination);
            if ((attrs & FileAttributes.Hidden) != 0 && !Path.GetFileName(destination).StartsWith('.'))
            {
                File.SetAttributes(destination, attrs & ~FileAttributes.Hidden);
            }
        }
        catch
        {
            // attributes are cosmetic
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // leave the temp file
        }
    }
}

/// <summary>Profile storage: the portable JSON shape of spec 7.5.4.</summary>
internal sealed class ImageProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public ImageOptions Options { get; set; } = new();
}

/// <summary>All options of the image tool.</summary>
internal sealed class ImageOptions
{
    [JsonPropertyName("quality")] public double Quality { get; set; } = 0.72;

    [JsonPropertyName("maxDimension")] public int MaxDimension { get; set; } = 1600;

    [JsonPropertyName("format")] public string Format { get; set; } = "jpeg";

    [JsonPropertyName("stripMetadata")] public bool StripMetadata { get; set; } = true;

    [JsonPropertyName("resizeKind")] public string ResizeKind { get; set; } = "maxDimension";

    [JsonPropertyName("resizeWidth")] public int ResizeWidth { get; set; } = 1600;

    [JsonPropertyName("resizeHeight")] public int ResizeHeight { get; set; } = 1200;

    [JsonPropertyName("exactMode")] public string ExactMode { get; set; } = "stretch";

    [JsonPropertyName("watermarkKind")] public string WatermarkKind { get; set; } = "off";

    [JsonPropertyName("watermarkText")] public string WatermarkText { get; set; } = string.Empty;

    [JsonPropertyName("watermarkLogoPath")] public string WatermarkLogoPath { get; set; } = string.Empty;

    [JsonPropertyName("watermarkPosition")] public string WatermarkPosition { get; set; } = "bottomRight";

    [JsonPropertyName("watermarkOpacity")] public double WatermarkOpacity { get; set; } = 0.45;

    [JsonPropertyName("watermarkMargin")] public int WatermarkMargin { get; set; } = 32;

    [JsonPropertyName("watermarkScale")] public double WatermarkScale { get; set; } = 0.18;

    [JsonPropertyName("renamePattern")] public string RenamePattern { get; set; } = string.Empty;

    [JsonPropertyName("background")] public string Background { get; set; } = "transparent";

    [JsonPropertyName("preserveModificationDate")] public bool PreserveModificationDate { get; set; }

    public static ImageOptions FromSettings() => new()
    {
        Quality = Ms.GetD("mediaImageQuality", 0.1, 1),
        MaxDimension = Ms.GetI("mediaImageMaxDimension", 64, 20000),
        Format = Ms.GetS("mediaImageFormat", "jpeg", "png", "heic", "pdf"),
        StripMetadata = Ms.GetB("mediaImageStripMetadata"),
        ResizeKind = Ms.GetS("mediaImageResizeKind", "maxDimension", "none", "width", "height", "exact"),
        ResizeWidth = Ms.GetI("mediaImageResizeWidth", 1, 20000),
        ResizeHeight = Ms.GetI("mediaImageResizeHeight", 1, 20000),
        ExactMode = Ms.GetS("mediaImageExactResizeMode", "stretch", "fit", "fill"),
        WatermarkKind = Ms.GetS("mediaImageWatermarkKind", "off", "text", "logo", "textAndLogo"),
        WatermarkText = Host.App.Get<string>("mediaImageWatermarkText") ?? string.Empty,
        WatermarkLogoPath = Host.App.Get<string>("mediaImageWatermarkLogoPath") ?? string.Empty,
        WatermarkPosition = Ms.GetS("mediaImageWatermarkPosition", "bottomRight", "topLeft", "topRight", "center", "bottomLeft"),
        WatermarkOpacity = Ms.GetD("mediaImageWatermarkOpacity", 0.1, 1),
        WatermarkMargin = Ms.GetI("mediaImageWatermarkMargin", 0, 2000),
        WatermarkScale = Ms.GetD("mediaImageWatermarkScale", 0.05, 0.8),
        RenamePattern = Host.App.Get<string>("mediaImageRenamePattern") ?? string.Empty,
        Background = Ms.GetS("mediaImageBackground", "transparent", "white", "black"),
        PreserveModificationDate = Ms.GetB("mediaImagePreserveModificationDate"),
    };

    public void SaveToSettings()
    {
        Ms.Set("mediaImageQuality", Quality);
        Ms.Set("mediaImageMaxDimension", MaxDimension);
        Ms.Set("mediaImageFormat", Format);
        Ms.Set("mediaImageStripMetadata", StripMetadata);
        Ms.Set("mediaImageResizeKind", ResizeKind);
        Ms.Set("mediaImageResizeWidth", ResizeWidth);
        Ms.Set("mediaImageResizeHeight", ResizeHeight);
        Ms.Set("mediaImageExactResizeMode", ExactMode);
        Ms.Set("mediaImageWatermarkKind", WatermarkKind);
        Ms.Set("mediaImageWatermarkText", WatermarkText);
        Ms.Set("mediaImageWatermarkLogoPath", WatermarkLogoPath);
        Ms.Set("mediaImageWatermarkPosition", WatermarkPosition);
        Ms.Set("mediaImageWatermarkOpacity", WatermarkOpacity);
        Ms.Set("mediaImageWatermarkMargin", WatermarkMargin);
        Ms.Set("mediaImageWatermarkScale", WatermarkScale);
        Ms.Set("mediaImageRenamePattern", RenamePattern);
        Ms.Set("mediaImageBackground", Background);
        Ms.Set("mediaImagePreserveModificationDate", PreserveModificationDate);
    }

    public ImageOptions Clone() => JsonSerializer.Deserialize<ImageOptions>(JsonSerializer.Serialize(this)) ?? new ImageOptions();

    public bool SameAs(ImageOptions other) => JsonSerializer.Serialize(this) == JsonSerializer.Serialize(other);

    public static List<ImageProfile> LoadProfiles()
    {
        try
        {
            return JsonSerializer.Deserialize<List<ImageProfile>>(Host.App.Get<string>("mediaImageProfiles") ?? "[]") ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void SaveProfiles(List<ImageProfile> profiles) => Ms.Set("mediaImageProfiles", JsonSerializer.Serialize(profiles));
}
