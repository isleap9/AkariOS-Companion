using System.Collections.Generic;

namespace AkariOSCompanion.Models;

/// <summary>
/// Interpreted live state of a single setting on this machine.
/// </summary>
public sealed record SettingStateResult
{
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Resolved dropdown index, or -1 when the live state matches no option
    /// (or the setting is not a Selection).
    /// </summary>
    public int CurrentIndex { get; init; } = -1;

    public bool Success { get; init; }

    /// <summary>Human-readable reason the read failed; null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Raw registry values backing this setting, for tooltips and diagnostics.</summary>
    public IReadOnlyDictionary<string, string> RawValues { get; init; } =
        new Dictionary<string, string>();

    /// <summary>True when the live value is not offered by any option.</summary>
    public bool IsCustomState { get; init; }

    /// <summary>
    /// Non-null when the setting's backing target does not exist on this machine
    /// (task absent, service key absent). Shown as a visual "not available" note.
    /// </summary>
    public string? UnavailableReason { get; init; }
}
