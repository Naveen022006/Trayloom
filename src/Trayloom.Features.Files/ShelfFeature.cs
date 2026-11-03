// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Trayloom.Features.Files;

/// <summary>
/// The Shelf: a floating spot to gather files, images, links and text, then drag them anywhere later. Opened by the shortcut,
/// by shaking the pointer during a drag, by the screen edge, or docked next to the tray icons while you drag.
/// </summary>
public sealed class ShelfFeature : IFeature, IShelfHost, IShelfService
{
    private const double ShakeWindowSeconds = 0.5;
    private const double ShakeTravel = 220;
    private const int ShakeStep = 6;
    private const int ShakeReversals = 3;
    private const int EdgeTrigger = 200;
    private const int EdgeRetreat = 330;

    private IFeatureContext? _context;
    private ShelfStore? _store;
    private ShelfFloatingWindow? _floating;
    private ShelfDockedWindow? _docked;
    private DragProbeWindow? _probe;
    private IDisposable? _mouse;
    private DispatcherTimer? _tick;
    private DispatcherTimer? _afterDrag;
    private DispatcherTimer? _probeTimeout;

    // gesture state (UI thread)
    private bool _gesture;
    private System.Windows.Point _down;
    private bool _moved;
    private bool _excluded;
    private bool _probeDone;
    private bool _contentDrag;
    private DateTime _lastSummon = DateTime.MinValue;
    private readonly List<(DateTime At, int X)> _samples = [];
    private DateTime _edgeSince = DateTime.MaxValue;
    private bool _edgeShown;
    private int _edgeSide;
    private DateTime _dockNear = DateTime.MaxValue;

    public string Id => "shelf";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", Strings.Shelf.OpenShelf, () => Toggle(), ""),
        new FeatureAction("clear", Strings.Shelf.ClearAll, () => _store?.ClearAll(), ""),
    ];

    ShelfStore IShelfHost.Store => _store!;

    IContributionServices IShelfHost.Services => _context!;

    public int ItemCount => _store?.LeafCount ?? 0;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<string> Paths =>
        _store is null ? [] : [.. _store.Items.SelectMany(i => i.Leaves()).Where(l => l.Kind == ShelfKinds.File && l.Path is not null).Select(l => l.Path!)];

    /// <inheritdoc />
    public int Add(IEnumerable<string> paths)
    {
        var added = _store?.AddFiles(paths) ?? 0;
        if (added > 0 && Enabled)
        {
            RefreshDocked();
        }

        return added;
    }

    /// <inheritdoc />
    public void Remove(IEnumerable<string> paths)
    {
        if (_store is null)
        {
            return;
        }

        var set = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _store.Remove(_store.Items.SelectMany(i => i.Leaves()).Where(l => l.Kind == ShelfKinds.File && l.Path is not null && set.Contains(l.Path)).Select(l => l.Id).ToList());
    }

    /// <inheritdoc />
    public void Clear() => _store?.ClearAll();

    public bool Pinned { get; set; }

    internal static void RegisterDefaults(IAppSettings app)
    {
        app.Register("shelfEnabled", false);
        app.Register("shelfShortcutEnabled", true);
        app.Register("shelfShortcutAddsFinderSelection", false);
        app.Register("shelfShakeToOpen", true);
        app.Register("shelfDropZoneEnabled", true);
        app.Register("shelfDockPlacement", "menuBar");
        app.Register("shelfEdgeDragEnabled", false);
        app.Register("shelfCloseAfterDrop", false);
        app.Register("shelfRemoveAfterDrop", true);
        app.Register("shelfClearOnClose", false);
        app.Register("shelfAutomaticExclusions", new List<string>());
        app.Register("notchShelf", true);
        app.Register("panelControlShelf", true);
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        RegisterDefaults(context.App);
        _store = new ShelfStore(context.DataDirectory, context.Log, context.Ui);
        _store.Changed += OnStoreChanged;
        context.Shortcuts.Bind("shelf", () => OnShortcut());
        context.Services.Provide<IShelfService>(this);
        context.App.Changed += OnSettingChanged;
        Hub.Shelf = this;
        Hub.Settings = context.App;
        Apply();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSettingChanged;
            _context.Services.Withdraw<IShelfService>(this);
        }

        if (ReferenceEquals(Hub.Shelf, this))
        {
            Hub.Shelf = null;
        }

        StopMonitor();
        _afterDrag?.Stop();
        _afterDrag = null;
        var floating = _floating;
        var docked = _docked;
        _floating = null;
        _docked = null;
        floating?.Close();
        docked?.Close();
        if (_store is not null)
        {
            _store.Changed -= OnStoreChanged;
            _store.Flush();
            _store = null;
        }

        _context = null;
    }

    private bool Enabled => _context?.App.Get<bool>("shelfEnabled") ?? false;

    private IAppSettings App => _context!.App;

    private void OnSettingChanged(string key)
    {
        if (key.StartsWith("shelf", StringComparison.Ordinal) || key == "notchEnabled" || key == "notchShelf")
        {
            _context?.Ui.BeginInvoke(Apply);
        }
    }

    private void OnStoreChanged()
    {
        RefreshDocked();
        Changed?.Invoke();
    }

    public void FloatingHidden() => RefreshDocked();

    private void Apply()
    {
        if (_context is null)
        {
            return;
        }

        if (!Enabled)
        {
            StopMonitor();
            _floating?.HideShelf();
            _docked?.Conceal();
            return;
        }

        var wantsMonitor = App.Get<bool>("shelfShakeToOpen") || App.Get<bool>("shelfDropZoneEnabled") || App.Get<bool>("shelfEdgeDragEnabled");
        if (wantsMonitor)
        {
            StartMonitor();
        }
        else
        {
            StopMonitor();
        }

        RefreshDocked();
    }

    // ---------------------------------------------------------------------------------------------- summon

    private void OnShortcut()
    {
        if (!Enabled)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (_floating is { IsVisible: true } && !(App.Get<bool>("shelfShortcutAddsFinderSelection") && ExplorerShell.IsExplorerWindow(foreground) && ExplorerShell.Selection(foreground).Count > 0))
        {
            _floating.HideShelf();
            Pinned = false;
            RefreshDocked();
            return;
        }

        if (App.Get<bool>("shelfShortcutAddsFinderSelection") && ExplorerShell.IsExplorerWindow(foreground))
        {
            var selection = ExplorerShell.Selection(foreground);
            if (selection.Count > 0)
            {
                _store?.AddFiles(selection);
            }
        }

        Summon();
    }

    public void Summon()
    {
        if (_context is null || _store is null || !Enabled)
        {
            return;
        }

        try
        {
            _floating ??= new ShelfFloatingWindow(this);
            _docked?.Conceal();
            _floating.ShowAt(nearPointer: false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("Could not show the shelf", ex);
        }
    }

    public void Toggle()
    {
        if (_floating is { IsVisible: true })
        {
            _floating.HideShelf();
            Pinned = false;
            RefreshDocked();
        }
        else
        {
            Summon();
        }
    }

    private void SummonNearPointer(int? edgeSide = null)
    {
        if (_context is null || _store is null)
        {
            return;
        }

        try
        {
            _floating ??= new ShelfFloatingWindow(this);
            _docked?.Conceal();
            _floating.ShowAt(nearPointer: edgeSide is null, edgeSide);
        }
        catch (Exception ex)
        {
            _context.Log.Error("Could not show the shelf", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------- drag-out rules

    public void OnDragOut(IReadOnlyList<string> ids, DragDropEffects effect, bool windowPinned, Action closeWindow)
    {
        var store = _store;
        if (store is null || _context is null)
        {
            return;
        }

        if (effect.HasFlag(DragDropEffects.Move) || App.Get<bool>("shelfRemoveAfterDrop"))
        {
            // Pinned items always stay; items accepted by another app leave the shelf.
            store.Remove(ids, includePinned: false);
        }

        if (App.Get<bool>("shelfCloseAfterDrop") && !windowPinned)
        {
            closeWindow();
        }
    }

    // ---------------------------------------------------------------------------------------------- docked drop zone

    private void RefreshDocked()
    {
        if (_context is null || !Enabled)
        {
            return;
        }

        var wanted = App.Get<bool>("shelfDropZoneEnabled");
        if (!wanted || _floating is { IsVisible: true })
        {
            _docked?.Conceal();
            return;
        }

        if (_docked is null && (_store?.Items.Count ?? 0) == 0)
        {
            return;
        }

        _docked ??= new ShelfDockedWindow(this);
        _docked.Present();
    }

    // ---------------------------------------------------------------------------------------------- drag monitor

    private void StartMonitor()
    {
        if (_mouse is not null || _context is null)
        {
            return;
        }

        _mouse = _context.Hooks.SubscribeMouse(OnMouse);
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Input, OnTick, _context.Ui);
        _tick.Stop();
    }

    private void StopMonitor()
    {
        _mouse?.Dispose();
        _mouse = null;
        _tick?.Stop();
        _tick = null;
        _probeTimeout?.Stop();
        var probe = _probe;
        _probe = null;
        probe?.Close();
        _gesture = false;
        _contentDrag = false;
    }

    /// <summary>Hook thread: records nothing but posts the gesture boundaries.</summary>
    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromTrayloom)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.LeftDown:
                var point = new System.Windows.Point(e.X, e.Y);
                _context?.Ui.BeginInvoke(() => BeginGesture(point));
                break;
            case MouseEventKind.LeftUp:
                _context?.Ui.BeginInvoke(EndGesture);
                break;
        }

        return false;
    }

    private void BeginGesture(System.Windows.Point down)
    {
        if (_context is null || !Enabled)
        {
            return;
        }

        _gesture = true;
        _down = down;
        _moved = false;
        _probeDone = false;
        _contentDrag = false;
        _samples.Clear();
        _edgeSince = DateTime.MaxValue;
        _dockNear = DateTime.MaxValue;
        _excluded = IsExcludedSource(down);
        _tick?.Start();
    }

    private bool IsExcludedSource(System.Windows.Point point)
    {
        try
        {
            var list = App.Get<List<string>>("shelfAutomaticExclusions");
            if (list is null || list.Count == 0)
            {
                return false;
            }

            var hwnd = WinApi.WindowFromPoint(new POINT { X = (int)point.X, Y = (int)point.Y });
            var root = hwnd == 0 ? 0 : WinApi.GetAncestor(hwnd, WinApi.GA_ROOT);
            var path = WinApi.GetWindowProcessPath(root == 0 ? hwnd : root, out _);
            return path is not null && Lists.Contains(list, Path.GetFileName(path).ToLowerInvariant(), path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly nint[] SystemCursors = LoadSystemCursors();

    private static nint[] LoadSystemCursors()
    {
        var ids = new[] { 32512, 32513, 32514, 32515, 32516, 32631, 32640, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };
        return [.. ids.Select(id => LoadCursor(0, id))];
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, int cursorName);

    private static bool CursorLooksLikeDrag()
    {
        var info = new CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        if (!WinApi.GetCursorInfo(ref info) || (info.flags & 1) == 0 || info.hCursor == 0)
        {
            return false;
        }

        return !SystemCursors.Contains(info.hCursor);
    }

    private static bool WindowIsMoving()
    {
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        return WinApi.GetGUIThreadInfo(0, ref info) && (info.flags & WinApi.GUI_INMOVESIZE) != 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_gesture || _context is null)
        {
            _tick?.Stop();
            return;
        }

        var cursor = Screens.CursorPosition();
        if (!_moved)
        {
            if (Math.Abs(cursor.X - _down.X) + Math.Abs(cursor.Y - _down.Y) < 6)
            {
                return;
            }

            _moved = true;
        }

        if (!_contentDrag)
        {
            if (!_probeDone && !WindowIsMoving() && CursorLooksLikeDrag())
            {
                ShowProbe(cursor);
            }

            if (_probe is { IsVisible: true })
            {
                _probe.ShowAt(cursor);
            }

            return;
        }

        if (_excluded)
        {
            return;
        }

        if (App.Get<bool>("shelfShakeToOpen"))
        {
            DetectShake(cursor);
        }

        if (App.Get<bool>("shelfEdgeDragEnabled"))
        {
            EdgeLogic(cursor);
        }

        if (App.Get<bool>("shelfDropZoneEnabled"))
        {
            DockLogic(cursor);
        }
    }

    private void ShowProbe(System.Windows.Point cursor)
    {
        _probeDone = true;
        _probe ??= CreateProbe();
        _probe.ShowAt(cursor);
        _probeTimeout?.Stop();
        _probeTimeout = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Input, (_, _) =>
        {
            _probeTimeout?.Stop();
            if (!_contentDrag)
            {
                _probe?.Hide();
            }
        }, _context!.Ui);
        _probeTimeout.Start();
    }

    private DragProbeWindow CreateProbe()
    {
        var probe = new DragProbeWindow();
        probe.Entered += args =>
        {
            if (args.Data.GetDataPresent("Trayloom.ShelfItems"))
            {
                // A drag out of the shelf itself is no reason to summon anything.
                _context?.Ui.BeginInvoke(() => _probe?.Hide());
                return;
            }

            _contentDrag = true;
            _samples.Clear();
            _context?.Ui.BeginInvoke(() =>
            {
                _probe?.Hide();
                if (!_excluded && App.Get<bool>("shelfDropZoneEnabled") && !_floatingVisible)
                {
                    _docked ??= new ShelfDockedWindow(this);
                    _docked.DragInFlight = true;
                    _docked.Present();
                }
            });
        };
        return probe;
    }

    private bool _floatingVisible => _floating is { IsVisible: true };

    private void EndGesture()
    {
        if (!_gesture)
        {
            return;
        }

        var wasContent = _contentDrag;
        _gesture = false;
        _contentDrag = false;
        _tick?.Stop();
        _probeTimeout?.Stop();
        _probe?.Hide();
        if (!wasContent)
        {
            return;
        }

        // The drop (if any) lands around the mouse-up; settle before deciding what stays on screen.
        _afterDrag?.Stop();
        _afterDrag = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) =>
        {
            _afterDrag?.Stop();
            if (_docked is not null)
            {
                _docked.DragInFlight = false;
            }

            if (_edgeShown)
            {
                _edgeShown = false;
                if (!Pinned && !(_floating?.PointerInside ?? false))
                {
                    _floating?.HideShelf();
                }
            }

            RefreshDocked();
        }, _context!.Ui);
        _afterDrag.Start();
    }

    // ---------------------------------------------------------------------------------------------- shake

    private void DetectShake(System.Windows.Point cursor)
    {
        var now = DateTime.UtcNow;
        _samples.Add((now, (int)cursor.X));
        _samples.RemoveAll(s => (now - s.At).TotalSeconds > ShakeWindowSeconds);
        if (_samples.Count < 5 || (now - _lastSummon).TotalSeconds < 1)
        {
            return;
        }

        var reversals = 0;
        double travel = 0;
        var direction = 0;
        for (var i = 1; i < _samples.Count; i++)
        {
            var step = _samples[i].X - _samples[i - 1].X;
            travel += Math.Abs(step);
            if (Math.Abs(step) <= ShakeStep)
            {
                continue;
            }

            var sign = Math.Sign(step);
            if (direction != 0 && sign != direction)
            {
                reversals++;
            }

            direction = sign;
        }

        if (reversals >= ShakeReversals && travel > ShakeTravel)
        {
            _lastSummon = now;
            _samples.Clear();
            SummonNearPointer();
        }
    }

    // ---------------------------------------------------------------------------------------------- edge peek

    private void EdgeLogic(System.Windows.Point cursor)
    {
        var area = Screens.FromPoint(cursor);
        var bounds = area.Bounds;
        var work = area.WorkArea;
        var leftDistance = cursor.X - bounds.Left;
        var rightDistance = bounds.Right - cursor.X;
        var side = leftDistance <= rightDistance ? -1 : 1;
        var distance = side < 0 ? leftDistance : rightDistance;
        var seam = side < 0
            ? Screens.FromPoint(new System.Windows.Point(bounds.Left - 10, cursor.Y)).Bounds != bounds
            : Screens.FromPoint(new System.Windows.Point(bounds.Right + 10, cursor.Y)).Bounds != bounds;
        var taskbarSide = side < 0 ? work.Left > bounds.Left + 1 : work.Right < bounds.Right - 1;

        if (_edgeShown)
        {
            if (distance > EdgeRetreat || side != _edgeSide)
            {
                _edgeShown = false;
                if (!Pinned)
                {
                    _floating?.HideShelf();
                }

                RefreshDocked();
            }

            return;
        }

        if (distance <= EdgeTrigger && !seam && !taskbarSide && !_floatingVisible)
        {
            if (_edgeSince == DateTime.MaxValue)
            {
                _edgeSince = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - _edgeSince).TotalSeconds >= 0.15)
            {
                _edgeShown = true;
                _edgeSide = side;
                SummonNearPointer(side);
            }
        }
        else
        {
            _edgeSince = DateTime.MaxValue;
        }
    }

    // ---------------------------------------------------------------------------------------------- dock

    private void DockLogic(System.Windows.Point cursor)
    {
        var docked = _docked;
        if (docked is null || _floatingVisible || docked.IsExpanded)
        {
            return;
        }

        if (docked.IsNear(cursor, 16))
        {
            if (_dockNear == DateTime.MaxValue)
            {
                _dockNear = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - _dockNear).TotalSeconds >= 0.15)
            {
                docked.Expand();
            }
        }
        else
        {
            _dockNear = DateTime.MaxValue;
        }
    }
}
