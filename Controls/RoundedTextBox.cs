using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

/// <summary>
/// A borderless dark text box wrapped in a rounded, focus-aware surface.
/// </summary>
internal sealed class RoundedTextBox : Panel
{
    private static readonly Color InvalidFill = Color.FromArgb(58, 25, 32);

    private readonly TextBox _textBox;
    private bool _focused;
    private bool _invalid;
    private int _cornerRadius = 10;
    private string _placeholderText = string.Empty;

    public RoundedTextBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Input;
        Padding = new Padding(12, 8, 12, 8);
        Cursor = Cursors.IBeam;

        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = Theme.Font(10f),
            AutoSize = false,
            TabStop = true
        };

        _textBox.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _textBox.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _textBox.TextChanged += (_, e) => OnTextChanged(e);
        _textBox.HandleCreated += (_, _) => ApplyPlaceholder();

        Controls.Add(_textBox);
        Height = 40;
        LayoutTextBox();
    }

    public TextBox InnerTextBox => _textBox;

    /// <summary>
    /// Highlights the field with a red border/background when the current value
    /// does not pass validation.
    /// </summary>
    public bool IsInvalid
    {
        get => _invalid;
        set
        {
            if (_invalid == value)
                return;

            _invalid = value;
            _textBox.BackColor = value ? InvalidFill : Theme.Input;
            Invalidate();
        }
    }

    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = Math.Max(0, value); Invalidate(); }
    }

    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            ApplyPlaceholder();
        }
    }

    public bool Multiline
    {
        get => _textBox.Multiline;
        set
        {
            _textBox.Multiline = value;
            _textBox.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            _textBox.WordWrap = value;
            ApplyPlaceholder();
            LayoutTextBox();
            Invalidate();
        }
    }

    public bool ReadOnly
    {
        get => _textBox.ReadOnly;
        set
        {
            _textBox.ReadOnly = value;
            _textBox.BackColor = Theme.Input;
            _textBox.ForeColor = Theme.Text;
        }
    }

    [AllowNull]
    public override string Text
    {
        get => _textBox.Text;
        set => _textBox.Text = value ?? string.Empty;
    }

    private void ApplyPlaceholder()
    {
        _textBox.PlaceholderText = _textBox.Multiline ? string.Empty : _placeholderText;
    }

    private void LayoutTextBox()
    {
        if (_textBox is null || Width <= 0 || Height <= 0)
            return;

        int width = Math.Max(1, Width - Padding.Horizontal);

        if (_textBox.Multiline)
        {
            int height = Math.Max(1, Height - Padding.Vertical);
            _textBox.Bounds = new Rectangle(Padding.Left, Padding.Top, width, height);
        }
        else
        {
            int height = Math.Max(16, _textBox.Font.Height + 4);
            _textBox.Bounds = new Rectangle(Padding.Left, (Height - height) / 2, width, height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _textBox.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        LayoutTextBox();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Blend the rounded corners into the real backdrop (never black).
        e.Graphics.Clear(Theme.EffectiveBackColor(Parent));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(_invalid ? InvalidFill : Theme.Input);
        graphics.FillPath(fill, path);

        Color borderColor = _invalid
            ? Theme.Danger
            : (_focused ? Theme.Accent : Theme.Border);

        using var pen = new Pen(borderColor, _invalid || _focused ? 1.6f : 1f);
        graphics.DrawPath(pen, path);
    }
}