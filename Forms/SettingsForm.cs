using System.Drawing;
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
    private FlowLayoutPanel _modsPanel = null!;
    private readonly string _buildId;

    public string CustomJvmArgs => _argsBox.Text.Trim();
    public string JavaPath => _pathBox.Text.Trim();
    public int? AppliedJvmPresetVersionOnSave { get; private set; }

    private bool _resetArgsClicked;
    private bool _resetModsClicked;

    public SettingsForm(LauncherConfig config, UserSettings current) : base(new Size(620, 720))
    {
        TitleText = "Настройки";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;

        // Allow scrolling when content exceeds the form (e.g. 125% DPI scaling).
        ContentArea.AutoScroll = true;

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

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Padding = new Padding(16, 12, 16, 12),
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ContentArea.Controls.Add(root);

        int row = 0;
        void Add(Control c, int marginBottom = 10)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            root.SetRow(c, row);
            root.SetColumn(c, 0);
            root.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            row++;
        }

        // ---- Header ----
        var title = UiFactory.Label(
            root,
            "Параметры запуска",
            Theme.Font(16f, FontStyle.Bold),
            Theme.Text,
            Theme.Background);
        title.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(title);

        var subtitle = UiFactory.Label(
            root,
            "JVM-аргументы и путь к Java",
            Theme.Font(8.5f),
            Theme.Muted,
            Theme.Background);
        subtitle.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(subtitle, 6);

        // ---- JVM arguments ----
        var argsLabel = UiFactory.Label(
            root,
            "ДОПОЛНИТЕЛЬНЫЕ JVM-АРГУМЕНТЫ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Background);
        argsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(argsLabel, 2);

        _argsBox = new RoundedTextBox
        {
            Multiline = true,
            Height = 120,
            Text = _current.CustomJvmArgs
        };
        _argsBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        Add(_argsBox, 6);

        var ramNote = UiFactory.Label(
            root,
            "Память задаётся ползунком на главном экране: -Xmx и -Xms из этих аргументов игнорируются.",
            Theme.Font(8.5f),
            Theme.Muted,
            Theme.Background);
        ramNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(ramNote, 6);

        var resetArgs = new RoundedButton
        {
            Text = "Сбросить аргументы по умолчанию",
            Width = 250,
            Height = 36,
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
        Add(resetArgs, 10);

        // ---- Java path ----
        var javaLabel = UiFactory.Label(
            root,
            "ПУТЬ К javaw.exe",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Background);
        javaLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(javaLabel, 2);

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
        Add(javaRow, 6);

        var autoNote = UiFactory.Label(
            root,
            "Пусто — автоопределение, включая рантайм Mojang, который скачивает CmlLib.",
            Theme.Font(8.5f),
            Theme.Muted,
            Theme.Background);
        autoNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(autoNote, 12);

        // ---- Optimization & visual mods ----
        var sectionHeader = UiFactory.Label(
            root,
            "ОПТИМИЗАЦИЯ И ВИЗУАЛ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Accent,
            Theme.Background);
        sectionHeader.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(sectionHeader, 2);

        var sectionNote = UiFactory.Label(
            root,
            "Отключение mods-файлов через суффикс .disabled (состояние сохраняется между обновлениями сборки).",
            Theme.Font(8.5f),
            Theme.Muted,
            Theme.Background);
        sectionNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Add(sectionNote, 6);

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
        modsBar.Controls.Add(new Label { Height = 1 }, 1, 0); // spacer
        Add(modsBar, 6);

        _modsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Background,
            Height = 260,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _modsPanel.Resize += (_, _) => RefreshModCardWidths();
        Add(_modsPanel, 10);

        if (_config.OptionalMods.Count == 0)
        {
            var empty = UiFactory.Label(
                _modsPanel,
                "Дополнительные моды не настроены на сервере.",
                Theme.Font(9f),
                Theme.Muted,
                Theme.Background);
            empty.AutoSize = false;
            empty.Width = _modsPanel.ClientSize.Width > 0 ? _modsPanel.ClientSize.Width - 20 : 500;
        }

        BuildModCards();

        // ---- Error label ----
        _errorLabel = UiFactory.Label(
            root,
            string.Empty,
            Theme.Font(8.5f),
            Theme.Danger,
            Theme.Background);
        _errorLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _errorLabel.Visible = false;
        Add(_errorLabel, 12);

        // ---- Action buttons ----
        var buttonBar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            Height = 44,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var saveButton = new RoundedButton
        {
            Text = "Сохранить",
            Width = 132,
            Height = 40,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
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
            ForeColor = Theme.Text
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        buttonBar.Controls.Add(cancelButton);
        Add(buttonBar, 0);

        ContentArea.ResumeLayout();
    }

    private void BuildModCards()
    {
        _modsPanel.Controls.Clear();

        foreach (var mod in _config.OptionalMods)
        {
            var state = OptionalModsService.GetState(mod, _buildId);

            var card = new RoundedPanel
            {
                CornerRadius = 10,
                FillColor = Theme.Surface,
                BorderColor = Theme.Border,
                BorderSize = 1,
                Height = 78,
                Width = _modsPanel.ClientSize.Width > 0 ? _modsPanel.ClientSize.Width - 20 : 560,
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
                Anchor = AnchorStyles.Left,
            };
            toggle.Tag = mod;
            toggle.CheckedChanged += (_, _) =>
            {
                // Reflect immediately; persisted on Save.
            };

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
                MaximumSize = new Size(420, 0)
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

            _modsPanel.Controls.Add(card);
        }
    }

    private void RefreshModCardWidths()
    {
        int width = _modsPanel.ClientSize.Width - 20;
        if (width < 100)
            width = 100;
        foreach (Control c in _modsPanel.Controls)
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
            BuildModCards(); // refresh checkbox states from disk
            _errorLabel.Visible = false;
            SetStatus($"Применены значения по умолчанию (изменено файлов: {changed})", Theme.Muted);
        }
        catch (Exception ex)
        {
            AppLog.Error("Reset mods to defaults", ex);
            ShowValidationError("Не удалось применить значения по умолчанию.");
        }
    }

    private void SetStatus(string text, Color color)
    {
        // Status is owned by the MainForm; keep a local trace instead.
        AppLog.Write(text);
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
            foreach (Control card in _modsPanel.Controls)
            {
                if (card.Controls[0] is not TableLayoutPanel inner)
                    continue;
                if (inner.Controls[0] is not ToggleSwitch toggle)
                    continue;

                var mod = (OptionalModInfo)toggle.Tag!;
                OptionalModsService.SetEnabled(mod, _buildId, toggle.Checked);
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
