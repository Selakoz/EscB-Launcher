using System.Text.Json;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(LauncherPaths.SettingsFile))
                return new UserSettings();

            string json = File.ReadAllText(LauncherPaths.SettingsFile);
            return JsonSerializer.Deserialize<UserSettings>(json, Options) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings load", ex);
            return new UserSettings();
        }
    }

    public static bool TrySave(UserSettings settings, out string? error)
    {
        try
        {
            LauncherPaths.EnsureRoot();
            string json = JsonSerializer.Serialize(settings, Options);
            string tempPath = LauncherPaths.SettingsFile + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, LauncherPaths.SettingsFile, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("Settings save", ex);
            return false;
        }
    }

    /// <summary>
    /// Применяет default_jvm_args из серверного конфига, только если
    /// jvm_preset_version на сервере выше сохранённого у игрока.
    /// Охватывает и первый запуск (AppliedJvmPresetVersion == -1).
    /// </summary>
    public static bool ApplyJvmPresetIfNeeded(
        LauncherConfig config,
        UserSettings settings)
    {
        if (config.JvmPresetVersion > settings.AppliedJvmPresetVersion)
        {
            settings.CustomJvmArgs = config.DefaultJvmArgs;
            settings.AppliedJvmPresetVersion = config.JvmPresetVersion;
            return true;
        }

        return false;
    }
}