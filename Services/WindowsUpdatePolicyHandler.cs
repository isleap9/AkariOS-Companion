using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AkariOSCompanion.Services;

/// <summary>
/// Special-setting handler for the "updates-policy-mode" dropdown. Ported 1:1 from the
/// old Akari-Tool project's WindowsUpdatePolicyHandler (originally Winhance's
/// UpdateService).
///
/// The generic <see cref="SettingOperationExecutor"/> cannot express this setting:
/// the four modes (Normal / Security Only / Paused / Disabled) are detected from a
/// COMBINATION of signals — renamed DLLs, pause timestamps, defer flags — and applying
/// them requires stopping services, disabling scheduled tasks and renaming files in
/// System32. Paused and Disabled states also collide under single-value registry
/// matching, so the dropdown needs its own read path.
/// </summary>
public sealed class WindowsUpdatePolicyHandler
{
    private const string AuHklm = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string AuHkcu = @"HKEY_CURRENT_USER\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string UxHklm = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private const string AuSub = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string UxSub = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private readonly IWindowsRegistryService _registry;
    private readonly Action<string> _log;

    public WindowsUpdatePolicyHandler(IWindowsRegistryService registry, Action<string> log)
    {
        _registry = registry;
        _log = log;
    }

    // ── Public entry points ─────────────────────────────────────────────────────

    /// <summary>Reads which of the four policy modes is currently in effect.</summary>
    public int GetCurrentPolicyIndex()
    {
        // Probe 1: critical DLLs renamed (Disabled mode).
        foreach (var dll in CriticalDlls)
        {
            var dllPath = SystemDll(dll);
            var backupPath = SystemDll(BackupName(dll));
            if (File.Exists(backupPath) && !File.Exists(dllPath))
            {
                _log($"[UPDATE] Detect: {dll} renamed -> Disabled (3)");
                return 3;
            }
        }

        // Probe 2: pause timestamps present (Paused mode).
        foreach (var name in PauseValueNames)
        {
            var v = _registry.ReadValue(UxHklm, name);
            if (v != null)
            {
                _log($"[UPDATE] Detect: {name} present ({v}) -> Paused (2)");
                return 2;
            }
        }

        // Probe 3: feature updates deferred (Security Only mode).
        if (_registry.ReadValue(UxHklm, "DeferFeatureUpdates") is int d && d == 1)
        {
            _log("[UPDATE] Detect: DeferFeatureUpdates=1 -> Security Only (1)");
            return 1;
        }

        // Probe 4: Normal.
        return 0;
    }

    /// <summary>Applies one of the four policy modes. Returns true when every step ran.</summary>
    public async Task<bool> ApplyPolicyModeAsync(int index)
    {
        if (index is < 0 or > 3)
        {
            _log($"[UPDATE] Invalid policy index {index}");
            return false;
        }

        _log($"[UPDATE] Applying policy mode {index}...");

        switch (index)
        {
            case 0:
                RestoreCriticalDlls();
                await EnableUpdateServicesAsync();
                await EnableUpdateTasksAsync();
                ApplyNormalRegistry();
                break;
            case 1:
                RestoreCriticalDlls();
                await EnableUpdateServicesAsync();
                ApplySecurityOnlyRegistry();
                break;
            case 2:
                RestoreCriticalDlls();
                await SetUpdateServicesManualAsync();
                ApplyPausedRegistry();
                break;
            case 3:
                await DisableUpdateServicesAsync();
                await DisableUpdateTasksAsync();
                RenameCriticalDlls();
                await CleanupUpdateFilesAsync();
                ApplyDisabledRegistry();
                break;
        }

        _log($"[UPDATE] Policy mode {index} applied.");
        return true;
    }

    // ── Registry helpers ────────────────────────────────────────────────────────

    private void ApplyNormalRegistry()
    {
        _log("[UPDATE] Normal: clearing AU + UX values...");
        foreach (var name in AuValueNames)
        {
            var a = _registry.DeleteValue(AuHklm, name);
            var b = _registry.DeleteValue(AuHkcu, name);
            if (!a || !b) _log($"       delete AU {name}: hklm={a} hkcu={b}");
        }

        // Must match Akari-Tool reference uxValues 1:1 — GetCurrentPolicyIndex
        // probes PauseUpdatesStartTime etc for Paused mode, so omitting them
        // here made Normal read straight back as Paused.
        foreach (var name in UxValueNames)
        {
            var ok = _registry.DeleteValue(UxHklm, name);
            var stillThere = _registry.ReadValue(UxHklm, name) != null;
            _log($"       delete UX {name}: ok={ok} gone={(!stillThere)}");
        }
    }

    private void ApplySecurityOnlyRegistry()
    {
        ApplyNormalRegistry();

        _registry.WriteValue(AuHklm, "AUOptions", 2, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "AUOptions", 2, RegistryValueKind.DWord);

        _registry.WriteValue(UxHklm, "BranchReadinessLevel", 20, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "DeferFeatureUpdates", 1, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "DeferFeatureUpdatesPeriodInDays", 365, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "DeferQualityUpdates", 1, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "DeferQualityUpdatesPeriodInDays", 7, RegistryValueKind.DWord);
    }

    private void ApplyPausedRegistry()
    {
        ApplyNormalRegistry();

        _registry.WriteValue(AuHklm, "NoAutoUpdate", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "NoAutoUpdate", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "AUOptions", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "AUOptions", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "NoAUShutdownOption", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "NoAUShutdownOption", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "AlwaysAutoRebootAtScheduledTime", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "AlwaysAutoRebootAtScheduledTime", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "AutoInstallMinorUpdates", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "AutoInstallMinorUpdates", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "UseWUServer", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "UseWUServer", 0, RegistryValueKind.DWord);

        // Pause window: 2025-01-01 through 2051-12-31 (effectively "indefinite").
        // Ported 1:1 from Akari-Tool reference — all 8 UX string values.
        var stringPairs = new (string Name, string Value)[]
        {
            ("PauseFeatureUpdatesStartTime", "2025-01-01T00:00:00Z"),
            ("PauseFeatureUpdatesEndTime",   "2051-12-31T00:00:00Z"),
            ("PauseQualityUpdatesStartTime", "2025-01-01T00:00:00Z"),
            ("PauseQualityUpdatesEndTime",   "2051-12-31T00:00:00Z"),
            ("PauseUpdatesStartTime",        "2025-01-01T00:00:00Z"),
            ("PauseUpdatesExpiryTime",       "2051-12-31T00:00:00Z"),
            ("PausedQualityDate",            "2025-01-01T00:00:00Z"),
            ("PausedFeatureDate",            "2025-01-01T00:00:00Z"),
        };
        foreach (var (name, value) in stringPairs)
            _registry.WriteValue(UxHklm, name, value, RegistryValueKind.String);

        _registry.WriteValue(UxHklm, "FlightSettingsMaxPauseDays", 10023, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "PausedFeatureStatus", 1, RegistryValueKind.DWord);
        _registry.WriteValue(UxHklm, "PausedQualityStatus", 1, RegistryValueKind.DWord);
    }

    private void ApplyDisabledRegistry()
    {
        ApplyNormalRegistry();

        _registry.WriteValue(AuHklm, "NoAutoUpdate", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "NoAutoUpdate", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "AUOptions", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "AUOptions", 1, RegistryValueKind.DWord);
        _registry.WriteValue(AuHklm, "UseWUServer", 0, RegistryValueKind.DWord);
        _registry.WriteValue(AuHkcu, "UseWUServer", 0, RegistryValueKind.DWord);
    }

    // ── Services ────────────────────────────────────────────────────────────────

    private static readonly (string Name, string StartType)[] EnableServices =
    [
        ("wuauserv", "auto"),
        ("UsoSvc", "auto"),
        ("WaaSMedicSvc", "demand"),
    ];

    private static readonly string[] DisableServices = ["wuauserv", "UsoSvc", "WaaSMedicSvc"];

    private async Task EnableUpdateServicesAsync()
    {
        foreach (var (service, startType) in EnableServices)
        {
            if (!await RunCommandAsync($"sc config {service} start= {startType}")) continue;
            await RunCommandAsync($"net start {service}");
            _log($"       service enabled: {service} ({startType})");
        }
    }

    private async Task SetUpdateServicesManualAsync()
    {
        foreach (var service in DisableServices)
        {
            if (await RunCommandAsync($"sc config {service} start= demand"))
                _log($"       service set to manual: {service}");
        }
    }

    private async Task DisableUpdateServicesAsync()
    {
        foreach (var service in DisableServices)
        {
            await RunCommandAsync($"net stop {service}");
            if (await RunCommandAsync($"sc config {service} start= disabled"))
            {
                await RunCommandAsync($"sc failure {service} reset= 0 actions= \"\"");
                _log($"       service disabled: {service}");
            }
        }
    }

    // ── Scheduled tasks ─────────────────────────────────────────────────────────

    private static readonly string[] DisableTaskFolders =
    [
        @"\Microsoft\Windows\InstallService\",
        @"\Microsoft\Windows\UpdateOrchestrator\",
        @"\Microsoft\Windows\UpdateAssistant\",
        @"\Microsoft\Windows\WaaSMedic\",
        @"\Microsoft\Windows\WindowsUpdate\",
    ];

    private static readonly string[] EnableTaskFolders =
    [
        @"\Microsoft\Windows\UpdateOrchestrator\",
        @"\Microsoft\Windows\WindowsUpdate\",
    ];

    private async Task DisableUpdateTasksAsync()
    {
        foreach (var folder in DisableTaskFolders)
        {
            var script =
                $"Get-ScheduledTask -TaskPath '{folder}' -ErrorAction SilentlyContinue " +
                "| Disable-ScheduledTask -ErrorAction SilentlyContinue";
            if (await RunCommandAsync($"powershell -NoProfile -Command \"{script}\""))
                _log($"       tasks disabled in: {folder}");
        }
    }

    private async Task EnableUpdateTasksAsync()
    {
        foreach (var folder in EnableTaskFolders)
        {
            var script =
                $"Get-ScheduledTask -TaskPath '{folder}' -ErrorAction SilentlyContinue " +
                "| Enable-ScheduledTask -ErrorAction SilentlyContinue";
            if (await RunCommandAsync($"powershell -NoProfile -Command \"{script}\""))
                _log($"       tasks enabled in: {folder}");
        }
    }

    // ── DLLs ────────────────────────────────────────────────────────────────────

    private static readonly string[] CriticalDlls = ["WaaSMedicSvc.dll", "wuaueng.dll"];

    private static string SystemDll(string name) => $@"C:\Windows\System32\{name}";

    private static string BackupName(string dll) =>
        Path.GetFileNameWithoutExtension(dll) + "_BAK.dll";

    private void RenameCriticalDlls()
    {
        foreach (var dll in CriticalDlls)
        {
            var dllPath = SystemDll(dll);
            var backupPath = SystemDll(BackupName(dll));

            try
            {
                if (File.Exists(backupPath))
                {
                    // Backup present AND original present = Windows already restored it.
                    // Drop the stale backup rather than clobbering the live DLL.
                    if (File.Exists(dllPath))
                    {
                        _log($"       conflict: {dll} already restored, deleting stale backup");
                        DeleteWithTakeown(backupPath);
                        continue;
                    }
                    continue; // already renamed
                }

                if (!File.Exists(dllPath)) continue;

                Takeown(dllPath);
                File.Move(dllPath, backupPath);
                _log($"       renamed {dll} -> {BackupName(dll)}");
            }
            catch (Exception ex)
            {
                _log($"       failed to rename {dll}: {ex.Message}");
            }
        }
    }

    private void RestoreCriticalDlls()
    {
        foreach (var dll in CriticalDlls)
        {
            var dllPath = SystemDll(dll);
            var backupPath = SystemDll(BackupName(dll));

            try
            {
                if (!File.Exists(backupPath)) continue;

                if (File.Exists(dllPath))
                {
                    // Original is back — Windows (or the user) restored it. Clear backup.
                    _log($"       {dll} already present, removing stale backup");
                    DeleteWithTakeown(backupPath);
                    continue;
                }

                Takeown(backupPath);
                File.Move(backupPath, dllPath);
                _log($"       restored {dll} from backup");
            }
            catch (Exception ex)
            {
                _log($"       failed to restore {dll}: {ex.Message}");
            }
        }
    }

    /// <summary>System32 is TrustedInstaller-owned; take ownership before writing.</summary>
    private void Takeown(string path)
    {
        RunCommandAsync($"takeown /f \"{path}\"").GetAwaiter().GetResult();
        RunCommandAsync($"icacls \"{path}\" /grant *S-1-1-0:F").GetAwaiter().GetResult();
    }

    private void DeleteWithTakeown(string path)
    {
        Takeown(path);
        File.Delete(path);
    }

    private async Task CleanupUpdateFilesAsync()
    {
        var script = "Remove-Item 'C:\\Windows\\SoftwareDistribution\\*' -Recurse -Force -ErrorAction SilentlyContinue";
        if (await RunCommandAsync($"powershell -NoProfile -Command \"{script}\""))
            _log("       cleaned SoftwareDistribution folder");
    }

    // ── Process runner ──────────────────────────────────────────────────────────

    private async Task<bool> RunCommandAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _log($"       command failed: {command} — {ex.Message}");
            return false;
        }
    }

    // ── Value name tables ───────────────────────────────────────────────────────

    private static readonly string[] AuValueNames =
    [
        "NoAutoUpdate", "AUOptions", "NoAUShutdownOption",
        "AlwaysAutoRebootAtScheduledTime", "AutoInstallMinorUpdates", "UseWUServer",
    ];

    private static readonly string[] UxValueNames =
    [
        "BranchReadinessLevel", "DeferFeatureUpdates", "DeferFeatureUpdatesPeriodInDays",
        "DeferQualityUpdates", "DeferQualityUpdatesPeriodInDays",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PausedQualityDate", "PausedFeatureDate", "FlightSettingsMaxPauseDays",
        "PausedFeatureStatus", "PausedQualityStatus",
    ];

    private static readonly string[] PauseValueNames =
    [
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PausedQualityDate", "PausedFeatureDate",
    ];
}
