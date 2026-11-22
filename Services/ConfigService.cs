using System.Text.Json;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class ConfigService
{
    /// <summary>
    /// Remote launcher configuration. This is the only source of build URLs:
    /// there is intentionally no local/cached/hardcoded fallback.
    /// </summary>
    public const string CONFIG_URL =
        "https://raw.githubusercontent.com/Selakoz/EscB-Launcher/main/config.json";

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
            throw new InvalidOperationException(
                "Ошибка: не удалось загрузить свежий config.json с GitHub (таймаут 20 секунд).\n" +
                $"URL: {CONFIG_URL}",
                ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("Config download", ex);
            throw new InvalidOperationException(
                "Ошибка: не удалось загрузить свежий config.json с GitHub.\n" +
                $"URL: {CONFIG_URL}\n" +
                $"Причина: {ex.Message}",
                ex);
        }

        // The cache is write-only (kept for diagnostics). It is never read as a
        // fallback, so a stale or corrupted .launcher_config.json can never
        // override the freshly downloaded remote configuration.
        LauncherConfig config = Deserialize(json);

        try
        {
            LauncherPaths.EnsureRoot();
            await File.WriteAllTextAsync(LauncherPaths.ConfigCacheFile, json, CancellationToken.None);
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
