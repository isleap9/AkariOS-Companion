using System.Collections.Generic;

namespace AkariOSCompanion.Services;

/// <summary>
/// Static per-setting warnings confirmed by VM testing, shown directly on the
/// row in the View. Keys are setting IDs; keep in sync with what
/// SettingStateReader.DetectUnavailable reports dynamically.
/// </summary>
public static class BuildWarnings
{
    private const string NotOn26H2 =
        "Not available on Windows 11 26H2 — this setting does nothing on this build.";

    private static readonly Dictionary<string, string> Warnings = new()
    {
        ["gaming-task-compatibility-appraiser"] = NotOn26H2,
        ["gaming-task-program-data-updater"] = NotOn26H2,
        ["gaming-task-windows-ai-recall-config"] = NotOn26H2,
        ["gaming-task-windows-ai-recall-pipeline"] = NotOn26H2,
        ["gaming-fax-service"] = NotOn26H2,
        ["gaming-mixed-reality-service"] = NotOn26H2,
        ["gaming-touch-keyboard-service"] = NotOn26H2,
    };

    public static string? For(string settingId) =>
        Warnings.TryGetValue(settingId, out var warning) ? warning : null;
}
