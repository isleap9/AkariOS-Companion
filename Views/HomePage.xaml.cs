using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = new HomeViewModel();
    }
}
