using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

public partial class MiscPage : Page
{
    public MiscPage()
    {
        InitializeComponent();
        DataContext = new MiscViewModel();
    }
}
