using System.Drawing;
using System.Windows.Forms;
using EscapeBook.Controls;
using EscapeBook.Models;
using EscapeBook.Services;

namespace EscapeBook.Forms;

internal sealed class SettingsForm : BaseDarkForm
{
    private static readonly Font SectionFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font InputFont = new("Segoe UI", 10f);
    private static readonly Font NoteFont = new("Segoe UI", 9f);
    private static readonly Font ButtonFont = new("Segoe UI", 10f, FontStyle.Bold);

    private readonly LauncherConfig _config;
    private readonly SettingsSession _session;
    private readonly UserSettings _settings;
    private readonly string _buildId;
    private readonly bool _isFirstLaunch;

    private DarkTextBox _argsBox = null!;
    private DarkTextBox _pathBox = null!;
    private ModernSlider _ramSlider = null!;
    private Label _ramValueLabel = null!;
    private Label _ramWarningLabel = null!;
    private Label _errorLabel = null!;
    private Panel _modsPanel = null!;

    private readonly List<ModToggleCard> _modCards = new();
    private int _maxRamMb = 8192;
    private bool _suppressEvents;

    /// <summary>Links a rendered card to its config mod and, for radio groups, its option.</summary>
    private sealed record ModCardContext(OptionalModInfo Mod, ModOption? Option);

    /// <summary>
    /// True when the user pressed "Выйти из аккаунта": the session file has
    /// been deleted and MainForm must revert to the unauthorized state.
    /// </summary>
    public bool LogoutRequested { get; private set; }

    public SettingsForm(LauncherConfig config, SettingsSession session, bool isFirstLaunch)
        : base(new Size(540, 460))
    {
        TitleText = "Настройки";
        ShowInTaskbar = false;
        CloseOnEscape = true;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);

        // Custom borderless window: a 1px dark theme frame drawn via Padding/BackColor,
        // so there is no white OS border anywhere.
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Border;
        Padding = new Padding(1);

        _config = config;
        _session = session;
        _settings = session.Settings;
        _isFirstLaunch = isFirstLaunch;
        _buildId = string.IsNullOrWhiteSpace(_settings.SelectedBuildId)
            ? (config.Builds.Count > 0 ? config.Builds[0].Id : string.Empty)
            : _settings.SelectedBuildId;

        BuildLayout();
        WireAutoSave();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        ContentArea.SuspendLayout();

        var tabControl = new DarkTabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var tabLaunch = new TabPage("Запуск и память")
        {
            BackColor = Theme.Background,
            UseVisualStyleBackColor = false,
            Margin = Padding.Empty,
            Padding = new Padding(16)
        };

        var tabMods = new TabPage("Оптимизация и визуал")
        {
            BackColor = Theme.Background,
            UseVisualStyleBackColor = false,
            Margin = Padding.Empty,
            Padding = new Padding(16)
        };

        tabControl.TabPages.Add(tabLaunch);
        tabControl.TabPages.Add(tabMods);

        BuildLaunchTab(tabLaunch);
        BuildModsTab(tabMods);

        // ---- Bottom action buttons (anchored bottom-right) ----
        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = new Padding(16, 8, 16, 12)
        };

        // Settings are auto-saved on every change, so a single Close button is enough.
        var closeButton = new RoundedButton
        {
            Text = "Закрыть",
            Width = 124,
            Height = 38,
            Font = ButtonFont,
            CornerRadius = 10,
            NormalBackColor = Color.FromArgb(30, 30, 46), // #1E1E2E
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,                   // #383850
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            // Same Y as the logout button: both sit on one horizontal line.
            Location = new Point(0, 8)
        };
        closeButton.Click += (_, _) =>
        {
            // Flush any pending debounced changes before closing.
            _session.Save();
            Close();
        };
        buttonBar.Controls.Add(closeButton);

        // Logout: deletes auth_session.json and reopens the OTP authorization form.
        var logoutButton = new RoundedButton
        {
            Text = "Выйти из аккаунта",
            Width = 170,
            Height = 38,
            Font = ButtonFont,
            CornerRadius = 10,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Color.FromArgb(58, 32, 40),
            PressedBackColor = Color.FromArgb(44, 26, 32),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Danger,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Location = new Point(16, 8)
        };
        logoutButton.Click += (_, _) => HandleLogout();
        buttonBar.Controls.Add(logoutButton);

        ContentArea.Controls.Add(tabControl);
        ContentArea.Controls.Add(buttonBar);

        ContentArea.ResumeLayout();

        // Keep the Close button pinned to the bottom-right corner.
        buttonBar.Resize += (_, _) =>
        {
            closeButton.Left = Math.Max(0, buttonBar.ClientSize.Width - 16 - closeButton.Width);
        };
        closeButton.Left = Math.Max(0, buttonBar.ClientSize.Width - 16 - closeButton.Width);
    }

    private void BuildLaunchTab(TabPage page)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Font = InputFont
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddRow(Control c, RowStyle style, int marginBottom = 6)
        {
            layout.RowStyles.Add(style);
            layout.SetRow(c, row);
            layout.SetColumn(c, 0);
            layout.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            c.Dock = DockStyle.Top;
            row++;
        }

        // ---- RAM section ----
        var ramHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var ramLabel = UiFactory.Label(
            ramHeader,
            "ОПЕРАТИВНАЯ ПАМЯТЬ",
            SectionFont,
            Theme.Muted,
            Theme.Background);
        ramLabel.AutoSize = false;
        ramLabel.SetBounds(0, 2, 230, 18);
        ramLabel.TextAlign = ContentAlignment.MiddleLeft;

        _ramValueLabel = UiFactory.Label(
            ramHeader,
            "4096 МБ (4 ГБ)",
            new Font("Segoe UI", 10f, FontStyle.Bold),
            Theme.Accent,
            Theme.Background);
        _ramValueLabel.AutoSize = false;
        _ramValueLabel.SetBounds(240, 2, 250, 18);
        _ramValueLabel.TextAlign = ContentAlignment.MiddleRight;

        ramHeader.Resize += (_, _) =>
        {
            _ramValueLabel.Left = Math.Max(0, ramHeader.Width - _ramValueLabel.Width - 4);
        };

        AddRow(ramHeader, new RowStyle(SizeType.Absolute, 22f), 4);

        _ramSlider = new ModernSlider
        {
            Minimum = 1024,
            Maximum = 8192,
            Step = 512,
            Value = 4096,
            Height = 30,
            Font = InputFont
        };
        AddRow(_ramSlider, new RowStyle(SizeType.Absolute, 32f), 4);

        _ramWarningLabel = UiFactory.Label(
            layout,
            "Оптимальный объем памяти.",
            NoteFont,
            Theme.Success,
            Theme.Background);
        _ramWarningLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _ramWarningLabel.AutoSize = true;
        AddRow(_ramWarningLabel, new RowStyle(SizeType.AutoSize), 10);

        // ---- JVM arguments ----
        var jvmLabel = UiFactory.Label(
            layout,
            "JVM-АРГУМЕНТЫ ЗАПУСКА",
            SectionFont,
            Theme.Muted,
            Theme.Background);
        jvmLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        jvmLabel.AutoSize = true;
        AddRow(jvmLabel, new RowStyle(SizeType.AutoSize), 4);

        _argsBox = new DarkTextBox
        {
            Multiline = true,
            Height = 90,
            Text = _settings.CustomJvmArgs,
            Font = InputFont
        };
        AddRow(_argsBox, new RowStyle(SizeType.Percent, 100f), 4);

        var ramNote = UiFactory.Label(
            layout,
            "Флаги -Xmx и -Xms из этого поля игнорируются: память задаётся ползунком выше.",
            NoteFont,
            Theme.Muted,
            Theme.Background);
        ramNote.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        ramNote.AutoSize = true;
        AddRow(ramNote, new RowStyle(SizeType.AutoSize), 8);

        var resetArgs = new RoundedButton
        {
            Text = "Сбросить аргументы по умолчанию",
            Width = 250,
            Height = 34,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
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
            _settings.AppliedJvmPresetVersion = _config.JvmPresetVersion;
            _session.Save();
            _errorLabel.Visible = false;
        };
        AddRow(resetArgs, new RowStyle(SizeType.Absolute, 34f), 10);

        // ---- Java path ----
        var javaLabel = UiFactory.Label(
            layout,
            "ПУТЬ К javaw.exe",
            SectionFont,
            Theme.Muted,
            Theme.Background);
        javaLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        javaLabel.AutoSize = true;
        AddRow(javaLabel, new RowStyle(SizeType.AutoSize), 4);

        var javaRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100f),
                new ColumnStyle(SizeType.AutoSize)
            },
            RowStyles = { new RowStyle(SizeType.Absolute, 34f) },
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _pathBox = new DarkTextBox
        {
            PlaceholderText = "Автоопределение",
            Text = _settings.JavaPath,
            Height = 34,
            Font = InputFont
        };
        javaRow.Controls.Add(_pathBox, 0, 0);

        var browseButton = new RoundedButton
        {
            Text = "Обзор...",
            Width = 96,
            Height = 34,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 8,
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

        AddRow(javaRow, new RowStyle(SizeType.Absolute, 34f), 0);

        // ---- Error label (hidden until needed) ----
        _errorLabel = UiFactory.Label(
            layout,
            string.Empty,
            NoteFont,
            Theme.Danger,
            Theme.Background);
        _errorLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _errorLabel.Visible = false;
        AddRow(_errorLabel, new RowStyle(SizeType.Absolute, 18f), 0);

        page.Controls.Add(layout);
    }

    private void BuildModsTab(TabPage page)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Font = InputFont
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        int row = 0;
        void AddRow(Control c, RowStyle style, int marginBottom = 6)
        {
            layout.RowStyles.Add(style);
            layout.SetRow(c, row);
            layout.SetColumn(c, 0);
            layout.Controls.Add(c);
            c.Margin = new Padding(0, 0, 0, marginBottom);
            c.Dock = DockStyle.Top;
            row++;
        }

        var header = UiFactory.Label(
            layout,
            "ОПТИМИЗАЦИЯ И ВИЗУАЛ",
            SectionFont,
            Theme.Accent,
            Theme.Background);
        header.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        // AutoSize lets WinForms measure the font metrics so descenders are not clipped.
        header.AutoSize = true;
        AddRow(header, new RowStyle(SizeType.AutoSize), 6);

        var resetMods = new RoundedButton
        {
            Text = "Сбросить по умолчанию",
            Width = 180,
            Height = 34,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            CornerRadius = 8,
            NormalBackColor = Theme.Surface,
            HoverBackColor = Theme.SurfaceHover,
            PressedBackColor = Color.FromArgb(19, 22, 29),
            BorderColor = Theme.Border,
            BorderSize = 1,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        resetMods.Click += (_, _) => ResetModsToDefaults();
        AddRow(resetMods, new RowStyle(SizeType.Absolute, 38f), 12);

        _modsPanel = new Panel
        {
            BackColor = Theme.Background,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 8, 8)
        };

        BuildModCards();

        AddRow(_modsPanel, new RowStyle(SizeType.Percent, 100f), 0);

        // AddRow docks children to the top; the scrollable list must fill the remaining
        // vertical space of the tab instead, otherwise it is cut off.
        _modsPanel.Dock = DockStyle.Fill;

        page.Controls.Add(layout);
    }

    private void BuildModCards()
    {
        _modsPanel.Controls.Clear();
        _modCards.Clear();

        var mods = GetOptionalMods();

        // Resolve conflicting choices before rendering so the UI and the files on
        // disk always show exactly one active option per radio group.
        ApplyGroupExclusivity(mods);
        ApplyRadioSelections(mods);

        // Dock.Top places the last added control at the very top, so add in reverse
        // to keep the visual order identical to the config order.
        foreach (var mod in Enumerable.Reverse(mods))
        {
            if (mod.IsRadio)
            {
                // One radio card per option; add in reverse so options keep config order.
                foreach (var option in Enumerable.Reverse(mod.Options))
                {
                    var optionCard = new ModToggleCard
                    {
                        ModName = option.Name,
                        Description = mod.Description,
                        Tag = new ModCardContext(mod, option),
                        IsRadio = true
                    };

                    optionCard.IsOn = ResolveRadioOptionState(mod, option);
                    optionCard.ToggleRequested += (_, _) =>
                        OnRadioOptionToggled(mod, option, optionCard);

                    _modsPanel.Controls.Add(optionCard);
                    _modCards.Add(optionCard);
                }

                continue;
            }

            var card = new ModToggleCard
            {
                ModName = mod.Name,
                Description = mod.Description,
                Tag = new ModCardContext(mod, null),
                IsRadio = IsRadioGroup(mod, mods)
            };

            card.IsOn = ResolveModState(mod);
            card.ToggleRequested += (_, _) => OnModToggled(mod, card);

            _modsPanel.Controls.Add(card);
            _modCards.Add(card);
        }

        if (mods.Count == 0)
        {
            var empty = UiFactory.Label(
                _modsPanel,
                "Дополнительные моды не настроены на сервере.",
                NoteFont,
                Theme.Muted,
                Theme.Background);
            empty.AutoSize = false;
            empty.Location = new Point(0, 0);
        }
    }

    /// <summary>
    /// A mod is rendered as a radio button only when its group actually contains at
    /// least two mods; a lone grouped mod behaves like a normal toggle.
    /// </summary>
    private static bool IsRadioGroup(OptionalModInfo mod, IReadOnlyList<OptionalModInfo> mods)
    {
        if (mod.IsRadio)
            return false;

        if (string.IsNullOrWhiteSpace(mod.Group))
            return false;

        return mods.Count(other =>
                   string.Equals(other.Group, mod.Group, StringComparison.OrdinalIgnoreCase)) > 1;
    }

    /// <summary>
    /// Keeps exactly one enabled mod per group. The first enabled member in config
    /// order wins; all other members are disabled both in settings and on disk.
    /// When no member is enabled yet, the group stays empty until the player chooses.
    /// </summary>
    private void ApplyGroupExclusivity(IReadOnlyList<OptionalModInfo> mods)
    {
        var groups = mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Group))
            .GroupBy(mod => mod.Group, StringComparer.OrdinalIgnoreCase);

        bool changed = false;

        foreach (var group in groups)
        {
            var members = group
                .Where(mod => !mod.IsRadio)
                .ToList();

            if (members.Count < 2)
                continue;

            var states = members
                .Select(mod => (Mod: mod, Enabled: ResolveModState(mod)))
                .ToList();

            OptionalModInfo? selected = states
                .Where(state => state.Enabled)
                .Select(state => state.Mod)
                .FirstOrDefault();

            foreach (var state in states)
            {
                bool desired = selected != null && ReferenceEquals(state.Mod, selected);
                if (state.Enabled == desired)
                    continue;

                _settings.SetModEnabled(state.Mod.Name, desired);

                try
                {
                    OptionalModsService.SetEnabled(state.Mod, _buildId, desired);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Optional mod group apply", ex);
                }

                changed = true;
            }
        }

        if (changed)
            _session.Save();
    }

    /// <summary>
    /// Makes sure radio mods have exactly one active option, applying the resolved
    /// choice to disk (other options get the <c>.disabled</c> suffix).
    /// </summary>
    private void ApplyRadioSelections(IReadOnlyList<OptionalModInfo> mods)
    {
        bool changed = false;

        foreach (var mod in mods.Where(mod => mod.IsRadio))
        {
            string? selected = OptionalModsService.ResolveSelection(
                mod,
                _settings.GetModOption(mod.Name),
                _buildId);

            if (selected is null)
                continue;

            if (!string.Equals(_settings.GetModOption(mod.Name), selected, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SetModOption(mod.Name, selected);
                changed = true;
            }

            try
            {
                OptionalModsService.ApplyRadioSelection(mod, selected, _buildId);
            }
            catch (Exception ex)
            {
                AppLog.Error("Optional radio mod apply", ex);
            }
        }

        if (changed)
            _session.Save();
    }

    /// <summary>
    /// Mods from config.json; when the config is unreachable a small built-in set is used
    /// so the UI stays testable.
    /// </summary>
    private List<OptionalModInfo> GetOptionalMods()
    {
        if (_config.OptionalMods.Count > 0)
            return _config.OptionalMods;

        return new List<OptionalModInfo>
        {
            new()
            {
                Name = "Iris Shaders",
                Description = "Шейдеры и улучшенная графика. Требуются банки iris-/oculus- в mods.",
                FilePrefixes = new List<string> { "iris-", "oculus-" },
                EnabledByDefault = true
            },
            new()
            {
                Name = "Voxy (Дальняя прорисовка)",
                Description = "Увеличение дальности прорисовки. Требуются банки voxy- в mods.",
                FilePrefixes = new List<string> { "voxy-" },
                EnabledByDefault = true
            },
            new()
            {
                Name = "Sodium (оптимизация FPS)",
                Description = "Оптимизация рендера и FPS. Требуются банки sodium- в mods.",
                FilePrefixes = new List<string> { "sodium-" },
                EnabledByDefault = false
            }
        };
    }

    // ----------------------------------------------------------- auto-save

    private void WireAutoSave()
    {
        _ramSlider.ValueChanged += (_, _) =>
        {
            if (_suppressEvents)
                return;

            _settings.RamMb = _ramSlider.Value;
            UpdateRamReadout();
            UpdateRamWarning();
            _session.Save();
        };

        _argsBox.TextChanged += (_, _) =>
        {
            if (_suppressEvents)
                return;

            _settings.CustomJvmArgs = _argsBox.Text;
            _session.SaveSoon();
        };

        _pathBox.TextChanged += (_, _) =>
        {
            if (_suppressEvents)
                return;

            _settings.JavaPath = _pathBox.Text.Trim();
            _session.SaveSoon();
        };
    }

    private void OnModToggled(OptionalModInfo mod, ModToggleCard card)
    {
        bool enabled = card.IsOn;

        var mods = GetOptionalMods();
        bool isGrouped = IsRadioGroup(mod, mods);

        // Radio semantics: clicking the already selected member must not turn it off.
        if (isGrouped && !enabled)
        {
            card.IsOn = true;
            return;
        }

        if (isGrouped)
        {
            // Disable every other member of the same group first.
            foreach (var other in mods)
            {
                if (ReferenceEquals(other, mod) ||
                    !string.Equals(other.Group, mod.Group, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _settings.SetModEnabled(other.Name, false);
                ApplyModToDisk(other, false);

                var otherCard = _modCards.FirstOrDefault(cardItem =>
                    cardItem.Tag is ModCardContext context &&
                    context.Option is null &&
                    ReferenceEquals(context.Mod, other));
                if (otherCard != null)
                    otherCard.IsOn = false;
            }
        }

        // Persist the preference (used when the build is not downloaded yet).
        _settings.SetModEnabled(mod.Name, enabled);

        // Apply immediately to the active instance mods folder.
        ApplyModToDisk(mod, enabled);
        card.IsOn = enabled;

        _session.Save();
    }

    /// <summary>
    /// Radio group click: the clicked option becomes the only active one. Clicking
    /// the already active option keeps it selected (radio buttons cannot be cleared).
    /// </summary>
    private void OnRadioOptionToggled(
        OptionalModInfo mod,
        ModOption option,
        ModToggleCard card)
    {
        _settings.SetModOption(mod.Name, option.Name);

        try
        {
            OptionalModsService.ApplyRadioSelection(mod, option.Name, _buildId);
        }
        catch (Exception ex)
        {
            AppLog.Error("Optional radio mod apply", ex);
        }

        // Keep every card of this radio group in sync with the single selection.
        foreach (var groupCard in _modCards)
        {
            if (groupCard.Tag is not ModCardContext context ||
                context.Option is null ||
                !ReferenceEquals(context.Mod, mod))
            {
                continue;
            }

            groupCard.IsOn = string.Equals(
                context.Option.Name,
                option.Name,
                StringComparison.OrdinalIgnoreCase);
        }

        card.IsOn = true;
        _session.Save();
    }

    private void ApplyModToDisk(OptionalModInfo mod, bool enabled)
    {
        try
        {
            OptionalModsService.SetEnabled(mod, _buildId, enabled);
        }
        catch (Exception ex)
        {
            AppLog.Error("Optional mods apply", ex);
        }
    }

    private void ResetModsToDefaults()
    {
        try
        {
            var mods = GetOptionalMods();

            foreach (var mod in mods)
            {
                if (mod.IsRadio)
                {
                    string? defaultOption = OptionalModsService.GetSavedOrDefaultOption(mod, null);
                    if (defaultOption is null)
                        _settings.RemoveModState(mod.Name);
                    else
                        _settings.SetModOption(mod.Name, defaultOption);

                    OptionalModsService.ApplyRadioSelection(mod, defaultOption, _buildId);
                    continue;
                }

                bool desired = mod.EnabledByDefault;

                _settings.SetModEnabled(mod.Name, desired);
                OptionalModsService.SetEnabled(mod, _buildId, desired);
            }

            // If several mods in one group default to enabled, the first one wins.
            ApplyGroupExclusivity(mods);

            RefreshModCards();
            _session.Save();
            _errorLabel.Visible = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Reset mods to defaults", ex);
            ShowValidationError("Не удалось применить значения по умолчанию.");
        }
    }

    private void RefreshModCards()
    {
        foreach (var card in _modCards)
        {
            if (card.Tag is not ModCardContext context)
                continue;

            card.IsOn = context.Option is not null
                ? ResolveRadioOptionState(context.Mod, context.Option)
                : ResolveModState(context.Mod);
        }
    }

    /// <summary>
    /// Disk state wins when the files exist, otherwise the saved preference,
    /// otherwise the config default.
    /// </summary>
    private bool ResolveModState(OptionalModInfo mod)
    {
        var state = OptionalModsService.GetState(mod, _buildId);
        if (state.HasFiles)
            return state.Enabled;

        if (_settings.GetModEnabled(mod.Name) is bool saved)
            return saved;

        return mod.EnabledByDefault;
    }

    /// <summary>True when this option is the single active choice of its radio group.</summary>
    private bool ResolveRadioOptionState(OptionalModInfo mod, ModOption option)
    {
        string? selected = OptionalModsService.ResolveSelection(
            mod,
            _settings.GetModOption(mod.Name),
            _buildId);

        return selected is not null &&
               string.Equals(selected, option.Name, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- helpers

    private void UpdateRamReadout()
    {
        int ram = _ramSlider.Value;
        double gigabytes = ram / 1024d;
        _ramValueLabel.Text = $"{ram:N0} МБ ({gigabytes:0.#} ГБ)";
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

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        await ApplyRamPolicyAsync();
    }

    private async Task ApplyRamPolicyAsync()
    {
        int totalRamMb = 0;

        try
        {
            var hardware = await Task.Run(HardwareInfoService.Collect);
            totalRamMb = hardware.TotalRamMb;
        }
        catch (Exception ex)
        {
            AppLog.Error("Hardware detection", ex);
        }

        if (IsDisposed)
            return;

        _maxRamMb = ComputeMaxRamMb(totalRamMb);

        _suppressEvents = true;
        _ramSlider.Minimum = 1024;
        _ramSlider.Maximum = _maxRamMb;
        _ramSlider.Step = 512;

        int initial;
        if (_isFirstLaunch)
        {
            // Smart default: 8192 MB on machines with >= 12 GB, otherwise 6144 MB,
            // never above the maximum available for this PC.
            initial = totalRamMb >= 12288 ? 8192 : 6144;
        }
        else
        {
            initial = _settings.RamMb > 0 ? _settings.RamMb : 6144;
        }

        _ramSlider.Value = Math.Clamp(NormalizeRam(initial), 1024, _maxRamMb);
        _suppressEvents = false;

        _settings.RamMb = _ramSlider.Value;
        UpdateRamReadout();
        UpdateRamWarning();
        _session.Save();
    }

    private static int ComputeMaxRamMb(int totalMb)
    {
        if (totalMb <= 0)
            return 8192;

        int max = Math.Max(1024, totalMb - 2048);
        max = (max / 512) * 512;
        return Math.Max(1024, max);
    }

    private static int NormalizeRam(int ramMb)
    {
        int value = (int)(Math.Round(ramMb / 512d) * 512d);
        return Math.Max(1024, value);
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

    private void ShowValidationError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    // -------------------------------------------------------------- logout

    /// <summary>
    /// Deletes auth_session.json and closes the settings window; MainForm
    /// switches back to the unauthorized state (non-intrusive flow — no modal
    /// re-auth dialog and no application exit).
    /// </summary>
    private void HandleLogout()
    {
        SessionManager.ClearSession();
        _session.Save();
        LogoutRequested = true;
        Close();
    }
}
