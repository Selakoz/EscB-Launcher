using System.Text;
using System.Text.Json;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class TelemetryService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task SendAsync(
        LauncherConfig config,
        string nickname,
        HardwareInfo hardware,
        int allocatedRamMb,
        string javaPath,
        string jvmArguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.DiscordWebhookUrl))
            return;

        try
        {
            string gpuText = hardware.Gpus.Count == 0
                ? "Неизвестно"
                : string.Join("\n", hardware.Gpus.Select(gpu =>
                    gpu.VramMb is > 0
                        ? $"{gpu.Name} ({gpu.VramMb} МБ)"
                        : gpu.Name));

            string ramText =
                $"{hardware.TotalRamMb:N0} МБ всего\n" +
                $"{allocatedRamMb:N0} МБ выделено игре\n" +
                (hardware.AvailableRamMb > 0
                    ? $"{hardware.AvailableRamMb:N0} МБ свободно"
                    : "свободно: н/д");

            var payload = new
            {
                username = "EscapeBook Client",
                embeds = new object[]
                {
                    new
                    {
                        title = "🚀 Запуск Minecraft",
                        description = $"Игрок **{nickname}** запускает клиент EscapeBook.",
                        color = 0x7C5CFF,
                        timestamp = DateTimeOffset.UtcNow,
                        fields = new object[]
                        {
                            new { name = "Никнейм", value = nickname, inline = true },
                            new
                            {
                                name = "Версия",
                                value = $"Minecraft {FirstBuild(config)?.McVersion ?? "?"} · сборка {FirstBuild(config)?.Version ?? "?"}",
                                inline = true
                            },
                            new { name = "Процессор", value = Truncate(hardware.Cpu, 1000), inline = false },
                            new { name = "Видеокарты", value = Truncate(gpuText, 1000), inline = false },
                            new { name = "ОЗУ", value = ramText, inline = false },
                            new { name = "Java", value = Truncate(javaPath, 1000), inline = false },
                            new
                            {
                                name = "Аргументы JVM",
                                value = $"```{Truncate(jvmArguments, 950)}```",
                                inline = false
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(
                config.DiscordWebhookUrl,
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                AppLog.Write(
                    $"Webhook failed: {(int)response.StatusCode} {response.ReasonPhrase}; " +
                    Truncate(body, 500));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Telemetry must never prevent the game from starting.
            AppLog.Error("Telemetry send", ex);
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        value = value.Trim();
        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 1)] + "…";
    }

    private static BuildInfo? FirstBuild(LauncherConfig config) =>
        config.Builds.FirstOrDefault();
}