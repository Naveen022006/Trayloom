// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinKnife.App;

/// <summary>The notification-area icon: theme-adaptive brand mark (or a chosen glyph), state badges, tooltip and click events.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly ILog _log;
    private Forms.NotifyIcon? _icon;
    private Drawing.Icon? _iconImage;
    private string _glyphCode = string.Empty;
    private string _tooltip = Loc.T("WinKnife: normal sleep");
    private Color? _badge;
    private Color? _tint;
    private string? _overrideGlyph;
    private string? _overrideFont;
    private string? _overlayText;
    private bool _visible = true;

    public TrayController(ILog log)
    {
        _log = log;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        Create();
    }

    /// <summary>Left click; argument = cursor position in physical pixels.</summary>
    public event Action<Point>? LeftClick;

    /// <summary>Right click; argument = cursor position in physical pixels.</summary>
    public event Action<Point>? RightClick;

    /// <summary>The user clicked a balloon / toast raised by <see cref="ShowBalloon"/>.</summary>
    public event Action? BalloonClicked;

    /// <summary>The configured glyph hex code (empty = brand mark).</summary>
    public string GlyphCode
    {
        get => _glyphCode;
        set
        {
            if (_glyphCode != value)
            {
                _glyphCode = value;
                Render();
            }
        }
    }

    /// <summary>Hover text (max 127 chars).</summary>
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value.Length > 120 ? value[..120] : value;
            if (_icon is not null)
            {
                _icon.Text = _tooltip;
            }
        }
    }

    /// <summary>A small coloured dot in the lower-right (keep awake active, mic muted...). Null = none.</summary>
    public Color? Badge
    {
        get => _badge;
        set
        {
            if (_badge != value)
            {
                _badge = value;
                Render();
            }
        }
    }

    /// <summary>Shows or hides the icon (hidden while the live metrics stand in for it).</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible != value)
            {
                _visible = value;
                if (_icon is not null)
                {
                    _icon.Visible = value;
                }
            }
        }
    }

    /// <summary>
    /// Active-state look (keep awake): a tint for the mark, an optional replacement glyph (character + font family) and an optional
    /// short text (remaining time) drawn instead of the mark. All null = the normal icon.
    /// </summary>
    public void SetLook(Color? tint, string? glyph, string? font, string? overlayText)
    {
        if (_tint == tint && _overrideGlyph == glyph && _overrideFont == font && _overlayText == overlayText)
        {
            return;
        }

        _tint = tint;
        _overrideGlyph = glyph;
        _overrideFont = font;
        _overlayText = overlayText;
        Render();
    }

    public void ShowBalloon(string title, string message, NotificationLevel level)
    {
        var icon = level switch
        {
            NotificationLevel.Warning => Forms.ToolTipIcon.Warning,
            NotificationLevel.Error => Forms.ToolTipIcon.Error,
            _ => Forms.ToolTipIcon.Info,
        };
        _icon?.ShowBalloonTip(4000, title, message, icon);
    }

    /// <summary>Disposes and recreates the icon ("Show tray icon").</summary>
    public void Recreate()
    {
        DisposeIcon();
        Create();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        DisposeIcon();
    }

    private void Create()
    {
        _icon = new Forms.NotifyIcon { Text = _tooltip, Visible = _visible };
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
        _icon.MouseUp += (_, e) =>
        {
            var p = Screens.CursorPosition();
            if (e.Button == Forms.MouseButtons.Left)
            {
                LeftClick?.Invoke(p);
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                RightClick?.Invoke(p);
            }
        };
        Render();
    }

    private void DisposeIcon()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }

        _iconImage?.Dispose();
        _iconImage = null;
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(Render);
        }
    }

    private void Render()
    {
        if (_icon is null)
        {
            return;
        }

        try
        {
            var size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
            var light = ThemeManager.ReadTaskbarIsLight();
            var color = _tint ?? (light ? Color.FromRgb(0x18, 0x18, 0x1A) : Colors.White);
            BitmapSource bmp;
            if (_overlayText is { Length: > 0 } overlay)
            {
                bmp = RenderText(overlay, size, color, _badge);
            }
            else if (_overrideGlyph is { Length: > 0 } glyph)
            {
                bmp = RenderGlyph(glyph, size, color, _badge, _overrideFont is null ? null : new FontFamily(_overrideFont));
            }
            else if (_glyphCode.Length > 0 && int.TryParse(_glyphCode, System.Globalization.NumberStyles.HexNumber, null, out var code))
            {
                bmp = RenderGlyph(char.ConvertFromUtf32(code), size, color, _badge);
            }
            else
            {
                bmp = BrandMark.Render(size, color, _badge);
            }

            var old = _iconImage;
            _iconImage = ToIcon(bmp);
            _icon.Icon = _iconImage;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Rendering the tray icon failed", ex);
        }
    }

    private static BitmapSource RenderText(string value, int size, Color color, Color? badge)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var text = new FormattedText(value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(UiKit.TextFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size * (value.Length > 2 ? 0.55 : 0.72), new SolidColorBrush(color), 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            if (badge is { } b)
            {
                var r = size * 0.2;
                dc.DrawEllipse(new SolidColorBrush(b), null, new Point(size - r - 0.5, size - r - 0.5), r, r);
            }
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource RenderGlyph(string glyph, int size, Color color, Color? badge, FontFamily? font = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font ?? UiKit.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.9, new SolidColorBrush(color), 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            if (badge is { } b)
            {
                var r = size * 0.2;
                dc.DrawEllipse(new SolidColorBrush(b), null, new Point(size - r - 0.5, size - r - 0.5), r, r);
            }
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static Drawing.Icon ToIcon(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        using var temp = Drawing.Icon.FromHandle(handle);
        var clone = (Drawing.Icon)temp.Clone();
        NativeDestroy(handle);
        return clone;
    }

    private static void NativeDestroy(nint handle) => DestroyIcon(handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}
