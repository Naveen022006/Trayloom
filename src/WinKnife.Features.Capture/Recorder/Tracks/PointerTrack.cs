// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Capture;

/// <summary>One pointer image the recording showed: PNG bytes, hot spot and size (physical pixels).</summary>
internal sealed class CursorShape
{
    private BitmapSource? _image;

    public double HotX { get; set; }

    public double HotY { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public byte[] Png { get; set; } = Array.Empty<byte>();

    /// <summary>The decoded (frozen) image, or null when the PNG is unreadable.</summary>
    public BitmapSource? Image
    {
        get
        {
            if (_image is not null || Png.Length == 0)
            {
                return _image;
            }

            try
            {
                var decoder = new PngBitmapDecoder(new MemoryStream(Png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                _image = frame;
            }
            catch
            {
                _image = null;
            }

            return _image;
        }
    }
}

/// <summary>pointer.bin (little-endian, "VRPT" v3): header, 16-byte samples, 8-byte clicks, PNG shapes. Decoding is tolerant of a truncated tail.</summary>
internal sealed class PointerTrack
{
    private const int Version = 3;

    public float SystemScale { get; set; } = 1;

    public float DisplayScale { get; set; } = 1;

    public List<PointerSample> Samples { get; } = new();

    public List<PointerClick> Clicks { get; } = new();

    public List<CursorShape> Shapes { get; } = new();

    public bool IsEmpty => Samples.Count == 0;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'V');
        w.Write((byte)'R');
        w.Write((byte)'P');
        w.Write((byte)'T');
        w.Write((ushort)Version);
        w.Write((ushort)0);
        w.Write(SystemScale);
        w.Write(DisplayScale);
        w.Write((uint)Samples.Count);
        w.Write((uint)Clicks.Count);
        w.Write((uint)Shapes.Count);
        foreach (var s in Samples)
        {
            w.Write((float)s.Time);
            w.Write((float)s.X);
            w.Write((float)s.Y);
            w.Write((ushort)Math.Clamp(s.ShapeIndex, 0, ushort.MaxValue));
            w.Write((ushort)(s.IsVisible ? 1 : 0));
        }

        foreach (var c in Clicks)
        {
            w.Write((float)c.Time);
            w.Write(c.IsDown ? 1u : 0u);
        }

        foreach (var shape in Shapes)
        {
            w.Write((float)shape.HotX);
            w.Write((float)shape.HotY);
            w.Write((float)shape.Width);
            w.Write((float)shape.Height);
            w.Write((uint)shape.Png.Length);
            w.Write(shape.Png);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static PointerTrack Decode(byte[] data)
    {
        var track = new PointerTrack();
        if (data.Length < 28 || data[0] != 'V' || data[1] != 'R' || data[2] != 'P' || data[3] != 'T')
        {
            return track;
        }

        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);
        r.ReadInt32();
        var version = r.ReadUInt16();
        r.ReadUInt16();
        if (version != Version)
        {
            return track;
        }

        track.SystemScale = r.ReadSingle();
        track.DisplayScale = r.ReadSingle();
        var samples = r.ReadUInt32();
        var clicks = r.ReadUInt32();
        var shapes = r.ReadUInt32();
        try
        {
            for (uint i = 0; i < samples && ms.Length - ms.Position >= 16; i++)
            {
                var t = r.ReadSingle();
                var x = r.ReadSingle();
                var y = r.ReadSingle();
                var shape = r.ReadUInt16();
                var visible = r.ReadUInt16();
                if (float.IsFinite(t) && float.IsFinite(x) && float.IsFinite(y))
                {
                    track.Samples.Add(new PointerSample(t, x, y, shape, visible != 0));
                }
            }

            for (uint i = 0; i < clicks && ms.Length - ms.Position >= 8; i++)
            {
                var t = r.ReadSingle();
                var down = r.ReadUInt32();
                if (float.IsFinite(t))
                {
                    track.Clicks.Add(new PointerClick(t, down != 0));
                }
            }

            for (uint i = 0; i < shapes && ms.Length - ms.Position >= 20; i++)
            {
                var shape = new CursorShape { HotX = r.ReadSingle(), HotY = r.ReadSingle(), Width = r.ReadSingle(), Height = r.ReadSingle() };
                var len = r.ReadUInt32();
                if (len > ms.Length - ms.Position)
                {
                    break;
                }

                shape.Png = r.ReadBytes((int)len);
                track.Shapes.Add(shape);
            }
        }
        catch (EndOfStreamException)
        {
            // truncated tail keeps the whole records read so far
        }

        track.Samples.Sort((a, b) => a.Time.CompareTo(b.Time));
        track.Clicks.Sort((a, b) => a.Time.CompareTo(b.Time));
        return track;
    }

    public static PointerTrack Load(string path)
    {
        try
        {
            return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : new PointerTrack();
        }
        catch
        {
            return new PointerTrack();
        }
    }
}

/// <summary>typing.json: key-down times only, never the key or the text.</summary>
internal static class TypingTrack
{
    public static List<double> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new List<double>();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("times", out var times) && times.ValueKind == JsonValueKind.Array
                ? times.EnumerateArray().Select(e => e.GetDouble()).Where(double.IsFinite).ToList()
                : new List<double>();
        }
        catch
        {
            return new List<double>();
        }
    }

    public static void Save(string path, IReadOnlyList<double> times) => File.WriteAllText(path, JsonSerializer.Serialize(new { times }));
}
