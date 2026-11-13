using System.Drawing.Drawing2D;

namespace EscapeBook.Controls;

internal sealed class ModernSlider : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _step = 1;

    public ModernSlider()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        Height = 30;
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Theme.Background;
    }

    public event EventHandler? ValueChanged;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
                _maximum = _minimum;
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    public int Step
    {
        get => _step;
        set { _step = Math.Max(1, value); Invalidate(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (clamped == _value)
                return;

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF TrackRect
    {
        get
        {
            const float pad = 12f;
            return new RectangleF(pad, (Height / 2f) - 3f, Math.Max(1f, Width - (pad * 2f)), 6f);
        }
    }

    private float ValueToX(int value)
    {
        if (_maximum == _minimum)
            return TrackRect.Left;

        double ratio = (value - (double)_minimum) / (_maximum - (double)_minimum);
        return TrackRect.Left + (float)(TrackRect.Width * Math.Clamp(ratio, 0d, 1d));
    }

    private void SetValueFromX(int x)
    {
        var track = TrackRect;
        double ratio = Math.Clamp((x - track.Left) / (double)track.Width, 0d, 1d);
        double raw = _minimum + (ratio * (_maximum - _minimum));
        int steps = (int)Math.Round((raw - _minimum) / _step, MidpointRounding.AwayFromZero);
        int value = _minimum + (steps * _step);
        Value = Math.Clamp(value, _minimum, _maximum);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Painted completely in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);

        var track = TrackRect;
        using (var trackPath = Theme.RoundedRect(track, 3f))
        using (var trackBrush = new SolidBrush(Theme.Track))
            graphics.FillPath(trackBrush, trackPath);

        float thumbX = ValueToX(_value);
        float fillWidth = thumbX - track.Left;
        if (fillWidth > 0.5f)
        {
            var fillRect = new RectangleF(track.Left, track.Top, fillWidth, track.Height);
            using var fillPath = Theme.RoundedRect(fillRect, Math.Min(3f, fillWidth / 2f));
            using var fillBrush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted);
            graphics.FillPath(fillBrush, fillPath);
        }

        const float thumbRadius = 9f;
        float centerY = Height / 2f;

        using (var ringBrush = new SolidBrush(Parent?.BackColor ?? Theme.Background))
            graphics.FillEllipse(
                ringBrush,
                thumbX - thumbRadius - 2f,
                centerY - thumbRadius - 2f,
                (thumbRadius + 2f) * 2f,
                (thumbRadius + 2f) * 2f);

        using (var thumbBrush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted))
            graphics.FillEllipse(
                thumbBrush,
                thumbX - thumbRadius,
                centerY - thumbRadius,
                thumbRadius * 2f,
                thumbRadius * 2f);

        if (Focused && Enabled)
        {
            using var pen = new Pen(Color.FromArgb(110, Theme.Text), 1.5f);
            graphics.DrawEllipse(
                pen,
                thumbX - thumbRadius - 3f,
                centerY - thumbRadius - 3f,
                (thumbRadius + 3f) * 2f,
                (thumbRadius + 3f) * 2f);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            Focus();
            Capture = true;
            SetValueFromX(e.X);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Enabled && Capture && (e.Button & MouseButtons.Left) == MouseButtons.Left)
            SetValueFromX(e.X);

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Enabled)
            Value = _value + (e.Delta > 0 ? _step : -_step);

        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value = _value - _step;
                e.Handled = true;
                break;
            case Keys.Right:
            case Keys.Up:
                Value = _value + _step;
                e.Handled = true;
                break;
            case Keys.Home:
                Value = _minimum;
                e.Handled = true;
                break;
            case Keys.End:
                Value = _maximum;
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}