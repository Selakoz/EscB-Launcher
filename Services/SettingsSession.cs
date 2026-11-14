using EscapeBook.Models;

namespace EscapeBook.Services;

/// <summary>
/// Holds the live <see cref="UserSettings"/> instance and persists it instantly
/// whenever the user changes any control.
/// Text input is coalesced with a short debounce, discrete controls (combo, slider,
/// toggle) are written to disk immediately.
/// </summary>
internal sealed class SettingsSession : IDisposable
{
    private const int DebounceMs = 300;

    private readonly System.Windows.Forms.Timer _timer;
    private bool _dirty;

    public SettingsSession(UserSettings settings)
    {
        Settings = settings;

        _timer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Flush();
        };
    }

    public UserSettings Settings { get; }

    /// <summary>Writes the settings file right away.</summary>
    public void Save()
    {
        _timer.Stop();
        _dirty = true;
        Flush();
    }

    /// <summary>Schedules a write (coalesces rapid keystrokes).</summary>
    public void SaveSoon()
    {
        _dirty = true;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Writes pending changes if any.</summary>
    public void Flush()
    {
        _timer.Stop();

        if (!_dirty)
            return;

        _dirty = false;

        if (!SettingsService.TrySave(Settings, out string? error) && !string.IsNullOrEmpty(error))
            AppLog.Write("Settings auto-save warning: " + error);
    }

    public void Dispose()
    {
        Flush();
        _timer.Dispose();
    }
}
