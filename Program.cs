using System.Windows.Forms;
using EscapeBook.Forms;
using EscapeBook.Services;

namespace EscapeBook;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Modern .NET 8 High DPI support.
        // PerMonitorV2 is required for correct 200% (192 DPI) rendering; the
        // legacy SetProcessDPIAware() call is system-DPI-only and gets blurry or
        // mis-scaled when the window moves between monitors with different DPI.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, args) =>
        {
            AppLog.Error("UI thread exception", args.Exception);

            MessageBox.Show(
                args.Exception.Message,
                "EscB Client — ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                AppLog.Error("Unhandled exception", exception);
        };

        // Authorization is non-intrusive: MainForm checks the saved session on
        // load and switches its own UI between authorized/unauthorized states.
        if (LauncherPaths.ResolutionWarning is { } installPathWarning)
            AppLog.Write("Установочный путь: " + installPathWarning);

        Application.Run(new MainForm());
    }
}
