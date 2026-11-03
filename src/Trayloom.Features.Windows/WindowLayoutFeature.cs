// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>
/// Shared handle on the running Window layout feature, published on the service hub so the settings page, the hosted panel and the
/// Command Bar actions (separate stateless classes) reach the same engine.
/// </summary>
internal sealed class WindowLayoutService
{
    public WindowLayoutService(WindowLayoutEngine engine, EdgeSnapController edge, WindowGestures gestures)
    {
        Engine = engine;
        Edge = edge;
        Gestures = gestures;
        engine.ResultPublished += r =>
        {
            LastResult = r;
            ResultChanged?.Invoke();
        };
    }

    public WindowLayoutEngine Engine { get; }

    public EdgeSnapController Edge { get; }

    public WindowGestures Gestures { get; }

    public LayoutResult? LastResult { get; private set; }

    public event Action? ResultChanged;

    /// <summary>Runs an action on the active window (called from the page's "try it" buttons, the panel and the Command Bar).</summary>
    public LayoutResult Run(LayoutAction action) => Engine.Apply(action, DevTarget());

    /// <summary>Developer aid (TRAYLOOM_DEV=1 only): a window handle in <c>layout-target.txt</c> is used instead of the foreground window, so tests never touch other windows.</summary>
    private static nint DevTarget()
    {
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") != "1")
        {
            return 0;
        }

        try
        {
            var file = System.IO.Path.Combine(AppPaths.Root, "layout-target.txt");
            return System.IO.File.Exists(file) && long.TryParse(System.IO.File.ReadAllText(file).Trim(), out var value) ? (nint)value : 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// Window layout (spec 05 A6): 41 placement actions with shortcuts, edge snapping with a zone map, move/resize by dragging with a
/// modifier chord, "Shortcut + pointer layout" ring, ignore list, gaps, and "Move pointer to next display".
/// </summary>
public sealed class WindowLayoutFeature : IFeature
{
    private IFeatureContext? _context;
    private WindowLayoutEngine? _engine;
    private EdgeSnapController? _edge;
    private WindowGestures? _gestures;
    private DirectionalRing? _ring;
    private WindowLayoutService? _service;
    private readonly List<IDisposable> _bindings = [];
    private Action<string>? _changed;

    public string Id => "window-layout";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        .. LayoutActions.All.Select(a => new FeatureAction("layout." + a.Raw, a.Title, () => _service?.Run(a), a.Glyph ?? "")),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        var app = context.App;
        Keys.Register(app);
        Ambient.App = app;
        context.Shortcuts.DefineRole(new ShortcutRole(
            Keys.DirectionalRole,
            "windowDirectionalShortcut",
            Strings.Directional,
            "window-layout",
            FeatureGroup.WindowsTaskbar,
            new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, Key.Space),
            [Keys.DirectionalEnabled],
            Section: Loc.T("Window layout"),
            HookBased: true));

        _engine = new WindowLayoutEngine(app, context.Log, context.Ui);
        _edge = new EdgeSnapController(app, _engine, context.Ui);
        _gestures = new WindowGestures(app, _engine, _edge, context.Ui);
        _ring = new DirectionalRing(app, context.Shortcuts, _engine, context.Ui);
        _service = new WindowLayoutService(_engine, _edge, _gestures);
        context.Services.Provide(_service);

        foreach (var action in LayoutActions.All)
        {
            _bindings.Add(context.Shortcuts.Bind(action.RoleId, () => RunShortcut(action)));
        }

        _bindings.Add(context.Shortcuts.Bind("pointerNextDisplay", MovePointerToNextDisplay));

        _changed = key =>
        {
            if (key is Keys.EdgeSnapEnabled or Keys.GestureEnabled or Keys.GestureModifiers or Keys.DirectionalEnabled)
            {
                context.Ui.BeginInvoke(Sync);
            }
        };
        app.Changed += _changed;
        Sync();
    }

    public void Stop()
    {
        if (_context is not null && _changed is not null)
        {
            _context.App.Changed -= _changed;
        }

        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        _ring?.Stop();
        _gestures?.Stop();
        _edge?.Stop();
        if (_service is not null)
        {
            _context?.Services.Withdraw(_service);
        }

        _engine?.Clear();
        _ring = null;
        _gestures = null;
        _edge = null;
        _engine = null;
        _service = null;
        _context = null;
    }

    private void Sync()
    {
        var app = _context?.App;
        if (app is null)
        {
            return;
        }

        if (app.Get<bool>(Keys.EdgeSnapEnabled))
        {
            _edge?.Start();
        }
        else
        {
            _edge?.Stop();
        }

        if (app.Get<bool>(Keys.GestureEnabled))
        {
            _gestures?.Refresh();
            _gestures?.Start(_context!.Hooks);
        }
        else
        {
            _gestures?.Stop();
        }

        if (app.Get<bool>(Keys.DirectionalEnabled))
        {
            _ring?.Start(_context!.Hooks);
        }
        else
        {
            _ring?.Stop();
        }
    }

    private void RunShortcut(LayoutAction action)
    {
        if (_engine is null || _engine.ForegroundIgnored())
        {
            return;
        }

        _engine.Apply(action);
    }

    /// <summary>Warps the pointer to the centre of the next display (left-to-right order, cycling). No special permission needed.</summary>
    private static void MovePointerToNextDisplay()
    {
        var monitors = WinUtil.Monitors();
        if (monitors.Count < 2)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var p);
        var current = NativeMethods.MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        var index = Math.Max(0, monitors.FindIndex(m => m.Handle == current));
        var next = monitors[(index + 1) % monitors.Count].Bounds;
        NativeMethods.SetCursorPos(next.Left + next.Width / 2, next.Top + next.Height / 2);
    }
}
