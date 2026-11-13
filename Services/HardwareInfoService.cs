using System.Management;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal static class HardwareInfoService
{
    public static HardwareInfo Collect()
    {
        var info = new HardwareInfo
        {
            Cpu = ReadCpuName(),
            TotalRamMb = ReadTotalRamMb(),
            AvailableRamMb = ReadAvailableRamMb()
        };

        info.Gpus = ReadGpus();

        if (info.TotalRamMb <= 0)
        {
            try
            {
                info.TotalRamMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
            }
            catch (Exception ex)
            {
                AppLog.Error("RAM fallback", ex);
            }
        }

        return info;
    }

    private static string ReadCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            using var results = searcher.Get();
            var names = new List<string>();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string? name = item["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }

            return names.Count > 0 ? string.Join(" / ", names) : "Неизвестно";
        }
        catch (Exception ex)
        {
            AppLog.Error("WMI CPU", ex);
            return "Неизвестно";
        }
    }

    private static List<GpuInfo> ReadGpus()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string? name = item["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    long? vramMb = null;
                    try
                    {
                        object? rawValue = item["AdapterRAM"];
                        if (rawValue != null)
                        {
                            ulong bytes = Convert.ToUInt64(rawValue);
                            if (bytes > 0 && bytes < uint.MaxValue)
                                vramMb = (long)(bytes / 1024 / 1024);
                        }
                    }
                    catch
                    {
                        // AdapterRAM is unreliable/absent on some drivers.
                    }

                    gpus.Add(new GpuInfo { Name = name, VramMb = vramMb });
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("WMI GPU", ex);
        }

        return gpus;
    }

    private static int ReadTotalRamMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    object? value = item["TotalPhysicalMemory"];
                    if (value != null)
                        return (int)(Convert.ToUInt64(value) / 1024 / 1024);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("WMI total RAM", ex);
        }

        return 0;
    }

    private static int ReadAvailableRamMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    object? value = item["FreePhysicalMemory"];
                    if (value != null)
                        return (int)(Convert.ToUInt64(value) / 1024);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("WMI available RAM", ex);
        }

        return 0;
    }
}