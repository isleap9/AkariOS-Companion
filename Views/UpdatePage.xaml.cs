using System.Threading.Tasks;
using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

/// <summary>
/// Windows Updates — policy mode, delivery optimisation, and update behaviour controls.
/// </summary>
public partial class UpdatePage : Page
{
    public UpdatePage()
    {
        InitializeComponent();

        DataContext = new UpdateViewModel(
            App.StateReader, App.Executor, App.Registry, App.Tool.Log);
        Loaded += async (_, _) => await ((UpdateViewModel)DataContext).LoadAsync();
    }
}
