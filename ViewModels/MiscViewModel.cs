using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AkariOSCompanion.Models;
using Microsoft.Win32;

namespace AkariOSCompanion.ViewModels;

public partial class MiscViewModel : ObservableObject
{
    public ObservableCollection<MiscItem> Items { get; } = new()
    {
        new() { Key = "cmd_admin",     Title = "Open CMD as Administrator",  Description = "Adds 'Open CMD As Administrator' to the right-click menu" },
        new() { Key = "ps_admin",      Title = "Open PowerShell as Admin",   Description = "Adds 'Open PowerShell As Administrator' to the right-click menu" },
        new() { Key = "take_own",      Title = "Take Ownership",             Description = "Adds 'Take Ownership' to the right-click menu" },
        new() { Key = "ctrl_panel",    Title = "Control Panel",              Description = "Adds 'Control Panel' shortcut to the desktop right-click menu" },
        new() { Key = "file_hash",     Title = "File Hash",                  Description = "Adds a 'Hash' submenu to file right-click for SHA/MD5 checksums" },
        new() { Key = "kill_nr",       Title = "Kill Not Responding",        Description = "Adds 'Kill not responding tasks' to the desktop right-click menu" },
        new() { Key = "win_tools",     Title = "Windows Tools",              Description = "Adds 'Windows Tools' shortcut to the desktop right-click menu" },
        new() { Key = "shutdown_menu", Title = "Shut Down Menu",             Description = "Adds a Shut Down/Restart submenu to the desktop right-click menu" },
        new() { Key = "pow_assoc",     Title = "Import Power Plan (.pow)",   Description = "Associates .pow files so double-clicking imports a power plan" },
        new() { Key = "run_priority",  Title = "Run with Priority",          Description = "Adds 'Run with priority' (Realtime/High) to .exe right-click" },
        new() { Key = "change_res",    Title = "Change Resolution",          Description = "Adds 'Change Resolution' to the desktop right-click menu" },
        new() { Key = "reboot_bios",   Title = "Reboot to BIOS",             Description = "Adds 'Reboot To BIOS' to the desktop right-click menu" },
    };

    [RelayCommand]
    private void Add(string key)
    {
        try
        {
            switch (key)
            {
                case "cmd_admin":     AddCmdAdmin();          break;
                case "ps_admin":      AddPsAdmin();           break;
                case "take_own":      AddTakeOwnership();     break;
                case "ctrl_panel":    AddControlPanel();      break;
                case "file_hash":     AddFileHash();          break;
                case "kill_nr":       AddKillNotResponding(); break;
                case "win_tools":     AddWindowsTools();      break;
                case "shutdown_menu": AddShutDownMenu();      break;
                case "pow_assoc":     AddPowerPlanAssoc();    break;
                case "run_priority":  AddRunWithPriority();   break;
                case "change_res":    AddChangeResolution();  break;
                case "reboot_bios":   AddRebootToBios();      break;
            }
            RestartExplorer();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Misc] Add({key}) error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Remove(string key)
    {
        try
        {
            switch (key)
            {
                case "cmd_admin":     RemoveCmdAdmin();          break;
                case "ps_admin":      RemovePsAdmin();           break;
                case "take_own":      RemoveTakeOwnership();     break;
                case "ctrl_panel":    RemoveControlPanel();      break;
                case "file_hash":     RemoveFileHash();          break;
                case "kill_nr":       RemoveKillNotResponding(); break;
                case "win_tools":     RemoveWindowsTools();      break;
                case "shutdown_menu": RemoveShutDownMenu();      break;
                case "pow_assoc":     RemovePowerPlanAssoc();    break;
                case "run_priority":  RemoveRunWithPriority();   break;
                case "change_res":    RemoveChangeResolution();  break;
                case "reboot_bios":   RemoveRebootToBios();      break;
            }
            RestartExplorer();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Misc] Remove({key}) error: {ex.Message}");
        }
    }

    // ── Explorer restart ──────────────────────────────────────────────────────

    private static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
            p.Kill();
        Process.Start("explorer.exe");
    }

    // ── Context menu Add implementations ─────────────────────────────────────

    private static void AddCmdAdmin()
    {
        foreach (var root in new[]
        {
            @"Directory\Shell\OpenElevatedCMD",
            @"Drive\Shell\OpenElevatedCMD",
            @"LibraryFolder\background\Shell\OpenElevatedCMD",
            @"Directory\Background\Shell\OpenElevatedCMD"
        })
        {
            using var key = Registry.ClassesRoot.CreateSubKey(root);
            key.SetValue("", "Open CMD As Administrator");
            key.SetValue("Icon", "imageres.dll,-5324");
            using var cmd = key.CreateSubKey("Command");
            cmd.SetValue("", @"Powershell.exe -windowstyle hidden -Command ""Start-Process cmd.exe -ArgumentList '/s,/k,pushd,%V' -Verb RunAs""");
        }
    }

    private static void AddPsAdmin()
    {
        foreach (var root in new[]
        {
            @"Directory\Shell\OpenElevatedPS",
            @"Drive\Shell\OpenElevatedPS",
            @"LibraryFolder\background\Shell\OpenElevatedPS",
            @"Directory\Background\Shell\OpenElevatedPS"
        })
        {
            using var key = Registry.ClassesRoot.CreateSubKey(root);
            key.SetValue("", "Open Powershell As Administrator");
            key.SetValue("Icon", "powershell.exe");
            using var cmd = key.CreateSubKey("Command");
            cmd.SetValue("", @"Powershell.exe -windowstyle hidden -Command ""Start-Process cmd.exe -ArgumentList '/s,/c,pushd %V && powershell' -Verb RunAs""");
        }
    }

    private static void AddTakeOwnership()
    {
        using var file = Registry.ClassesRoot.CreateSubKey(@"*\shell\TakeOwnership");
        file.SetValue("", "Take Ownership");
        file.SetValue("HasLUAShield", "");
        file.SetValue("NoWorkingDirectory", "");
        file.SetValue("NeverDefault", "");
        using var fileCmd = file.CreateSubKey("command");
        fileCmd.SetValue("", @"powershell.exe -windowstyle hidden -command ""Start-Process cmd -ArgumentList '/c takeown /f ""%1"" && icacls ""%1"" /grant *S-1-3-4:F /c /l & pause' -Verb runAs""");
        fileCmd.SetValue("IsolatedCommand", @"powershell.exe -windowstyle hidden -command ""Start-Process cmd -ArgumentList '/c takeown /f ""%1"" && icacls ""%1"" /grant *S-1-3-4:F /c /l & pause' -Verb runAs""");

        using var dir = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\TakeOwnership");
        dir.SetValue("", "Take Ownership");
        dir.SetValue("HasLUAShield", "");
        dir.SetValue("NoWorkingDirectory", "");
        dir.SetValue("NeverDefault", "");
        using var dirCmd = dir.CreateSubKey("command");
        dirCmd.SetValue("", @"powershell.exe -windowstyle hidden -command ""Start-Process cmd -ArgumentList '/c takeown /f ""%1""  /r /d y /skipsl && icacls ""%1"" /grant *S-1-3-4:F /t /c /l & pause' -Verb runAs""");
        dirCmd.SetValue("IsolatedCommand", @"powershell.exe -windowstyle hidden -command ""Start-Process cmd -ArgumentList '/c takeown /f ""%1"" /r /d y /skipsl && icacls ""%1"" /grant *S-1-3-4:F /t /c /l & pause' -Verb runAs""");
    }

    private static void AddControlPanel()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"DesktopBackground\shell\ControlPanel");
        key.SetValue("MUIVerb", "@shell32.dll,-4161");
        key.SetValue("Icon", "imageres.dll,-27");
        key.SetValue("Position", "Bottom");
        key.SetValue("SubCommands", "");
        using var sub1 = key.CreateSubKey(@"shell\1ControlPanelCmd");
        sub1.SetValue("MUIVerb", "@shell32.dll,-31061");
        sub1.SetValue("Icon", "imageres.dll,-27");
        using var cmd1 = sub1.CreateSubKey("Command");
        cmd1.SetValue("", @"explorer.exe shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}");
        using var sub2 = key.CreateSubKey(@"shell\2ControlPanelCmd");
        sub2.SetValue("MUIVerb", "@shell32.dll,-31062");
        using var cmd2 = sub2.CreateSubKey("Command");
        cmd2.SetValue("", @"explorer.exe shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}");
        using var sub3 = key.CreateSubKey(@"shell\3ControlPanelCmd");
        sub3.SetValue("MUIVerb", "@shell32.dll,-32537");
        sub3.SetValue("CommandFlags", 32, RegistryValueKind.DWord);
        using var cmd3 = sub3.CreateSubKey("Command");
        cmd3.SetValue("", @"explorer.exe shell:::{ED7BA470-8E54-465E-825C-99712043E01C}");
    }

    private static void AddFileHash()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"*\shell\Hash");
        key.SetValue("MUIVerb", "Hash");
        key.SetValue("SubCommands", "");
        var hashes = new (string Menu, string Algo)[]
        {
            ("01Menu", "SHA1"), ("02Menu", "SHA256"), ("03Menu", "SHA384"),
            ("04Menu", "SHA512"), ("05Menu", "MACTripleDES"), ("06Menu", "MD5"), ("07Menu", "RIPEMD160")
        };
        foreach (var (menu, algo) in hashes)
        {
            using var sub = key.CreateSubKey($@"shell\{menu}");
            sub.SetValue("MUIVerb", algo);
            using var cmd = sub.CreateSubKey("Command");
            cmd.SetValue("", $"powershell -noexit get-filehash -literalpath '%1' -algorithm {algo} | format-list");
        }
        using var all = key.CreateSubKey(@"shell\08Menu");
        all.SetValue("MUIVerb", "Show all");
        all.SetValue("CommandFlags", 0x32, RegistryValueKind.DWord);
        using var allCmd = all.CreateSubKey("Command");
        allCmd.SetValue("", "powershell -noexit get-filehash -literalpath '%1' -algorithm SHA1 | format-list;get-filehash -literalpath '%1' -algorithm SHA256 | format-list;get-filehash -literalpath '%1' -algorithm MD5 | format-list");
    }

    private static void AddKillNotResponding()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"DesktopBackground\shell\KillNotResponding");
        key.SetValue("MUIVerb", "Kill not responding tasks");
        key.SetValue("Icon", @"%SystemRoot%\System32\imageres.dll,-98");
        key.SetValue("Position", "Top");
        using var cmd = key.CreateSubKey("Command");
        cmd.SetValue("", @"cmd.exe /K taskkill.exe /F /FI ""status eq NOT RESPONDING""");
    }

    private static void AddWindowsTools()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"DesktopBackground\Shell\WindowsTools");
        key.SetValue("MUIVerb", "Windows Tools");
        key.SetValue("Icon", "imageres.dll,-114");
        key.SetValue("Position", "Bottom");
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", @"explorer.exe shell:::{D20EA4E1-3957-11d2-A40B-0C5020524153}");
    }

    private static void AddShutDownMenu()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"DesktopBackground\shell\ShutDown");
        key.SetValue("MUIVerb", "Shut Down");
        key.SetValue("Icon", "shell32.dll,-28");
        key.SetValue("Position", "Bottom");
        key.SetValue("SubCommands", "");
        using var s1 = key.CreateSubKey(@"shell\001ShutdownInstantly");
        s1.SetValue("MUIVerb", "Shut down instantly"); s1.SetValue("Icon", "shell32.dll,-28");
        using var c1 = s1.CreateSubKey("Command"); c1.SetValue("", "shutdown -s -f -t 0");
        using var s2 = key.CreateSubKey(@"shell\002ShutdownWarning");
        s2.SetValue("MUIVerb", "Shut down with warning"); s2.SetValue("Icon", "shell32.dll,-28");
        using var c2 = s2.CreateSubKey("Command"); c2.SetValue("", "shutdown -s");
        using var s3 = key.CreateSubKey(@"shell\003RestartInstantly");
        s3.SetValue("MUIVerb", "Restart instantly"); s3.SetValue("Icon", "shell32.dll,-16739");
        s3.SetValue("CommandFlags", 32, RegistryValueKind.DWord);
        using var c3 = s3.CreateSubKey("Command"); c3.SetValue("", "shutdown -r -f -t 0");
        using var s4 = key.CreateSubKey(@"shell\004RestartWarning");
        s4.SetValue("MUIVerb", "Restart with warning"); s4.SetValue("Icon", "shell32.dll,-16739");
        using var c4 = s4.CreateSubKey("Command"); c4.SetValue("", "shutdown -r");
    }

    private static void AddPowerPlanAssoc()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(".pow");
        using var icon = Registry.ClassesRoot.CreateSubKey(@".pow\DefaultIcon");
        icon.SetValue("", @"%SystemRoot%\System32\powercfg.cpl,-202");
        using var cmd = key.CreateSubKey(@"Shell\open\command");
        cmd.SetValue("", "powercfg /import %1");
    }

    private static void AddRunWithPriority()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"exefile\shell\Priority");
        key.SetValue("MUIVerb", "Run with priority");
        key.SetValue("SubCommands", "");
        using var rt = key.CreateSubKey(@"shell\001flyout");
        rt.SetValue("", "Realtime");
        using var rtCmd = rt.CreateSubKey("command");
        rtCmd.SetValue("", @"cmd /c start """" /Realtime ""%1""");
        using var hi = key.CreateSubKey(@"shell\002flyout");
        hi.SetValue("", "High");
        using var hiCmd = hi.CreateSubKey("command");
        hiCmd.SetValue("", @"cmd /c start """" /High ""%1""");
    }

    private static void AddChangeResolution()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"Directory\background\shell\Change Res");
        key.SetValue("MUIVerb", "Change Resolution");
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", @"C:\Windows\System32\rundll32.exe display.dll,ShowAdapterSettings 0");
    }

    private static void AddRebootToBios()
    {
        using var key = Registry.ClassesRoot.CreateSubKey(@"Directory\background\shell\reboot to fw");
        key.SetValue("MUIVerb", "Reboot To BIOS");
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", @"shutdown /r /fw /t 0");
    }

    // ── Context menu Remove implementations ──────────────────────────────────

    private static void RemoveCmdAdmin()
    {
        Registry.ClassesRoot.OpenSubKey(@"Directory\Background\Shell\", true)?.DeleteSubKeyTree("OpenElevatedCMD", throwOnMissingSubKey: false);
        Registry.ClassesRoot.OpenSubKey(@"Directory\Shell\", true)?.DeleteSubKeyTree("OpenElevatedCMD", throwOnMissingSubKey: false);
        Registry.ClassesRoot.OpenSubKey(@"Drive\Shell\", true)?.DeleteSubKeyTree("OpenElevatedCMD", throwOnMissingSubKey: false);
    }

    private static void RemovePsAdmin()
    {
        Registry.ClassesRoot.OpenSubKey(@"Directory\Background\Shell\", true)?.DeleteSubKeyTree("OpenElevatedPS", throwOnMissingSubKey: false);
        Registry.ClassesRoot.OpenSubKey(@"Directory\Shell\", true)?.DeleteSubKeyTree("OpenElevatedPS", throwOnMissingSubKey: false);
        Registry.ClassesRoot.OpenSubKey(@"Drive\Shell\", true)?.DeleteSubKeyTree("OpenElevatedPS", throwOnMissingSubKey: false);
    }

    private static void RemoveTakeOwnership()
    {
        Registry.ClassesRoot.OpenSubKey(@"*\shell\", true)?.DeleteSubKeyTree("TakeOwnership", throwOnMissingSubKey: false);
        Registry.ClassesRoot.OpenSubKey(@"Directory\shell", true)?.DeleteSubKeyTree("TakeOwnership", throwOnMissingSubKey: false);
    }

    private static void RemoveControlPanel()
    {
        Registry.ClassesRoot.OpenSubKey(@"DesktopBackground\shell\", true)?.DeleteSubKeyTree("ControlPanel", throwOnMissingSubKey: false);
    }

    private static void RemoveFileHash()
    {
        Registry.ClassesRoot.OpenSubKey(@"*\shell\", true)?.DeleteSubKeyTree("Hash", throwOnMissingSubKey: false);
    }

    private static void RemoveKillNotResponding()
    {
        Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\DesktopBackground\Shell", true)?.DeleteSubKeyTree("KillNotResponding", throwOnMissingSubKey: false);
    }

    private static void RemoveWindowsTools()
    {
        Registry.ClassesRoot.OpenSubKey(@"DesktopBackground\Shell", true)?.DeleteSubKeyTree("WindowsTools", throwOnMissingSubKey: false);
    }

    private static void RemoveShutDownMenu()
    {
        Registry.ClassesRoot.OpenSubKey(@"DesktopBackground\shell", true)?.DeleteSubKeyTree("ShutDown", throwOnMissingSubKey: false);
    }

    private static void RemovePowerPlanAssoc()
    {
        RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64).DeleteSubKeyTree(".pow", throwOnMissingSubKey: false);
    }

    private static void RemoveRunWithPriority()
    {
        RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64).DeleteSubKeyTree(@"exefile\shell\Priority", throwOnMissingSubKey: false);
    }

    private static void RemoveChangeResolution()
    {
        RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64).DeleteSubKeyTree(@"Directory\background\shell\Change Res", throwOnMissingSubKey: false);
    }

    private static void RemoveRebootToBios()
    {
        RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64).DeleteSubKeyTree(@"Directory\background\shell\reboot to fw", throwOnMissingSubKey: false);
    }
}