using System.Text.Json;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class ConfigService
{
    /// <summary>
    /// Remote launcher configuration. This is the source of build URLs.
    /// </summary>
    public const string CONFIG_URL =
        "https://raw.githubusercontent.com/Selakoz/EscB-Launcher/main/config.json";

    /// <summary>
    /// Maximum age of the local configuration cache. A newer cache is reused
    /// without a network request; an older one is refreshed silently, so a
    /// corrected URL in <c>config.json</c> reaches players automatically.
    /// </summary>
    public const int ConfigCacheMaxAgeHours = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;

    public ConfigService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LauncherConfig> LoadAsync(
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new LaunchProgress(2, "Загрузка конфигурации с GitHub..."));

        // A fresh local cache avoids a network round-trip on every launch.
        // Missing/corrupted entries and entries older than the TTL fall through
        // to a real download, which is how stale build URLs self-heal.
        LauncherConfig? cachedConfig = TryLoadCachedConfig(requireFresh: true);
        if (cachedConfig != null)
        {
            AppLog.Write(
                $"Config loaded from local cache (<= {ConfigCacheMaxAgeHours} h): " +
                $"builds={cachedConfig.Builds.Count}, jvmPreset={cachedConfig.JvmPresetVersion}");
            return cachedConfig;
        }

        string json;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            using var response = await _httpClient.GetAsync(
                CONFIG_URL,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            LauncherConfig? fallback = TryLoadCachedConfig(requireFresh: false);
            if (fallback != null)
            {
                AppLog.Write("Config download timed out; using cached config: " + ex.Message);
                return fallback;
            }

            throw new InvalidOperationException(
                "Ошибка: не удалось загрузить свежий config.json с GitHub (таймаут 20 секунд).\n" +
                $"URL: {CONFIG_URL}",
                ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("Config download", ex);

            LauncherConfig? fallback = TryLoadCachedConfig(requireFresh: false);
            if (fallback != null)
            {
                AppLog.Write("Config download failed; using cached config: " + ex.Message);
                return fallback;
            }

            throw new InvalidOperationException(
                "Ошибка: не удалось загрузить свежий config.json с GitHub.\n" +
                $"URL: {CONFIG_URL}\n" +
                $"Причина: {ex.Message}",
                ex);
        }

        LauncherConfig config = Deserialize(json);

        try
        {
            LauncherPaths.EnsureRoot();
            await File.WriteAllTextAsync(LauncherPaths.ConfigCacheFile, json, CancellationToken.None);
            File.SetLastWriteTimeUtc(LauncherPaths.ConfigCacheFile, DateTime.UtcNow);
        }
        catch (Exception cacheError)
        {
            AppLog.Error("Config cache write", cacheError);
        }

        AppLog.Write(
            $"Config loaded from GitHub: builds={config.Builds.Count}, " +
            $"jvmPreset={config.JvmPresetVersion}");
        return config;
    }

    /// <summary>
    /// Reads the local configuration cache. When <paramref name="requireFresh"/>
    /// is set, an entry older than <see cref="ConfigCacheMaxAgeHours"/> is
    /// treated as missing so the caller re-downloads it. Any read/parse failure
    /// also returns null instead of surfacing to the UI.
    /// </summary>
    private static LauncherConfig? TryLoadCachedConfig(bool requireFresh)
    {
        try
        {
            string cacheFile = LauncherPaths.ConfigCacheFile;

            if (!File.Exists(cacheFile))
                return null;

            if (requireFresh)
            {
                DateTime lastWriteUtc = File.GetLastWriteTimeUtc(cacheFile);
                TimeSpan age = DateTime.UtcNow - lastWriteUtc;

                if (age > TimeSpan.FromHours(ConfigCacheMaxAgeHours))
                {
                    AppLog.Write(
                        $"Config cache is stale ({age.TotalHours:F1} h > " +
                        $"{ConfigCacheMaxAgeHours} h); re-downloading.");
                    return null;
                }
            }

            string json = File.ReadAllText(cacheFile);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return Deserialize(json);
        }
        catch (Exception ex)
        {
            AppLog.Write("Config cache read failed: " + ex.Message);
            return null;
        }
    }

    private static LauncherConfig Deserialize(string json)
    {
        LauncherConfig config;

        try
        {
            config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions)
                     ?? throw new InvalidDataException("JSON конфигурации пуст.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Ошибка: не удалось разобрать config.json, полученный с GitHub.\n" +
                ex.Message,
                ex);
        }

        config.NormalizeAndValidate();

        // Fail fast with a descriptive message instead of silently falling back
        // to an old URL when a build has no usable client_zip_url.
        foreach (BuildInfo build in config.Builds)
            ValidateClientZipUrl(build);

        foreach (SecretBuildInfo secret in config.SecretBuilds)
            ValidateClientZipUrl(secret.Build);

        return config;
    }

    private static void ValidateClientZipUrl(BuildInfo build)
    {
        if (string.IsNullOrWhiteSpace(build.ClientZipUrl))
        {
            throw new InvalidDataException(
                "Ошибка: не удалось получить URL сборки из config.json — " +
                $"поле client_zip_url пусто для сборки \"{build.Id}\".");
        }

        if (!Uri.TryCreate(build.ClientZipUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException(
                "Ошибка: не удалось получить URL сборки из config.json — " +
                $"некорректный client_zip_url для сборки \"{build.Id}\": {build.ClientZipUrl}");
        }
    }
}
