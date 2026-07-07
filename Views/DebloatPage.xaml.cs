using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkariOSCompanion.Services;

namespace AkariOSCompanion.Views;

public partial class DebloatPage : Page
{
    // App static service reference — set by App.xaml.cs
    private static ToolService Service => App.Tool;
    private readonly List<string> _applied = [];

    public DebloatPage()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        // ── Page heading ─────────────────────────────────────────────────────
        Add(new TextBlock
        {
            Text = "Debloat / Tweaks",
            FontFamily = (FontFamily)FindResource("AkariDisplay"),
            FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = Brush("#F0EAEB"), Margin = new Thickness(0, 0, 0, 4)
        });
        Add(new TextBlock
        {
            Text = "Run or undo PowerShell-backed privacy, performance, and cleanup tweaks.",
            FontSize = 13, Foreground = Brush("#8A7E80"),
            Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap
        });

        // ── Groups ───────────────────────────────────────────────────────────
        BuildGroup("Privacy & Telemetry", new[]
        {
            ("Telemetry — Disable",          "Disables Windows data collection and telemetry",                   "Telemetry.ps1",            "Telemetry-Undo.ps1"),
            ("Activity History — Disable",   "Erases recent docs, clipboard, and run history",                   "ActivityHistory.ps1",      "ActivityHistory-Undo.ps1"),
            ("Location Tracking — Disable",  "Disables Windows location services",                               "LocationTracking.ps1",     "LocationTracking-Undo.ps1"),
            ("PS7 Telemetry — Disable",      "Opts out of PowerShell 7 telemetry collection",                    "PS7Telemetry.ps1",         "PS7Telemetry-Undo.ps1"),
            ("Windows AI — Disable",         "Removes Copilot, Recall, and all AI features",                     "WindowsAI.ps1",            "WindowsAI-Undo.ps1"),
            ("Consumer Features — Disable",  "Disables suggested apps, tips, and Windows promotions",            "ConsumerFeatures.ps1",     "ConsumerFeatures-Undo.ps1"),
            ("Background Apps — Disable",    "Stops all Microsoft Store apps from running in the background",    "DisableBGApps.ps1",        "DisableBGApps-Undo.ps1"),
            ("Store Search — Disable",       "Hides Microsoft Store results from Start Menu search",             "StoreSearch.ps1",          "StoreSearch-Undo.ps1"),
        });

        BuildGroup("System & Performance", new[]
        {
            ("Create Restore Point",         "Creates a Windows system restore point before making changes",     "RestorePoint.ps1",         ""),
            ("Visual Effects — Best Perf",   "Disables animations and visual fluff for max speed",               "VisualEffects.ps1",        "VisualEffects-Undo.ps1"),
            ("Services — Set to Manual",     "Sets non-essential services to manual startup",                    "Services.ps1",             "Services-Undo.ps1"),
            ("Delivery Optimization — Dis",  "Stops Windows using your bandwidth to share updates",              "DeliveryOptimization.ps1", "DeliveryOptimization-Undo.ps1"),
            ("BitLocker — Disable",          "Disables BitLocker encryption on the system drive",                "DisableBitLocker.ps1",     "DisableBitLocker-Undo.ps1"),
            ("Hibernation — Disable",        "Disables hibernation and removes hiberfil.sys",                    "Hibernation.ps1",          "Hibernation-Undo.ps1"),
            ("Storage Sense — Disable",      "Stops Windows from auto-deleting temp files",                      "StorageSense.ps1",         "StorageSense-Undo.ps1"),
            ("WPBT — Disable",               "Disables Windows Platform Binary Table execution",                 "WPBT.ps1",                 "WPBT-Undo.ps1"),
            ("Set Time to UTC",              "Fixes time sync when dual booting with Linux",                     "UTC.ps1",                  "UTC-Undo.ps1"),
        });

        BuildGroup("Cleanup", new[]
        {
            ("Disk Cleanup — Run",           "Runs cleanup on C: and removes old Windows Updates",               "DiskCleanup.ps1",          ""),
            ("Temporary Files — Remove",     "Clears temp folders and prefetch files",                           "TempFiles.ps1",            ""),
            ("Unwanted Apps — Remove",       "Removes pre-installed bloatware apps",                             "Debloat.ps1",              "Debloat-Undo.ps1"),
            ("OneDrive — Remove",            "Completely removes OneDrive from the system",                      "RemoveOneDrive.ps1",       "RemoveOneDrive-Undo.ps1"),
            ("Microsoft Edge — Debloat",     "Disables telemetry, popups, and annoyances in Edge",               "EdgeDebloat.ps1",          "EdgeDebloat-Undo.ps1"),
            ("Microsoft Edge — Remove",      "Fully uninstalls Microsoft Edge from the system",                  "RemoveEdge.ps1",           ""),
        });

        BuildGroup("Explorer & UI", new[]
        {
            ("End Task — Enable",            "Adds End Task when right-clicking taskbar apps",                   "EndTask.ps1",              "EndTask-Undo.ps1"),
            ("Folder Discovery — Disable",   "Stops Explorer auto-changing folder view layouts",                 "FolderDiscovery.ps1",      "FolderDiscovery-Undo.ps1"),
            ("Explorer Home — Remove",       "Hides Home and Gallery from Explorer sidebar",                     "RemoveHomeAndGallery.ps1", "RemoveHomeAndGallery-Undo.ps1"),
            ("Right-Click — Classic",        "Restores the old Windows 10 right-click menu",                    "RightClickMenu.ps1",       "RightClickMenu-Undo.ps1"),
            ("Widgets — Remove",             "Removes the Widgets button from the taskbar",                      "Widgets.ps1",              "Widgets-Undo.ps1"),
        });

        BuildGroup("Tools", new[]
        {
            ("O&O ShutUp10++ — Run",         "Downloads and launches the O&O ShutUp10 privacy tool",            "OOSU.ps1",                 ""),
        });
    }

    // ── Card builder ──────────────────────────────────────────────────────────

    private void BuildGroup(string title, (string Title, string Desc, string Script, string Undo)[] items)
    {
        Add(new TextBlock
        {
            Text = title,
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F0EAEB"),
            Margin = new Thickness(0, 16, 0, 6)
        });

        var card = new Border
        {
            Background = Brush("#8C130508"),
            BorderBrush = Brush("#26FF3C46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var stack = new StackPanel();

        for (int i = 0; i < items.Length; i++)
        {
            var (itemTitle, desc, script, undo) = items[i];

            var row = new Grid { Margin = new Thickness(18, 13, 18, 13) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            Grid.SetColumn(info, 0);
            info.Children.Add(new TextBlock
            {
                Text = itemTitle, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#F0EAEB")
            });
            info.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 12, Foreground = Brush("#8A7E80"),
                Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
            });

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(btns, 1);

            var capturedTitle  = itemTitle;
            var capturedScript = script;
            var capturedUndo   = undo;

            var runBtn = new Button { Content = "Run", Style = (Style)FindResource("RunBtn") };
            runBtn.Click += async (_, _) =>
                await Service.RunWithTracking(new ScriptAction(capturedScript), capturedTitle, _applied);
            btns.Children.Add(runBtn);

            if (!string.IsNullOrEmpty(undo))
            {
                var undoBtn = new Button { Content = "Undo", Style = (Style)FindResource("UndoBtn") };
                undoBtn.Click += async (_, _) =>
                    await Service.RunAction(new ScriptAction(capturedUndo));
                btns.Children.Add(undoBtn);
            }

            row.Children.Add(info);
            row.Children.Add(btns);
            stack.Children.Add(row);

            if (i < items.Length - 1)
                stack.Children.Add(new Separator
                {
                    Background = Brush("#1AFFFFFF"),
                    Height = 1,
                    Margin = new Thickness(0)
                });
        }

        card.Child = stack;
        Add(card);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Add(UIElement el) => RootPanel.Children.Add(el);

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
