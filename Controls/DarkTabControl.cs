using System.Drawing;

namespace EscapeBook.Controls;

/// <summary>
/// Fully custom-painted TabControl: dark header strip, dark tab headers with an accent
/// underline for the active tab, and a dark 1px page frame. Removes all native white
/// chrome that the standard TabControl draws.
/// </summary>
internal sealed class DarkTabControl : TabControl
{
    public DarkTabControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(156, 30);
        Appearance = TabAppearance.Normal;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Theme.Background);

        Rectangle page = DisplayRectangle;

        // Dark 1px frame around the page area (replaces the native light border).
        if (page.Width > 1 && page.Height > 1)
        {
            using var frame = new Pen(Theme.Border, 1f);
            graphics.DrawRectangle(
                frame,
                Math.Max(0, page.Left - 1),
                Math.Max(0, page.Top - 1),
                Math.Min(Width - Math.Max(0, page.Left - 1) - 1, page.Width + 1),
                Math.Min(Height - Math.Max(0, page.Top - 1) - 1, page.Height + 1));
        }

        // Tab headers.
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle bounds = GetTabRect(i);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            bool selected = SelectedIndex == i;

            using (var back = new SolidBrush(selected ? Theme.SurfaceInverted : Theme.Background))
                graphics.FillRectangle(back, bounds);

            using (var border = new Pen(Theme.Border, 1f))
                graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                graphics.FillRectangle(
                    accent,
                    bounds.Left + 1,
                    bounds.Bottom - 3,
                    Math.Max(0, bounds.Width - 2),
                    2);
            }

            TextRenderer.DrawText(
                graphics,
                TabPages[i].Text,
                Font,
                bounds,
                selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
