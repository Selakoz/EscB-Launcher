using System.Diagnostics;
using System.Drawing;
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
    private ModernSlider _ramSlider = null!;
    private Label _ramValueLabel = null!;
    private Label _ramWarningLabel = null!;
    private Label _statusLabel = null!;
    private ModernProgressBar _progressBar = null!;
    private RoundedButton _playButton = null!;
    private RoundedButton _settingsButton = null!;

    private readonly UserSettings _settings;
    private readonly bool _isFirstLaunch;
    private HardwareInfo _hardware = new();
    private BuildInfo _selectedBuild = new();
    private Process? _gameProcess;
    private bool _busy;
    private int _maxRamMb = 8192;

    public MainForm() : base(new Size(700, 840), allowMinimize: true)
    {
        TitleText = "EscB Client";

        _isFirstLaunch = !File.Exists(LauncherPaths.SettingsFile);
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
            Padding = new Padding(16, 12, 16, 12),
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ContentArea.Controls.Add(root);

        int row = 0;
        void Add(Control c)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize, 100f));
            root.SetRow(c, row);
            root.SetColumn(c, 0);
            root.Controls.Add(c);
            row++;
        }

        // ---- 1. Banner centered at top ----
        var bannerHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
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
            Size = new Size(380, 95), // 4:1 aspect ratio
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Background,
            Image = Branding.LoadBanner(),
            Anchor = AnchorStyles.None // centered in the middle cell
        };
        bannerHost.Controls.Add(banner, 1, 0);
        Add(bannerHost);

        // ---- 2. Card: nickname, build selector, RAM ----
        var card = new RoundedPanel
        {
            CornerRadius = 16,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            BorderSize = 1,
            Dock = DockStyle.Top,
            Height = 250,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(18)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        // Controls that should stretch to the full card width.
        var stretchControls = new List<Control>();

        void AddCard(Control c, int marginBottom = 8, bool stretch = false)
        {
            c.Margin = new Padding(0, 0, 0, marginBottom);
            if (stretch)
            {
                c.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                stretchControls.Add(c);
            }
            flow.Controls.Add(c);
        }

        var nickLabel = UiFactory.Label(
            flow,
            "НИКНЕЙМ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        nickLabel.Margin = new Padding(0, 0, 0, 2);
        flow.Controls.Add(nickLabel);

        _nickBox = new RoundedTextBox
        {
            PlaceholderText = "Введите игровой никнейм",
            Text = _settings.Nickname,
            Height = 40
        };
        _nickBox.InnerTextBox.KeyDown += NickBox_KeyDown;
        AddCard(_nickBox, 8, stretch: true);

        var buildLabel = UiFactory.Label(
            flow,
            "СБОРКА СЕРВЕРА",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        buildLabel.Margin = new Padding(0, 0, 0, 2);
        flow.Controls.Add(buildLabel);

        _buildsBox = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 34
        };
        _buildsBox.SelectedIndexChanged += (_, _) =>
        {
            if (_buildsBox.SelectedItem is BuildInfo build)
            {
                _selectedBuild = build;
                _settings.SelectedBuildId = build.Id;
            }
        };
        AddCard(_buildsBox, 8, stretch: true);

        // RAM header: label left, value right.
        var ramHeader = new TableLayoutPanel
        {
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 70f), new ColumnStyle(SizeType.Percent, 30f) },
            RowStyles = { new RowStyle(SizeType.Absolute, 22f) },
            Margin = new Padding(0, 2, 0, 2),
            Padding = Padding.Empty
        };

        var ramLabel = UiFactory.Label(
            ramHeader,
            "ОПЕРАТИВНАЯ ПАМЯТЬ",
            Theme.Font(8.5f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        ramLabel.Anchor = AnchorStyles.Left;

        _ramValueLabel = UiFactory.Label(
            ramHeader,
            "4096 МБ",
            Theme.Font(10f, FontStyle.Bold),
            Theme.Accent,
            Theme.Surface);
        _ramValueLabel.Anchor = AnchorStyles.Right;
        _ramValueLabel.TextAlign = ContentAlignment.MiddleRight;

        ramHeader.Controls.Add(ramLabel, 0, 0);
        ramHeader.Controls.Add(_ramValueLabel, 1, 0);
        ramHeader.Margin = new Padding(0, 0, 0, 2);
        flow.Controls.Add(ramHeader);

        _ramSlider = new ModernSlider
        {
            Minimum = 1024,
            Maximum = 8192,
            Step = 512,
            Value = 4096,
            Height = 30
        };
        _ramSlider.ValueChanged += (_, _) => UpdateRamWarning();
        AddCard(_ramSlider, 4, stretch: true);

        _ramWarningLabel = UiFactory.Label(
            flow,
            "Определение объёма ОЗУ...",
            Theme.Font(8.5f),
            Theme.Muted,
            Theme.Surface);
        _ramWarningLabel.Margin = new Padding(0, 0, 0, 0);
        flow.Controls.Add(_ramWarningLabel);

        card.Controls.Add(flow);

        // Keep stretchable controls full-width whenever the card/flow resizes.
        void StretchCard()
        {
            int w = Math.Max(1, flow.ClientSize.Width - 20);
            foreach (var c in stretchControls)
                c.Width = w;
            ramHeader.Width = w;
        }

        flow.Resize += (_, _) => StretchCard();
        card.Resize += (_, _) => StretchCard();

        Add(card);

        // ---- 3. Buttons: Settings + Play ----
        _settingsButton = new RoundedButton
        {
            Text = "Настройки",
            Width = 160,
            Height = 38,
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
        Add(_settingsButton);

        _playButton = new RoundedButton
        {
            Text = "ИГРАТЬ",
            Height = 58,
            Font = Theme.Font(14f, FontStyle.Bold),
            CornerRadius = 14,
            ShowPlayIcon = true,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _playButton.Click += PlayButton_Click;
        Add(_playButton);

        // ---- 4. Status + progress ----
        _statusLabel = UiFactory.Label(
            root,
            "Готов к запуску",
            Theme.Font(9.5f),
            Theme.Muted,
            Theme.Background);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _statusLabel.Margin = new Padding(0, 10, 0, 0);
        Add(_statusLabel);

        _progressBar = new ModernProgressBar
        {
            Height = 10
        };
        _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _progressBar.Margin = new Padding(0, 6, 0, 0);
        Add(_progressBar);

        // ---- 5. Footer ----
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
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
        Add(footer);

        ContentArea.ResumeLayout();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        try
        {
            var hardware = await Task.Run(HardwareInfoService.Collect);
            if (IsDisposed)
                return;

            _hardware = hardware;
            ApplyRamDefaults();
            ApplyRamLimit();
        }
        catch (Exception ex)
        {
            AppLog.Error("Hardware detection", ex);
            if (!IsDisposed)
            {
                ApplyRamDefaults();
                ApplyRamLimit();
            }
        }

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

    private void ApplyRamDefaults()
    {
        int max = _maxRamMb;
        int intended;

        if (_isFirstLaunch)
        {
            intended = _hardware.TotalRamMb >= 12288 ? 8192 : 6144;
        }
        else
        {
            intended = _settings.RamMb > 0 ? _settings.RamMb : 4096;
        }

        int ram = NormalizeRam(intended);
        _ramSlider.Value = Math.Min(ram, max);
        UpdateRamValue();
    }

    private void ApplyRamLimit()
    {
        _maxRamMb = ComputeMaxRamMb(_hardware.TotalRamMb);

        _ramSlider.Minimum = 1024;
        _ramSlider.Maximum = _maxRamMb;
        _ramSlider.Step = 512;
        _ramSlider.Value = Math.Min(_ramSlider.Value, _maxRamMb);
        UpdateRamValue();
        UpdateRamWarning();

        _ramWarningLabel.Text = _hardware.TotalRamMb > 0
            ? $"Максимум {_maxRamMb:N0} МБ · всего {_hardware.TotalRamMb:N0} МБ (резерв системе)"
            : $"Максимум {_maxRamMb:N0} МБ · объём ОЗУ определить не удалось";
    }

    private static int ComputeMaxRamMb(int totalMb)
    {
        if (totalMb <= 0)
            return 8192;

        int max = Math.Max(1024, totalMb - 2048);
        max = (max / 512) * 512;
        return Math.Max(1024, max);
    }

    private int NormalizeRam(int ramMb)
    {
        int value = (int)(Math.Round(ramMb / 512d) * 512d);
        return Math.Clamp(value, 1024, _maxRamMb);
    }

    private void UpdateRamValue()
    {
        _ramValueLabel.Text = $"{_ramSlider.Value:N0} МБ";
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
        _ramSlider.Enabled = enabled;
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

        _settings.Nickname = nickname;
        _settings.RamMb = NormalizeRam(_ramSlider.Value);

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
        _settings.RamMb = NormalizeRam(_ramSlider.Value);
        SettingsService.TrySave(_settings, out _);

        base.OnFormClosing(e);
    }
}
