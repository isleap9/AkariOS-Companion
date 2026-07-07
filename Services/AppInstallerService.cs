using System.Diagnostics;
using AkariOSCompanion.Models;

namespace AkariOSCompanion.Services;

/// <summary>
/// Installs selected apps. STUB: shells out to winget per package id.
/// Swap for your own downloader/silent-installer pipeline as needed.
/// </summary>
public sealed class AppInstallerService
{
    public async Task InstallAsync(IEnumerable<AppItem> apps, IProgress<string>? progress = null)
    {
        foreach (var app in apps)
        {
            progress?.Report($"Installing {app.Name}…");

            if (string.IsNullOrWhiteSpace(app.PackageId))
            {
                Debug.WriteLine($"[Installer] No package id for {app.Name} — skipped.");
                continue;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"install --id {app.PackageId} --silent " +
                            "--accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc is not null)
                    await proc.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Installer] {app.Name} failed: {ex.Message}");
            }
        }

        progress?.Report("Done.");
    }
}
