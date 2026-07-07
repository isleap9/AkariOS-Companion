using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace AkariOSCompanion.Models;

/// <summary>A single installable application shown in the Downloads grid.</summary>
public partial class AppItem : ObservableObject
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string License { get; init; } = "Free";

    /// <summary>winget package id (or a download URL) — used by the installer service.</summary>
    public string PackageId { get; init; } = "";

    /// <summary>First letter, used for the monogram tile.</summary>
    public string Mono => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    /// <summary>Per-category accent brush (resolved by the view via a converter, or set here).</summary>
    public Brush Accent { get; set; } = Brushes.IndianRed;

    [ObservableProperty]
    private bool _isSelected;
}
