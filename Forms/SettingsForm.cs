using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EscapeBook.Controls;
using EscapeBook.Models;
using EscapeBook.Services;

namespace EscapeBook.Forms;

internal sealed class SettingsForm : BaseDarkForm
{
    private readonly LauncherConfig _config;
    private readonly UserSettings _current;
    private RoundedTextBox _argsBox = null!;
    private RoundedTextBox _pathBox = null!;
    private Label _errorLabel = null!;

    private ModernSlider _ramSlider = null!;
    private Label _ramValueLabel = null!;
    private Label _ramWarningLabel = null!;

    public string CustomJvmArgs => _argsBox.Text.Trim();
    public string JavaPath => _pathBox.Text.Trim();
    public int? AppliedJvmPresetVersionOnSave { get; private set; }

    private bool _resetArgsClicked;
    private bool _resetModsClicked;
    private readonly string _buildId;

    public SettingsForm(LauncherConfig config, UserSettings current) : base(new Size(660, 720))
    {
        TitleText = "Настройки";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;

        _config = config;
        _current = current.Clone();
        _buildId = string.IsNullOrWhiteSpace(current.SelectedBuildId)
            ? (config.Builds.Count > 0 ? config.Builds[0].Id : string.Empty)
            : current.SelectedBuildId;

        BuildLayout();
    }

    private void BuildLayout()
    {
        ContentArea.SuspendLayout();

        // ---- TabControl ----
        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };

        var tabLaunch = new TabPage("Запуск и память")
        {
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };
        var tabMods = new TabPage("Оптимизация и визуал")
        {
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };

        tabControl.TabPages.Add(tabLaunch);
        tabControl.TabPages.Add(tabMods);

        // ===== Tab 1: Запуск и память =====
        var tab1 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(14, 10, 14, 10),
            Padding = Padding.Empty
        };
        tab1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int t1 = 0;
        void T1Add(Control c, int marginBottom = 8)
        {
            tab1.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            tab1.SetRow(c, t1);
            tab1.SetColumn(c, 0);
            tab1.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            c.Dock = DockStyle.Top;
            t1++;
        }

        var ramSection = UiFactory.Label(
            tab1,
            "ОПЕРАТИВНАЯ ПАМЯТЬ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Color.Transparent);
        ramSection.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T1Add(ramSection, 1);

        _ramSlider = new ModernSlider
        {
            Minimum = 1024,
            Maximum = 8192,
            Step = 512,
            Value = 4096,
            Height = 30
        };
        _ramSlider.ValueChanged += (_, _) => UpdateRamWarning();
        T1Add(_ramSlider, 4);

        _ramValueLabel = UiFactory.Label(
            tab1,
            "4096 МБ",
            Theme.Font(10f, FontStyle.Bold),
            Theme.Accent,
            Color.Transparent);
        _ramValueLabel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _ramValueLabel.TextAlign = ContentAlignment.MiddleRight;
        T1Add(_ramValueLabel, 2);

        _ramWarningLabel = UiFactory.Label(
            tab1,
            "Оптимальный объем памяти.",
            Theme.Font(8.5f),
            Theme.Success,
            Color.Transparent);
        _ramWarningLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T1Add(_ramWarningLabel, 8);

        var jvmSection = UiFactory.Label(
            tab1,
            "JVM-АРГУМЕНТЫ ЗАПУСКА",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Color.Transparent);
        jvmSection.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T1Add(jvmSection, 1);

        _argsBox = new RoundedTextBox
        {
            Multiline = true,
            Height = 110,
            Text = _current.CustomJvmArgs
        };
        _argsBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        T1Add(_argsBox, 6);

        var ramNote = UiFactory.Label(
            tab1,
            "Флаги -Xmx и -Xms из этого поля игнорируются: память задаётся ползунком выше.",
            Theme.Font(8.5f),
            Theme.Muted,
            Color.Transparent);
        ramNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T1Add(ramNote, 8);

        var resetArgs = new RoundedButton
        {
            Text = "Сбросить аргументы по умолчанию",
            Width = 250,
            Height = 34,
            Font = Theme.Font(9f, FontStyle.Bold),
            CornerRadius = 8,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        resetArgs.Click += (_, _) =>
        {
            _argsBox.Text = _config.DefaultJvmArgs;
            _resetArgsClicked = true;
            _errorLabel.Visible = false;
        };
        T1Add(resetArgs, 10);

        var javaSection = UiFactory.Label(
            tab1,
            "ПУТЬ К javaw.exe",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Color.Transparent);
        javaSection.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T1Add(javaSection, 1);

        var javaRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Absolute, 40f) },
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _pathBox = new RoundedTextBox
        {
            PlaceholderText = "Автоопределение",
            Text = _current.JavaPath
        };
        _pathBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        javaRow.Controls.Add(_pathBox, 0, 0);

        var browseButton = new RoundedButton
        {
            Text = "Обзор...",
            Width = 110,
            Height = 40,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        browseButton.Click += (_, _) => BrowseForJava();
        javaRow.Controls.Add(browseButton, 1, 0);
        T1Add(javaRow, 0);

        tabLaunch.Controls.Add(tab1);

        // ===== Tab 2: Оптимизация и визуал =====
        var tab2 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(14, 10, 14, 10),
            Padding = Padding.Empty
        };
        tab2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int t2 = 0;
        void T2Add(Control c, int marginBottom = 8)
        {
            tab2.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            tab2.SetRow(c, t2);
            tab2.SetColumn(c, 0);
            tab2.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            c.Dock = DockStyle.Top;
            t2++;
        }

        var modsSection = UiFactory.Label(
            tab2,
            "ОПТИМИЗАЦИЯ И ВИЗУАЛ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Accent,
            Color.Transparent);
        modsSection.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T2Add(modsSection, 1);

        var modsNote = UiFactory.Label(
            tab2,
            "Отключение mods-файлов через суффикс .disabled (состояние сохраняется между обновлениями сборки).",
            Theme.Font(8.5f),
            Theme.Muted,
            Color.Transparent);
        modsNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        T2Add(modsNote, 6);

        var modsBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100f) },
            RowStyles = { new RowStyle(SizeType.Absolute, 34f) },
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var resetMods = new RoundedButton
        {
            Text = "Сбросить по умолчанию",
            Width = 170,
            Height = 34,
            Font = Theme.Font(9f, FontStyle.Bold),
            CornerRadius = 8,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        resetMods.Click += ResetMods_Click;
        modsBar.Controls.Add(resetMods, 0, 0);
        modsBar.Controls.Add(new Label { Height = 1 }, 1, 0);
        T2Add(modsBar, 6);

        var modsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        modsPanel.Resize += (_, _) => RefreshModCardWidths(modsPanel);
        T2Add(modsPanel, 0);

        if (_config.OptionalMods.Count == 0)
        {
            var empty = UiFactory.Label(
                modsPanel,
                "Дополнительные моды не настроены на сервере.",
                Theme.Font(9f),
                Theme.Muted,
                Theme.Background);
            empty.AutoSize = false;
            empty.Width = modsPanel.ClientSize.Width > 0 ? modsPanel.ClientSize.Width - 20 : 500;
            empty.Location = new Point(0, 0);
        }

        BuildModCards(modsPanel);
        tabMods.Controls.Add(tab2);

        // ===== Bottom action buttons (Save / Cancel) =====
        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = new Padding(14, 6, 14, 10)
        };

        var saveButton = new RoundedButton
        {
            Text = "Сохранить",
            Width = 132,
            Height = 40,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        saveButton.Click += SaveAndClose;
        buttonBar.Controls.Add(saveButton);

        var cancelButton = new RoundedButton
        {
            Text = "Отмена",
            Width = 120,
            Height = 40,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        buttonBar.Controls.Add(cancelButton);

        ContentArea.Controls.Add(tabControl);
        ContentArea.Controls.Add(buttonBar);

        ContentArea.ResumeLayout();
    }

    private void BuildModCards(Panel modsPanel)
    {
        modsPanel.Controls.Clear();

        // Add in reverse order with Dock.Top so they stack top-to-bottom in config order.
        foreach (var mod in Enumerable.Reverse(_config.OptionalMods))
        {
            var state = OptionalModsService.GetState(mod, _buildId);

            var card = new RoundedPanel
            {
                CornerRadius = 10,
                FillColor = Theme.Surface,
                BorderColor = Theme.Border,
                BorderSize = 1,
                Height = 78,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnStyles =
                {
                    new ColumnStyle(SizeType.AutoSize),
                    new ColumnStyle(SizeType.Percent, 100f)
                },
                RowStyles = { new RowStyle(SizeType.Percent, 100f) },
                Margin = Padding.Empty,
                Padding = new Padding(12, 8, 12, 8)
            };

            var toggle = new ToggleSwitch
            {
                Checked = state.Enabled,
                Enabled = state.HasFiles,
                Anchor = AnchorStyles.Left
            };
            toggle.Tag = mod;

            var textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f) },
                RowStyles =
                {
                    new RowStyle(SizeType.AutoSize),
                    new RowStyle(SizeType.AutoSize)
                },
                Margin = Padding.Empty,
                Padding = new Padding(10, 0, 0, 0)
            };

            var title = new Label
            {
                Text = mod.Name,
                Font = Theme.Font(9.5f, FontStyle.Bold),
                ForeColor = state.HasFiles ? Theme.Text : Theme.Muted,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                AutoSize = false,
                Height = 18
            };

            var desc = new Label
            {
                Text = mod.Description,
                Font = Theme.Font(8.5f),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                AutoSize = false,
                Height = 30,
                MaximumSize = new Size(460, 0)
            };

            if (!state.HasFiles)
            {
                title.Text += "  (файлы не найдены в папке mods инстанса)";
            }

            textPanel.Controls.Add(title, 0, 0);
            textPanel.Controls.Add(desc, 0, 1);

            inner.Controls.Add(toggle, 0, 0);
            inner.Controls.Add(textPanel, 1, 0);
            card.Controls.Add(inner);

            modsPanel.Controls.Add(card);
        }
    }

    private void RefreshModCardWidths(Panel modsPanel)
    {
        int width = Math.Max(1, modsPanel.ClientSize.Width - 20);
        foreach (Control c in modsPanel.Controls)
        {
            if (c is RoundedPanel card)
                card.Width = width;
        }
    }

    private void ResetMods_Click(object? sender, EventArgs e)
    {
        try
        {
            int changed = OptionalModsService.ApplyDefaults(_config.OptionalMods, _buildId);
            _resetModsClicked = true;
            // Rebuild cards to reflect the new on-disk state.
            foreach (Control c in ContentArea.Controls)
            {
                if (c is TabControl tc && tc.TabPages.Count > 1)
                {
                    var modsPanel = tc.TabPages[1].Controls[0] as TableLayoutPanel;
                    var panel = modsPanel?.Controls.OfType<Panel>().FirstOrDefault();
                    if (panel != null)
                        BuildModCards(panel);
                }
            }
            _errorLabel.Visible = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Reset mods to defaults", ex);
            ShowValidationError("Не удалось применить значения по умолчанию.");
        }
    }

    private void BrowseForJava()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите javaw.exe",
            Filter = "javaw.exe|javaw.exe|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        string currentPath = _pathBox.Text.Trim();
        if (File.Exists(currentPath))
        {
            string? directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(directory))
                dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.FileName;
            _errorLabel.Visible = false;
        }
    }

    private void UpdateRamWarning()
    {
        int ram = _ramSlider.Value;

        if (ram < 6144)
        {
            _ramWarningLabel.ForeColor = Theme.Danger;
            _ramWarningLabel.Text = "Выделено мало ОЗУ — возможны фризы и вылеты сборки.";
        }
        else if (ram <= 12288)
        {
            _ramWarningLabel.ForeColor = Theme.Success;
            _ramWarningLabel.Text = "Оптимальный объем памяти.";
        }
        else
        {
            _ramWarningLabel.ForeColor = Theme.Warning;
            _ramWarningLabel.Text =
                "Выделение более 12 ГБ может ухудшить производительность из-за особенностей Java.";
        }
    }

    private void SaveAndClose(object? sender, EventArgs e)
    {
        string path = _pathBox.Text.Trim();

        if (path.Length > 0)
        {
            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                path = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                ShowValidationError("Некорректный путь к javaw.exe.");
                return;
            }

            if (!File.Exists(path))
            {
                ShowValidationError("Файл javaw.exe по указанному пути не найден.");
                return;
            }

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowValidationError("Укажите исполняемый файл .exe (обычно javaw.exe).");
                return;
            }
        }

        // Apply optional mod enable/disable to the selected instance mods folder.
        try
        {
            // Find the mods panel on tab 2.
            Panel? modsPanel = null;
            foreach (Control c in ContentArea.Controls)
            {
                if (c is TabControl tc && tc.TabPages.Count > 1)
                {
                    var layout = tc.TabPages[1].Controls[0] as TableLayoutPanel;
                    modsPanel = layout?.Controls.OfType<Panel>().FirstOrDefault();
                }
            }

            if (modsPanel != null)
            {
                foreach (Control card in modsPanel.Controls)
                {
                    if (card.Controls[0] is not TableLayoutPanel inner)
                        continue;
                    if (inner.Controls[0] is not ToggleSwitch toggle)
                        continue;

                    var mod = (OptionalModInfo)toggle.Tag!;
                    OptionalModsService.SetEnabled(mod, _buildId, toggle.Checked);
                }
            }

            if (_resetModsClicked)
                AppLog.Write("Mod defaults reset applied and saved.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Optional mods apply", ex);
        }

        if (_resetArgsClicked)
            AppliedJvmPresetVersionOnSave = _config.JvmPresetVersion;

        _pathBox.Text = path;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowValidationError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }
}
