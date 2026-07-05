using System.Windows.Controls;
using System.Windows.Input;
using Companion.ViewModels.Hub;

namespace Companion.App.Views;

/// <summary>
/// The Career Hub shell: a left tab rail around the re-homed loop. The only code-behind is the
/// number-key tab accelerator (decision 8 parity) — bare 1..9 selects tab N, but it yields to
/// any focused text box so the result-entry grammar (which types car numbers into its input)
/// keeps its digits. Everything else is bindings to <see cref="HubViewModel"/>.
/// </summary>
public partial class HubView : UserControl
{
    public HubView() => InitializeComponent();

    private HubViewModel? ViewModel => DataContext as HubViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Yield to a focused editable box (the result-entry InputBox owns bare digits) and to
        // any modified chord, so tab-switching never fights the grammar or a shortcut.
        if (Keyboard.FocusedElement is TextBox { IsReadOnly: false } || Keyboard.Modifiers != ModifierKeys.None)
            return;

        int tab = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
            _ => 0,
        };

        if (tab > 0 && vm.SelectTabByNumber(tab))
            e.Handled = true;
    }
}
