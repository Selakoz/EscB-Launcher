using System.Drawing.Drawing2D;
using EscapeBook.Models;

namespace EscapeBook.Controls;

/// <summary>
/// Dark-themed owner-drawn ComboBox.
/// Draws a dark background (#1E1E2E), light text, subtle border and accent selection highlight.
/// </summary>
internal sealed class DarkComboBox : ComboBox
{
    private const int DropDownListHeight = 30;

    public DarkComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.FromArgb(30, 30, 46); // #1E1E2E
        ForeColor = Theme.Text;
        Font = Theme.Font(10f);
        DropDownStyle = ComboBoxStyle.DropDownList;
        ItemHeight = DropDownListHeight;
        MaxDropDownItems = 8;
        Cursor = Cursors.Hand;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (e.Index < 0)
        {
            e.DrawBackground();
            return;
        }

        bool selected = (e.State & DrawItemState.Selected) != 0;
        var rect = e.Bounds;

        // Background: accent on selection, dark surface otherwise.
        using var backBrush = new SolidBrush(selected ? Theme.Accent : Color.FromArgb(30, 30, 46));
        e.Graphics.FillRectangle(backBrush, rect);

        // Subtle border around the item.
        using var borderPen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawRectangle(borderPen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);

        var item = Items[e.Index] as BuildInfo ?? new BuildInfo { Name = Items[e.Index]?.ToString() ?? string.Empty };

        var textRect = new Rectangle(rect.Left + 12, rect.Top, Math.Max(1, rect.Width - 24), rect.Height);
        TextRenderer.DrawText(
            e.Graphics,
            item.Name,
            Font,
            textRect,
            selected ? Color.FromArgb(250, 250, 255) : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        e.DrawFocusRectangle();
    }

    protected override void OnDropDownStyleChanged(EventArgs e)
    {
        // Keep DropDownList enforced.
        DropDownStyle = ComboBoxStyle.DropDownList;
        base.OnDropDownStyleChanged(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DropDownStyle = ComboBoxStyle.DropDownList;
    }
}

/// <summary>
/// Custom-painted toggle switch (CheckBox). Dark track with an accent-colored active state
/// and a sliding circular thumb.
/// </summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        AutoSize = false;
        Size = new Size(52, 28);
        Font = Theme.Font(9f);
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        bool on = Checked;
        Color trackColor = on ? Theme.Accent : Theme.ToggleOff;
        Color thumbColor = on ? Color.FromArgb(250, 250, 255) : Theme.ToggleOffThumb;

        // Track (rounded capsule).
        float trackRadius = rect.Height / 2f;
        using var trackPath = Theme.RoundedRect(rect, trackRadius);
        using var trackBrush = new SolidBrush(trackColor);
        e.Graphics.FillPath(trackBrush, trackPath);

        // Thumb circle.
        float thumbDiameter = rect.Height - 8f;
        float thumbX = on
            ? rect.Right - thumbDiameter - 4f
            : rect.Left + 4f;
        float thumbY = (rect.Height - thumbDiameter) / 2f;
        using var thumbPath = Theme.RoundedRect(
            new RectangleF(thumbX, thumbY, thumbDiameter, thumbDiameter),
            thumbDiameter / 2f);
        using var thumbBrush = new SolidBrush(thumbColor);
        e.Graphics.FillPath(thumbBrush, thumbPath);

        // Optional small label text centered.
        if (!string.IsNullOrEmpty(Text))
        {
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                on ? Color.FromArgb(245, 245, 250) : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Keep the control a fixed capsule aspect.
        if (Height > 0)
            Width = Math.Max(Height * 2, 40);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            Invalidate();
    }
}
