using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Briefing;

namespace Companion.App.Views;

/// <summary>
/// The Race Day briefing. The viewmodel is WPF-free, so this code-behind bridges the two
/// WPF-specific pieces: the CopyRequested event onto the real clipboard, and the
/// CompactChecklistOpen flag onto the small always-on-top checklist window
/// (<see cref="BriefingCompactWindow"/>) bound to the SAME viewmodel, plus the
/// stage/force-stage confirmation popovers (pure open/confirm mechanics; staging itself is
/// the viewmodel's command).
/// </summary>
public partial class BriefingView : UserControl
{
    private BriefingCompactWindow? _compactWindow;

    public BriefingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private BriefingViewModel? ViewModel => DataContext as BriefingViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BriefingViewModel oldVm)
        {
            oldVm.CopyRequested -= OnCopyRequested;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is BriefingViewModel newVm)
        {
            newVm.CopyRequested += OnCopyRequested;
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            SyncCompactWindow(newVm);
        }
    }

    private static void OnCopyRequested(object? sender, string text)
    {
        try
        {
            Clipboard.SetDataObject(text);
        }
        catch (COMException)
        {
            // Another process holds the clipboard open (CLIPBRD_E_CANT_OPEN), one retry,
            // then give up quietly; a failed copy must never crash the briefing.
            try
            {
                Clipboard.SetDataObject(text);
            }
            catch (COMException)
            {
            }
        }
    }

    // ---------- compact always-on-top checklist window ----------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BriefingViewModel.CompactChecklistOpen) &&
            sender is BriefingViewModel vm)
        {
            SyncCompactWindow(vm);
        }
    }

    private void SyncCompactWindow(BriefingViewModel vm)
    {
        if (vm.CompactChecklistOpen)
        {
            if (_compactWindow is not null)
                return;
            _compactWindow = new BriefingCompactWindow
            {
                DataContext = vm,
                Owner = Window.GetWindow(this),
            };
            _compactWindow.Closed += OnCompactWindowClosed;
            _compactWindow.Show();
        }
        else if (_compactWindow is not null)
        {
            var window = _compactWindow;
            _compactWindow = null; // guard against Closed -> Sync re-entrancy
            window.Closed -= OnCompactWindowClosed;
            window.Close();
        }
    }

    /// <summary>The user closed the floating window directly, reflect it in the toggle.</summary>
    private void OnCompactWindowClosed(object? sender, EventArgs e)
    {
        _compactWindow = null;
        if (ViewModel is { CompactChecklistOpen: true } vm)
            vm.CompactChecklistOpen = false;
    }

    /// <summary>Leaving the briefing view closes the overlay (v1: the overlay lives with the
    /// briefing screen; its ticks are keyed per round in the viewmodel and survive).</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { CompactChecklistOpen: true } vm)
        {
            vm.CompactChecklistOpen = false; // property change closes the window
        }
        else if (_compactWindow is not null)
        {
            var window = _compactWindow;
            _compactWindow = null;
            window.Closed -= OnCompactWindowClosed;
            window.Close();
        }
    }

    // ---------- stage / force-stage confirmation popovers ----------

    private void OnStageClick(object sender, RoutedEventArgs e) => StageConfirmPopup.IsOpen = true;

    private void OnStageConfirm(object sender, RoutedEventArgs e)
    {
        StageConfirmPopup.IsOpen = false;
        ViewModel?.StageGridCommand.Execute(null);
    }

    private void OnForceStageClick(object sender, RoutedEventArgs e) => ForceConfirmPopup.IsOpen = true;

    private void OnForceStageConfirm(object sender, RoutedEventArgs e)
    {
        ForceConfirmPopup.IsOpen = false;
        ViewModel?.ForceStageGridCommand.Execute(null);
    }

    private void OnStagePopupCancel(object sender, RoutedEventArgs e)
    {
        StageConfirmPopup.IsOpen = false;
        ForceConfirmPopup.IsOpen = false;
    }
}
