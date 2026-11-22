using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

/// <summary>
/// Card for a radio-type optional mod: title, description and one real
/// <see cref="RadioButton"/> per configured option. All buttons share the same
/// parent panel, so WinForms enforces mutual exclusivity automatically.
/// </summary>
internal sealed class ModRadioGroupCard : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _descriptionLabel;
    private readonly FlowLayoutPanel _optionsPanel;
    private int _optionHeight;
    private bool _layingOut;

    public ModRadioGroupCard()
    {
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _titleLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        Controls.Add(_titleLabel);

        _descriptionLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(175, 180, 195),
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        Controls.Add(_descriptionLabel);

        _optionsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            AutoScroll = false,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TabStop = false
        };
        Controls.Add(_optionsPanel);
    }

    public string ModName
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value ?? string.Empty;
            PerformContentLayout();
        }
    }

    public string Description
    {
        get => _descriptionLabel.Text;
        set
        {
            _descriptionLabel.Text = value ?? string.Empty;
            PerformContentLayout();
        }
    }

    /// <summary>Adds one selectable option to the card's radio group.</summary>
    public RadioButton AddOption(string text)
    {
        var radio = new RadioButton
        {
            Text = text ?? string.Empty,
            AutoSize = false,
            AutoCheck = true,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Cursor = Cursors.Hand,
            UseMnemonic = false,
            Margin = Padding.Empty,
            TabStop = true,
            AccessibleName = text ?? string.Empty
        };

        // Measure once so every option row has the same comfortable height.
        int preferred = radio.GetPreferredSize(Size.Empty).Height + 4;
        _optionHeight = Math.Max(_optionHeight, Math.Max(26, preferred));

        _optionsPanel.Controls.Add(radio);
        PerformContentLayout();
        return radio;
    }

    public void ClearOptions()
    {
        _optionsPanel.Controls.Clear();
        _optionHeight = 0;
        PerformContentLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PerformContentLayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        PerformContentLayout();
    }

    private void PerformContentLayout()
    {
        if (_layingOut || IsDisposed)
            return;

        _layingOut = true;

        try
        {
            const int horizontal = 14;
            const int top = 10;
            const int bottom = 12;
            const int gap = 4;

            int width = Math.Max(80, ClientSize.Width - horizontal * 2);

            int y = top;
            int titleHeight = _titleLabel.Font.Height + 4;
            _titleLabel.SetBounds(horizontal, y, width, titleHeight);
            y += titleHeight + gap;

            int descriptionHeight = MeasureDescriptionHeight(width);
            _descriptionLabel.SetBounds(horizontal, y, width, descriptionHeight);
            if (descriptionHeight > 0)
                y += descriptionHeight + 6;

            int optionHeight = Math.Max(26, _optionHeight);
            int optionsHeight = _optionsPanel.Controls.Count * optionHeight;
            _optionsPanel.SetBounds(horizontal, y, width, optionsHeight);

            foreach (Control option in _optionsPanel.Controls)
            {
                option.Width = width;
                option.Height = optionHeight;
            }

            y += optionsHeight + bottom;

            if (Height != y)
                Height = y;
        }
        finally
        {
            _layingOut = false;
        }
    }

    private int MeasureDescriptionHeight(int width)
    {
        if (string.IsNullOrWhiteSpace(_descriptionLabel.Text))
            return 0;

        Size measured = TextRenderer.MeasureText(
            _descriptionLabel.Text,
            _descriptionLabel.Font,
            new Size(Math.Max(1, width), int.MaxValue),
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.TextBoxControl);

        return Math.Max(_descriptionLabel.Font.Height, measured.Height);
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
