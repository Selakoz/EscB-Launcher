using EscapeBook.Models;

namespace EscapeBook.Services;

/// <summary>
/// Determines the local installation state of every build declared in config.json by
/// comparing the downloaded instance version marker with the remote build version.
/// </summary>
internal sealed class BuildStatusService
{
    /// <summary>
    /// Re-checks every build and returns a build-id → status map.
    /// </summary>
    public Dictionary<string, BuildStatus> RefreshStatuses(IEnumerable<BuildInfo> builds)
    {
        var statuses = new Dictionary<string, BuildStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var build in builds)
        {
            if (string.IsNullOrWhiteSpace(build.Id))
                continue;

            statuses[build.Id] = GetStatus(build);
        }

        return statuses;
    }

    /// <summary>Computes the status of a single build.</summary>
    public BuildStatus GetStatus(BuildInfo build) => GetStatus(build.Id, build.Version);

    /// <summary>Computes the status from a build id and the expected version string.</summary>
    public static BuildStatus GetStatus(string buildId, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(buildId))
            return BuildStatus.NotInstalled;

        string instanceDirectory = LauncherPaths.GetInstancePath(buildId);
        if (!Directory.Exists(instanceDirectory))
            return BuildStatus.NotInstalled;

        // Primary marker written after a successful download.
        string versionFile = LauncherPaths.GetStatusVersionFile(buildId);

        // Migration fallback for instances installed before version.txt existed.
        if (!File.Exists(versionFile))
        {
            string legacyVersionFile = LauncherPaths.GetVersionFile(buildId);
            if (!File.Exists(legacyVersionFile))
                return BuildStatus.NotInstalled;

            versionFile = legacyVersionFile;
        }

        try
        {
            string localVersion = File.ReadAllText(versionFile).Trim();
            if (string.IsNullOrEmpty(localVersion))
                return BuildStatus.NotInstalled;

            return string.Equals(
                localVersion,
                (expectedVersion ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase)
                ? BuildStatus.UpToDate
                : BuildStatus.UpdateAvailable;
        }
        catch (Exception ex)
        {
            AppLog.Error("Build status check", ex);
            return BuildStatus.NotInstalled;
        }
    }
}