using System.Drawing.Drawing2D;

namespace EscapeBook;

/// <summary>
/// Central palette and helpers for the dark UI.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 15, 20);
    public static readonly Color Surface = Color.FromArgb(23, 26, 33);
    public static readonly Color SurfaceHover = Color.FromArgb(31, 35, 45);
    public static readonly Color Input = Color.FromArgb(17, 20, 26);
    public static readonly Color Border = Color.FromArgb(40, 45, 58);
    public static readonly Color Track = Color.FromArgb(38, 43, 56);

    public static readonly Color Text = Color.FromArgb(240, 243, 250);
    public static readonly Color Muted = Color.FromArgb(138, 147, 166);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color AccentHover = Color.FromArgb(145, 118, 255);
    public static readonly Color AccentPressed = Color.FromArgb(103, 74, 224);
    public static readonly Color AccentSoft = Color.FromArgb(91, 140, 255);
    public static readonly Color Danger = Color.FromArgb(255, 107, 122);
    public static readonly Color Success = Color.FromArgb(80, 210, 150);
    public static readonly Color Warning = Color.FromArgb(255, 190, 70);
    public static readonly Color WarningAlt = Color.FromArgb(255, 145, 50);
    public static readonly Color ToggleOff = Color.FromArgb(50, 55, 70);
    public static readonly Color ToggleOffThumb = Color.FromArgb(220, 224, 232);
    public static readonly Color SurfaceInverted = Color.FromArgb(30, 34, 44);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    /// <summary>
    /// Returns the first opaque background colour walking up the parent chain.
    /// Custom-painted controls use this to blend their rounded corners into the real
    /// backdrop instead of painting black when a parent is transparent.
    /// </summary>
    public static Color EffectiveBackColor(Control? control)
    {
        for (Control? c = control; c is not null; c = c.Parent)
        {
            Color back = c.BackColor;
            if (back != Color.Transparent && back.A == 255)
                return back;
        }

        return Background;
    }

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0f || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        float diameter = radius * 2f;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}