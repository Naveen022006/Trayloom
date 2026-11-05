// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Trayloom.Features.Maintenance;

/// <summary>The few Win32 entry points the maintenance tools need.</summary>
internal static class Win32
{
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SETTINGCHANGE = 0x001A;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateProcess(nint process, uint exitCode);
    [DllImport("kernel32.dll")] public static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] public static extern uint GetFileAttributesW([MarshalAs(UnmanagedType.LPWStr)] string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern uint GetCompressedFileSizeW(string path, out uint high);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern uint GetDriveTypeW(string root);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern nint SHGetFileInfoW(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBinW(nint hwnd, string? root, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] public static extern nint LocalFree(nint mem);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    public const uint FO_DELETE = 3;
    public const ushort FOF_SILENT = 0x0004;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_NOERRORUI = 0x0400;
    public const ushort FOF_WANTNUKEWARNING = 0x4000;

    public const uint SHERB_NOCONFIRMATION = 1;
    public const uint SHERB_NOPROGRESSUI = 2;
    public const uint SHERB_NOSOUND = 4;

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0;
    public const uint SHGFI_SMALLICON = 1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    /// <summary>Opens a file or directory for identity queries without following reparse points.</summary>
    public static SafeFileHandle OpenForIdentity(string path) =>
        CreateFileW(path, 0, 7, nint.Zero, 3, 0x02000000 | 0x00200000, nint.Zero);

    /// <summary>Volume serial plus file index of a path, or null when it cannot be read.</summary>
    public static string? FileIdentity(string path)
    {
        try
        {
            using var handle = OpenForIdentity(path);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            {
                return null;
            }

            return $"{info.VolumeSerialNumber:x}-{info.FileIndexHigh:x}{info.FileIndexLow:x8}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the path itself is a reparse point (symlink, junction, mount point).</summary>
    public static bool IsReparsePoint(string path)
    {
        var attrs = GetFileAttributesW(path);
        return attrs != 0xFFFFFFFF && (attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
    }

    /// <summary>Process creation time (FILETIME ticks) or 0 when unreadable.</summary>
    public static long CreationTime(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return 0;
        }

        try
        {
            return GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Image path of a process or null.</summary>
    public static string? ImagePath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(1024);
            var size = builder.Capacity;
            return QueryFullProcessImageNameW(handle, 0, builder, ref size) ? builder.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
