using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security;
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
    /// Applies <paramref name="values"/> to every network interface that has an
    /// adapter-specific sub-key, used by the DNS settings.
    /// </summary>
    int WritePerNetworkInterface(string keyPathTemplate, string? valueName,
                                  object? value, RegistryValueKind kind);
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

    public bool DeleteKey(string keyPath)
    {
        try
        {
            if (!KeyExists(keyPath)) return true;
            using var key = RegistryKey.OpenBaseKey(ResolveHive(keyPath), RegistryView.Registry64)
                                   .OpenSubKey(SubKeyPath(keyPath), true);
            if (key == null) return false;
            key.DeleteSubKeyTree(string.Empty, false);
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

    public int WritePerNetworkInterface(string keyPathTemplate, string? valueName,
                                        object? value, RegistryValueKind kind)
    {
        // Templates look like: ...\Tcpip\Parameters\Interfaces\{iface}
        var written = 0;
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            // Registry sub-keys are keyed by the adapter interface GUID, not the MAC.
            var guid = iface.Id;
            var path = keyPathTemplate.Replace("{iface}", guid);
            if (WriteValue(path, valueName, value, kind)) written++;
        }
        return written;
    }

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
