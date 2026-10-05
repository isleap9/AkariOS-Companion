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
