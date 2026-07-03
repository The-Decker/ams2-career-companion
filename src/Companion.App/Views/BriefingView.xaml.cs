using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Briefing;

namespace Companion.App.Views;

/// <summary>
/// The Race Day briefing. The viewmodel is WPF-free, so its CopyRequested event is bridged
/// to the real clipboard here — the only WPF-specific piece of the copy flow.
/// </summary>
public partial class BriefingView : UserControl
{
    public BriefingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BriefingViewModel oldVm)
            oldVm.CopyRequested -= OnCopyRequested;
        if (e.NewValue is BriefingViewModel newVm)
            newVm.CopyRequested += OnCopyRequested;
    }

    private static void OnCopyRequested(object? sender, string text)
    {
        try
        {
            Clipboard.SetDataObject(text);
        }
        catch (COMException)
        {
            // Another process holds the clipboard open (CLIPBRD_E_CANT_OPEN) — one retry,
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
}
