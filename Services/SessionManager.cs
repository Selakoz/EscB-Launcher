using System.Text.Json;
using System.Text.Json.Serialization;

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
        try
        {
            if (File.Exists(LauncherPaths.AuthSessionFile))
                File.Delete(LauncherPaths.AuthSessionFile);
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth session clear", ex);
        }
    }
}
