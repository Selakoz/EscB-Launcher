using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EscapeBookBootstrapper
{
    internal static class Program
    {
        private const uint MbOk = 0x00000000;
        private const uint MbIconError = 0x00000010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [STAThread]
        private static int Main(string[] args)
        {
            // Диалог выбора папки должен выглядеть как нативный для Windows:
            // без этого .NET Framework 4.8 рисует элементы в устаревшем стиле.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Стартовое меню (запуск / переустановка / удаление) показывается
            // до EULA, TLS и любых сетевых вызовов. В тихом режиме
            // (--silent/--auto) меню пропускается: так ярлык и обновлятор
            // работают как раньше.
            if (!IsSilentLaunch(args))
            {
                DialogResult choice;

                using (var menu = new StartupMenuForm())
                {
                    choice = menu.ShowDialog();
                }

                switch (choice)
                {
                    case DialogResult.Retry:
                        Bootstrapper.ReinstallLauncher();
                        break;

                    case DialogResult.Abort:
                        Bootstrapper.UninstallLauncher();
                        return 0;

                    case DialogResult.OK:
                    default:
                        // Обычный запуск: продолжаем проверку обновлений.
                        break;
                }
            }

            // Лицензионное соглашение показывается до TLS, сетевых вызовов
            // и любых изменений на диске (первый запуск загрузчика).
            if (!Bootstrapper.IsEulaAccepted())
            {
                using (var eula = new EulaForm())
                {
                    if (eula.ShowDialog() != DialogResult.OK)
                    {
                        MessageBox.Show(
                            "Для использования лаунчера необходимо принять лицензионное соглашение.",
                            "Установка отменена",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return 0;
                    }

                    Bootstrapper.SaveEulaAcceptance();
                }
            }

            // ОБЯЗАТЕЛЬНО для .NET Framework 4.8: без явного TLS 1.2 запросы
            // к GitHub API падают с ошибкой SSL/аутентификации. Выполняется
            // до любых сетевых вызовов.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            try
            {
                int exitCode = Bootstrapper.RunAsync(args).GetAwaiter().GetResult();

                if (exitCode != 0 && exitCode != 2)
                {
                    ShowError(
                        "Не удалось запустить EscB Client.\n\n" +
                        "Проверьте подключение к интернету и повторите попытку.\n\n" +
                        "Журнал: " + Bootstrapper.LogFile);
                }

                return exitCode;
            }
            catch (Exception ex)
            {
                Bootstrapper.Log("Критическая ошибка: " + ex);

                ShowError(
                    "Критическая ошибка загрузчика:\n\n" +
                    ex.Message + "\n\nЖурнал: " + Bootstrapper.LogFile);

                return 1;
            }
        }

        /// <summary>
        /// Тихий запуск без стартового меню (совместимость с ярлыком,
        /// указывающим на EscBUpdater, и с автоматическими вызовами).
        /// </summary>
        private static bool IsSilentLaunch(string[] args)
        {
            if (args == null)
                return false;

            foreach (string arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg))
                    continue;

                string normalized = arg.Trim();

                if (normalized.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("--auto", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("/auto", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ShowError(string text)
        {
            MessageBoxW(IntPtr.Zero, text, "EscB Bootstrapper", MbOk | MbIconError);
        }
    }
}
