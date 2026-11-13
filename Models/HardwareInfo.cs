namespace EscapeBook.Models;

internal sealed class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public long? VramMb { get; set; }
}

internal sealed class HardwareInfo
{
    public string Cpu { get; set; } = "Неизвестно";
    public List<GpuInfo> Gpus { get; set; } = new();
    public int TotalRamMb { get; set; }
    public int AvailableRamMb { get; set; }

    public string GpuNames => Gpus.Count == 0
        ? "Неизвестно"
        : string.Join("\n", Gpus.Select(gpu => gpu.Name));
}