using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;
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

    /// <summary>Presentation-only main-menu layer switch. The StartViewModel remains the owner of
    /// career selection and navigation; this simply reveals the retained gallery as a garage drawer.</summary>
    private void OnToggleCareerGarage(object sender, RoutedEventArgs e) =>
        SetCareerGarageOpen(CareerGaragePanel.Visibility != Visibility.Visible);

    private void OnCloseCareerGarage(object sender, RoutedEventArgs e) =>
        SetCareerGarageOpen(false);

    private void SetCareerGarageOpen(bool open)
    {
        MainMenuModeStage.IsEnabled = !open;
        CareerGarageBackdrop.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        CareerGaragePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            if (CareerGalleryList.SelectedIndex < 0 && CareerGalleryList.Items.Count > 0)
                CareerGalleryList.SelectedIndex = 0;
            CareerGarageCloseButton.Focus();
        }
        else
        {
            ModeCareerGarageButton.Focus();
        }
    }

    private void OnGaragePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        SetCareerGarageOpen(false);
        e.Handled = true;
    }

    private void OnExitApplication(object sender, RoutedEventArgs e) =>
        Window.GetWindow(this)?.Close();

    /// <summary>"Open career…" button: mirror of the Ctrl+O keybind.</summary>
    private void OnOpenCareerFile(object sender, RoutedEventArgs e) => OpenCareerFilePicker();

    /// <summary>Shows the .ams2career file dialog and hands the chosen path to the VM command. The
    /// dialog (view-layer only, per the shell contract) never blocks the VM, the command takes the
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

    /// <summary>Start-gallery save action: open the selected career through the app's tracking
    /// factory, then show the surface only when the session reports Normal-mode SavesEnabled.</summary>
    private void OnRecentManageSaves(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentCareer career } ||
            Application.Current is not App { TrackedCareerFactory: { } factory } ||
            Window.GetWindow(this)?.DataContext is not ShellViewModel shell)
            return;

        try
        {
            var session = factory.Open(career.Path);
            if (!SaveManagerWindow.ShowIfEnabled(
                    Window.GetWindow(this), session, career.Path, shell, ownsSession: true))
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Save points are available only for careers created with Normal mortality.\n\n" +
                    "Off mode has no deaths to undo; Hardcore has no rollback.",
                    "Career saves", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Career save points could not be opened:\n\n{ex.Message}",
                "Career saves", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
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

    /// <summary>Right-click MRU → Rename career…: view-layer prompt (keyboard-native: textbox
    /// focused, Enter = rename, Esc = cancel), then the VM does the validated rename.</summary>
    private void OnRecentRename(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentCareer career } ||
            DataContext is not StartViewModel vm)
            return;

        var dialog = new RenameCareerDialog(career.CareerName) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            vm.RenameRecent(career, dialog.NewName);
    }

    /// <summary>Right-click MRU → Set card image…: pick an image for this career's gallery card. The
    /// dialog is view-layer only; the VM records the chosen path (point-to-file, never copied). Any
    /// image is accepted, a 16:9 source fills the hero band uncropped.</summary>
    private void OnRecentSetImage(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentCareer career } ||
            DataContext is not StartViewModel vm)
            return;

        var dialog = new OpenFileDialog
        {
            Title = $"Card image for '{career.CareerName}'",
            Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            vm.SetCareerImage(career, dialog.FileName);
    }

    /// <summary>Right-click MRU → Clear card image: drop the custom image so the card reverts to the
    /// era art resolved from the career's year.</summary>
    private void OnRecentClearImage(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCareer career } &&
            DataContext is StartViewModel vm)
        {
            vm.SetCareerImage(career, null);
        }
    }

    /// <summary>Right-click MRU → Duplicate career (non-destructive, no confirmation).</summary>
    private void OnRecentDuplicate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCareer career } &&
            DataContext is StartViewModel vm)
        {
            vm.DuplicateRecentCommand.Execute(career);
        }
    }

    /// <summary>Right-click MRU → Delete career file…: view-layer confirmation (same contract as
    /// the open picker's dialog, the VM command is the already-confirmed action, so it stays
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
