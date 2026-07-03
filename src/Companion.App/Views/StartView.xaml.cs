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

    /// <summary>Right-click MRU → Continue (same as double-click).</summary>
    private void OnRecentContinue(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCareer career } &&
            DataContext is StartViewModel vm &&
            vm.ContinueCommand.CanExecute(career))
        {
            vm.ContinueCommand.Execute(career);
        }
    }

    /// <summary>Right-click MRU → Remove from list (the career file stays on disk).</summary>
    private void OnRecentRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCareer career } &&
            DataContext is StartViewModel vm)
        {
            vm.RemoveRecentCommand.Execute(career);
        }
    }

    /// <summary>Right-click MRU → Open the folder with the career file selected.</summary>
    private void OnRecentOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentCareer career })
            return;
        try
        {
            if (File.Exists(career.Path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{career.Path}\"")
                {
                    UseShellExecute = true,
                });
                return;
            }

            string? directory = Path.GetDirectoryName(career.Path);
            if (directory is { Length: > 0 } && Directory.Exists(directory))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Could not open the folder of '{career.Path}':\n\n{ex.Message}",
                "AMS2 Career Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Contract start-screen entry point: open the user pack folder in Explorer.</summary>
    private void OnOpenPackFolder(object sender, RoutedEventArgs e) =>
        OpenDocumentsFolder("Packs");

    /// <summary>Empty state (ux-round §4): open the careers folder in Explorer.</summary>
    private void OnOpenCareersFolder(object sender, RoutedEventArgs e) =>
        OpenDocumentsFolder("Careers");

    private static void OpenDocumentsFolder(string subFolder)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AMS2CareerCompanion", subFolder);
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
