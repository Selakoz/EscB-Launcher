using System.Text.Json;
using System.Text.Json.Serialization;
using EscapeBook.Models;

namespace EscapeBook.Services;

/// <summary>
/// Persisted authorization session (auth_session.json).
/// The session token must never be logged or displayed in the UI.
/// </summary>
internal sealed class SessionData
{
    [JsonPropertyName("mc_nickname")]
    public string McNickname { get; set; } = string.Empty;

    [JsonPropertyName("session_token")]
    public string SessionToken { get; set; } = string.Empty;

    [JsonPropertyName("hwid")]
    public string HWID { get; set; } = string.Empty;

    [JsonPropertyName("saved_at_unix")]
    public long SavedAtUnix { get; set; }
}

/// <summary>
/// Saves/loads/clears %APPDATA%\.escapebook\auth_session.json.
/// </summary>
internal static class SessionManager
{
    /// <summary>
    /// Raised after a session is saved or cleared, so UI (main window status
    /// indicator) can react immediately to auth changes.
    /// </summary>
    public static event EventHandler? AuthChanged;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void SaveSession(string mcNickname, string sessionToken, string hwid)
    {
        try
        {
            LauncherPaths.EnsureRoot();

            var data = new SessionData
            {
                McNickname = mcNickname,
                SessionToken = sessionToken,
                HWID = hwid,
                SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string json = JsonSerializer.Serialize(data, Options);
            string tempPath = LauncherPaths.AuthSessionFile + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, LauncherPaths.AuthSessionFile, overwrite: true);

            // Mirror the token into user_settings.json immediately so external
            // tools (e.g. the skin upload page) can read it. Never logged.
            PersistSessionToken(sessionToken);

            RaiseAuthChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth session save", ex);
        }
    }

    /// <summary>
    /// Returns the stored session, or null when the file is missing, corrupt
    /// or incomplete.
    /// </summary>
    public static SessionData? LoadSession()
    {
        try
        {
            if (!File.Exists(LauncherPaths.AuthSessionFile))
                return null;

            string json = File.ReadAllText(LauncherPaths.AuthSessionFile);
            SessionData? data = JsonSerializer.Deserialize<SessionData>(json, Options);

            if (data == null ||
                string.IsNullOrWhiteSpace(data.McNickname) ||
                string.IsNullOrWhiteSpace(data.SessionToken) ||
                string.IsNullOrWhiteSpace(data.HWID))
            {
                return null;
            }

            return data;
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth session load", ex);
            return null;
        }
    }

    public static void ClearSession()
    {
        bool cleared = false;

        try
        {
            if (File.Exists(LauncherPaths.AuthSessionFile))
                File.Delete(LauncherPaths.AuthSessionFile);

            cleared = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth session clear", ex);
        }

        // Only refresh the UI when the session is actually gone; otherwise the
        // handler would load the old file and re-authorize the window.
        if (cleared)
        {
            // Remove the token from user_settings.json as well so external
            // tools cannot keep using a logged-out session.
            PersistSessionToken(string.Empty);
            RaiseAuthChanged();
        }
    }

    /// <summary>
    /// Writes (or clears, when <paramref name="sessionToken"/> is empty) the
    /// <c>session_token</c> field in user_settings.json. The token value is
    /// secret and must never be written to the log.
    /// </summary>
    private static void PersistSessionToken(string sessionToken)
    {
        try
        {
            UserSettings settings = SettingsService.Load();

            if (string.Equals(settings.SessionToken, sessionToken, StringComparison.Ordinal))
                return;

            settings.SessionToken = sessionToken;

            if (!SettingsService.TrySave(settings, out string? error) &&
                !string.IsNullOrEmpty(error))
            {
                AppLog.Write("Session token settings save warning: " + error);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Session token settings persist", ex);
        }
    }

    private static void RaiseAuthChanged()
    {
        try
        {
            AuthChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A UI handler must never break session persistence/logout.
            AppLog.Error("Auth state event", ex);
        }
    }
}
