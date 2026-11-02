// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>Registered defaults of every flat <see cref="IAppSettings"/> key this project owns (Vorssaint names and values).</summary>
internal static class SystemDefaults
{
    /// <summary>Menu-bar metric ids in their default order (spec 01 2.5).</summary>
    public static readonly string[] MetricOrder =
    [
        "cpu", "cpuTemperature", "gpu", "gpuTemperature", "memory", "battery", "batteryTime", "batteryTemperature",
        "peripheralBattery", "network", "diskUsage", "diskActivity", "connectedDevices", "power", "fanSpeed",
    ];

    /// <summary>Registers every default (safe to call many times).</summary>
    public static void Register(IAppSettings app)
    {
        var d = new Dictionary<string, object?>
        {
            // Sound
            ["mixerShowFinder"] = true,
            ["mixerHideInactiveApps"] = false,
            ["mixerAppArrangement"] = string.Empty,
            ["mixerLowerVolumeOnHeadphonesDisconnect"] = false,
            ["mixerHeadphonesDisconnectVolumePercent"] = 25,
            ["mixerHeadphonesDisconnectSilenceAllowed"] = false,
            ["preciseVolumeRollerEnabled"] = false,
            ["mixerHiddenApps"] = new List<string>(),
            ["appVolumes"] = new Dictionary<string, double>(),
            ["appVolumesArePositions"] = true,
            ["appOutputDevices"] = new Dictionary<string, string>(),
            ["appMutedApps"] = new List<string>(),
            ["mixerCallAppVolumeEnabled"] = new List<string>(),
            ["mixerKnownApps"] = new Dictionary<string, string>(),
            ["soundOutputSwitcherEnabled"] = false,
            ["soundOutputSwitcherDeviceUIDs"] = new List<string>(),
            ["audioPriorityOutputEnabled"] = true,
            ["audioPriorityInputEnabled"] = true,
            ["audioPriorityOutputUIDs"] = new List<string>(),
            ["audioPriorityInputUIDs"] = new List<string>(),
            ["audioPriorityDeviceNames"] = new Dictionary<string, string>(),
            ["micMuteMenuBarIndicator"] = true,
            ["micMuteShortcutEnabled"] = false,
            ["micMuteSavedVolume"] = 0.75,
            ["micMuteActive"] = false,
            ["micMuteSavedLevels"] = new Dictionary<string, double>(),
            ["musicBlockEnabled"] = false,
            ["musicBlockReplacementPath"] = string.Empty,
            ["musicBlockPlayReplacement"] = true,

            // Energy and display
            ["defaultDurationMinutes"] = 0,
            ["batteryLimitPercent"] = 10,
            ["keepAwakeAutoStart"] = false,
            ["keepAwakeRightClickToggle"] = false,
            ["keepAwakeAllowDisplaySleep"] = false,
            ["keepAwakeExternalDisplay"] = false,
            ["keepAwakeConnectedToPower"] = false,
            ["keepAwakeRunningApps"] = false,
            ["keepAwakeRunningAppBundleIDs"] = new List<string>(),
            ["keepAwakeAutomationRequireAll"] = false,
            ["keepAwakePauseWhenLocked"] = false,
            ["keepAwakeSwitchUsesUntil"] = false,
            ["keepAwakeUntilTime"] = 0.0,
            ["keepAwakeMouseJiggleEnabled"] = false,
            ["keepAwakeMouseJiggleIntervalMinutes"] = 5,
            ["keepAwakeIconTint"] = "orange",
            ["keepAwakeActiveIcon"] = "vorssaint",
            ["showCountdownInMenuBar"] = false,
            ["clamshellPreferred"] = false,
            ["dimScreenOnLidClose"] = false,
            ["hotkeyEnabled"] = true,
            ["brightnessControlEnabled"] = false,
            ["brightnessKeysEnabled"] = false,
            ["brightnessOSDEnabled"] = false,
            ["brightnessKeyStep"] = "standard",
            ["displayBrightnessShortcutsEnabled"] = false,
            ["softwareDimmingPreferred"] = new List<string>(),
            ["softwareDimLevels"] = new Dictionary<string, int>(),
            ["bluetoothSleepEnabled"] = false,
            ["bluetoothSleepRestoreOnWake"] = true,
            ["bluetoothSleepRestorePending"] = false,
            ["extraBrightnessEnabled"] = false,
            ["extraBrightnessLevel"] = 100,

            // Monitor: tray / menu-bar metrics
            ["menuBarMetricOrder"] = string.Join(',', MetricOrder),
            ["menuBarMetricAppearance"] = "values",
            ["menuBarCombineTemperatures"] = true,
            ["menuBarSeparateMetrics"] = false,
            ["menuBarMetricSpacing"] = "compact",
            ["menuBarHideIconWithMetrics"] = false,
            ["menuBarUsageBarNormalColor"] = "#64D2FF",
            ["menuBarUsageBarElevatedColor"] = "#FFD60A",
            ["menuBarUsageBarCriticalColor"] = "#FF453A",
            ["menuBarUsageBarMediumThreshold"] = 70,
            ["menuBarUsageBarHighThreshold"] = 90,
            ["menuBarLabelStyle"] = "compact",
            ["menuBarPreset"] = "dense",
            ["menuBarNetworkUploadFirst"] = false,
            ["menuBarMemoryStyle"] = "percent",
            ["menuBarDiskStyle"] = "percent",
            ["menuBarMetricsPlacement"] = "strip",
            ["monitorIntervalSeconds"] = 2,
            ["monitorGraphScale"] = true,
            ["temperatureUnit"] = "celsius",
            ["networkSpeedUnit"] = "bytes",
            ["monitorMemoryMetric"] = "used",
            ["panelShowFanControl"] = true,

            // Monitor: panel
            ["monitorShowSystem"] = true,
            ["monitorShowNetwork"] = true,
            ["monitorShowDisk"] = true,
            ["monitorShowPower"] = true,
            ["monitorShowMixer"] = true,
            ["monitorSysTemps"] = true,
            ["monitorSysCPU"] = true,
            ["monitorSysCPUCores"] = true,
            ["monitorSysGPU"] = true,
            ["monitorSysMemory"] = true,
            ["monitorSysAlerts"] = true,
            ["monitorSysUptime"] = true,
            ["monitorSysConnectedDevices"] = true,
            ["monitorSysBattery"] = true,
            ["monitorNetSpeed"] = true,
            ["monitorNetApps"] = true,
            ["monitorNetTotals"] = true,
            ["monitorNetAddresses"] = true,
            ["monitorNetTest"] = true,
            ["monitorDiskUsage"] = true,
            ["monitorDiskActivity"] = true,
            ["monitorDiskSMART"] = true,
            ["monitorDiskProtection"] = true,
            ["monitorDiskTools"] = true,
            ["monitorPwrSystem"] = true,
            ["monitorPwrAdapter"] = true,
            ["monitorPwrBattery"] = true,
            ["monitorPwrTimeRemaining"] = true,
            ["monitorPwrTemperature"] = true,
            ["monitorPwrHealth"] = true,
            ["monitorPwrPeripherals"] = false,
            ["monitorGraphCPU"] = true,
            ["monitorGraphGPU"] = true,
            ["monitorGraphMemory"] = true,
            ["monitorGraphNetwork"] = true,
            ["monitorGraphDisk"] = true,
            ["monitorGraphPower"] = true,
            ["monitorGraphBattery"] = true,
            ["panelSystemOrder"] = "temps,usage,memory,uptime,connectedDevices",
            ["panelNetworkOrder"] = "speed,apps,totals,addresses,test",
            ["panelDiskOrder"] = "usage,activity,smart,protection,tools",
            ["panelPowerOrder"] = "charge,temperature,system,adapter,battery,remaining,health,peripherals",
            ["diskEjectExcludedVolumes"] = new List<string>(),

            // Monitor: alerts
            ["monitorAlertCPU"] = false,
            ["monitorAlertCPUThreshold"] = 90,
            ["monitorAlertCPUTemperature"] = false,
            ["monitorAlertCPUTemperatureThreshold"] = 90,
            ["monitorAlertMemory"] = false,
            ["monitorAlertDisk"] = false,
            ["monitorAlertDiskFreePercent"] = 10,
            ["monitorAlertBatteryTemperature"] = false,
            ["monitorAlertBatteryTemperatureThreshold"] = 40,
            ["monitorAlertBattery"] = false,
            ["monitorAlertBatteryPercent"] = 15,
            ["monitorAlertCooldownMinutes"] = 15,
        };

        foreach (var id in MetricOrder)
        {
            d[MetricKey(id)] = false;
        }

        app.RegisterDefaults(d);
    }

    /// <summary>The settings key that pins a menu-bar metric (<c>menuBarCPU</c>, <c>menuBarDiskUsage</c>...).</summary>
    public static string MetricKey(string metricId)
    {
        var name = char.ToUpperInvariant(metricId[0]) + metricId[1..];
        if (name.StartsWith("Cpu", StringComparison.Ordinal))
        {
            name = "CPU" + name[3..];
        }
        else if (name.StartsWith("Gpu", StringComparison.Ordinal))
        {
            name = "GPU" + name[3..];
        }

        return "menuBar" + name;
    }
}
