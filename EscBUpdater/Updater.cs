using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EscBUpdater
{
    /// <summary>
    /// Лёгкий «проверяльщик обновлений». Запускается ярлыком с рабочего стола:
    ///   1. Запрашивает у GitHub Releases последний релиз.
    ///   2. Если тег новее локального <c>.bootstrapper_version</c> — скачивает
    ///      <c>EscBClient.zip</c>, чистит старую папку лаунчера (кроме
    ///      пользовательских файлов) и распаковывает архив.
    ///   3. Запускает <c>EscBClient.exe</c> и завершается.
    /// Вся логика намеренно повторяет загрузчик, но без UI и без лишних
    /// зависимостей: EXE остаётся меньше 1 МБ.
    /// </summary>
    internal static class Updater
    {
        // Репозиторий с релизами (общий с загрузчиком).
        private const string GitHubOwner = "Selakoz";
        private const string GitHubRepo = "EscB-Launcher";

        private const string ReleaseAssetName = "EscBClient.zip";
        private const string MainExecutableName = "EscBClient.exe";
        private const string UpdaterExecutableName = "EscBUpdater.exe";
        private const string VersionFileName = ".bootstrapper_version";

        private const int CopyBufferSize = 81920;

        private static readonly string LatestReleaseApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        /// <summary>Папка, в которой лежит сам EscBUpdater.exe (и клиент).</summary>
        public static string LauncherDirectory
        {
            get
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    baseDirectory = Path.GetDirectoryName(typeof(Updater).Assembly.Location);
                }

                return new DirectoryInfo(baseDirectory).FullName;
            }
        }

        public static string MainExecutablePath
        {
            get { return Path.Combine(LauncherDirectory, MainExecutableName); }
        }

        public static string VersionFile
        {
            get { return Path.Combine(LauncherDirectory, VersionFileName); }
        }

        public static async Task<int> RunAsync(string[] args)
        {
            Log("=== EscBUpdater запущен ===");
            Log("Папка лаунчера: " + LauncherDirectory);

            CleanupLeftoverFiles();

            bool updated = false;

            try
            {
                ReleaseInfo release = await FetchLatestReleaseAsync().ConfigureAwait(false);
                string assetUrl = release == null ? null : release.AssetUrl;

                if (release == null)
                {
                    Log("Не удалось получить информацию о релизе; пробуем запустить установленный клиент.");
                }
                else if (string.IsNullOrEmpty(assetUrl))
                {
                    Log("В релизе '" + release.TagName + "' не найден ассет '" + ReleaseAssetName + "'.");
                }
                else
                {
                    string localTag = ReadLocalVersion();

                    if (IsRemoteNewer(release.TagName, localTag))
                    {
                        Log("Требуется обновление: локально='" + localTag + "', удалённо='" + release.TagName + "'.");
                        await DownloadAndApplyAsync(assetUrl, release.TagName).ConfigureAwait(false);
                        updated = true;
                    }
                    else
                    {
                        Log("Обновление не требуется ('" + localTag + "').");
                    }
                }
            }
            catch (Exception ex)
            {
                // Сетевая/архивная ошибка не должна мешать запуску клиента.
                Log("Обновление не выполнено: " + ex);
            }

            if (!File.Exists(MainExecutablePath))
            {
                Log("Не найден основной файл: " + MainExecutablePath);
                return 1;
            }

            try
            {
                LaunchMainClient();
            }
            catch (Exception ex)
            {
                Log("Не удалось запустить клиент: " + ex);
                return 1;
            }

            Log(updated ? "Клиент обновлён и запущен." : "Клиент запущен.");
            return 0;
        }

        /// <summary>Имя мьютекса, чтобы два одновременных запуска не обновляли файлы.</summary>
        public static Mutex CreateSingleInstanceMutex()
        {
            // Хэш вместо самого пути: имя мьютекса ограничено 260 символами,
            // а путь установки может быть длинным.
            string key = LauncherDirectory.ToLowerInvariant();
            string suffix;

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder builder = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));

                suffix = builder.ToString();
            }

            return new Mutex(false, "Local\\EscBUpdater_" + suffix);
        }

        // ------------------------------------------------------------------ release

        private static async Task<ReleaseInfo> FetchLatestReleaseAsync()
        {
            using (HttpClient client = CreateHttpClient(TimeSpan.FromSeconds(30)))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl))
            {
                string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using (HttpResponseMessage response = await client
                           .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                           .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log("GitHub API вернул " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                        return null;
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseRelease(json);
                }
            }
        }

        /// <summary>
        /// Разбирает ответ GitHub через DataContractJsonSerializer
        /// (System.Runtime.Serialization входит в .NET Framework, поэтому
        /// дополнительных DLL рядом с EXE не нужно).
        /// </summary>
        private static ReleaseInfo ParseRelease(string json)
        {
            ReleaseDto dto;

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ReleaseDto));
                dto = (ReleaseDto)serializer.ReadObject(stream);
            }

            if (dto == null)
                return null;

            string tagName = dto.TagName ?? string.Empty;
            string assetUrl = null;

            if (dto.Assets != null)
            {
                foreach (AssetDto asset in dto.Assets)
                {
                    if (asset == null)
                        continue;

                    if (!string.Equals(asset.Name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                    {
                        assetUrl = asset.BrowserDownloadUrl;
                        break;
                    }
                }
            }

            Log("Тег последнего релиза: '" + tagName + "'.");
            return new ReleaseInfo(tagName, assetUrl);
        }

        // ----------------------------------------------------------------- download

        private static async Task DownloadAndApplyAsync(string assetUrl, string remoteTag)
        {
            string tempZip = Path.Combine(
                Path.GetTempPath(),
                "escb_updater_" + Guid.NewGuid().ToString("N") + ".zip");

            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "escb_updater_stage_" + Guid.NewGuid().ToString("N"));

            try
            {
                Log("Скачивание обновления...");
                await DownloadFileAsync(assetUrl, tempZip).ConfigureAwait(false);

                if (!ArchiveContainsMainExecutable(tempZip))
                {
                    throw new InvalidDataException(
                        "Архив обновления не содержит " + MainExecutableName + ".");
                }

                Log("Распаковка обновления...");
                Directory.CreateDirectory(stagingDirectory);
                ExtractArchive(tempZip, stagingDirectory);

                // Чистим старую версию только после успешной распаковки нового
                // архива: при сбое скачивания установка останется рабочей.
                Log("Очистка предыдущей версии...");
                ClearLauncherDirectory();

                DeployDirectory(stagingDirectory, LauncherDirectory);

                if (!File.Exists(MainExecutablePath))
                {
                    throw new FileNotFoundException(
                        "Не удалось установить " + MainExecutableName + ".",
                        MainExecutablePath);
                }

                File.WriteAllText(VersionFile, remoteTag);
                Log("Файл версии обновлён: '" + remoteTag + "'.");
            }
            finally
            {
                TryDeleteFile(tempZip);
                TryDeleteDirectory(stagingDirectory);
            }
        }

        private static async Task DownloadFileAsync(string url, string destination)
        {
            using (HttpClient client = CreateHttpClient(TimeSpan.FromMinutes(30)))
            using (HttpResponseMessage response = await client
                       .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream output = new FileStream(
                           destination,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           CopyBufferSize,
                           true))
                {
                    byte[] buffer = new byte[CopyBufferSize];
                    long totalRead = 0;
                    int lastReportedPercent = -1;
                    int read;

                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        totalRead += read;

                        if (!totalBytes.HasValue || totalBytes.Value <= 0)
                            continue;

                        long rawPercent = totalRead * 100 / totalBytes.Value;
                        int percent = (int)Math.Min(100L, Math.Max(0L, rawPercent));

                        if (percent >= lastReportedPercent + 10 || percent == 100)
                        {
                            lastReportedPercent = percent;
                            Log("Загрузка: " + percent + "%");
                        }
                    }
                }
            }

            FileInfo downloaded = new FileInfo(destination);
            if (!downloaded.Exists || downloaded.Length == 0)
            {
                throw new InvalidDataException("Скачанный архив пуст.");
            }
        }

        // ------------------------------------------------------------------- deploy

        /// <summary>
        /// Удаляет файлы предыдущей версии из папки лаунчера. Пользовательские
        /// данные (<c>user_settings.json</c>, кэш конфига) и сам обновлятор
        /// сохраняются; папка <c>instances</c> лежит вне launcher и не трогается.
        /// Занятые запущенным клиентом файлы переименовываются в <c>.old</c>.
        /// </summary>
        private static void ClearLauncherDirectory()
        {
            if (!Directory.Exists(LauncherDirectory))
                return;

            HashSet<string> preserveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "user_settings.json",
                ".launcher_config.json",
                UpdaterExecutableName,
                UpdaterLog.LogFileName,
                VersionFileName
            };

            int deleted = 0;
            int renamed = 0;

            foreach (string file in Directory.GetFiles(LauncherDirectory))
            {
                string fileName = Path.GetFileName(file);
                if (preserveFiles.Contains(fileName))
                    continue;

                try
                {
                    File.Delete(file);
                    deleted++;
                    continue;
                }
                catch
                {
                    // Файл может быть занят (EscBClient.exe, загруженные DLL):
                    // ниже пробуем переименовать его, чтобы освободить имя.
                }

                try
                {
                    string backupPath = file + ".old";
                    TryDeleteFile(backupPath);
                    File.Move(file, backupPath);
                    renamed++;
                }
                catch
                {
                    // Даже переименовать не удалось — файл будет заменён при копировании.
                }
            }

            foreach (string directory in Directory.GetDirectories(LauncherDirectory))
            {
                string directoryName = Path.GetFileName(directory);
                if (string.Equals(directoryName, "instances", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // Папка занята — оставляем как есть, новые файлы перезапишут старые.
                }
            }

            Log("Старая версия очищена: удалено файлов " + deleted + ", отложено " + renamed + ".");
        }

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

                string targetSubDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetSubDirectory))
                    Directory.CreateDirectory(targetSubDirectory);

                CopyWithLockHandling(sourceFile, targetFile);
                copied++;
            }

            Log("Установлено файлов: " + copied + ".");
        }

        /// <summary>
        /// Возвращает папку с фактическим EscBClient.exe (архив может содержать
        /// одну вложенную папку верхнего уровня).
        /// </summary>
        private static string ResolvePayloadRoot(string stagingDirectory)
        {
            if (File.Exists(Path.Combine(stagingDirectory, MainExecutableName)))
                return stagingDirectory;

            string nested = Directory
                .EnumerateFiles(stagingDirectory, MainExecutableName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (nested == null)
            {
                throw new FileNotFoundException(
                    "Архив обновления не содержит " + MainExecutableName + ".");
            }

            return Path.GetDirectoryName(nested);
        }

        /// <summary>
        /// Копирует файл в папку лаунчера. Запущенный EscBClient.exe нельзя
        /// перезаписать напрямую, поэтому старый файл переименовывается в .bak.
        /// </summary>
        private static void CopyWithLockHandling(string sourceFile, string targetFile)
        {
            const int attempts = 3;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    File.Copy(sourceFile, targetFile, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt == attempts)
                        break;
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == attempts)
                        break;
                }

                Thread.Sleep(400);
            }

            string backupFile = targetFile + ".bak";
            TryDeleteFile(backupFile);

            try
            {
                if (File.Exists(targetFile))
                {
                    File.Move(targetFile, backupFile);
                    Log("Занятый файл переименован в .bak: " + targetFile);
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось переименовать занятый файл '" + targetFile + "': " + ex.Message);
            }

            File.Copy(sourceFile, targetFile, true);
            TryDeleteFile(backupFile);
        }

        private static void CleanupLeftoverFiles()
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(
                             LauncherDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    string extension = Path.GetExtension(file);
                    if (string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".old", StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось удалить старые резервные копии: " + ex.Message);
            }
        }

        // -------------------------------------------------------------------- archive

        private static void ExtractArchive(string zipPath, string extractPath)
        {
            Directory.CreateDirectory(extractPath);

            string root = Path.GetFullPath(extractPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));

                    if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Архив содержит недопустимый путь: " + entry.FullName);
                    }

                    string destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                        Directory.CreateDirectory(destinationDirectory);

                    entry.ExtractToFile(destinationPath, true);
                }
            }
        }

        private static bool ArchiveContainsMainExecutable(string zipPath)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                return archive.Entries.Any(entry =>
                    !string.IsNullOrEmpty(entry.Name) &&
                    string.Equals(entry.Name, MainExecutableName, StringComparison.OrdinalIgnoreCase));
            }
        }

        // -------------------------------------------------------------- version/store

        private static string ReadLocalVersion()
        {
            try
            {
                if (!File.Exists(VersionFile))
                    return string.Empty;

                return File.ReadAllText(VersionFile).Trim().TrimStart('\uFEFF');
            }
            catch (Exception ex)
            {
                Log("Не удалось прочитать локальную версию: " + ex.Message);
                return string.Empty;
            }
        }

        private static bool IsRemoteNewer(string remoteTag, string localTag)
        {
            if (string.IsNullOrWhiteSpace(localTag))
                return true;

            if (string.Equals(remoteTag, localTag, StringComparison.OrdinalIgnoreCase))
                return false;

            Version remote;
            Version local;
            if (TryParseVersion(remoteTag, out remote) && TryParseVersion(localTag, out local))
                return remote.CompareTo(local) > 0;

            // Несопоставимые теги (например, "season-2") считаем обновлением.
            return true;
        }

        private static bool TryParseVersion(string tag, out Version version)
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
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = MainExecutablePath,
                WorkingDirectory = LauncherDirectory,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        // -------------------------------------------------------------------- infra

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            HttpClient client = new HttpClient();
            client.Timeout = timeout;

            // GitHub требует заголовок User-Agent для запросов к API.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EscB-Updater/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            return client;
        }

        private static void Log(string message)
        {
            UpdaterLog.Write(message);
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
                    Directory.Delete(path, true);
            }
            catch
            {
                // Best effort.
            }
        }

        private sealed class ReleaseInfo
        {
            public ReleaseInfo(string tagName, string assetUrl)
            {
                TagName = tagName ?? string.Empty;
                AssetUrl = assetUrl;
            }

            public string TagName { get; }

            public string AssetUrl { get; }
        }
    }

    /// <summary>Минимальное представление ответа GitHub Releases API.</summary>
    [DataContract]
    internal sealed class ReleaseDto
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "assets")]
        public AssetDto[] Assets { get; set; }
    }

    [DataContract]
    internal sealed class AssetDto
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }
}
