using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AkariOSCompanion.Helpers;
using AkariOSCompanion.Views;

namespace AkariOSCompanion.ViewModels;

public sealed class HomeCard
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "";      // Segoe Fluent Icons glyph
    public Type Target { get; init; } = typeof(HomePage);
}

public partial class HomeViewModel : ObservableObject
{
    public IReadOnlyList<HomeCard> Cards { get; } = new[]
    {
        new HomeCard { Title = "Gaming Tweaks",   Description = "GPU, latency & service tuning for peak FPS",   Glyph = "\uE7FC", Target = typeof(AkariOSPage) },
        new HomeCard { Title = "Akari OS Tweaks", Description = "Toggle deep system modifications & services",  Glyph = "\uE713", Target = typeof(AkariOSTweaksPage) },
        new HomeCard { Title = "Downloads",       Description = "Playbooks, drivers & recommended utilities",   Glyph = "\uE896", Target = typeof(DownloadsPage) },
        new HomeCard { Title = "Misc",            Description = "Context-menu entries & extra tools",          Glyph = "\uE712", Target = typeof(MiscPage) },
    };

    [RelayCommand]
    private void Open(Type pageType) => AppNavigation.Navigate(pageType);
}
