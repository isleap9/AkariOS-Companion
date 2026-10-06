using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

/// <summary>
/// Notifications — behavior and system alert controls.
/// </summary>
public partial class NotificationsPage : Page
{
    public NotificationsPage()
    {
        InitializeComponent();

        DataContext = new NotificationsViewModel(
            App.StateReader, App.Executor, App.Tool.Log);
        Loaded += async (_, _) => await ((NotificationsViewModel)DataContext).LoadAsync();
    }
}
