using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EscapeBook.Services;

/// <summary>
/// Result of an auth API call. <see cref="SessionToken"/> is only set on a
/// successful OTP confirmation and must never be logged or shown in the UI.
/// </summary>
internal sealed class AuthResult
{
    public bool Success { get; init; }
    public string ErrorReason { get; init; } = string.Empty;
    public string SessionToken { get; init; } = string.Empty;
    public string BotUsername { get; init; } = string.Empty;

    public static AuthResult Ok(string sessionToken = "", string botUsername = "") =>
        new() { Success = true, SessionToken = sessionToken, BotUsername = botUsername };

    public static AuthResult Fail(string reason) =>
        new() { Success = false, ErrorReason = reason };
}

/// <summary>Account-link flags for a trusted device (IDs are never returned).</summary>
internal sealed class LinksResult
{
    public bool Success { get; init; }
    public string ErrorReason { get; init; } = string.Empty;
    public bool TgLinked { get; init; }
    public bool DsLinked { get; init; }
    public string BotUsername { get; init; } = string.Empty;

    public static LinksResult Ok(bool tgLinked, bool dsLinked, string botUsername) =>
        new() { Success = true, TgLinked = tgLinked, DsLinked = dsLinked, BotUsername = botUsername };

    public static LinksResult Fail(string reason) =>
        new() { Success = false, ErrorReason = reason };
}

/// <summary>
/// Client for the EscapeBook auth worker (Cloudflare Worker + D1):
/// OTP requests, OTP confirmation and device (HWID) registration.
/// </summary>
internal static class AuthService
{
    // Single place to change the auth API endpoint later
    // (e.g. when moving to a custom domain).
    private const string ApiBaseUrl = "https://escapebook-auth.fireman777sasha.workers.dev";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = new() { Timeout = RequestTimeout };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string? _cachedHwid;

    // ------------------------------------------------------------- API calls

    public static async Task<AuthResult> RequestOTPAsync(string mcNickname, string hwid)
    {
        var payload = new NicknameHwidRequest
        {
            McNickname = mcNickname,
            Hwid = hwid
        };

        return await PostAsync("/auth/request-otp", payload);
    }

    /// <summary>
    /// Second OTP channel: the code is delivered via a Discord DM from the bot.
    /// Confirmation uses the same <see cref="ConfirmOTPAsync"/> (auto-trust).
    /// </summary>
    public static async Task<AuthResult> RequestOtpDiscordAsync(string mcNickname, string hwid)
    {
        var payload = new NicknameHwidRequest
        {
            McNickname = mcNickname,
            Hwid = hwid
        };

        return await PostAsync("/auth/request-otp-discord", payload);
    }

    public static async Task<AuthResult> ConfirmOTPAsync(string mcNickname, string code, string hwid)
    {
        // The server auto-trusts this HWID on successful confirmation,
        // so no separate RegisterDevice call is needed afterwards.
        var payload = new NicknameCodeRequest
        {
            McNickname = mcNickname,
            Code = code,
            Hwid = hwid
        };

        return await PostAsync("/auth/confirm-otp", payload);
    }

    private static async Task<AuthResult> PostAsync(string path, object payload)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(ApiBaseUrl + path, payload, JsonOptions);
            string body = await response.Content.ReadAsStringAsync();

            ApiResponse? parsed = TryParse(body);
            if (parsed != null)
            {
                return parsed.Ok
                    ? AuthResult.Ok(parsed.SessionToken ?? string.Empty, parsed.BotUsername ?? string.Empty)
                    : AuthResult.Fail(parsed.Reason ?? "unknown_error");
            }

            // Non-JSON body (proxy error, worker crash, ...) — do not expose details.
            AppLog.Write($"Auth API {path}: unexpected response, HTTP {(int)response.StatusCode}");
            return AuthResult.Fail("server_unavailable");
        }
        catch (TaskCanceledException)
        {
            // HttpClient timeout.
            return AuthResult.Fail("timeout");
        }
        catch (HttpRequestException)
        {
            return AuthResult.Fail("network");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Auth API {path}", ex);
            return AuthResult.Fail("internal_error");
        }
    }

    private static ApiResponse? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ApiResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks the auth worker which external accounts are linked to this player.
    /// Only a trusted device (nickname + HWID) gets an answer; IDs themselves
    /// are never returned.
    /// </summary>
    public static async Task<LinksResult> GetLinksAsync(string mcNickname, string hwid)
    {
        var payload = new NicknameHwidRequest
        {
            McNickname = mcNickname,
            Hwid = hwid
        };

        try
        {
            using var response = await Http.PostAsJsonAsync(
                ApiBaseUrl + "/auth/links", payload, JsonOptions);
            string body = await response.Content.ReadAsStringAsync();

            LinksResponse? parsed = TryParseLinks(body);
            if (parsed != null)
            {
                return parsed.Ok
                    ? LinksResult.Ok(parsed.TgLinked, parsed.DsLinked, parsed.BotUsername ?? string.Empty)
                    : LinksResult.Fail(parsed.Reason ?? "unknown_error");
            }

            AppLog.Write($"Auth API /auth/links: unexpected response, HTTP {(int)response.StatusCode}");
            return LinksResult.Fail("server_unavailable");
        }
        catch (TaskCanceledException)
        {
            return LinksResult.Fail("timeout");
        }
        catch (HttpRequestException)
        {
            return LinksResult.Fail("network");
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth API /auth/links", ex);
            return LinksResult.Fail("internal_error");
        }
    }

    private static LinksResponse? TryParseLinks(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LinksResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ HWID

    /// <summary>
    /// Stable hardware ID: SHA-256 of (CPU ProcessorId + motherboard SerialNumber),
    /// first 32 hex chars. Falls back to machine + user name when WMI is unavailable.
    /// The result is cached for the process lifetime.
    /// </summary>
    public static string GetHWID()
    {
        if (!string.IsNullOrEmpty(_cachedHwid))
            return _cachedHwid;

        string raw;
        try
        {
            raw = CollectWmiHardwareId();
        }
        catch (Exception ex)
        {
            AppLog.Error("HWID WMI collect", ex);
            raw = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Fallback for systems without WMI access.
            raw = Environment.MachineName + "|" + Environment.UserName;
        }

        _cachedHwid = HashFirst32Hex(raw);
        return _cachedHwid;
    }

    private static string CollectWmiHardwareId()
    {
        var builder = new StringBuilder();

        using (var cpuSearcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
        {
            foreach (ManagementBaseObject mo in cpuSearcher.Get())
            {
                builder.Append(mo["ProcessorId"]);
                break;
            }
        }

        using (var boardSearcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
        {
            foreach (ManagementBaseObject mo in boardSearcher.Get())
            {
                builder.Append('|').Append(mo["SerialNumber"]);
                break;
            }
        }

        return builder.ToString();
    }

    private static string HashFirst32Hex(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    // ------------------------------------------------------ user-facing text

    /// <summary>
    /// Maps API/network failure reasons to Russian UI messages.
    /// </summary>
    public static string DescribeError(string reason) => reason switch
    {
        "invalid_nickname" => "Некорректный никнейм (3–16 символов: A-Z, a-z, 0-9, _).",
        "invalid_request" => "Некорректный запрос. Попробуйте ещё раз.",
        "rate_limited" => "Слишком много запросов. Подождите 10 минут.",
        "player_not_found" => "Игрок не найден в базе. Попросите администратора выполнить /add и /link в Discord.",
        "banned" => "Этот аккаунт заблокирован.",
        "no_tg_linked" => "К этому аккаунту не привязан Telegram. Используйте команду /link в Discord.",
        "no_otp_requested" => "Сначала запросите код подтверждения.",
        "otp_expired" => "Срок действия кода истёк. Запросите новый код.",
        "too_many_attempts" => "Слишком много попыток ввода. Запросите новый код.",
        "invalid_code" => "Неверный код. Проверьте сообщение в Telegram.",
        "invalid_session" => "Сессия недействительна. Запросите код заново.",
        "tg_not_started" => $"Бот не может написать вам первым. Откройте @{AuthLinks.FallbackBotUsername} в Telegram, нажмите «Запустить» (/start) и запросите код снова.",
        "tg_delivery_failed" => $"Не удалось отправить код в Telegram. Попробуйте позже или напишите {AuthLinks.AdminContact}.",
        "no_ds_linked" => "К этому аккаунту не привязан Discord. Используйте команду /link в Discord.",
        "ds_dm_blocked" => "Бот не может написать вам в Discord. Откройте настройки конфиденциальности сервера EscapeBook и разрешите ЛС от участников.",
        "ds_delivery_failed" => $"Не удалось отправить код в Discord. Попробуйте позже или напишите {AuthLinks.AdminContact}.",
        "unauthorized" => "Доступ запрещён.",
        "network" => "Нет подключения к интернету.",
        "timeout" => "Сервер недоступен. Повторите попытку позже.",
        "server_unavailable" => "Сервер недоступен. Повторите попытку позже.",
        "internal_error" => "Внутренняя ошибка. Повторите попытку позже.",
        _ => "Неизвестная ошибка. Повторите попытку позже."
    };

    // ------------------------------------------------------------------- DTO

    private sealed class NicknameHwidRequest
    {
        [JsonPropertyName("mc_nickname")]
        public string McNickname { get; set; } = string.Empty;

        [JsonPropertyName("hwid")]
        public string Hwid { get; set; } = string.Empty;
    }

    private sealed class NicknameCodeRequest
    {
        [JsonPropertyName("mc_nickname")]
        public string McNickname { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("hwid")]
        public string Hwid { get; set; } = string.Empty;
    }

    private sealed class ApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("session_token")]
        public string? SessionToken { get; set; }

        [JsonPropertyName("bot_username")]
        public string? BotUsername { get; set; }
    }

    private sealed class LinksResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("tg_linked")]
        public bool TgLinked { get; set; }

        [JsonPropertyName("ds_linked")]
        public bool DsLinked { get; set; }

        [JsonPropertyName("bot_username")]
        public string? BotUsername { get; set; }
    }
}
