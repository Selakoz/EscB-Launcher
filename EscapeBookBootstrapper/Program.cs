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
            // ОБЯЗАТЕЛЬНО для .NET Framework 4.8: без явного TLS 1.2 запросы
            // к GitHub API падают с ошибкой SSL/аутентификации. Выполняется
            // до любых сетевых вызовов.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Диалог выбора папки должен выглядеть как нативный для Windows:
            // без этого .NET Framework 4.8 рисует элементы в устаревшем стиле.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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

        private static void ShowError(string text)
        {
            MessageBoxW(IntPtr.Zero, text, "EscB Bootstrapper", MbOk | MbIconError);
        }
    }
}
