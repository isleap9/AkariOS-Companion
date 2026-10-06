using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using AkariOSCompanion.Models;
using Microsoft.Win32;

namespace AkariOSCompanion.Services;

/// <summary>
/// Thin wrapper over <see cref="RegistryKey"/> that never throws: every operation
/// degrades to a null/false result so a locked-down key or missing hive cannot take
/// the app down mid-scan.
/// </summary>
public interface IWindowsRegistryService
{
    bool TryOpenSubKey(string keyPath, out RegistryKey? subKey);
    object? ReadValue(string keyPath, string? valueName);
    bool WriteValue(string keyPath, string? valueName, object? value, RegistryValueKind kind);
    bool DeleteValue(string keyPath, string? valueName);
    bool KeyExists(string keyPath);
    bool CreateKey(string keyPath);
    bool DeleteKey(string keyPath);
    string[] GetSubKeyNames(string keyPath);
    bool ModifyBinaryBit(string keyPath, string valueName, int byteIndex, byte bitMask, bool setBit);
    bool ModifyBinaryByte(string keyPath, string valueName, int byteIndex, byte byteValue);
    bool WriteCompositeString(string keyPath, string valueName, string compositeKey, string? value);

    /// <summary>
    /// Applies a whole <see cref="RegistrySetting"/> — every special case (per-NIC,
    /// per-monitor, key-existence, composite string, binary bit/byte) is handled
    /// inside. This is the single write chokepoint, ported verbatim from the old
    /// project's WindowsRegistryService.ApplySettingCore.
    /// </summary>
    /// <param name="specificValue">
    /// When non-null this exact value is written instead of resolving from
    /// Enabled/DisabledValue. Used by dropdown options.
    /// </param>
    bool ApplySetting(RegistrySetting setting, bool enable, object? specificValue = null);
}

public sealed class WindowsRegistryService : IWindowsRegistryService
{
    // ── Reads ────────────────────────────────────────────────────────────────

    public bool TryOpenSubKey(string keyPath, out RegistryKey? subKey)
    {
        subKey = null;
        try
        {
            subKey = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                          .OpenSubKey(SubKeyPath(keyPath));
            return subKey != null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public object? ReadValue(string keyPath, string? valueName)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .OpenSubKey(SubKeyPath(keyPath));
            return key?.GetValue(valueName ?? string.Empty);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return null;
        }
    }

    public bool KeyExists(string keyPath)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .OpenSubKey(SubKeyPath(keyPath));
            return key != null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    // ── Writes ───────────────────────────────────────────────────────────────

    public bool WriteValue(string keyPath, string? valueName, object? value, RegistryValueKind kind)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .CreateSubKey(SubKeyPath(keyPath), true);
            if (key == null) return false;

            if (value == null)
            {
                // Null means "remove this value" — the key-absent sentinel.
                if (key.GetValue(valueName ?? string.Empty) != null)
                    key.DeleteValue(valueName ?? string.Empty, false);
                return true;
            }

            key.SetValue(valueName ?? string.Empty, value, kind);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool DeleteValue(string keyPath, string? valueName)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .OpenSubKey(SubKeyPath(keyPath), true);
            if (key?.GetValue(valueName ?? string.Empty) == null) return true;
            key.DeleteValue(valueName ?? string.Empty, false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool CreateKey(string keyPath)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .CreateSubKey(SubKeyPath(keyPath), true);
            return key != null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    private const int MinDeleteDepth = 2;
    private static readonly HashSet<string> ProtectedSubKeyRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        @"SOFTWARE\Microsoft\Windows",
        @"SOFTWARE\Microsoft\Windows NT",
        @"SOFTWARE\Policies",
        @"SYSTEM\CurrentControlSet",
        @"SYSTEM\CurrentControlSet\Services",
    };

    public bool DeleteKey(string keyPath)
    {
        try
        {
            if (!KeyExists(keyPath)) return true;
            var hive = ResolveHive(keyPath);
            var subKeyPath = SubKeyPath(keyPath);
            var segments = subKeyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < MinDeleteDepth)
            {
                Log($"[REGISTRY] Refusing to delete shallow key '{keyPath}'");
                return false;
            }
            foreach (var protectedRoot in ProtectedSubKeyRoots)
            {
                if (subKeyPath.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[REGISTRY] Refusing to delete protected key '{keyPath}'");
                    return false;
                }
            }
            RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                       .DeleteSubKeyTree(subKeyPath, false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public string[] GetSubKeyNames(string keyPath)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .OpenSubKey(SubKeyPath(keyPath));
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public bool ModifyBinaryBit(string keyPath, string valueName, int byteIndex, byte bitMask, bool setBit)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .CreateSubKey(SubKeyPath(keyPath), true);
            if (key == null) return false;
            var data = key.GetValue(valueName) as byte[] ?? Array.Empty<byte>();
            if (byteIndex >= data.Length)
                Array.Resize(ref data, byteIndex + 1);
            if (setBit) data[byteIndex] |= bitMask;
            else        data[byteIndex] = (byte)(data[byteIndex] & ~bitMask);
            key.SetValue(valueName, data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool ModifyBinaryByte(string keyPath, string valueName, int byteIndex, byte byteValue)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .CreateSubKey(SubKeyPath(keyPath), true);
            if (key == null) return false;
            var data = key.GetValue(valueName) as byte[] ?? Array.Empty<byte>();
            if (byteIndex >= data.Length)
                Array.Resize(ref data, byteIndex + 1);
            data[byteIndex] = byteValue;
            key.SetValue(valueName, data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool WriteCompositeString(string keyPath, string valueName, string compositeKey, string? value)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .CreateSubKey(SubKeyPath(keyPath), true);
            if (key == null) return false;

            var current = key.GetValue(valueName) as string ?? "";
            var pairs = ParseCompositeString(current);
            if (value != null) pairs[compositeKey] = value;
            else pairs.Remove(compositeKey);
            var merged = BuildCompositeString(pairs);

            key.SetValue(valueName, merged, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException or ArgumentException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseCompositeString(string value)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value)) return pairs;
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq > 0) pairs[entry[..eq]] = entry[(eq + 1)..];
        }
        return pairs;
    }

    private static string BuildCompositeString(Dictionary<string, string> pairs)
    {
        if (pairs.Count == 0) return "";
        return string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}")) + ";";
    }

    // ── Single write chokepoint ───────────────────────────────────────────────
    // Ported verbatim from the old project's WindowsRegistryService.ApplySettingCore.
    // Do NOT split this up: per-NIC / per-monitor expansion, composite-string merge
    // and binary bit/byte edits all share this one recursive path, which is why the
    // old project needed no per-setting special cases at the executor level.

    public bool ApplySetting(RegistrySetting setting, bool enable, object? specificValue = null)
        => ApplySettingCore(setting, enable, specificValue, useDefaultValue: false);

    private bool ApplySettingCore(RegistrySetting setting, bool isEnabled,
                                  object? specificValue, bool useDefaultValue)
    {
        if (setting == null) return false;

        try
        {
            // ── Per-network-interface expansion ──
            // The path points at the Interfaces container and each adapter is a sub-key
            // named by its GUID, so expand over the registry's own sub-keys.
            if (setting.ApplyPerNetworkInterface)
            {
                var subKeys = GetSubKeyNames(setting.KeyPath);
                if (subKeys.Length == 0)
                {
                    Log($"[REGISTRY] No subkeys under '{setting.KeyPath}' for per-interface setting");
                    return false;
                }
                var allSucceeded = true;
                foreach (var subKey in subKeys)
                {
                    var expanded = setting with
                    {
                        KeyPath = $@"{setting.KeyPath}\{subKey}",
                        ApplyPerNetworkInterface = false
                    };
                    if (!ApplySettingCore(expanded, isEnabled, specificValue, useDefaultValue))
                        allSucceeded = false;
                }
                return allSucceeded;
            }

            // ── Per-monitor expansion ──
            if (setting.ApplyPerMonitor)
            {
                var subKeys = GetSubKeyNames(setting.KeyPath);
                if (subKeys.Length == 0)
                {
                    Log($"[REGISTRY] No subkeys under '{setting.KeyPath}' for per-monitor setting");
                    return false;
                }
                var allSucceeded = true;
                foreach (var subKey in subKeys)
                {
                    var expanded = setting with
                    {
                        KeyPath = $@"{setting.KeyPath}\{subKey}",
                        ApplyPerMonitor = false
                    };
                    if (!ApplySettingCore(expanded, isEnabled, specificValue, useDefaultValue))
                        allSucceeded = false;
                }
                return allSucceeded;
            }

            Log($"[REGISTRY] Applying: {setting.KeyPath}\\{setting.ValueName} enable={isEnabled}");

            // ── Key-existence toggle (ValueName == null) ──
            if (setting.ValueName == null)
                return isEnabled ? CreateKey(setting.KeyPath) : DeleteKey(setting.KeyPath);

            // ── Composite REG_SZ (e.g. DirectXUserGlobalSettings) ──
            if (setting.CompositeStringKey != null)
            {
                if (!CreateKey(setting.KeyPath)) return false;
                var current = ValueExists(setting.KeyPath, setting.ValueName)
                    ? (ReadValue(setting.KeyPath, setting.ValueName)?.ToString() ?? "")
                    : "";
                var pairs = ParseCompositeString(current);
                var subValue = specificValue?.ToString()
                    ?? (isEnabled ? GetWriteValue(setting.EnabledValue)?.ToString()
                                  : GetWriteValue(setting.DisabledValue)?.ToString());
                if (subValue != null) pairs[setting.CompositeStringKey] = subValue;
                else pairs.Remove(setting.CompositeStringKey);
                var merged = BuildCompositeString(pairs);
                return WriteValue(setting.KeyPath, setting.ValueName, merged, RegistryValueKind.String);
            }

            // ── REG_BINARY bit-mask (BinaryByteIndex + BitMask) ──
            if (setting.BitMask.HasValue && setting.BinaryByteIndex.HasValue)
            {
                if (!CreateKey(setting.KeyPath)) return false;
                var setBit = specificValue switch
                {
                    bool b => b,
                    int   i => i != 0,
                    byte  b => b != 0,
                    _        => isEnabled
                };
                return ModifyBinaryBit(setting.KeyPath, setting.ValueName!,
                    setting.BinaryByteIndex.Value, setting.BitMask.Value, setBit);
            }

            // ── REG_BINARY byte-only modify ──
            if (setting.ModifyByteOnly && setting.BinaryByteIndex.HasValue)
            {
                var byteValue = specificValue switch
                {
                    byte b => b,
                    int   i => (byte)i,
                    _ when isEnabled => GetWriteValue(setting.EnabledValue) switch
                    {
                        byte b => b,
                        int  i => (byte)i,
                        _      => (byte)0
                    },
                    _ => GetWriteValue(setting.DisabledValue) switch
                    {
                        byte b => b,
                        int  i => (byte)i,
                        _      => (byte)0
                    }
                };
                if (!CreateKey(setting.KeyPath)) return false;
                return ModifyBinaryByte(setting.KeyPath, setting.ValueName!,
                    setting.BinaryByteIndex.Value, byteValue);
            }

            // ── Standard value write / delete ──
            var valueToSet = useDefaultValue
                ? GetWriteValue(setting.DisabledValue)
                : specificValue ?? (isEnabled
                    ? GetWriteValue(setting.EnabledValue)
                    : GetWriteValue(setting.DisabledValue));

            if (valueToSet == null)
                return DeleteValue(setting.KeyPath, setting.ValueName);

            if (!CreateKey(setting.KeyPath)) return false;
            return WriteValue(setting.KeyPath, setting.ValueName, valueToSet, setting.ValueType);
        }
        catch (Exception ex)
        {
            Log($"[REGISTRY] Error applying '{setting.KeyPath}\\{setting.ValueName}': {ex.Message}");
            return false;
        }
    }

    private static object? GetWriteValue(object?[]? values) => values?.FirstOrDefault(v => v != null);

    private bool ValueExists(string keyPath, string? valueName) =>
        ReadValue(keyPath, valueName) is not null;

    private static void Log(string message) => AppLog.Write(message);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RegistryHive ResolveHive(string keyPath) =>
        keyPath.ToUpperInvariant().StartsWith("HKEY_CURRENT_USER") ? RegistryHive.CurrentUser
        : keyPath.ToUpperInvariant().StartsWith("HKEY_USERS")         ? RegistryHive.Users
        : keyPath.ToUpperInvariant().StartsWith("HKEY_CLASSES_ROOT") ? RegistryHive.ClassesRoot
        : keyPath.ToUpperInvariant().StartsWith("HKEY_CURRENT_CONFIG") ? RegistryHive.CurrentConfig
        : RegistryHive.LocalMachine;

    private static string SubKeyPath(string keyPath)
    {
        var i = keyPath.IndexOf('\\');
        return i >= 0 ? keyPath[(i + 1)..] : string.Empty;
    }
}
