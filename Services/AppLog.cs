namespace EscapeBook.Services;

internal static class AppLog
{
    private static readonly object SyncRoot = new();

    public static void Write(string message)
    {
        try
        {
            LauncherPaths.EnsureRoot();
            lock (SyncRoot)
            {
                File.AppendAllText(
                    LauncherPaths.LauncherLogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the launcher.
        }
    }

    public static void Error(string context, Exception exception) =>
        Write($"{context}: {exception}");
}