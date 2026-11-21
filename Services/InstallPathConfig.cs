namespace EscapeBook.Services;

/// <summary>
/// Shared installation root configuration. The path is written by the
/// bootstrapper to %LOCALAPPDATA%\EscB\install_path.txt and read by the
/// launcher so both processes use the same data directory.
/// </summary>
internal static class InstallPathConfig
{
    private const string ConfigFolderName = "EscB";
    private const string ConfigFileName = "install_path.txt";

    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ConfigFolderName);

    public static string ConfigFile { get; } = Path.Combine(ConfigDirectory, ConfigFileName);

    /// <summary>Default root: %APPDATA%\.escapebook.</summary>
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".escapebook");

    /// <summary>
    /// Warning produced by the last <see cref="ResolveRoot"/> call when the
    /// configured path was unreadable, invalid or not writable.
    /// </summary>
    public static string? LastWarning { get; private set; }

    /// <summary>
    /// Returns the configured installation root when it is valid and writable,
    /// otherwise the default root (%APPDATA%\.escapebook).
    /// </summary>
    public static string GetBasePath()
    {
        return ResolveRoot();
    }

    /// <summary>
    /// Returns the configured installation root when it is valid and writable,
    /// otherwise the default root (%APPDATA%\.escapebook).
    /// </summary>
    public static string ResolveRoot()
    {
        LastWarning = null;

        string? configured = ReadConfiguredPath(out string? warning);
        if (warning is not null)
            LastWarning = warning;

        return configured ?? DefaultRoot;
    }

    /// <summary>Checks whether the chosen directory exists (or can be created) and is writable.</summary>
    public static bool IsValidWritableDirectory(string path, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Путь не указан.";
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            error = "Путь должен быть абсолютным.";
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);

            string probeFile = Path.Combine(
                fullPath,
                ".escb_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");

            using (new FileStream(
                       probeFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigFile, fullPath);
    }

    private static string? ReadConfiguredPath(out string? warning)
    {
        warning = null;

        try
        {
            if (!File.Exists(ConfigFile))
                return null;

            string raw = File.ReadAllText(ConfigFile)
                .Trim()
                .TrimStart('\uFEFF')
                .Trim('"');

            if (string.IsNullOrWhiteSpace(raw))
            {
                warning = $"Файл {ConfigFile} пуст; используется папка по умолчанию {DefaultRoot}.";
                return null;
            }

            if (!Path.IsPathRooted(raw))
            {
                warning = $"Путь '{raw}' в {ConfigFile} не является абсолютным; используется папка по умолчанию {DefaultRoot}.";
                return null;
            }

            string fullPath = Path.GetFullPath(raw);

            if (!IsValidWritableDirectory(fullPath, out string error))
            {
                warning = $"Папка '{fullPath}' из {ConfigFile} недоступна для записи ({error}); используется папка по умолчанию {DefaultRoot}.";
                return null;
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            warning = $"Не удалось прочитать {ConfigFile} ({ex.Message}); используется папка по умолчанию {DefaultRoot}.";
            return null;
        }
    }
}
