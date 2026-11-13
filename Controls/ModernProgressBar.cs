using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

internal sealed class ModernProgressBar : Control
{
    private double _value;

    public ModernProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Height = 10;
        BackColor = Theme.Background;
    }

    /// <summary>Progress value from 0 to 100.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0d, 100d);
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Painted completely in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);

        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var trackPath = Theme.RoundedRect(track, Height / 2f))
        using (var trackBrush = new SolidBrush(Theme.Track))
            graphics.FillPath(trackBrush, trackPath);

        float fillWidth = (float)(track.Width * (_value / 100d));
        if (fillWidth <= 0.5f)
            return;

        var fill = new RectangleF(track.X, track.Y, Math.Max(1f, fillWidth), track.Height);
        using var fillPath = Theme.RoundedRect(fill, Math.Min(Height / 2f, fillWidth / 2f));
        using var gradient = new LinearGradientBrush(
            new RectangleF(track.X, track.Y, Math.Max(2f, track.Width), track.Height),
            Theme.Accent,
            Theme.AccentSoft,
            LinearGradientMode.Horizontal);

        graphics.FillPath(gradient, fillPath);
    }
}