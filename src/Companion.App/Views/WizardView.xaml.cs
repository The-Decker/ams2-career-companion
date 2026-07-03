using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Wizard;

namespace Companion.App.Views;

public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
    }

    /// <summary>RefreshPacks is a plain method on the viewmodel (not a command) — bridge it.</summary>
    private void OnRefreshPacks(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewCareerWizardViewModel vm)
            vm.RefreshPacks();
    }
}
