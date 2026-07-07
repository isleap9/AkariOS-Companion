using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

public partial class AkariOSTweaksPage : Page
{
    public AkariOSTweaksPage()
    {
        InitializeComponent();
        DataContext = new AkariOSTweaksViewModel(App.Tweaks);
    }
}
