using System.Reflection;
using EscapeBook.Services;

namespace EscapeBook;

/// <summary>
/// Loads icon.ico and banner.png that are embedded into the executable.
/// A remotely updated banner in %APPDATA%\.escapebook\assets\banner.png overrides the embedded one.
/// </summary>
internal static class Branding
{
    public static Icon LoadIcon()
    {
        try
        {
            using Stream? stream = OpenEmbeddedResource("icon.ico");
            if (stream != null)
            {
                using var icon = new Icon(stream);
                return (Icon)icon.Clone();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Embedded icon load", ex);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public static Image LoadBanner()
    {
        try
        {
            if (File.Exists(LauncherPaths.BannerFile))
            {
                Image? localBanner = LoadImageFromFile(LauncherPaths.BannerFile);
                if (localBanner != null)
                    return localBanner;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Local banner load", ex);
        }

        try
        {
            using Stream? stream = OpenEmbeddedResource("banner.png");
            if (stream != null)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                Image? embedded = LoadImage(memory.ToArray());
                if (embedded != null)
                    return embedded;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Embedded banner load", ex);
        }

        return CreatePlaceholderBanner();
    }

    public static Image? LoadImageFromFile(string path)
    {
        try
        {
            return LoadImage(File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            AppLog.Error("Banner file load", ex);
            return null;
        }
    }

    private static Image? LoadImage(byte[] bytes)
    {
        try
        {
            using var memory = new MemoryStream(bytes);
            using var image = Image.FromStream(memory);
            return new Bitmap(image);
        }
        catch (Exception ex)
        {
            AppLog.Error("Banner decode", ex);
            return null;
        }
    }

    private static Stream? OpenEmbeddedResource(string suffix)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return resourceName == null
            ? null
            : assembly.GetManifestResourceStream(resourceName);
    }

    private static Image CreatePlaceholderBanner()
    {
        var bitmap = new Bitmap(1920, 480);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Theme.Background);
        using var font = Theme.Font(96f, FontStyle.Bold);
        TextRenderer.DrawText(
            graphics,
            "ESCAPEBOOK",
            font,
            new Rectangle(0, 0, 1920, 480),
            Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        return bitmap;
    }
}