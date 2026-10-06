using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
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
        // Script-only System Protection toggle: state lives in SPP\Clients matched
        // against the C: volume GUID (plus the DisableSR policy override), not in a
        // plain value. Ported from Akari-Tool's SystemRestoreService.IsEnabledForC,
        // minus WMI: the C: GUID comes from HKLM\SYSTEM\MountedDevices instead.
        if (setting.Id == "system-restore-protection")
            return IsSystemProtectionEnabled();

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
        // A missing key is treated exactly like a missing value: "not configured",
        // i.e. Windows is running its own default. Returning false here made
        // delete-to-enable rows (feedback prompts, push notifications, store
        // auto-downloads) permanently stuck — ON deleted nothing and OFF never
        // fired because the row always read OFF.
        _registry.TryOpenSubKey(rs.KeyPath, out var key);

        using (key)
        {
            // Key-existence toggles are on when the key itself is present.
            if (rs.ValueName == null)
                return key is not null;

            var current = key?.GetValue(rs.ValueName);

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
                UnavailableReason = DetectUnavailable(setting),
            };
        }
        catch (Exception ex)
        {
            return new SettingStateResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ── Availability ─────────────────────────────────────────────────────────

    /// <summary>
    /// System Protection state for C:, mirroring Akari-Tool's SystemRestoreService:
    /// DisableSR policy forces off; otherwise the C: volume GUID (from
    /// MountedDevices) must appear in SPP\Clients\{SR-GUID} (REG_MULTI_SZ).
    /// </summary>
    private bool IsSystemProtectionEnabled()
    {
        const string SrGuid = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";

        try
        {
            if (_registry.ReadValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore",
                    "DisableSR") is int p && p == 1)
                return false;

            var dosDevices = _registry.ReadValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\MountedDevices", @"\DosDevices\C:") as byte[];
            if (dosDevices is null) return false;
            var cGuid = ExtractVolumeGuid(dosDevices);
            if (cGuid is null) return false;

            if (_registry.ReadValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients",
                    SrGuid) is not string[] entries)
                return false;

            return entries.Any(e =>
                !string.IsNullOrEmpty(e) &&
                e.IndexOf(cGuid, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// C: volume GUID in canonical "{...}" form from MountedDevices data.
    /// Basic disks store UTF-16 "\??\Volume{GUID}"; dynamic disks store ASCII
    /// "DMIO:ID:" followed by the 16-byte mixed-endian GUID.
    /// </summary>
    private static string? ExtractVolumeGuid(byte[] data)
    {
        var text = Encoding.Unicode.GetString(data).TrimEnd('\0');
        var start = text.IndexOf('{');
        var end = text.IndexOf('}');
        if (start >= 0 && end > start) return text.Substring(start, end - start + 1);

        const string DmioPrefix = "DMIO:ID:";
        if (data.Length >= 24 && Encoding.ASCII.GetString(data, 0, 8) == DmioPrefix)
        {
            var guidBytes = new byte[16];
            Buffer.BlockCopy(data, 8, guidBytes, 0, 16);
            return "{" + new Guid(guidBytes).ToString("D") + "}";
        }

        return null;
    }

    /// <summary>
    /// The backing task itself is gone (removed in 26H2, app not installed).
    /// Registry keys are NOT checked here: absent policy keys are normal
    /// (Windows default), not missing features.
    /// </summary>
    private string? DetectUnavailable(SettingDefinition setting)
    {
        const string reason = "Not available on this Windows build";

        if (setting.RegistrySettings.Count == 0 && setting.ScheduledTaskSettings.Count > 0)
            return _tasks.TaskExists(setting.ScheduledTaskSettings[0].TaskPath) ? null : reason;

        return null;
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
