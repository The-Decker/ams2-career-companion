using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;
using Microsoft.Win32;

namespace Companion.App.Views;

public partial class StartView : UserControl
{
    /// <summary>The Ctrl+O accelerator target (bound in XAML) for the "Open career…" picker, so the
    /// keybind and the button run the exact same code path (career-hub-design.md decision 8).</summary>
    public static readonly RoutedUICommand OpenCareerFileCommand =
        new("Open career file", nameof(OpenCareerFileCommand), typeof(StartView));

    public StartView()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(OpenCareerFileCommand, (_, _) => OpenCareerFilePicker()));
    }

    /// <summary>"Open career…" button: mirror of the Ctrl+O keybind.</summary>
    private void OnOpenCareerFile(object sender, RoutedEventArgs e) => OpenCareerFilePicker();

    /// <summary>Shows the .ams2career file dialog and hands the chosen path to the VM command. The
    /// dialog (view-layer only, per the shell contract) never blocks the VM — the command takes the
    /// path so it stays unit-testable; opening routes through the same continue flow as the gallery.</summary>
    private void OpenCareerFilePicker()
    {
        if (DataContext is not StartViewModel vm)
            return;

        string careersFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AMS2CareerCompanion", "Careers");
        var dialog = new OpenFileDialog
        {
            Title = "Open career",
            Filter = "AMS2 career files (*.ams2career)|*.ams2career|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(careersFolder) ? careersFolder : string.Empty,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            vm.OpenCareerCommand.Execute(dialog.FileName);
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

    /// <summary>Right-click MRU → Delete career file…: view-layer confirmation (same contract as
    /// the open picker's dialog — the VM command is the already-confirmed action, so it stays
    /// unit-testable). Defaults to No; reachable by keyboard via the context-menu key.</summary>
    private void OnRecentDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentCareer career } ||
            DataContext is not StartViewModel vm)
            return;

        var choice = MessageBox.Show(
            $"Delete '{career.CareerName}' permanently?\n\n{career.Path}\n\n" +
            "This deletes the career file from disk. It cannot be undone.",
            "Delete career", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes)
            vm.DeleteRecentCommand.Execute(career);
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
