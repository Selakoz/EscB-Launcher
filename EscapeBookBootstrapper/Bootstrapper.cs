using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EscapeBookBootstrapper;

/// <summary>
/// Lightweight two-step launcher:
///   1. Asks the GitHub Releases API for the latest release of the launcher.
///   2. Downloads <c>EscBClient.zip</c> when the remote tag differs from the locally
///      stored tag, deploys it to %APPDATA%\.escapebook\launcher and starts
///      <c>EscBClient.exe</c>.
/// Uses only built-in .NET libraries so the published size stays well under 8 MB.
/// </summary>
internal static class Bootstrapper
{
    // TODO: replace the placeholders with the repository that publishes the releases.
    private const string GitHubOwner = "Selakoz";
    private const string GitHubRepo = "EscB-Launcher";

    private const string ReleaseAssetName = "EscBClient.zip";
    private const string MainExecutableName = "EscBClient.exe";

    private static readonly string LatestReleaseApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".escapebook");

    public static string LauncherDirectory { get; } = Path.Combine(RootDirectory, "launcher");

    public static string MainExecutablePath { get; } =
        Path.Combine(LauncherDirectory, MainExecutableName);

    public static string VersionFile { get; } =
        Path.Combine(LauncherDirectory, ".bootstrapper_version");

    public static string LogFile { get; } =
        Path.Combine(LauncherDirectory, "bootstrapper.log");

    public static async Task<int> RunAsync(string[] args)
    {
        Directory.CreateDirectory(LauncherDirectory);
        CleanupLeftoverBackups();

        Log("=== Bootstrapper started ===");
        Log($"Launcher directory: {LauncherDirectory}");
        Log($"API: {LatestReleaseApiUrl}");

        bool updated = false;

        try
        {
            ReleaseInfo? release = await FetchLatestReleaseAsync();

            if (release is null)
            {
                Log("Release information unavailable; will try the installed client.");
            }
            else if (string.IsNullOrWhiteSpace(release.AssetUrl))
            {
                Log($"Asset '{ReleaseAssetName}' not found in release '{release.TagName}'.");
            }
            else
            {
                string localTag = ReadLocalVersion();

                if (IsRemoteNewer(release.TagName, localTag))
                {
                    Log($"Update required: local='{localTag}', remote='{release.TagName}'.");
                    await DownloadAndApplyAsync(release.AssetUrl, release.TagName);
                    updated = true;
                }
                else
                {
                    Log($"Already up to date ('{localTag}').");
                }
            }
        }
        catch (Exception ex)
        {
            Log("Update failed: " + ex);
        }

        if (!File.Exists(MainExecutablePath))
        {
            Log($"Main executable not found: {MainExecutablePath}");
            return 1;
        }

        LaunchMainClient();
        Log(updated ? "Client updated and launched." : "Client launched.");
        return 0;
    }

    // ------------------------------------------------------------------ release

    private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
    {
        using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);

        // GitHub requires a User-Agent header for API requests.
        request.Headers.UserAgent.ParseAdd("EscB-Bootstrapper/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            Log($"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        JsonElement root = document.RootElement;

        string tagName = root.TryGetProperty("tag_name", out JsonElement tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;

        string? assetUrl = null;

        if (root.TryGetProperty("assets", out JsonElement assets) &&
            assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.Equals(name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                assetUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;

                break;
            }
        }

        Log($"Latest release tag: '{tagName}'.");
        return new ReleaseInfo(tagName, assetUrl);
    }

    // ----------------------------------------------------------------- download

    private static async Task DownloadAndApplyAsync(string assetUrl, string remoteTag)
    {
        string tempZip = Path.Combine(
            Path.GetTempPath(),
            $"escb_client_{Guid.NewGuid():N}.zip");

        string stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"escb_stage_{Guid.NewGuid():N}");

        try
        {
            Console.WriteLine("Скачивание обновления...");
            await DownloadFileAsync(assetUrl, tempZip);

            if (!ArchiveContainsMainExecutable(tempZip))
            {
                throw new InvalidDataException(
                    $"Архив обновления не содержит {MainExecutableName}.");
            }

            Console.WriteLine("Распаковка обновления...");
            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(tempZip, stagingDirectory, overwriteFiles: true);

            DeployDirectory(stagingDirectory, LauncherDirectory);

            File.WriteAllText(VersionFile, remoteTag);
            Log($"Version marker updated to '{remoteTag}'.");
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static async Task DownloadFileAsync(string url, string destination)
    {
        using var client = CreateHttpClient(TimeSpan.FromMinutes(15));

        using var response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int lastReportedPercent = -1;
        int read;

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;

            if (totalBytes is not > 0)
                continue;

            int percent = (int)Math.Clamp(totalRead * 100 / totalBytes.Value, 0L, 100L);

            if (percent >= lastReportedPercent + 10 || percent == 100)
            {
                lastReportedPercent = percent;
                Console.WriteLine($"  Загрузка: {percent}%");
            }
        }
    }

    // ------------------------------------------------------------------- deploy

    private static void DeployDirectory(string stagingDirectory, string targetDirectory)
    {
        string payloadRoot = ResolvePayloadRoot(stagingDirectory);
        int copied = 0;

        foreach (string sourceFile in Directory.EnumerateFiles(
                     payloadRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(payloadRoot, sourceFile);
            string targetFile = Path.Combine(targetDirectory, relativePath);

            string? targetSubDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetSubDirectory))
                Directory.CreateDirectory(targetSubDirectory);

            CopyWithLockHandling(sourceFile, targetFile);
            copied++;
        }

        Log($"Deployed {copied} file(s).");
    }

    /// <summary>
    /// Returns the folder that actually contains EscBClient.exe, so ZIPs created by
    /// "send to compressed folder" (which wrap everything in one directory) also work.
    /// </summary>
    private static string ResolvePayloadRoot(string stagingDirectory)
    {
        if (File.Exists(Path.Combine(stagingDirectory, MainExecutableName)))
            return stagingDirectory;

        string? nested = Directory
            .EnumerateFiles(stagingDirectory, MainExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (nested is null)
            throw new FileNotFoundException(
                $"Архив обновления не содержит {MainExecutableName}.");

        return Path.GetDirectoryName(nested)!;
    }

    /// <summary>
    /// Copies a file into the launcher directory. A running EscBClient.exe (or a loaded
    /// DLL) cannot be overwritten, so the old file is renamed aside first and removed
    /// afterwards.
    /// </summary>
    private static void CopyWithLockHandling(string sourceFile, string targetFile)
    {
        const int attempts = 3;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Copy(sourceFile, targetFile, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(400);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(400);
            }
        }

        string backupFile = targetFile + ".old";
        TryDeleteFile(backupFile);

        try
        {
            if (File.Exists(targetFile))
            {
                File.Move(targetFile, backupFile);
                Log($"Moved locked file aside: {targetFile}");
            }
        }
        catch (Exception ex)
        {
            Log($"Could not move locked file '{targetFile}': {ex.Message}");
        }

        File.Copy(sourceFile, targetFile, overwrite: true);
        TryDeleteFile(backupFile);
    }

    private static void CleanupLeftoverBackups()
    {
        try
        {
            foreach (string backup in Directory.EnumerateFiles(
                         LauncherDirectory,
                         "*.old",
                         SearchOption.TopDirectoryOnly))
            {
                TryDeleteFile(backup);
            }
        }
        catch (Exception ex)
        {
            Log("Backup cleanup failed: " + ex.Message);
        }
    }

    private static bool ArchiveContainsMainExecutable(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        return archive.Entries.Any(entry =>
            !string.IsNullOrEmpty(entry.Name) &&
            string.Equals(entry.Name, MainExecutableName, StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------- version/store

    private static string ReadLocalVersion()
    {
        try
        {
            return File.Exists(VersionFile)
                ? File.ReadAllText(VersionFile).Trim()
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log($"Could not read local version: {ex.Message}");
            return string.Empty;
        }
    }

    private static bool IsRemoteNewer(string remoteTag, string localTag)
    {
        if (string.IsNullOrWhiteSpace(localTag))
            return true;

        if (string.Equals(remoteTag, localTag, StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryParseVersion(remoteTag, out Version? remote) &&
            TryParseVersion(localTag, out Version? local))
        {
            return remote.CompareTo(local) > 0;
        }

        // Non-comparable tags (e.g. "season-2") are treated as an update.
        return true;
    }

    private static bool TryParseVersion(string tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        string cleaned = tag.Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
            cleaned = cleaned[1..];

        return Version.TryParse(cleaned, out version);
    }

    // ---------------------------------------------------------------- launching

    private static void LaunchMainClient()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = MainExecutablePath,
            WorkingDirectory = LauncherDirectory,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    // -------------------------------------------------------------------- infra

    private static HttpClient CreateHttpClient(TimeSpan timeout) =>
        new() { Timeout = timeout };

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(LauncherDirectory);
            File.AppendAllText(
                LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the bootstrapper.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort (the file may be in use).
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private sealed record ReleaseInfo(string TagName, string? AssetUrl);
}