using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkariOSCompanion.Services;
using Wpf.Ui.Appearance;

namespace AkariOSCompanion;

public partial class App : Application
{
    // ── Shared services (accessible from any page) ────────────────────────────
    public static ITweakService Tweaks { get; } = new TweakService();
    public static AppInstallerService Installer { get; } = new AppInstallerService();

    /// <summary>
    /// Set by MainWindow after InitializeComponent() so TxtLog/LogProgress exist.
    /// </summary>
    public static ToolService Tool { get; set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogCrash("UnhandledException", ex.ExceptionObject?.ToString());

        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("DispatcherUnhandledException", ex.Exception?.ToString());
            ex.Handled = true;
            MessageBox.Show(ex.Exception?.ToString(), "Startup error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);

        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            var crimson = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString("#D92D39");
            ApplicationAccentColorManager.Apply(crimson, ApplicationTheme.Dark);
        }
        catch (Exception ex) { LogCrash("Theme", ex.ToString()); }
    }

    private static void LogCrash(string tag, string? msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "AkariCrash.log"),
                $"[{DateTime.Now:HH:mm:ss}] {tag}\n{msg}\n\n");
        }
        catch { }
    }
}