using CmlLib.Core.ProcessBuilder;

namespace EscapeBook.Services;

internal static class JvmArgumentUtil
{
    /// <summary>
    /// Merges default_jvm_args from GitHub with the player's local arguments.
    /// -Xmx/-Xms are removed because memory is controlled by the RAM slider.
    /// </summary>
    public static string[] ParseAndMerge(string defaultArgs, string userArgs)
    {
        string combined = string.Join(
            " ",
            new[] { defaultArgs, userArgs }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(combined))
            return Array.Empty<string>();

        var parsed = MArgument.FromCommandLine(combined);
        return parsed.Values
            .Where(argument => !IsMemoryArgument(argument))
            .ToArray();
    }

    public static string FormatForTelemetry(
        IReadOnlyCollection<string> arguments,
        int maxRamMb,
        int minRamMb)
    {
        var result = new List<string>(arguments)
        {
            $"-Xmx{maxRamMb}M",
            $"-Xms{minRamMb}M"
        };

        return string.Join(" ", result);
    }

    private static bool IsMemoryArgument(string argument) =>
        argument.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase);
}