using System.Runtime.InteropServices;
using System.Text;

namespace EscapeBookBootstrapper;

internal static class Program
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Console encoding is best-effort (e.g. redirected output).
        }

        try
        {
            int exitCode = Bootstrapper.RunAsync(args).GetAwaiter().GetResult();

            if (exitCode != 0)
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
            Bootstrapper.Log("Fatal: " + ex);

            ShowError(
                "Критическая ошибка загрузчика:\n\n" +
                ex.Message + "\n\nЖурнал: " + Bootstrapper.LogFile);

            return 1;
        }
    }

    private static void ShowError(string text) =>
        MessageBoxW(IntPtr.Zero, text, "EscB Bootstrapper", MbOk | MbIconError);
}