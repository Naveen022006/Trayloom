// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.Json;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>The screen edge the island rests on; its pages open away from it.</summary>
internal enum IslandEdge
{
    Top,
    Bottom,
}

/// <summary>
/// Where the island sits on the work area of one display: <see cref="X"/> is the island's centre across the work area
/// (0 left, 0.5 centre, 1 right; the body is kept <see cref="IslandPlacement.EdgeMargin"/> units inside), <see cref="Edge"/> the edge it rests
/// on and <see cref="Offset"/> the distance in island units (DIP before the island zoom) from that edge to the body's near side.
/// </summary>
internal readonly record struct IslandPosition(double X, IslandEdge Edge, double Offset)
{
    /// <summary>Top centre, attached: where the island has always been.</summary>
    public static readonly IslandPosition Default = new(0.5, IslandEdge.Top, 0);

    /// <summary>Resting against its edge (the notch silhouette is only allowed then).</summary>
    public bool Attached => Offset < 0.5;

    /// <summary>Reads <c>"x,top|bottom,offset"</c> (invariant numbers). Anything else, NaN and infinity included, is <see cref="Default"/>.</summary>
    public static IslandPosition Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Default;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
            || !double.IsFinite(x)
            || !double.IsFinite(offset))
        {
            return Default;
        }

        IslandEdge edge;
        if (string.Equals(parts[1], "top", StringComparison.OrdinalIgnoreCase))
        {
            edge = IslandEdge.Top;
        }
        else if (string.Equals(parts[1], "bottom", StringComparison.OrdinalIgnoreCase))
        {
            edge = IslandEdge.Bottom;
        }
        else
        {
            return Default;
        }

        return new IslandPosition(Math.Clamp(x, 0, 1), edge, Math.Clamp(offset, 0, 2000));
    }

    /// <summary>The stored form, e.g. <c>0.5,top,0</c> (the inverse of <see cref="Parse"/>).</summary>
    public string Format() =>
        string.Create(CultureInfo.InvariantCulture, $"{X:0.####},{(Edge == IslandEdge.Top ? "top" : "bottom")},{Offset:0.##}");
}

/// <summary>
/// One display as the island sees it. Rectangles are physical pixels in virtual-screen coordinates; <see cref="Dpi"/> is the effective
/// scale factor (1.0 = 96 dpi, 1.25 = 120 dpi), the same unit as <c>IslandNative.MonitorScale</c>.
/// </summary>
/// <param name="Key">Stable id: the monitor's device interface path (survives re-plugging and re-ordering); the GDI name when Windows gives none.</param>
/// <param name="HardwareId">The model part of the interface path (e.g. <c>STD0110</c>), for matching after the path changed.</param>
/// <param name="GdiName">The GDI device name (<c>\\.\DISPLAY1</c>); not stable across restarts.</param>
/// <param name="Name">What the person sees: the monitor's own name, else "Built-in display" / "Display n".</param>
/// <param name="Work">The work area (taskbar excluded), already shrunk by 2 px on an edge that carries an auto-hide taskbar.</param>
/// <param name="Handle">The HMONITOR; only valid until the display configuration changes.</param>
internal sealed record IslandMonitor(string Key, string HardwareId, string GdiName, string Name, bool IsPrimary, bool IsInternal, RECT Bounds, RECT Work, double Dpi, nint Handle);

/// <summary>
/// The pure geometry of one placement: a display, a position and the island zoom resolved into numbers. All "units" are island units
/// (DIP before zoom), so the window layout works in the same unit whatever the display's scale; <see cref="Scale"/> turns them into
/// physical pixels.
/// </summary>
/// <param name="Scale">Physical pixels per island unit (the display's DPI scale times the island zoom).</param>
/// <param name="UsableWidth">Work-area width in units.</param>
/// <param name="UsableHeight">Work-area height in units.</param>
/// <param name="Inset">The position's offset, clamped to <paramref name="MaxOffset"/> (units from the edge to the body's near side).</param>
/// <param name="MaxOffset">The largest offset this display can take while pages still have room to open.</param>
internal readonly record struct IslandFrame(IslandMonitor Monitor, IslandPosition Position, double Scale, double UsableWidth, double UsableHeight, double Inset, double MaxOffset)
{
    public IslandEdge Edge => Position.Edge;

    /// <summary>Bottom island: pages open upward.</summary>
    public bool OpensUp => Position.Edge == IslandEdge.Bottom;

    /// <summary>The island's nominal centre in units from the work area's left edge (before the body is kept inside the edge margins).</summary>
    public double CentreX => Position.X * UsableWidth;

    /// <summary>
    /// The window rectangle in physical pixels for an envelope of <paramref name="envW"/> x <paramref name="envH"/> units: centred on the island
    /// but kept inside the work area (never straddling two displays, so its DPI is this display's). A top island's window starts at the top of
    /// the work area and grows down; a bottom island's ends at the bottom of the work area and grows up.
    /// </summary>
    public (int X, int Y, int W, int H) WindowRect(double envW, double envH)
    {
        var work = Monitor.Work;
        var w = Math.Clamp((int)Math.Ceiling(envW * Scale), 1, Math.Max(1, work.Width));
        var h = Math.Clamp((int)Math.Ceiling(envH * Scale), 1, Math.Max(1, work.Height));

        // X is a fraction of the work width, so the default (0.5) is the exact middle however the units round
        var x = (int)Math.Round(work.Left + (Position.X * work.Width) - (w / 2.0));
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Edge == IslandEdge.Top ? work.Top : work.Bottom - h;
        return (x, y, w, h);
    }

    /// <summary>The work area's left edge in units from the window's left edge (the window starts at <paramref name="windowX"/> physical pixels).</summary>
    public double WorkLeftIn(int windowX) => (Monitor.Work.Left - windowX) / Scale;

    /// <summary>The work area's top edge in units from the window's top edge (the window starts at <paramref name="windowY"/> physical pixels).</summary>
    public double WorkTopIn(int windowY) => (Monitor.Work.Top - windowY) / Scale;

    /// <summary>
    /// Height in units a page may take when opening away from the edge: the work height minus the near gap (capsule gap and drop), the offset,
    /// the margin kept free at the far side and <paramref name="farGutter"/> (the bottom quick-button row, 0 when none).
    /// </summary>
    public double FarRoom(double nearGap, double farGutter) => UsableHeight - nearGap - Inset - IslandPlacement.FarMargin - farGutter;
}

/// <summary>Pure placement decisions of the Dynamic Island: frame, clamping, snapping on drop and display choice. No window, no screen.</summary>
internal static class IslandPlacement
{
    /// <summary>The body keeps this many units from the left and right edge of the work area.</summary>
    public const double EdgeMargin = 12;

    /// <summary>A dropped island within this many units of an edge or of the centre snaps to it.</summary>
    public const double SnapDistance = 20;

    /// <summary>Height in units an open page must still have at the largest offset.</summary>
    public const double MinOpenRoom = 360;

    /// <summary>Units kept free at the far side of an open page.</summary>
    public const double FarMargin = 48;

    /// <summary>Width of a quick-button column (also the bottom row's height).</summary>
    public const double SideGutter = 72;

    /// <summary><c>notchDisplay</c> value for "the display chosen in <c>notchDisplayId</c>".</summary>
    public const string SpecificDisplay = "specific";

    /// <summary>The largest offset a display of this usable height (units) can take: half the display at most, and pages keep their room.</summary>
    public static double MaxOffsetFor(double usableHeight) =>
        Math.Max(0, Math.Min((usableHeight / 2) - EdgeMargin, usableHeight - MinOpenRoom - FarMargin - SideGutter));

    /// <summary>Resolves a display, a position and the island zoom into a <see cref="IslandFrame"/> (the offset is clamped to what fits).</summary>
    public static IslandFrame Frame(IslandMonitor m, IslandPosition p, double zoom)
    {
        var scale = Math.Max(0.1, m.Dpi * zoom);
        var usableW = m.Work.Width / scale;
        var usableH = m.Work.Height / scale;
        var max = MaxOffsetFor(usableH);
        var inset = Math.Clamp(double.IsFinite(p.Offset) ? p.Offset : 0, 0, max);
        var pos = new IslandPosition(Math.Clamp(double.IsFinite(p.X) ? p.X : 0.5, 0, 1), p.Edge, inset);
        return new IslandFrame(m, pos, scale, usableW, usableH, inset, max);
    }

    /// <summary>
    /// The body's centre (units from the work area's left): <paramref name="centreX"/> moved as little as needed to keep the body and its quick-button
    /// gutters <see cref="EdgeMargin"/> inside the work area. When they cannot fit, the middle of the room they have.
    /// </summary>
    public static double ClampCentre(double centreX, double w, double gutterL, double gutterR, double usableW)
    {
        var min = EdgeMargin + (w / 2) + gutterL;
        var max = usableW - EdgeMargin - (w / 2) - gutterR;
        return min > max ? (min + max) / 2 : Math.Clamp(centreX, min, max);
    }

    /// <summary>
    /// The closed body (<paramref name="w"/> x <paramref name="h"/> units, no gutters) on screen, physical pixels: the rectangle the gesture hook,
    /// the hover test and the mirrors work with. <paramref name="nearGap"/> is the capsule gap plus the "Capsule fit" drop (units).
    /// </summary>
    public static RECT PillRectPx(IslandFrame f, double w, double h, double nearGap) =>
        BodyRectPx(f, ClampCentre(f.CentreX, w, 0, 0, f.UsableWidth), w, h, nearGap);

    /// <summary>A body of <paramref name="w"/> x <paramref name="h"/> units centred <paramref name="centre"/> units from the work area's left, on screen (physical pixels).</summary>
    public static RECT BodyRectPx(IslandFrame f, double centre, double w, double h, double nearGap)
    {
        var work = f.Monitor.Work;
        var wPx = w * f.Scale;
        var hPx = h * f.Scale;

        // an unclamped centre is taken straight from the fraction of the work width so the default matches the old centred math to the pixel
        var centrePx = Math.Abs(centre - f.CentreX) < 1e-9 ? work.Left + (f.Position.X * work.Width) : work.Left + (centre * f.Scale);
        var left = centrePx - (wPx / 2);
        var near = (nearGap + f.Inset) * f.Scale;
        var top = f.Edge == IslandEdge.Top ? work.Top + near : work.Bottom - near - hPx;
        return new RECT
        {
            Left = (int)Math.Floor(left),
            Top = (int)Math.Floor(top),
            Right = (int)Math.Ceiling(left + wPx),
            Bottom = (int)Math.Ceiling(top + hPx),
        };
    }

    /// <summary>
    /// Snaps a dropped island. <paramref name="cxPx"/>/<paramref name="cyPx"/> is the body's centre in physical pixels, <paramref name="wUnits"/> x
    /// <paramref name="hUnits"/> its size. The display is the work area containing the centre, else the nearest one; the edge is the one the centre is
    /// closer to; an offset or a side gap within <see cref="SnapDistance"/> snaps to 0, and a centre within it of the middle snaps to 0.5.
    /// The capsule gap is not known here (at most 2 units of the distance), it is added on top by the window.
    /// </summary>
    public static (IslandMonitor Monitor, IslandPosition Position) FromDrop(IReadOnlyList<IslandMonitor> ms, double cxPx, double cyPx, double wUnits, double hUnits, double zoom)
    {
        if (ms.Count == 0)
        {
            throw new ArgumentException("No displays.", nameof(ms));
        }

        var m = ms[0];
        var best = long.MaxValue;
        foreach (var candidate in ms)
        {
            var d = DistanceSquared(candidate.Work, cxPx, cyPx);
            if (d < best)
            {
                best = d;
                m = candidate;
            }
        }

        var scale = Math.Max(0.1, m.Dpi * zoom);
        var usableW = m.Work.Width / scale;
        var usableH = m.Work.Height / scale;
        var cx = Math.Clamp((cxPx - m.Work.Left) / scale, 0, usableW);
        var cy = Math.Clamp((cyPx - m.Work.Top) / scale, 0, usableH);

        var edge = cy <= usableH / 2 ? IslandEdge.Top : IslandEdge.Bottom;
        var offset = edge == IslandEdge.Top ? cy - (hUnits / 2) : usableH - (cy + (hUnits / 2));
        offset = offset < SnapDistance ? 0 : Math.Min(Math.Round(offset), MaxOffsetFor(usableH));

        double x;
        if (Math.Abs(cx - (usableW / 2)) <= SnapDistance)
        {
            x = 0.5;
        }
        else if (cx - (wUnits / 2) <= SnapDistance)
        {
            x = 0;
        }
        else if (usableW - (cx + (wUnits / 2)) <= SnapDistance)
        {
            x = 1;
        }
        else
        {
            x = Math.Round(cx / usableW, 4);
        }

        return (m, new IslandPosition(x, edge, offset));
    }

    /// <summary>
    /// The display the island is on for a <c>notchDisplay</c> setting: the one under the cursor for "pointer" and "all", the one saved in
    /// <paramref name="displayId"/> for "specific" (the main display while it is not connected), the internal panel for "builtIn" (the main
    /// display without one), the main display otherwise ("automatic", "main"). Never null; <paramref name="ms"/> must not be empty.
    /// </summary>
    public static IslandMonitor Resolve(string display, string displayId, IReadOnlyList<IslandMonitor> ms, int cursorX, int cursorY)
    {
        var primary = ms.FirstOrDefault(m => m.IsPrimary) ?? ms[0];
        switch (display)
        {
            case "pointer" or "all":
                {
                    var under = ms.FirstOrDefault(m => Contains(m.Bounds, cursorX, cursorY));
                    if (under is not null)
                    {
                        return under;
                    }

                    var best = primary;
                    var bestD = long.MaxValue;
                    foreach (var m in ms)
                    {
                        var d = DistanceSquared(m.Bounds, cursorX, cursorY);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = m;
                        }
                    }

                    return best;
                }

            case SpecificDisplay:
                return FindByKey(ms, displayId) ?? primary;
            case "builtIn":
                return ms.FirstOrDefault(m => m.IsInternal) ?? primary;
            default:
                return primary;
        }
    }

    /// <summary>
    /// Finds a saved display id among the connected ones: the exact key, then the hardware id (the model part of the key, or a bare id), then
    /// the GDI name. Null when nothing matches (the display is unplugged).
    /// </summary>
    public static IslandMonitor? FindByKey(IReadOnlyList<IslandMonitor> ms, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        key = key.Trim();
        var exact = ms.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var hardware = HardwareIdOf(key);
        if (hardware.Length > 0)
        {
            var byHardware = ms.FirstOrDefault(m => m.HardwareId.Length > 0 && string.Equals(m.HardwareId, hardware, StringComparison.OrdinalIgnoreCase));
            if (byHardware is not null)
            {
                return byHardware;
            }
        }

        return ms.FirstOrDefault(m => m.GdiName.Length > 0 && string.Equals(m.GdiName, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The model part of a device interface path (<c>\\?\DISPLAY#STD0110#4&amp;...#{guid}</c> gives <c>STD0110</c>); a string without <c>#</c> is taken as a bare hardware id.</summary>
    internal static string HardwareIdOf(string key)
    {
        if (!key.Contains('#'))
        {
            return key.StartsWith(@"\\", StringComparison.Ordinal) ? "" : key;
        }

        var parts = key.Split('#');
        return parts.Length >= 3 ? parts[1] : "";
    }

    /// <summary>
    /// The display setting after the island was dropped on <paramref name="dropped"/> while it had been on <paramref name="resolved"/>: a fixed choice
    /// (automatic, main, built-in, specific) that was left for another display becomes "specific" on that display; "pointer" and "all" never change.
    /// </summary>
    public static (string Display, string DisplayId) DisplayAfterDrop(string display, string displayId, IslandMonitor resolved, IslandMonitor dropped)
    {
        if (display is "pointer" or "all" || string.Equals(resolved.Key, dropped.Key, StringComparison.OrdinalIgnoreCase))
        {
            return (display, displayId);
        }

        return (SpecificDisplay, dropped.Key);
    }

    private static bool Contains(RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private static long DistanceSquared(RECT r, double x, double y)
    {
        var dx = x < r.Left ? r.Left - x : x > r.Right ? x - r.Right : 0;
        var dy = y < r.Top ? r.Top - y : y > r.Bottom ? y - r.Bottom : 0;
        return (long)((dx * dx) + (dy * dy));
    }
}

/// <summary>Reads and writes the island position settings: the per-display map, the plain fallback and the display rule.</summary>
internal static class IslandPlacementStore
{
    /// <summary>The map keeps the displays used most recently; older ones are forgotten.</summary>
    internal const int MaxEntries = 16;

    /// <summary>The saved position for a display: its entry in the map (by key, else by hardware id), else the plain <c>notchPosition</c>.</summary>
    public static IslandPosition Load(IAppSettings app, IslandMonitor m)
    {
        var map = ReadMap(app);
        var i = IndexOf(map, m);
        return i >= 0 ? IslandPosition.Parse(map[i].Value) : IslandPosition.Parse(app.Get<string>(K.Position));
    }

    /// <summary>
    /// Saves <paramref name="p"/> for <paramref name="m"/> (map entry, plain fallback). When <paramref name="resolvedBefore"/> is given (a drag or a
    /// preset on the display the island was on), the display setting follows by <see cref="IslandPlacement.DisplayAfterDrop"/>.
    /// </summary>
    public static void Save(IAppSettings app, IslandMonitor m, IslandPosition p, IslandMonitor? resolvedBefore)
    {
        var map = ReadMap(app);
        var i = IndexOf(map, m);
        if (i >= 0)
        {
            map.RemoveAt(i);
        }

        map.Add(new KeyValuePair<string, string>(m.Key, p.Format()));
        while (map.Count > MaxEntries)
        {
            map.RemoveAt(0);
        }

        app.Set(K.PositionsByDisplay, WriteMap(map));
        app.Set(K.Position, p.Format());

        if (resolvedBefore is not null)
        {
            var (display, id) = IslandPlacement.DisplayAfterDrop(app.Get<string>(K.Display), app.Get<string>(K.DisplayId), resolvedBefore, m);
            if (display != app.Get<string>(K.Display))
            {
                app.Set(K.Display, display);
            }

            if (id != app.Get<string>(K.DisplayId))
            {
                app.Set(K.DisplayId, id);
            }
        }
    }

    /// <summary>Back to the top centre on every display; a "specific" display choice goes back to automatic.</summary>
    public static void Reset(IAppSettings app)
    {
        app.Set(K.PositionsByDisplay, "");
        app.Set(K.Position, IslandPosition.Default.Format());
        if (app.Get<string>(K.Display) == IslandPlacement.SpecificDisplay)
        {
            app.Set(K.Display, "automatic");
        }
    }

    private static int IndexOf(List<KeyValuePair<string, string>> map, IslandMonitor m)
    {
        var i = map.FindIndex(e => string.Equals(e.Key, m.Key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            return i;
        }

        // the interface path changed (another port, a new driver) but the model is the same: only when it is unambiguous
        if (m.HardwareId.Length == 0)
        {
            return -1;
        }

        var matches = map.Select((e, n) => (e, n)).Where(t => string.Equals(IslandPlacement.HardwareIdOf(t.e.Key), m.HardwareId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0].n : -1;
    }

    private static List<KeyValuePair<string, string>> ReadMap(IAppSettings app)
    {
        var list = new List<KeyValuePair<string, string>>();
        try
        {
            var json = app.Get<string>(K.PositionsByDisplay);
            if (string.IsNullOrWhiteSpace(json))
            {
                return list;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return list;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
                {
                    list.Add(new KeyValuePair<string, string>(property.Name, value));
                }
            }
        }
        catch (JsonException)
        {
            list.Clear();
        }

        return list;
    }

    private static string WriteMap(List<KeyValuePair<string, string>> map)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var entry in map)
            {
                writer.WriteString(entry.Key, entry.Value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
