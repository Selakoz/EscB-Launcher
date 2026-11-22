using System.Text.Json;
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
            CustomJvmArgs = CustomJvmArgs,
            JavaPath = JavaPath,
            SelectedBuildId = SelectedBuildId,
            AppliedJvmPresetVersion = AppliedJvmPresetVersion,
            OptionalModStates = new Dictionary<string, JsonElement>(OptionalModStates)
        };
    }
}
