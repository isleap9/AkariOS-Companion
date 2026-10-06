using System.Windows.Controls;
using AkariOSCompanion.ViewModels;

namespace AkariOSCompanion.Views;

/// <summary>
/// Privacy — AI features, app permissions, and content delivery controls.
/// </summary>
public partial class PrivacyPage : Page
{
    public PrivacyPage()
    {
        InitializeComponent();

        DataContext = new PrivacyViewModel(
            App.StateReader, App.Executor, App.Tool.Log);
        Loaded += async (_, _) => await ((PrivacyViewModel)DataContext).LoadAsync();
    }
}
