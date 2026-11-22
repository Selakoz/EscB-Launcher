using System.Runtime.InteropServices;

namespace EscapeBook.Services;

/// <summary>
/// Fast physical memory query (GlobalMemoryStatusEx, no WMI) used by the low-RAM guard.
/// </summary>
internal static class SystemMemoryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>Currently available physical memory in megabytes (0 when unknown).</summary>
    public static int GetAvailablePhysicalMemoryMb()
    {
        try
        {
            var status = CreateStatus();

            if (GlobalMemoryStatusEx(ref status))
                return (int)(status.ullAvailPhys / 1024UL / 1024UL);
        }
        catch (Exception ex)
        {
            AppLog.Error("Available memory query", ex);
        }

        return 0;
    }

    /// <summary>Total physical memory in megabytes (0 when unknown).</summary>
    public static int GetTotalPhysicalMemoryMb()
    {
        try
        {
            var status = CreateStatus();

            if (GlobalMemoryStatusEx(ref status))
                return (int)(status.ullTotalPhys / 1024UL / 1024UL);
        }
        catch (Exception ex)
        {
            AppLog.Error("Total memory query", ex);
        }

        return 0;
    }

    private static MEMORYSTATUSEX CreateStatus() => new()
    {
        dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
    };
}