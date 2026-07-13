using System.Windows;
using System.Windows.Controls;

namespace Companion.App.Views;

/// <summary>Small view-layer prompt for a manual save label. Enter confirms and Esc cancels.</summary>
public partial class SaveLabelDialog : Window
{
    public SaveLabelDialog(string suggestedLabel = "")
    {
        InitializeComponent();
        LabelBox.Text = suggestedLabel;
        LabelBox.SelectAll();
        Loaded += (_, _) => LabelBox.Focus();
    }

    public string Label => LabelBox.Text.Trim();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Label.Length == 0)
        {
            ValidationText.Visibility = Visibility.Visible;
            LabelBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnLabelChanged(object sender, TextChangedEventArgs e)
    {
        if (ValidationText is not null && LabelBox.Text.Trim().Length > 0)
            ValidationText.Visibility = Visibility.Collapsed;
    }
}
