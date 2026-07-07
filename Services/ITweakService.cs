using AkariOSCompanion.Models;

namespace AkariOSCompanion.Services;

/// <summary>
/// Abstraction over the actual system changes. The UI only flips
/// <see cref="TweakItem.IsOn"/>; the concrete implementation does the
/// registry / service / PowerShell work and reports current state back.
/// </summary>
public interface ITweakService
{
    /// <summary>Read the live system state for a tweak key (drives the initial toggle position).</summary>
    bool GetState(string key);

    /// <summary>Apply a tweak. Should be idempotent and safe to call repeatedly.</summary>
    void SetState(string key, bool enabled);
}
