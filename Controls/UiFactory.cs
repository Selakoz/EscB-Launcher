namespace EscapeBook.Controls;

internal static class UiFactory
{
    public static Label Label(
        Control parent,
        string text,
        Font font,
        Color foreColor,
        Color backColor,
        Rectangle bounds,
        ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        var label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = font,
            Location = bounds.Location,
            Size = bounds.Size,
            Text = text,
            TextAlign = alignment,
            UseMnemonic = false
        };

        parent.Controls.Add(label);
        return label;
    }

    /// <summary>
    /// Creates a label whose size/position is managed by a layout panel (no absolute Location).
    /// </summary>
    public static Label Label(
        Control parent,
        string text,
        Font font,
        Color foreColor,
        Color backColor)
    {
        var label = new Label
        {
            AutoSize = true,
            AutoEllipsis = true,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = font,
            Text = text,
            UseMnemonic = false
        };

        parent.Controls.Add(label);
        return label;
    }
}