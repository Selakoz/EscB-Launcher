using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

/// <summary>
/// A single optional-mod row: custom-painted toggle switch, mod name and description.
/// Clicking the toggle or anywhere on the card flips the switch.
/// </summary>
internal sealed class ModToggleCard : Panel
{
    private const int CardHeight = 64;

    private readonly Label _nameLabel;
    private readonly Label _descriptionLabel;
    private readonly SwitchToggle _toggle;
    private bool _suppressToggleEvents;

    public ModToggleCard()
    {
        Height = CardHeight;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _toggle = new SwitchToggle
        {
            Location = new Point(12, (CardHeight - 30) / 2),
            Size = new Size(48, 26)
        };
        _toggle.CheckedChanged += (_, _) =>
        {
            if (_suppressToggleEvents)
                return;

            OnToggleRequested();
        };
        Controls.Add(_toggle);

        int textLeft = 12 + 48 + 12;

        _nameLabel = new Label
        {
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(textLeft, 8),
            Height = 22
        };
        _nameLabel.MouseDown += (_, e) => HandleCardClick(e);
        Controls.Add(_nameLabel);

        _descriptionLabel = new Label
        {
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(175, 180, 195),
            BackColor = Color.Transparent,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(textLeft, 30),
            Height = 28
        };
        _descriptionLabel.MouseDown += (_, e) => HandleCardClick(e);
        Controls.Add(_descriptionLabel);

        // Clicking the empty card area also toggles.
        MouseDown += (_, e) => HandleCardClick(e);
    }

    /// <summary>Raised when the user flips the switch (by clicking the toggle or the card).</summary>
    public event EventHandler? ToggleRequested;

    public bool IsOn
    {
        get => _toggle.Checked;
        set
        {
            _suppressToggleEvents = true;
            _toggle.Checked = value;
            _suppressToggleEvents = false;
            _toggle.Invalidate();
        }
    }

    public string ModName
    {
        get => _nameLabel.Text;
        set => _nameLabel.Text = value;
    }

    public string Description
    {
        get => _descriptionLabel.Text;
        set => _descriptionLabel.Text = value;
    }

    /// <summary>When false the toggle is greyed out and the card is not clickable.</summary>
    public bool Interactive { get; set; } = true;

    private void HandleCardClick(MouseEventArgs e)
    {
        if (!Interactive || e.Button != MouseButtons.Left)
            return;

        _toggle.Checked = !_toggle.Checked;
        _toggle.Invalidate();
        OnToggleRequested();
    }

    private void OnToggleRequested()
    {
        _toggle.Invalidate();
        Invalidate();
        ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_toggle is null || _nameLabel is null || _descriptionLabel is null)
            return;

        int textLeft = 12 + _toggle.Width + 12;
        int textWidth = Math.Max(10, Width - textLeft - 10);

        _nameLabel.SetBounds(textLeft, 8, textWidth, 22);
        _descriptionLabel.SetBounds(textLeft, 30, textWidth, 28);
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
        using var path = Theme.RoundedRect(rect, 10);

        using (var fill = new SolidBrush(Theme.Surface))
            graphics.FillPath(fill, path);

        using (var pen = new Pen(Theme.Border, 1f))
            graphics.DrawPath(pen, path);
    }
}

/// <summary>
/// Custom-painted switch (track + sliding thumb) used by <see cref="ModToggleCard"/>.
/// </summary>
internal sealed class SwitchToggle : Control
{
    private bool _checked;
    private bool _hovered;

    public SwitchToggle()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Checked = !Checked;
        }

        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Space or Keys.Enter => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        bool on = Checked;

        Color trackColor = on ? Theme.Accent : Theme.ToggleOff;
        if (!on && _hovered)
            trackColor = Theme.SurfaceHover;

        using (var trackPath = Theme.RoundedRect(rect, rect.Height / 2f))
        using (var trackBrush = new SolidBrush(trackColor))
            graphics.FillPath(trackBrush, trackPath);

        float diameter = rect.Height - 8f;
        float thumbX = on ? rect.Right - diameter - 4f : rect.Left + 4f;
        float thumbY = (rect.Height - diameter) / 2f;

        using (var thumbPath = Theme.RoundedRect(
                   new RectangleF(thumbX, thumbY, diameter, diameter),
                   diameter / 2f))
        using (var thumbBrush = new SolidBrush(on ? Color.FromArgb(250, 250, 255) : Theme.ToggleOffThumb))
            graphics.FillPath(thumbBrush, thumbPath);
    }
}
