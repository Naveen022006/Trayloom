// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Trayloom.Features.Island;

/// <summary>Session / power state (lock, sleep, display, screen saver) feeding the island's presentation gates (spec Part D 10.2).</summary>
internal sealed class SessionState : IDisposable
{
    private const uint SpiGetScreenSaverRunning = 0x0072;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _poll;
    private bool _screenSaver;

    public SessionState(Dispatcher ui)
    {
        _ui = ui;
        _poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
        _poll.Tick += (_, _) => PollScreenSaver();
    }

    public bool Locked { get; private set; }

    public bool Sleeping { get; private set; }

    public bool DisplaysSleeping { get; private set; }

    public bool OnConsole { get; private set; } = true;

    public bool ScreenSaverRunning => _screenSaver;

    public bool CanRunTimer => !Locked && !Sleeping && OnConsole;

    public bool CanPresent => CanRunTimer && !DisplaysSleeping && !_screenSaver;

    public bool HearsLockChange => !Sleeping && OnConsole && !DisplaysSleeping && !_screenSaver;

    public event Action? Changed;

    public event Action? Locking;

    public event Action? Unlocked;

    public void Start()
    {
        SystemEvents.SessionSwitch += OnSwitch;
        SystemEvents.PowerModeChanged += OnPower;
        _poll.Start();
    }

    private void OnSwitch(object? sender, SessionSwitchEventArgs e) => _ui.BeginInvoke(() =>
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Locked = true;
                Locking?.Invoke();
                break;
            case SessionSwitchReason.SessionUnlock:
                Locked = false;
                Unlocked?.Invoke();
                break;
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteConnect:
                OnConsole = false;
                break;
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteDisconnect:
                OnConsole = true;
                break;
        }

        Changed?.Invoke();
    });

    private void OnPower(object? sender, PowerModeChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Sleeping = true;
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Sleeping = false;
            DisplaysSleeping = false;
        }

        Changed?.Invoke();
    });

    private void PollScreenSaver()
    {
        var running = false;
        try
        {
            SystemParametersInfo(SpiGetScreenSaverRunning, 0, ref running, 0);
        }
        catch
        {
        }

        if (running != _screenSaver)
        {
            _screenSaver = running;
            Changed?.Invoke();
        }
    }

    /// <summary>Plays the lock / unlock sound (Windows media files when present, else the system asterisk).</summary>
    public void PlayLockSound(bool unlock)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", unlock ? "Windows Unlock.wav" : "Windows Logon.wav");
            if (File.Exists(path))
            {
                using var player = new SoundPlayer(path);
                player.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSwitch;
        SystemEvents.PowerModeChanged -= OnPower;
        _poll.Stop();
    }
}
