// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;

namespace WinKnife.Core;

/// <summary>One language of the picker (Vorssaint's <c>AppLanguage</c>): its code and its own name in its own script.</summary>
public sealed record AppLanguage(string Code, string NativeName)
{
    /// <summary>The 15 languages Vorssaint ships, in Vorssaint's order.</summary>
    public static IReadOnlyList<AppLanguage> All { get; } =
    [
        new("en-US", "English (US)"), new("pt-BR", "Português (Brasil)"), new("tr", "Türkçe"), new("ru", "Русский"),
        new("es", "Español"), new("sk", "Slovenčina"), new("de", "Deutsch"), new("fr", "Français"), new("it", "Italiano"),
        new("ja", "日本語"), new("ko", "한국어"), new("uk", "Українська"),
        new("zh-Hans", "简体中文"), new("zh-TW", "繁體中文（台灣）"), new("zh-HK", "繁體中文（香港）"),
    ];
}

/// <summary>
/// Localization service. Every user-visible string of the UI goes through <see cref="T"/> / <see cref="F"/> with its
/// English text as the key. The tables (<c>Strings.&lt;locale&gt;.json</c>, embedded) were harvested from Vorssaint's own
/// translation tables so the wording matches the original; a text without an entry is shown in English.
/// </summary>
public static class Loc
{
    /// <summary>Value of the <c>appLanguage</c> setting that follows the Windows display language.</summary>
    public const string SystemDefault = "system";

    /// <summary>The Windows display language, captured before the app changes the thread cultures.</summary>
    private static readonly string WindowsDisplayLanguage = CultureInfo.CurrentUICulture.Name;

    private static readonly object Gate = new();
    private static Dictionary<string, string> _table = new(StringComparer.Ordinal);
    private static IAppSettings? _settings;

    /// <summary>The language in use (a Vorssaint language code, e.g. <c>de</c>, <c>zh-Hans</c>).</summary>
    public static string Language { get; private set; } = "en-US";

    /// <summary>Culture used for numbers, dates and string formatting (the language, with the Windows region when it fits).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The short time pattern of the current culture (<c>h:mm tt</c> or <c>HH:mm</c>).</summary>
    public static string ShortTime => Culture.DateTimeFormat.ShortTimePattern;

    /// <summary>Incremented each time the language changes (see <see cref="LocValue{T}"/>).</summary>
    public static int Version { get; private set; }

    /// <summary>Raised after the language changed.</summary>
    public static event Action? Changed;

    /// <summary>Reads the <c>appLanguage</c> setting, applies it and follows later changes.</summary>
    public static void Initialize(IAppSettings settings)
    {
        _settings = settings;
        Apply(Resolve(settings.Get<string>(AppKeys.AppLanguage)), raise: false);
        settings.Changed += key =>
        {
            if (key == AppKeys.AppLanguage)
            {
                var language = Resolve(settings.Get<string>(AppKeys.AppLanguage));
                if (language != Language)
                {
                    Apply(language, raise: true);
                }
            }
        };
    }

    /// <summary>The configured choice: <see cref="SystemDefault"/> or a language code.</summary>
    public static string Setting => _settings?.Get<string>(AppKeys.AppLanguage) is { Length: > 0 } s ? s : SystemDefault;

    /// <summary>Translates an English UI text (returned unchanged when there is no translation).</summary>
    public static string T(string english)
    {
        var table = _table;
        return table.Count > 0 && table.TryGetValue(english, out var translated) ? translated : english;
    }

    /// <summary>Translates a composite format string and formats it with the current culture.</summary>
    public static string F(string english, params object?[] args)
    {
        var format = T(english);
        try
        {
            return string.Format(Culture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(Culture, english, args);
        }
    }

    /// <summary>Formats one value with a format string using the current culture (used for <c>{value:F1}</c> holes).</summary>
    public static string Fmt(object? value, string format) =>
        value is IFormattable f ? f.ToString(format, Culture) : value?.ToString() ?? string.Empty;

    /// <summary>True when a translation exists for this English text in the current language.</summary>
    public static bool Has(string english) => _table.ContainsKey(english);

    /// <summary>Maps a setting value (or null / empty / "system") to a language code.</summary>
    public static string Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting) && !string.Equals(setting, SystemDefault, StringComparison.OrdinalIgnoreCase))
        {
            var match = AppLanguage.All.FirstOrDefault(l => string.Equals(l.Code, setting, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Code;
            }
        }

        return FromWindows(WindowsDisplayLanguage);
    }

    /// <summary>Vorssaint's <c>AppLanguage.systemDefault</c> for a Windows culture name.</summary>
    public static string FromWindows(string cultureName)
    {
        var p = cultureName.ToLowerInvariant();
        if (p.StartsWith("zh-hk", StringComparison.Ordinal) || p.StartsWith("zh-hant-hk", StringComparison.Ordinal))
        {
            return "zh-HK";
        }

        if (p.StartsWith("zh-tw", StringComparison.Ordinal) || p.StartsWith("zh-hant", StringComparison.Ordinal) || p.StartsWith("zh-mo", StringComparison.Ordinal))
        {
            return "zh-TW";
        }

        (string Prefix, string Code)[] map =
        [
            ("pt", "pt-BR"), ("tr", "tr"), ("ru", "ru"), ("es", "es"), ("sk", "sk"), ("de", "de"),
            ("fr", "fr"), ("it", "it"), ("ja", "ja"), ("ko", "ko"), ("uk", "uk"), ("zh", "zh-Hans"),
        ];
        foreach (var (prefix, code) in map)
        {
            if (p.StartsWith(prefix, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return "en-US";
    }

    private static void Apply(string language, bool raise)
    {
        lock (Gate)
        {
            Language = language;
            Version++;
            _table = Load(language);
            Culture = BuildCulture(language);
            CultureInfo.DefaultThreadCurrentCulture = Culture;
            CultureInfo.DefaultThreadCurrentUICulture = Culture;
            try
            {
                CultureInfo.CurrentCulture = Culture;
                CultureInfo.CurrentUICulture = Culture;
            }
            catch (Exception)
            {
                // read-only on some threads
            }
        }

        if (raise)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The language's culture, combined with the Windows region (number and date formats) when such a culture exists.</summary>
    private static CultureInfo BuildCulture(string language)
    {
        var tag = language == "zh-Hans" ? "zh-CN" : language;
        try
        {
            if (!tag.Contains('-'))
            {
                var region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                var combined = CultureInfo.GetCultureInfo($"{tag}-{region}");
                if (!combined.IsNeutralCulture && combined.Name.Length > 0 && combined.Name.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase))
                {
                    return CultureInfo.ReadOnly(new CultureInfo(combined.Name));
                }
            }
        }
        catch (Exception)
        {
            // no such combination
        }

        try
        {
            return CultureInfo.ReadOnly(new CultureInfo(tag));
        }
        catch (Exception)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        if (language == "en-US")
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var asm = typeof(Loc).Assembly;
            using var stream = asm.GetManifestResourceStream($"WinKnife.Localization.Strings.{language}.json");
            if (stream is null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            return new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Number of texts translated in a language (for diagnostics and tests).</summary>
    public static int TranslatedCount(string language) => Load(language).Count;
}

/// <summary>
/// A value built from translated texts (a static table of titles, for instance) that is rebuilt after the language changed.
/// </summary>
public sealed class LocValue<T>(Func<T> build)
{
    private T? _value;
    private int _version = -1;

    /// <summary>The value for the current language.</summary>
    public T Value
    {
        get
        {
            var version = Loc.Version;
            if (_version != version || _value is null)
            {
                _value = build();
                _version = version;
            }

            return _value;
        }
    }
}
