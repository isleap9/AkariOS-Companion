using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using AkariOSCompanion.Models;
using Microsoft.Win32;

namespace AkariOSCompanion.Services;

/// <summary>
/// Reads the live state of a <see cref="SettingDefinition"/> off this machine:
/// toggle on/off, and which dropdown option is currently in effect.
/// </summary>
public interface ISettingStateReader
{
    SettingStateResult ReadState(SettingDefinition setting);
    bool ReadToggleState(SettingDefinition setting);
    int ReadSelectionIndex(SettingDefinition setting);
}

public sealed class SettingStateReader : ISettingStateReader
{
    private readonly IWindowsRegistryService _registry;
    private readonly IScheduledTaskService _tasks;

    public SettingStateReader(IWindowsRegistryService registry,
                              IScheduledTaskService tasks)
    {
        _registry = registry;
        _tasks = tasks;
    }

    // ── Toggle ───────────────────────────────────────────────────────────────

    public bool ReadToggleState(SettingDefinition setting)
    {
        var rs = PrimaryRegistrySetting(setting);
        if (rs == null)
        {
            // Task-backed toggle (RegistrySettings is empty for these, not null).
            // Presence is not state: the task's Enabled flag is.
            var task = setting.ScheduledTaskSettings.FirstOrDefault();
            return task is not null && _tasks.IsTaskEnabled(task.TaskPath);
        }

        if (!_registry.TryOpenSubKey(rs.KeyPath, out var key) || key is null)
            return false;

        using (key)
        {
            // Key-existence toggles are on when the key itself is present.
            if (rs.ValueName == null)
                return true;

            var current = key.GetValue(rs.ValueName);

            // Value absent. The null sentinel inside EnabledValue means "absent
            // counts as on", but only when the catalog author opted into it for
            // this value. VBS, HVCI and Game DVR list { 1, null } yet Windows leaves
            // the value absent precisely when the feature is OFF, so an absent value
            // here means off.
            if (current == null)
            {
                // An absent value means "not configured", i.e. Windows is using its
                // own default. When the catalog states that default explicitly we can
                // trust it; VBS, HVCI and Game DVR all declare DefaultToggleState and
                // Windows leaves the value absent precisely when the feature is off.
                if (setting.RecommendedToggleState is not null
                    && setting.DefaultToggleState is not null)
                    return SettingDefinitionToggleState.GetDefaultToggleState(setting) ?? false;

                // Otherwise keep the sentinel convention: a null inside EnabledValue
                // means the feature is on by default and absence preserves that.
                return rs.EnabledValue?.Contains(null) == true;
            }

            // REG_BINARY flag inside a specific byte, tested against a bit mask.
            if (current is byte[] blob && rs.BinaryByteIndex is int idx && rs.BitMask is byte mask)
                return BlobBitIsSet(blob, idx, mask);

            // Composite REG_SZ: compare only the sub-key this setting owns.
            if (rs.CompositeStringKey is not null)
                return CompositeContains(current, rs.CompositeStringKey);

            return ValueEquals(current, rs.EnabledValue?.FirstOrDefault());
        }
    }

    // ── Selection ────────────────────────────────────────────────────────────

    public int ReadSelectionIndex(SettingDefinition setting)
    {
        var options = setting.ComboBox?.Options;
        if (options is null or { Count: 0 }) return -1;

        if (setting.DetectionType == DetectionType.DnsServer)
            return DetectDnsServerIndex(setting);

        var rs = PrimaryRegistrySetting(setting);
        if (rs == null) return -1;

        var live = _registry.ReadValue(rs.KeyPath, rs.ValueName);

        // No value present at all: the default option is what Windows is doing.
        if (live == null)
            return setting.ResolveUnmatchedToDefault
                ? IndexOfDefault(options)
                : FirstIndexWhereAllValuesAbsent(options, rs);

        for (var i = 0; i < options.Count; i++)
            if (OptionMatches(options[i], rs, live))
                return i;

        // Unrecognised live value.
        return setting.ResolveUnmatchedToDefault ? IndexOfDefault(options) : -1;
    }

    // ── Combined ─────────────────────────────────────────────────────────────

    public SettingStateResult ReadState(SettingDefinition setting)
    {
        var raw = new Dictionary<string, string>();

        foreach (var rs in setting.RegistrySettings)
        {
            if (!_registry.TryOpenSubKey(rs.KeyPath, out var key) || key is null) continue;
            using (key)
            {
                if (rs.ValueName != null && key.GetValue(rs.ValueName) is { } v)
                    raw[$"{rs.KeyPath}\\{rs.ValueName}"] = Format(v);
                else if (rs.ValueName != null)
                    raw[$"{rs.KeyPath}\\{rs.ValueName}"] = "(absent)";
                else
                    raw[rs.KeyPath] = "(key present)";
            }
        }

        try
        {
            var isSelection = setting.InputType == InputType.Selection;
            return new SettingStateResult
            {
                IsEnabled = ReadToggleState(setting),
                CurrentIndex = isSelection ? ReadSelectionIndex(setting) : -1,
                IsCustomState = isSelection && ReadSelectionIndex(setting) == -1,
                Success = true,
                RawValues = raw,
            };
        }
        catch (Exception ex)
        {
            return new SettingStateResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ── Option matching ──────────────────────────────────────────────────────

    private static bool OptionMatches(ComboBoxOption option, RegistrySetting rs, object? live)
    {
        if (option.ValueMappings is { Count: > 0 })
            return option.ValueMappings.TryGetValue(rs.ValueName ?? string.Empty, out var mapped)
                   && ValueEquals(live, mapped);

        return option.SimpleValue is not null && ValueEquals(live, option.SimpleValue);
    }

    /// <summary>
    /// When every backing value is absent, the machine is on Windows defaults, so
    /// resolve to whichever option is marked default (or the first).
    /// </summary>
    private static int FirstIndexWhereAllValuesAbsent(IReadOnlyList<ComboBoxOption> options,
                                                      RegistrySetting rs)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].ValueMappings is not { } map) continue;
            if (map.TryGetValue(rs.ValueName ?? string.Empty, out var mapped) && mapped != null)
                return i;
        }

        return -1;
    }

    private static int IndexOfDefault(IReadOnlyList<ComboBoxOption> options)
    {
        for (var i = 0; i < options.Count; i++)
            if (options[i].IsDefault) return i;
        return 0;
    }

    // ── DNS ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// DNS dropdowns resolve from the live adapter configuration rather than a
    /// registry value, so without this the ComboBox renders blank on every launch.
    /// </summary>
    private int DetectDnsServerIndex(SettingDefinition setting)
    {
        if (setting.ComboBox?.Options is not { Count: > 0 } options) return -1;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var dns = nic.GetIPProperties().DnsAddresses.FirstOrDefault()?.ToString();
            if (dns is null) continue;

            // Options carry the server address either as a registry ValueMapping or,
            // for the script-driven variants, as a ScriptVariable.
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];

                if (option.ValueMappings is { Count: > 0 } map
                    && map.Values.OfType<string>().Any(s =>
                        string.Equals(s, dns, StringComparison.OrdinalIgnoreCase)))
                    return i;

                if (option.ScriptVariables is { Count: > 0 } vars
                    && vars.Values.OfType<string>().Any(s =>
                        string.Equals(s, dns, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
        }
        return -1;
    }

    // ── Value helpers ────────────────────────────────────────────────────────

    private static RegistrySetting? PrimaryRegistrySetting(SettingDefinition setting) =>
        setting.RegistrySettings.FirstOrDefault(r => r.IsPrimary)
        ?? setting.RegistrySettings.FirstOrDefault();

    internal static bool ValueEquals(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        // REG_BINARY arrives as byte[]; compare the flags we care about.
        if (a is byte[] ba && b is byte[] bb) return ba.SequenceEqual(bb);

        // Numeric values arrive as int/uint/long depending on the writer; normalise.
        if (IsNumeric(a) && IsNumeric(b))
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture)
                 == Convert.ToDecimal(b, CultureInfo.InvariantCulture);

        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);

        return a.Equals(b);
    }

    private static bool IsNumeric(object v) =>
        v is int or uint or long or ulong or short or ushort or byte or sbyte or double or float or decimal;

    private static bool BlobBitIsSet(byte[] blob, int index, byte mask) =>
        index >= 0 && index < blob.Length && (blob[index] & mask) == mask;

    /// <summary>
    /// Composite REG_SZ values pack several flags into one string, each with its own
    /// sub-key and value ("subkey:value;"). Only the pair we own decides the state.
    /// </summary>
    private static bool CompositeContains(object? raw, string ownedKey)
    {
        if (raw is not string s) return false;
        foreach (var pair in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split(':', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(ownedKey, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim() is "1" or "true" or "enabled";
        }
        return false;
    }

    internal static string Format(object? v) => v switch
    {
        null => "(absent)",
        byte[] b => BitConverter.ToString(b),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? v.ToString() ?? "(null)",
    };
}
