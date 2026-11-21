using System.Drawing;
using System.Windows.Forms;

namespace EscapeBookBootstrapper
{
    /// <summary>
    /// Минимальное окно первого запуска: автоматическая установка или выбор
    /// папки вручную. Закрытие окна означает отмену установки.
    /// </summary>
    internal sealed class InstallPathForm : Form
    {
        public InstallPathForm(string defaultPath)
        {
            Text = "EscB Client — установка";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(480, 250);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Label title = new Label
            {
                Text = "Куда установить EscB Client?",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 16)
            };

            Label description = new Label
            {
                Text = "Автоматическая установка разместит файлы в папке:\n" +
                       defaultPath + "\n\n" +
                       "Можно выбрать другую папку вручную.",
                AutoSize = false,
                Size = new Size(444, 86),
                Location = new Point(18, 52)
            };

            Button automaticButton = new Button
            {
                Text = "Автоматическая установка (рекомендуется)",
                DialogResult = DialogResult.Yes,
                Size = new Size(444, 38),
                Location = new Point(18, 148)
            };

            Button manualButton = new Button
            {
                Text = "Выбрать папку вручную...",
                DialogResult = DialogResult.No,
                Size = new Size(444, 38),
                Location = new Point(18, 194)
            };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(automaticButton);
            Controls.Add(manualButton);

            AcceptButton = automaticButton;
        }
    }
}
