using System;
using System.IO;

namespace EscBUpdater
{
    /// <summary>
    /// Журнал обновлятора: <c>%APPDATA%\.escapebook\launcher\updater.log</c>.
    /// Логирование не должно ронять процесс, поэтому все ошибки проглатываются.
    /// </summary>
    internal static class UpdaterLog
    {
        public const string LogFileName = "updater.log";

        public static string LogFile
        {
            get { return Path.Combine(Updater.LauncherDirectory, LogFileName); }
        }

        public static void Write(string message)
        {
            WriteToDirectory(Updater.LauncherDirectory, message);
        }

        /// <summary>
        /// Запись в журнал конкретной папки. Нужна процессу замены обновлятора:
        /// он запущен из %TEMP%, но логи должен писать рядом с лаунчером.
        /// </summary>
        public static void WriteToDirectory(string directory, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                    directory = Updater.LauncherDirectory;

                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, LogFileName),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                    message + Environment.NewLine);
            }
            catch
            {
                // Журнал не должен мешать обновлению/запуску.
            }
        }
    }
}
