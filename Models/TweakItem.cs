using CommunityToolkit.Mvvm.ComponentModel;

namespace AkariOSCompanion.Models;

/// <summary>A boolean system tweak rendered as a labelled ToggleSwitch.</summary>
public partial class TweakItem : ObservableObject
{
    /// <summary>Stable key the TweakService uses to read/write the underlying state.</summary>
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    [ObservableProperty]
    private bool _isOn;
}

/// <summary>A context-menu entry on the Misc page (Add / Remove actions).</summary>
public sealed class MiscItem
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
}
