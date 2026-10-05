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
        if (setting.RegistrySettings.Count == 0 && setting.ScheduledTaskSettings.Count == 0)
            return OperationResult.Fail($"{setting.Name} has no registry or task backing.");

        var failures = new List<string>();
        var applied = 0;

        // Direction is resolved once, from the explicit Recommended/DefaultToggleState
        // flags when the catalog sets them. For VBS, HVCI and Game DVR "recommended"
        // means OFF, so inferring direction from the value arrays inverted them.
        var recommended = SettingDefinitionToggleState.GetRecommendedToggleState(setting);
        var windowsDefault = SettingDefinitionToggleState.GetDefaultToggleState(setting);

        foreach (var rs in setting.RegistrySettings)
        {
            var target = ResolveTarget(rs, enabled, recommended, windowsDefault);

            if (Write(rs, rs.ValueName, target, rs.ValueType)) applied++;
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
        }

        foreach (var task in setting.ScheduledTaskSettings)
        {
            // Direction: when the catalog states that "recommended" is off, ask for
            // the recommended state when disabling and the default when enabling.
            var want = recommended is bool rec
                ? (enabled == rec ? task.RecommendedState : task.DefaultState)
                : (enabled ? task.RecommendedState : task.DefaultState);
            if (want is null) continue;

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

        var result = Build(failures, applied, setting);
        LogOutcome(setting, enabled, result);
        return result;
    }

    private void LogOutcome(SettingDefinition setting, bool enabled, OperationResult result)
    {
        if (_log is null) return;

        if (result.Failures.Count == 0)
        {
            _log($"[OK] {setting.Name}: applied {(enabled ? "ON" : "OFF")} "
                + $"({result.AppliedCount} value(s))");
        }
        else
        {
            _log($"[PARTIAL] {setting.Name}: {result.AppliedCount} applied, "
                + $"{result.Failures.Count} failed");
            foreach (var f in result.Failures) _log($"         failed: {f}");
        }
    }

    /// <summary>
    /// The value that realises <paramref name="enabled"/> for one backing registry
    /// setting. A null result means "remove the value".
    /// </summary>
    private static object? ResolveTarget(RegistrySetting rs, bool enabled,
                                         bool? recommended, bool? windowsDefault)
    {
        // The setting's explicit flags define the sense: pick whichever of this
        // value's recommended/default pair matches the state being asked for.
        if (recommended is bool rec)
        {
            bool useRecommended = enabled == rec;
            return useRecommended ? rs.RecommendedValue : rs.DefaultValue;
        }

        // Otherwise infer from the value arrays, honouring the null sentinel
        // ("key absent means on") only for the enabled direction.
        if (enabled)
            return rs.EnabledValue?.FirstOrDefault(v => v is not null) ?? rs.RecommendedValue;

        var disabled = rs.DisabledValue?.FirstOrDefault(v => v is not null);
        return disabled ?? rs.DefaultValue;
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
        if (rs is null)
            return OperationResult.Fail($"{setting.Name} has no registry backing.");

        var failures = new List<string>();
        var applied = 0;

        if (option.ValueMappings is { Count: > 0 })
        {
            foreach (var (valueName, value) in option.ValueMappings)
            {
                var kind = setting.RegistrySettings
                    .FirstOrDefault(r => r.ValueName == valueName)?.ValueType
                    ?? InferKind(value);
                if (Write(rs, valueName, value, kind)) applied++;
                else failures.Add($"{rs.KeyPath}\\{valueName}");
            }
        }
        else if (option.SimpleValue is not null)
        {
            if (Write(rs, rs.ValueName, option.SimpleValue, rs.ValueType)) applied++;
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
        }

        _log?.Invoke($"[{setting.Name}] option \"{option.DisplayName}\" applied ({applied} value(s)");
        return Build(failures, applied, setting);
    }

    // ── Write helpers ────────────────────────────────────────────────────────

    private bool Write(RegistrySetting rs, string? valueName, object? value, RegistryValueKind kind)
    {
        var ok = rs.ApplyPerNetworkInterface
            ? _registry.WritePerNetworkInterface(rs.KeyPath, valueName, value, kind) > 0
            : _registry.WriteValue(rs.KeyPath, valueName, value, kind);

        if (!ok) _log?.Invoke($"[FAIL] {rs.KeyPath}\\{valueName} = {value ?? "(absent)"}");
        return ok;
    }

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

    private static RegistryValueKind InferKind(object? value) => value switch
    {
        null => RegistryValueKind.String,
        int or uint or long or short => RegistryValueKind.DWord,
        byte[] => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };

    private static OperationResult Build(List<string> failures, int applied, SettingDefinition setting)
    {
        if (failures.Count == 0)
            return OperationResult.Ok(applied,
                setting.RequiresRestart ? "A restart is required for this change to take effect." : null);

        return new OperationResult
        {
            Success = false,
            AppliedCount = applied,
            Failures = failures,
            ErrorMessage = $"{failures.Count} write(s) failed for {setting.Name}",
            RequiresRestart = setting.RequiresRestart ? "A restart is required for this change to take effect." : null,
        };
    }
}
