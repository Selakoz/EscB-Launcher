using System.Drawing;

namespace EscapeBook.Models;

/// <summary>
/// Dropdown entry for the build selector: display text, build id and local status.
/// </summary>
internal sealed class BuildListItem
{
    public string Text { get; init; } = string.Empty;

    public string BuildId { get; init; } = string.Empty;

    public BuildStatus Status { get; init; }

    public BuildInfo Build { get; init; } = new();

    /// <summary>Text suffix appended to the build name in the dropdown.</summary>
    public static string StatusSuffix(BuildStatus status) => status switch
    {
        BuildStatus.UpToDate => " — Актуальная",
        BuildStatus.UpdateAvailable => " — Требуется обновление",
        BuildStatus.NotInstalled => " — Не установлено",
        _ => string.Empty
    };

    /// <summary>Background colour used for this entry in the dropdown popup.</summary>
    public static Color StatusBackColor(BuildStatus status) => status switch
    {
        BuildStatus.UpdateAvailable => Color.FromArgb(74, 63, 26),   // #4A3F1A amber
        BuildStatus.NotInstalled => Color.FromArgb(26, 63, 26),     // #1A3F1A green
        _ => Color.FromArgb(30, 30, 46)                             // #1E1E2E default
    };

    public override string ToString() => Text;
}