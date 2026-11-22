using System.Text.Json.Serialization;

namespace EscapeBook.Models;

/// <summary>
/// Remote EscapeBook launcher configuration (config.json on GitHub).
/// </summary>
internal sealed class LauncherConfig
{
    [JsonPropertyName("jvm_preset_version")]
    public int JvmPresetVersion { get; set; }

    [JsonPropertyName("default_jvm_args")]
    public string DefaultJvmArgs { get; set; } = string.Empty;

    [JsonPropertyName("discord_webhook_url")]
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("banner_url")]
    public string BannerUrl { get; set; } = string.Empty;

    [JsonPropertyName("banner_hash")]
    public string BannerHash { get; set; } = string.Empty;

    [JsonPropertyName("website_url")]
    public string WebsiteUrl { get; set; } = "https://escapebook.site/";

    [JsonPropertyName("builds")]
    public List<BuildInfo> Builds { get; set; } = new();

    [JsonPropertyName("optional_mods")]
    public List<OptionalModInfo> OptionalMods { get; set; } = new();

    /// <summary>
    /// Hidden builds activated by typing a code on the main window. They are not
    /// part of <see cref="Builds"/> and therefore do not appear in the dropdown
    /// until the matching code is entered.
    /// </summary>
    [JsonPropertyName("secret_builds")]
    public List<SecretBuildInfo> SecretBuilds { get; set; } = new();

    public void NormalizeAndValidate()
    {
        JvmPresetVersion = Math.Max(0, JvmPresetVersion);
        DefaultJvmArgs = DefaultJvmArgs?.Trim() ?? string.Empty;
        DiscordWebhookUrl = DiscordWebhookUrl?.Trim() ?? string.Empty;
        BannerUrl = BannerUrl?.Trim() ?? string.Empty;
        BannerHash = BannerHash?.Trim().ToLowerInvariant() ?? string.Empty;

        if (Builds == null || Builds.Count == 0)
            throw new InvalidDataException("В конфигурации нет ни одной сборки (builds).");

        var normalizedBuilds = new List<BuildInfo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var build in Builds)
        {
            if (build == null)
                continue;

            build.Id = build.Id?.Trim() ?? string.Empty;
            build.Name = string.IsNullOrWhiteSpace(build.Name) ? build.Id : build.Name.Trim();
            build.McVersion = build.McVersion?.Trim() ?? string.Empty;
            build.Version = string.IsNullOrWhiteSpace(build.Version) ? "0" : build.Version.Trim();
            build.ClientZipUrl = build.ClientZipUrl?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(build.Id))
                throw new InvalidDataException("У сборки не заполнено поле id.");

            if (build.Id.Length > 64 ||
                !build.Id.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            {
                throw new InvalidDataException(
                    $"Недопустимый id сборки \"{build.Id}\". Используйте латиницу, цифры, _ и -.");
            }

            if (string.IsNullOrWhiteSpace(build.McVersion))
                throw new InvalidDataException($"У сборки \"{build.Id}\" не заполнено поле mc_version.");

            if (ids.Add(build.Id))
                normalizedBuilds.Add(build);
        }

        if (normalizedBuilds.Count == 0)
            throw new InvalidDataException("В конфигурации нет корректных сборок (builds).");

        Builds = normalizedBuilds;

        var normalizedSecretBuilds = new List<SecretBuildInfo>();
        var secretCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SecretBuilds != null)
        {
            foreach (var secret in SecretBuilds)
            {
                if (secret == null || secret.Build == null)
                    continue;

                secret.Code = secret.Code?.Trim() ?? string.Empty;

                if (secret.Code.Length == 0 || !secretCodes.Add(secret.Code))
                    continue;

                BuildInfo build = secret.Build;
                build.Id = build.Id?.Trim() ?? string.Empty;
                build.Name = string.IsNullOrWhiteSpace(build.Name) ? build.Id : build.Name.Trim();
                build.McVersion = build.McVersion?.Trim() ?? string.Empty;
                build.Version = string.IsNullOrWhiteSpace(build.Version) ? "0" : build.Version.Trim();
                build.ClientZipUrl = build.ClientZipUrl?.Trim() ?? string.Empty;

                // Invalid secret builds are skipped silently so a typo in one entry
                // never prevents the launcher from starting.
                if (string.IsNullOrWhiteSpace(build.Id) ||
                    build.Id.Length > 64 ||
                    !build.Id.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') ||
                    string.IsNullOrWhiteSpace(build.McVersion))
                {
                    continue;
                }

                normalizedSecretBuilds.Add(secret);
            }
        }

        SecretBuilds = normalizedSecretBuilds;

        var normalizedMods = new List<OptionalModInfo>();

        if (OptionalMods != null)
        {
            foreach (var mod in OptionalMods)
            {
                if (mod == null)
                    continue;

                mod.Name = mod.Name?.Trim() ?? string.Empty;
                mod.Description = mod.Description?.Trim() ?? string.Empty;
                mod.Group = mod.Group?.Trim() ?? string.Empty;

                // Missing or unknown "type" keeps the classic checkbox behaviour.
                mod.Type = string.Equals(mod.Type?.Trim(), "radio", StringComparison.OrdinalIgnoreCase)
                    ? "radio"
                    : "checkbox";

                var options = new List<ModOption>();

                if (mod.Options != null)
                {
                    foreach (var option in mod.Options)
                    {
                        if (option == null)
                            continue;

                        option.Name = option.Name?.Trim() ?? string.Empty;
                        option.FilePrefixes = (option.FilePrefixes ?? new List<string>())
                            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                            .Select(prefix => prefix.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (string.IsNullOrWhiteSpace(option.Name) || option.FilePrefixes.Count == 0)
                            continue;

                        if (options.Any(existing =>
                                string.Equals(existing.Name, option.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        options.Add(option);
                    }
                }

                mod.Options = options;

                // An empty option list (or a single option) cannot form a radio
                // group, so fall back to a normal checkbox.
                if (mod.Type == "radio" && options.Count < 2)
                    mod.Type = "checkbox";

                mod.FilePrefixes = (mod.FilePrefixes ?? new List<string>())
                    .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                    .Select(prefix => prefix.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Radio mods declare prefixes per option; keep the aggregate list
                // populated for generic file helpers.
                if (mod.IsRadio && mod.FilePrefixes.Count == 0)
                {
                    mod.FilePrefixes = options
                        .SelectMany(option => option.FilePrefixes)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (string.IsNullOrWhiteSpace(mod.Name) || mod.FilePrefixes.Count == 0)
                    continue;

                normalizedMods.Add(mod);
            }
        }

        OptionalMods = normalizedMods;
    }
}