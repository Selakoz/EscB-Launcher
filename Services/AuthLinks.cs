using System.Diagnostics;

namespace EscapeBook.Services;

/// <summary>
/// Single source of truth for user-facing contact/link constants used by the
/// auth and profile-linking UI. No scattered literals elsewhere.
/// </summary>
internal static class AuthLinks
{
    /// <summary>Administrator contact (Telegram and Discord).</summary>
    public const string AdminContact = "@Selakoz";

    /// <summary>
    /// Bot username used when the server did not provide TG_BOT_USERNAME.
    /// Single source for every user-facing bot reference in the launcher.
    /// </summary>
    public const string FallbackBotUsername = "escapebook_auth_bot";

    /// <summary>Normalizes "@name" / "name" / null into a bare username.</summary>
    public static string NormalizeBotUsername(string? botUsername)
    {
        string name = (botUsername ?? string.Empty).Trim().TrimStart('@');
        return name.Length > 0 ? name : FallbackBotUsername.Trim();
    }

    /// <summary>https://t.me/&lt;username&gt;?start=1, or empty when unknown.</summary>
    public static string BuildBotLink(string? botUsername)
    {
        string name = NormalizeBotUsername(botUsername);
        return name.Length == 0 ? string.Empty : $"https://t.me/{name}?start=1";
    }

    /// <summary>
    /// Opens the bot deep link in the default browser (Telegram shows the bot
    /// with the "Запустить" button). Returns false when the link is unknown
    /// or the shell failed — callers show the admin-contact hint instead.
    /// </summary>
    public static bool TryOpenBotInTelegram(string? botUsername)
    {
        string link = BuildBotLink(botUsername);
        if (link.Length == 0)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Open Telegram bot link", ex);
            return false;
        }
    }

    /// <summary>
    /// Launches the Discord desktop app (discord://). Returns false when the
    /// app is not installed or the shell failed.
    /// </summary>
    public static bool TryOpenDiscord()
    {
        try
        {
            Process.Start(new ProcessStartInfo("discord://") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Open Discord app", ex);
            return false;
        }
    }
}
