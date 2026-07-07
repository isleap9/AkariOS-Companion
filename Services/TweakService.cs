using System.Diagnostics;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace AkariOSCompanion.Services;

/// <summary>
/// Real implementation of ITweakService.
/// All tweak state is persisted in HKCU\Software\AkariTool so toggle positions
/// survive app restarts. The app runs elevated (requireAdministrator in app.manifest),
/// so HKLM writes and service/bcdedit calls work without extra elevation.
/// </summary>
public sealed class TweakService : ITweakService
{
    // ── Persistent state store ────────────────────────────────────────────────
    private static readonly RegistryKey _store =
        Registry.CurrentUser.CreateSubKey(
            @"Software\AkariTool",
            RegistryKeyPermissionCheck.ReadWriteSubTree);

    private static void SaveState(string key) => _store.SetValue(key, 1);
    private static void ClearState(string key) => _store.DeleteValue(key, throwOnMissingValue: false);
    private static bool HasState(string key) => _store.GetValue(key) != null;

    // ── ITweakService ─────────────────────────────────────────────────────────

    public bool GetState(string key) => HasState(StateKeyFor(key));

    public void SetState(string key, bool enabled)
    {
        try
        {
            switch (key)
            {
                case "wifi":            SetWifi(enabled);               break;
                case "tsx":             SetTsx(enabled);                break;
                case "actioncenter":    SetActionCenter(enabled);       break;
                case "dep":             SetDepNx(enabled);              break;
                case "clipboard":       SetClipboard(enabled);          break;
                case "bluetooth":       SetBluetooth(enabled);          break;
                case "bootmenu":        SetBootMenuPolicy(enabled);     break;
                case "vpn":             SetVpn(enabled);                break;
                case "ntfsenc":         SetNtfsEncryption(enabled);     break;
                case "fso":             SetFsoGamebar(enabled);         break;
                case "notifications":   SetNotifications(enabled);      break;
                case "prefetch":        SetPrefetch(enabled);           break;
                case "cdrom":           SetCdrom(enabled);              break;
                case "spooler":         SetPrintSpooler(enabled);       break;
                case "nolazy":          SetNoLazyMode(enabled);         break;
                case "uacadmin":        SetAdminUac(enabled);           break;
                case "vr":              SetVr(enabled);                 break;
                case "uac":             SetUac(enabled);                break;
                case "startmenu":       SetStartMenu(enabled);          break;
                case "hyperv":          SetHyperV(enabled);             break;
                case "vbs":             SetVbs(enabled);                break;
                case "wallpaperq":      SetWallpaperQuality(enabled);   break;
                case "mpo":             SetMpo(enabled);                break;
                case "transparency":    SetTransparency(enabled);       break;
                case "lockscreen":      SetLockScreen(enabled);         break;
                case "animations":      SetAnimations(enabled);         break;
                case "dcom":            SetDcom(enabled);               break;
                case "nvme":            SetNvmeTweaks(enabled);         break;
                case "largecache":      SetLargeSystemCache(enabled);   break;
                case "sysprofile":      SetSystemProfile(enabled);      break;
                case "defender":        SetDefender(enabled);           break;
                case "mitigation":      SetProcessMitigations(enabled); break;
                // Gaming tweaks
                case "preempt":         SetPreemption(enabled);         break;
                case "hdcp":            SetHdcp(enabled);               break;
                case "netopt":          SetNetworkOptimization(enabled); break;
                default:
                    Debug.WriteLine($"[TweakService] Unknown key: {key}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TweakService] SetState({key}) error: {ex.Message}");
        }
    }

    // Maps the ViewModel key to the registry state key name
    private static string StateKeyFor(string vmKey) => vmKey switch
    {
        "wifi"          => "DisableWiFi",
        "tsx"           => "EnableTSX",
        "actioncenter"  => "DisableActionCenter",
        "dep"           => "DisableNX",
        "clipboard"     => "EnableClipboardSvc",
        "bluetooth"     => "DisableBluetooth",
        "bootmenu"      => "BootMenuPolicy",
        "vpn"           => "DisableVPN",
        "ntfsenc"       => "DisableNTFSEncryption",
        "fso"           => "DisableFSO",
        "notifications" => "DisableNotifications",
        "prefetch"      => "DisablePrefetch",
        "cdrom"         => "EnableCDROM",
        "spooler"       => "DisablePrintSpooler",
        "nolazy"        => "NoLazyMode",
        "uacadmin"      => "EnableAdminUAC",
        "vr"            => "EnableVR",
        "uac"           => "EnableUAC",
        "startmenu"     => "DisableStartmenu",
        "hyperv"        => "DisableHyperV",
        "vbs"           => "EnableVBS",
        "wallpaperq"    => "WallpaperQuality",
        "mpo"           => "DisableMPO",
        "transparency"  => "TransparencyEffects",
        "lockscreen"    => "DisableLockScreen",
        "animations"    => "DisableAnimations",
        "dcom"          => "DisableDCOM",
        "nvme"          => "NVMETweaks",
        "largecache"    => "EnableLargeSystemCache",
        "sysprofile"    => "SystemProfile",
        "defender"      => "DisableDefender",
        "mitigation"    => "EnableProcessMitigations",
        "preempt"       => "DisablePreemption",
        "hdcp"          => "DisableHDCP",
        "netopt"        => "NetworkOptimization",
        _               => vmKey
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RunCommand(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit();
        }
        catch { /* best-effort */ }
    }

    // Reads real user HKCU even when running as admin (via explorer token)
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    private static RegistryKey CreateRealHkcuSubKey(string subKey)
    {
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault()
            ?? throw new InvalidOperationException("explorer.exe not found.");
        if (!OpenProcessToken(explorer.Handle, 8, out var token))
            throw new InvalidOperationException("Could not open explorer process token.");
        using var identity = new System.Security.Principal.WindowsIdentity(token);
        var sid = identity.User!.Value;
        var hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
        return hku.CreateSubKey($@"{sid}\{subKey}", writable: true)!;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    // ═════════════════════════════════════════════════════════════════════════
    // AKARI OS TWEAKS
    // ═════════════════════════════════════════════════════════════════════════

    private static void SetWifi(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableWiFi")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WlanSvc", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\vwififlt", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\netprofm", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NlaSvc", "Start", 4, RegistryValueKind.DWord);
            SaveState("DisableWiFi");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WlanSvc", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\vwififlt", "Start", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\netprofm", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NlaSvc", "Start", 2, RegistryValueKind.DWord);
            ClearState("DisableWiFi");
        }
    }

    private static void SetTsx(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableTSX")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Control\Session Manager\kernel", "DisableTsx", 0, RegistryValueKind.DWord);
            SaveState("EnableTSX");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Control\Session Manager\kernel", "DisableTsx", 1, RegistryValueKind.DWord);
            ClearState("EnableTSX");
        }
    }

    private static void SetActionCenter(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableActionCenter")) return;
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", 1, RegistryValueKind.DWord);
            SaveState("DisableActionCenter");
        }
        else
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", 0, RegistryValueKind.DWord);
            ClearState("DisableActionCenter");
        }
    }

    private static void SetDepNx(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableNX")) return;
            RunCommand("bcdedit", "/set NX AlwaysOff");
            SaveState("DisableNX");
        }
        else
        {
            RunCommand("bcdedit", "/set NX OptIn");
            ClearState("DisableNX");
        }
    }

    private static void SetClipboard(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableClipboardSvc")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\cbdhsvc", "Start", 2, RegistryValueKind.DWord);
            SaveState("EnableClipboardSvc");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\cbdhsvc", "Start", 4, RegistryValueKind.DWord);
            ClearState("EnableClipboardSvc");
        }
    }

    private static void SetBluetooth(bool disable)
    {
        string[] services =
        {
            "BthA4dp", "BthEnum", "BthHFEnum", "BthLEEnum", "BTHMODEM",
            "Microsoft_Bluetooth_AvrcpTransport", "BluetoothUserService",
            "BthAvctpSvc", "RFCOMM", "bthserv", "BTAGService",
            "BTHUSB", "BTHPORT", "BthMini", "HidBth"
        };
        int startVal = disable ? 4 : 3;
        if (disable && HasState("DisableBluetooth")) return;
        foreach (var svc in services)
            Registry.SetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{svc}", "Start", startVal, RegistryValueKind.DWord);
        if (disable) SaveState("DisableBluetooth"); else ClearState("DisableBluetooth");
    }

    private static void SetBootMenuPolicy(bool standard)
    {
        if (standard)
        {
            if (HasState("BootMenuPolicy")) return;
            RunCommand("bcdedit.exe", "/set bootmenupolicy Standard");
            SaveState("BootMenuPolicy");
        }
        else
        {
            RunCommand("bcdedit.exe", "/set bootmenupolicy legacy");
            ClearState("BootMenuPolicy");
        }
    }

    private static void SetVpn(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableVPN")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\IKEEXT", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WinHttpAutoProxySvc", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\RasMan", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SstpSvc", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\iphlpsvc", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NdisVirtualBus", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Eaphost", "Start", 4, RegistryValueKind.DWord);
            SaveState("DisableVPN");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\IKEEXT", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BFE", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WinHttpAutoProxySvc", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\RasMan", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SstpSvc", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\iphlpsvc", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NdisVirtualBus", "Start", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Eaphost", "Start", 3, RegistryValueKind.DWord);
            ClearState("DisableVPN");
        }
    }

    private static void SetNtfsEncryption(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableNTFSEncryption")) return;
            RunCommand("fsutil", "behavior set disableencryption 1");
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Policies", "NtfsDisableEncryption", 1, RegistryValueKind.DWord);
            SaveState("DisableNTFSEncryption");
        }
        else
        {
            RunCommand("fsutil", "behavior set disableencryption 0");
            Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Policies", true)
                ?.DeleteValue("NtfsDisableEncryption", throwOnMissingValue: false);
            ClearState("DisableNTFSEncryption");
        }
    }

    private static void SetFsoGamebar(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableFSO")) return;
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "ShowStartupPanel", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "GamePanelStartupTipIndex", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AllowAutoGameMode", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehavior", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_DSEBehavior", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BcastDVRUserService", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "__COMPAT_LAYER", "~ DISABLEDXMAXIMIZEDWINDOWEDMODE", RegistryValueKind.String);
            SaveState("DisableFSO");
        }
        else
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehaviorMode", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehavior", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_DSEBehavior", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BcastDVRUserService", "Start", 3, RegistryValueKind.DWord);
            Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", true)
                ?.DeleteValue("__COMPAT_LAYER", throwOnMissingValue: false);
            ClearState("DisableFSO");
        }
    }

    private static void SetNotifications(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableNotifications")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WpnService", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userNotificationListener",
                "Value", "Deny", RegistryValueKind.String);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications", "NoCloudApplicationNotification", 1, RegistryValueKind.DWord);
            SaveState("DisableNotifications");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WpnService", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userNotificationListener",
                "Value", "Allow", RegistryValueKind.String);
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications", true)
                ?.DeleteValue("ToastEnabled", throwOnMissingValue: false);
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications", true)
                ?.DeleteValue("NoCloudApplicationNotification", throwOnMissingValue: false);
            ClearState("DisableNotifications");
        }
    }

    private static void SetPrefetch(bool disable)
    {
        if (disable)
        {
            if (HasState("DisablePrefetch")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FontCache", "Start", 4, RegistryValueKind.DWord);
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", writable: true);
            key?.SetValue("EnablePrefetcher", 0, RegistryValueKind.DWord);
            SaveState("DisablePrefetch");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FontCache", "Start", 2, RegistryValueKind.DWord);
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", writable: true);
            key?.SetValue("EnablePrefetcher", 3, RegistryValueKind.DWord);
            ClearState("DisablePrefetch");
        }
    }

    private static void SetCdrom(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableCDROM")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\cdrom", "Start", 3, RegistryValueKind.DWord);
            // Re-register IMAPI2
            const string imapi2Key = @"SYSTEM\CurrentControlSet\Services\IMAPI2";
            using (var key = Registry.LocalMachine.CreateSubKey(imapi2Key, writable: true))
            {
                if (key != null)
                {
                    key.SetValue("Description", "@%SystemRoot%\\system32\\imapi2.dll,-2", RegistryValueKind.ExpandString);
                    key.SetValue("DisplayName", "@%SystemRoot%\\system32\\imapi2.dll,-1", RegistryValueKind.ExpandString);
                    key.SetValue("ErrorControl", 1, RegistryValueKind.DWord);
                    key.SetValue("ImagePath", "%SystemRoot%\\system32\\svchost.exe -k imapi", RegistryValueKind.ExpandString);
                    key.SetValue("ObjectName", "LocalSystem", RegistryValueKind.String);
                    key.SetValue("Start", 3, RegistryValueKind.DWord);
                    key.SetValue("Type", 32, RegistryValueKind.DWord);
                    using var param = key.CreateSubKey("Parameters");
                    param?.SetValue("ServiceDll", "%SystemRoot%\\system32\\imapi2.dll", RegistryValueKind.ExpandString);
                    param?.SetValue("ServiceDllUnloadOnStop", 1, RegistryValueKind.DWord);
                }
            }
            SaveState("EnableCDROM");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\cdrom", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\IMAPI2", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\IMAPI2FS", "Start", 4, RegistryValueKind.DWord);
            ClearState("EnableCDROM");
        }
    }

    private static void SetPrintSpooler(bool disable)
    {
        if (disable)
        {
            if (HasState("DisablePrintSpooler")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Spooler", "Start", 4, RegistryValueKind.DWord);
            SaveState("DisablePrintSpooler");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Spooler", "Start", 2, RegistryValueKind.DWord);
            ClearState("DisablePrintSpooler");
        }
    }

    private static void SetNoLazyMode(bool enable)
    {
        if (enable)
        {
            if (HasState("NoLazyMode")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NoLazyMode", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "AlwaysOn", 1, RegistryValueKind.DWord);
            SaveState("NoLazyMode");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NoLazyMode", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "AlwaysOn", 0, RegistryValueKind.DWord);
            ClearState("NoLazyMode");
        }
    }

    private static void SetAdminUac(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableAdminUAC")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AppInfo", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ValidateAdminCodeSignatures", 1, RegistryValueKind.DWord);
            SaveState("EnableAdminUAC");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AppInfo", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ValidateAdminCodeSignatures", 0, RegistryValueKind.DWord);
            ClearState("EnableAdminUAC");
        }
    }

    private static void SetVr(bool enable)
    {
        int val = enable ? 2 : 4;
        if (enable && HasState("EnableVR")) return;
        var services = new (string Svc, int EnableVal, int DisableVal)[]
        {
            ("KSecPkg", 0, 4), ("LanmanWorkstation", 2, 4), ("mrxsmb", 3, 4),
            ("mrxsmb20", 3, 4), ("rdbss", 1, 4), ("srv2", 2, 4),
            ("QwaveDrv", 3, 4), ("Qwave", 3, 4), ("FontCache", 2, 4)
        };
        foreach (var (svc, en, dis) in services)
            Registry.SetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\{svc}", "Start", enable ? en : dis, RegistryValueKind.DWord);
        if (enable)
        {
            RunCommand("DISM", "/Online /Enable-Feature /FeatureName:SmbDirect /NoRestart");
            SaveState("EnableVR");
        }
        else
        {
            RunCommand("DISM", "/Online /Disable-Feature /FeatureName:SmbDirect /NoRestart");
            ClearState("EnableVR");
        }
    }

    private static void SetUac(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableUAC")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AppInfo", "Start", 2, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1, RegistryValueKind.DWord);
            SaveState("EnableUAC");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AppInfo", "Start", 4, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0, RegistryValueKind.DWord);
            ClearState("EnableUAC");
        }
    }

    private void SetStartMenu(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableStartmenu")) return;
            using var searchKey = CreateRealHkcuSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search");
            searchKey?.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
            searchKey?.SetValue("SearchBoxTaskbarMode", 1, RegistryValueKind.DWord);
            using var classKey = CreateRealHkcuSubKey(@"Software\Classes\Software\Microsoft\Windows\CurrentVersion\Search");
            classKey?.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
            SaveState("DisableStartmenu");
        }
        else
        {
            using var searchKey = CreateRealHkcuSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search");
            searchKey?.SetValue("BingSearchEnabled", 1, RegistryValueKind.DWord);
            searchKey?.SetValue("SearchBoxTaskbarMode", 0, RegistryValueKind.DWord);
            using var classKey = CreateRealHkcuSubKey(@"Software\Classes\Software\Microsoft\Windows\CurrentVersion\Search");
            classKey?.SetValue("BingSearchEnabled", 1, RegistryValueKind.DWord);
            ClearState("DisableStartmenu");
        }
    }

    private static void SetHyperV(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableHyperV")) return;
            RunCommand("bcdedit", "/set hypervisorlaunchtype off");
            RunCommand("bcdedit", "/set vm no");
            RunCommand("bcdedit", "/set vsmlaunchtype Off");
            RunCommand("bcdedit", "/set loadoptions DISABLE-LSA-ISO,DISABLE-VBS");
            RunCommand("DISM", "/Online /Disable-Feature:Microsoft-Hyper-V-All /Quiet /NoRestart");
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "RequirePlatformSecurityFeatures", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "HypervisorEnforcedCodeIntegrity", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "HVCIMATRequired", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "LsaCfgFlags", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "ConfigureSystemGuardLaunch", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequireMicrosoftSignedBootChain", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, RegistryValueKind.DWord);
            SaveState("DisableHyperV");
        }
        else
        {
            RunCommand("bcdedit", "/set hypervisorlaunchtype auto");
            RunCommand("bcdedit", "/deletevalue vm");
            RunCommand("bcdedit", "/deletevalue loadoptions");
            RunCommand("DISM", "/Online /Enable-Feature:Microsoft-Hyper-V-All /Quiet /NoRestart");
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequireMicrosoftSignedBootChain", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequirePlatformSecurityFeatures", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 1, RegistryValueKind.DWord);
            ClearState("DisableHyperV");
        }
    }

    private static void SetVbs(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableVBS")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequirePlatformSecurityFeatures", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 1, RegistryValueKind.DWord);
            SaveState("EnableVBS");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequirePlatformSecurityFeatures", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, RegistryValueKind.DWord);
            ClearState("EnableVBS");
        }
    }

    private void SetWallpaperQuality(bool disable)
    {
        if (disable)
        {
            if (HasState("WallpaperQuality")) return;
            Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "JPEGImportQuality", 100, RegistryValueKind.DWord);
            SaveState("WallpaperQuality");
        }
        else
        {
            Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true)
                ?.DeleteValue("JPEGImportQuality", throwOnMissingValue: false);
            ClearState("WallpaperQuality");
        }
    }

    private static void SetMpo(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableMPO")) return;
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
            key?.SetValue("DisableOverlays", 1, RegistryValueKind.DWord);
            SaveState("DisableMPO");
        }
        else
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
            key?.DeleteValue("DisableOverlays", throwOnMissingValue: false);
            ClearState("DisableMPO");
        }
    }

    private void SetTransparency(bool enable)
    {
        if (enable)
        {
            if (HasState("TransparencyEffects")) return;
            using var key = CreateRealHkcuSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            key.SetValue("EnableTransparency", 1, RegistryValueKind.DWord);
            SaveState("TransparencyEffects");
        }
        else
        {
            using var key = CreateRealHkcuSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
            ClearState("TransparencyEffects");
        }
    }

    private static void SetLockScreen(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableLockScreen")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1, RegistryValueKind.DWord);
            SaveState("DisableLockScreen");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 0, RegistryValueKind.DWord);
            ClearState("DisableLockScreen");
        }
    }

    private static void SetAnimations(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableAnimations")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DWM", "DisallowAnimations", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM", "EnableAeroPeek", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop\WindowMetrics", "MinAnimate", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "UserPreferencesMask",
                new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
            SystemParametersInfo(0x0014u, 0u, IntPtr.Zero, 0x0003u);
            SaveState("DisableAnimations");
        }
        else
        {
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DWM", true)
                ?.DeleteValue("DisallowAnimations", throwOnMissingValue: false);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM", "EnableAeroPeek", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "UserPreferencesMask",
                new byte[] { 0x9E, 0x3E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
            SystemParametersInfo(0x0014u, 0u, IntPtr.Zero, 0x0003u);
            ClearState("DisableAnimations");
        }
    }

    private static void SetDcom(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableDCOM")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Ole", "EnableDCOM", "N", RegistryValueKind.String);
            SaveState("DisableDCOM");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Ole", "EnableDCOM", "Y", RegistryValueKind.String);
            ClearState("DisableDCOM");
        }
    }

    private static void SetNvmeTweaks(bool enable)
    {
        if (enable)
        {
            if (HasState("NVMETweaks")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\stornvme\Parameters\Device", "ContiguousMemoryFromAnyNode", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\stornvme\Parameters\Device", "LogSize", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\stornvme\Parameters\Device", "IdlePowerMode", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\stornvme\Parameters\Device", "DiagnosticFlags", 0, RegistryValueKind.DWord);
            SaveState("NVMETweaks");
        }
        else
        {
            var nvme = Registry.LocalMachine.OpenSubKey(@"SYSTEM\ControlSet001\Services\stornvme\Parameters\Device", true);
            if (nvme != null)
                foreach (var v in new[] { "ContiguousMemoryFromAnyNode", "LogSize", "IdlePowerMode", "DiagnosticFlags" })
                    nvme.DeleteValue(v, throwOnMissingValue: false);
            ClearState("NVMETweaks");
        }
    }

    private static void SetLargeSystemCache(bool enable)
    {
        if (enable)
        {
            if (HasState("EnableLargeSystemCache")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 1, RegistryValueKind.DWord);
            SaveState("EnableLargeSystemCache");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 0, RegistryValueKind.DWord);
            ClearState("EnableLargeSystemCache");
        }
    }

    private static void SetSystemProfile(bool enable)
    {
        const string games = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        const string proAudio = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio";
        const string audio = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio";

        if (enable)
        {
            if (HasState("SystemProfile")) return;
            Registry.SetValue(games, "Affinity", 0, RegistryValueKind.DWord);
            Registry.SetValue(games, "Background Only", "False", RegistryValueKind.String);
            Registry.SetValue(games, "Clock Rate", 2710, RegistryValueKind.DWord);
            Registry.SetValue(games, "GPU Priority", 8, RegistryValueKind.DWord);
            Registry.SetValue(games, "Priority", 8, RegistryValueKind.DWord);
            Registry.SetValue(games, "SFIO Priority", "High", RegistryValueKind.String);
            Registry.SetValue(games, "Scheduling Category", "High", RegistryValueKind.String);
            Registry.SetValue(proAudio, "Priority", 8, RegistryValueKind.DWord);
            Registry.SetValue(proAudio, "Scheduling Category", "Medium", RegistryValueKind.String);
            Registry.SetValue(audio, "Priority", 8, RegistryValueKind.DWord);
            SaveState("SystemProfile");
        }
        else
        {
            var lm = Registry.LocalMachine;
            foreach (var v in new[] { "Affinity", "Background Only", "Clock Rate", "GPU Priority", "Priority", "SFIO Priority", "Scheduling Category" })
                lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true)
                  ?.DeleteValue(v, throwOnMissingValue: false);
            lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio", true)
              ?.DeleteValue("Priority", throwOnMissingValue: false);
            lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio", true)
              ?.DeleteValue("Scheduling Category", throwOnMissingValue: false);
            lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio", true)
              ?.DeleteValue("Priority", throwOnMissingValue: false);
            ClearState("SystemProfile");
        }
    }

    private static readonly string _localRoot        = @"C:\PostInstall";
    private static readonly string _minSudoPath      = @"C:\PostInstall\Tweaks\MinSudo.exe";
    private static readonly string _powerRunPath     = @"C:\PostInstall\Tweaks\PowerRun.exe";
    private static readonly string _noDefenderPath   = @"C:\PostInstall\Defender\NoDefender.cab";
    private static readonly string _winNoDefenderCab = @"C:\Windows\NoDefender.cab";
 
    private static readonly string[] DefenderServices =
    {
        "MsSecCore", "MsSecFlt", "MsSecWfp", "SecurityHealthService",
        "Sense", "WdBoot", "WdFilter", "WdNisDrv", "WdNisSvc",
        "WinDefend", "wscsvc", "MDCoreSvc", "SgrmAgent", "SgrmBroker",
        "webthreatdefsvc", "webthreatdefusersvc",
    };
 
    private static readonly string[] DefenderScheduledTasks =
    {
        @"\Microsoft\Windows\Windows Defender\Windows Defender Cache Maintenance",
        @"\Microsoft\Windows\Windows Defender\Windows Defender Cleanup",
        @"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan",
        @"\Microsoft\Windows\Windows Defender\Windows Defender Verification",
    };
 
    private static void SetDefender(bool disable) => _ = SetDefenderAsync(disable);
 
    private static async Task SetDefenderAsync(bool disable)
    {
        static void Log(string msg) => App.Tool?.Log(msg);
 
        try
        {
            // Auto-download required files if missing (matches Akari Tool Premium).
            // On AkariOS, all files already exist and this returns instantly.
            // On a fresh VM / stock Windows, this fetches ~30MB from GitHub.
            bool filesReady = disable
                ? await PostInstallService.EnsureDefenderFilesAsync()
                : await PostInstallService.EnsureMinSudoAsync();
 
            if (!filesReady)
            {
                Log("[DEFENDER] Could not obtain required files. Check your internet connection and try again.");
                return;
            }
 
            if (disable)
            {
                if (HasState("DisableDefender")) return;
 
                Log("[DEFENDER] Disabling Windows Defender...");
                Log("[DEFENDER] Checking Tamper Protection status...");
 
                if (IsDefenderTamperProtectionOn())
                {
                    Log("[DEFENDER] ERROR: Tamper Protection is ON.");
                    Log("[DEFENDER] Go to: Windows Security → Virus & threat protection");
                    Log("[DEFENDER]   → Manage settings → Tamper Protection → Off");
                    Log("[DEFENDER] Then try again.");
                    return;
                }
 
                Log("[DEFENDER] Tamper Protection is off — proceeding.");
                Log("[DEFENDER] Preparing NoDefender package...");
                File.Copy(_noDefenderPath, _winNoDefenderCab, overwrite: true);
 
                Log("[DEFENDER] Installing NoDefender (30–60s)...");
                await DefenderRunElevatedPsFileAsync(
                    Path.Combine(_localRoot, @"Defender\DisableDefender.ps1"));
 
                Log("[DEFENDER] Scheduling post-reboot service cleanup...");
                await DefenderScheduleCleanup();
 
                SaveState("DisableDefender");
                Log("[DEFENDER] Phase 1 complete. Please restart now.");
                Log("[DEFENDER] On next login, Phase 2 will finish disabling Defender automatically.");
            }
            else
            {
                Log("[DEFENDER] Re-enabling Windows Defender...");
                Log("[DEFENDER] Restoring Defender package (30–60s)...");
 
                await DefenderRunElevatedPsAsync(
                    $"if (Test-Path '{_winNoDefenderCab}') " +
                    $"{{ Remove-WindowsPackage -Online -PackagePath '{_winNoDefenderCab}' -NoRestart }}");
 
                Log("[DEFENDER] Restoring Defender services...");
                await DefenderRunAsTrustedInstallerAsync(DefenderBuildServiceBat(startValue: 2));
 
                ClearState("DisableDefender");
                Log("[DEFENDER] Defender re-enabled. Restart required.");
            }
        }
        catch (Exception ex) { App.Tool?.Log($"[DEFENDER] ERROR: {ex.Message}"); }
    }
 
    private static async Task DefenderScheduleCleanup()
    {
        static void Log(string msg) => App.Tool?.Log(msg);
 
        var batPath  = Path.Combine(_localRoot, @"Defender\AkariDefenderCleanup.bat");
        var sysCmd   = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var powerRun = _powerRunPath;
 
        var lines = new List<string>
        {
            "@echo off",
            ":: AkariTool — Defender Phase 2 cleanup (runs once after reboot)",
            "",
            ":: Disable real-time monitoring first",
            @"PowerShell -NonInteractive -NoLogo -NoProfile -C ""Set-MpPreference -DisableRealtimeMonitoring 1"" >NUL 2>nul",
            "",
            ":: Kill all Defender service registry keys (ControlSet001)",
        };
 
        foreach (var cmd in DefenderBuildServiceBat(startValue: 4))
            lines.Add($@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c {cmd}");
 
        lines.AddRange(new[]
        {
            "",
            ":: Remove SecurityHealth from Run key",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe delete ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v ""SecurityHealth"" /f",
            "",
            ":: Disable SmartScreen binary",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c taskkill /f /im smartscreen.exe",
            $@"if not exist ""%systemroot%\system32\smartscreen.exe.old"" if exist ""%systemroot%\system32\smartscreen.exe"" (",
            $@"  ""{powerRun}"" /SW:0 ""{sysCmd}"" /c takeown /F ""%systemroot%\system32\smartscreen.exe"" /A",
            $@"  ""{powerRun}"" /SW:0 ""{sysCmd}"" /c icacls ""%systemroot%\system32\smartscreen.exe"" /grant Administrators:F",
            $@"  ""{powerRun}"" /SW:0 ""{sysCmd}"" /c copy ""%systemroot%\system32\smartscreen.exe"" ""%systemroot%\system32\smartscreen.exe.old"" /v",
            $@"  ""{powerRun}"" /SW:0 ""{sysCmd}"" /c del ""%systemroot%\system32\smartscreen.exe""",
            ")",
            "",
            ":: SmartScreen registry keys",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"" /v ""SmartScreenEnabled"" /t REG_SZ /d ""Off"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\Software\Policies\Microsoft\System"" /v ""EnableSmartScreen"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\Software\Policies\Microsoft\Windows Defender\SmartScreen"" /v ""ConfigureAppInstallControlEnabled"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\Software\Policies\Microsoft\Windows Defender\SmartScreen"" /v ""EnableSmartScreen"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost"" /v ""EnableWebContentEvaluation"" /t REG_DWORD /d ""0"" /f",
            "",
            ":: CI/Policy and DeviceGuard keys",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\SYSTEM\ControlSet001\Control\CI\Policy"" /v ""VerifiedAndReputablePolicyState"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\Software\Microsoft\Windows Defender"" /v ""PUAProtection"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\SYSTEM\ControlSet001\Control\CI\Config"" /v ""VulnerableDriverBlocklistEnable"" /t REG_DWORD /d ""0"" /f",
            $@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c Reg.exe add ""HKLM\SYSTEM\ControlSet001\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"" /v ""Enabled"" /t REG_DWORD /d ""0"" /f",
            "",
            ":: Disable Defender scheduled tasks",
        });
 
        foreach (var task in DefenderScheduledTasks)
            lines.Add($@"""{powerRun}"" /SW:0 ""{sysCmd}"" /c schtasks.exe /change /disable /TN ""{task}""");
 
        lines.AddRange(new[]
        {
            "",
            ":: Self-cleanup",
            $@"Reg.exe delete ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"" /v ""AkariDefenderCleanup"" /f >NUL 2>nul",
            $@"(del /f /q ""%~f0"") >NUL 2>nul",
        });
 
        await File.WriteAllLinesAsync(batPath, lines);
 
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Registry.SetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            "AkariDefenderCleanup",
            $"\"{cmdExe}\" /c \"{batPath}\"",
            RegistryValueKind.String);
 
        Log($"[DEFENDER] Phase 2 bat written to: {batPath}");
        Log("[DEFENDER] It will run automatically on next login.");
    }
 
    private static bool IsDefenderTamperProtectionOn()
    {
        try
        {
            var val = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Features",
                "TamperProtection", null);
            return val is not int i || i != 4;
        }
        catch { return true; }
    }
 
    private static string[] DefenderBuildServiceBat(int startValue) =>
        DefenderServices
            .Select(svc =>
                $@"Reg.exe add ""HKLM\SYSTEM\ControlSet001\Services\{svc}"" /v ""Start"" /t REG_DWORD /d ""{startValue}"" /f")
            .ToArray();
 
    private static async Task DefenderRunElevatedPsFileAsync(string ps1Path)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-ExecutionPolicy Bypass -NonInteractive -NoLogo -NoProfile -File \"{ps1Path}\"",
            UseShellExecute = true,
            Verb            = "runas",
            CreateNoWindow  = false
        };
        await Process.Start(psi)!.WaitForExitAsync();
    }
 
    private static async Task DefenderRunElevatedPsAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NonInteractive -NoLogo -NoProfile -C \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow  = true
        };
        await Process.Start(psi)!.WaitForExitAsync();
    }
 
    private static async Task DefenderRunAsTrustedInstallerAsync(IEnumerable<string> commands)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"AkariDef-{Guid.NewGuid():N}.bat");
        try
        {
            var lines = new List<string> { "@echo off" };
            lines.AddRange(commands);
            await File.WriteAllLinesAsync(tmp, lines);
 
            var psi = new ProcessStartInfo
            {
                FileName        = _minSudoPath,
                Arguments       = $"--NoLogo --TrustedInstaller --Privileged cmd /c \"{tmp}\"",
                UseShellExecute = true,
                CreateNoWindow  = false
            };
            await Process.Start(psi)!.WaitForExitAsync();
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static void SetProcessMitigations(bool enable)
    {
        const string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        if (enable)
        {
            if (HasState("EnableProcessMitigations")) return;
            Registry.SetValue(key, "FeatureSettingsOverride", 0, RegistryValueKind.DWord);
            Registry.SetValue(key, "FeatureSettingsOverrideMask", 3, RegistryValueKind.DWord);
            SaveState("EnableProcessMitigations");
        }
        else
        {
            Registry.SetValue(key, "FeatureSettingsOverride", 3, RegistryValueKind.DWord);
            Registry.SetValue(key, "FeatureSettingsOverrideMask", 3, RegistryValueKind.DWord);
            ClearState("EnableProcessMitigations");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GAMING TWEAKS
    // ═════════════════════════════════════════════════════════════════════════

    private static void SetPreemption(bool disable)
    {
        if (disable)
        {
            if (HasState("DisablePreemption")) return;
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "EnablePreemption", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisablePreemption", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisableCudaContextPreemption", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "EnableCEPreemption", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisablePreemptionOnS3S4", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "ComputePreemption", 0, RegistryValueKind.DWord);
            SaveState("DisablePreemption");
        }
        else
        {
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "EnablePreemption", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisablePreemption", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisableCudaContextPreemption", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "EnableCEPreemption", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisablePreemptionOnS3S4", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm", "ComputePreemption", 1, RegistryValueKind.DWord);
            ClearState("DisablePreemption");
        }
    }

    private static void SetHdcp(bool disable)
    {
        if (disable)
        {
            if (HasState("DisableHDCP")) return;
            RunCommand("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Disable HDCP.bat""");
            SaveState("DisableHDCP");
        }
        else
        {
            Registry.SetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001",
                "RMHdcpKeyglobZero", 0, RegistryValueKind.DWord);
            ClearState("DisableHDCP");
        }
    }

    private static void SetNetworkOptimization(bool enable)
    {
        if (enable)
        {
            if (HasState("NetworkOptimization")) return;
            RunCommand("cmd.exe", @"/c ""C:\PostInstall\Others\Network\Run this if you had to install a network driver.bat""");
            SaveState("NetworkOptimization");
        }
        else
        {
            RunCommand("cmd.exe", @"/c ""C:\PostInstall\Others\Network\Revert Network Tweaks.bat""");
            ClearState("NetworkOptimization");
        }
    }
}