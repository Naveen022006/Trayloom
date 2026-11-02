// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;

namespace WinKnife.Features.Tools;

/// <summary>A mounted logical drive as the "Eject all disks" logic sees it (spec 2.2.5).</summary>
internal sealed record ToggleVolume(string Root, string Label, string? VolumeGuid, int DriveType, bool IsLocal, bool IsRoot, bool IsRemovable, bool IsInternal)
{
    /// <summary>"E:".</summary>
    public string Letter => Root.TrimEnd('\\');

    /// <summary>Name shown in menus and stored in the exclusion list (label, else the drive letter).</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Letter : Label;
}

/// <summary>Volume classification, exclusion matching and safe removal for the Eject all disks toggle.</summary>
internal static class ToggleDisks
{
    private const int DriveRemovable = 2;
    private const int DriveFixed = 3;
    private const int DriveCdrom = 5;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    private static readonly Guid GuidDevinterfaceDisk = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    /// <summary>Every local volume (removable, fixed, optical) with its classification flags.</summary>
    public static IReadOnlyList<ToggleVolume> Enumerate()
    {
        var list = new List<ToggleVolume>();
        var system = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\') + "\\";
        var mask = GetLogicalDrives();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0)
            {
                continue;
            }

            var root = $"{(char)('A' + i)}:\\";
            var type = (int)GetDriveTypeW(root);
            if (type is not (DriveRemovable or DriveFixed or DriveCdrom))
            {
                continue;
            }

            var label = ReadLabel(root);
            string? guid = null;
            var sb = new StringBuilder(64);
            if (GetVolumeNameForVolumeMountPointW(root, sb, sb.Capacity))
            {
                guid = sb.ToString();
            }

            var removableMedia = type is DriveRemovable or DriveCdrom;
            var internalBus = false;
            if (type == DriveFixed)
            {
                var bus = QueryBus(root.TrimEnd('\\'), out var removableFlag);
                removableMedia |= removableFlag;
                internalBus = bus switch
                {
                    1 or 3 or 8 or 0xA or 0xB or 0x10 or 0x11 => !removableFlag,
                    -1 => true, // unreadable fixed disk: never offer it
                    _ => false,
                };
            }

            list.Add(new ToggleVolume(root, label, guid, type, true, root.Equals(system, StringComparison.OrdinalIgnoreCase), removableMedia, internalBus));
        }

        return list;
    }

    /// <summary>Vorssaint's <c>shouldOfferEject</c>: local, not the boot volume, and (not internal or removable/ejectable).</summary>
    public static bool ShouldOffer(ToggleVolume v) => v.IsLocal && !v.IsRoot && (!v.IsInternal || v.IsRemovable);

    /// <summary>True when an exclusion entry equals the name, GUID, mount path or last path component (case-insensitive).</summary>
    public static bool IsExcluded(ToggleVolume v, IEnumerable<string> excluded)
    {
        foreach (var raw in excluded)
        {
            var e = raw.Trim();
            if (e.Length == 0)
            {
                continue;
            }

            var letter = v.Letter;
            if (e.Equals(v.Label, StringComparison.OrdinalIgnoreCase)
                || e.Equals(v.Root, StringComparison.OrdinalIgnoreCase)
                || e.Equals(letter, StringComparison.OrdinalIgnoreCase)
                || e.Equals(letter[..1], StringComparison.OrdinalIgnoreCase)
                || (v.VolumeGuid is not null && (e.Equals(v.VolumeGuid, StringComparison.OrdinalIgnoreCase) || e.Equals(v.VolumeGuid.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Offerable volumes without the exclusion list (candidates of the "Add drive…" menu).</summary>
    public static IReadOnlyList<ToggleVolume> OfferableIgnoringExclusions() => Enumerate().Where(ShouldOffer).ToList();

    /// <summary>Offerable volumes after the exclusion list.</summary>
    public static IReadOnlyList<ToggleVolume> Candidates(IEnumerable<string> excluded)
    {
        var ex = excluded.ToList();
        return Enumerate().Where(v => ShouldOffer(v) && !IsExcluded(v, ex)).ToList();
    }

    /// <summary>
    /// Ejects every candidate. A failure counts only when the drive letter is still present afterwards (a drive carrying several
    /// volumes leaves whole on the first eject). Returns true when nothing failed.
    /// </summary>
    public static bool EjectAll(IEnumerable<string> excluded)
    {
        var failed = false;
        foreach (var v in Candidates(excluded))
        {
            if (!DriveExists(v.Root))
            {
                continue;
            }

            try
            {
                Eject(v);
            }
            catch
            {
                // judged by presence below
            }

            var gone = false;
            for (var i = 0; i < 12 && !gone; i++)
            {
                gone = !DriveExists(v.Root);
                if (!gone)
                {
                    Thread.Sleep(250);
                }
            }

            if (!gone)
            {
                failed = true;
            }
        }

        return !failed;
    }

    /// <summary>Starts the removal of one volume (optical: lock, dismount, eject media; disks: PnP eject, shell verb fallback).</summary>
    public static bool Eject(ToggleVolume v)
    {
        if (v.DriveType == DriveCdrom)
        {
            return EjectOptical(v.Letter);
        }

        if (EjectPnp(v.Letter))
        {
            return true;
        }

        return EjectShellVerb(v.Root);
    }

    private static bool DriveExists(string root) => (GetLogicalDrives() & (1u << (char.ToUpperInvariant(root[0]) - 'A'))) != 0;

    private static string ReadLabel(string root)
    {
        var sb = new StringBuilder(261);
        return GetVolumeInformationW(root, sb, sb.Capacity, out _, out _, out _, null, 0) ? sb.ToString() : string.Empty;
    }

    /// <summary>Bus type of the disk behind a drive letter (-1 when it cannot be read).</summary>
    private static int QueryBus(string letter, out bool removableMedia)
    {
        removableMedia = false;
        var handle = CreateFileW($"\\\\.\\{letter}", 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle == -1)
        {
            return -1;
        }

        try
        {
            var query = new byte[12]; // STORAGE_PROPERTY_QUERY: PropertyId 0 (device), QueryType 0
            var buffer = new byte[1024];
            if (!DeviceIoControl(handle, 0x2D1400, query, query.Length, buffer, buffer.Length, out var returned, 0) || returned < 32)
            {
                return -1;
            }

            removableMedia = buffer[10] != 0;
            return BitConverter.ToInt32(buffer, 28);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool EjectOptical(string letter)
    {
        var handle = CreateFileW($"\\\\.\\{letter}", GenericRead | GenericWrite, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle == -1)
        {
            return false;
        }

        try
        {
            DeviceIoControl(handle, 0x90018, null, 0, null, 0, out _, 0);
            DeviceIoControl(handle, 0x90020, null, 0, null, 0, out _, 0);
            var prevent = new byte[] { 0 };
            DeviceIoControl(handle, 0x2D4804, prevent, 1, null, 0, out _, 0);
            return DeviceIoControl(handle, 0x2D4808, null, 0, null, 0, out _, 0);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool EjectShellVerb(string root)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            shell.NameSpace(17).ParseName(root).InvokeVerb("Eject");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool EjectPnp(string letter)
    {
        var number = DeviceNumberOfVolume(letter);
        if (number is null)
        {
            return false;
        }

        var set = SetupDiGetClassDevsW(GuidDevinterfaceDisk, null, 0, 0x12);
        if (set == -1)
        {
            return false;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, GuidDevinterfaceDisk, index, ref iface))
                {
                    return false;
                }

                var info = new SpDevinfoData { cbSize = Marshal.SizeOf<SpDevinfoData>() };
                SetupDiGetDeviceInterfaceDetailW(set, ref iface, 0, 0, out var required, 0);
                if (required == 0)
                {
                    continue;
                }

                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, required, out _, ref info))
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null || DeviceNumberOfPath(path) != number)
                    {
                        continue;
                    }

                    if (CM_Get_Parent(out var parent, info.DevInst, 0) != 0)
                    {
                        return false;
                    }

                    var name = new StringBuilder(260);
                    return CM_Request_Device_EjectW(parent, out _, name, name.Capacity, 0) == 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static (uint Type, uint Number)? DeviceNumberOfVolume(string letter) => DeviceNumberOfPath($"\\\\.\\{letter}");

    private static (uint Type, uint Number)? DeviceNumberOfPath(string path)
    {
        var handle = CreateFileW(path, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle == -1)
        {
            return null;
        }

        try
        {
            var buffer = new byte[12];
            return DeviceIoControl(handle, 0x2D1080, null, 0, buffer, buffer.Length, out _, 0)
                ? (BitConverter.ToUInt32(buffer, 0), BitConverter.ToUInt32(buffer, 4))
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [DllImport("kernel32.dll")] private static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetVolumeNameForVolumeMountPointW(string root, StringBuilder name, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetVolumeInformationW(string root, StringBuilder label, int labelSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fs, int fsSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(nint handle, uint code, byte[]? input, int inputSize, byte[]? output, int outputSize, out int returned, nint overlapped);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevsW(in Guid classGuid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, in Guid classGuid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SpDeviceInterfaceData data, nint detail, uint size, out uint required, nint devInfo);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SpDeviceInterfaceData data, nint detail, uint size, out uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Request_Device_EjectW(uint devInst, out int vetoType, StringBuilder vetoName, int nameLength, uint flags);
}
