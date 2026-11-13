using Microsoft.Win32;

namespace EscapeBook.Services;

internal static class GpuPreferenceService
{
    private const string PreferencesKeyPath =
        @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>
    /// Forces Windows to prefer the discrete GPU for the exact javaw.exe that will
    /// be used for this launch: "GpuPreference=2;" means High performance.
    /// </summary>
    public static bool TryApply(string javaPath, out string? error)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            {
                error = "javaw.exe не найден, запись GPU-настройки пропущена.";
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath, writable: true);
            if (key == null)
            {
                error = "Не удалось открыть раздел реестра UserGpuPreferences.";
                return false;
            }

            key.SetValue(javaPath, "GpuPreference=2;", RegistryValueKind.String);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("GPU preference registry", ex);
            return false;
        }
    }
}