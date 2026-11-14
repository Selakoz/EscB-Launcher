namespace EscapeBook.Models;

/// <summary>Local installation state of a build declared in config.json.</summary>
internal enum BuildStatus
{
    /// <summary>Installed and matches the latest config version.</summary>
    UpToDate,

    /// <summary>Installed, but an older version than the one in config.json.</summary>
    UpdateAvailable,

    /// <summary>Not downloaded to this PC yet.</summary>
    NotInstalled
}