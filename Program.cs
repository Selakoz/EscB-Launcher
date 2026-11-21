using System.Windows.Forms;
using EscapeBook.Forms;
using EscapeBook.Services;

namespace EscapeBook;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        // Enable High DPI support.
        // Must run before any window/handle is created so Windows does not
        // bitmap-scale (stretch) the launcher on monitors above 100% scaling.
        if (Environment.OSVersion.Version.Major >= 6) // Windows Vista or later
        {
            SetProcessDPIAware();
        }

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
