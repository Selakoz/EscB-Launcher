using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace EscapeBookBootstrapper
{
    /// <summary>
    /// Окно лицензионного соглашения при первом запуске загрузчика.
    /// Пользователь должен явно принять условия или отказаться от установки.
    /// </summary>
    internal sealed class EulaForm : Form
    {
        private static readonly Color AccentColor = Color.FromArgb(0x4C, 0xC8, 0xD6);

        public EulaForm()
        {
            Text = "Лицензионное соглашение — EscB Client";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 500);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Label instruction = new Label
            {
                Text = "Пожалуйста, прочитайте и примите условия перед установкой.",
                AutoSize = false,
                Size = new Size(660, 24),
                Location = new Point(20, 18),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30)
            };

            RichTextBox eulaText = new RichTextBox
            {
                Text = LoadEulaText(),
                ReadOnly = true,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                DetectUrls = false,
                Size = new Size(660, 356),
                Location = new Point(20, 50),
                Font = new Font("Segoe UI", 9F)
            };

            Button acceptButton = new Button
            {
                Text = "Принять и установить",
                DialogResult = DialogResult.OK,
                Size = new Size(320, 40),
                Location = new Point(20, 440),
                BackColor = AccentColor,
                ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            acceptButton.FlatAppearance.BorderSize = 0;

            Button rejectButton = new Button
            {
                Text = "Отклонить",
                DialogResult = DialogResult.Cancel,
                Size = new Size(320, 40),
                Location = new Point(360, 440),
                BackColor = Color.FromArgb(225, 225, 225),
                ForeColor = Color.FromArgb(30, 30, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F)
            };
            rejectButton.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);

            Controls.Add(instruction);
            Controls.Add(eulaText);
            Controls.Add(acceptButton);
            Controls.Add(rejectButton);

            AcceptButton = acceptButton;
            CancelButton = rejectButton;
        }

        /// <summary>
        /// Запрещает закрытие окна крестиком или Alt+F4: выбор должен быть явным.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.None)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private static string LoadEulaText()
        {
            try
            {
                Assembly assembly = typeof(EulaForm).Assembly;

                using (Stream stream = assembly.GetManifestResourceStream("EULA.txt"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                            return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                // Ниже показывается резервный текст, чтобы окно не осталось пустым.
            }

            return "Не удалось загрузить текст лицензионного соглашения.\n\n" +
                   "Нажимая «Принять», вы подтверждаете, что принимаете условия " +
                   "использования EscB Client. Версия соглашения: " + Bootstrapper.EulaVersion;
        }
    }
}
