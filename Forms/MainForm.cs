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
    private const string DefaultWebsiteUrl = "https://escapebook.site/";

    private static readonly Regex NicknameRegex =
        new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private readonly LauncherService _launcherService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SettingsSession _session;
    private readonly BuildStatusService _statusService = new();

    private LauncherConfig? _config;
    private RoundedTextBox _nickBox = null!;
    private Label _lblAuthStatus = null!;
    private RoundedComboBox _buildsBox = null!;
    private Label _statusLabel = null!;
    private ModernProgressBar _progressBar = null!;
    private RoundedButton _playButton = null!;
    private RoundedButton _settingsButton = null!;
    private RoundedButton _websiteButton = null!;
    private PictureBox _bannerBox = null!;

    private readonly UserSettings _settings;
    private readonly bool _isFirstLaunch;
    private BuildInfo _selectedBuild = new();
    private BuildStatus _selectedStatus = BuildStatus.NotInstalled;
    private Dictionary<string, BuildStatus> _statuses =
        new(StringComparer.OrdinalIgnoreCase);
    private Process? _gameProcess;
    private bool _busy;
    private bool _suppressBuildEvents;

    // ---- Secret builds reveal (Ctrl+Shift+S, this session only) ----
    private bool _showSecretBuilds;
    private Label? _secretToastLabel;
    private System.Windows.Forms.Timer? _secretToastTimer;

    // ---- Official website button debounce ----
    private DateTime _lastWebsiteClick = DateTime.MinValue;

    // ---- Secret build code buffer (typed on the main window) ----
    private string _codeBuffer = string.Empty;

    // ---- Authorization UI state ----
    private readonly ToolTip _toolTip = new();
    private RoundedButton _authTgButton = null!;
    private RoundedButton _authDsButton = null!;
    private RoundedButton _profileButton = null!;
    private ContextMenuStrip? _profileMenu;
    private ToolStripMenuItem? _linkTelegramItem;
    private ToolStripMenuItem? _linkDiscordItem;
    private bool _authorized;

    // ---- Nickname validation / auth status ----
    private string? _authMcNickname;
    private bool _authStatusGreen;
    private bool _nicknameValid;

    public MainForm() : base(new Size(460, 510), allowMinimize: true)
    {
        TitleText = "EscB Client";
        Font = new Font("Segoe UI", 10f);

        // Standard OS window frame (no custom title bar).
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.MinimizeBox = true;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        CollapseTitleBar();

        _isFirstLaunch = !File.Exists(LauncherPaths.SettingsFile);
        _settings = SettingsService.Load();
        _session = new SettingsSession(_settings);

        BuildLayout();
        WireAutoSave();

        // React immediately to session save/clear from the auth dialog or settings.
        SessionManager.AuthChanged += SessionManager_AuthChanged;

        // Secret codes are typed directly on the main window (no dedicated input).
        KeyPreview = true;
        KeyPress += MainForm_KeyPress;
        KeyDown += MainForm_KeyDown;

        // Start in the unauthorized state; OnShown upgrades it when a valid
        // session for this machine (HWID match) is found.
        ApplyAuthState(false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        // WinForms rescales the form frame and the TableLayoutPanel-based
        // layout automatically. Point-sized fonts follow the monitor DPI, so
        // no manual font/position adjustments are required here.
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

        // Nickname header: field label on the left, auth status on the right.
        var nickHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        nickHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nickHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        nickHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nickLabel = UiFactory.Label(
            nickHeader,
            "НИКНЕЙМ",
            new Font("Segoe UI", 9f, FontStyle.Bold),
            Theme.Muted,
            Theme.Surface);
        nickLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        nickLabel.Margin = Padding.Empty;
        nickHeader.Controls.Add(nickLabel, 0, 0);

        _lblAuthStatus = UiFactory.Label(
            nickHeader,
            "❌ Оффлайн аккаунт",
            new Font("Segoe UI", 8.5f),
            Theme.Danger,
            Theme.Surface);
        _lblAuthStatus.AutoSize = true;
        _lblAuthStatus.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _lblAuthStatus.TextAlign = ContentAlignment.MiddleRight;
        _lblAuthStatus.Margin = new Padding(8, 0, 0, 0);
        nickHeader.Controls.Add(_lblAuthStatus, 1, 0);

        AddInner(nickHeader, 4);

        _nickBox = new RoundedTextBox
        {
            PlaceholderText = "Введите никнейм",
            Text = _settings.Nickname,
            Height = 40,
            Font = new Font("Segoe UI", 10f)
        };
        _nickBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _nickBox.InnerTextBox.KeyDown += NickBox_KeyDown;
        _nickBox.InnerTextBox.LostFocus += NickBox_LostFocus;
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
            Formatter = item => (item as BuildListItem)?.LeftText ?? item?.ToString() ?? string.Empty,
            ItemBackColorSelector = item => item is BuildListItem buildItem
                ? BuildListItem.StatusBackColor(buildItem.Status)
                : Color.FromArgb(30, 30, 46),
            // Правая зона строки: статус сборки в круглых скобках.
            ItemRightTextSelector = item => (item as BuildListItem)?.RightText
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

        // ---- Official website button (opens the system default browser) ----
        _websiteButton = new RoundedButton
        {
            Text = "🌐 Сайт",
            Width = 100,
            Height = 36,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 10,
            BackColor = Color.FromArgb(11, 33, 31),        // #0B211F dark emerald
            NormalBackColor = Color.FromArgb(11, 33, 31),
            HoverBackColor = Color.FromArgb(11, 33, 31),
            PressedBackColor = Color.FromArgb(8, 24, 22),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(76, 200, 214),      // #4CC8D6 ice
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(8, 0, 0, 0)
        };
        _websiteButton.FlatAppearance.BorderSize = 0;
        _websiteButton.MouseEnter += (_, _) =>
        {
            _websiteButton.ForeColor = Color.FromArgb(240, 180, 41); // #F0B429 gold
            _websiteButton.Invalidate();
        };
        _websiteButton.MouseLeave += (_, _) =>
        {
            _websiteButton.ForeColor = Color.FromArgb(76, 200, 214);
            _websiteButton.Invalidate();
        };
        _websiteButton.Click += WebsiteButton_Click;
        _toolTip.SetToolTip(_websiteButton, DefaultWebsiteUrl);
        buttonRow.Controls.Add(_websiteButton, 1, 0);

        // ---- Auth area (right side of the settings row) ----
        var authArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        // Profile button (authorized state): nickname + logout context menu.
        _profileButton = new RoundedButton
        {
            Text = "Профиль",
            Width = 150,
            Height = 36,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Accent,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Margin = new Padding(8, 0, 0, 0),
            Visible = false
        };
        _profileButton.Click += (_, _) => ShowProfileMenu();
        _toolTip.SetToolTip(_profileButton, "Ваш аккаунт");
        authArea.Controls.Add(_profileButton);

        // Embedded Assets/ds.png icon (cached); null -> 🎮 glyph fallback below.
        Image? discordIcon = Branding.LoadDiscordAuthIcon();

        _authDsButton = new RoundedButton
        {
            Text = discordIcon == null ? "🎮" : string.Empty,
            Image = discordIcon,
            Width = 46,
            Height = 36,
            Font = new Font("Segoe UI Emoji", 12f, FontStyle.Regular),
            CornerRadius = 10,
            NormalBackColor = Color.FromArgb(88, 101, 242), // Discord blurple
            HoverBackColor = Color.FromArgb(103, 115, 250),
            PressedBackColor = Color.FromArgb(70, 82, 220),
            ForeColor = Theme.Text,
            Margin = new Padding(8, 0, 0, 0)
        };
        _authDsButton.Click += AuthDiscord_Click;
        _toolTip.SetToolTip(_authDsButton, "Авторизоваться через Discord");
        authArea.Controls.Add(_authDsButton);

        // Embedded Assets/tg.png icon (cached); null -> ✈️ glyph fallback below.
        Image? telegramIcon = Branding.LoadTelegramAuthIcon();

        _authTgButton = new RoundedButton
        {
            // Icon when available, otherwise the ✈️ glyph (never crashes).
            Text = telegramIcon == null ? "✈️" : string.Empty,
            Image = telegramIcon,
            Width = 46,
            Height = 36,
            Font = new Font("Segoe UI Emoji", 12f, FontStyle.Regular),
            CornerRadius = 10,
            NormalBackColor = Theme.AccentSoft,
            HoverBackColor = Color.FromArgb(120, 162, 255),
            PressedBackColor = Color.FromArgb(70, 118, 230),
            ForeColor = Theme.Text,
            Margin = new Padding(8, 0, 0, 0)
        };
        _authTgButton.Click += AuthTelegram_Click;
        _toolTip.SetToolTip(_authTgButton, "Авторизоваться через Telegram");
        authArea.Controls.Add(_authTgButton);

        buttonRow.Controls.Add(authArea, 2, 0);
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

        // ---- Secret-builds toast (revealed with Ctrl+Shift+S, session only) ----
        _secretToastLabel = new Label
        {
            AutoSize = false,
            Size = new Size(360, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Surface,
            ForeColor = Theme.Success,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Visible = false
        };
        ContentArea.Controls.Add(_secretToastLabel);
        _secretToastLabel.BringToFront();

        _secretToastTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _secretToastTimer.Tick += (_, _) => HideSecretToast();

        CenterBanner();
        PositionSecretToast();
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
        PositionSecretToast();
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
            UpdateAuthStatus();
        };

        _buildsBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBuildEvents)
                return;

            if (_buildsBox.SelectedItem is BuildListItem item)
            {
                _selectedBuild = item.Build;
                _selectedStatus = item.Status;
                _settings.SelectedBuildId = item.BuildId;
                _session.Save();
                UpdatePlayButtonForSelectedBuild();
            }
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Re-centre after the window has its final size/font scaling.
        CenterBanner();

        // Non-blocking session check: upgrades the UI to "authorized" when a
        // valid session for this machine exists.
        _ = InitializeAuthStateAsync();

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

    // -------------------------------------------------------- authorization

    /// <summary>
    /// Loads the saved session and validates it against the current HWID
    /// (computed off the UI thread). Missing/foreign session keeps the
    /// unauthorized state.
    /// </summary>
    private async Task InitializeAuthStateAsync()
    {
        try
        {
            SessionData? session = SessionManager.LoadSession();
            if (session == null)
                return;

            string hwid = await Task.Run(AuthService.GetHWID);

            if (IsDisposed)
                return;

            if (string.Equals(session.HWID, hwid, StringComparison.Ordinal))
                ApplyAuthState(true, session.McNickname);
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth state init", ex);
        }
    }

    /// <summary>
    /// Switches the whole UI between authorized and unauthorized states:
    /// auth buttons vs profile button, play/build controls locked or not.
    /// </summary>
    private void ApplyAuthState(bool authorized, string? nickname = null, bool clearNickname = false)
    {
        _authorized = authorized;

        _authTgButton.Visible = !authorized;
        _authDsButton.Visible = !authorized;
        _profileButton.Visible = authorized;

        if (authorized)
        {
            if (!string.IsNullOrWhiteSpace(nickname))
                _authMcNickname = nickname.Trim();

            _profileButton.Text = _authMcNickname ?? "Профиль";
        }
        else
        {
            _authMcNickname = null;
            _profileButton.Text = "Профиль";

            // Logout explicitly clears the field; the initial unauthorized
            // state keeps the nickname restored from settings.
            if (clearNickname)
                _nickBox.Text = string.Empty;
        }

        if (!_busy)
            _buildsBox.Enabled = authorized;

        if (authorized)
        {
            _toolTip.SetToolTip(_playButton, string.Empty);
            _toolTip.SetToolTip(_buildsBox, string.Empty);
            SetStatus("Готов к запуску", Theme.Muted);
        }
        else
        {
            const string hint = "Сначала авторизуйтесь через Telegram или Discord";
            _toolTip.SetToolTip(_playButton, hint);
            _toolTip.SetToolTip(_buildsBox, hint);
            SetStatus("Не авторизовано — войдите через Telegram или Discord", Theme.Warning);
        }

        // Auto-fills the nickname for an authorized session and refreshes the
        // status label, validation colours and play-button state.
        UpdateAuthStatus();
    }

    /// <summary>
    /// SessionManager raises this after the session file is saved or cleared.
    /// Keeping the indicator here makes the status update immediately after
    /// linking/logging out, even when the change comes from another dialog.
    /// </summary>
    private void SessionManager_AuthChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => SessionManager_AuthChanged(sender, e));
            return;
        }

        SessionData? session = SessionManager.LoadSession();

        if (session == null)
        {
            ApplyAuthState(false, clearNickname: true);
            return;
        }

        ApplyAuthState(true, session.McNickname);
    }

    /// <summary>
    /// Five-state indicator from the task table:
    /// offline+empty, offline+typed, authorized+auto-filled, authorized+equal,
    /// authorized+mismatch. Also refreshes nickname validation and play button.
    /// </summary>
    private void UpdateAuthStatus()
    {
        if (_lblAuthStatus is null || _nickBox is null)
            return;

        if (string.IsNullOrWhiteSpace(_authMcNickname))
        {
            _authStatusGreen = false;
            bool empty = string.IsNullOrWhiteSpace(_nickBox.Text);

            SetAuthStatus(
                "❌ Оффлайн аккаунт",
                Theme.Danger,
                empty
                    ? "Привяжите аккаунт через Telegram/Discord для входа на сервер"
                    : "С оффлайн аккаунтом нельзя зайти на сервер");
        }
        else
        {
            // Case 3: authorized account with an empty field is auto-filled.
            if (string.IsNullOrWhiteSpace(_nickBox.Text))
                _nickBox.Text = _authMcNickname;

            // Minecraft nicknames are case-insensitive.
            if (string.Equals(
                    _nickBox.Text.Trim(),
                    _authMcNickname,
                    StringComparison.OrdinalIgnoreCase))
            {
                _authStatusGreen = true;
                SetAuthStatus(
                    "✅ Авторизованный аккаунт",
                    Theme.Success,
                    "Можно заходить на сервер");
            }
            else
            {
                _authStatusGreen = false;
                SetAuthStatus(
                    "❌ Оффлайн аккаунт",
                    Theme.Danger,
                    "Никнейм не совпадает с привязанным аккаунтом. Войти на сервер нельзя");
            }
        }

        UpdateNicknameValidation();
        UpdatePlayEnabled();
    }

    private void SetAuthStatus(string text, Color color, string tooltip)
    {
        _lblAuthStatus.Text = text;
        _lblAuthStatus.ForeColor = color;
        _toolTip.SetToolTip(_lblAuthStatus, tooltip);
    }

    /// <summary>
    /// Real-time Minecraft nickname validation: 3-16 letters/digits/underscore.
    /// Invalid values highlight the field and lock the primary action.
    /// </summary>
    private void UpdateNicknameValidation()
    {
        if (_nickBox is null)
            return;

        string nickname = _nickBox.Text.Trim();
        _nicknameValid = NicknameRegex.IsMatch(nickname);
        _nickBox.IsInvalid = !_nicknameValid;

        if (_nicknameValid)
        {
            _toolTip.SetToolTip(_nickBox, string.Empty);
        }
        else if (nickname.Length > 0)
        {
            _toolTip.SetToolTip(
                _nickBox,
                "Никнейм: 3–16 символов (A-Z, a-z, 0-9, _)");
        }
        else
        {
            _toolTip.SetToolTip(_nickBox, "Введите никнейм");
        }
    }

    private void UpdatePlayEnabled()
    {
        if (_playButton is null)
            return;

        _playButton.Enabled =
            !_busy &&
            _gameProcess == null &&
            _authorized &&
            _authStatusGreen &&
            _nicknameValid;
    }

    /// <summary>
    /// Placeholder disappears on focus (native TextBox behaviour). On blur an
    /// empty field is restored to the linked Minecraft nickname.
    /// </summary>
    private void NickBox_LostFocus(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nickBox.Text) &&
            !string.IsNullOrWhiteSpace(_authMcNickname))
        {
            _nickBox.Text = _authMcNickname;
        }

        UpdateAuthStatus();
    }

    private void AuthTelegram_Click(object? sender, EventArgs e) => OpenAuthDialog(AuthChannel.Telegram);

    private void AuthDiscord_Click(object? sender, EventArgs e) => OpenAuthDialog(AuthChannel.Discord);

    /// <summary>
    /// Opens the OTP dialog in the requested channel mode. Both modes share the
    /// nickname/OTP fields and the server-side HWID auto-trust on confirm.
    /// </summary>
    private void OpenAuthDialog(AuthChannel channel)
    {
        if (_busy || _gameProcess != null)
            return;

        try
        {
            using var dialog = new InlineAuthDialog(_nickBox.Text.Trim(), channel);
            if (dialog.ShowDialog(this) != DialogResult.OK || IsDisposed)
                return;

            // Sync the authorized nickname with the launcher settings (autosave).
            _nickBox.Text = dialog.McNickname;
            _settings.Nickname = dialog.McNickname;
            _session.SaveSoon();

            ApplyAuthState(true, dialog.McNickname);
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth dialog", ex);
            SetStatus("Не удалось открыть окно авторизации", Theme.Danger);
        }
    }

    private void ShowProfileMenu()
    {
        BuildProfileMenu();
        _profileMenu?.Show(_profileButton, new Point(0, _profileButton.Height));

        // Query linked accounts asynchronously; the menu is already visible and
        // link items update in place (logout always works).
        _ = RefreshLinkMenuItemsAsync();
    }

    /// <summary>
    /// Builds the dark profile menu: link items (hidden until the async lookup
    /// confirms they are missing), separator, then logout.
    /// </summary>
    private void BuildProfileMenu()
    {
        if (_profileMenu != null)
            return;

        _profileMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f)
        };

        _linkTelegramItem = CreateMenuItem("Привязать Telegram");
        _linkDiscordItem = CreateMenuItem("Привязать Discord");
        _linkTelegramItem.Visible = false;
        _linkDiscordItem.Visible = false;
        _linkTelegramItem.Click += (_, _) => ShowLinkInstructions(LinkAccountKind.Telegram);
        _linkDiscordItem.Click += (_, _) => ShowLinkInstructions(LinkAccountKind.Discord);

        var logoutItem = CreateMenuItem("Выйти из аккаунта");
        logoutItem.Click += (_, _) => PerformLogout();

        _profileMenu.Items.Add(_linkTelegramItem);
        _profileMenu.Items.Add(_linkDiscordItem);
        _profileMenu.Items.Add(new ToolStripSeparator());
        _profileMenu.Items.Add(logoutItem);
    }

    private static ToolStripMenuItem CreateMenuItem(string text)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text
        };
        item.MouseEnter += (_, _) => item.BackColor = Theme.SurfaceHover;
        item.MouseLeave += (_, _) => item.BackColor = Theme.Surface;
        return item;
    }

    /// <summary>
    /// Asks /auth/links which external accounts are linked and shows only the
    /// missing ones. Any failure hides both link items; logout stays usable.
    /// </summary>
    private async Task RefreshLinkMenuItemsAsync()
    {
        string nickname = (_settings.Nickname ?? string.Empty).Trim();
        if (nickname.Length == 0)
            nickname = _nickBox.Text.Trim();

        try
        {
            string hwid = await Task.Run(AuthService.GetHWID);
            LinksResult links = await AuthService.GetLinksAsync(nickname, hwid);

            if (IsDisposed)
                return;

            bool ok = links.Success;
            if (_linkTelegramItem != null)
                _linkTelegramItem.Visible = ok && !links.TgLinked;
            if (_linkDiscordItem != null)
                _linkDiscordItem.Visible = ok && !links.DsLinked;
        }
        catch (Exception ex)
        {
            AppLog.Error("Profile links lookup", ex);
            if (_linkTelegramItem != null)
                _linkTelegramItem.Visible = false;
            if (_linkDiscordItem != null)
                _linkDiscordItem.Visible = false;
        }
    }

    private void ShowLinkInstructions(LinkAccountKind kind)
    {
        string title;
        string text;

        if (kind == LinkAccountKind.Telegram)
        {
            title = "Привязка Telegram";
            text =
                $"Откройте бота в Telegram, нажмите «Запустить», затем напишите администратору {AuthLinks.AdminContact} " +
                "в Telegram или Discord: ваш ник в Minecraft и ваш Telegram ID. " +
                "Узнать ID можно у @userinfobot.";
        }
        else
        {
            title = "Привязка Discord";
            text =
                $"Напишите администратору {AuthLinks.AdminContact} в Discord: ваш ник в Minecraft и ваш Discord ID " +
                "(Настройки → Расширенные → Режим разработчика → ПКМ по себе → Копировать ID).";
        }

        using var dialog = new LinkInstructionsDialog(title, text);
        dialog.ShowDialog(this);
    }

    private void PerformLogout()
    {
        SessionManager.ClearSession();
        ApplyAuthState(false, clearNickname: true);
        SetStatus("Вы вышли из аккаунта", Theme.Muted);
    }

    private void PopulateBuilds()
    {
        if (_config == null || _config.Builds.Count == 0)
        {
            SetStatus("В конфиге нет сборок", Theme.Danger);
            return;
        }

        // Re-check every build against the local instance folders.
        _statuses = _statusService.RefreshStatuses(_config.Builds);

        List<BuildInfo> visibleBuilds = GetVisibleBuilds();

        var items = visibleBuilds
            .Select(build =>
            {
                BuildStatus status = _statuses.TryGetValue(build.Id, out BuildStatus value)
                    ? value
                    : BuildStatus.NotInstalled;

                // The status is rendered in the right zone of the row, not in the text.
                return new BuildListItem
                {
                    BuildId = build.Id,
                    Status = status,
                    Build = build
                };
            })
            .ToList();

        _suppressBuildEvents = true;
        _buildsBox.SetItems(
            items,
            _settings.SelectedBuildId,
            item => (item as BuildListItem)?.BuildId);

        if (_buildsBox.SelectedItem is BuildListItem selected)
        {
            _selectedBuild = selected.Build;
            _selectedStatus = selected.Status;
            _settings.SelectedBuildId = selected.BuildId;
        }
        else
        {
            _selectedBuild = visibleBuilds.Count > 0 ? visibleBuilds[0] : _config.Builds[0];
            _selectedStatus = BuildStatus.NotInstalled;
        }

        _suppressBuildEvents = false;

        UpdatePlayButtonForSelectedBuild();
    }

    // ---------------------------------------------------------- secret builds

    /// <summary>
    /// Builds shown in the dropdown. Secret builds stay hidden unless the user
    /// revealed them for this session (Ctrl+Shift+S) or manually selected one via
    /// <c>selectedBuildId</c> in user_settings.json — the manually chosen build is
    /// always kept visible so friends can still play it.
    /// </summary>
    private List<BuildInfo> GetVisibleBuilds()
    {
        if (_config == null)
            return new List<BuildInfo>();

        return _config.Builds
            .Where(build =>
                !build.IsSecret ||
                _showSecretBuilds ||
                string.Equals(
                    build.Id,
                    _settings.SelectedBuildId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Ctrl+Shift+S on the builds dropdown reveals secret builds for this session.</summary>
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || !e.Shift || e.KeyCode != Keys.S || !_buildsBox.Focused)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        RevealSecretBuilds();
    }

    /// <summary>
    /// Rebuilds the dropdown with secret builds included and shows the reveal
    /// toast. The flag lives only in memory, so a restart hides them again.
    /// </summary>
    private void RevealSecretBuilds()
    {
        if (_busy || _config == null)
            return;

        _showSecretBuilds = true;
        PopulateBuilds();
        ShowSecretToast("Секретные сборки раскрыты на эту сессию");
    }

    private void ShowSecretToast(string text)
    {
        if (_secretToastLabel == null)
            return;

        _secretToastLabel.Text = text;
        PositionSecretToast();
        _secretToastLabel.Visible = true;
        _secretToastLabel.BringToFront();

        _secretToastTimer?.Stop();
        _secretToastTimer?.Start();
    }

    private void HideSecretToast()
    {
        _secretToastTimer?.Stop();

        if (_secretToastLabel != null)
            _secretToastLabel.Visible = false;
    }

    private void PositionSecretToast()
    {
        if (_secretToastLabel == null || _secretToastLabel.Parent is not Control host)
            return;

        _secretToastLabel.Left = Math.Max(0, (host.ClientSize.Width - _secretToastLabel.Width) / 2);
        _secretToastLabel.Top = Math.Max(0, host.ClientSize.Height - _secretToastLabel.Height - 96);
    }

    /// <summary>
    /// Listens for a secret code typed anywhere on the main window. The buffer keeps
    /// the last N characters (N = longest configured code) so codes work without a
    /// dedicated input field and even if extra characters were typed first.
    /// </summary>
    private void MainForm_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_config == null || _config.SecretBuilds.Count == 0)
            return;

        // Enter, Backspace and Escape reset the attempt.
        if (e.KeyChar is '\r' or '\n' or '\b' or '\u001b')
        {
            _codeBuffer = string.Empty;
            return;
        }

        if (!char.IsLetterOrDigit(e.KeyChar))
            return;

        _codeBuffer += char.ToUpperInvariant(e.KeyChar);

        int maxLength = _config.SecretBuilds.Max(secret => secret.Code.Length);
        if (maxLength <= 0)
        {
            _codeBuffer = string.Empty;
            return;
        }

        if (_codeBuffer.Length > maxLength)
            _codeBuffer = _codeBuffer[^maxLength..];

        foreach (var secret in _config.SecretBuilds)
        {
            if (!_codeBuffer.Equals(secret.Code, StringComparison.OrdinalIgnoreCase))
                continue;

            _codeBuffer = string.Empty;
            ActivateSecretBuild(secret.Build);
            break;
        }
    }

    /// <summary>
    /// Adds the secret build to the dropdown (if it is not there yet), selects it and
    /// persists the choice through the normal autosave path.
    /// </summary>
    private void ActivateSecretBuild(BuildInfo secretBuild)
    {
        if (_config == null || _busy)
            return;

        if (!_config.Builds.Any(build =>
                string.Equals(build.Id, secretBuild.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _config.Builds.Add(secretBuild);
        }

        _statuses = _statusService.RefreshStatuses(_config.Builds);

        var items = GetVisibleBuilds()
            .Select(build =>
            {
                BuildStatus status = _statuses.TryGetValue(build.Id, out BuildStatus value)
                    ? value
                    : BuildStatus.NotInstalled;

                return new BuildListItem
                {
                    BuildId = build.Id,
                    Status = status,
                    Build = build
                };
            })
            .ToList();

        _suppressBuildEvents = true;
        _buildsBox.SetItems(items, secretBuild.Id, item => (item as BuildListItem)?.BuildId);

        if (_buildsBox.SelectedItem is BuildListItem selected)
        {
            _selectedBuild = selected.Build;
            _selectedStatus = selected.Status;
            _settings.SelectedBuildId = selected.BuildId;
            _session.Save();
        }

        _suppressBuildEvents = false;

        UpdatePlayButtonForSelectedBuild();
        SetStatus($"Секретная сборка «{secretBuild.Name}» активирована", Theme.Success);
    }

    /// <summary>
    /// Applies the selected build status to the main action button:
    /// ИГРАТЬ (accent) / ОБНОВИТЬ (amber) / УСТАНОВИТЬ (green).
    /// </summary>
    private void UpdatePlayButtonForSelectedBuild()
    {
        switch (_selectedStatus)
        {
            case BuildStatus.UpToDate:
                _playButton.Text = "ИГРАТЬ";
                _playButton.NormalBackColor = Theme.Accent;
                _playButton.HoverBackColor = Theme.AccentHover;
                _playButton.PressedBackColor = Theme.AccentPressed;
                break;

            case BuildStatus.UpdateAvailable:
                _playButton.Text = "ОБНОВИТЬ";
                _playButton.NormalBackColor = Color.FromArgb(212, 160, 23);   // #D4A017
                _playButton.HoverBackColor = Color.FromArgb(228, 178, 45);
                _playButton.PressedBackColor = Color.FromArgb(180, 134, 12);
                break;

            default:
                _playButton.Text = "УСТАНОВИТЬ";
                _playButton.NormalBackColor = Color.FromArgb(46, 125, 50);    // #2E7D32
                _playButton.HoverBackColor = Color.FromArgb(58, 150, 62);
                _playButton.PressedBackColor = Color.FromArgb(36, 99, 40);
                break;
        }

        _playButton.Invalidate();
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _nickBox.Enabled = enabled;
        // Game actions stay locked while the user is not authorized.
        _buildsBox.Enabled = enabled && _authorized;
        _settingsButton.Enabled = enabled;
        _playButton.Enabled =
            enabled &&
            !_busy &&
            _gameProcess == null &&
            _authorized &&
            _authStatusGreen &&
            _nicknameValid;
    }

    /// <summary>
    /// Opens the official website in the system default browser (never in an
    /// embedded WebView). Repeated clicks are ignored for one second.
    /// </summary>
    private void WebsiteButton_Click(object? sender, EventArgs e)
    {
        if ((DateTime.Now - _lastWebsiteClick).TotalMilliseconds < 1000)
            return;

        _lastWebsiteClick = DateTime.Now;

        string url = _config?.WebsiteUrl ?? DefaultWebsiteUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = DefaultWebsiteUrl;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Не удалось открыть ссылку. Убедитесь, что в системе назначен браузер по умолчанию.",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async void PlayButton_Click(object? sender, EventArgs e) =>
        await HandlePrimaryActionAsync();

    private async Task HandlePrimaryActionAsync()
    {
        if (!_authorized)
        {
            SetStatus("Сначала авторизуйтесь через Telegram или Discord", Theme.Warning);
            return;
        }

        if (!_authStatusGreen || !_nicknameValid)
        {
            SetStatus(
                "Войдите под привязанным никнеймом, чтобы играть на сервере",
                Theme.Danger);
            _nickBox.InnerTextBox.Focus();
            return;
        }

        if (_selectedStatus == BuildStatus.UpToDate)
            await StartGameAsync();
        else
            await InstallOrUpdateAsync();
    }

    /// <summary>
    /// Downloads/updates the selected build, then refreshes statuses and restores the
    /// main button to its status-aware state.
    /// </summary>
    private async Task InstallOrUpdateAsync()
    {
        if (_busy || _gameProcess != null)
            return;

        if (_config == null || _config.Builds.Count == 0)
        {
            SetStatus("Список сборок не загружен", Theme.Danger);
            return;
        }

        _busy = true;
        SetControlsEnabled(false);
        _playButton.Text = "ОБНОВЛЕНИЕ...";
        _progressBar.Value = 0;
        SetStatus("Подготовка к обновлению...", Theme.Muted);

        var progress = new Progress<LaunchProgress>(update =>
        {
            if (IsDisposed)
                return;

            _progressBar.Value = update.Percent;
            SetStatus(update.Status, Theme.Muted);
        });

        try
        {
            await _launcherService.UpdateBuildAsync(
                _config,
                _selectedBuild,
                _settings,
                progress,
                _lifetimeCts.Token);

            if (IsDisposed)
                return;

            // Re-check statuses and rebuild the dropdown/button state.
            PopulateBuilds();
            _progressBar.Value = 100;
            SetStatus("Сборка готова к запуску", Theme.Success);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // The launcher is closing; no error dialog is needed.
        }
        catch (Exception ex)
        {
            AppLog.Error("Build update", ex);

            if (!IsDisposed)
            {
                SetStatus("Ошибка обновления", Theme.Danger);
                ShowError(
                    "Не удалось обновить сборку",
                    BuildUserMessage(ex),
                    BuildFullDetails(ex),
                    null);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _busy = false;
                SetControlsEnabled(true);
                UpdatePlayButtonForSelectedBuild();
            }
        }
    }

    private async void NickBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (!_busy && _gameProcess == null)
            await HandlePrimaryActionAsync();
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
            LaunchResult? result = await _launcherService.LaunchAsync(
                _config,
                _selectedBuild,
                _settings,
                progress,
                _lifetimeCts.Token);

            if (result is null)
            {
                // Low-RAM confirmation was declined by the user.
                if (!IsDisposed)
                    SetStatus("Запуск отменён", Theme.Muted);
                return;
            }

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
                UpdatePlayButtonForSelectedBuild();
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
        UpdatePlayEnabled();
        UpdatePlayButtonForSelectedBuild();
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

            // The user logged out from the settings window: revert the main
            // window to the unauthorized state (game actions locked).
            if (form.LogoutRequested && !IsDisposed)
            {
                ApplyAuthState(false, clearNickname: true);
                SetStatus("Вы вышли из аккаунта", Theme.Muted);
            }
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
            SessionManager.AuthChanged -= SessionManager_AuthChanged;
            _session.Dispose();
            _lifetimeCts.Dispose();
            _toolTip.Dispose();
            _profileMenu?.Dispose();
            _secretToastTimer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
