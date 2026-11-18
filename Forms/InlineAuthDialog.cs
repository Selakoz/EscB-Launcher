using System.Text.RegularExpressions;
using EscapeBook.Controls;
using EscapeBook.Services;

namespace EscapeBook.Forms;

/// <summary>Which channel delivers the OTP code.</summary>
internal enum AuthChannel
{
    Telegram,
    Discord
}

/// <summary>
/// Compact inline OTP authorization dialog (Telegram flow):
/// nickname -> "Получить код" -> 6-digit code -> "Войти".
/// "Открыть в Telegram" lets the user press /start in the bot proactively.
/// On success saves auth_session.json and closes with DialogResult.OK.
/// The session token is never displayed or logged.
/// </summary>
internal sealed class InlineAuthDialog : BaseDarkForm
{
    private static readonly Regex NicknameRegex =
        new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private static readonly Regex OtpRegex =
        new("^[0-9]{6}$", RegexOptions.Compiled);

    private readonly RoundedTextBox _nickBox;
    private readonly RoundedTextBox _otpBox;
    private readonly RoundedButton _getCodeButton;
    private readonly RoundedButton _openBotButton;
    private readonly RoundedButton _loginButton;
    private readonly Label _sentLabel;
    private readonly Label _statusLabel;

    private string _hwid = string.Empty;
    private string _botUsername = string.Empty;
    private bool _busy;
    private readonly AuthChannel _channel;

    // Cooldown for "Получить код" (60 s normally, 10 min after rate_limited,
    // 10 s after tg_not_started). Static so the countdown survives reopening.
    private static DateTime? CooldownUntilUtc;
    private DateTime _cooldownEndsAt;
    private System.Windows.Forms.Timer? _cooldownTimer;

    /// <summary>Nickname of the successfully authorized account.</summary>
    public string McNickname { get; private set; } = string.Empty;

    public InlineAuthDialog(string initialNickname, AuthChannel channel = AuthChannel.Telegram)
        : base(new Size(380, 422))
    {
        _channel = channel;
        TitleText = "Авторизация";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);

        // Standard OS frame like the main window (no custom title bar).
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        CollapseTitleBar();

        // ---- Card (final height is computed from the measured content) ----
        var card = new RoundedPanel
        {
            Location = new Point(16, 16),
            Size = new Size(336, 390),
            CornerRadius = 16,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            BorderSize = 1
        };
        ContentArea.Controls.Add(card);

        const int contentX = 24;
        const int contentWidth = 288;

        UiFactory.Label(
            card,
            _channel == AuthChannel.Telegram ? "Вход через Telegram" : "Вход через Discord",
            Theme.Font(13f, FontStyle.Bold),
            Theme.Text,
            Theme.Surface,
            new Rectangle(18, 18, 300, 24),
            ContentAlignment.MiddleCenter);

        UiFactory.Label(
            card,
            "Введите ваш никнейм в Minecraft",
            Theme.Font(9f),
            Theme.Muted,
            Theme.Surface,
            new Rectangle(18, 44, 300, 16),
            ContentAlignment.MiddleCenter);

        _nickBox = new RoundedTextBox
        {
            PlaceholderText = "Никнейм",
            Location = new Point(contentX, 66),
            Size = new Size(contentWidth, 40),
            CornerRadius = 10,
            Text = initialNickname ?? string.Empty,
            Font = new Font("Segoe UI", 10f)
        };
        card.Controls.Add(_nickBox);

        // ---- Hint: wraps at the content width; everything below is laid out
        // from its measured height, so the text can never overlap the buttons.
        var hintLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(contentWidth, 0),
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            Font = Theme.Font(8f),
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
            Text = _channel == AuthChannel.Telegram
                ? "Перед первым входом откройте бота @escapebook_auth_bot и нажмите «Запустить» (/start)."
                : "Код придёт в личные сообщения Discord от бота EscapeBook. " +
                  "Убедитесь, что в настройках конфиденциальности разрешены ЛС от участников сервера.",
            Location = new Point(contentX, 112)
        };
        card.Controls.Add(hintLabel);

        // 10 px of guaranteed padding between the hint and the button row.
        int buttonsY = hintLabel.Bottom + 10;

        _getCodeButton = new RoundedButton
        {
            Text = "Получить код",
            Location = new Point(contentX, buttonsY),
            Size = new Size(128, 38),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
        };
        _getCodeButton.Click += async (_, _) => await GetCodeAsync();
        card.Controls.Add(_getCodeButton);

        _openBotButton = new RoundedButton
        {
            Text = _channel == AuthChannel.Telegram ? "Открыть в Telegram" : "Открыть Discord",
            Location = new Point(160, buttonsY),
            Size = new Size(152, 38),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            CornerRadius = 10,
            NormalBackColor = Theme.SurfaceHover,
            HoverBackColor = Color.FromArgb(38, 43, 56),
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text
        };
        _openBotButton.Click += OpenBot_Click;
        card.Controls.Add(_openBotButton);

        int sentY = buttonsY + 38 + 8;
        _sentLabel = UiFactory.Label(
            card,
            "Код отправлен в Telegram",
            Theme.Font(9f, FontStyle.Bold),
            Theme.Success,
            Theme.Surface,
            new Rectangle(18, sentY, 300, 18),
            ContentAlignment.MiddleCenter);
        _sentLabel.Visible = false;

        int otpY = sentY + 18 + 4;
        _otpBox = new RoundedTextBox
        {
            PlaceholderText = "6-значный код",
            Location = new Point(contentX, otpY),
            Size = new Size(contentWidth, 40),
            CornerRadius = 10,
            Font = new Font("Segoe UI", 10f)
        };
        _otpBox.InnerTextBox.MaxLength = 6;
        _otpBox.InnerTextBox.KeyPress += OtpBox_KeyPress;
        _otpBox.InnerTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await LoginAsync();
            }
        };
        card.Controls.Add(_otpBox);

        int loginY = otpY + 40 + 8;
        _loginButton = new RoundedButton
        {
            Text = "Войти",
            Location = new Point(contentX, loginY),
            Size = new Size(contentWidth, 42),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            CornerRadius = 10,
            ForeColor = Theme.Text
        };
        _loginButton.Click += async (_, _) => await LoginAsync();
        card.Controls.Add(_loginButton);

        const int statusHeight = 66;
        int statusY = loginY + 42 + 8;
        _statusLabel = UiFactory.Label(
            card,
            string.Empty,
            Theme.Font(9f),
            Theme.Muted,
            Theme.Surface,
            new Rectangle(contentX, statusY, contentWidth, statusHeight),
            ContentAlignment.TopCenter);
        _statusLabel.AutoEllipsis = false;
        SetStatus("1. Введите никнейм.\n2. Нажмите «Получить код».\n3. Введите код из Telegram.", Theme.Muted);

        // Size the card/form exactly to the content (no empty bottom space).
        int cardHeight = statusY + statusHeight + 12;
        card.Size = new Size(336, cardHeight);
        ClientSize = new Size(380, cardHeight + 32);

        ActiveControl = _nickBox.InnerTextBox;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Warm up the HWID off the UI thread (WMI queries can be slow).
        _ = WarmUpHwidAsync();

        // Restore an active cooldown if the dialog was reopened.
        if (CooldownUntilUtc is DateTime until && until > DateTime.UtcNow)
            StartCooldown((int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds), updateStatic: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cooldownTimer?.Dispose();

        base.Dispose(disposing);
    }

    private async Task WarmUpHwidAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_hwid))
                _hwid = await Task.Run(AuthService.GetHWID);
        }
        catch (Exception ex)
        {
            AppLog.Error("HWID warm-up", ex);
        }
    }

    private async Task<string> GetHwidAsync()
    {
        if (string.IsNullOrEmpty(_hwid))
            _hwid = await Task.Run(AuthService.GetHWID);
        return _hwid;
    }

    // Digits only in the OTP field.
    private void OtpBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            e.Handled = true;
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _nickBox.Enabled = !busy;
        _otpBox.Enabled = !busy;
        // The code button also stays locked while a cooldown is running.
        _getCodeButton.Enabled = !busy && !IsCoolingDown;
        _loginButton.Enabled = !busy;
    }

    // ------------------------------------------------------------- cooldown

    private bool IsCoolingDown => DateTime.UtcNow < _cooldownEndsAt;

    /// <summary>
    /// Locks "Получить код" for the given number of seconds and shows the
    /// remaining time in the button text ("Получить код (59)").
    /// </summary>
    private void StartCooldown(int seconds, bool updateStatic = true)
    {
        _cooldownEndsAt = DateTime.UtcNow.AddSeconds(Math.Max(1, seconds));
        if (updateStatic)
            CooldownUntilUtc = _cooldownEndsAt;

        if (_cooldownTimer == null)
        {
            _cooldownTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _cooldownTimer.Tick += (_, _) => UpdateCooldownState();
        }

        _cooldownTimer.Start();
        UpdateCooldownState();
    }

    private void UpdateCooldownState()
    {
        TimeSpan remaining = _cooldownEndsAt - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            _cooldownTimer?.Stop();
            CooldownUntilUtc = null;
            _getCodeButton.Text = "Получить код";
            _getCodeButton.Enabled = !_busy;
            return;
        }

        _getCodeButton.Text = $"Получить код ({Math.Ceiling(remaining.TotalSeconds):0})";
        _getCodeButton.Enabled = false;
    }

    /// <summary>
    /// Telegram mode opens https://t.me/&lt;bot&gt;?start=1; Discord mode
    /// launches the Discord app (discord://) so the user can check DMs.
    /// </summary>
    private void OpenBot_Click(object? sender, EventArgs e)
    {
        bool opened = _channel == AuthChannel.Telegram
            ? AuthLinks.TryOpenBotInTelegram(_botUsername)
            : AuthLinks.TryOpenDiscord();

        if (!opened)
        {
            SetStatus(
                $"Не удалось открыть приложение. Напишите {AuthLinks.AdminContact} в Telegram или Discord.",
                Theme.Muted);
            return;
        }

        SetStatus(
            _channel == AuthChannel.Telegram
                ? "Откройте Telegram, нажмите «Запустить» и запросите код снова."
                : "Откройте Discord: код придёт в личные сообщения от бота EscapeBook.",
            Theme.Success);
    }

    private async Task GetCodeAsync()
    {
        if (_busy || IsCoolingDown)
            return;

        string nickname = _nickBox.Text.Trim();
        if (!NicknameRegex.IsMatch(nickname))
        {
            SetStatus("Никнейм: 3–16 символов (A-Z, a-z, 0-9, _).", Theme.Danger);
            return;
        }

        SetBusy(true);
        SetStatus("Запрашиваем код...", Theme.Muted);

        // Anti-spam: 60 s cooldown starts as soon as the request is sent.
        StartCooldown(60);

        try
        {
            string hwid = await GetHwidAsync();
            AuthResult result = _channel == AuthChannel.Telegram
                ? await AuthService.RequestOTPAsync(nickname, hwid)
                : await AuthService.RequestOtpDiscordAsync(nickname, hwid);

            if (IsDisposed)
                return;

            if (result.Success)
            {
                _sentLabel.Visible = true;
                SetStatus(
                    _channel == AuthChannel.Telegram
                        ? "Код отправлен в Telegram.\nВведите 6-значный код ниже."
                        : "Код отправлен в Discord.\nВведите 6-значный код ниже.",
                    Theme.Success);
                _otpBox.Text = string.Empty;
                _otpBox.InnerTextBox.Focus();
            }
            else
            {
                _sentLabel.Visible = false;
                SetStatus(AuthService.DescribeError(result.ErrorReason), Theme.Danger);

                switch (result.ErrorReason)
                {
                    case "rate_limited":
                        // Server-side limit hit: lock the button for 10 minutes.
                        StartCooldown(600);
                        break;

                    case "tg_not_started" when _channel == AuthChannel.Telegram:
                        // Not the user's fault: keep the server username for the
                        // "Открыть в Telegram" button and allow a quick retry.
                        if (!string.IsNullOrWhiteSpace(result.BotUsername))
                            _botUsername = result.BotUsername;
                        StartCooldown(10);
                        break;

                    // ds_dm_blocked / ds_delivery_failed keep the normal 60 s
                    // cooldown started on click — no 10-minute lockout.
                }
            }
        }
        finally
        {
            if (!IsDisposed)
                SetBusy(false);
        }
    }

    private async Task LoginAsync()
    {
        if (_busy)
            return;

        string nickname = _nickBox.Text.Trim();
        if (!NicknameRegex.IsMatch(nickname))
        {
            SetStatus("Никнейм: 3–16 символов (A-Z, a-z, 0-9, _).", Theme.Danger);
            return;
        }

        string code = _otpBox.Text.Trim();
        if (!OtpRegex.IsMatch(code))
        {
            SetStatus("Введите 6-значный код из Telegram.", Theme.Danger);
            return;
        }

        SetBusy(true);
        SetStatus("Проверяем код...", Theme.Muted);

        try
        {
            string hwid = await GetHwidAsync();

            // The server auto-trusts this HWID and deletes the OTP on success —
            // no separate device registration call is required.
            AuthResult result = await AuthService.ConfirmOTPAsync(nickname, code, hwid);

            if (IsDisposed)
                return;

            if (!result.Success)
            {
                SetStatus(AuthService.DescribeError(result.ErrorReason), Theme.Danger);
                return;
            }

            SessionManager.SaveSession(nickname, result.SessionToken, hwid);

            McNickname = nickname;
            SetStatus("Вход выполнен.", Theme.Success);
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            if (!IsDisposed)
                SetBusy(false);
        }
    }
}
