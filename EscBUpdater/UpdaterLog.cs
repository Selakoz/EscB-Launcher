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
            try
            {
                Directory.CreateDirectory(Updater.LauncherDirectory);
                File.AppendAllText(
                    LogFile,
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
