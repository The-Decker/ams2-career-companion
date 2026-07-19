using System;
using System.Windows;
using System.Windows.Controls;
using Companion.Data;

namespace Companion.App.Views;

/// <summary>
/// DB-free bankruptcy presentation, the economy's terminal surface. The owning Hub handles a
/// restore request because restoring spends the current session and the shell must reopen the
/// career file afterwards (the same escape the Normal death screen offers).
/// </summary>
public partial class BankruptcyView : UserControl
{
    public BankruptcyView() => InitializeComponent();

    /// <summary>
    /// Raised when a save slot is chosen. The event carries captured slot metadata only; this
    /// view never reaches through <c>HomeViewModel.Session</c>.
    /// </summary>
    public event EventHandler<SaveSlotInfo>? RestoreRequested;

    private void OnRestoreRequestedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SaveSlotInfo slot })
            RestoreRequested?.Invoke(this, slot);
    }
}
