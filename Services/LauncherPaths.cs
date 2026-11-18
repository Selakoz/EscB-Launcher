namespace EscapeBook.Services;

internal static class LauncherPaths
{
    /// <summary>Launcher data root: %APPDATA%\.escapebook.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".escapebook");

    public static string SettingsFile => Path.Combine(Root, "user_settings.json");
    public static string AuthSessionFile => Path.Combine(Root, "auth_session.json");
    public static string ConfigCacheFile => Path.Combine(Root, ".launcher_config.json");
    public static string LauncherLogFile => Path.Combine(Root, "launcher.log");
    public static string BannerFile => Path.Combine(Root, "assets", "banner.png");

    public static string InstancesDirectory => Path.Combine(Root, "instances");

    public static string GetInstancePath(string buildId) =>
        Path.Combine(InstancesDirectory, buildId);

    public static string GetModsPath(string buildId) =>
        Path.Combine(GetInstancePath(buildId), "mods");

    public static string GetVersionFile(string buildId) =>
        Path.Combine(GetInstancePath(buildId), ".client_version");

    /// <summary>
    /// Human-readable version marker used by <see cref="BuildStatusService"/>.
    /// Written next to the legacy <c>.client_version</c> marker after each download.
    /// </summary>
    public static string GetStatusVersionFile(string buildId) =>
        Path.Combine(GetInstancePath(buildId), "version.txt");

    public static string GetClientUrlFile(string buildId) =>
        Path.Combine(GetInstancePath(buildId), ".client_zip_url");

    public static string GetGameLogFile(string buildId) =>
        Path.Combine(GetInstancePath(buildId), "logs", "latest.log");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);

    public static void EnsureInstance(string buildId) =>
        Directory.CreateDirectory(GetInstancePath(buildId));

    public static void EnsureAssets() =>
        Directory.CreateDirectory(Path.Combine(Root, "assets"));
}