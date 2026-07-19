using System.Windows;

namespace Companion.App.Views;

/// <summary>Modal "Rename career" prompt (view-layer, like every dialog): shows the current name
/// pre-selected, returns the edited text via <see cref="NewName"/> when confirmed. The VM never
/// sees the dialog, StartView hands the collected name to <c>StartViewModel.RenameRecent</c>.</summary>
public partial class RenameCareerDialog : Window
{
    public RenameCareerDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string NewName => NameBox.Text;

    private void OnRename(object sender, RoutedEventArgs e) => DialogResult = true;
}
