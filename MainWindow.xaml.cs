using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AkariOSCompanion.Helpers;
using AkariOSCompanion.Services;
using AkariOSCompanion.Views;
using Wpf.Ui.Controls;

namespace AkariOSCompanion;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // ── Wire ToolService to the status bar controls ───────────────────────
        // MUST happen after InitializeComponent() so TxtLog/LogProgress exist.
        App.Tool = new ToolService(TxtLog, LogProgress, TxtProgressStatus);

        // Register the Frame so pages can trigger navigation (e.g. Home cards)
        AppNavigation.Register(PageFrame);

        // Start on Home
        Loaded += (_, _) => PageFrame.Navigate(new HomePage());
    }

    // ── Nav RadioButton clicks ────────────────────────────────────────────────

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton btn) return;

        Page page = btn.Tag?.ToString() switch
        {
            "Home"          => new HomePage(),
            "Debloat"       => new DebloatPage(),
            "GamingTweaks"  => new GamingTweaksPage(),
            "AkariOSTweaks" => new AkariOSTweaksPage(),
            "Downloads"     => new DownloadsPage(),
            "Misc"          => new MiscPage(),
            _               => new HomePage()
        };

        PageFrame.Navigate(page);
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/isleap9/Akari-Tool",
            UseShellExecute = true
        });
    }
}