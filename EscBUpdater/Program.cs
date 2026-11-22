using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace EscBUpdater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // GitHub требует TLS 1.2+: в .NET Framework 4.8 протокол не включён
            // по умолчанию, поэтому выставляем его до любых HTTP-запросов.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Режим замены самого обновлятора: новый EXE запущен из %TEMP%
            // и должен перезаписать старый файл после его завершения.
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--replace-self", StringComparison.Ordinal))
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    UpdaterLog.Write("Режим --replace-self: не указан путь к старому обновлятору.");
                    return 1;
                }

                string remoteTag = args.Length > 2 ? args[2] : null;
                return ReplaceSelf(args[1], remoteTag);
            }

            using (Mutex mutex = Updater.CreateSingleInstanceMutex())
            {
                bool acquired = false;

                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // Предыдущий экземпляр завершился аварийно — мьютекс наш.
                    acquired = true;
                }

                if (!acquired)
                {
                    // Первый экземпляр уже проверяет обновления и запустит клиент.
                    UpdaterLog.Write("EscBUpdater уже выполняется; повторный запуск пропущен.");
                    return 0;
                }

                try
                {
                    int exitCode = Updater.RunAsync(args).GetAwaiter().GetResult();

                    if (exitCode != 0)
                    {
                        ShowError(
                            "Не удалось запустить EscB Client.\n\n" +
                            "Запустите EscapeBookBootstrapper.exe, чтобы установить лаунчер.\n\n" +
                            "Журнал: " + UpdaterLog.LogFile);
                    }

                    return exitCode;
                }
                catch (Exception ex)
                {
                    UpdaterLog.Write("Критическая ошибка: " + ex);

                    ShowError(
                        "Критическая ошибка обновления:\n\n" +
                        ex.Message + "\n\nЖурнал: " + UpdaterLog.LogFile);

                    return 1;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Заменяет старый обновлятор новым EXE и запускает его без аргументов.
        /// Вызывается новым процессом из %TEMP% после выхода старого.
        /// </summary>
        private static int ReplaceSelf(string oldPath, string remoteTag)
        {
            string logDirectory = Path.GetDirectoryName(oldPath);

            UpdaterLog.WriteToDirectory(logDirectory, "=== Замена обновлятора ===");
            UpdaterLog.WriteToDirectory(logDirectory, "Старый файл: " + oldPath);

            // Даём старому процессу завершиться и освободить EXE.
            Thread.Sleep(1000);

            string newPath = Application.ExecutablePath;

            if (string.IsNullOrWhiteSpace(newPath) || !File.Exists(newPath))
            {
                UpdaterLog.WriteToDirectory(
                    logDirectory,
                    "Не найден новый файл обновлятора: " + newPath);
                return 1;
            }

            const int attempts = 20;
            bool copied = false;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    File.Copy(newPath, oldPath, true);
                    copied = true;
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == attempts)
                    {
                        UpdaterLog.WriteToDirectory(
                            logDirectory,
                            "Не удалось заменить обновлятор: " + ex);
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
            }

            if (!copied)
                return 1;

            UpdaterLog.WriteToDirectory(logDirectory, "Обновлятор заменён: " + oldPath);

            if (!string.IsNullOrWhiteSpace(remoteTag) && !string.IsNullOrWhiteSpace(logDirectory))
            {
                try
                {
                    Updater.WriteUpdaterVersionForDirectory(logDirectory, remoteTag);
                }
                catch (Exception ex)
                {
                    // Не блокируем запуск: версия будет обновлена при следующем запуске.
                    UpdaterLog.WriteToDirectory(
                        logDirectory,
                        "Не удалось сохранить версию обновлятора: " + ex.Message);
                }
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = oldPath,
                    WorkingDirectory = logDirectory,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                UpdaterLog.WriteToDirectory(logDirectory, "Обновлённый обновлятор запущен.");
                return 0;
            }
            catch (Exception ex)
            {
                UpdaterLog.WriteToDirectory(
                    logDirectory,
                    "Не удалось запустить обновлённый обновлятор: " + ex);
                return 1;
            }
        }

        private static void ShowError(string text)
        {
            MessageBox.Show(
                text,
                "EscB Client — обновление",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
