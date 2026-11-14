using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

/// <summary>
/// Fully custom dark dropdown control.
/// - Rounded corners (radius 8) painted with GraphicsPath.
/// - Dark fill #1E1E2E, subtle border #383850 (highlighted on hover).
/// - Custom-painted arrow.
/// - Dark ContextMenuStrip dropdown (no white OS chrome anywhere).
/// </summary>
internal sealed class RoundedComboBox : UserControl
{
    internal const int CornerRadius = 8;
    internal const int ItemHeight = 30;

    internal static readonly Color Fill = Color.FromArgb(30, 30, 46);        // #1E1E2E
    internal static readonly Color BorderIdle = Color.FromArgb(56, 56, 80);  // #383850
    internal static readonly Color BorderHover = Color.FromArgb(92, 92, 132);

    private readonly List<object> _items = new();
    private int _selectedIndex = -1;
    private bool _hovered;
    private bool _pressed;
    private bool _popupOpen;
    private ContextMenuStrip? _menu;

    public RoundedComboBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Height = 36;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>Converts an item into the text shown in the list.</summary>
    public Func<object?, string>? Formatter { get; set; }

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<object> Items => _items;

    public int Count => _items.Count;

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int index = value < -1 || value >= _items.Count ? -1 : value;
            if (index == _selectedIndex)
                return;

            _selectedIndex = index;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string DisplayText =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? Format(_items[_selectedIndex])
            : string.Empty;

    /// <summary>Replaces the item list. Raises <see cref="SelectedIndexChanged"/> once.</summary>
    public void SetItems(
        IEnumerable<object> items,
        string? selectedKey = null,
        Func<object?, string?>? keySelector = null)
    {
        _items.Clear();
        _items.AddRange(items.Where(item => item is not null));

        int index = -1;
        if (selectedKey is not null && keySelector is not null)
        {
            index = _items.FindIndex(item =>
                string.Equals(keySelector(item), selectedKey, StringComparison.OrdinalIgnoreCase));
        }

        _selectedIndex = index >= 0 ? index : (_items.Count > 0 ? 0 : -1);
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearItems()
    {
        _items.Clear();
        _selectedIndex = -1;
        Invalidate();
    }

    private string Format(object? item)
    {
        if (item is null)
            return string.Empty;

        return Formatter?.Invoke(item) ?? item.ToString() ?? string.Empty;
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

        using (var fill = new SolidBrush(Fill))
            graphics.FillPath(fill, path);

        Color borderColor = _popupOpen
            ? Theme.Accent
            : _hovered
                ? BorderHover
                : BorderIdle;

        using (var pen = new Pen(borderColor, _popupOpen || _pressed ? 1.6f : 1f))
            graphics.DrawPath(pen, path);

        // Selected text.
        var textRect = new Rectangle(12, 0, Math.Max(1, Width - 46), Height);
        TextRenderer.DrawText(
            graphics,
            DisplayText,
            Font,
            textRect,
            Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Custom arrow.
        DrawArrow(graphics, Width - 22, Height / 2f, _hovered || _popupOpen || _pressed);
    }

    private static void DrawArrow(Graphics graphics, float centerX, float centerY, bool bright)
    {
        using var brush = new SolidBrush(bright ? Theme.Text : Theme.Muted);
        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(centerX - 5f, centerY - 2.5f),
            new PointF(centerX + 5f, centerY - 2.5f),
            new PointF(centerX, centerY + 3.5f)
        });
        graphics.FillPath(brush, path);
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            ToggleDropDown();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Enter or Keys.Space => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                if (_selectedIndex > 0)
                    SelectedIndex = _selectedIndex - 1;
                e.Handled = true;
                break;
            case Keys.Down:
                if (_selectedIndex < _items.Count - 1)
                    SelectedIndex = _selectedIndex + 1;
                e.Handled = true;
                break;
            case Keys.Enter:
            case Keys.Space:
                ToggleDropDown();
                e.Handled = true;
                break;
            case Keys.Escape:
                CloseDropDown();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private void ToggleDropDown()
    {
        if (_popupOpen)
        {
            CloseDropDown();
            return;
        }

        if (_items.Count == 0)
            return;

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Fill,
            ForeColor = Theme.Text,
            Font = Font,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(2),
            DropShadowEnabled = false,
            MinimumSize = new Size(Math.Max(140, Width), 0),
            AutoSize = true
        };

        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Format(_items[i]))
            {
                ForeColor = Theme.Text,
                BackColor = Fill,
                AutoSize = true,
                Padding = new Padding(10, 6, 10, 6),
                Tag = index
            };

            item.Click += (_, _) =>
            {
                SelectedIndex = index;
                CloseDropDown();
            };

            menu.Items.Add(item);
        }

        menu.Closed += (_, _) =>
        {
            _popupOpen = false;
            _menu = null;
            Invalidate();
        };

        menu.Show(this, new Point(0, Height), ToolStripDropDownDirection.BelowRight);
        _menu = menu;
        _popupOpen = true;
        Invalidate();
    }

    private void CloseDropDown()
    {
        var menu = _menu;
        _menu = null;
        _popupOpen = false;
        menu?.Close();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu?.Close();
            _menu?.Dispose();
            _menu = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Dark renderer for the dropdown, avoiding any white OS chrome.</summary>
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Fill);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(BorderIdle, 1f);
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool highlighted = e.Item.Selected || e.Item.Pressed;

            using var brush = new SolidBrush(highlighted ? Theme.Accent : Fill);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected || e.Item.Pressed
                ? Color.FromArgb(250, 250, 255)
                : Theme.Text;

            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => BorderIdle;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.Accent;
        public override Color MenuItemSelectedGradientBegin => Theme.Accent;
        public override Color MenuItemSelectedGradientEnd => Theme.Accent;
        public override Color MenuStripGradientBegin => Fill;
        public override Color MenuStripGradientEnd => Fill;
        public override Color ToolStripDropDownBackground => Fill;
        public override Color ImageMarginGradientBegin => Fill;
        public override Color ImageMarginGradientMiddle => Fill;
        public override Color ImageMarginGradientEnd => Fill;
    }
}
