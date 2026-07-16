using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Companion.App.Audio;
using Companion.ViewModels.Wizard;

namespace Companion.App.Views;

public partial class WizardView : UserControl
{
    private int _seatCarouselIndex;
    private bool _seatCarouselLoaded;

    public WizardView()
    {
        InitializeComponent();
    }

    /// <summary>RefreshPacks is a plain method on the viewmodel (not a command) — bridge it.</summary>
    private void OnRefreshPacks(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewCareerWizardViewModel vm)
            vm.RefreshPacks();
    }

    /// <summary>Empty state (ux-round §4): open the user pack folder in Explorer.</summary>
    private void OnOpenPackFolder(object sender, RoutedEventArgs e)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AMS2CareerCompanion", "Packs");
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

    private void OnPreviousSeatCard(object sender, RoutedEventArgs e) => PageSeatCarousel(-1);

    private void OnNextSeatCard(object sender, RoutedEventArgs e) => PageSeatCarousel(1);

    private void PageSeatCarousel(int direction)
    {
        int count = SeatCarousel.Items.Count;
        if (count == 0)
            return;

        _seatCarouselIndex = ((_seatCarouselIndex + direction) % count + count) % count;
        SnapSeatCarousel();
    }

    private void OnSeatCarouselLoaded(object sender, RoutedEventArgs e)
    {
        _seatCarouselIndex = Math.Max(0, SeatCarousel.SelectedIndex);
        _seatCarouselLoaded = true;
        QueueSeatCarouselSnap();
    }

    private void OnSeatCarouselSizeChanged(object sender, SizeChangedEventArgs e) => QueueSeatCarouselSnap();

    private void OnSeatCarouselSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeatCarousel.SelectedIndex < 0)
            return;

        _seatCarouselIndex = SeatCarousel.SelectedIndex;
        if (_seatCarouselLoaded && e.AddedItems.Count > 0)
            SoundAssist.Play(SoundEffectCue.SeatConfirm);
        QueueSeatCarouselSnap();
    }

    /// <summary>
    /// Each item stays viewport-wide so paging remains exact, but only the visible card may choose
    /// a seat. This prevents the empty gutters around a centered card from behaving like a button.
    /// </summary>
    private void OnSeatCarouselPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || !IsInsideSeatCard(source))
            e.Handled = true;
    }

    private static bool IsInsideSeatCard(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { Name: "SeatCardFrame" })
                return true;
            if (current is ListBox)
                return false;

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent,
                _ => LogicalTreeHelper.GetParent(current),
            };
        }

        return false;
    }

    private void QueueSeatCarouselSnap() =>
        Dispatcher.BeginInvoke(SnapSeatCarousel, DispatcherPriority.Loaded);

    private void SnapSeatCarousel()
    {
        double pageWidth = SeatCarouselScroller.ViewportWidth;
        if (pageWidth > 0)
            SeatCarouselScroller.ScrollToHorizontalOffset(_seatCarouselIndex * pageWidth);
    }

}
