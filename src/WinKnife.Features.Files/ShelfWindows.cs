// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;
using Brushes = System.Windows.Media.Brushes;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace WinKnife.Features.Files;

/// <summary>What the shelf windows ask of the running feature.</summary>
internal interface IShelfHost
{
    ShelfStore Store { get; }

    int ItemCount { get; }

    IContributionServices Services { get; }

    /// <summary>Session-only "keep open" pin of the floating shelf.</summary>
    bool Pinned { get; set; }

    void Summon();

    void Toggle();

    void OnDragOut(IReadOnlyList<string> ids, DragDropEffects effect, bool windowPinned, Action closeWindow);

    /// <summary>The floating card went away: the docked capsule may take over again.</summary>
    void FloatingHidden();
}

/// <summary>Shared behaviour of the windows that host the shelf card.</summary>
internal abstract class ShelfWindowBase : OverlayWindow, IShelfActions
{
    protected ShelfWindowBase(IShelfHost host)
    {
        Host = host;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
    }

    protected IShelfHost Host { get; }

    public ShelfStore Store => Host.Store;

    public IContributionServices Services => Host.Services;

    public virtual bool IsPinned => Host.Pinned;

    public bool IsDropTargeted { get; set; }

    public bool PointerInside { get; private set; }

    public virtual void TogglePin()
    {
        Host.Pinned = !Host.Pinned;
        OnPinChanged();
    }

    protected virtual void OnPinChanged()
    {
    }

    public abstract void Dismiss();

    public void MoveWindow()
    {
        try
        {
            DragMove();
            OnMoved();
        }
        catch (InvalidOperationException)
        {
            // the button was released already
        }
    }

    protected virtual void OnMoved()
    {
    }

    public void PointerInsidePanel(bool inside) => PointerInside = inside;

    public void DragOutFinished(IReadOnlyList<string> ids, DragDropEffects effect) =>
        Host.OnDragOut(ids, effect, IsPinned, () => Dismiss());

    public void Share(IReadOnlyList<string> paths, FrameworkElement anchor) => ShelfShare.Share(Handle != 0 ? Handle : new WindowInteropHelper(this).EnsureHandle(), paths);

    /// <summary>Physical bounds of the window (0 when not created).</summary>
    public bool TryGetBounds(out RECT rect)
    {
        rect = default;
        return Handle != 0 && NativeMethods.GetWindowRect(Handle, out rect);
    }
}

/// <summary>The floating shelf card: summoned by the shortcut, by shaking the pointer during a drag, or by the screen edge.</summary>
internal sealed class ShelfFloatingWindow : ShelfWindowBase
{
    private ShelfPanel? _panel;

    public ShelfFloatingWindow(IShelfHost host)
        : base(host)
    {
        _panel = new ShelfPanel(this, docked: false) { Margin = new Thickness(20) };
        Content = _panel;
        Store.Changed += OnChanged;
        Closed += (_, _) => Store.Changed -= OnChanged;
    }

    protected override void OnPinChanged() => _panel?.RefreshHeader();

    private void OnChanged()
    {
        // Content size changes with the item count; keep the card where it is.
    }

    public override void Dismiss()
    {
        if (Services.App.Get<bool>("shelfClearOnClose"))
        {
            Store.ClearAll();
        }

        Host.Pinned = false;
        HideShelf();
    }

    /// <summary>Hides without clearing (automatic hiding keeps the items).</summary>
    public void HideShelf()
    {
        SavePosition();
        if (IsVisible)
        {
            Hide();
            Host.FloatingHidden();
        }
    }

    private void SavePosition()
    {
        if (IsVisible && TryGetBounds(out var rect))
        {
            Services.App.Set("shelfWindowX", rect.Left);
            Services.App.Set("shelfWindowY", rect.Top);
        }
    }

    /// <summary>Shows the card near the pointer, or at the remembered place.</summary>
    public void ShowAt(bool nearPointer, int? edgeSide = null)
    {
        var cursor = Screens.CursorPosition();
        var area = Screens.FromPoint(cursor);
        var scale = WinUtil.PrepareOnMonitorAt(this, (int)cursor.X, (int)cursor.Y);
        var size = WinUtil.DesiredSize(this);
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);
        var work = area.WorkArea;
        int x;
        int y;
        var savedX = Services.App.Get("shelfWindowX", int.MinValue);
        var savedY = Services.App.Get("shelfWindowY", int.MinValue);
        if (edgeSide is { } side)
        {
            x = side < 0 ? (int)work.Left + (int)(8 * scale) : (int)work.Right - width - (int)(8 * scale);
            y = (int)Math.Clamp(cursor.Y - height / 2.0, work.Top + 8 * scale, work.Bottom - height - 8 * scale);
        }
        else if (nearPointer)
        {
            x = (int)Math.Clamp(cursor.X - width / 2.0, work.Left + 8 * scale, work.Right - width - 8 * scale);
            y = (int)Math.Clamp(cursor.Y + 12 * scale, work.Top + 8 * scale, work.Bottom - height - 8 * scale);
        }
        else if (savedX != int.MinValue && savedY != int.MinValue && OnAnyScreen(savedX, savedY))
        {
            x = savedX;
            y = savedY;
        }
        else
        {
            x = (int)(work.Left + (work.Width - width) / 2);
            y = (int)(work.Top + 24 * scale);
        }

        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        if (!IsVisible)
        {
            Opacity = 0;
            ShowNoActivate();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.15)));
        }
    }

    private static bool OnAnyScreen(int x, int y)
    {
        var area = Screens.FromPoint(new System.Windows.Point(x + 40, y + 40));
        return x + 40 >= area.Bounds.Left && x + 40 < area.Bounds.Right && y + 40 >= area.Bounds.Top && y + 40 < area.Bounds.Bottom;
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePosition();
        base.OnClosed(e);
    }
}

/// <summary>
/// The docked shelf: a collapsed capsule next to the tray icons (or a badge at the top centre) that expands into the card and
/// shrinks back; it is also a drop target and stays while the shelf holds items.
/// </summary>
internal sealed class ShelfDockedWindow : ShelfWindowBase
{
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _caught;
    private bool _expanded;
    private DateTime _nearSince = DateTime.MaxValue;
    private bool _justCaught;
    private int _lastCount;

    public ShelfDockedWindow(IShelfHost host)
        : base(host)
    {
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input, OnTick, Dispatcher);
        _tick.Stop();
        DispatcherTimer? caught = null;
        caught = new DispatcherTimer(TimeSpan.FromSeconds(1.2), DispatcherPriority.Background, (_, _) =>
        {
            caught!.Stop();
            _justCaught = false;
            Render();
        }, Dispatcher);
        _caught = caught;
        _caught.Stop();
        _lastCount = Store.LeafCount;
        Store.Changed += OnStoreChanged;
        Closed += (_, _) => Store.Changed -= OnStoreChanged;
        Render();
    }

    public bool IsExpanded => _expanded;

    public bool DragInFlight { get; set; }

    public override bool IsPinned => false;

    private bool TopCenter => Services.App.Get<string>("shelfDockPlacement") == "topCenter";

    private void OnStoreChanged()
    {
        var count = Store.LeafCount;
        if (count > _lastCount && !_expanded)
        {
            _justCaught = true;
            _caught.Stop();
            _caught.Start();
        }

        _lastCount = count;
        if (!_expanded)
        {
            Render();
        }
    }

    public override void Dismiss() => Collapse();

    public void Expand()
    {
        if (_expanded)
        {
            return;
        }

        _expanded = true;
        Render();
        Reposition();
        _tick.Start();
    }

    public void Collapse()
    {
        if (!_expanded)
        {
            return;
        }

        _expanded = false;
        if (Services.App.Get<bool>("shelfClearOnClose") == false)
        {
            // collapsing keeps the items
        }

        Render();
        Reposition();
        _tick.Stop();
    }

    /// <summary>True when the window should exist now (a drag is in flight or the shelf holds something).</summary>
    public bool Wanted => DragInFlight || Store.Items.Count > 0 || _expanded;

    public void Present()
    {
        if (!Wanted)
        {
            Conceal();
            return;
        }

        Render();
        Reposition();
        if (!IsVisible)
        {
            Opacity = 0;
            ShowNoActivate();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.13)));
        }
    }

    public void Conceal()
    {
        _expanded = false;
        _tick.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Render()
    {
        (Content as ShelfPanel)?.Detach();
        if (_expanded)
        {
            var panel = new ShelfPanel(this, docked: true) { Margin = new Thickness(10) };
            Content = panel;
        }
        else
        {
            var pill = new ShelfPill(this, Expand, TopCenter) { Margin = new Thickness(10) };
            if (_justCaught)
            {
                pill.Child = CaughtTick();
            }

            Content = pill;
        }
    }

    private static UIElement CaughtTick()
    {
        var tick = UiKit.Icon(G.CheckCircle, 16, ThemeKeys.Green, FontWeights.SemiBold);
        tick.Margin = new Thickness(2, 0, 2, 0);
        return tick;
    }

    /// <summary>Places the window under the tray icons or at the top centre of the display with the pointer.</summary>
    public void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var cursor = Screens.CursorPosition();
        var area = Screens.FromPoint(cursor);
        int x;
        int y;
        double scale;
        if (TopCenter)
        {
            scale = WinUtil.PrepareOnMonitorAt(this, (int)cursor.X, (int)cursor.Y);
            var width = WinUtil.DesiredSize(this).Width * scale;
            x = (int)(area.Bounds.Left + (area.Bounds.Width - width) / 2);
            y = (int)(area.WorkArea.Top + 4 * scale - 10 * scale);
        }
        else
        {
            var tray = WinApi.FindWindowW("Shell_TrayWnd", null);
            RECT trayRect = default;
            RECT notify = default;
            if (tray != 0 && NativeMethods.GetWindowRect(tray, out trayRect))
            {
                var inner = WinApi.FindWindowExW(tray, 0, "TrayNotifyWnd", null);
                notify = trayRect;
                if (inner != 0)
                {
                    NativeMethods.GetWindowRect(inner, out notify);
                }
            }
            else
            {
                trayRect = new RECT { Left = (int)area.WorkArea.Left, Right = (int)area.WorkArea.Right, Top = (int)area.WorkArea.Bottom, Bottom = (int)area.WorkArea.Bottom };
                notify = trayRect;
            }

            scale = WinUtil.PrepareOnMonitorAt(this, trayRect.Left + 1, trayRect.Top + 1);
            var size = WinUtil.DesiredSize(this);
            var width = (int)Math.Ceiling(size.Width * scale);
            var height = (int)Math.Ceiling(size.Height * scale);
            var anchorCenter = (notify.Left + notify.Right) / 2;
            var screen = Screens.FromPoint(new System.Windows.Point(trayRect.Left + 1, trayRect.Top + 1));
            if (trayRect.Width >= trayRect.Height)
            {
                x = (int)Math.Clamp(anchorCenter - width / 2.0, screen.Bounds.Left + 4, screen.Bounds.Right - width - 4);
                var top = trayRect.Top > screen.Bounds.Top + 100;
                y = top ? trayRect.Top - height - (int)(2 * scale) : trayRect.Bottom + (int)(2 * scale);
            }
            else
            {
                var right = trayRect.Left > screen.Bounds.Left + 100;
                x = right ? trayRect.Left - width : trayRect.Right;
                y = (int)Math.Clamp(notify.Top - height / 2.0, screen.Bounds.Top + 4, screen.Bounds.Bottom - height - 4);
            }
        }

        NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Rect of the window in physical pixels (inflated by <paramref name="margin"/>).</summary>
    public bool IsNear(System.Windows.Point cursor, int margin)
    {
        if (!TryGetBounds(out var r))
        {
            return false;
        }

        return cursor.X >= r.Left - margin && cursor.X <= r.Right + margin && cursor.Y >= r.Top - margin && cursor.Y <= r.Bottom + margin;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // An expanded docked shelf retreats once the pointer is well away and nothing is being dragged.
        if (!_expanded)
        {
            return;
        }

        var cursor = Screens.CursorPosition();
        if (DragInFlight || IsDropTargeted || PointerInside || IsNear(cursor, 32))
        {
            return;
        }

        Collapse();
        if (!Wanted)
        {
            Conceal();
        }
    }
}

/// <summary>
/// A tiny near-invisible drop target shown for an instant under the pointer once a drag looks like it could carry content. If it
/// receives <c>DragEnter</c> an OLE drag with data is in progress (title-bar window drags never raise it).
/// </summary>
internal sealed class DragProbeWindow : Window
{
    public DragProbeWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Width = 6;
        Height = 6;
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            Entered?.Invoke(e);
        };
        DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
    }

    /// <summary>Raised on DragEnter (with the drag's data).</summary>
    public event Action<DragEventArgs>? Entered;

    public void ShowAt(System.Windows.Point cursor)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var scale = WinUtil.PrepareOnMonitorAt(this, (int)cursor.X - 3, (int)cursor.Y - 3);
        var half = (int)(3 * scale);
        NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, (int)cursor.X - half, (int)cursor.Y - half, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        if (!IsVisible)
        {
            ShowActivated = false;
            Show();
            WinUtil.MakeToolWindow(this, noActivate: true);
        }
    }
}

/// <summary>Windows Share UI for files (the shelf's Share button).</summary>
internal static class ShelfShare
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        [PreserveSig]
        int GetForWindow(nint appWindow, ref Guid riid, out nint dataTransferManager);

        [PreserveSig]
        int ShowShareUIForWindow(nint appWindow);
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(nint hstring);

    private static readonly Guid InteropIid = new("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
    private static readonly Guid ManagerIid = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    public static void Share(nint hwnd, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            const string className = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            WindowsCreateString(className, className.Length, out var hstring);
            try
            {
                var iid = InteropIid;
                if (RoGetActivationFactory(hstring, ref iid, out var factoryPtr) != 0 || factoryPtr == 0)
                {
                    return;
                }

                var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                var managerIid = ManagerIid;
                if (interop.GetForWindow(hwnd, ref managerIid, out var managerPtr) != 0 || managerPtr == 0)
                {
                    return;
                }

                var manager = WinRT.MarshalInspectable<Windows.ApplicationModel.DataTransfer.DataTransferManager>.FromAbi(managerPtr);
                Windows.Foundation.TypedEventHandler<Windows.ApplicationModel.DataTransfer.DataTransferManager, Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs>? handler = null;
                handler = async (sender, args) =>
                {
                    var deferral = args.Request.GetDeferral();
                    try
                    {
                        var items = new List<Windows.Storage.IStorageItem>();
                        foreach (var path in paths)
                        {
                            try
                            {
                                if (Directory.Exists(path))
                                {
                                    items.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path));
                                }
                                else
                                {
                                    items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                                }
                            }
                            catch (Exception)
                            {
                                // unreadable item
                            }
                        }

                        args.Request.Data.Properties.Title = Path.GetFileName(paths[0]);
                        args.Request.Data.SetStorageItems(items);
                    }
                    finally
                    {
                        deferral.Complete();
                        manager.DataRequested -= handler;
                    }
                };
                manager.DataRequested += handler;
                interop.ShowShareUIForWindow(hwnd);
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }
        catch (Exception)
        {
            // Share UI unavailable: nothing else to do
        }
    }
}
