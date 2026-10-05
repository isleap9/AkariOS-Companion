using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AkariOSCompanion.Models;
using AkariOSCompanion.Services;
using Microsoft.Win32;

namespace AkariOSCompanion.ViewModels;

public partial class AkariOSViewModel : ObservableObject
{
    private readonly ITweakService _tweaks;

    public ObservableCollection<TweakItem> Recommended { get; }
    public IReadOnlyList<string> NvidiaButtons { get; }
    public IReadOnlyList<string> AmdButtons    { get; }
    public IReadOnlyList<string> UsefulToolsButtons { get; }

    // ── SvcHost split threshold ───────────────────────────────────────────────
    public IReadOnlyList<string> SvcHostOptions { get; } = new[]
        { "Default (380000 KB)", "4GB", "8GB", "16GB", "32GB", "64GB" };

    private static readonly long[] SvcHostValues = { 380000, 4194304, 8388608, 16777216, 33554432, 67108864 };

    [ObservableProperty] private int _selectedSvcHostIndex;

    partial void OnSelectedSvcHostIndexChanged(int value)
    {
        if (value < 0 || value >= SvcHostValues.Length) return;
        try
        {
            Registry.SetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control",
                "SvcHostSplitThresholdInKB",
                SvcHostValues[value],
                RegistryValueKind.DWord);

            // Persist the selected index so it survives restarts
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\AkariTool");
            key?.SetValue("SvcHostSplitThreshold", value, RegistryValueKind.DWord);
        }
        catch { }
    }

    // ── Win32 Priority Separation ─────────────────────────────────────────────
    public IReadOnlyList<string> Win32PriorityOptions { get; } = new[]
        { "2A (Hex)", "26 (Hex)", "28 (Hex)", "16 (Hex)", "06 (Hex)" };

    private static readonly int[] Win32PriorityValues = { 42, 38, 40, 22, 6 };

    [ObservableProperty] private int _selectedWin32PriorityIndex;

    partial void OnSelectedWin32PriorityIndexChanged(int value)
    {
        if (value < 0 || value >= Win32PriorityValues.Length) return;
        try
        {
            Registry.SetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl",
                "Win32PrioritySeparation",
                Win32PriorityValues[value],
                RegistryValueKind.DWord);

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\AkariTool");
            key?.SetValue("Win32PrioritySeparation", value, RegistryValueKind.DWord);
        }
        catch { }
    }

    // ── Services preset ───────────────────────────────────────────────────────
    public IReadOnlyList<string> ServicesOptions { get; } = new[]
        { "AkariOS (Default)", "Windows Default" };

    [ObservableProperty] private int _selectedServicesIndex;

    partial void OnSelectedServicesIndexChanged(int value)
    {
        var regFiles = new[] { "AkariOS-Default-services.reg", "Windows-Default-services.reg" };
        if (value < 0 || value >= regFiles.Length) return;

        // Persist index immediately regardless of whether the file exists
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\AkariTool");
            key?.SetValue("ServicesPreset", value, RegistryValueKind.DWord);
        }
        catch { }

        var regPath = Path.Combine(@"C:\PostInstall\Services", regFiles[value]);
        if (!File.Exists(regPath)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "regedit.exe",
                Arguments = $"/s \"{regPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }
        catch { }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AkariOSViewModel(ITweakService tweaks)
    {
        _tweaks = tweaks;

        Recommended = new ObservableCollection<TweakItem>
        {
            Make("preempt", "Disable Preemption (NVIDIA)", "Toggle preemption On or Off"),
            Make("hdcp",    "Disable HDCP",                "Toggle HDCP On or Off"),
            Make("netopt",  "Network Optimization",        "Optimize network settings"),
        };

        foreach (var t in Recommended)
            t.PropertyChanged += OnTweakChanged;

        NvidiaButtons = new[]
        {
            "NIP", "P-State 0", "Disable ECC", "Disable Telemetry",
            "Unrestrict Clock Policy", "NVCleanstall", "Miscellaneous"
        };
        AmdButtons = new[]
        {
            "DWORDS", "RSS", "Driver Download", "Disable DXNAVI",
            "Shader Cache AlwaysON", "Shader Cache Default"
        };
        UsefulToolsButtons = new[]
        {
            "Autoruns", "Devmanview", "Serviwin", "InSpectre",
            "MouseTester", "MinSudo", "CRU", "AUTO DSCP", "MSI Util",
            "DISM++", "Dev. Cleanup", "Interrupt AFPT",
            "HIDUSB", "MeasureSleep", "Process Explorer", "ReservedCPUSets"
        };

        // Restore saved dropdown positions
        _selectedSvcHostIndex       = ReadDropdownState("SvcHostSplitThreshold", 0);
        _selectedWin32PriorityIndex = ReadDropdownState("Win32PrioritySeparation", 0);
        _selectedServicesIndex      = ReadDropdownState("ServicesPreset", 0);
    }

    // ── NVIDIA button commands ────────────────────────────────────────────────

    [RelayCommand]
    private void NvidiaAction(string label)
    {
        switch (label)
        {
            case "NIP":
                RunFile(@"C:\PostInstall\GPU\Nvidia\NIP\nvidiaProfileInspector.exe",
                        @"/s C:\PostInstall\GPU\Nvidia\NIP\Settings.nip");
                break;
            case "P-State 0":
                RunFile("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!P-State 0.bat""");
                break;
            case "Disable ECC":
                RunFile("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!No ECC.bat""");
                break;
            case "Disable Telemetry":
                RunFile("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Disable telemetry (Breaks Geforce).bat""");
                break;
            case "Unrestrict Clock Policy":
                RunFile("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Unrestricted Clock Policy by Cancerogeno.bat""");
                break;
            case "NVCleanstall":
                RunFile(@"C:\PostInstall\GPU\Nvidia\NVCleanstall_1.18.0.exe", "");
                break;
            case "Miscellaneous":
                ApplyNvidiaMisc();
                break;
        }
    }

    [RelayCommand]
    private void AmdAction(string label)
    {
        switch (label)
        {
            case "DWORDS":
                RunFile("cmd.exe", @"/c ""C:\PostInstall\GPU\AMD\AMD Dwords by imribiy.bat""");
                break;
            case "RSS":
                RunFile(@"C:\PostInstall\GPU\AMD\radeon software slimmer\RadeonSoftwareSlimmer.exe", "");
                break;
            case "Driver Download":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://www.amd.com/en/support/download/drivers.html") { UseShellExecute = true });
                break;
            case "Disable DXNAVI":
                RunFile(@"C:\PostInstall\GPU\AMD\disable_dx11navi.exe", "");
                break;
            case "Shader Cache AlwaysON":
                ApplyAmdShaderCache(alwaysOn: true);
                break;
            case "Shader Cache Default":
                ApplyAmdShaderCache(alwaysOn: false);
                break;
        }
    }

    [RelayCommand]
    private void UsefulToolAction(string label)
    {
        var paths = new Dictionary<string, (string exe, string args)>
        {
            ["Autoruns"]        = (@"C:\PostInstall\Tweaks\Autoruns.exe", ""),
            ["Devmanview"]      = (@"C:\PostInstall\Tweaks\DevManView.exe", ""),
            ["Serviwin"]        = (@"C:\PostInstall\Tweaks\serviwin.exe", ""),
            ["InSpectre"]       = (@"C:\PostInstall\Mitigations\InSpectre.exe", ""),
            ["MouseTester"]     = (@"C:\PostInstall\Tweaks\Mouse Polling Test\MouseTester.exe", ""),
            ["MinSudo"]         = (@"C:\PostInstall\Tweaks\MinSudo.exe", ""),
            ["CRU"]             = (@"C:\PostInstall\Tweaks\CRU\CRU.exe", ""),
            ["AUTO DSCP"]       = ("cmd.exe", @"/c ""C:\PostInstall\Tweaks\Auto DSCP & FSE.bat"""),
            ["MSI Util"]        = (@"C:\PostInstall\Tweaks\MSI Mode Utility.exe", ""),
            ["DISM++"]          = (@"C:\Users\Administrator\Desktop\Dism++10.1.1002.1B\Dism++x64.exe", ""),
            ["Dev. Cleanup"]    = (@"C:\PostInstall\Tweaks\DeviceCleanup.exe", ""),
            ["Interrupt AFPT"]  = (@"C:\PostInstall\Tweaks\Interrupt Affinity Policy Tool.exe", ""),
            ["HIDUSB"]          = (@"C:\PostInstall\Tweaks\hidusbf\DRIVER\Setup.exe", ""),
            ["MeasureSleep"]    = (@"C:\PostInstall\Tweaks\MeasureSleep.exe", ""),
            ["Process Explorer"]= (@"C:\PostInstall\Tweaks\Process explorer\Process Explorer.exe", ""),
            ["ReservedCPUSets"] = (@"C:\PostInstall\Tweaks\ReservedCpuSets.exe", ""),
        };
        if (paths.TryGetValue(label, out var p))
            RunFile(p.exe, p.args);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TweakItem Make(string key, string title, string desc) => new()
    {
        Key = key,
        Title = title,
        Description = desc,
        IsOn = _tweaks.GetState(key)
    };

    private void OnTweakChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TweakItem t && e.PropertyName == nameof(TweakItem.IsOn))
            _tweaks.SetState(t.Key, t.IsOn);
    }

    private static void RunFile(string fileName, string arguments)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }
        catch { }
    }

    private static void ApplyNvidiaMisc()
    {
        try
        {
            const string gpuClass = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            Registry.SetValue(gpuClass, "RmDisableHwFaultBuffer", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "RMD3Feature", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "RMDisableGpuASPMFlags", 3, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "EnableRuntimePowerManagement", 0, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "DisableOverlay", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "D3PCLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "F1TransitionLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "Node3DLowLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "PreferSystemMemoryContiguous", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "TCCSupported", 0, RegistryValueKind.DWord);
            Registry.SetValue(gpuClass, "TrackResetEngine", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm\FTS", "EnableRID61684", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "DisplayPowerSaving", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "RmGpsPsEnablePerCpuCoreDpc", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "DisableWriteCombining", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "PlatformSupportMiracast", 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void ApplyAmdShaderCache(bool alwaysOn)
    {
        try
        {
            const string umdKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD";
            Registry.SetValue(umdKey, "ShaderCache",
                alwaysOn ? new byte[] { 0x32, 0x00 } : new byte[] { 0x31, 0x00 },
                RegistryValueKind.Binary);
        }
        catch { }
    }

    private static int ReadDropdownState(string regValue, int defaultIndex)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\AkariTool");
            if (key?.GetValue(regValue) is int idx) return idx;
        }
        catch { }
        return defaultIndex;
    }
}