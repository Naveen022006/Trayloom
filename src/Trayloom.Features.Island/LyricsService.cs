// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Island;

internal enum LyricsState
{
    Idle,
    Loading,
    Unavailable,
    Failed,
    Ready,
}

internal sealed record LyricLine(double Time, string Text);

/// <summary>Lyrics of one recording (lrclib.net when the user allowed it, or an imported .lrc file) — spec Part C 1.5.</summary>
internal sealed class LyricsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly IslandCore _core;
    private string _identity = "";
    private int _generation;
    private DateTime _lastFailure = DateTime.MinValue;

    static LyricsService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Trayloom/0.2 (https://github.com/Naveen022006/Trayloom)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public LyricsService(IslandCore core) => _core = core;

    public LyricsState State { get; private set; }

    public List<LyricLine> Lines { get; private set; } = [];

    public string Plain { get; private set; } = "";

    public bool Instrumental { get; private set; }

    public bool Synced => Lines.Count > 0;

    public double Offset { get; set; }

    public event Action? Changed;

    public bool Enabled => _core.App.Get<bool>(K.LyricsEnabled) && _core.Ctx.Runtime.IsAvailable("notch-lyrics");

    public bool Online => _core.App.Get<bool>(K.LyricsOnline);

    public void Clear()
    {
        _generation++;
        State = LyricsState.Idle;
        Lines = [];
        Plain = "";
        Instrumental = false;
        Offset = 0;
        _identity = "";
        Changed?.Invoke();
    }

    /// <summary>Called while the lyrics page is open; loads once per recording.</summary>
    public void Ensure(Playback p, bool force = false)
    {
        if (!Enabled || !p.HasTrack)
        {
            return;
        }

        if (p.Identity != _identity)
        {
            _generation++;
            State = LyricsState.Idle;
            Lines = [];
            Plain = "";
            Instrumental = false;
            Offset = 0;
            _identity = p.Identity;
        }
        else if (!force && State != LyricsState.Idle)
        {
            return;
        }

        if (!Online)
        {
            State = LyricsState.Unavailable;
            Changed?.Invoke();
            return;
        }

        _ = LoadAsync(p, _generation);
    }

    private async Task LoadAsync(Playback p, int gen)
    {
        if (string.IsNullOrWhiteSpace(p.Title) || string.IsNullOrWhiteSpace(p.Artist) || p.Duration is < 1 or > 3600)
        {
            State = LyricsState.Unavailable;
            Changed?.Invoke();
            return;
        }

        State = LyricsState.Loading;
        Changed?.Invoke();
        try
        {
            var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(p.Title)}&artist_name={Uri.EscapeDataString(p.Artist)}"
                + (string.IsNullOrWhiteSpace(p.Album) ? "" : $"&album_name={Uri.EscapeDataString(p.Album)}")
                + $"&duration={(int)Math.Round(p.Duration)}";
            using var resp = await Http.GetAsync(url);
            if (gen != _generation)
            {
                return;
            }

            if (!resp.IsSuccessStatusCode)
            {
                State = resp.StatusCode == System.Net.HttpStatusCode.NotFound ? LyricsState.Unavailable : LyricsState.Failed;
                Changed?.Invoke();
                return;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number && Math.Abs(dur.GetDouble() - p.Duration) > 2)
            {
                State = LyricsState.Unavailable;
                Changed?.Invoke();
                return;
            }

            Instrumental = root.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True;
            var synced = root.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
            var plain = root.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() ?? "" : "";
            if (synced.Length > 0)
            {
                Lines = ParseLrc(synced, p.Duration);
            }

            Plain = plain;
            State = Instrumental || Lines.Count > 0 || plain.Length > 0 ? LyricsState.Ready : LyricsState.Unavailable;
        }
        catch
        {
            if (gen == _generation)
            {
                State = LyricsState.Failed;
                _lastFailure = DateTime.Now;
            }
        }

        if (gen == _generation)
        {
            Changed?.Invoke();
        }
    }

    public void Import(string path, double duration)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 128 * 1024)
            {
                State = LyricsState.Failed;
                Changed?.Invoke();
                return;
            }

            var text = File.ReadAllText(path);
            var lines = ParseLrc(text, duration);
            if (lines.Count > 0)
            {
                Lines = lines;
                Plain = "";
                State = LyricsState.Ready;
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                Lines = [];
                Plain = text;
                State = LyricsState.Ready;
            }
            else
            {
                State = LyricsState.Failed;
            }
        }
        catch
        {
            State = LyricsState.Failed;
        }

        Changed?.Invoke();
    }

    public static List<LyricLine> ParseLrc(string text, double duration)
    {
        var result = new List<LyricLine>();
        if (text.Length > 128 * 1024)
        {
            text = text[..(128 * 1024)];
        }

        text = text.TrimStart('﻿');
        double offset = 0;
        var tag = new Regex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]");
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var off = Regex.Match(line, @"^\[offset:\s*([+-]?\d+)\]", RegexOptions.IgnoreCase);
            if (off.Success && int.TryParse(off.Groups[1].Value, out var ms) && Math.Abs(ms) <= 60000)
            {
                offset = ms / 1000.0;
                continue;
            }

            var matches = tag.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var body = tag.Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var frac = m.Groups[3].Success ? double.Parse("0." + m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                var t = (min * 60) + sec + frac - offset;
                if (duration > 0 && t > duration + 2)
                {
                    continue;
                }

                result.Add(new LyricLine(Math.Max(0, t), body));
            }

            if (result.Count >= 2000)
            {
                break;
            }
        }

        result = [.. result.OrderBy(l => l.Time)];
        var merged = new List<LyricLine>();
        foreach (var l in result)
        {
            if (merged.Count > 0 && Math.Abs(merged[^1].Time - l.Time) < 0.001)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + "\n" + l.Text };
            }
            else
            {
                merged.Add(l);
            }
        }

        return merged.All(l => string.IsNullOrWhiteSpace(l.Text)) ? [] : merged;
    }

    public int ActiveIndex(double position)
    {
        var p = position - Offset;
        var idx = -1;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= p)
            {
                idx = i;
            }
            else
            {
                break;
            }
        }

        return idx;
    }
}
