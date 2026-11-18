using EscapeBook.Controls;
using EscapeBook.Services;

namespace EscapeBook.Forms;

internal enum LinkAccountKind
{
    Telegram,
    Discord
}

/// <summary>
/// Small dark rounded dialog with copyable linking instructions and two
/// buttons aligned on the same Y coordinate ("Скопировать текст" / "Закрыть").
/// </summary>
internal sealed class LinkInstructionsDialog : BaseDarkForm
{
    private readonly RoundedTextBox _textBox;
    private readonly RoundedButton _copyButton;

    public LinkInstructionsDialog(string title, string text)
        : base(new Size(560, 430))
    {
        TitleText = title;
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);

        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        CollapseTitleBar();

        var card = new RoundedPanel
        {
            Location = new Point(16, 16),
            Size = new Size(528, 390),
            CornerRadius = 16,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            BorderSize = 1
        };
        ContentArea.Controls.Add(card);

        UiFactory.Label(
            card,
            title,
            Theme.Font(12f, FontStyle.Bold),
            Theme.Text,
            Theme.Surface,
            new Rectangle(24, 20, 480, 26));

        _textBox = new RoundedTextBox
        {
            Location = new Point(24, 56),
            Size = new Size(480, 244),
            Multiline = true,
            ReadOnly = true,
            CornerRadius = 10,
            Text = text
        };
        _textBox.InnerTextBox.TabStop = false;
        _textBox.InnerTextBox.SelectionStart = 0;
        _textBox.InnerTextBox.SelectionLength = 0;
        card.Controls.Add(_textBox);

        // Both buttons share the exact same Y coordinate.
        const int buttonTop = 320;

        _copyButton = new RoundedButton
        {
            Text = "Скопировать текст",
            Location = new Point(24, buttonTop),
            Size = new Size(180, 38),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.SurfaceHover,
            HoverBackColor = Color.FromArgb(38, 43, 56),
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text
        };
        _copyButton.Click += (_, _) => CopyInstructions();
        card.Controls.Add(_copyButton);

        var closeButton = new RoundedButton
        {
            Text = "Закрыть",
            Location = new Point(344, buttonTop),
            Size = new Size(160, 38),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
        };
        closeButton.Click += (_, _) => Close();
        card.Controls.Add(closeButton);
    }

    private void CopyInstructions()
    {
        try
        {
            Clipboard.SetText(_textBox.Text);
            _copyButton.Text = "Скопировано";
            _copyButton.Enabled = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard copy", ex);
        }
    }
}
