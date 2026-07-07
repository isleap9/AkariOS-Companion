using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AkariOSCompanion.Models;
using AkariOSCompanion.Services;

namespace AkariOSCompanion.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly AppInstallerService _installer;

    /// <summary>Master list of every app.</summary>
    public ObservableCollection<AppItem> Apps { get; } = new();

    /// <summary>Filtered/searched view bound to the ItemsControl.</summary>
    public ICollectionView AppsView { get; }

    public IReadOnlyList<string> Categories { get; } =
        new[] { "All", "Browsers", "Comms", "Dev", "Gaming", "Utilities" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _statusText = "";

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();
    partial void OnSelectedCategoryChanged(string value) => AppsView.Refresh();

    public DownloadsViewModel(AppInstallerService installer)
    {
        _installer = installer;
        SeedApps();

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = Filter;

        foreach (var a in Apps)
            a.PropertyChanged += OnAppChanged;
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppItem.IsSelected))
            SelectedCount = Apps.Count(a => a.IsSelected);
    }

    private bool Filter(object obj)
    {
        if (obj is not AppItem a) return false;
        var matchesCat = SelectedCategory == "All" || a.Category == SelectedCategory;
        var q = SearchText?.Trim() ?? "";
        var matchesQuery = q.Length == 0
            || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
        return matchesCat && matchesQuery;
    }

    [RelayCommand]
    private void SelectCategory(string category) => SelectedCategory = category;

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var a in Apps) a.IsSelected = false;
    }

    [RelayCommand]
    private async Task InstallSelected()
    {
        var picked = Apps.Where(a => a.IsSelected).ToList();
        if (picked.Count == 0) return;

        var progress = new Progress<string>(msg => StatusText = msg);
        await _installer.InstallAsync(picked, progress);
    }

    private void SeedApps()
    {
        (string Name, string Desc, string Cat, string Pkg)[] defs =
        {
            ("Google Chrome",              "Fast browser with Google account sync",                     "Browsers",  "Google.Chrome"),
            ("Ungoogled Chromium",         "Chromium without any Google services or telemetry",         "Browsers",  "eloston.ungoogled-chromium"),
            ("Mozilla Firefox",            "Open-source browser with strong privacy defaults",          "Browsers",  "Mozilla.Firefox"),
            ("LibreWolf",                  "Privacy-hardened Firefox fork, no telemetry",               "Browsers",  "LibreWolf.LibreWolf"),
            ("Zen Browser",                "Beautifully designed, privacy-focused Firefox-based browser","Browsers", "Zen-Team.Zen-Browser"),
            ("Waterfox",                   "64-bit Firefox fork focused on speed and privacy",          "Browsers",  "Waterfox.Waterfox"),
            ("Brave",                      "Privacy-focused browser with built-in ad-blocking",         "Browsers",  "Brave.Brave"),
            ("Arc Browser",                "Innovative browser with built-in productivity tools",       "Browsers",  "TheBrowserCompany.Arc"),
            ("DuckDuckGo Privacy Browser", "Privacy-focused browser with built-in tracker blocking",    "Browsers",  "DuckDuckGo.DesktopBrowser"),
            ("Vivaldi",                    "Highly customisable browser that does not track you",       "Browsers",  "Vivaldi.Vivaldi"),
            ("Tor Browser",               "Anonymity browser routed through the Tor network",           "Browsers",  "TorProject.TorBrowser"),
            ("Discord",                    "Voice, video and text chat for communities",                "Comms",     "Discord.Discord"),
            ("TeamSpeak",                  "Low-latency voice communication for gamers",                "Comms",     "TeamSpeakSystems.TeamSpeakClient"),
            ("Telegram",                   "Fast, cloud-based secure messaging",                        "Comms",     "Telegram.TelegramDesktop"),
            ("Signal",                     "End-to-end encrypted private messenger",                    "Comms",     "OpenWhisperSystems.Signal"),
            ("Notepad++",                  "Lightweight text and code editor with syntax highlighting", "Dev",       "Notepad++.Notepad++"),
            ("Visual Studio Code",         "Full-featured code editor with extensions and debugging",   "Dev",       "Microsoft.VisualStudioCode"),
            ("Visual Studio Community",    "Full-featured IDE for .NET and C++ development",             "Dev",       "Microsoft.VisualStudio.2022.Community"),
            ("GitHub Desktop",            "Official GitHub client for Windows",                         "Dev",       "GitHub.GitHubDesktop"),
            ("Git",                        "Distributed version control system",                        "Dev",       "Git.Git"),
            ("Python 3.13",                "Dynamic programming language for rapid development",         "Dev",       "Python.Python.3.13"),
            ("Steam",                      "Popular game distribution platform with a large library",   "Gaming",    "Valve.Steam"),
            ("GOG Galaxy",                 "DRM-free game platform with cross-play features",            "Gaming",    "GOG.Galaxy"),
            ("Epic Games Launcher",        "Store and launcher for Epic titles",                        "Gaming",    "EpicGames.EpicGamesLauncher"),
            ("MSI Afterburner",            "GPU overclocking and monitoring utility",                   "Gaming",    "Guru3D.Afterburner"),
            ("7-Zip",                      "High-ratio file archiver and extractor",                    "Utilities", "7zip.7zip"),
            ("VLC Media Player",           "Plays virtually any audio or video format",                 "Utilities", "VideoLAN.VLC"),
            ("ShareX",                     "Powerful screen capture and screen recording tool",         "Utilities", "ShareX.ShareX"),
            ("PowerToys",                  "Windows system utilities for power users",                  "Utilities", "Microsoft.PowerToys"),
        };

        foreach (var d in defs)
            Apps.Add(new AppItem
            {
                Name = d.Name,
                Description = d.Desc,
                Category = d.Cat,
                PackageId = d.Pkg,
            });
    }
}
