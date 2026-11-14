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
    private const int HorizontalMargin = 32;
    private const int BottomMargin = 24;

    private static readonly Regex NicknameRegex =
        new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private readonly LauncherService _launcherService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SettingsSession _session;

    private LauncherConfig? _config;
    private RoundedTextBox _nickBox = null!;
    private RoundedComboBox _buildsBox = null!;
    private Label _statusLabel = null!;
    private ModernProgressBar _progressBar = null!;
    private RoundedButton _playButton = null!;
    private RoundedButton _settingsButton = null!;
    private PictureBox _bannerBox = null!;

    private readonly UserSettings _settings;
    private readonly bool _isFirstLaunch;
    private BuildInfo _selectedBuild = new();
    private Process? _gameProcess;
    private bool _busy;
    private bool _suppressBuildEvents;

    public MainForm() : base(new Size(460, 510), allowMinimize: true)
    {
        TitleText = "EscB Client";
        Font = new Font("Segoe UI", 10f);

        // Standard OS window frame (no custom title bar).
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.MinimizeBox = true;
        this.AutoScaleMode = AutoScaleMode.Font;
        CollapseTitleBar();

        _isFirstLaunch = !File.Exists(LauncherPaths.SettingsFile);
        _settings = SettingsService.Load();
        _session = new SettingsSession(_settings);

        BuildLayout();
        WireAutoSave();
    }

    private void BuildLayout()
    {
        ContentArea.SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(HorizontalMargin, 10, HorizontalMargin, BottomMargin),
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ContentArea.Controls.Add(root);

        int row = 0;
        void Add(Control c, RowStyle style)
        {
            root.RowStyles.Add(style);
            root.SetRow(c, row);
            root.SetColumn(c, 0);
            root.Controls.Add(c);
            row++;
        }

        // ---- 1. Banner (centered horizontally, 380x95 => 4:1) ----
        var bannerHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 105,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8)
        };

        _bannerBox = new PictureBox
        {
            Size = new Size(380, 95),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Image = Branding.LoadBanner()
        };
        bannerHost.Controls.Add(_bannerBox);
        Add(bannerHost, new RowStyle(SizeType.Absolute, 105f));

        // ---- 2. Card: nickname + build version ----
        var card = new RoundedPanel
        {
            CornerRadius = 14,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            BorderSize = 1,
            Dock = DockStyle.Top,
            // Tall enough for both label rows + input rows + internal padding, so the
            // dropdown is never clipped at the bottom.
            Height = 172,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(16)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            // Opaque surface colour: rounded controls blend their corners into the card
            // instead of painting black over a transparent parent.
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int r = 0;
        void AddInner(Control c, int marginBottom)
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
            new Font("Segoe UI", 9f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        nickLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddInner(nickLabel, 4);

        _nickBox = new RoundedTextBox
        {
            PlaceholderText = "Введите игровой никнейм",
            Text = _settings.Nickname,
            Height = 40,
            Font = new Font("Segoe UI", 10f)
        };
        _nickBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _nickBox.InnerTextBox.KeyDown += NickBox_KeyDown;
        AddInner(_nickBox, 12);

        var buildLabel = UiFactory.Label(
            inner,
            "ВЕРСИЯ СБОРКИ",
            new Font("Segoe UI", 9f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        buildLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddInner(buildLabel, 4);

        _buildsBox = new RoundedComboBox
        {
            Height = 36,
            Font = new Font("Segoe UI", 10f),
            Formatter = item => (item as BuildInfo)?.Name ?? item?.ToString() ?? string.Empty
        };
        _buildsBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        AddInner(_buildsBox, 0);

        card.Controls.Add(inner);
        Add(card, new RowStyle(SizeType.AutoSize));

        // ---- 3. Flexible spacer ----
        Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, new RowStyle(SizeType.Percent, 100f));

        // ---- 4. Settings button ----
        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.Percent, 100f)
            },
            RowStyles = { new RowStyle(SizeType.Absolute, 38f) },
            Margin = new Padding(0, 0, 0, 8)
        };

        _settingsButton = new RoundedButton
        {
            Text = "Настройки",
            Width = 120,
            Height = 36,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            // No margin: the button must use the whole 38px row without being clipped.
            Margin = Padding.Empty
        };
        _settingsButton.Click += (_, _) => OpenSettings();
        buttonRow.Controls.Add(_settingsButton, 0, 0);
        buttonRow.Controls.Add(new Label { Height = 1 }, 1, 0);
        // Outer row includes the 8px bottom margin, otherwise the inner row shrinks
        // to 30px and clips the 36px button.
        Add(buttonRow, new RowStyle(SizeType.Absolute, 46f));

        // ---- 5. Status + progress ----
        _statusLabel = UiFactory.Label(
            root,
            "Готов к запуску",
            new Font("Segoe UI", 9.5f),
            Theme.Muted,
            Theme.Background);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _statusLabel.Margin = new Padding(0, 0, 0, 4);
        Add(_statusLabel, new RowStyle(SizeType.Absolute, 20f));

        _progressBar = new ModernProgressBar
        {
            Height = 8
        };
        _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _progressBar.Margin = new Padding(0, 0, 0, 10);
        Add(_progressBar, new RowStyle(SizeType.Absolute, 18f));
        // ---- 6. Play button (24px below it is the window edge) ----
        _playButton = new RoundedButton
        {
            Text = "ИГРАТЬ",
            Height = 58,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            CornerRadius = 14,
            ShowPlayIcon = true,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        _playButton.Click += PlayButton_Click;
        _playButton.Margin = Padding.Empty;
        Add(_playButton, new RowStyle(SizeType.Absolute, 58f));

        ContentArea.ResumeLayout();

        CenterBanner();
    }

    /// <summary>
    /// Keeps the banner strictly centered horizontally and vertically.
    /// The banner's parent is the padded layout table, so the form-relative centre is
    /// converted into the parent's coordinate space.
    /// </summary>
    private void CenterBanner()
    {
        if (_bannerBox is null || _bannerBox.Parent is not Control host)
            return;

        // Exact horizontal centre relative to the form's client area.
        int leftOnForm = (this.ClientSize.Width - _bannerBox.Width) / 2;

        // Convert from form coordinates into the host's coordinate space.
        int hostOffsetX = host.PointToScreen(Point.Empty).X - this.PointToScreen(Point.Empty).X;

        _bannerBox.Left = leftOnForm - hostOffsetX;
        _bannerBox.Top = Math.Max(0, (host.ClientSize.Height - _bannerBox.Height) / 2);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CenterBanner();
    }

    /// <summary>
    /// Persists every setting change instantly.
    /// </summary>
    private void WireAutoSave()
    {
        _nickBox.TextChanged += (_, _) =>
        {
            _settings.Nickname = _nickBox.Text;
            _session.SaveSoon();
        };

        _buildsBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBuildEvents)
                return;

            if (_buildsBox.SelectedItem is BuildInfo build)
            {
                _selectedBuild = build;
                _settings.SelectedBuildId = build.Id;
                _session.Save();
            }
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Re-centre after the window has its final size/font scaling.
        CenterBanner();

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
        if (_config == null || _config.Builds.Count == 0)
        {
            SetStatus("В конфиге нет сборок", Theme.Danger);
            return;
        }

        _suppressBuildEvents = true;
        _buildsBox.SetItems(
            _config.Builds,
            _settings.SelectedBuildId,
            item => (item as BuildInfo)?.Id);

        _selectedBuild = _buildsBox.SelectedItem as BuildInfo ?? new BuildInfo();
        if (_selectedBuild.Id.Length > 0)
            _settings.SelectedBuildId = _selectedBuild.Id;

        _suppressBuildEvents = false;
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

        _settings.Nickname = nickname;

        // Apply default_jvm_args from the server only when the preset version grew.
        bool presetApplied = SettingsService.ApplyJvmPresetIfNeeded(_config, _settings);

        _session.Save();

        if (presetApplied)
        {
            SetStatus("Аргументы обновлены по пресету сервера", Theme.Muted);
        }

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

        try
        {
            using var form = new SettingsForm(_config, _session, _isFirstLaunch);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppLog.Error("Open settings", ex);
            SetStatus("Не удалось открыть настройки", Theme.Danger);
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
        _session.Save();

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Dispose();
            _lifetimeCts.Dispose();
        }

        base.Dispose(disposing);
    }
}
