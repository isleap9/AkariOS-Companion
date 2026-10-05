using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkariOSCompanion.Services;
using Microsoft.Win32;
using WpfUi = Wpf.Ui.Controls;

namespace AkariOSCompanion.Views;

/// <summary>
/// Gaming Tweaks page — ported 1:1 from the old Akari Tool Companion.
/// All UI is built in code-behind using the same helpers as the original.
/// </summary>
public partial class AkariOSPage
{
    private static ToolService Svc => App.Tool;

    // Persisted toggle state key
    private static readonly RegistryKey AkariKey =
        Registry.CurrentUser.CreateSubKey(@"Software\AkariTool",
            RegistryKeyPermissionCheck.ReadWriteSubTree);

    // Maps toggle title → visual setter (used to restore state on load)
    private readonly Dictionary<string, Action<bool>> _toggleSetters = new();

    public AkariOSPage()
    {
        InitializeComponent();
        Build();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BUILD
    // ═════════════════════════════════════════════════════════════════════════

    private void Build()
    {
        // Caption
        RootPanel.Children.Add(new TextBlock
        {
            FontSize = 13,
            Foreground = Brush("#9E9E9E"),
            Margin = new Thickness(0, 0, 0, 18)
        });

        // Services dropdown card
        var servicesCard = CreateCard(null);
        AddServicesDropdown(servicesCard);

        // Section header
        AddGroupHeader("Gaming Tweaks");

        // Recommended toggles card
        var recommended = CreateCard("Recommended");
        AddToggleRow(recommended, "Disable Preemption (NVIDIA)", "Toggle preemption On or Off", SetPreemption);
        AddToggleRow(recommended, "Disable HDCP",                "Toggle HDCP On or Off",         SetHdcp);
        AddToggleRow(recommended, "Network Optimization",        "Optimize network settings",      SetNetworkOptimization);

        // Miscellaneous dropdowns card
        var misc = CreateCard("Miscellaneous");
        AddSvcHostDropdown(misc);
        AddSeparator(misc);
        AddWin32PriorityDropdown(misc);

        // NVIDIA
        AddGroupHeader("NVIDIA");
        var nvidiaCard = CreateCard(null);
        AddButtonGrid(nvidiaCard, new (string, Action)[]
        {
            ("NIP",                    () => RunShell(@"C:\PostInstall\GPU\Nvidia\NIP\nvidiaProfileInspector.exe", @"/s C:\PostInstall\GPU\Nvidia\NIP\Settings.nip")),
            ("P-State 0",              () => RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!P-State 0.bat""")),
            ("Disable ECC",            () => RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!No ECC.bat""")),
            ("Disable Telemetry",      () => RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Disable telemetry (Breaks Geforce).bat""")),
            ("Unrestrict Clock Policy",() => RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Unrestricted Clock Policy by Cancerogeno.bat""")),
            ("NVCleanstall",           () => RunShell(@"C:\PostInstall\GPU\Nvidia\NVCleanstall_1.18.0.exe", "")),
            ("Miscellaneous",          ApplyNvidiaMisc)
        });

        // AMD
        AddGroupHeader("AMD");
        var amdCard = CreateCard(null);
        AddButtonGrid(amdCard, new (string, Action)[]
        {
            ("DWORDS",                () => RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\AMD\AMD Dwords by imribiy.bat""")),
            ("RSS",                   () => RunShell(@"C:\PostInstall\GPU\AMD\radeon software slimmer\RadeonSoftwareSlimmer.exe", "")),
            ("Driver Download",       () => Process.Start(new ProcessStartInfo("https://www.amd.com/en/support/download/drivers.html") { UseShellExecute = true })),
            ("Disable DXNAVI",        () => RunShell(@"C:\PostInstall\GPU\AMD\disable_dx11navi.exe", "")),
            ("Shader Cache AlwaysON", () => ApplyAmdShaderCache(true)),
            ("Shader Cache Default",  () => ApplyAmdShaderCache(false))
        });

        // Useful Tools
        AddGroupHeader("Useful Tools");
        var toolsCard = CreateCard(null);
        AddButtonGrid(toolsCard, new (string, Action)[]
        {
            ("Autoruns",         () => RunShell(@"C:\PostInstall\Tweaks\Autoruns.exe", "")),
            ("Devmanview",       () => RunShell(@"C:\PostInstall\Tweaks\DevManView.exe", "")),
            ("Serviwin",         () => RunShell(@"C:\PostInstall\Tweaks\serviwin.exe", "")),
            ("InSpectre",        () => RunShell(@"C:\PostInstall\Mitigations\InSpectre.exe", "")),
            ("MouseTester",      () => RunShell(@"C:\PostInstall\Tweaks\Mouse Polling Test\MouseTester.exe", "")),
            ("MinSudo",          () => RunShell(@"C:\PostInstall\Tweaks\MinSudo.exe", "")),
            ("CRU",              () => RunShell(@"C:\PostInstall\Tweaks\CRU\CRU.exe", "")),
            ("AUTO DSCP",        () => RunShell("cmd.exe", @"/c ""C:\PostInstall\Tweaks\Auto DSCP & FSE.bat""")),
            ("MSI Util",         () => RunShell(@"C:\PostInstall\Tweaks\MSI Mode Utility.exe", "")),
            ("DISM++",           () => RunShell(@"C:\Users\Administrator\Desktop\Dism++10.1.1002.1B\Dism++x64.exe", "")),
            ("Dev. Cleanup",     () => RunShell(@"C:\PostInstall\Tweaks\DeviceCleanup.exe", "")),
            ("Interrupt AFPT",   () => RunShell(@"C:\PostInstall\Tweaks\Interrupt Affinity Policy Tool.exe", "")),
            ("HIDUSB",           () => RunShell(@"C:\PostInstall\Tweaks\hidusbf\DRIVER\Setup.exe", "")),
            ("MeasureSleep",     () => RunShell(@"C:\PostInstall\Tweaks\MeasureSleep.exe", "")),
            ("Process Explorer", () => RunShell(@"C:\PostInstall\Tweaks\Process explorer\Process Explorer.exe", "")),
            ("ReservedCPUSets",  () => RunShell(@"C:\PostInstall\Tweaks\ReservedCpuSets.exe", ""))
        });

        // Restore saved toggle states
        ReadGamingSettings();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STATE RESTORE
    // ═════════════════════════════════════════════════════════════════════════

    private void ReadGamingSettings()
    {
        var map = new Dictionary<string, string>
        {
            ["Disable Preemption (NVIDIA)"] = "DisablePreemption",
            ["Disable HDCP"]                = "DisableHDCP",
            ["Network Optimization"]        = "NetworkOptimization",
        };
        foreach (var (title, regKey) in map)
            if (AkariKey.GetValue(regKey) != null && _toggleSetters.TryGetValue(title, out var setter))
                setter(true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UI HELPERS  (copied from TweakUIHelpers.cs)
    // ═════════════════════════════════════════════════════════════════════════

    private StackPanel CreateCard(string? title)
    {
        var card = new Border
        {
            Background      = Brush("#8C1E1E1E"),
            BorderBrush     = Brush("#26FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Margin          = new Thickness(0, 0, 0, 12)
        };

        var outer = new StackPanel();

        if (title != null)
        {
            var header = new Border
            {
                Background   = Brush("#11343434"),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Padding      = new Thickness(20, 12, 20, 12)
            };
            header.Child = new TextBlock
            {
                Text       = title,
                FontSize   = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#F0EAEB")
            };
            outer.Children.Add(header);
        }

        var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        outer.Children.Add(content);
        card.Child = outer;
        RootPanel.Children.Add(card);
        return content;
    }

    private void AddGroupHeader(string text)
    {
        RootPanel.Children.Add(new TextBlock
        {
            Text       = text,
            FontSize   = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#323434"),
            Margin     = new Thickness(0, 20, 0, 8)
        });
    }

    private void AddToggleRow(StackPanel parent, string title, string desc, Action<bool> onToggle)
    {
        if (parent.Children.Count > 0)
            parent.Children.Add(new Separator
            {
                Background = Brush("#1AFFFFFF"),
                Height     = 1,
                Margin     = new Thickness(-16, 0, -16, 0)
            });

        var row = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        Grid.SetColumn(info, 0);
        info.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F0EAEB") });
        info.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Brush("#9E9E9E"), Margin = new Thickness(0, 2, 0, 0) });

        var (toggleEl, setter) = BuildToggle(onToggle);
        Grid.SetColumn(toggleEl, 1);

        row.Children.Add(info);
        row.Children.Add(toggleEl);
        parent.Children.Add(row);

        _toggleSetters[title] = setter;
    }

    private static void AddSeparator(StackPanel parent) =>
        parent.Children.Add(new Separator { Background = Brush("#1AFFFFFF"), Height = 1, Margin = new Thickness(-16, 0, -16, 0) });

    private static (FrameworkElement el, Action<bool> setter) BuildToggle(Action<bool> onToggle)
    {
        var toggle = new WpfUi.ToggleSwitch
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(12, 0, 0, 0),
            IsChecked         = false
        };

        toggle.Click += (_, _) =>
        {
            bool isOn = toggle.IsChecked == true;
            onToggle(isOn);
        };

        Action<bool> setter = state =>
        {
            if (toggle.IsChecked == state) return;
            toggle.IsChecked = state;
            onToggle(state);
        };

        return (toggle, setter);
    }

    private void AddButtonGrid(StackPanel parent, (string Label, Action Action)[] buttons)
    {
        const int cols = 3;
        int rows = (int)Math.Ceiling(buttons.Length / (double)cols);
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (int c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < buttons.Length; i++)
        {
            var (label, action) = buttons[i];
            var el = MakeButtonBorder(label, action);
            Grid.SetColumn(el, i % cols);
            Grid.SetRow(el, i / cols);
            grid.Children.Add(el);
        }
        parent.Children.Add(grid);
    }

    private static Border MakeButtonBorder(string label, Action action)
    {
        var text = new TextBlock
        {
            Text                = label,
            Foreground          = Brush("#CFCFCF"),
            FontSize            = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var border = new Border
        {
            Background      = Brush("#8C242424"),
            BorderBrush     = Brush("#26FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(7),
            Height          = 44,
            Margin          = new Thickness(4),
            Cursor          = Cursors.Hand,
            Child           = text
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background  = Brush("#2832343A");
            border.BorderBrush = Brush("#FF323434");
            text.Foreground    = Brush("#F0EAEB");
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background  = Brush("#8C242424");
            border.BorderBrush = Brush("#26FFFFFF");
            text.Foreground    = Brush("#CFCFCF");
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { App.Tool?.Log($"[ERROR] {label}: {ex.Message}"); }
        };

        return border;
    }


    private void AddServicesDropdown(StackPanel parent)
    {
        var options  = new[] { "AkariOS (Default)", "Windows Default" };
        var regFiles = new[] { "AkariOS-Default-services.reg", "Windows-Default-services.reg" };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        Grid.SetColumn(info, 0);
        info.Children.Add(new TextBlock { Text = "Services", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F0EAEB") });
        info.Children.Add(new TextBlock { Text = "Select service configuration", FontSize = 12, Foreground = Brush("#9E9E9E"), Margin = new Thickness(0, 2, 0, 0) });

        var dropdown = new ComboBox { Width = 220, VerticalAlignment = VerticalAlignment.Center };
        foreach (var opt in options) dropdown.Items.Add(opt);

        // Restore saved index
        int savedIdx = 0;
        try { if (AkariKey.GetValue("ServicesPreset") is int idx) savedIdx = idx; } catch { }
        dropdown.SelectedIndex = savedIdx;

        bool init = false;
        dropdown.Loaded += (_, _) => init = true;
        dropdown.SelectionChanged += async (_, _) =>
        {
            if (!init) return;
            int i = dropdown.SelectedIndex;
            if (i < 0 || i >= regFiles.Length) return;
            try { AkariKey.SetValue("ServicesPreset", i, RegistryValueKind.DWord); } catch { }
            string regPath  = Path.Combine(@"C:\PostInstall\Services", regFiles[i]);
            string enableBat = Path.Combine(@"C:\PostInstall\Services", "exes enable.bat");
            string minSudo  = @"C:\PostInstall\Tweaks\MinSudo.exe";
            if (!File.Exists(regPath)) { Svc.Log($"[SERVICES] File not found: {regPath}"); return; }
            Svc.Log($"[SERVICES] Applying: {options[i]}");
            string tempBat = Path.Combine(Path.GetTempPath(), "akari_services.bat");
            File.WriteAllText(tempBat, $"@echo off\r\ncall \"{enableBat}\"\r\nregedit /s \"{regPath}\"\r\n");
            await Svc.RunProcess(minSudo, $"--System --Privileged --NoLogo cmd /c \"{tempBat}\"", timeout: null);
            try { File.Delete(tempBat); } catch { }
        };

        Grid.SetColumn(dropdown, 1);
        row.Children.Add(info);
        row.Children.Add(dropdown);
        parent.Children.Add(row);
    }

    private void AddSvcHostDropdown(StackPanel parent)
    {
        var options = new[] { "4GB", "8GB", "16GB", "32GB", "64GB", "Default (380000 KB)" };
        var values  = new long[] { 4194304, 8388608, 16777216, 33554432, 67108864, 380000 };

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(new TextBlock { Text = "SvcHostSplitThreshold", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F0EAEB") });
        row.Children.Add(new TextBlock { Text = "Set SvcHost split threshold", FontSize = 12, Foreground = Brush("#9E9E9E"), Margin = new Thickness(0, 2, 0, 8) });

        var dropdown = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, Width = 280 };
        foreach (var opt in options) dropdown.Items.Add(opt);

        int savedIdx = 5; // default
        try { if (AkariKey.GetValue("SvcHostSplitThreshold") is int idx && idx >= 0 && idx < options.Length) savedIdx = idx; } catch { }
        dropdown.SelectedIndex = savedIdx;

        bool init = false;
        dropdown.Loaded += (_, _) => init = true;
        dropdown.SelectionChanged += (_, _) =>
        {
            if (!init) return;
            int i = dropdown.SelectedIndex;
            if (i < 0 || i >= values.Length) return;
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", values[i], RegistryValueKind.DWord);
                AkariKey.SetValue("SvcHostSplitThreshold", i, RegistryValueKind.DWord);
                Svc.Log($"[SVCHOST] Set to {options[i]}. Restart to apply.");
            }
            catch (Exception ex) { Svc.Log($"[ERROR] SvcHost: {ex.Message}"); }
        };

        row.Children.Add(dropdown);
        parent.Children.Add(row);
    }

    private void AddWin32PriorityDropdown(StackPanel parent)
    {
        var options = new[] { "2A (Hex)", "26 (Hex)", "28 (Hex)", "16 (Hex)", "06 (Hex)" };
        var values  = new int[] { 42, 38, 40, 22, 6 };

        var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Win32 Priority Separation", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F0EAEB") });
        row.Children.Add(new TextBlock { Text = "Set Win32 priority separation (Hex) — AkariOS default is 2A", FontSize = 12, Foreground = Brush("#9E9E9E"), Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });

        var dropdown = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, Width = 280 };
        foreach (var opt in options) dropdown.Items.Add(opt);

        int savedIdx = 0;
        try { if (AkariKey.GetValue("Win32PrioritySeparation") is int idx && idx >= 0 && idx < options.Length) savedIdx = idx; } catch { }
        dropdown.SelectedIndex = savedIdx;

        bool init = false;
        dropdown.Loaded += (_, _) => init = true;
        dropdown.SelectionChanged += (_, _) =>
        {
            if (!init) return;
            int i = dropdown.SelectedIndex;
            if (i < 0 || i >= values.Length) return;
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", values[i], RegistryValueKind.DWord);
                AkariKey.SetValue("Win32PrioritySeparation", i, RegistryValueKind.DWord);
                Svc.Log($"[WIN32] Priority set to {options[i]}.");
            }
            catch (Exception ex) { Svc.Log($"[ERROR] Win32Priority: {ex.Message}"); }
        };

        row.Children.Add(dropdown);
        parent.Children.Add(row);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TOGGLE IMPLEMENTATIONS  (1:1 from AkariOS.cs)
    // ═════════════════════════════════════════════════════════════════════════

    private static void SaveState(string key)   => AkariKey.SetValue(key, 1);
    private static void ClearState(string key)  => AkariKey.DeleteValue(key, throwOnMissingValue: false);
    private static bool HasState(string key)    => AkariKey.GetValue(key) != null;

    private void SetPreemption(bool disable)
    {
        try
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
            Svc.Log($"[PREEMPTION] {(disable ? "Disabled" : "Enabled")}. Restart to apply.");
        }
        catch (Exception ex) { Svc.Log($"[ERROR] SetPreemption: {ex.Message}"); }
    }

    private void SetHdcp(bool disable)
    {
        try
        {
            if (disable)
            {
                if (HasState("DisableHDCP")) return;
                RunShell("cmd.exe", @"/c ""C:\PostInstall\GPU\Nvidia\!Disable HDCP.bat""");
                SaveState("DisableHDCP");
            }
            else
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001", "RMHdcpKeyglobZero", 0, RegistryValueKind.DWord);
                ClearState("DisableHDCP");
            }
            Svc.Log($"[HDCP] {(disable ? "Disabled" : "Enabled")}. Restart to apply.");
        }
        catch (Exception ex) { Svc.Log($"[ERROR] SetHdcp: {ex.Message}"); }
    }

    private void SetNetworkOptimization(bool enable)
    {
        try
        {
            if (enable)
            {
                if (HasState("NetworkOptimization")) return;
                RunShell("cmd.exe", @"/c ""C:\PostInstall\Others\Network\Run this if you had to install a network driver.bat""");
                SaveState("NetworkOptimization");
            }
            else
            {
                RunShell("cmd.exe", @"/c ""C:\PostInstall\Others\Network\Revert Network Tweaks.bat""");
                ClearState("NetworkOptimization");
            }
            Svc.Log($"[NETWORK] Optimization {(enable ? "applied" : "reverted")}.");
        }
        catch (Exception ex) { Svc.Log($"[ERROR] SetNetworkOptimization: {ex.Message}"); }
    }

    private void ApplyNvidiaMisc()
    {
        try
        {
            const string gpu = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            Registry.SetValue(gpu, "RmDisableHwFaultBuffer", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMD3Feature", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMDisableGpuASPMFlags", 3, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMBlcg", 286331153, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMElcg", 1431655765, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMElpg", 4095, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMFspg", 15, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "RMSlcg", 262143, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "EnableRuntimePowerManagement", 0, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "DisableOverlay", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "D3PCLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "F1TransitionLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "Node3DLowLatency", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "PreferSystemMemoryContiguous", 1, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "TCCSupported", 0, RegistryValueKind.DWord);
            Registry.SetValue(gpu, "TrackResetEngine", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm\FTS", "EnableRID61684", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "DisplayPowerSaving", 0, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "RmGpsPsEnablePerCpuCoreDpc", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\nvlddmkm", "DisableWriteCombining", 1, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "PlatformSupportMiracast", 0, RegistryValueKind.DWord);
            Svc.Log("[NVIDIA MISC] Applied. Restart to apply.");
        }
        catch (Exception ex) { Svc.Log($"[ERROR] NvidiaMisc: {ex.Message}"); }
    }

    private void ApplyAmdShaderCache(bool alwaysOn)
    {
        try
        {
            Registry.SetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD",
                "ShaderCache",
                alwaysOn ? new byte[] { 0x32, 0x00 } : new byte[] { 0x31, 0x00 },
                RegistryValueKind.Binary);
            Svc.Log($"[AMD] Shader Cache set to {(alwaysOn ? "AlwaysON" : "Default")}.");
        }
        catch (Exception ex) { Svc.Log($"[ERROR] AmdShaderCache: {ex.Message}"); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PROCESS HELPER
    // ═════════════════════════════════════════════════════════════════════════

    private static void RunShell(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                UseShellExecute = true,
                CreateNoWindow  = false
            });
            p?.WaitForExit();
        }
        catch { /* best-effort */ }
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}