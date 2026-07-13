using System;
using System.Windows;
using System.Windows.Controls;
using Companion.Data;

namespace Companion.App.Views;

/// <summary>
/// DB-free death/permadeath presentation. The owning Hub handles a restore request because restoring
/// spends the current session and the shell must reopen the career file afterwards.
/// </summary>
public partial class DeathScreenView : UserControl
{
    public DeathScreenView() => InitializeComponent();

    /// <summary>
    /// Raised when a Normal-mode save is chosen. The event carries captured slot metadata only; this
    /// view never reaches through <c>HomeViewModel.Session</c>, which keeps the Hardcore path DB-free.
    /// </summary>
    public event EventHandler<SaveSlotInfo>? RestoreRequested;

    private void OnRestoreRequestedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SaveSlotInfo slot })
            RestoreRequested?.Invoke(this, slot);
    }
}
