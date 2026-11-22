using System;
using System.Drawing;
using System.Windows.Forms;

namespace EscapeBookBootstrapper
{
    /// <summary>
    /// Стартовое меню загрузчика: обычный запуск (с проверкой обновлений),
    /// переустановка лаунчера или полное удаление данных.
    /// </summary>
    internal sealed class StartupMenuForm : Form
    {
        private static readonly Color WindowBackColor = Color.FromArgb(0x12, 0x17, 0x19);

        public StartupMenuForm()
        {
            Text = "EscB Client — запуск";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 220);
            BackColor = WindowBackColor;
            Font = new Font("Segoe UI", 9F);

            Label caption = new Label
            {
                Text = "Выберите действие:",
                AutoSize = false,
                Size = new Size(360, 24),
                Location = new Point(30, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White
            };

            Button launchButton = CreateMenuButton(
                "🚀 Обновить и запустить",
                DialogResult.OK,
                Color.FromArgb(0x0B, 0x21, 0x1F),
                Color.FromArgb(0x4C, 0xC8, 0xD6),
                50);

            Button reinstallButton = CreateMenuButton(
                "🔄 Переустановить лаунчер",
                DialogResult.Retry,
                Color.FromArgb(0x2A, 0x1F, 0x0B),
                Color.FromArgb(0xF0, 0xB4, 0x29),
                98);

            Button uninstallButton = CreateMenuButton(
                "🗑️ Удалить лаунчер",
                DialogResult.Abort,
                Color.FromArgb(0x2A, 0x0B, 0x0B),
                Color.FromArgb(0xFF, 0x6B, 0x6B),
                146);

            Controls.Add(caption);
            Controls.Add(launchButton);
            Controls.Add(reinstallButton);
            Controls.Add(uninstallButton);

            // Enter запускает обычный режим. Esc закрывает меню тем же
            // действием: это безопасный вариант, клиент остаётся установленным.
            AcceptButton = launchButton;
            CancelButton = launchButton;
        }

        private static Button CreateMenuButton(
            string text,
            DialogResult result,
            Color backColor,
            Color foreColor,
            int top)
        {
            Button button = new Button
            {
                Text = text,
                DialogResult = result,
                Size = new Size(360, 40),
                Location = new Point(30, top),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            // Фон при наведении/нажатии не меняем: подсвечивается только текст.
            button.FlatAppearance.MouseOverBackColor = backColor;
            button.FlatAppearance.MouseDownBackColor = backColor;

            button.MouseEnter += delegate { button.ForeColor = Brighten(foreColor); };
            button.MouseLeave += delegate { button.ForeColor = foreColor; };

            return button;
        }

        private static Color Brighten(Color color)
        {
            const float amount = 0.35F;

            int red = color.R + (int)((255 - color.R) * amount);
            int green = color.G + (int)((255 - color.G) * amount);
            int blue = color.B + (int)((255 - color.B) * amount);

            return Color.FromArgb(
                Math.Min(255, red),
                Math.Min(255, green),
                Math.Min(255, blue));
        }
    }
}
