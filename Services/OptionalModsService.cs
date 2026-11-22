using EscapeBook.Models;

namespace EscapeBook.Services;

internal readonly record struct OptionalModState(bool HasFiles, bool Enabled);

internal sealed record DisabledModSnapshot(
    IReadOnlySet<string> DisabledFileNames,
    IReadOnlyList<string> DisabledPrefixes);

internal static class OptionalModsService
{
    public const string DisabledSuffix = ".disabled";

    public static OptionalModState GetState(OptionalModInfo mod, string buildId) =>
        GetPrefixState(mod.FilePrefixes, buildId);

    public static OptionalModState GetOptionState(ModOption option, string buildId) =>
        GetPrefixState(option.FilePrefixes, buildId);

    private static OptionalModState GetPrefixState(
        IReadOnlyList<string> prefixes,
        string buildId)
    {
        List<string> files = GetMatchingFiles(prefixes, buildId);
        bool hasFiles = files.Count > 0;
        bool enabled = files.Any(file => !IsDisabled(file));
        return new OptionalModState(hasFiles, enabled);
    }

    public static int SetEnabled(OptionalModInfo mod, string buildId, bool enabled) =>
        SetPrefixesEnabled(mod.FilePrefixes, buildId, enabled);

    /// <summary>
    /// Applies a radio group choice: files of the selected option are enabled and
    /// every other option is disabled with the <c>.disabled</c> suffix.
    /// </summary>
    public static int ApplyRadioSelection(
        OptionalModInfo mod,
        string? selectedOption,
        string buildId)
    {
        int changed = 0;

        foreach (var option in mod.Options)
        {
            bool enabled =
                !string.IsNullOrWhiteSpace(selectedOption) &&
                string.Equals(option.Name, selectedOption, StringComparison.OrdinalIgnoreCase);

            changed += SetPrefixesEnabled(option.FilePrefixes, buildId, enabled);
        }

        return changed;
    }

    /// <summary>
    /// Saved radio choice when it still matches a configured option, otherwise the
    /// first option marked <c>enabled_by_default</c>, otherwise the first option.
    /// </summary>
    public static string? GetSavedOrDefaultOption(OptionalModInfo mod, string? savedOption)
    {
        if (mod.Options.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(savedOption) &&
            mod.Options.Any(option =>
                string.Equals(option.Name, savedOption, StringComparison.OrdinalIgnoreCase)))
        {
            return savedOption;
        }

        return mod.Options.FirstOrDefault(option => option.EnabledByDefault)?.Name
               ?? mod.Options[0].Name;
    }

    /// <summary>
    /// Resolves the active radio option for the settings UI: the saved preference
    /// wins, then whichever option currently has enabled files on disk, then the
    /// configured default.
    /// </summary>
    public static string? ResolveSelection(
        OptionalModInfo mod,
        string? savedOption,
        string buildId)
    {
        if (mod.Options.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(savedOption) &&
            mod.Options.Any(option =>
                string.Equals(option.Name, savedOption, StringComparison.OrdinalIgnoreCase)))
        {
            return savedOption;
        }

        ModOption? enabledOption = mod.Options.FirstOrDefault(option =>
            GetOptionState(option, buildId) is { HasFiles: true, Enabled: true });

        return enabledOption?.Name ?? GetSavedOrDefaultOption(mod, null);
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
            if (mod.IsRadio)
            {
                changed += ApplyRadioSelection(
                    mod,
                    GetSavedOrDefaultOption(mod, null),
                    buildId);
                continue;
            }

            changed += SetEnabled(mod, buildId, mod.EnabledByDefault);
        }

        return changed;
    }

    public static List<string> GetMatchingFiles(OptionalModInfo mod, string buildId) =>
        GetMatchingFiles(mod.FilePrefixes, buildId);

    private static List<string> GetMatchingFiles(
        IReadOnlyList<string> prefixes,
        string buildId)
    {
        string modsDirectory = LauncherPaths.GetModsPath(buildId);
        if (!Directory.Exists(modsDirectory))
            return new List<string>();

        try
        {
            return Directory
                .EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(file => prefixes.Any(prefix =>
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
            if (mod.IsRadio)
            {
                foreach (var option in mod.Options)
                {
                    OptionalModState optionState = GetOptionState(option, buildId);
                    if (optionState.HasFiles && !optionState.Enabled)
                        disabledPrefixes.AddRange(option.FilePrefixes);
                }

                continue;
            }

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

    private static int SetPrefixesEnabled(
        IReadOnlyList<string> prefixes,
        string buildId,
        bool enabled)
    {
        int changed = 0;

        foreach (string file in GetMatchingFiles(prefixes, buildId))
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
}
