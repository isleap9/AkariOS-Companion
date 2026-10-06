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

        // A toggle can be mirrored across hives under the same value name — Storage Sense
        // writes both HKCU and HKLM policy keys. Reads covered only the primary, so a
        // setting that was disabled in one hive but left on in the other reported ON.
        // Report ON only when every mirror agrees, which also makes a half-applied state
        // read as OFF so re-toggling pushes the change back out to all locations.
        var mirrors = setting.RegistrySettings
                              .Where(r => r.ValueName == rs.ValueName)
                              .ToList();
        if (mirrors.Count > 1)
        {
            foreach (var mirror in mirrors)
                if (!ReadSingleToggle(mirror, setting)) return false;
            return true;
        }

        return ReadSingleToggle(rs, setting);
    }

    /// <summary>Reads one registry setting's on/off state, in isolation.</summary>
    private bool ReadSingleToggle(RegistrySetting rs, SettingDefinition setting)
    {
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

            // Composite REG_SZ: extract the owned sub-key and compare against
            // EnabledValue; when absent, fall back to DefaultValue. Ported from the
            // old project's ResolveCompositeState — the write side merges
            // "key=value;" pairs, so the read must split on '=' (not ':').
            if (rs.CompositeStringKey is not null)
                return ResolveCompositeState(rs, setting, current);

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

        // A dropdown can be backed by the same value name under several hives. Delivery
        // Optimization declares DODownloadMode in both HKCU and HKLM policy keys and
        // Windows honours the HKLM one, so probe them in order and take the first value
        // that is actually present — reading only the primary would report a stale HKCU
        // value (or "default") while the machine was really running something else.
        var candidates = new List<RegistrySetting> { rs };
        candidates.AddRange(setting.RegistrySettings
                                .Where(r => r.ValueName == rs.ValueName && !ReferenceEquals(r, rs)));

        object? live = null;
        RegistrySetting? liveFrom = null;
        foreach (var candidate in candidates)
        {
            var v = _registry.ReadValue(candidate.KeyPath, candidate.ValueName);
            if (v is not null) { live = v; liveFrom = candidate; break; }
        }

        // No value present anywhere: the default option is what Windows is doing.
        if (live is null || liveFrom is null)
            return setting.ResolveUnmatchedToDefault
                ? IndexOfDefault(options)
                : FirstIndexWhereAllValuesAbsent(options, rs);

        for (var i = 0; i < options.Count; i++)
            if (OptionMatches(options[i], liveFrom, live))
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
        index >= 0 && index < blob.Length && (blob[index] & mask) != 0;

    /// <summary>
    /// Composite REG_SZ values pack several flags into one string as "key=value;"
    /// pairs. Only the pair this setting owns decides the state: present → compare
    /// against EnabledValue; absent → compare DefaultValue against EnabledValue.
    /// </summary>
    private static bool ResolveCompositeState(RegistrySetting rs, SettingDefinition setting, object? raw)
    {
        var pairs = ParseCompositeString(raw?.ToString() ?? "");
        var enabledStr = rs.EnabledValue?.FirstOrDefault(v => v is not null)?.ToString();
        if (pairs.TryGetValue(rs.CompositeStringKey!, out var subValue))
            return string.Equals(subValue, enabledStr, StringComparison.OrdinalIgnoreCase);

        return string.Equals(rs.DefaultValue?.ToString(),
            enabledStr, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseCompositeString(string value)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value)) return pairs;
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq > 0) pairs[entry[..eq].Trim()] = entry[(eq + 1)..].Trim();
        }
        return pairs;
    }

    internal static string Format(object? v) => v switch
    {
        null => "(absent)",
        byte[] b => BitConverter.ToString(b),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? v.ToString() ?? "(null)",
    };
}
