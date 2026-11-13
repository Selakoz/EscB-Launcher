using EscapeBook.Models;

namespace EscapeBook.Services;

internal readonly record struct OptionalModState(bool HasFiles, bool Enabled);

internal sealed record DisabledModSnapshot(
    IReadOnlySet<string> DisabledFileNames,
    IReadOnlyList<string> DisabledPrefixes);

internal static class OptionalModsService
{
    public const string DisabledSuffix = ".disabled";

    public static OptionalModState GetState(OptionalModInfo mod, string buildId)
    {
        List<string> files = GetMatchingFiles(mod, buildId);
        bool hasFiles = files.Count > 0;
        bool enabled = files.Any(file => !IsDisabled(file));
        return new OptionalModState(hasFiles, enabled);
    }

    public static int SetEnabled(OptionalModInfo mod, string buildId, bool enabled)
    {
        int changed = 0;

        foreach (string file in GetMatchingFiles(mod, buildId))
        {
            bool disabled = IsDisabled(file);

            if (enabled && disabled)
            {
                string target = file[..^DisabledSuffix.Length];
                if (File.Exists(target))
                    File.Delete(file);
                else
                    File.Move(file, target);

                changed++;
            }
            else if (!enabled && !disabled)
            {
                string target = file + DisabledSuffix;
                if (File.Exists(target))
                    File.Delete(file);
                else
                    File.Move(file, target);

                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Applies <see cref="OptionalModInfo.EnabledByDefault"/> to every mod whose files exist,
    /// renaming files to add or remove the <c>.disabled</c> suffix so they match the default.
    /// Used on first instance setup and on "Сбросить по умолчанию".
    /// </summary>
    public static int ApplyDefaults(IReadOnlyList<OptionalModInfo> optionalMods, string buildId)
    {
        int changed = 0;

        foreach (var mod in optionalMods)
        {
            changed += SetEnabled(mod, buildId, mod.EnabledByDefault);
        }

        return changed;
    }

    public static List<string> GetMatchingFiles(OptionalModInfo mod, string buildId)
    {
        string modsDirectory = LauncherPaths.GetModsPath(buildId);
        if (!Directory.Exists(modsDirectory))
            return new List<string>();

        try
        {
            return Directory
                .EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(file => mod.FilePrefixes.Any(prefix =>
                    Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error("Optional mods enumeration", ex);
            return new List<string>();
        }
    }

    public static bool IsDisabled(string path) =>
        path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    public static DisabledModSnapshot CaptureDisabledState(
        IReadOnlyList<OptionalModInfo> optionalMods,
        string buildId)
    {
        var disabledFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledPrefixes = new List<string>();

        string modsDirectory = LauncherPaths.GetModsPath(buildId);
        if (!Directory.Exists(modsDirectory))
            return new DisabledModSnapshot(disabledFileNames, disabledPrefixes);

        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         modsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (IsDisabled(file))
                {
                    string fileName = Path.GetFileName(file);
                    disabledFileNames.Add(fileName[..^DisabledSuffix.Length]);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Disabled mods snapshot", ex);
        }

        foreach (var mod in optionalMods)
        {
            OptionalModState state = GetState(mod, buildId);
            if (state.HasFiles && !state.Enabled)
                disabledPrefixes.AddRange(mod.FilePrefixes);
        }

        return new DisabledModSnapshot(disabledFileNames, disabledPrefixes);
    }

    public static int DisableMatchingPrefixes(
        IReadOnlyList<string> prefixes,
        string buildId)
    {
        if (prefixes.Count == 0)
            return 0;

        string modsDirectory = LauncherPaths.GetModsPath(buildId);
        if (!Directory.Exists(modsDirectory))
            return 0;

        int changed = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         modsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (IsDisabled(file))
                    continue;

                string fileName = Path.GetFileName(file);
                if (!prefixes.Any(prefix =>
                        fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string target = file + DisabledSuffix;
                if (File.Exists(target))
                    File.Delete(file);
                else
                    File.Move(file, target);

                changed++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Restore disabled mods", ex);
        }

        return changed;
    }
}