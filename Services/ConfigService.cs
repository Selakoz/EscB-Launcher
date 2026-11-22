using System.Text.Json;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class ConfigService
{
    /// <summary>
    /// TODO: замените на реальный URL вашего config.json в GitHub.
    /// Например: https://raw.githubusercontent.com/your-account/your-repo/main/config.json
    /// </summary>
    public const string CONFIG_URL =
        "https://raw.githubusercontent.com/Selakoz/EscB-Launcher/refs/heads/main/config.json";

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

        var errors = new List<string>();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            using var response = await _httpClient.GetAsync(
                CONFIG_URL,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var config = Deserialize(json);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            errors.Add("таймаут загрузки (20 секунд)");
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            AppLog.Error("Config download", ex);
        }

        var fallbacks = new[]
        {
            (Path: LauncherPaths.ConfigCacheFile, Label: "кэш конфигурации"),
            (Path: Path.Combine(AppContext.BaseDirectory, "config.json"), Label: "локальный config.json")
        };

        foreach (var fallback in fallbacks)
        {
            if (!File.Exists(fallback.Path))
                continue;

            try
            {
                string json = await File.ReadAllTextAsync(fallback.Path, cancellationToken);
                var config = Deserialize(json);
                progress.Report(new LaunchProgress(6, $"Использую {fallback.Label}"));
                AppLog.Write(
                    $"Config loaded from {fallback.Label}: builds={config.Builds.Count}, " +
                    $"jvmPreset={config.JvmPresetVersion}");
                return config;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{fallback.Label}: {ex.Message}");
                AppLog.Error($"Config fallback ({fallback.Label})", ex);
            }
        }

        throw new InvalidOperationException(
            "Не удалось загрузить конфигурацию лаунчера.\n\n" +
            string.Join("\n", errors.Distinct()) +
            "\n\nПроверьте CONFIG_URL и доступ к интернету. " +
            "Локальный файл config.json также должен лежать рядом с EscapeBook.exe.");
    }

    private static LauncherConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions)
                     ?? throw new InvalidDataException("JSON конфигурации пуст.");

        config.NormalizeAndValidate();
        return config;
    }
}