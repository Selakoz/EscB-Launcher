using System.Text.Json.Serialization;

namespace EscapeBook.Models;

/// <summary>
/// Local player settings, stored in %APPDATA%\.escapebook\user_settings.json.
/// </summary>
internal sealed class UserSettings
{
    public string Nickname { get; set; } = string.Empty;
    public int RamMb { get; set; } = 4096;
    public string CustomJvmArgs { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public string SelectedBuildId { get; set; } = string.Empty;

    [JsonPropertyName("applied_jvm_preset_version")]
    public int AppliedJvmPresetVersion { get; set; } = -1;

    public UserSettings Clone() => new()
    {
        Nickname = Nickname,
        RamMb = RamMb,
        CustomJvmArgs = CustomJvmArgs,
        JavaPath = JavaPath,
        SelectedBuildId = SelectedBuildId,
        AppliedJvmPresetVersion = AppliedJvmPresetVersion
    };
}