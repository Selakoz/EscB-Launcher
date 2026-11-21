using System.Runtime.InteropServices;

namespace EscapeBook.Forms;

/// <summary>
/// Borderless dark window base: custom title bar, dragging, window buttons.
/// </summary>
internal class BaseDarkForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    private readonly Label _minimizeButton;
    private readonly Label _closeButton;
    private readonly TableLayoutPanel _root;
    private bool _allowMinimize;

    protected Panel TitleBarPanel { get; }
    protected Panel ContentArea { get; }
    protected Label TitleLabel { get; }

    /// <summary>
    /// Hides the custom title bar and frees its row (used by forms that use the
    /// standard Windows border instead).
    /// </summary>
    protected void CollapseTitleBar()
    {
        if (_root.RowStyles.Count > 0)
        {
            _root.RowStyles[0].SizeType = SizeType.Absolute;
            _root.RowStyles[0].Height = 0f;
        }

        TitleBarPanel.Visible = false;
    }

    protected BaseDarkForm(Size clientSize, bool allowMinimize = false)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Font(9f);
        // Dpi scaling with an explicit 96-DPI design baseline. Without the
        // baseline WinForms captures the current monitor DPI as the design
        // size, so a window created on a 125%/150% monitor would not grow
        // while point-sized fonts still would (tiny window, huge clipped text).
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = clientSize;
        DoubleBuffered = true;
        KeyPreview = true;

        try
        {
            Icon = Branding.LoadIcon();
        }
        catch
        {
            // Fall back to default application icon.
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(root);
        _root = root;

        TitleBarPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };
        root.Controls.Add(TitleBarPanel, 0, 0);

        ContentArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };
        root.Controls.Add(ContentArea, 0, 1);

        var accent = new Panel
        {
            BackColor = Theme.Accent,
            Location = new Point(20, 14),
            Size = new Size(4, 16)
        };
        TitleBarPanel.Controls.Add(accent);

        TitleLabel = new Label
        {
            AutoSize = false,
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f, FontStyle.Bold),
            Location = new Point(34, 0),
            Size = new Size(360, 44),
            TextAlign = ContentAlignment.MiddleLeft
        };
        TitleBarPanel.Controls.Add(TitleLabel);

        var border = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Theme.Border
        };
        TitleBarPanel.Controls.Add(border);

        _closeButton = CreateTitleButton("✕");
        _closeButton.Click += (_, _) => Close();
        TitleBarPanel.Controls.Add(_closeButton);
        HookHover(_closeButton, Color.FromArgb(72, 32, 40));

        _minimizeButton = CreateTitleButton("—");
        _minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        TitleBarPanel.Controls.Add(_minimizeButton);
        HookHover(_minimizeButton, Theme.SurfaceHover);

        TitleBarPanel.Resize += (_, _) => RepositionTitleButtons();
        RepositionTitleButtons();
        AllowMinimize = allowMinimize;

        MouseEventHandler dragHandler = (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        };

        TitleBarPanel.MouseDown += dragHandler;
        TitleLabel.MouseDown += dragHandler;
        accent.MouseDown += dragHandler;
    }

    public bool AllowMinimize
    {
        get => _allowMinimize;
        set
        {
            _allowMinimize = value;
            _minimizeButton.Visible = value;
            RepositionTitleButtons();
        }
    }

    public bool CloseOnEscape { get; set; }

    public string TitleText
    {
        get => TitleLabel.Text;
        set
        {
            TitleLabel.Text = value;
            Text = value;
        }
    }

    private static Label CreateTitleButton(string text) => new()
    {
        AutoSize = false,
        Size = new Size(44, 43),
        Text = text,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Theme.Background,
        ForeColor = Theme.Muted,
        Font = Theme.Font(10f),
        Cursor = Cursors.Hand
    };

    private void RepositionTitleButtons()
    {
        // Use the buttons' actual (DPI-scaled) widths instead of the 96-DPI
        // design constants so they stay pinned to the right edge at any scaling.
        _closeButton.Location = new Point(Math.Max(0, TitleBarPanel.Width - _closeButton.Width), 0);
        _minimizeButton.Location = new Point(
            Math.Max(0, TitleBarPanel.Width - _closeButton.Width - _minimizeButton.Width), 0);
    }

    private void HookHover(Label button, Color hoverBackColor)
    {
        button.MouseEnter += (_, _) =>
        {
            button.BackColor = hoverBackColor;
            button.ForeColor = Theme.Text;
        };
        button.MouseLeave += (_, _) =>
        {
            button.BackColor = Theme.Background;
            button.ForeColor = Theme.Muted;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (CloseOnEscape && keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            // Do not set CS_DROPSHADOW here: Windows renders that shadow shifted
            // right/down, which made borderless windows look misaligned. The
            // centered DWM shadow is enabled in OnHandleCreated instead.
            return base.CreateParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            int roundPreference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref roundPreference, sizeof(int));

            int darkMode = 1;
            DwmSetWindowAttribute(Handle, 20, ref darkMode, sizeof(int));

            if (FormBorderStyle == FormBorderStyle.None)
            {
                // Borderless windows used to rely on CS_DROPSHADOW, which Windows
                // always paints offset towards the bottom-right. Enable the DWM
                // non-client shadow instead so it stays centered around the frame.
                int renderingPolicy = 2; // DWMNCRP_ENABLED
                DwmSetWindowAttribute(Handle, 2, ref renderingPolicy, sizeof(int));

                var margins = new Margins
                {
                    Left = 1,
                    Right = 1,
                    Top = 1,
                    Bottom = 1
                };
                DwmExtendFrameIntoClientArea(Handle, ref margins);
            }
        }
        catch
        {
            // Windows 10 and older simply keep square corners.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}