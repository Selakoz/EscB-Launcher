using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

internal sealed class RoundedPanel : Panel
{
    private int _cornerRadius = 14;
    private Color _fillColor = Theme.Surface;
    private Color _borderColor = Theme.Border;
    private int _borderSize = 1;

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = _fillColor;
    }

    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = Math.Max(0, value); Invalidate(); }
    }

    public Color FillColor
    {
        get => _fillColor;
        set { _fillColor = value; BackColor = value; Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    public int BorderSize
    {
        get => _borderSize;
        set { _borderSize = Math.Max(0, value); Invalidate(); }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        graphics.FillPath(fill, path);

        if (BorderSize > 0 && BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, BorderSize);
            graphics.DrawPath(pen, path);
        }
    }
}