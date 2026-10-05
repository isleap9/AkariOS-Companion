using System;
using System.Globalization;
using System.Linq;
using AkariOSCompanion.Models;
using Microsoft.Win32;

namespace AkariOSCompanion.Services;

/// <summary>
/// Single source of truth for which toggle state counts as "recommended" and which
/// as the Windows default.
///
/// Order of resolution:
///   1. Explicit <see cref="SettingDefinition.RecommendedToggleState"/> /
///      <see cref="SettingDefinition.DefaultToggleState"/> win. These exist for
///      features like VBS, HVCI and Game DVR where "recommended" for a gaming
///      machine means the feature is OFF — the opposite of the usual sense, and
///      not derivable from the value arrays alone.
///   2. Otherwise map DefaultValue / RecommendedValue to a state by checking which
///      of EnabledValue / DisabledValue contains it.
///   3. Otherwise fall back to the first scheduled task that declares a state.
///   4. Otherwise null — caller applies nothing.
/// </summary>
public static class SettingDefinitionToggleState
{
    public static RegistrySetting? GetPrimaryRegistrySetting(SettingDefinition setting) =>
        setting.RegistrySettings.FirstOrDefault(r => r.IsPrimary)
        ?? setting.RegistrySettings.FirstOrDefault();

    /// <summary>True when the setting has no value name and no value arrays:
    /// the mere presence of the key means "on".</summary>
    public static bool IsKeyExistenceToggle(RegistrySetting r) =>
        r.ValueName == null
        && r.EnabledValue == null
        && r.DisabledValue == null
        && r.ValueType == RegistryValueKind.None;

    public static bool? GetRecommendedToggleState(SettingDefinition setting)
    {
        if (setting.RecommendedToggleState is bool explicitState) return explicitState;

        var reg = GetPrimaryRegistrySetting(setting);
        if (reg is not null)
        {
            var fromReg = ResolveToggleState(reg, reg.RecommendedValue, deriveFromKeyAbsent: false);
            if (fromReg is bool b) return b;
        }

        return setting.ScheduledTaskSettings.FirstOrDefault(t => t.RecommendedState.HasValue)
                        ?.RecommendedState;
    }

    public static bool? GetDefaultToggleState(SettingDefinition setting)
    {
        if (setting.DefaultToggleState is bool explicitState) return explicitState;

        var reg = GetPrimaryRegistrySetting(setting);
        if (reg is not null)
        {
            // Default may legitimately be "key absent", so the null sentinel counts.
            var fromReg = ResolveToggleState(reg, reg.DefaultValue, deriveFromKeyAbsent: true);
            if (fromReg is bool b) return b;
        }

        return setting.ScheduledTaskSettings.FirstOrDefault(t => t.DefaultState.HasValue)
                        ?.DefaultState;
    }

    /// <summary>
    /// The registry value that realises <paramref name="desiredState"/> for this
    /// setting, or null when the state is expressed by the value being absent.
    /// </summary>
    public static object? GetValueForState(SettingDefinition setting, bool desiredState)
    {
        var reg = GetPrimaryRegistrySetting(setting);
        if (reg is null) return null;

        // An explicit flag on the setting tells us the sense; otherwise infer it
        // from which array the target value belongs to.
        var recommended = GetRecommendedToggleState(setting);
        if (recommended is bool recState)
            return desiredState == recState ? reg.RecommendedValue : reg.DefaultValue;

        var array = desiredState ? reg.EnabledValue : reg.DisabledValue;
        return array?.FirstOrDefault(v => v is not null);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static bool? ResolveToggleState(RegistrySetting reg, object? targetValue,
                                           bool deriveFromKeyAbsent)
    {
        if (targetValue is null && !deriveFromKeyAbsent) return null;
        if (targetValue is null && deriveFromKeyAbsent && IsKeyExistenceToggle(reg)) return true;

        return TargetState(targetValue, reg.EnabledValue, reg.DisabledValue);
    }

    private static bool? TargetState(object? target, object?[]? enabled, object?[]? disabled)
    {
        if (target is null)
        {
            if (ArrayContainsNull(enabled)) return true;
            if (ArrayContainsNull(disabled)) return false;
            return null;
        }
        if (ArrayContains(target, enabled)) return true;
        if (ArrayContains(target, disabled)) return false;
        return null;
    }

    private static bool ArrayContainsNull(object?[]? array) => array?.Any(v => v is null) == true;

    private static bool ArrayContains(object? value, object?[]? array) =>
        array?.Any(v => ValuesEqual(value, v)) == true;

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is byte[] ba && b is byte[] bb) return ba.SequenceEqual(bb);

        if (IsNumeric(a) && IsNumeric(b))
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture)
                 == Convert.ToDecimal(b, CultureInfo.InvariantCulture);

        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);

        return a.Equals(b);
    }

    private static bool IsNumeric(object v) =>
        v is int or uint or long or ulong or short or ushort or byte or sbyte or double or float or decimal;
}
