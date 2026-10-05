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
        if (setting.RegistrySettings.Count == 0 && setting.ScheduledTaskSettings.Count == 0)
            return OperationResult.Fail($"{setting.Name} has no registry or task backing.");

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
            if (ApplyRegistrySetting(rs, enabled, failures, appliedWrites, ref applied))
                continue;
            // If ApplyRegistrySetting returned false, the failure was already recorded.
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

        var result = Build(failures, applied, setting, appliedWrites);
        LogOutcome(setting, enabled, result);
        return result;
    }

    /// <summary>
    /// Applies one registry setting, handling all special cases (key-existence,
    /// composite strings, binary bit-masks, binary byte-only, per-monitor).
    /// Returns true on success, false on failure (failure is recorded in the list).
    /// </summary>
    private bool ApplyRegistrySetting(RegistrySetting rs, bool enabled,
                                      List<string> failures, List<string> appliedWrites, ref int applied)
    {
        // Key-existence toggle (ValueName == null)
        if (rs.ValueName == null)
        {
            var ok = enabled ? _registry.CreateKey(rs.KeyPath) : _registry.DeleteKey(rs.KeyPath);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath} (key {(enabled ? "created" : "deleted")})");
            }
            else failures.Add($"{rs.KeyPath} (key)");
            return ok;
        }

        // Composite REG_SZ
        if (rs.CompositeStringKey is not null)
        {
            var target = enabled ? FirstNonNull(rs.EnabledValue)?.ToString()
                                 : FirstNonNull(rs.DisabledValue)?.ToString();
            var ok = _registry.WriteCompositeString(rs.KeyPath, rs.ValueName, rs.CompositeStringKey, target);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{rs.ValueName} [{rs.CompositeStringKey}] = {target ?? "(removed)"}");
            }
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
            return ok;
        }

        // REG_BINARY bit-mask
        if (rs.BitMask.HasValue && rs.BinaryByteIndex.HasValue)
        {
            var ok = _registry.ModifyBinaryBit(rs.KeyPath, rs.ValueName,
                rs.BinaryByteIndex.Value, rs.BitMask.Value, enabled);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{rs.ValueName} bit[{rs.BinaryByteIndex}] {(enabled ? "set" : "clear")}");
            }
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
            return ok;
        }

        // REG_BINARY byte-only
        if (rs.ModifyByteOnly && rs.BinaryByteIndex.HasValue)
        {
            var byteValue = enabled ? FirstNonNull(rs.EnabledValue) switch
            {
                byte b => b,
                int i => (byte)i,
                _ => (byte)0
            } : FirstNonNull(rs.DisabledValue) switch
            {
                byte b => b,
                int i => (byte)i,
                _ => (byte)0
            };
            var ok = _registry.ModifyBinaryByte(rs.KeyPath, rs.ValueName,
                rs.BinaryByteIndex.Value, byteValue);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{rs.ValueName} byte[{rs.BinaryByteIndex}] = 0x{byteValue:X2}");
            }
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
            return ok;
        }

        // Per-monitor expansion
        if (rs.ApplyPerMonitor)
        {
            var subKeys = _registry.GetSubKeyNames(rs.KeyPath);
            if (subKeys.Length == 0)
            {
                failures.Add($"{rs.KeyPath} (no subkeys)");
                return false;
            }
            var allOk = true;
            foreach (var subKey in subKeys)
            {
                var expanded = rs with { KeyPath = $@"{rs.KeyPath}\{subKey}", ApplyPerMonitor = false };
                if (!ApplyRegistrySetting(expanded, enabled, failures, appliedWrites, ref applied))
                    allOk = false;
            }
            return allOk;
        }

        // Standard value write / delete
        var targetValue = ResolveTarget(rs, enabled);
        var ok2 = _registry.WriteValue(rs.KeyPath, rs.ValueName, targetValue, rs.ValueType);
        if (ok2)
        {
            applied++;
            appliedWrites.Add($"{rs.KeyPath}\\{rs.ValueName} = {targetValue ?? "(value removed)"}");
        }
        else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
        return ok2;
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

    /// <summary>
    /// The value that realises <paramref name="enabled"/> for one backing registry
    /// setting, or null to delete the value.
    ///
    /// Ported from the old project's WindowsRegistryService.ApplySettingCore:
    ///   valueToSet = specificValue ?? (isEnabled
    ///                  ? GetWriteValue(EnabledValue)
    ///                  : GetWriteValue(DisabledValue))
    ///
    /// The value arrays are the ONLY input. RecommendedValue / DefaultValue /
    /// RecommendedToggleState describe the *badge* ("Recommended", "Default") shown
    /// in the UI, not the write path — consulting them here inverted every setting
    /// whose recommended state happens to be "off" (VBS, Game DVR, and others).
    /// </summary>
    private static object? ResolveTarget(RegistrySetting rs, bool enabled)
    {
        var target = enabled ? FirstNonNull(rs.EnabledValue)
                             : FirstNonNull(rs.DisabledValue);

        // Null means "remove the value" — a legitimate outcome, e.g. Alt+Tab's
        // EnabledValue { 3, null } where 3 is written and null is never reached.
        return target;
    }

    private static object? FirstNonNull(object?[]? values) => values?.FirstOrDefault(v => v is not null);

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
        var appliedWrites = new List<string>();
        var applied = 0;

        if (option.ValueMappings is { Count: > 0 })
        {
            foreach (var (valueName, value) in option.ValueMappings)
            {
                var kind = setting.RegistrySettings
                    .FirstOrDefault(r => r.ValueName == valueName)?.ValueType
                    ?? InferKind(value);
                var targetRs = setting.RegistrySettings.FirstOrDefault(r => r.ValueName == valueName) ?? rs;
                if (Write(targetRs, valueName, value, kind))
                {
                    applied++;
                    appliedWrites.Add($"{targetRs.KeyPath}\\{valueName} = "
                                    + $"{value ?? "(value removed)"}");
                }
                else failures.Add($"{targetRs.KeyPath}\\{valueName}");
            }
        }
        else if (option.SimpleValue is not null)
        {
            if (Write(rs, rs.ValueName, option.SimpleValue, rs.ValueType))
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{rs.ValueName} = {option.SimpleValue}");
            }
            else failures.Add($"{rs.KeyPath}\\{rs.ValueName}");
        }
        var result = Build(failures, applied, setting, appliedWrites);
        if (result.Failures.Count == 0)
            _log?.Invoke($"[OK] {setting.Name}: option \"{option.DisplayName}\" applied ({applied} value(s)");
        else
            LogOutcome(setting, true, result, $"option \"{option.DisplayName}\"");
        return result;
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

    /// <summary>
    /// Applies a specific value to a registry setting, handling special cases
    /// (composite strings, binary bit-masks, binary byte-only, per-monitor).
    /// Returns true on success, false on failure.
    /// </summary>
    private bool ApplyRegistrySettingWithValue(RegistrySetting rs, string valueName,
        object? value, RegistryValueKind kind,
        List<string> failures, List<string> appliedWrites, ref int applied)
    {
        // Composite REG_SZ
        if (rs.CompositeStringKey is not null)
        {
            var ok = _registry.WriteCompositeString(rs.KeyPath, valueName, rs.CompositeStringKey, value?.ToString());
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{valueName} [{rs.CompositeStringKey}] = {value ?? "(removed)"}");
            }
            else failures.Add($"{rs.KeyPath}\\{valueName}");
            return ok;
        }

        // REG_BINARY bit-mask — set or clear based on value
        if (rs.BitMask.HasValue && rs.BinaryByteIndex.HasValue)
        {
            var setBit = value switch
            {
                bool b => b,
                int i => i != 0,
                byte b => b != 0,
                _ => true
            };
            var ok = _registry.ModifyBinaryBit(rs.KeyPath, valueName,
                rs.BinaryByteIndex.Value, rs.BitMask.Value, setBit);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{valueName} bit[{rs.BinaryByteIndex}] {(setBit ? "set" : "clear")}");
            }
            else failures.Add($"{rs.KeyPath}\\{valueName}");
            return ok;
        }

        // REG_BINARY byte-only
        if (rs.ModifyByteOnly && rs.BinaryByteIndex.HasValue)
        {
            var byteValue = value switch
            {
                byte b => b,
                int i => (byte)i,
                _ => (byte)0
            };
            var ok = _registry.ModifyBinaryByte(rs.KeyPath, valueName,
                rs.BinaryByteIndex.Value, byteValue);
            if (ok)
            {
                applied++;
                appliedWrites.Add($"{rs.KeyPath}\\{valueName} byte[{rs.BinaryByteIndex}] = 0x{byteValue:X2}");
            }
            else failures.Add($"{rs.KeyPath}\\{valueName}");
            return ok;
        }

        // Per-monitor expansion
        if (rs.ApplyPerMonitor)
        {
            var subKeys = _registry.GetSubKeyNames(rs.KeyPath);
            if (subKeys.Length == 0)
            {
                failures.Add($"{rs.KeyPath} (no subkeys)");
                return false;
            }
            var allOk = true;
            foreach (var subKey in subKeys)
            {
                var expanded = rs with { KeyPath = $@"{rs.KeyPath}\{subKey}", ApplyPerMonitor = false };
                if (!ApplyRegistrySettingWithValue(expanded, valueName, value, kind, failures, appliedWrites, ref applied))
                    allOk = false;
            }
            return allOk;
        }

        // Standard value write / delete
        var ok2 = _registry.WriteValue(rs.KeyPath, valueName, value, kind);
        if (ok2)
        {
            applied++;
            appliedWrites.Add($"{rs.KeyPath}\\{valueName} = {value ?? "(value removed)"}");
        }
        else failures.Add($"{rs.KeyPath}\\{valueName}");
        return ok2;
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
