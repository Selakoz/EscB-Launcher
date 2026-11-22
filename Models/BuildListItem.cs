using System.Drawing;
using System.Text.RegularExpressions;

namespace EscapeBook.Models;

/// <summary>
/// Dropdown entry for the build selector. The row is rendered in two zones:
/// the build name with the release version on the left and the status in
/// parentheses on the right.
/// </summary>
internal sealed class BuildListItem
{
    /// <summary>
    /// Matches the release tag in a GitHub download URL:
    /// <c>.../releases/download/vX.Y.Z/file.zip</c>.
    /// </summary>
    private static readonly Regex TagRx =
        new(@"/releases/download/(v?\d+(?:\.\d+)*)/", RegexOptions.Compiled);

    public string BuildId { get; init; } = string.Empty;

    public BuildInfo Build { get; init; } = new();

    /// <summary>Секретная сборка (скрыта из списка по умолчанию).</summary>
    public bool IsSecret => Build.IsSecret;

    public BuildStatus Status { get; init; }

    /// <summary>
    /// Версия сборки для отображения: тег релиза из <c>client_zip_url</c>
    /// (например, <c>v1.0.0</c>). Если ссылка не содержит тег релиза —
    /// запасное значение из <c>version</c> в config.json, иначе <c>v?</c>.
    /// <c>mc_version</c> здесь намеренно не используется: он нужен только CmlLib.
    /// </summary>
    public string DisplayVersion
    {
        get
        {
            Match match = TagRx.Match(Build.ClientZipUrl ?? string.Empty);
            if (match.Success)
            {
                string tag = match.Groups[1].Value;
                return tag.StartsWith("v", StringComparison.Ordinal) ? tag : "v" + tag;
            }

            return string.IsNullOrWhiteSpace(Build.Version) ? "v?" : Build.Version;
        }
    }

    /// <summary>Левая зона строки: название сборки и версия релиза.</summary>
    public string LeftText => $"{Build.Name} ({DisplayVersion})";

    /// <summary>Правая зона строки: статус в круглых скобках.</summary>
    public string RightText => $"({StatusWord(Status)})";

    /// <summary>
    /// Базовое слово статуса — единый источник для всех отображений
    /// (правая зона выпадающего списка и суффикс строки).
    /// </summary>
    public static string StatusWord(BuildStatus status) => status switch
    {
        BuildStatus.UpToDate => "Актуальная",
        BuildStatus.UpdateAvailable => "Требуется обновление",
        BuildStatus.NotInstalled => "Не установлено",
        _ => string.Empty
    };

    /// <summary>Суффикс статуса (« — Актуальная» и т.п.), собранный из StatusWord.</summary>
    public static string StatusSuffix(BuildStatus status)
    {
        string word = StatusWord(status);
        return string.IsNullOrEmpty(word) ? string.Empty : " — " + word;
    }

    /// <summary>Background colour used for this entry in the dropdown popup.</summary>
    public static Color StatusBackColor(BuildStatus status) => status switch
    {
        BuildStatus.UpdateAvailable => Color.FromArgb(74, 63, 26),   // #4A3F1A amber
        BuildStatus.NotInstalled => Color.FromArgb(26, 63, 26),     // #1A3F1A green
        _ => Color.FromArgb(30, 30, 46)                             // #1E1E2E default
    };

    public override string ToString() => LeftText;
}