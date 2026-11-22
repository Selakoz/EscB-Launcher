using System.Text.Json;
using System.Text.Json.Serialization;

namespace EscapeBook.Models;

/// <summary>
/// Local player settings, stored in %APPDATA%\.escapebook\user_settings.json.
/// </summary>
internal sealed class UserSettings
{
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// Allocated RAM in MB. 0 means "not chosen yet": the launcher computes the
    /// hardware-based default (8 GB cap, total RAM minus 2 GB).
    /// </summary>
    public int RamMb { get; set; }

    /// <summary>
    /// True once the player moved the RAM slider. Keeps an explicitly chosen
    /// value (including exactly 4 GB) from being replaced by the auto default.
    /// </summary>
    public bool RamMbUserSet { get; set; }

    public string CustomJvmArgs { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public string SelectedBuildId { get; set; } = string.Empty;

    /// <summary>
    /// Session token returned by the auth worker after a successful Telegram or
    /// Discord login. Persisted so external tools (skin upload page) can read
    /// it. Never logged or shown in the UI. Empty when logged out.
    /// </summary>
    [JsonPropertyName("session_token")]
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// Per-mod state chosen by the player (key = optional_mods.name).
    /// Checkbox mods store a boolean; radio mods store the selected option name,
    /// e.g. <c>"Миникарта": "Xaero's Minimap"</c>.
    /// Applied to the instance mods folder and honoured on future build downloads.
    /// </summary>
    [JsonPropertyName("optionalModStates")]
    public Dictionary<string, JsonElement> OptionalModStates { get; set; } = new();

    [JsonPropertyName("applied_jvm_preset_version")]
    public int AppliedJvmPresetVersion { get; set; } = -1;

    /// <summary>Saved boolean state of a checkbox mod, or null when not stored.</summary>
    public bool? GetModEnabled(string name)
    {
        if (!OptionalModStates.TryGetValue(name, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // A radio selection counts as "enabled" for generic consumers.
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : true,
            _ => null
        };
    }

    /// <summary>Saved option name of a radio mod, or null when not stored.</summary>
    public string? GetModOption(string name)
    {
        if (!OptionalModStates.TryGetValue(name, out JsonElement value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>True when any value (boolean or string) is stored for this mod.</summary>
    public bool HasModState(string name) => OptionalModStates.ContainsKey(name);

    public void SetModEnabled(string name, bool enabled) =>
        OptionalModStates[name] = JsonSerializer.SerializeToElement(enabled);

    public void SetModOption(string name, string optionName) =>
        OptionalModStates[name] = JsonSerializer.SerializeToElement(optionName);

    public void RemoveModState(string name) => OptionalModStates.Remove(name);

    /// <summary>Boolean view of all stored states, used by the client updater.</summary>
    public Dictionary<string, bool> GetModEnabledStates()
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in OptionalModStates.Keys)
        {
            if (GetModEnabled(name) is bool enabled)
                states[name] = enabled;
        }

        return states;
    }

    /// <summary>Radio choices only (mod name → option name).</summary>
    public Dictionary<string, string> GetModOptionStates()
    {
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in OptionalModStates.Keys)
        {
            if (GetModOption(name) is string option && !string.IsNullOrWhiteSpace(option))
                states[name] = option;
        }

        return states;
    }

    public UserSettings Clone()
    {
        return new UserSettings
        {
            Nickname = Nickname,
            RamMb = RamMb,
            RamMbUserSet = RamMbUserSet,
            CustomJvmArgs = CustomJvmArgs,
            JavaPath = JavaPath,
            SelectedBuildId = SelectedBuildId,
            SessionToken = SessionToken,
            AppliedJvmPresetVersion = AppliedJvmPresetVersion,
            OptionalModStates = new Dictionary<string, JsonElement>(OptionalModStates)
        };
    }
}
