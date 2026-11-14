using System.Diagnostics.CodeAnalysis;

namespace EscapeBook.Controls;

/// <summary>
/// Dark single-line / multi-line text field with an inner TextBox that uses
/// BorderStyle.FixedSingle, a dark background and comfortable padding.
/// </summary>
internal sealed class DarkTextBox : UserControl
{
    private readonly TextBox _textBox;

    public DarkTextBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Input;
        Padding = new Padding(2);
        Height = 32;
        Cursor = Cursors.IBeam;

        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f),
            AutoSize = false,
            Dock = DockStyle.Fill
        };

        _textBox.TextChanged += (_, e) => OnTextChanged(e);
        _textBox.Enter += (_, _) => Invalidate();
        _textBox.Leave += (_, _) => Invalidate();
        _textBox.GotFocus += (_, _) => Invalidate();
        _textBox.LostFocus += (_, _) => Invalidate();

        Controls.Add(_textBox);
    }

    public TextBox InnerTextBox => _textBox;

    public string PlaceholderText
    {
        get => _textBox.PlaceholderText;
        set => _textBox.PlaceholderText = value ?? string.Empty;
    }

    public bool Multiline
    {
        get => _textBox.Multiline;
        set
        {
            _textBox.Multiline = value;
            _textBox.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            _textBox.WordWrap = value;
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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        // Keep the inner text box in sync with the control font (Segoe UI).
        _textBox.Font = Font;
    }
}
