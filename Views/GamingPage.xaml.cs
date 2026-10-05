using System.Threading.Tasks;
using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

public partial class GamingPage : Page
{
    public GamingPage()
    {
        InitializeComponent();

        DataContext = new GamingViewModel(App.StateReader, App.Executor, App.Tool.Log);
        Loaded += async (_, _) => await ((GamingViewModel)DataContext).LoadAsync();
    }
}
