using System;
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
