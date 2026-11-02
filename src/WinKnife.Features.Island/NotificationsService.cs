// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinKnife.Features.Island;

internal sealed class NotificationItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string App { get; init; } = "";

    public string Title { get; init; } = "";

    public string Subtitle { get; init; } = "";

    public string Body { get; init; } = "";

    public DateTime Received { get; init; } = DateTime.Now;

    public string? Aumid { get; init; }

    public ImageSource? Icon { get; init; }

    public bool CanOpen { get; set; }

    public string? Unavailable { get; set; }

    public string CompactTitle => string.IsNullOrWhiteSpace(Title) ? App : Title;

    public string CompactDetail => string.Join(" · ", new[] { Subtitle, Body }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

internal enum NotificationMode
{
    Off,
    Listener,
    Automation,
}

/// <summary>
/// Mirrors new visible banners: UserNotificationListener when the process has package identity, otherwise a best-effort
/// UI Automation read of the toast popup. Messages stay in memory only (max 50) and are cleared on lock / stop.
/// </summary>
internal sealed class NotificationsService : IDisposable
{
    private readonly IslandCore _core;
    private readonly List<NotificationItem> _items = [];
    private DispatcherTimer? _poll;
    private Windows.UI.Notifications.Management.UserNotificationListener? _listener;
    private readonly HashSet<uint> _baseline = [];
    private bool _first = true;
    private CancellationTokenSource? _uiaCts;
    private string _lastUia = "";
    private DateTime _lastUiaAt = DateTime.MinValue;

    public NotificationsService(IslandCore core) => _core = core;

    public IReadOnlyList<NotificationItem> Items => _items;

    public NotificationMode Mode { get; private set; }

    public bool Monitoring => Mode != NotificationMode.Off;

    /// <summary>True when the listener refused because the process has no package identity.</summary>
    public bool NeedsIdentity { get; private set; }

    public bool AccessDenied { get; private set; }

    public event Action? Changed;

    public event Action<NotificationItem>? Received;

    public bool Wanted => _core.IslandOn && _core.Ctx.Runtime.IsAvailable("notch-notifications") && _core.App.Get<bool>(K.NotificationsEnabled);

    public void Start()
    {
        _core.Session.Locking += Clear;
        Sync();
    }

    public void Sync()
    {
        if (!Wanted)
        {
            Stop();
            return;
        }

        if (Mode != NotificationMode.Off)
        {
            return;
        }

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        _first = true;
        NeedsIdentity = false;
        AccessDenied = false;
        try
        {
            _listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            var status = _listener.GetAccessStatus();
            if (status == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Unspecified)
            {
                status = await _listener.RequestAccessAsync();
            }

            if (status == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
            {
                Mode = NotificationMode.Listener;
                _poll = new DispatcherTimer(DispatcherPriority.Background, _core.Ui) { Interval = TimeSpan.FromSeconds(2) };
                _poll.Tick += async (_, _) => await ReadListener();
                _poll.Start();
                await ReadListener();
                Changed?.Invoke();
                return;
            }

            AccessDenied = true;
        }
        catch
        {
            NeedsIdentity = true;
        }

        StartAutomation();
        Changed?.Invoke();
    }

    private async Task ReadListener()
    {
        if (_listener is null)
        {
            return;
        }

        try
        {
            var list = await _listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            foreach (var n in list)
            {
                if (_first)
                {
                    _baseline.Add(n.Id);
                    continue;
                }

                if (!_baseline.Add(n.Id))
                {
                    continue;
                }

                string title = "", sub = "", body = "";
                try
                {
                    var binding = n.Notification.Visual.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).ToList() ?? [];
                    title = texts.ElementAtOrDefault(0) ?? "";
                    if (texts.Count >= 3)
                    {
                        sub = texts[1];
                        body = texts[2];
                    }
                    else
                    {
                        body = texts.ElementAtOrDefault(1) ?? "";
                    }
                }
                catch
                {
                }

                ImageSource? icon = null;
                try
                {
                    var logo = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
                    icon = await PlaybackService.LoadArtworkAsync(logo, 32);
                }
                catch
                {
                }

                Add(new NotificationItem
                {
                    App = n.AppInfo.DisplayInfo.DisplayName,
                    Title = title,
                    Subtitle = sub,
                    Body = body,
                    Aumid = n.AppInfo.AppUserModelId,
                    Icon = icon,
                    CanOpen = !string.IsNullOrEmpty(n.AppInfo.AppUserModelId),
                    Received = n.CreationTime.LocalDateTime,
                });
            }

            _first = false;
        }
        catch
        {
        }
    }

    // ---------------------------------------------------------------- automation fallback

    private void StartAutomation()
    {
        Mode = NotificationMode.Automation;
        _uiaCts = new CancellationTokenSource();
        var token = _uiaCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    ReadToast();
                }
                catch
                {
                }

                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }, token);
    }

    private void ReadToast()
    {
        var root = AutomationElement.RootElement;
        var cond = new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "New notification"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NormalToastView"));
        var toast = root.FindFirst(TreeScope.Children, cond);
        if (toast is null)
        {
            _lastUia = "";
            return;
        }

        var texts = toast.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .Select(e => e.Current.Name)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        if (texts.Count == 0)
        {
            var name = toast.Current.Name;
            if (!string.IsNullOrWhiteSpace(name) && name != "New notification")
            {
                texts.Add(name);
            }
        }

        if (texts.Count == 0)
        {
            return;
        }

        var key = string.Join("|", texts);
        if (key == _lastUia && (DateTime.Now - _lastUiaAt).TotalSeconds < 8)
        {
            return;
        }

        _lastUia = key;
        _lastUiaAt = DateTime.Now;
        string app = "", title = "", sub = "", body = "";
        if (texts.Count == 1 && texts[0].Contains(','))
        {
            var parts = texts[0].Split(',', 3, StringSplitOptions.TrimEntries);
            app = parts[0];
            title = parts.ElementAtOrDefault(1) ?? "";
            body = parts.ElementAtOrDefault(2) ?? "";
        }
        else
        {
            app = texts[0];
            title = texts.ElementAtOrDefault(1) ?? "";
            if (texts.Count >= 4)
            {
                sub = texts[2];
                body = texts[3];
            }
            else
            {
                body = texts.ElementAtOrDefault(2) ?? "";
            }
        }

        _core.Ui.BeginInvoke(() => Add(new NotificationItem { App = app, Title = title, Subtitle = sub, Body = body }));
    }

    private void Add(NotificationItem item)
    {
        _items.Insert(0, item);
        while (_items.Count > 50)
        {
            _items.RemoveAt(_items.Count - 1);
        }

        Changed?.Invoke();
        Received?.Invoke(item);
        _core.ShowNotice(new Notice
        {
            Event = NoticeEvents.SystemNotification,
            Title = item.CompactTitle,
            Detail = item.CompactDetail,
            Image = item.Icon,
            Glyph = G.Bell,
            Banner = true,
            FullBody = item.Body,
            OpensModule = "notifications",
            OnClick = () => Open(item),
        });
    }

    public void Dismiss(NotificationItem item)
    {
        _items.Remove(item);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke();
    }

    public bool Open(NotificationItem item)
    {
        if (string.IsNullOrEmpty(item.Aumid))
        {
            item.Unavailable = "This notification can no longer accept this action.";
            Changed?.Invoke();
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.Aumid}") { UseShellExecute = true });
            return true;
        }
        catch
        {
            item.Unavailable = "This notification can no longer accept this action.";
            Changed?.Invoke();
            return false;
        }
    }

    public void OpenAccessSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public async Task RequestAccessAsync()
    {
        Stop();
        await StartAsync();
    }

    private void Stop()
    {
        _poll?.Stop();
        _poll = null;
        _uiaCts?.Cancel();
        _uiaCts = null;
        _listener = null;
        _baseline.Clear();
        if (Mode != NotificationMode.Off)
        {
            Mode = NotificationMode.Off;
            _items.Clear();
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _core.Session.Locking -= Clear;
        Stop();
    }
}
