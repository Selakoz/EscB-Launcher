using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace EscapeBookBootstrapper
{
    /// <summary>
    /// Лёгкий двухшаговый загрузчик (только встроенные API .NET Framework 4.8):
    ///   1. Запрашивает у GitHub Releases API последний релиз лаунчера.
    ///   2. Скачивает <c>EscBClient.zip</c>, если удалённый тег отличается от
    ///      локального маркера, разворачивает его в папку установки
    ///      (<c>%APPDATA%\.escapebook\launcher</c> по умолчанию или выбранную
    ///      пользователем) и запускает <c>EscBClient.exe</c>.
    /// Итоговый EXE самодостаточен (зависимости вшиты Costura.Fody) и работает
    /// на чистой Windows 10/11 без установленного .NET 8.
    /// </summary>
    internal static class Bootstrapper
    {
        // Репозиторий, в котором публикуются релизы
        // (ассеты EscapeBookBootstrapper.exe и EscBClient.zip).
        private const string GitHubOwner = "Selakoz";
        private const string GitHubRepo = "EscB-Launcher";

        private const string ReleaseAssetName = "EscBClient.zip";
        private const string MainExecutableName = "EscBClient.exe";

        // Обновлятор и ярлык на рабочем столе, который его запускает.
        private const string UpdaterExecutableName = "EscBUpdater.exe";
        private const string UpdaterResourceName = "EscBUpdater.exe";
        private const string ShortcutFileName = "EscB Client.lnk";
        private const string ShortcutDescription = "EscB Client — запуск и обновление";

        private const int CopyBufferSize = 81920;

        // Версия текста лицензионного соглашения. При изменении текста
        // увеличьте версию, чтобы соглашение показалось повторно.
        public const string EulaVersion = "1.0";

        private const string EulaFlagFileName = "eula_accepted.flag";
        private const string EulaRegistryPath = @"Software\EscB";
        private const string EulaRegistryValue = "EulaAccepted";

        private static readonly string LatestReleaseApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        private static string _rootDirectory = InstallPathConfig.GetBasePath();

        /// <summary>
        /// Корень данных лаунчера. Может быть изменён при первом запуске
        /// (автоматически или вручную выбранная папка).
        /// </summary>
        public static string RootDirectory
        {
            get { return _rootDirectory; }
        }

        public static string LauncherDirectory
        {
            get { return Path.Combine(_rootDirectory, "launcher"); }
        }

        public static string InstancesDirectory
        {
            get { return Path.Combine(_rootDirectory, "instances"); }
        }

        public static string MainExecutablePath
        {
            get { return Path.Combine(LauncherDirectory, MainExecutableName); }
        }

        public static string UpdaterExecutablePath
        {
            get { return Path.Combine(LauncherDirectory, UpdaterExecutableName); }
        }

        // Версия лаунчера. Обновлятор пишет тот же файл, поэтому загрузчик
        // и ярлык используют единый маркер установленной версии клиента.
        public static string VersionFile
        {
            get { return Path.Combine(LauncherDirectory, ".client_version"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(LauncherDirectory, "bootstrapper.log"); }
        }

        /// <summary>Пользователь закрыл окно выбора пути установки.</summary>
        public static bool UserCancelled { get; private set; }

        public static async Task<int> RunAsync(string[] args)
        {
            // GitHub требует TLS 1.2+. В .NET Framework 4.8 протокол не включён
            // по умолчанию, поэтому выставляем его явно до любых HTTP-запросов.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            if (!InitializeInstallRoot())
                return UserCancelled ? 2 : 1;

            CleanupLeftoverBackups();

            Log("=== Загрузчик запущен ===");
            Log("Корень установки: " + RootDirectory);
            Log("Папка лаунчера: " + LauncherDirectory);
            Log("API: " + LatestReleaseApiUrl);

            // Обновлятор вшит в этот EXE и распаковывается рядом с клиентом:
            // именно на него будет указывать ярлык с рабочего стола.
            EnsureUpdaterInstalled();

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
                // Любая сетевая/архивная ошибка не должна мешать запуску
                // уже установленного клиента.
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

            // Ярлык создаётся только после успешного первого запуска и только
            // если его ещё нет (повторные запуски ничего не меняют).
            EnsureShortcut();

            Log(updated ? "Клиент обновлён и запущен." : "Клиент запущен.");
            return 0;
        }

        // -------------------------------------------------------------- install path

        /// <summary>
        /// Определяет корень установки: читает общий файл настроек, а при его
        /// отсутствии показывает выбор «автоматически / вручную».
        /// </summary>
        private static bool InitializeInstallRoot()
        {
            UserCancelled = false;

            string warning;
            string root = InstallPathConfig.GetBasePath(out warning);

            if (!File.Exists(InstallPathConfig.ConfigFile))
            {
                root = PromptForInstallPath();
                if (root == null)
                {
                    UserCancelled = true;
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(warning))
            {
                Log("Предупреждение: " + warning);
            }

            _rootDirectory = root;

            try
            {
                Directory.CreateDirectory(RootDirectory);
                Directory.CreateDirectory(LauncherDirectory);
                Directory.CreateDirectory(InstancesDirectory);
            }
            catch (Exception ex)
            {
                Log("Не удалось создать папки установки: " + ex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Спрашивает у пользователя способ установки. Возвращает выбранный
        /// абсолютный путь или null, если пользователь отменил выбор.
        /// </summary>
        private static string PromptForInstallPath()
        {
            string defaultRoot = InstallPathConfig.DefaultRoot;

            using (InstallPathForm form = new InstallPathForm(defaultRoot))
            {
                DialogResult result = form.ShowDialog();

                if (result == DialogResult.Yes)
                {
                    InstallPathConfig.Save(defaultRoot);
                    return Path.GetFullPath(defaultRoot);
                }

                if (result != DialogResult.No)
                    return null;
            }

            while (true)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Выберите папку для установки EscB Client";
                    dialog.ShowNewFolderButton = true;

                    if (dialog.ShowDialog() != DialogResult.OK)
                        return null;

                    string selectedPath = dialog.SelectedPath;
                    string error;

                    if (InstallPathConfig.IsValidWritableDirectory(selectedPath, out error))
                    {
                        string fullPath = Path.GetFullPath(selectedPath);
                        InstallPathConfig.Save(fullPath);
                        return fullPath;
                    }

                    MessageBox.Show(
                        "Выбранная папка недоступна для записи.\n\n" + error +
                        "\n\nВыберите другую папку.",
                        "EscB Client — установка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
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
        /// Разбирает минимально необходимое подмножество ответа GitHub через
        /// Newtonsoft.Json (в .NET Framework 4.8 нет System.Text.Json).
        /// Сборка Newtonsoft.Json вшивается в EXE через Costura.Fody.
        /// </summary>
        private static ReleaseInfo ParseRelease(string json)
        {
            JObject root = JObject.Parse(json);

            string tagName = (string)root["tag_name"] ?? string.Empty;
            string assetUrl = null;

            JArray assets = root["assets"] as JArray;
            if (assets != null)
            {
                foreach (JToken entry in assets)
                {
                    if (entry == null)
                        continue;

                    string name = (string)entry["name"] ?? string.Empty;
                    if (!string.Equals(name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string url = (string)entry["browser_download_url"] ?? string.Empty;
                    if (!string.IsNullOrEmpty(url))
                    {
                        assetUrl = url;
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
                "escb_client_" + Guid.NewGuid().ToString("N") + ".zip");

            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "escb_stage_" + Guid.NewGuid().ToString("N"));

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

                if (IsMainClientRunning())
                {
                    Log("EscBClient.exe уже запущен: занятые файлы будут заменены через .bak.");
                }

                // Полная переустановка: удаляем файлы прошлой версии, сохраняя
                // настройки игрока и папку instances (она лежит вне launcher).
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
        /// Возвращает папку, в которой фактически лежит EscBClient.exe, чтобы ZIP-ы,
        /// созданные через «Отправить → Сжатая ZIP-папка» (все файлы в одной
        /// вложенной папке), тоже работали.
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
        /// Копирует файл в папку лаунчера. Запущенный EscBClient.exe (или
        /// загруженную DLL) нельзя перезаписать напрямую, поэтому старый файл
        /// сначала переименовывается в .bak, а затем удаляется.
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

        private static void CleanupLeftoverBackups()
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

        /// <summary>
        /// Полная очистка папки лаунчера перед установкой новой версии.
        /// Сохраняются настройки игрока, кэш конфига, сам обновлятор и файлы
        /// версии/журналов. Папка <c>instances</c> находится вне launcher,
        /// поэтому данные сборок не затрагиваются.
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
                "EscapeBookBootstrapper.exe",
                "updater.log",
                ".client_version",
                ".bootstrapper_version",
                ".updater_version",
                "bootstrapper.log"
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
                    // Файл занят запущенным клиентом — пробуем переименовать ниже.
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
                    // Не удалось даже переименовать: файл заменится при копировании.
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
                    // Папка занята — оставляем, новые файлы перезапишут старые.
                }
            }

            Log("Старая версия очищена: удалено файлов " + deleted + ", отложено " + renamed + ".");
        }

        /// <summary>
        /// Распаковывает вшитый <c>EscBUpdater.exe</c> в папку лаунчера, чтобы
        /// ярлыку было на что указывать. Если файл уже на месте и совпадает по
        /// размеру — ничего не делает.
        /// </summary>
        private static void EnsureUpdaterInstalled()
        {
            string targetPath = UpdaterExecutablePath;

            try
            {
                Assembly assembly = typeof(Bootstrapper).Assembly;

                using (Stream resource = assembly.GetManifestResourceStream(UpdaterResourceName))
                {
                    if (resource == null)
                    {
                        Log("Встроенный " + UpdaterExecutableName +
                            " не найден: ярлык обновления не будет создан.");
                        return;
                    }

                    if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
                    {
                        Log(UpdaterExecutableName + " уже установлен.");
                        return;
                    }

                    string tempPath = targetPath + ".tmp";

                    try
                    {
                        using (FileStream output = new FileStream(
                                   tempPath,
                                   FileMode.Create,
                                   FileAccess.Write,
                                   FileShare.None))
                        {
                            resource.CopyTo(output);
                        }

                        File.Copy(tempPath, targetPath, true);
                        Log(UpdaterExecutableName + " установлен в папку лаунчера.");
                    }
                    finally
                    {
                        TryDeleteFile(tempPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Например, обновлятор сейчас запущен и файл заблокирован.
                Log("Не удалось установить " + UpdaterExecutableName + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Создаёт ярлык «EscB Client» на рабочем столе (общий, затем
        /// пользовательский), если его ещё нет. Ярлык указывает на
        /// <c>EscBUpdater.exe</c>: он проверяет обновления и запускает клиент.
        /// </summary>
        private static void EnsureShortcut()
        {
            try
            {
                string updaterPath = UpdaterExecutablePath;

                if (!File.Exists(updaterPath))
                {
                    Log("Ярлык не создан: отсутствует " + updaterPath + ".");
                    return;
                }

                string commonDesktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonDesktopDirectory);
                string userDesktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);

                string existing = FindExistingShortcut(commonDesktop, userDesktop);
                if (!string.IsNullOrEmpty(existing))
                {
                    Log("Ярлык уже существует: " + existing);
                    return;
                }

                string iconPath = File.Exists(MainExecutablePath)
                    ? MainExecutablePath
                    : updaterPath;

                string[] targetDirectories = new string[] { commonDesktop, userDesktop };

                foreach (string directory in targetDirectories)
                {
                    if (string.IsNullOrEmpty(directory))
                        continue;

                    string shortcutPath = Path.Combine(directory, ShortcutFileName);

                    try
                    {
                        CreateShortcut(shortcutPath, updaterPath, ShortcutDescription, iconPath);
                        Log("Ярлык создан: " + shortcutPath);
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Общий рабочий стол может требовать прав администратора.
                        Log("Не удалось создать ярлык в '" + directory + "': " + ex.Message);
                    }
                }

                Log("Ярлык не создан: нет доступа к рабочему столу.");
            }
            catch (Exception ex)
            {
                Log("Не удалось создать ярлык: " + ex);
            }
        }

        private static string FindExistingShortcut(params string[] directories)
        {
            foreach (string directory in directories)
            {
                if (string.IsNullOrEmpty(directory))
                    continue;

                string shortcutPath = Path.Combine(directory, ShortcutFileName);
                if (File.Exists(shortcutPath))
                    return shortcutPath;
            }

            return null;
        }

        /// <summary>
        /// Создаёт .lnk через COM-объект WScript.Shell (есть во всех версиях
        /// Windows, дополнительных сборок не требует).
        /// </summary>
        private static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string description,
            string iconPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("Компонент WScript.Shell недоступен.");

            object shell = Activator.CreateInstance(shellType);

            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                try
                {
                    Type shortcutType = shortcut.GetType();

                    shortcutType.InvokeMember(
                        "TargetPath",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { targetPath });

                    shortcutType.InvokeMember(
                        "WorkingDirectory",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { Path.GetDirectoryName(targetPath) });

                    shortcutType.InvokeMember(
                        "Description",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { description });

                    shortcutType.InvokeMember(
                        "IconLocation",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { iconPath + ",0" });

                    shortcutType.InvokeMember(
                        "Save",
                        BindingFlags.InvokeMethod,
                        null,
                        shortcut,
                        null);
                }
                finally
                {
                    if (shortcut != null && Marshal.IsComObject(shortcut))
                        Marshal.ReleaseComObject(shortcut);
                }
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                    Marshal.ReleaseComObject(shell);
            }
        }

        /// <summary>
        /// Проверяет, запущен ли уже установленный EscBClient.exe: в этом случае
        /// его файлы нельзя перезаписать, и они будут заменены через .bak.
        /// </summary>
        private static bool IsMainClientRunning()
        {
            string processName = Path.GetFileNameWithoutExtension(MainExecutableName);

            try
            {
                Process current = Process.GetCurrentProcess();
                Process[] processes = Process.GetProcessesByName(processName);

                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.Id == current.Id)
                            continue;

                        if (process.MainModule == null || string.IsNullOrEmpty(process.MainModule.FileName))
                            continue;

                        if (string.Equals(
                                Path.GetFullPath(process.MainModule.FileName),
                                Path.GetFullPath(MainExecutablePath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Нет доступа к чужому процессу — считаем, что это не наш клиент.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось проверить запущенные процессы: " + ex.Message);
            }

            return false;
        }

        // -------------------------------------------------------------------- archive

        /// <summary>
        /// Распаковывает архив с перезаписью файлов. В .NET Framework 4.8 нет
        /// перегрузки ZipFile.ExtractToDirectory(path, dir, overwrite), поэтому
        /// используется эквивалентная реализация поверх ZipArchive.
        /// </summary>
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
                    // Записи-каталоги (пустое имя) пропускаем: папки создаются ниже.
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));

                    // Защита от «zip-slip»: файл не должен выйти за пределы папки.
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

        // ---------------------------------------------------------------------- eula

        /// <summary>
        /// Путь к флагу принятия EULA: %LOCALAPPDATA%\EscB\eula_accepted.flag.
        /// </summary>
        private static string EulaFlagFile
        {
            get
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "EscB", EulaFlagFileName);
            }
        }

        /// <summary>
        /// Проверяет, принята ли текущая версия EULA. Сначала читается файл
        /// флага, затем (если файла нет) — резервная запись в реестре.
        /// </summary>
        public static bool IsEulaAccepted()
        {
            try
            {
                if (File.Exists(EulaFlagFile))
                {
                    string content = File.ReadAllText(EulaFlagFile).Trim();
                    return IsCurrentEulaVersion(content);
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось прочитать флаг EULA: " + ex.Message);
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EulaRegistryPath))
                {
                    if (key == null)
                        return false;

                    string content = key.GetValue(EulaRegistryValue) as string;
                    return IsCurrentEulaVersion(content);
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось прочитать флаг EULA из реестра: " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Сохраняет принятие текущей версии EULA. Основной путь — файл в
        /// %LOCALAPPDATA%; если запись невозможна, используется HKCU-реестр.
        /// </summary>
        public static void SaveEulaAcceptance()
        {
            string content = EulaVersion + "|" + DateTime.UtcNow.ToString("o");

            try
            {
                string directory = Path.GetDirectoryName(EulaFlagFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(EulaFlagFile, content);
                return;
            }
            catch (Exception ex)
            {
                Log("Не удалось сохранить флаг EULA в файл: " + ex.Message);
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(EulaRegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(EulaRegistryValue, content, RegistryValueKind.String);
                        Log("Флаг EULA сохранён в реестре.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось сохранить флаг EULA в реестр: " + ex.Message);
            }
        }

        /// <summary>
        /// Формат флага: "версия|ISO-время". Старые версии соглашения
        /// требуют повторного принятия.
        /// </summary>
        private static bool IsCurrentEulaVersion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            int separator = content.IndexOf('|');
            string version = separator >= 0
                ? content.Substring(0, separator).Trim()
                : content.Trim();

            return string.Equals(version, EulaVersion, StringComparison.Ordinal);
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EscB-Bootstrapper/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            return client;
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(LauncherDirectory);
                File.AppendAllText(
                    LogFile,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                    message + Environment.NewLine);
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
}
