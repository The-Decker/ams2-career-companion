using System.Windows;

namespace Companion.App;

/// <summary>
/// The shell window: a ContentControl over ShellViewModel.Current (DataContext), with every
/// screen mapped by the DataTemplates in App.xaml. No logic here — navigation lives in
/// Companion.ViewModels.Shell.ShellViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
