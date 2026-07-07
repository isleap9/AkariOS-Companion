using System.Windows.Controls;
using AkariOSCompanion.Views;

namespace AkariOSCompanion.Helpers;

/// <summary>
/// Lets pages (e.g. Home quick-access cards) navigate the main Frame
/// without needing a DI container or reference to MainWindow.
/// </summary>
public static class AppNavigation
{
    private static Frame? _frame;

    public static void Register(Frame frame) => _frame = frame;

    public static void Navigate(Type pageType)
    {
        if (_frame is null) return;

        Page page = pageType.Name switch
        {
            nameof(HomePage)           => new HomePage(),
            nameof(GamingTweaksPage)   => new GamingTweaksPage(),
            nameof(AkariOSTweaksPage)  => new AkariOSTweaksPage(),
            nameof(DownloadsPage)      => new DownloadsPage(),
            nameof(MiscPage)           => new MiscPage(),
            _                          => new HomePage()
        };

        _frame.Navigate(page);
    }
}
