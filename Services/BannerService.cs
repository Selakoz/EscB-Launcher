using System.Security.Cryptography;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class BannerService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    /// <summary>
    /// Silently downloads a new banner when banner_hash differs from the local file.
    /// Returns true when a new banner was written to disk.
    /// </summary>
    public async Task<bool> TryUpdateAsync(
        LauncherConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.BannerUrl) ||
            string.IsNullOrWhiteSpace(config.BannerHash))
        {
            return false;
        }

        string expectedHash = config.BannerHash.Trim().ToLowerInvariant();
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
        {
            AppLog.Write("Banner update skipped: banner_hash is not a valid SHA-256 hash.");
            return false;
        }

        string tempPath = LauncherPaths.BannerFile + ".download";

        try
        {
            if (File.Exists(LauncherPaths.BannerFile))
            {
                string currentHash = ComputeFileHash(LauncherPaths.BannerFile);
                if (string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            LauncherPaths.EnsureAssets();

            using (var response = await Http.GetAsync(
                       config.BannerUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);

                await source.CopyToAsync(target, cancellationToken);
            }

            string downloadedHash = ComputeFileHash(tempPath);
            if (!string.Equals(downloadedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write(
                    $"Banner hash mismatch: expected {expectedHash}, got {downloadedHash}. " +
                    "Keeping the current banner.");
                return false;
            }

            File.Move(tempPath, LauncherPaths.BannerFile, overwrite: true);
            AppLog.Write("Remote banner updated: " + config.BannerUrl);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A broken banner URL must never prevent launching the game.
            AppLog.Error("Banner update", ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                AppLog.Error("Banner temp cleanup", ex);
            }
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}