using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

public partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        InitializeComponent();
        DataContext = new DownloadsViewModel(App.Installer);
    }
}
