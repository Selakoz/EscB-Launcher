using System.Diagnostics;

namespace EscapeBook.Models;

internal readonly record struct LaunchProgress(double Percent, string Status);

internal sealed record LaunchResult(
    Process Process,
    string JavaPath,
    string JvmArguments,
    string LogPath);