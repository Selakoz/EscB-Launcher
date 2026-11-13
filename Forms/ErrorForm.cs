using System.Diagnostics;
using System.Drawing;
using EscapeBook.Controls;
using EscapeBook.Services;

namespace EscapeBook.Forms;

internal sealed class ErrorForm : BaseDarkForm
{
    public ErrorForm(
        string title,
        string userMessage,
        string? fullDetails = null,
        string? logPath = null)
        : base(new Size(580, 400))
    {
        TitleText = "Ошибка";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;

        var iconPanel = new RoundedPanel
        {
            Location = new Point(24, 22),
            Size = new Size(44, 44),
            CornerRadius = 12,
            FillColor = Color.FromArgb(48, 27, 34),
            BorderColor = Color.FromArgb(92, 45, 55),
            BorderSize = 1
        };
        ContentArea.Controls.Add(iconPanel);

        var icon = new Label
        {
            AutoSize = false,
            BackColor = iconPanel.FillColor,
            ForeColor = Theme.Danger,
            Font = Theme.Font(18f, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(44, 44),
            Text = "!",
            TextAlign = ContentAlignment.MiddleCenter
        };
        iconPanel.Controls.Add(icon);

        UiFactory.Label(
            ContentArea,
            title,
            Theme.Font(14f, FontStyle.Bold),
            Theme.Text,
            Theme.Background,
            new Rectangle(82, 24, 474, 40));

        var details = new RoundedTextBox
        {
            Location = new Point(24, 84),
            Size = new Size(532, 150),
            Multiline = true,
            ReadOnly = true,
            CornerRadius = 10,
            Text = userMessage ?? string.Empty
        };
        details.InnerTextBox.TabStop = false;
        details.InnerTextBox.SelectionStart = 0;
        details.InnerTextBox.SelectionLength = 0;
        ContentArea.Controls.Add(details);

        int nextY = 248;

        if (!string.IsNullOrWhiteSpace(fullDetails))
        {
            var showDetails = new LinkLabel
            {
                Location = new Point(24, nextY),
                Size = new Size(260, 20),
                Text = "Показать полный текст ошибки »",
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Theme.Background,
                Font = Theme.Font(9f)
            };
            showDetails.LinkClicked += (_, _) => OpenDetails(fullDetails);
            ContentArea.Controls.Add(showDetails);
            nextY += 24;
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            UiFactory.Label(
                ContentArea,
                $"Лог: {logPath}",
                Theme.Font(8.5f),
                Theme.Muted,
                Theme.Background,
                new Rectangle(24, nextY, 532, 20));
            nextY += 24;
        }

        var closeButton = new RoundedButton
        {
            Text = "Закрыть",
            Location = new Point(436, nextY + 8),
            Size = new Size(120, 40),
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
        };
        closeButton.Click += (_, _) => Close();
        ContentArea.Controls.Add(closeButton);
    }

    private void OpenDetails(string fullDetails)
    {
        using var detailForm = new ErrorDetailForm(fullDetails);
        detailForm.ShowDialog(this);
    }
}

internal sealed class ErrorDetailForm : BaseDarkForm
{
    public ErrorDetailForm(string fullDetails) : base(new Size(700, 520))
    {
        TitleText = "Подробности ошибки";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;

        UiFactory.Label(
            ContentArea,
            "Полный текст исключения",
            Theme.Font(12f, FontStyle.Bold),
            Theme.Text,
            Theme.Background,
            new Rectangle(24, 16, 652, 26));

        var textbox = new RoundedTextBox
        {
            Location = new Point(24, 50),
            Size = new Size(652, 320),
            Multiline = true,
            ReadOnly = true,
            CornerRadius = 10,
            Text = fullDetails
        };
        textbox.InnerTextBox.TabStop = false;
        ContentArea.Controls.Add(textbox);

        var copyButton = new RoundedButton
        {
            Text = "Скопировать текст",
            Location = new Point(24, 386),
            Size = new Size(150, 40),
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(textbox.Text);
                copyButton.Text = "Скопировано";
                copyButton.Enabled = false;
            }
            catch (Exception ex)
            {
                AppLog.Error("Clipboard copy", ex);
            }
        };
        ContentArea.Controls.Add(copyButton);

        var closeButton = new RoundedButton
        {
            Text = "Закрыть",
            Location = new Point(526, 386),
            Size = new Size(150, 40),
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
        };
        closeButton.Click += (_, _) => Close();
        ContentArea.Controls.Add(closeButton);
    }
}
