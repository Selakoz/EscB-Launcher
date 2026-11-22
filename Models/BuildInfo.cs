using System.Text.Json.Serialization;

namespace EscapeBook.Models;

internal sealed class BuildInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mc_version")]
    public string McVersion { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0";

    [JsonPropertyName("client_zip_url")]
    public string ClientZipUrl { get; set; } = string.Empty;

    /// <summary>
    /// Секретная сборка: не показывается в выпадающем списке, пока пользователь
    /// не выберет её вручную в user_settings.json (selectedBuildId) или не
    /// раскроет список сочетанием Ctrl+Shift+S. По умолчанию false.
    /// </summary>
    [JsonPropertyName("is_secret")]
    public bool IsSecret { get; set; } = false;

    public override string ToString() => Name;
}

internal sealed class OptionalModInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("file_prefixes")]
    public List<string> FilePrefixes { get; set; } = new();

    [JsonPropertyName("enabled_by_default")]
    public bool EnabledByDefault { get; set; }

    /// <summary>
    /// Optional mutually-exclusive group name. Mods sharing the same non-empty
    /// group are shown as radio buttons and only one of them can be enabled.
    /// </summary>
    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// UI control type: "checkbox" (default) or "radio". A radio mod renders one
    /// radio button per entry in <see cref="Options"/> and exactly one is active.
    /// Missing/unknown values are treated as "checkbox" for backward compatibility.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "checkbox";

    /// <summary>
    /// Available choices for a radio-type mod (for example Xaero's vs JourneyMap).
    /// Empty for regular checkbox mods.
    /// </summary>
    [JsonPropertyName("options")]
    public List<ModOption> Options { get; set; } = new();

    /// <summary>True only when the mod is a real radio group with at least two options.</summary>
    [JsonIgnore]
    public bool IsRadio =>
        string.Equals(Type, "radio", StringComparison.OrdinalIgnoreCase) &&
        Options.Count >= 2;
}

internal sealed class ModOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("file_prefixes")]
    public List<string> FilePrefixes { get; set; } = new();

    [JsonPropertyName("enabled_by_default")]
    public bool EnabledByDefault { get; set; }
}

internal sealed class SecretBuildInfo
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("build")]
    public BuildInfo Build { get; set; } = new();
}