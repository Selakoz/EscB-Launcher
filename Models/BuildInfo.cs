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
}