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
    ///   2. Проверяет три компонента из одного релиза: сам обновлятор
    ///      (<c>EscBUpdater.exe</c>), загрузчик (<c>EscapeBookBootstrapper.exe</c>)
    ///      и лаунчер (<c>EscBClient.zip</c>).
    ///   3. Обновлятор заменяет сам себя через новый экземпляр с ключом
    ///      <c>--replace-self</c>, загрузчик обновляется рядом с лаунчером,
    ///      архив клиента распаковывается в папку лаунчера.
    ///   4. Запускает <c>EscBClient.exe</c> и завершается.
    /// Вся логика намеренно повторяет загрузчик, но без UI и без лишних
    /// зависимостей: EXE остаётся меньше 1 МБ.
    /// </summary>
    internal static class Updater
    {
        // Репозиторий с релизами (общий с загрузчиком).
        private const string GitHubOwner = "Selakoz";
        private const string GitHubRepo = "EscB-Launcher";

        // Все три компонента публикуются ассетами в одном GitHub Release.
        private const string ClientAssetName = "EscBClient.zip";
        private const string BootstrapperAssetName = "EscapeBookBootstrapper.exe";
        private const string UpdaterAssetName = "EscBUpdater.exe";

        private const string MainExecutableName = "EscBClient.exe";
        private const string BootstrapperExecutableName = "EscapeBookBootstrapper.exe";
        private const string UpdaterExecutableName = "EscBUpdater.exe";

        // По умолчанию папка лаунчера — %APPDATA%\.escapebook\launcher.
        // Файлы версий лежат рядом с компонентами (в той же папке).
        private const string ClientVersionFileName = ".client_version";
        private const string BootstrapperVersionFileName = ".bootstrapper_version";
        internal const string UpdaterVersionFileName = ".updater_version";

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

        public static string BootstrapperExecutablePath
        {
            get { return Path.Combine(LauncherDirectory, BootstrapperExecutableName); }
        }

        public static string ClientVersionFile
        {
            get { return Path.Combine(LauncherDirectory, ClientVersionFileName); }
        }

        public static string BootstrapperVersionFile
        {
            get { return Path.Combine(LauncherDirectory, BootstrapperVersionFileName); }
        }

        public static string UpdaterVersionFile
        {
            get { return Path.Combine(LauncherDirectory, UpdaterVersionFileName); }
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

                if (release == null)
                {
                    Log("Не удалось получить информацию о релизе; пропускаем обновления и запускаем установленный клиент.");
                }
                else
                {
                    Log("Ассеты релиза: обновлятор=" + DescribeAsset(release.UpdaterAssetUrl) +
                        ", загрузчик=" + DescribeAsset(release.BootstrapperAssetUrl) +
                        ", клиент=" + DescribeAsset(release.ClientAssetUrl) + ".");

                    // Самообновление всегда первым: новый экземпляр продолжит
                    // остальные обновления уже из заменённого файла.
                    bool selfUpdated = await TrySelfUpdateAsync(release).ConfigureAwait(false);
                    if (selfUpdated)
                    {
                        Log("Текущий обновлятор завершает работу; замену продолжит новый экземпляр.");
                        return 0;
                    }

                    bool bootstrapperUpdated = await TryUpdateBootstrapperAsync(release).ConfigureAwait(false);
                    bool clientUpdated = await TryUpdateClientAsync(release).ConfigureAwait(false);
                    updated = bootstrapperUpdated || clientUpdated;
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

            Log(updated ? "Обновления применены, клиент запущен." : "Клиент запущен.");
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
            string clientUrl = null;
            string bootstrapperUrl = null;
            string updaterUrl = null;

            if (dto.Assets != null)
            {
                foreach (AssetDto asset in dto.Assets)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                        continue;

                    if (string.Equals(asset.Name, ClientAssetName, StringComparison.OrdinalIgnoreCase))
                        clientUrl = asset.BrowserDownloadUrl;
                    else if (string.Equals(asset.Name, BootstrapperAssetName, StringComparison.OrdinalIgnoreCase))
                        bootstrapperUrl = asset.BrowserDownloadUrl;
                    else if (string.Equals(asset.Name, UpdaterAssetName, StringComparison.OrdinalIgnoreCase))
                        updaterUrl = asset.BrowserDownloadUrl;
                }
            }

            Log("Тег последнего релиза: '" + tagName + "'.");
            return new ReleaseInfo(tagName, clientUrl, bootstrapperUrl, updaterUrl);
        }

        private static string DescribeAsset(string assetUrl)
        {
            return string.IsNullOrEmpty(assetUrl) ? "нет" : "есть";
        }

        // -------------------------------------------------------------- self-update

        /// <summary>
        /// Скачивает новый обновлятор во временный файл и запускает его с
        /// <c>--replace-self &lt;текущий exe&gt; &lt;тег&gt;</c>. Текущий процесс
        /// после этого должен немедленно завершиться.
        /// </summary>
        private static async Task<bool> TrySelfUpdateAsync(ReleaseInfo release)
        {
            if (string.IsNullOrEmpty(release.UpdaterAssetUrl))
            {
                Log("В релизе '" + release.TagName + "' нет ассета '" + UpdaterAssetName + "'.");
                return false;
            }

            string localTag = ReadVersion(UpdaterVersionFile);

            if (!IsRemoteNewer(release.TagName, localTag))
            {
                Log("Обновлятор актуален ('" + localTag + "').");
                return false;
            }

            Log("Требуется обновление обновлятора: локально='" + localTag +
                "', удалённо='" + release.TagName + "'.");

            string tempPath = Path.Combine(Path.GetTempPath(), "EscBUpdater_new.exe");

            try
            {
                TryDeleteFile(tempPath);

                Log("Скачивание новой версии обновлятора...");
                await DownloadFileAsync(release.UpdaterAssetUrl, tempPath).ConfigureAwait(false);

                string currentExecutable = GetCurrentExecutablePath();

                if (string.IsNullOrEmpty(currentExecutable) || !File.Exists(currentExecutable))
                {
                    throw new FileNotFoundException(
                        "Не найден текущий файл обновлятора.",
                        currentExecutable);
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = tempPath,
                    Arguments = "--replace-self " + QuoteArgument(currentExecutable) + " " +
                                QuoteArgument(release.TagName),
                    WorkingDirectory = LauncherDirectory,
                    UseShellExecute = true
                };

                Process.Start(startInfo);

                Log("Новый обновлятор запущен для замены: " + currentExecutable);
                return true;
            }
            catch (Exception ex)
            {
                Log("Не удалось обновить обновлятор: " + ex.Message);
                TryDeleteFile(tempPath);
                return false;
            }
        }

        /// <summary>Записывает версию обновлятора рядом с заменённым EXE.</summary>
        internal static void WriteUpdaterVersionForDirectory(string launcherDirectory, string remoteTag)
        {
            WriteVersion(Path.Combine(launcherDirectory, UpdaterVersionFileName), remoteTag);
        }

        // ------------------------------------------------------------ bootstrapper

        private static async Task<bool> TryUpdateBootstrapperAsync(ReleaseInfo release)
        {
            if (string.IsNullOrEmpty(release.BootstrapperAssetUrl))
            {
                Log("В релизе '" + release.TagName + "' нет ассета '" + BootstrapperAssetName + "'.");
                return false;
            }

            string localTag = ReadVersion(BootstrapperVersionFile);
            bool missing = !File.Exists(BootstrapperExecutablePath);

            if (!missing && !IsRemoteNewer(release.TagName, localTag))
            {
                Log("Загрузчик актуален ('" + localTag + "').");
                return false;
            }

            Log("Требуется обновление загрузчика: локально='" +
                (missing ? "файл отсутствует" : localTag) +
                "', удалённо='" + release.TagName + "'.");

            try
            {
                await DownloadAndApplyBootstrapperAsync(
                    release.BootstrapperAssetUrl,
                    release.TagName).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                // Ошибка одного компонента не должна мешать остальным.
                Log("Не удалось обновить загрузчик: " + ex);
                return false;
            }
        }

        private static async Task DownloadAndApplyBootstrapperAsync(string assetUrl, string remoteTag)
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "EscapeBookBootstrapper_new_" + Guid.NewGuid().ToString("N") + ".exe");

            string targetPath = BootstrapperExecutablePath;
            string backupPath = targetPath + ".old";

            try
            {
                Log("Скачивание новой версии загрузчика...");
                await DownloadFileAsync(assetUrl, tempFile).ConfigureAwait(false);

                Log("Установка загрузчика...");
                TryDeleteFile(backupPath);

                bool hadOldFile = File.Exists(targetPath);
                if (hadOldFile)
                    File.Move(targetPath, backupPath);

                try
                {
                    MoveOrCopyFile(tempFile, targetPath);

                    if (!File.Exists(targetPath))
                    {
                        throw new FileNotFoundException(
                            "Не удалось установить " + BootstrapperExecutableName + ".",
                            targetPath);
                    }
                }
                catch
                {
                    // Возвращаем старый загрузчик, чтобы установка осталась рабочей.
                    if (hadOldFile && !File.Exists(targetPath) && File.Exists(backupPath))
                    {
                        try
                        {
                            File.Move(backupPath, targetPath);
                        }
                        catch
                        {
                            // Best effort.
                        }
                    }

                    throw;
                }

                WriteVersion(BootstrapperVersionFile, remoteTag);
                TryDeleteFile(backupPath);
                Log("Загрузчик обновлён до '" + remoteTag + "'.");
            }
            finally
            {
                TryDeleteFile(tempFile);
            }
        }

        // ------------------------------------------------------------------ client

        private static async Task<bool> TryUpdateClientAsync(ReleaseInfo release)
        {
            if (string.IsNullOrEmpty(release.ClientAssetUrl))
            {
                Log("В релизе '" + release.TagName + "' нет ассета '" + ClientAssetName + "'.");
                return false;
            }

            string localTag = ReadVersion(ClientVersionFile);

            if (!IsRemoteNewer(release.TagName, localTag))
            {
                Log("Клиент актуален ('" + localTag + "').");
                return false;
            }

            Log("Требуется обновление клиента: локально='" + localTag +
                "', удалённо='" + release.TagName + "'.");

            try
            {
                await DownloadAndApplyAsync(release.ClientAssetUrl, release.TagName).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                // Ошибка клиента не должна мешать запуску уже установленной версии.
                Log("Не удалось обновить клиент: " + ex);
                return false;
            }
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

                WriteVersion(ClientVersionFile, remoteTag);
                Log("Файл версии клиента обновлён: '" + remoteTag + "'.");
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
                throw new InvalidDataException("Скачанный файл пуст.");
            }
        }

        // ------------------------------------------------------------------- deploy

        /// <summary>
        /// Удаляет файлы предыдущей версии из папки лаунчера. Пользовательские
        /// данные (<c>user_settings.json</c>, кэш конфига), сам обновлятор,
        /// загрузчик и файлы версий сохраняются; папка <c>instances</c> лежит
        /// вне launcher и не трогается. Занятые запущенным клиентом файлы
        /// переименовываются в <c>.old</c>.
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
                BootstrapperExecutableName,
                UpdaterLog.LogFileName,
                ClientVersionFileName,
                BootstrapperVersionFileName,
                UpdaterVersionFileName
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

        private static void MoveOrCopyFile(string sourceFile, string targetFile)
        {
            try
            {
                File.Move(sourceFile, targetFile);
            }
            catch (IOException)
            {
                // %TEMP% может быть на другом диске — тогда переносим копированием.
                File.Copy(sourceFile, targetFile, true);
                TryDeleteFile(sourceFile);
            }
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

        private static string ReadVersion(string versionFile)
        {
            try
            {
                if (!File.Exists(versionFile))
                    return string.Empty;

                return File.ReadAllText(versionFile).Trim().TrimStart('\uFEFF');
            }
            catch (Exception ex)
            {
                Log("Не удалось прочитать файл версии '" + versionFile + "': " + ex.Message);
                return string.Empty;
            }
        }

        private static void WriteVersion(string versionFile, string remoteTag)
        {
            string directory = Path.GetDirectoryName(versionFile);

            try
            {
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(versionFile, remoteTag ?? string.Empty);
                UpdaterLog.WriteToDirectory(directory, "Файл версии обновлён: '" + versionFile +
                                                       "' = '" + remoteTag + "'.");
            }
            catch (Exception ex)
            {
                UpdaterLog.WriteToDirectory(directory, "Не удалось записать файл версии '" +
                                                       versionFile + "': " + ex.Message);
                throw;
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

        private static string GetCurrentExecutablePath()
        {
            try
            {
                string path = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
            catch
            {
                // MainModule может быть недоступен — используем папку лаунчера.
            }

            return Path.Combine(LauncherDirectory, UpdaterExecutableName);
        }

        private static string QuoteArgument(string value)
        {
            // Тег не должен ломать командную строку; кавычки в нём недопустимы.
            return "\"" + (value ?? string.Empty).Replace("\"", string.Empty) + "\"";
        }

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
            public ReleaseInfo(
                string tagName,
                string clientAssetUrl,
                string bootstrapperAssetUrl,
                string updaterAssetUrl)
            {
                TagName = tagName ?? string.Empty;
                ClientAssetUrl = clientAssetUrl;
                BootstrapperAssetUrl = bootstrapperAssetUrl;
                UpdaterAssetUrl = updaterAssetUrl;
            }

            public string TagName { get; }

            public string ClientAssetUrl { get; }

            public string BootstrapperAssetUrl { get; }

            public string UpdaterAssetUrl { get; }
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
