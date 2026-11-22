using System.IO.Compression;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class ClientUpdater
{
    private readonly HttpClient _httpClient;

    public ClientUpdater(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task UpdateAsync(
        BuildInfo build,
        IReadOnlyList<OptionalModInfo> optionalMods,
        IReadOnlyDictionary<string, bool>? playerModStates,
        IReadOnlyDictionary<string, string>? playerModChoices,
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        string instanceDirectory = LauncherPaths.GetInstancePath(build.Id);
        bool instanceExisted = Directory.Exists(instanceDirectory);
        Directory.CreateDirectory(instanceDirectory);

        string versionFile = LauncherPaths.GetVersionFile(build.Id);
        string urlFile = LauncherPaths.GetClientUrlFile(build.Id);

        string localVersion = ReadTextFile(versionFile);
        string localUrl = ReadTextFile(urlFile);

        bool needsUpdate =
            !instanceExisted ||
            !string.Equals(localVersion, build.Version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(localUrl, build.ClientZipUrl, StringComparison.OrdinalIgnoreCase);

        if (!needsUpdate)
        {
            progress.Report(new LaunchProgress(8, $"Сборка «{build.Name}» актуальна"));
            return;
        }

        if (string.IsNullOrWhiteSpace(build.ClientZipUrl))
        {
            progress.Report(new LaunchProgress(8, "client_zip_url не задан — обновление сборки пропущено"));
            AppLog.Write($"client_zip_url is empty for build {build.Id}, skipping client update");
            return;
        }

        progress.Report(new LaunchProgress(8, $"Скачивание сборки «{build.Name}»..."));

        string tempZip = Path.Combine(
            Path.GetTempPath(),
            $"escapebook_{build.Id}_{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadAsync(
                build.ClientZipUrl,
                tempZip,
                ratio => progress.Report(new LaunchProgress(
                    8 + (ratio * 35),
                    $"Скачивание сборки: {ratio * 100:0}%")),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Не удалось скачать сборку клиента.\n" +
                $"Сборка: {build.Name} ({build.Id})\n" +
                $"URL: {build.ClientZipUrl}\n" +
                $"Причина: {ex.Message}",
                ex);
        }

        DisabledModSnapshot disabledState = OptionalModsService.CaptureDisabledState(
            optionalMods,
            build.Id);

        progress.Report(new LaunchProgress(44, "Распаковка сборки..."));

        try
        {
            await Task.Run(
                () => ExtractZip(
                    tempZip,
                    instanceDirectory,
                    disabledState,
                    progress,
                    cancellationToken),
                cancellationToken);

            OptionalModsService.DisableMatchingPrefixes(
                disabledState.DisabledPrefixes,
                build.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Не удалось распаковать сборку клиента: " + ex.Message,
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
            catch (Exception ex)
            {
                AppLog.Error("Temp zip cleanup", ex);
            }
        }

        await File.WriteAllTextAsync(versionFile, build.Version, cancellationToken);
        await File.WriteAllTextAsync(urlFile, build.ClientZipUrl, cancellationToken);

        // Human-readable marker consumed by BuildStatusService.
        await File.WriteAllTextAsync(
            LauncherPaths.GetStatusVersionFile(build.Id),
            build.Version,
            cancellationToken);

        progress.Report(new LaunchProgress(48, $"Сборка «{build.Name}» установлена"));
        AppLog.Write($"Client updated: build={build.Id}, version={build.Version}");

        // Apply mod states: the player's explicit choice wins, otherwise enabled_by_default.
        try
        {
            int changed = 0;

            foreach (var mod in optionalMods)
            {
                if (mod.IsRadio)
                {
                    string? savedChoice = playerModChoices?.GetValueOrDefault(mod.Name);

                    string? selected = OptionalModsService.GetSavedOrDefaultOption(mod, savedChoice);
                    changed += OptionalModsService.ApplyRadioSelection(mod, selected, build.Id);
                    continue;
                }

                bool desired = playerModStates is not null &&
                               playerModStates.TryGetValue(mod.Name, out bool playerState)
                    ? playerState
                    : mod.EnabledByDefault;

                changed += OptionalModsService.SetEnabled(mod, build.Id, desired);
            }

            if (changed > 0)
                AppLog.Write($"Applied mod states: build={build.Id}, changed={changed}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Apply mod states", ex);
        }
    }

    private static string ReadTextFile(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadAllText(path).Trim()
                : string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Error("Client state read", ex);
            return string.Empty;
        }
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        Action<double> reportProgress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int lastPercent = -1;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;

            if (contentLength is > 0)
            {
                int percent = (int)Math.Clamp(totalRead * 100L / contentLength.Value, 0L, 100L);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    reportProgress(percent / 100d);
                }
            }
        }

        reportProgress(1d);
    }

    private static void ExtractZip(
        string zipPath,
        string destinationRoot,
        DisabledModSnapshot disabledState,
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var files = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToList();

        if (files.Count == 0)
            throw new InvalidDataException("Архив сборки пуст.");

        string? commonRoot = FindSingleRootFolder(files);
        string rootFullPath = Path.GetFullPath(destinationRoot);
        string rootPrefix = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        int processed = 0;

        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = entry.FullName.Replace('\\', '/');

            if (commonRoot != null)
            {
                if (!relative.StartsWith(commonRoot + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                relative = relative[(commonRoot.Length + 1)..];
            }

            if (string.IsNullOrWhiteSpace(relative))
                continue;

            string targetPath = Path.GetFullPath(
                Path.Combine(rootFullPath, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!targetPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Недопустимый путь в архиве сборки: {entry.FullName}");

            string fileName = Path.GetFileName(targetPath);

            if (fileName.Equals("options.txt", StringComparison.OrdinalIgnoreCase)
                && File.Exists(targetPath))
            {
                processed++;
                continue;
            }

            // Keep an exact .disabled file instead of restoring its enabled counterpart.
            if (relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                && disabledState.DisabledFileNames.Contains(fileName))
            {
                processed++;
                continue;
            }

            string? targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            entry.ExtractToFile(targetPath, overwrite: true);
            processed++;

            if (processed % 20 == 0 || processed == files.Count)
            {
                double percent = 44 + (Math.Min(processed, files.Count) * 4d / files.Count);
                progress.Report(new LaunchProgress(
                    percent,
                    $"Распаковка: {processed}/{files.Count}"));
            }
        }
    }

    /// <summary>
    /// If every file in the archive lives under one top-level folder, strip that folder
    /// so the client is extracted directly into the selected instance directory.
    /// </summary>
    private static string? FindSingleRootFolder(IReadOnlyList<ZipArchiveEntry> files)
    {
        string? root = null;

        foreach (var entry in files)
        {
            string name = entry.FullName.Replace('\\', '/').TrimStart('/');
            int slash = name.IndexOf('/');

            if (slash < 0)
                return null;

            string firstSegment = name[..slash];

            if (root == null)
                root = firstSegment;
            else if (!root.Equals(firstSegment, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return root;
    }
}