using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EscapeBookBootstrapper;

/// <summary>
/// Лёгкий двухшаговый загрузчик (только встроенные API .NET Framework 4.8):
///   1. Запрашивает у GitHub Releases API последний релиз лаунчера.
///   2. Скачивает <c>EscBClient.zip</c>, если удалённый тег отличается от
///      локального маркера, разворачивает его в %APPDATA%\.escapebook\launcher
///      и запускает <c>EscBClient.exe</c>.
/// Итоговый EXE самодостаточен (&lt;1 МБ) и работает на чистой Windows 10/11.
/// </summary>
internal static class Bootstrapper
{
    // ЗАМЕНИТЕ YOUR_ACCOUNT и YOUR_REPO на репозиторий, в котором публикуются
    // релизы (ассеты EscapeBookBootstrapper.exe и EscBClient.zip).
    private const string GitHubOwner = "YOUR_ACCOUNT";
    private const string GitHubRepo = "YOUR_REPO";

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
        // GitHub требует TLS 1.2+; на .NET Framework включаем его явно.
        ServicePointManager.SecurityProtocol =
            ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

        Directory.CreateDirectory(LauncherDirectory);
        CleanupLeftoverBackups();

        Log("=== Bootstrapper started ===");
        Log($"Launcher directory: {LauncherDirectory}");
        Log($"API: {LatestReleaseApiUrl}");

        bool updated = false;

        try
        {
            ReleaseInfo? release = await FetchLatestReleaseAsync();
            string? assetUrl = release?.AssetUrl;

            if (release is null)
            {
                Log("Release information unavailable; will try the installed client.");
            }
            else if (assetUrl == null || assetUrl.Trim().Length == 0)
            {
                Log($"Asset '{ReleaseAssetName}' not found in release '{release.TagName}'.");
            }
            else
            {
                string localTag = ReadLocalVersion();

                if (IsRemoteNewer(release.TagName, localTag))
                {
                    Log($"Update required: local='{localTag}', remote='{release.TagName}'.");
                    await DownloadAndApplyAsync(assetUrl, release.TagName);
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

        // GitHub требует заголовок User-Agent для запросов к API.
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

        string json = await response.Content.ReadAsStringAsync();

        // Разбор минимально необходимого подмножества ответа встроенным
        // JavaScriptSerializer (без NuGet и без рефлексии System.Text.Json).
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        object? parsed = serializer.DeserializeObject(json);

        if (parsed is not Dictionary<string, object> root)
        {
            Log("GitHub API returned an unexpected payload.");
            return null;
        }

        string tagName = root.TryGetValue("tag_name", out object? tagValue)
            ? Convert.ToString(tagValue) ?? string.Empty
            : string.Empty;

        string? assetUrl = null;

        if (root.TryGetValue("assets", out object? assetsValue) && assetsValue is ArrayList assets)
        {
            foreach (object? entry in assets)
            {
                if (entry is not Dictionary<string, object> asset)
                    continue;

                string name = asset.TryGetValue("name", out object? nameValue)
                    ? Convert.ToString(nameValue) ?? string.Empty
                    : string.Empty;

                if (!string.Equals(name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                assetUrl = asset.TryGetValue("browser_download_url", out object? urlValue)
                    ? Convert.ToString(urlValue)
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

            // В .NET Framework 4.8 нет перегрузки ExtractToDirectory с overwrite —
            // распаковываем вручную с перезаписью существующих файлов.
            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // Записи-каталоги (пустое имя) пропускаем: папки создаются ниже.
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destinationPath = Path.Combine(stagingDirectory, entry.FullName);
                    string? destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrEmpty(destinationDirectory))
                        Directory.CreateDirectory(destinationDirectory);

                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

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

        using var input = await response.Content.ReadAsStreamAsync();
        using var output = new FileStream(
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

        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await output.WriteAsync(buffer, 0, read);
            totalRead += read;

            if (!totalBytes.HasValue || totalBytes.Value <= 0)
                continue;

            long rawPercent = totalRead * 100 / totalBytes.Value;
            int percent = (int)Math.Min(100L, Math.Max(0L, rawPercent));

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
            string relativePath = sourceFile
                .Substring(payloadRoot.Length)
                .TrimStart('\\', '/');
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
    /// Возвращает папку, в которой фактически лежит EscBClient.exe, чтобы ZIP-ы,
    /// созданные через «Отправить → Сжатая ZIP-папка» (все файлы в одной
    /// вложенной папке), тоже работали.
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
    /// Копирует файл в папку лаунчера. Запущенный EscBClient.exe (или
    /// загруженную DLL) нельзя перезаписать, поэтому старый файл сначала
    /// переименовывается в сторону и удаляется afterwards.
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

        // Несопоставимые теги (например, "season-2") считаем обновлением.
        return true;
    }

    private static bool TryParseVersion(string tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        string cleaned = tag.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(1);

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
        new HttpClient { Timeout = timeout };

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
            // Логирование не должно ронять загрузчик.
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
            // Best effort (файл может быть занят).
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

    private sealed class ReleaseInfo
    {
        public ReleaseInfo(string tagName, string? assetUrl)
        {
            TagName = tagName;
            AssetUrl = assetUrl;
        }

        public string TagName { get; }

        public string? AssetUrl { get; }
    }
}
