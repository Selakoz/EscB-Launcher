namespace EscapeBook.Services;

/// <summary>
/// Finds a system javaw.exe when CmlLib has no Mojang runtime installed.
/// MinecraftPath runtimes always have priority in LauncherService.
/// </summary>
internal static class JavaLocator
{
    public static string? FindSystemJavaw()
    {
        var candidates = new List<string>();

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.Combine(javaHome.Trim().Trim('"'), "bin", "javaw.exe"));

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (string directory in pathVariable.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim().Trim('"'), "javaw.exe"));
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        foreach (string root in GetJavaRoots())
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                string? found = Directory
                    .EnumerateFiles(root, "javaw.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (found != null)
                    candidates.Add(found);
            }
            catch
            {
                // Some vendor folders can be inaccessible; simply skip them.
            }
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetJavaRoots()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(programFiles, "Java");
        yield return Path.Combine(programFilesX86, "Java");
        yield return Path.Combine(programFiles, "Eclipse Adoptium");
        yield return Path.Combine(programFiles, "AdoptOpenJDK");
        yield return Path.Combine(programFiles, "Zulu");
        yield return Path.Combine(programFiles, "BellSoft");
        yield return Path.Combine(programFiles, "Amazon Corretto");
        yield return Path.Combine(programFiles, "Semeru");
        yield return Path.Combine(programFiles, "Microsoft");
        yield return Path.Combine(localAppData, "Programs", "Eclipse Adoptium");
        yield return Path.Combine(userProfile, ".jdks");
        yield return Path.Combine(userProfile, "scoop", "apps");
        yield return Path.Combine(localAppData, "Programs", "Microsoft");
        yield return Path.Combine(
            localAppData,
            "Packages",
            "Microsoft.4297127D64EC6_8wekyb3d8bbwe",
            "LocalCache",
            "Local",
            "runtime");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft",
            "runtime");
    }
}