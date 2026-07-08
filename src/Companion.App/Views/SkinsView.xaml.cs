using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Companion.ViewModels.Hub;

namespace Companion.App.Views;

/// <summary>
/// The Skins lens. The viewmodel is WPF-free, so this code-behind bridges the one WPF-specific
/// piece: the CopyRequested event (a livery NAME to copy) onto the real clipboard — the same
/// pattern the briefing uses for its copy action.
/// </summary>
public partial class SkinsView : UserControl
{
    public SkinsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SkinsViewModel oldVm)
            oldVm.CopyRequested -= OnCopyRequested;
        if (e.NewValue is SkinsViewModel newVm)
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
            // Another process holds the clipboard open (CLIPBRD_E_CANT_OPEN) — one retry, then
            // give up quietly; a failed copy must never crash the hub.
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
