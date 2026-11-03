// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;

namespace Trayloom.Core;

/// <summary>
/// Maps Vorssaint's macOS shortcuts to Windows (ARCHITECTURE_V2): ⌘ becomes <b>Win</b> in multi-modifier global hotkeys
/// (⌃⌥⌘ = Ctrl+Alt+Win, ⌃⌘ = Ctrl+Win, ⇧⌥⌘ = Shift+Alt+Win), ⌥ = Alt, ⌃ = Ctrl, ⇧ = Shift. Combinations Windows
/// reserves fall back to Shift in place of Win.
/// </summary>
public static class HotkeyMap
{
    /// <summary>Builds a Windows hotkey from the macOS modifier glyphs (<c>⌃ ⌥ ⇧ ⌘</c>) and a key.</summary>
    public static Hotkey Mac(string macModifiers, Key key)
    {
        var mods = HotkeyModifiers.None;
        foreach (var c in macModifiers)
        {
            mods |= c switch
            {
                '⌃' => HotkeyModifiers.Control,
                '⌥' => HotkeyModifiers.Alt,
                '⇧' => HotkeyModifiers.Shift,
                '⌘' => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
        }

        return new Hotkey(mods, key);
    }

    /// <summary>True for combinations Windows owns (RegisterHotKey fails or the shell swallows them).</summary>
    public static bool IsReserved(Hotkey hotkey)
    {
        var m = hotkey.Modifiers;
        var k = hotkey.Key;
        if (m == HotkeyModifiers.Win)
        {
            return true; // Win + key is the shell's namespace
        }

        if (m == (HotkeyModifiers.Win | HotkeyModifiers.Shift) && k is Key.S or Key.M or Key.T or Key.C or Key.Left or Key.Right or Key.Up or Key.Down or Key.V)
        {
            return true;
        }

        if (m == (HotkeyModifiers.Win | HotkeyModifiers.Control) && k is Key.Left or Key.Right or Key.D or Key.F4 or Key.Enter)
        {
            return true;
        }

        return (m == HotkeyModifiers.Alt && k is Key.Tab or Key.F4 or Key.Escape or Key.Space)
            || (m == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && k is Key.Delete or Key.Tab)
            || (m == (HotkeyModifiers.Control | HotkeyModifiers.Shift) && k == Key.Escape);
    }

    /// <summary>The fallback used when <paramref name="hotkey"/> is reserved or taken: Win replaced by Shift (null when there is no Win to replace).</summary>
    public static Hotkey? Fallback(Hotkey hotkey)
    {
        if (!hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            return null;
        }

        // Ctrl+Win+X would become Ctrl+Shift+X, which many apps use (paste as plain text, reopen tab...): Ctrl+Alt+X stays out of the way.
        var replacement = (hotkey.Modifiers & ~HotkeyModifiers.Win) == HotkeyModifiers.Control ? HotkeyModifiers.Alt : HotkeyModifiers.Shift;
        var mods = (hotkey.Modifiers & ~HotkeyModifiers.Win) | replacement;
        var result = new Hotkey(mods, hotkey.Key);
        return IsReserved(result) ? null : result;
    }
}

/// <summary>Which hub group a shortcut role is listed under on the Keyboard shortcuts page.</summary>
/// <param name="Id">Role id (e.g. <c>keepAwake</c>).</param>
/// <param name="StorageKey">IAppSettings key of the saved shortcut (a <see cref="Hotkey"/>, null stored = cleared).</param>
/// <param name="Title">Row title.</param>
/// <param name="FeatureId">Catalog id that must be installed for the role to be listed (null = always).</param>
/// <param name="Group">Hub group the role is listed under.</param>
/// <param name="Default">Default Windows hotkey (null = none by default).</param>
/// <param name="EnableKeys">All must be true for the role to be "Active" (registered default values count).</param>
/// <param name="Section">Optional sub-header inside the group ("Screen capture", "Window layout", ...).</param>
/// <param name="HookBased">The feature implements it with its own keyboard hook; the service only stores the key and checks conflicts.</param>
/// <param name="AllowBare">Allow a key without modifier (e.g. F2).</param>
/// <param name="ReadOnly">Edited elsewhere (e.g. the Radial menu page); the row has no recorder.</param>
public sealed record ShortcutRole(
    string Id,
    string StorageKey,
    string Title,
    string? FeatureId,
    FeatureGroup Group,
    Hotkey? Default,
    IReadOnlyList<string> EnableKeys,
    string? Section = null,
    bool HookBased = false,
    bool AllowBare = false,
    bool ReadOnly = false);

/// <summary>Current state of one shortcut role.</summary>
/// <param name="Configured">What the user (or the default) configured; null = cleared.</param>
/// <param name="Effective">What is actually registered (differs from <paramref name="Configured"/> after a Win→Shift fallback).</param>
/// <param name="IsActive">All enable keys are on.</param>
/// <param name="IsRegistered">The OS accepted the registration (always true for hook-based roles and while nothing is bound).</param>
/// <param name="Problem">Text for the row ("Windows reserves ... using ...", "Windows rejected this shortcut. Choose another one.").</param>
public sealed record ShortcutState(Hotkey? Configured, Hotkey? Effective, bool IsActive, bool IsRegistered, string? Problem);

/// <summary>Result of <see cref="IShortcutService.TrySet"/>.</summary>
/// <param name="Ok">The shortcut was saved.</param>
/// <param name="Error">Message for the row when it was not (conflict, no modifier, OS refusal).</param>
public sealed record ShortcutSetResult(bool Ok, string? Error);

/// <summary>
/// Central registry of user-configurable global shortcuts. Features <see cref="Bind"/> a callback to a role; the
/// service reads the saved/default hotkey, registers it while the role is active, falls back from Win to Shift (Ctrl+Win to Ctrl+Alt) when
/// Windows reserves the combination, re-registers when the shortcut or an enable key changes, and reports conflicts
/// the Keyboard shortcuts page and the feature pages can show.
/// </summary>
public interface IShortcutService
{
    /// <summary>All roles (static table of spec 02a 6.2 + roles defined at runtime) in listing order.</summary>
    IReadOnlyList<ShortcutRole> Roles { get; }

    /// <summary>Finds a role.</summary>
    ShortcutRole? Role(string roleId);

    /// <summary>Defines (or replaces) a role at runtime (a feature with extra shortcuts, e.g. window-layout actions).</summary>
    void DefineRole(ShortcutRole role);

    /// <summary>The configured hotkey (saved value, else the default); null when cleared. Hook-based features read this.</summary>
    Hotkey? GetHotkey(string roleId);

    /// <summary>Configured / effective hotkey, active flag and problem text.</summary>
    ShortcutState GetState(string roleId);

    /// <summary>
    /// Binds <paramref name="callback"/> (UI thread) to the role. The hotkey is registered while the role is active
    /// and re-registered automatically. Dispose to unbind. Registrations made through a feature context are also
    /// disposed when the feature stops.
    /// </summary>
    IDisposable Bind(string roleId, Action callback);

    /// <summary>Saves a new shortcut (null clears it) after validating modifiers and conflicts.</summary>
    ShortcutSetResult TrySet(string roleId, Hotkey? hotkey);

    /// <summary>Restores the default shortcut.</summary>
    void Reset(string roleId);

    /// <summary>True when the role currently has its default shortcut.</summary>
    bool IsDefault(string roleId);

    /// <summary>Title of another role that uses <paramref name="hotkey"/> (null when free).</summary>
    string? FindConflict(Hotkey hotkey, string exceptRoleId);

    /// <summary>Raised with the role id when its shortcut, state or binding changed (any thread).</summary>
    event Action<string>? Changed;
}

/// <summary>Default <see cref="IShortcutService"/>.</summary>
public sealed class ShortcutService : IShortcutService
{
    private sealed class Binding
    {
        public required string RoleId { get; init; }

        public required Action Callback { get; init; }

        public IDisposable? Registration { get; set; }
    }

    private readonly IAppSettings _settings;
    private readonly IHotkeyService _hotkeys;
    private readonly ILog _log;
    private readonly List<ShortcutRole> _roles = [];
    private readonly Dictionary<string, ShortcutRole> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Binding>> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Hotkey? Effective, bool Registered, string? Problem)> _runtime = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates the service and defines the static roles.</summary>
    public ShortcutService(IAppSettings settings, IHotkeyService hotkeys, ILog log)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _log = log;
        foreach (var role in ShortcutRoles.CreateStatic())
        {
            AddRole(role);
        }

        _settings.Changed += OnSettingChanged;
    }

    /// <inheritdoc />
    public event Action<string>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<ShortcutRole> Roles
    {
        get
        {
            lock (_gate)
            {
                return [.. _roles];
            }
        }
    }

    /// <inheritdoc />
    public ShortcutRole? Role(string roleId)
    {
        lock (_gate)
        {
            return _byId.GetValueOrDefault(roleId);
        }
    }

    /// <inheritdoc />
    public void DefineRole(ShortcutRole role)
    {
        AddRole(role);
        Changed?.Invoke(role.Id);
    }

    /// <inheritdoc />
    public Hotkey? GetHotkey(string roleId)
    {
        var role = Role(roleId);
        if (role is null)
        {
            return null;
        }

        return _settings.IsSet(role.StorageKey) ? _settings.Get<Hotkey?>(role.StorageKey) : role.Default;
    }

    /// <inheritdoc />
    public ShortcutState GetState(string roleId)
    {
        var role = Role(roleId);
        var configured = GetHotkey(roleId);
        var active = role is not null && IsActive(role);
        lock (_gate)
        {
            if (_runtime.TryGetValue(roleId, out var rt))
            {
                return new ShortcutState(configured, rt.Effective, active, rt.Registered, rt.Problem);
            }
        }

        return new ShortcutState(configured, configured, active, true, null);
    }

    /// <inheritdoc />
    public IDisposable Bind(string roleId, Action callback)
    {
        var binding = new Binding { RoleId = roleId, Callback = callback };
        lock (_gate)
        {
            if (!_bindings.TryGetValue(roleId, out var list))
            {
                _bindings[roleId] = list = [];
            }

            list.Add(binding);
        }

        Rebind(roleId);
        return new Unbinder(this, binding);
    }

    /// <inheritdoc />
    public ShortcutSetResult TrySet(string roleId, Hotkey? hotkey)
    {
        var role = Role(roleId);
        if (role is null)
        {
            return new ShortcutSetResult(false, Loc.T("Unknown shortcut."));
        }

        if (hotkey is not null)
        {
            var hasModifier = hotkey.Modifiers != HotkeyModifiers.None;
            var functionKey = hotkey.Key is >= Key.F1 and <= Key.F24;
            if (!hasModifier && !(role.AllowBare || functionKey))
            {
                return new ShortcutSetResult(false, Loc.T("Use at least Control, Alt or Windows with a key."));
            }

            var other = FindConflict(hotkey, roleId);
            if (other is not null)
            {
                return new ShortcutSetResult(false, Loc.F("This shortcut is already used by {0}.", other));
            }

            if (!role.HookBased && !hotkey.Equals(GetHotkey(roleId)) && !hotkey.Equals(GetState(roleId).Effective))
            {
                if (!_hotkeys.TryRegister(hotkey, () => { }, out var probe))
                {
                    return new ShortcutSetResult(false, Loc.T("Nothing was captured. Windows or another app already uses that combination. Try another one."));
                }

                probe.Dispose();
            }
        }

        _settings.Set(role.StorageKey, hotkey);
        Rebind(roleId);
        return new ShortcutSetResult(true, null);
    }

    /// <inheritdoc />
    public void Reset(string roleId)
    {
        var role = Role(roleId);
        if (role is null)
        {
            return;
        }

        _settings.Reset(role.StorageKey);
        Rebind(roleId);
    }

    /// <inheritdoc />
    public bool IsDefault(string roleId)
    {
        var role = Role(roleId);
        return role is null || !_settings.IsSet(role.StorageKey) || Equals(GetHotkey(roleId), role.Default);
    }

    /// <inheritdoc />
    public string? FindConflict(Hotkey hotkey, string exceptRoleId)
    {
        foreach (var role in Roles)
        {
            if (role.Id != exceptRoleId && Equals(GetHotkey(role.Id), hotkey))
            {
                return role.Title;
            }
        }

        return null;
    }

    private void AddRole(ShortcutRole role)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(role.Id, out var existing))
            {
                _roles[_roles.IndexOf(existing)] = role;
            }
            else
            {
                _roles.Add(role);
            }

            _byId[role.Id] = role;
        }
    }

    private bool IsActive(ShortcutRole role) => role.EnableKeys.All(k => _settings.Get<bool>(k));

    private void OnSettingChanged(string key)
    {
        List<string> affected;
        lock (_gate)
        {
            affected = _roles.Where(r => r.StorageKey == key || r.EnableKeys.Contains(key)).Select(r => r.Id).ToList();
        }

        foreach (var id in affected)
        {
            Rebind(id);
        }
    }

    private void Rebind(string roleId)
    {
        var role = Role(roleId);
        if (role is null)
        {
            return;
        }

        List<Binding> bindings;
        lock (_gate)
        {
            bindings = _bindings.TryGetValue(roleId, out var list) ? [.. list] : [];
        }

        foreach (var b in bindings)
        {
            b.Registration?.Dispose();
            b.Registration = null;
        }

        var configured = GetHotkey(roleId);
        var effective = configured;
        var registered = true;
        string? problem = null;
        if (bindings.Count > 0 && configured is not null && IsActive(role) && !role.HookBased)
        {
            var candidate = configured;
            if (HotkeyMap.IsReserved(candidate) && HotkeyMap.Fallback(candidate) is { } reservedFallback)
            {
                problem = $"{candidate} is reserved by Windows. Using {reservedFallback} instead.";
                candidate = reservedFallback;
            }

            foreach (var b in bindings)
            {
                var cb = b.Callback;
                if (!_hotkeys.TryRegister(candidate, cb, out var reg) && HotkeyMap.Fallback(candidate) is { } second && problem is null)
                {
                    problem = $"{candidate} is taken. Using {second} instead.";
                    candidate = second;
                    _hotkeys.TryRegister(candidate, cb, out reg);
                }

                b.Registration = reg;
                if (reg is null)
                {
                    registered = false;
                    problem = Loc.T("Windows rejected this shortcut. Choose another one.");
                }
            }

            effective = candidate;
        }

        lock (_gate)
        {
            _runtime[roleId] = (effective, registered, problem);
        }

        if (problem is not null)
        {
            _log.Warn($"Shortcut {roleId}: {problem}");
        }

        Changed?.Invoke(roleId);
    }

    private void Unbind(Binding binding)
    {
        lock (_gate)
        {
            if (_bindings.TryGetValue(binding.RoleId, out var list))
            {
                list.Remove(binding);
            }
        }

        binding.Registration?.Dispose();
        binding.Registration = null;
    }

    private sealed class Unbinder(ShortcutService owner, Binding binding) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unbind(binding);
            }
        }
    }
}

/// <summary>The static role table of the Keyboard shortcuts page (spec 02a 6.2 / 6.1) with Windows defaults.</summary>
public static class ShortcutRoles
{
    private const string Cac = "⌃⌥⌘"; // ⌃⌥⌘

    /// <summary>Creates the static roles in listing order.</summary>
    public static IReadOnlyList<ShortcutRole> CreateStatic()
    {
        var list = new List<ShortcutRole>();
        const FeatureGroup W = FeatureGroup.WindowsTaskbar;
        const FeatureGroup Mk = FeatureGroup.MouseKeyboard;
        const FeatureGroup Cf = FeatureGroup.ClipboardFiles;
        const FeatureGroup So = FeatureGroup.Sound;
        const FeatureGroup En = FeatureGroup.EnergyDisplay;
        const FeatureGroup To = FeatureGroup.Tools;
        const FeatureGroup Is = FeatureGroup.Island;

        ShortcutRole R(string id, string title, string? feature, FeatureGroup group, Hotkey? def, string[] enable, string? section = null, bool hook = false, bool bare = false, bool ro = false) =>
            new(id, id + "Shortcut", title, feature, group, def, enable, section, hook, bare, ro);

        Hotkey Mac(string mods, Key key) => HotkeyMap.Mac(mods, key);
        var ctrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

        // Windows and taskbar
        list.Add(R("switcher", Loc.T("Apps"), "switcher", W, new Hotkey(HotkeyModifiers.Alt, Key.Tab), ["switcherEnabled"], hook: true));
        list.Add(R("switcherWindow", Loc.T("Windows"), "switcher", W, new Hotkey(HotkeyModifiers.Alt, Key.OemTilde), ["switcherEnabled"], hook: true));
        list.Add(R("switcherAllWindows", Loc.T("All windows"), "switcher", W, new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.Tab), ["switcherEnabled", "switcherAllWindowsShortcutEnabled"], hook: true));
        list.Add(R("pointerNextDisplay", Loc.T("Move pointer to next display"), "window-layout", W, Mac(Cac, Key.Z), ["pointerDisplayEnabled"]));
        foreach (var (id, title, def) in WindowLayoutActions(ctrlAlt))
        {
            list.Add(new ShortcutRole("windowLayout." + id, "windowLayoutShortcut." + id, title, "window-layout", W, def, ["windowLayoutShortcutsEnabled"], Section: Loc.T("Window layout")));
        }

        // Mouse and keyboard
        list.Add(R("snippetLibrary", Loc.T("Quick snippet menu"), "text-snippets", Mk, Mac(Cac, Key.L), ["snippetLibraryEnabled"]));

        // Clipboard and files
        list.Add(R("shelf", Loc.T("Shelf"), "shelf", Cf, Mac(Cac, Key.D), ["shelfEnabled", "shelfShortcutEnabled"]));
        list.Add(R("clipboardHistory", Loc.T("Clipboard"), "clipboard-history", Cf, Mac(Cac, Key.V), ["clipboardHistoryEnabled", "clipboardHistoryShortcutEnabled"]));
        list.Add(R("pastePlain", Loc.T("Paste as plain text"), "paste-plain", Cf, Mac("⇧⌥⌘", Key.V), ["pastePlainEnabled"]));
        list.Add(R("finderRename", Loc.T("Rename shortcut"), "finder-rename", Cf, new Hotkey(HotkeyModifiers.None, Key.F2), ["finderRenameEnabled"], hook: true, bare: true));

        // Sound
        list.Add(R("soundOutputSwitcher", Loc.T("Output switcher"), "sound-output-switcher", So, Mac(Cac, Key.S), ["soundOutputSwitcherEnabled"]));
        list.Add(R("micMute", Loc.T("Mute microphone"), "mic-mute", So, Mac(Cac, Key.M), ["micMuteShortcutEnabled"]));

        // Energy and display
        list.Add(R("keepAwake", Loc.T("Keep awake"), "keep-awake", En, Mac(Cac, Key.K), ["hotkeyEnabled"]));
        list.Add(R("displayBrightnessDecrease", Loc.T("Decrease display brightness"), "brightness", En, Mac("⇧⌘", Key.OemMinus), ["brightnessControlEnabled", "displayBrightnessShortcutsEnabled"]));
        list.Add(R("displayBrightnessIncrease", Loc.T("Increase display brightness"), "brightness", En, Mac("⇧⌘", Key.OemPlus), ["brightnessControlEnabled", "displayBrightnessShortcutsEnabled"]));

        // Tools
        list.Add(R("quickLauncher", Loc.T("Quick panel"), "quick-launcher", To, Mac("⌃⌘", Key.V), ["quickLauncherShortcutEnabled"]));
        list.Add(R("commandBar", Loc.T("Command Bar"), "command-bar", To, new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.Space), ["commandBarShortcutEnabled"]));
        list.Add(R("scratchpad", Loc.T("Scratchpad"), "scratchpad", To, Mac(Cac, Key.N), ["scratchpadShortcutEnabled"]));
        list.Add(R("cameraPreview", Loc.T("Camera preview"), "camera-preview", To, Mac(Cac, Key.W), ["cameraPreviewShortcutEnabled"]));
        list.Add(R("quitAll", Loc.T("Quit all apps"), "quit-apps", To, Mac(Cac, Key.Q), ["quitAppsShortcutEnabled"]));
        list.Add(R("quitAllExceptFront", Loc.T("Quit all apps except the front one"), "quit-apps", To, Mac(Cac, Key.O), ["quitAppsExceptFrontShortcutEnabled"]));
        list.Add(R("radialMenu", Loc.T("Radial menu"), "radial-menu", To, Mac(Cac, Key.Space), ["radialMenuEnabled"], ro: true));

        // Screen capture (Tools group, "Screen capture" header)
        const string capture = "Screen capture";
        list.Add(R("screenshot", Loc.T("Screenshot"), "screenshot", To, Mac(Cac, Key.D4), ["screenshotShortcutEnabled"], capture));
        list.Add(R("screenRecorder", Loc.T("Screen recording"), "screen-recorder", To, Mac(Cac, Key.D5), ["recorderShortcutEnabled"], capture));
        list.Add(R("screenOCR", Loc.T("Copy text from screen"), "screen-ocr", To, Mac(Cac, Key.T), ["screenOCRShortcutEnabled"], capture));
        list.Add(R("colorPicker", Loc.T("Color picker"), "color-picker", To, Mac(Cac, Key.C), ["colorPickerShortcutEnabled"], capture));
        list.Add(R("recentCaptures", Loc.T("Recent captures"), "screenshot", To, Mac(Cac, Key.H), ["recentCapturesShortcutEnabled"], capture));
        list.Add(R("screenshotFullScreen", Loc.T("Capture the whole screen"), "screenshot", To, Mac(Cac, Key.D3), ["screenshotFullScreenShortcutEnabled"], capture));
        list.Add(R("screenshotLastCapture", Loc.T("Edit latest screenshot"), "screenshot", To, Mac(Cac, Key.E), ["screenshotLastCaptureShortcutEnabled"], capture));
        list.Add(R("screenshotClipboard", Loc.T("Edit clipboard image"), "screenshot", To, Mac(Cac, Key.P), ["screenshotClipboardShortcutEnabled"], capture));
        list.Add(R("screenshotUpload", Loc.T("Upload latest screenshot"), "screenshot", To, Mac(Cac, Key.U), ["screenshotUploadShortcutEnabled", "screenshotSharingEnabled"], capture));

        // Dynamic Island
        list.Add(R("notch", Loc.T("Dynamic Island"), "notch", Is, Mac(Cac, Key.I), ["notchEnabled", "notchShortcutEnabled"]));
        return list;
    }

    private static IEnumerable<(string Id, string Title, Hotkey? Default)> WindowLayoutActions(HotkeyModifiers ca)
    {
        Hotkey? K(Key key) => new(ca, key);
        yield return ("left", Loc.T("Left"), K(Key.Left));
        yield return ("right", Loc.T("Right"), K(Key.Right));
        yield return ("top", Loc.T("Top"), K(Key.Up));
        yield return ("bottom", Loc.T("Bottom"), K(Key.Down));
        yield return ("centerHalf", Loc.T("Center half"), null);
        yield return ("leftThird", Loc.T("Left 1/3"), K(Key.D));
        yield return ("centerThird", Loc.T("Center 1/3"), K(Key.F));
        yield return ("rightThird", Loc.T("Right 1/3"), K(Key.G));
        yield return ("leftTwoThirds", Loc.T("Left 2/3"), K(Key.E));
        yield return ("rightTwoThirds", Loc.T("Right 2/3"), K(Key.T));
        foreach (var (id, title) in new[]
        {
            ("centerTwoThirds", Loc.T("Center 2/3")), ("topThird", Loc.T("Top 1/3")), ("middleThird", Loc.T("Middle 1/3")), ("bottomThird", Loc.T("Bottom 1/3")),
            ("topTwoThirds", Loc.T("Top 2/3")), ("bottomTwoThirds", Loc.T("Bottom 2/3")), ("topQuarter", Loc.T("Top 1/4")), ("upperMiddleQuarter", Loc.T("Upper middle 1/4")),
            ("lowerMiddleQuarter", Loc.T("Lower middle 1/4")), ("bottomQuarter", Loc.T("Bottom 1/4")), ("leftQuarter", Loc.T("Left 1/4")), ("leftMiddleQuarter", Loc.T("Left middle 1/4")),
            ("rightMiddleQuarter", Loc.T("Right middle 1/4")), ("rightQuarter", Loc.T("Right 1/4")), ("topLeftSixth", Loc.T("Top left 1/6")), ("topCenterSixth", Loc.T("Top center 1/6")),
            ("topRightSixth", Loc.T("Top right 1/6")), ("bottomLeftSixth", Loc.T("Bottom left 1/6")), ("bottomCenterSixth", Loc.T("Bottom center 1/6")), ("bottomRightSixth", Loc.T("Bottom right 1/6")),
        })
        {
            yield return (id, title, null);
        }

        yield return ("topLeft", Loc.T("Top left"), K(Key.U));
        yield return ("topRight", Loc.T("Top right"), K(Key.I));
        yield return ("bottomLeft", Loc.T("Bottom left"), K(Key.J));
        yield return ("bottomRight", Loc.T("Bottom right"), K(Key.K));
        yield return ("maximize", Loc.T("Maximize"), K(Key.Return));
        yield return ("maximizeWithMargin", Loc.T("Maximize with Margin"), null);
        yield return ("fullScreen", Loc.T("Full Screen"), null);
        yield return ("center", Loc.T("Center"), K(Key.C));
        yield return ("restore", Loc.T("Restore"), K(Key.R));
        yield return ("previousDisplay", Loc.T("Previous display"), null);
        yield return ("nextDisplay", Loc.T("Next display"), HotkeyMap.Mac("⌃⌥⌘", Key.Right));
    }
}
