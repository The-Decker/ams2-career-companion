using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Settings;
using Microsoft.Win32;

namespace Companion.App.Views;

/// <summary>Thin shell over SettingsViewModel: the folder picker and open-in-Explorer
/// buttons are the only view-side logic (everything else is bindings).</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnAddPackFolder(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        var dialog = new OpenFolderDialog
        {
            Title = "Pick a folder containing season packs",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            vm.AddPackFolder(dialog.FolderName);
    }

    private void OnOpenDefaultPacksFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(Vm?.DefaultPacksFolder, create: true);

    private void OnOpenCareersFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(Vm?.CareersFolder, create: true);

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(Vm?.SettingsFolder, create: true);

    /// <summary>Open button on a listed extra pack folder (the row's DataContext is the path).</summary>
    private void OnOpenListedFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string path })
            OpenFolder(path, create: false);
    }

    private void OpenFolder(string? directory, bool create)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        try
        {
            if (create)
                Directory.CreateDirectory(directory);
            if (!Directory.Exists(directory))
            {
                MessageBox.Show($"'{directory}' does not exist.",
                    "AMS2 Career Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Could not open '{directory}':\n\n{ex.Message}",
                "AMS2 Career Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
