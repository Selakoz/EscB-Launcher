using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using EscapeBook.Controls;
using EscapeBook.Models;
using EscapeBook.Services;

namespace EscapeBook.Forms;

internal sealed class MainForm : BaseDarkForm
{
    private static readonly Regex NicknameRegex =
        new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private readonly LauncherService _launcherService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    private LauncherConfig? _config;
    private RoundedTextBox _nickBox = null!;
    private DarkComboBox _buildsBox = null!;
    private Label _statusLabel = null!;
    private ModernProgressBar _progressBar = null!;
    private RoundedButton _playButton = null!;
    private RoundedButton _settingsButton = null!;

    private readonly UserSettings _settings;
    private BuildInfo _selectedBuild = new();
    private Process? _gameProcess;
    private bool _busy;

    public MainForm() : base(new Size(460, 480), allowMinimize: true)
    {
        TitleText = "EscB Client";

        // Clean fixed-size OS window (not borderless, not DPI-scaled).
        this.AutoScaleMode = AutoScaleMode.Font;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.TitleBarPanel.Visible = false;

        _settings = SettingsService.Load();

        BuildLayout();
    }

    private void BuildLayout()
    {
        ContentArea.SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(12, 8, 12, 8),
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ContentArea.Controls.Add(root);

        int row = 0;
        void Add(Control c, int marginBottom = 8)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            root.SetRow(c, row);
            root.SetColumn(c, 0);
            root.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            row++;
        }

        // ---- 1. Banner centered at top (380x95, 4:1) ----
        var bannerHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 115,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        bannerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        bannerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        bannerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        bannerHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var banner = new PictureBox
        {
            Size = new Size(380, 95),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Background,
            Image = Branding.LoadBanner(),
            Anchor = AnchorStyles.None
        };
        bannerHost.Controls.Add(banner, 1, 0);
        Add(bannerHost, 10);

        // ---- 2. Card: nickname + build version ----
        var card = new RoundedPanel
        {
            CornerRadius = 14,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            BorderSize = 1,
            Dock = DockStyle.Top,
            Height = 140,
            Margin = Padding.Empty,
            Padding = new Padding(16)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int r = 0;
        void AddInner(Control c, int marginBottom = 6)
        {
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            inner.SetRow(c, r);
            inner.SetColumn(c, 0);
            inner.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            c.Dock = DockStyle.Top;
            r++;
        }

        var nickLabel = UiFactory.Label(
            inner,
            "НИКНЕЙМ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        nickLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddInner(nickLabel, 1);

        _nickBox = new RoundedTextBox
        {
            PlaceholderText = "Введите игровой никнейм",
            Text = _settings.Nickname,
            Height = 40
        };
        _nickBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _nickBox.InnerTextBox.KeyDown += NickBox_KeyDown;
        AddInner(_nickBox, 8);

        var buildLabel = UiFactory.Label(
            inner,
            "ВЕРСИЯ СБОРКИ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        buildLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddInner(buildLabel, 1);

        _buildsBox = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 34
        };
        _buildsBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _buildsBox.SelectedIndexChanged += (_, _) =>
        {
            if (_buildsBox.SelectedItem is BuildInfo build)
            {
                _selectedBuild = build;
                _settings.SelectedBuildId = build.Id;
            }
        };
        AddInner(_buildsBox, 0);

        card.Controls.Add(inner);
        Add(card, 10);

        // ---- 3. Buttons: Settings + Play ----
        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.Percent, 100f)
            },
            RowStyles = { new RowStyle(SizeType.Absolute, 40f) },
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _settingsButton = new RoundedButton
        {
            Text = "Настройки",
            Width = 110,
            Height = 36,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _settingsButton.Click += (_, _) => OpenSettings();
        buttonRow.Controls.Add(_settingsButton, 0, 0);
        buttonRow.Controls.Add(new Label { Height = 1 }, 1, 0); // spacer
        Add(buttonRow, 6);

        _playButton = new RoundedButton
        {
            Text = "ИГРАТЬ",
            Height = 52,
            Font = Theme.Font(14f, FontStyle.Bold),
            CornerRadius = 14,
            ShowPlayIcon = true,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _playButton.Click += PlayButton_Click;
        Add(_playButton, 8);

        // ---- 4. Status + progress ----
        _statusLabel = UiFactory.Label(
            root,
            "Готов к запуску",
            Theme.Font(9.5f),
            Theme.Muted,
            Theme.Background);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _statusLabel.Margin = new Padding(0, 2, 0, 0);
        Add(_statusLabel, 2);

        _progressBar = new ModernProgressBar
        {
            Height = 8
        };
        _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _progressBar.Margin = Padding.Empty;
        Add(_progressBar, 0);

        // ---- 5. Footer ----
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            Padding = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var versionLabel = UiFactory.Label(
            footer,
            "v2.1.0",
            Theme.Font(8f),
            Theme.Muted,
            Theme.Background);
        versionLabel.Anchor = AnchorStyles.Left;

        var creditsLabel = UiFactory.Label(
            footer,
            "CmlLib.Core 4.1.0 · .NET 8",
            Theme.Font(8f),
            Theme.Muted,
            Theme.Background);
        creditsLabel.Anchor = AnchorStyles.Right;
        creditsLabel.TextAlign = ContentAlignment.MiddleRight;

        footer.Controls.Add(versionLabel, 0, 0);
        footer.Controls.Add(creditsLabel, 1, 0);
        Add(footer, 0);

        ContentArea.ResumeLayout();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        try
        {
            _config = await _launcherService.LoadConfigAsync();
            if (!IsDisposed)
                PopulateBuilds();
        }
        catch (Exception ex)
        {
            AppLog.Error("Config load", ex);
            if (!IsDisposed)
                SetStatus("Не удалось загрузить список сборок", Theme.Danger);
        }
    }

    private void PopulateBuilds()
    {
        _buildsBox.Items.Clear();

        if (_config == null || _config.Builds.Count == 0)
        {
            SetStatus("В конфиге нет сборок", Theme.Danger);
            return;
        }

        BuildInfo? selectBuild = null;

        foreach (var build in _config.Builds)
        {
            _buildsBox.Items.Add(build);

            if (string.Equals(build.Id, _settings.SelectedBuildId, StringComparison.OrdinalIgnoreCase))
                selectBuild = build;
        }

        if (selectBuild != null)
            _buildsBox.SelectedItem = selectBuild;
        else if (_buildsBox.Items.Count > 0)
        {
            _buildsBox.SelectedIndex = 0;
            _selectedBuild = (BuildInfo)_buildsBox.Items[0]!;
            _settings.SelectedBuildId = _selectedBuild.Id;
        }
        else
        {
            _selectedBuild = new BuildInfo();
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _nickBox.Enabled = enabled;
        _buildsBox.Enabled = enabled;
        _settingsButton.Enabled = enabled;
        _playButton.Enabled = enabled;
    }

    private async void PlayButton_Click(object? sender, EventArgs e) =>
        await StartGameAsync();

    private async void NickBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (!_busy && _gameProcess == null)
            await StartGameAsync();
    }

    private async Task StartGameAsync()
    {
        if (_busy)
            return;

        if (_gameProcess != null)
        {
            SetStatus("Minecraft уже запущен", Theme.Muted);
            return;
        }

        if (_config == null || _config.Builds.Count == 0)
        {
            SetStatus("Список сборок не загружен", Theme.Danger);
            return;
        }

        string nickname = _nickBox.Text.Trim();
        if (!NicknameRegex.IsMatch(nickname))
        {
            SetStatus("Никнейм: 3–16 символов (A-Z, 0-9, _)", Theme.Danger);
            _nickBox.InnerTextBox.Focus();
            return;
        }

        // RAM is configured in Settings -> "Запуск и память"; value persisted in user_settings.json.
        _settings.Nickname = nickname;

        // Применяем default_jvm_args из сервера, если preset-версия выросла (в т.ч. первый запуск).
        bool presetApplied = SettingsService.ApplyJvmPresetIfNeeded(_config, _settings);

        if (!SettingsService.TrySave(_settings, out string? saveError))
            AppLog.Write("Settings save warning: " + saveError);

        if (presetApplied)
            SetStatus("Аргументы обновлены по пресету сервера", Theme.Muted);

        _busy = true;
        SetControlsEnabled(false);
        _playButton.Text = "ЗАПУСК...";
        _progressBar.Value = 0;
        SetStatus("Подготовка...", Theme.Muted);

        var progress = new Progress<LaunchProgress>(update =>
        {
            if (IsDisposed)
                return;

            _progressBar.Value = update.Percent;
            SetStatus(update.Status, Theme.Muted);
        });

        try
        {
            LaunchResult result = await _launcherService.LaunchAsync(
                _config,
                _selectedBuild,
                _settings,
                progress,
                _lifetimeCts.Token);

            _busy = false;
            AttachGameProcess(result);

            SetControlsEnabled(true);
            _playButton.Enabled = false;
            _playButton.Text = "ИГРА ИДЁТ";
            _progressBar.Value = 100;
            SetStatus($"Minecraft запущен · {Path.GetFileName(result.JavaPath)}", Theme.Success);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // The launcher is closing; no error dialog is needed.
        }
        catch (Exception ex)
        {
            AppLog.Error("Launch", ex);

            if (!IsDisposed)
            {
                SetStatus("Ошибка запуска", Theme.Danger);
                ShowError(
                    "Не удалось запустить Minecraft",
                    BuildUserMessage(ex),
                    BuildFullDetails(ex),
                    null);
            }
        }
        finally
        {
            if (!IsDisposed && _gameProcess == null)
            {
                _busy = false;
                SetControlsEnabled(true);
                _playButton.Text = "ИГРАТЬ";
            }
        }
    }

    private void AttachGameProcess(LaunchResult result)
    {
        Process process = result.Process;
        _gameProcess = process;

        process.Exited += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            TryBeginInvoke(() =>
            {
                if (ReferenceEquals(_gameProcess, process))
                    OnGameExited(result, GetExitCode(process));
            });
        };

        if (process.HasExited)
        {
            TryBeginInvoke(() =>
            {
                if (ReferenceEquals(_gameProcess, process))
                    OnGameExited(result, GetExitCode(process));
            });
        }
    }

    private void TryBeginInvoke(Action action)
    {
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window handle is already gone.
        }
    }

    private static int GetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private void OnGameExited(LaunchResult result, int exitCode)
    {
        if (IsDisposed)
            return;

        _gameProcess = null;
        _busy = false;

        SetControlsEnabled(true);
        _playButton.Enabled = true;
        _playButton.Text = "ИГРАТЬ";
        _progressBar.Value = 100;

        if (exitCode == 0)
        {
            SetStatus("Игра завершена", Theme.Muted);
            return;
        }

        SetStatus($"Игра завершилась с ошибкой (код {exitCode})", Theme.Danger);
        ShowError(
            "Minecraft завершился с ошибкой",
            $"Процесс завершился с кодом {exitCode}.\n\nЛог игры:\n{result.LogPath}",
            null,
            result.LogPath);
    }

    private void OpenSettings()
    {
        if (_busy || _gameProcess != null || _config == null)
            return;

        using var form = new SettingsForm(_config, _settings);
        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomJvmArgs = form.CustomJvmArgs;
        _settings.JavaPath = form.JavaPath;

        if (form.AppliedJvmPresetVersionOnSave.HasValue)
            _settings.AppliedJvmPresetVersion = form.AppliedJvmPresetVersionOnSave.Value;

        if (SettingsService.TrySave(_settings, out string? error))
        {
            SetStatus("Настройки сохранены", Theme.Muted);
        }
        else
        {
            SetStatus("Не удалось сохранить настройки", Theme.Danger);
            AppLog.Write("Settings save warning: " + error);
        }
    }

    private void ShowError(
        string title,
        string userMessage,
        string? fullDetails,
        string? logPath)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        using var form = new ErrorForm(title, userMessage, fullDetails, logPath);
        form.ShowDialog(this);
    }

    private static string BuildUserMessage(Exception exception)
    {
        var builder = new StringBuilder();

        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine(current.Message);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildFullDetails(Exception exception)
    {
        var builder = new StringBuilder();

        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (builder.Length > 0)
                builder.AppendLine(new('-', 60));

            builder.AppendLine("Тип: " + current.GetType().FullName);
            builder.AppendLine("Сообщение: " + current.Message);

            if (!string.IsNullOrEmpty(current.StackTrace))
            {
                builder.AppendLine("Трассировка стека:");
                builder.AppendLine(current.StackTrace);
            }
        }

        builder.AppendLine();
        builder.AppendLine(new('-', 60));
        builder.Append("Лог лаунчера: ");
        builder.AppendLine(LauncherPaths.LauncherLogFile);
        return builder.ToString();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _lifetimeCts.Cancel();

        _settings.Nickname = _nickBox.Text.Trim();
        SettingsService.TrySave(_settings, out _);

        base.OnFormClosing(e);
    }
}
