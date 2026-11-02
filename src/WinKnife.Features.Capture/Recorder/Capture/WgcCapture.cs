// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace WinKnife.Features.Capture;

/// <summary>A captured frame already cropped to the take size: BGRA bytes plus a timestamp on the capture clock.</summary>
internal sealed record CapturedFrame(byte[] Bgra, TimeSpan Time);

/// <summary>Windows.Graphics.Capture of a monitor (cropped to the picked rectangle) or a window, delivering BGRA frames on a pool thread.</summary>
internal sealed class WgcCapture : IDisposable
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);

        nint CreateForMonitor(nint monitor, ref Guid iid);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out int level, out nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private static readonly Guid IidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IidDxgiDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private readonly GraphicsCaptureItem _item;
    private readonly RegionPick _pick;
    private readonly int _w;
    private readonly int _h;
    private readonly int _fps;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private TimeSpan _lastTime = TimeSpan.MinValue;
    private bool _disposed;

    public WgcCapture(RegionPick pick, int fps)
    {
        _pick = pick;
        _fps = fps;
        _item = CreateItem(pick);
        _w = pick.Window != 0 ? Math.Max(32, _item.Size.Width & ~1) : pick.PixelRect.Width;
        _h = pick.Window != 0 ? Math.Max(32, _item.Size.Height & ~1) : pick.PixelRect.Height;
        _item.Closed += (_, _) => Closed?.Invoke();
    }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public int Width => _w;

    public int Height => _h;

    public event Action<CapturedFrame>? FrameReady;

    public event Action? Closed;

    private static GraphicsCaptureItem CreateItem(RegionPick pick)
    {
        const string classId = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(classId, classId.Length, out var hs);
        var iid = typeof(IGraphicsCaptureItemInterop).GUID;
        var hr = RoGetActivationFactory(hs, ref iid, out var factory);
        WindowsDeleteString(hs);
        Marshal.ThrowExceptionForHR(hr);
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
        var itemIid = IidItem;
        var ptr = pick.Window != 0 ? interop.CreateForWindow(pick.Window, ref itemIid) : interop.CreateForMonitor(pick.Monitor.Handle, ref itemIid);
        return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
    }

    private static IDirect3DDevice CreateDevice()
    {
        var hr = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var ctx);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var iid = IidDxgiDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref iid, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(dxgi);
            }
        }
        finally
        {
            Marshal.Release(ctx);
            Marshal.Release(device);
        }
    }

    public void Start()
    {
        var device = CreateDevice();
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _item.Size);
        _pool.FrameArrived += OnFrame;
        _session = _pool.CreateCaptureSession(_item);
        try
        {
            _session.IsCursorCaptureEnabled = false;
        }
        catch
        {
            // older builds
        }

        try
        {
            _session.GetType().GetProperty("IsBorderRequired")?.SetValue(_session, false);
        }
        catch
        {
            // needs the borderless capability; the OS draws its border outside the captured pixels
        }

        _session.StartCapture();
    }

    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            var t = frame.SystemRelativeTime;
            if (_lastTime != TimeSpan.MinValue && (t - _lastTime).TotalSeconds < 0.9 / _fps)
            {
                return;
            }

            _lastTime = t;
            using var sb = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().GetAwaiter().GetResult();
            var fw = sb.PixelWidth;
            var fh = sb.PixelHeight;
            var src = new byte[fw * fh * 4];
            sb.CopyToBuffer(src.AsBuffer());
            var dst = new byte[_w * _h * 4];
            var ox = _pick.Window != 0 ? 0 : _pick.PixelRect.X;
            var oy = _pick.Window != 0 ? 0 : _pick.PixelRect.Y;
            var copyW = Math.Min(_w, fw - ox);
            var copyH = Math.Min(_h, fh - oy);
            if (copyW > 0 && copyH > 0)
            {
                for (var y = 0; y < copyH; y++)
                {
                    Buffer.BlockCopy(src, ((oy + y) * fw + ox) * 4, dst, y * _w * 4, copyW * 4);
                }
            }

            FrameReady?.Invoke(new CapturedFrame(dst, t));
        }
        catch (Exception ex)
        {
            CaptureHost.Log("frame failed", ex);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            _session?.Dispose();
            _pool?.Dispose();
        }
        catch
        {
            // already torn down
        }
    }
}
