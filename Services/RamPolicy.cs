namespace EscapeBook.Services;

/// <summary>
/// Single source of truth for the RAM slider limits and the hardware-based
/// default. The maximum leaves 2 GB for Windows, and the default is capped at
/// 8 GB so a freshly installed launcher does not allocate too much memory.
/// </summary>
internal static class RamPolicy
{
    public const int MinimumRamMb = 1024;
    public const int SystemReserveMb = 2048;
    public const int DefaultRamCapMb = 8192;
    public const int RamStepMb = 512;

    /// <summary>
    /// The old implicit default (4 GB). Settings files written by older builds
    /// may contain it even though the player never touched the slider.
    /// </summary>
    public const int LegacyDefaultRamMb = 4096;

    /// <summary>Maximum RAM the player may allocate: total RAM minus 2 GB.</summary>
    public static int ComputeMaxRamMb(int totalRamMb)
    {
        if (totalRamMb <= 0)
            return DefaultRamCapMb;

        int max = Math.Max(MinimumRamMb, totalRamMb - SystemReserveMb);
        max = (max / RamStepMb) * RamStepMb;
        return Math.Max(MinimumRamMb, max);
    }

    /// <summary>
    /// Default allocation for a missing/unset setting: 8 GB when the machine
    /// allows it, otherwise the maximum available (total RAM minus 2 GB).
    /// </summary>
    public static int ComputeDefaultRamMb(int totalRamMb) =>
        Math.Max(MinimumRamMb, Math.Min(DefaultRamCapMb, ComputeMaxRamMb(totalRamMb)));

    public static int NormalizeRamMb(int ramMb)
    {
        int value = (int)(Math.Round(ramMb / (double)RamStepMb) * RamStepMb);
        return Math.Max(MinimumRamMb, value);
    }
}
