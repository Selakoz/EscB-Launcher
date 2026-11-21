using System;
using System.IO;

namespace EscapeBookBootstrapper
{
    /// <summary>
    /// Общая конфигурация пути установки. Загрузчик записывает выбранный путь
    /// в %LOCALAPPDATA%\EscB\install_path.txt, лаунчер читает этот же файл,
    /// поэтому оба процесса работают с одной папкой данных.
    /// </summary>
    internal static class InstallPathConfig
    {
        private const string ConfigFolderName = "EscB";
        private const string ConfigFileName = "install_path.txt";

        public static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ConfigFolderName);

        public static readonly string ConfigFile = Path.Combine(
            ConfigDirectory,
            ConfigFileName);

        /// <summary>Путь по умолчанию: %APPDATA%\.escapebook.</summary>
        public static readonly string DefaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".escapebook");

        /// <summary>
        /// Возвращает настроенный путь установки, если он существует и доступен
        /// для записи, иначе путь по умолчанию (%APPDATA%\.escapebook).
        /// </summary>
        public static string GetBasePath()
        {
            return ResolveRoot(out _);
        }

        /// <summary>
        /// Возвращает настроенный путь установки, если он существует и доступен
        /// для записи, иначе путь по умолчанию. При отказе от настроенного пути
        /// в <paramref name="warning"/> записывается причина.
        /// </summary>
        public static string GetBasePath(out string warning)
        {
            return ResolveRoot(out warning);
        }

        /// <summary>
        /// Возвращает настроенный путь установки, если он существует и доступен
        /// для записи, иначе путь по умолчанию. При отказе от настроенного пути
        /// в <paramref name="warning"/> записывается причина.
        /// </summary>
        public static string ResolveRoot(out string warning)
        {
            string configured = ReadConfiguredPath(out warning);
            return configured ?? DefaultRoot;
        }

        /// <summary>Проверяет, что папка существует (или может быть создана) и доступна для записи.</summary>
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

        private static string ReadConfiguredPath(out string warning)
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
                    warning = "Файл " + ConfigFile + " пуст; используется папка по умолчанию " + DefaultRoot + ".";
                    return null;
                }

                if (!Path.IsPathRooted(raw))
                {
                    warning = "Путь '" + raw + "' в " + ConfigFile +
                              " не является абсолютным; используется папка по умолчанию " + DefaultRoot + ".";
                    return null;
                }

                string fullPath = Path.GetFullPath(raw);
                string error;
                if (!IsValidWritableDirectory(fullPath, out error))
                {
                    warning = "Папка '" + fullPath + "' из " + ConfigFile +
                              " недоступна для записи (" + error + "); используется папка по умолчанию " +
                              DefaultRoot + ".";
                    return null;
                }

                return fullPath;
            }
            catch (Exception ex)
            {
                warning = "Не удалось прочитать " + ConfigFile + " (" + ex.Message +
                          "); используется папка по умолчанию " + DefaultRoot + ".";
                return null;
            }
        }
    }
}
