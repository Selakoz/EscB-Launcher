using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public int CornerRadius { get; set; } = 10;
    public int BorderSize { get; set; }
    public Color BorderColor { get; set; } = Color.Transparent;
    public Color NormalBackColor { get; set; } = Theme.Accent;
    public Color HoverBackColor { get; set; } = Theme.AccentHover;
    public Color PressedBackColor { get; set; } = Theme.AccentPressed;
    public Color DisabledBackColor { get; set; } = Color.FromArgb(34, 38, 49);
    public bool ShowPlayIcon { get; set; }

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Font(10f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Painted completely in OnPaint to keep the rounded corners clean.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.EffectiveBackColor(Parent));

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(rect, CornerRadius);

        var fillColor = !Enabled
            ? DisabledBackColor
            : _pressed
                ? PressedBackColor
                : _hovered
                    ? HoverBackColor
                    : NormalBackColor;

        using (var fill = new SolidBrush(fillColor))
            graphics.FillPath(fill, path);

        if (BorderSize > 0 && BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, BorderSize);
            graphics.DrawPath(pen, path);
        }

        var textColor = Enabled ? ForeColor : Theme.Muted;

        if (Image != null)
        {
            // Auth icons (e.g. Telegram): draw the image centered, ~20-24 px.
            const int iconSide = 22;
            var iconRect = new Rectangle((Width - iconSide) / 2, (Height - iconSide) / 2, iconSide, iconSide);
            var previousMode = graphics.InterpolationMode;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(Image, iconRect);
            graphics.InterpolationMode = previousMode;
        }
        else if (ShowPlayIcon)
        {
            var measured = TextRenderer.MeasureText(
                Text,
                Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            const int iconWidth = 13;
            const int gap = 13;
            int totalWidth = measured.Width + iconWidth + gap;
            int startX = (Width - totalWidth) / 2;
            int centerY = Height / 2;

            using (var iconBrush = new SolidBrush(textColor))
            {
                var triangle = new[]
                {
                    new Point(startX, centerY - 7),
                    new Point(startX, centerY + 7),
                    new Point(startX + iconWidth, centerY)
                };
                graphics.FillPolygon(iconBrush, triangle);
            }

            var textRect = new Rectangle(startX + iconWidth + gap, 0, measured.Width + 2, Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        else
        {
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}