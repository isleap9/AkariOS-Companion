using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AkariOSCompanion.Models;
using AkariOSCompanion.Services;

namespace AkariOSCompanion.ViewModels;

public partial class AkariOSTweaksViewModel : ObservableObject
{
    private readonly ITweakService _tweaks;

    public ObservableCollection<TweakItem> Tweaks { get; } = new();

    public AkariOSTweaksViewModel(ITweakService tweaks)
    {
        _tweaks = tweaks;

        var defs = new (string Key, string Title, string Desc)[]
        {
            ("wifi",            "Disable WiFi",                       "Toggle WiFi On or Off"),
            ("tsx",             "Enable Intel TSX",                   "Enable Intel Transactional Synchronization Extensions"),
            ("actioncenter",    "Disable Action Center",              "Toggle Action Center On or Off"),
            ("dep",             "Disable DEP/NX",                     "Toggle Data Execution Prevention"),
            ("clipboard",       "Enable Clipboard",                   "Toggle Clipboard service On or Off"),
            ("bluetooth",       "Disable Bluetooth",                  "Toggle Bluetooth On or Off"),
            ("bootmenu",        "BootMenuPolicy Standard",            "Set Boot Menu Policy to Standard"),
            ("vpn",             "Disable VPN",                        "Toggle VPN On or Off"),
            ("ntfsenc",         "Disable NTFS Encryption",            "Toggle NTFS Encryption On or Off"),
            ("fso",             "Disable FSO and Gamebar",            "Toggle FSO and Gamebar On or Off"),
            ("notifications",   "Disable Notifications",              "Toggle Notifications On or Off"),
            ("prefetch",        "Disable Prefetch",                   "Toggle Prefetch On or Off"),
            ("cdrom",           "CDROM",                              "Enable the CDROM service"),
            ("spooler",         "Disable Print Spooler",              "Toggle Print Spooler On or Off"),
            ("nolazy",          "NoLazyMode",                         "Disable MMCSS lazy mode"),
            ("uacadmin",        "UAC For Admin Account",              "Configure UAC for admin accounts"),
            ("vr",              "VR",                                 "Enable VR Services"),
            ("uac",             "User Account Control",               "Configure User Account Control settings"),
            ("startmenu",       "Disable Startmenu",                  "Toggle Start Menu search/Bing On or Off"),
            ("hyperv",          "Disable Hyper-V",                    "Toggle Hyper-V On or Off"),
            ("vbs",             "Enable VBS",                         "Toggle Virtualization Based Security"),
            ("wallpaperq",      "Disable Wallpaper Quality Reduction","Prevent wallpaper quality reduction"),
            ("mpo",             "Disable Multi-Plane-Overlay",        "Toggle MPO On or Off"),
            ("transparency",    "Transparency Effects",               "Toggle transparency effects"),
            ("lockscreen",      "Disable Lock Screen",                "Toggle lock screen On or Off"),
            ("animations",      "Disable Animations",                 "Toggle system animations"),
            ("dcom",            "Disable DCOM",                       "Toggle DCOM On or Off"),
            ("nvme",            "NVME Tweaks",                        "Apply NVME performance tweaks"),
            ("largecache",      "LargeSystemCache",                   "Configure large system cache"),
            ("sysprofile",      "System Profile Tweaks",              "Apply various system profile tweaks"),
            ("defender",        "Disable Defender",                   "Toggle Windows Defender On or Off"),
            ("mitigation",      "Enable Process Mitigation",          "Enable process mitigation policies"),
        };

        foreach (var (key, title, desc) in defs)
        {
            var item = new TweakItem { Key = key, Title = title, Description = desc, IsOn = _tweaks.GetState(key) };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakItem.IsOn))
                    _tweaks.SetState(item.Key, item.IsOn);
            };
            Tweaks.Add(item);
        }
    }
}
