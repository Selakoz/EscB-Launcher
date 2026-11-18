using System.Windows.Forms;
using EscapeBook.Forms;
using EscapeBook.Services;

namespace EscapeBook;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
        Application.Run(new MainForm());
    }
}
