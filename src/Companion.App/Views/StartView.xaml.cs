using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;

namespace Companion.App.Views;

public partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    /// <summary>Double-click a recent career = continue it.</summary>
    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: RecentCareer career } &&
            DataContext is StartViewModel vm &&
            vm.ContinueCommand.CanExecute(career))
        {
            vm.ContinueCommand.Execute(career);
        }
    }

    /// <summary>Contract start-screen entry point: open the user pack folder in Explorer.</summary>
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
}
