using System;
using System.Collections.Generic;
using System.Linq;
using AkariOSCompanion.Models;
using Microsoft.Win32;

namespace AkariOSCompanion.Services;

public sealed record OperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    public int AppliedCount { get; init; }

    /// <summary>Every registry write that succeeded, as "path = value".</summary>
    public IReadOnlyList<string> AppliedWrites { get; init; } = Array.Empty<string>();

    public bool RequiresElevation { get; init; }
    public string? RequiresRestart { get; init; }

    public static OperationResult Ok(int applied = 1, string? requiresRestart = null) =>
        new() { Success = true, AppliedCount = applied, RequiresRestart = requiresRestart };
    public static OperationResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Writes a setting's change to the machine: registry values, per-NIC values,
/// scheduled tasks and scripts.
/// </summary>
public interface ISettingOperationExecutor
{
    OperationResult ApplyRecommended(SettingDefinition setting);
    OperationResult ApplyDefault(SettingDefinition setting);
    OperationResult ApplyToggle(SettingDefinition setting, bool enabled);
    OperationResult ApplySelection(SettingDefinition setting, int optionIndex);
}

public sealed class SettingOperationExecutor : ISettingOperationExecutor
{
    private readonly IWindowsRegistryService _registry;
    private readonly IScheduledTaskService _tasks;
    private readonly Action<string>? _log;

    public SettingOperationExecutor(IWindowsRegistryService registry,
                                    IScheduledTaskService tasks,
                                    Action<string>? log = null)
    {
        _registry = registry;
        _tasks = tasks;
        _log = log;
    }

    // ── Public entry points ──────────────────────────────────────────────────

    public OperationResult ApplyRecommended(SettingDefinition setting) =>
        setting.InputType == InputType.Toggle
            ? ApplyToggle(setting,
                  SettingDefinitionToggleState.GetRecommendedToggleState(setting) ?? true)
            : ApplySelection(setting, IndexOfRecommended(setting));

    public OperationResult ApplyDefault(SettingDefinition setting) =>
        setting.InputType == InputType.Toggle
            ? ApplyToggle(setting,
                  SettingDefinitionToggleState.GetDefaultToggleState(setting) ?? true)
            : ApplySelection(setting, IndexOfDefault(setting));

    public OperationResult ApplyToggle(SettingDefinition setting, bool enabled)
    {
        if (setting.RegistrySettings.Count == 0 && setting.ScheduledTaskSettings.Count == 0
            && setting.PowerShellScripts.Count == 0)
            return OperationResult.Fail($"{setting.Name} has no registry, task or script backing.");

        var failures = new List<string>();
        var appliedWrites = new List<string>();
        var applied = 0;

        // Direction is resolved once, from the explicit Recommended/DefaultToggleState
        // flags when the catalog sets them. For VBS, HVCI and Game DVR "recommended"
        // means OFF, so inferring direction from the value arrays inverted them.
        var recommended = SettingDefinitionToggleState.GetRecommendedToggleState(setting);
        var windowsDefault = SettingDefinitionToggleState.GetDefaultToggleState(setting);

        foreach (var rs in setting.RegistrySettings)
        {
            // One chokepoint, ported verbatim from the old project. Every special case
            // (per-NIC, per-monitor, key-existence, composite, binary) lives inside it.
            if (_registry.ApplySetting(rs, enabled))
            {
                applied++;
                appliedWrites.Add(Describe(rs, enabled, ResolveWriteValue(rs, enabled)));
            }
            else
            {
                failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
                _log?.Invoke($"[FAIL] {rs.KeyPath}\\{rs.ValueName}");
            }
        }

        foreach (var task in setting.ScheduledTaskSettings)
        {
            // Direction: when the catalog states that "recommended" is off, ask for
            // the recommended state when disabling and the default when enabling.
            var want = recommended is bool rec
                ? (enabled == rec ? task.RecommendedState : task.DefaultState)
                : (enabled ? task.RecommendedState : task.DefaultState);
            if (want is null) continue;

            // Absent on this machine (removed in 26H2, Office not installed, …):
            // skip honestly instead of logging a phantom success or a failure.
            if (!_tasks.TaskExists(task.TaskPath))
            {
                _log?.Invoke($"[SKIP] Task not present on this machine: {task.TaskPath}");
                continue;
            }

            if (_tasks.SetTaskEnabled(task.TaskPath, want.Value, out var err))
            {
                applied++;
                _log?.Invoke($"[TASK] {(want.Value ? "Enabled" : "Disabled")}: {task.TaskPath}");
            }
            else
            {
                failures.Add($"Task: {task.TaskPath}");
                _log?.Invoke($"[TASK] FAILED to {(want.Value ? "enable" : "disable")} "
                            + $"{task.TaskPath}: {err}");
            }
        }

        RunScripts(setting, option: null, useEnabled: enabled,
                   appliedWrites, failures, ref applied);

        var result = Build(failures, applied, setting, appliedWrites);
        LogOutcome(setting, enabled, result);
        return result;
    }

    /// <summary>
    /// Runs a setting's PowerShell scripts, ported from Akari-Tool's executor:
    /// per-option <see cref="ScriptOption"/> picks Enabled/Disabled/None, then the
    /// selected option's <c>ScriptVariables</c> are substituted into
    /// <c>{{placeholders}}</c>. Unsubstituted placeholders are left intact —
    /// the DoH sweep script self-guards on them.
    /// </summary>
    private void RunScripts(SettingDefinition setting, ComboBoxOption? option, bool useEnabled,
                            List<string> appliedWrites, List<string> failures, ref int applied)
    {
        foreach (var scriptSetting in setting.PowerShellScripts)
        {
            if (option?.Script is { } scriptOption)
            {
                if (scriptOption == ScriptOption.None) continue;
                useEnabled = scriptOption == ScriptOption.Enabled;
            }

            var script = useEnabled ? scriptSetting.EnabledScript : scriptSetting.DisabledScript;

            if (!string.IsNullOrEmpty(script) && option?.ScriptVariables is { } variables)
                foreach (var kvp in variables)
                    script = script.Replace("{{" + kvp.Key + "}}", kvp.Value);

            if (string.IsNullOrEmpty(script)) continue;

            _log?.Invoke($"[SCRIPT] {setting.Name}: \"{option?.DisplayName ?? (useEnabled ? "ON" : "OFF")}\"");
            if (PowerShellScriptRunner.Run(script, _log))
            {
                applied++;
                appliedWrites.Add($"script: {setting.Name} \"{option?.DisplayName ?? (useEnabled ? "ON" : "OFF")}\"");
            }
            else
            {
                failures.Add($"Script: {setting.Name}");
            }
        }
    }

    private void LogOutcome(SettingDefinition setting, bool enabled, OperationResult result,
                            string? detail = null)
    {
        if (_log is null) return;

        var what = detail ?? $"applied {(enabled ? "ON" : "OFF")}";

        if (result.Failures.Count == 0)
        {
            _log($"[OK] {setting.Name}: {what} ({result.AppliedCount} value(s))");
        }
        else
        {
            _log($"[PARTIAL] {setting.Name}: {what} - {result.AppliedCount} applied, "
                + $"{result.Failures.Count} FAILED");
            foreach (var f in result.Failures) _log($"           failed: {f}");
        }

        if (result.RequiresRestart is not null)
            _log($"[RESTART] {setting.Name}: {result.RequiresRestart}");
    }

    public OperationResult ApplySelection(SettingDefinition setting, int optionIndex)
    {
        var options = setting.ComboBox?.Options;
        if (options is null or { Count: 0 })
            return OperationResult.Fail($"{setting.Name} is not a dropdown setting.");

        if (optionIndex < 0 || optionIndex >= options.Count)
            return OperationResult.Fail($"Option index {optionIndex} out of range.");

        var option = options[optionIndex];
        var rs = setting.RegistrySettings.FirstOrDefault(r => r.IsPrimary)
                 ?? setting.RegistrySettings.FirstOrDefault();
        // Script-driven dropdowns (e.g. DNS) have no registry backing — the
        // PowerShell scripts below are their entire apply path.
        if (rs is null && setting.PowerShellScripts.Count == 0)
            return OperationResult.Fail($"{setting.Name} has no registry backing.");

        var failures = new List<string>();
        var appliedWrites = new List<string>();
        var applied = 0;

        if (rs is not null && option.ValueMappings is { Count: > 0 })
        {
            foreach (var (valueName, value) in option.ValueMappings)
            {
                // Every entry that owns this value name gets written. Delivery
                // Optimization declares DODownloadMode under both HKCU and HKLM policy
                // keys and Windows only honours HKLM, so writing a single match left the
                // dropdown looking applied while the machine ignored it.
                var targets = setting.RegistrySettings
                                 .Where(r => r.ValueName == valueName)
                                 .ToList();

                if (targets.Count == 0)
                {
                    // Option names a value the catalog didn't declare — write it under
                    // the primary entry so the mapping is not silently dropped.
                    targets.Add(rs with { ValueName = valueName });
                }

                foreach (var targetRs in targets)
                {
                    if (_registry.ApplySetting(targetRs, enable: true, specificValue: value))
                    {
                        applied++;
                        appliedWrites.Add(Describe(targetRs, true, value));
                    }
                    else
                    {
                        failures.Add($"{targetRs.KeyPath}\\{valueName}");
                        _log?.Invoke($"[FAIL] {targetRs.KeyPath}\\{valueName} = {value ?? "(absent)"}");
                    }
                }
            }
        }
        else if (rs is not null && option.SimpleValue is not null)
        {
            if (_registry.ApplySetting(rs, enable: true, specificValue: option.SimpleValue))
            {
                applied++;
                appliedWrites.Add(Describe(rs, true, option.SimpleValue));
            }
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
        }
        RunScripts(setting, option, useEnabled: true,
                   appliedWrites, failures, ref applied);
        var result = Build(failures, applied, setting, appliedWrites);
        if (result.Failures.Count == 0)
            _log?.Invoke($"[OK] {setting.Name}: option \"{option.DisplayName}\" applied ({applied} value(s)");
        else
            LogOutcome(setting, true, result, $"option \"{option.DisplayName}\"");
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Human-readable log line for one applied write. Mirrors what ApplySettingCore
    /// actually did (delete-on-null, bit edit, composite sub-key, per-location fan-out).
    /// </summary>
    private static string Describe(RegistrySetting rs, bool enabled, object? value)
    {
        if (rs.ValueName == null)
            return $"{rs.KeyPath} (key {(enabled ? "created" : "deleted")})";

        var where = $"{rs.KeyPath}\\{rs.ValueName}";
        if (rs.CompositeStringKey is not null)
            return $"{where} [{rs.CompositeStringKey}] = {FormatValue(value)}";
        if (rs.BitMask.HasValue && rs.BinaryByteIndex.HasValue)
            return $"{where} bit[{rs.BinaryByteIndex}] {(enabled ? "set" : "clear")}";
        if (rs.ApplyPerNetworkInterface || rs.ApplyPerMonitor)
            return $"{where} = {FormatValue(value)} (per-location expansion)";
        return $"{where} = {FormatValue(value)}";
    }

    /// <summary>
    /// Mirrors WindowsRegistryService.GetWriteValue: first non-null entry wins,
    /// null (or empty) means "delete this value". Must stay in sync with
    /// ApplySettingCore's standard-value path or the log lies about what was written.
    /// </summary>
    private static object? ResolveWriteValue(RegistrySetting rs, bool enabled)
    {
        var values = enabled ? rs.EnabledValue : rs.DisabledValue;
        if (values is null) return null;
        foreach (var v in values)
            if (v is not null) return v;
        return null;
    }

    private static string FormatValue(object? value) =>
        value is null ? "(value removed)" : value.ToString() ?? "(null)";

    private static int IndexOfRecommended(SettingDefinition setting)
    {
        var options = setting.ComboBox?.Options ?? Array.Empty<ComboBoxOption>();
        for (var i = 0; i < options.Count; i++) if (options[i].IsRecommended) return i;
        return 0;
    }

    private static int IndexOfDefault(SettingDefinition setting)
    {
        var options = setting.ComboBox?.Options ?? Array.Empty<ComboBoxOption>();
        for (var i = 0; i < options.Count; i++) if (options[i].IsDefault) return i;
        return 0;
    }

    private static OperationResult Build(List<string> failures, int applied, SettingDefinition setting,
                                        List<string>? appliedWrites = null)
    {
        IReadOnlyList<string> writes = appliedWrites ?? (IReadOnlyList<string>)Array.Empty<string>();

        if (failures.Count == 0)
            return new OperationResult
            {
                Success = true,
                AppliedCount = applied,
                AppliedWrites = writes,
                RequiresRestart = setting.RequiresRestart
                    ? "A restart is required before this takes effect."
                    : null,
            };

        return new OperationResult
        {
            Success = false,
            AppliedCount = applied,
            AppliedWrites = writes,
            Failures = failures,
            ErrorMessage = $"{failures.Count} write(s) failed for {setting.Name}",
            RequiresRestart = setting.RequiresRestart ? "A restart is required for this change to take effect." : null,
        };
    }
}
