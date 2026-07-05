using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>Empty state (ux-round §4): open the user pack folder in Explorer.</summary>
    private void OnOpenPackFolder(object sender, RoutedEventArgs e)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AMS2CareerCompanion", "Packs");
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Could not open '{directory}':\n\n{ex.Message}",
                "AMS2 Career Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Mouse parity (ux-round §2): double-click a seat = select it and advance.</summary>
    private void OnSeatDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: SeatOption seat } ||
            DataContext is not NewCareerWizardViewModel vm)
            return;

        vm.SelectedSeat = seat;
        if (vm.NextCommand.CanExecute(null))
            vm.NextCommand.Execute(null);
    }
}
