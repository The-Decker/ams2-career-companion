using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Companion.ViewModels.Wizard;

namespace Companion.App.Views;

public partial class WizardView : UserControl
{
    private int _seatCarouselIndex;

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
        if (SeatCarousel.Items.Count == 0)
            return;

        _seatCarouselIndex = Math.Clamp(
            _seatCarouselIndex + direction,
            0,
            SeatCarousel.Items.Count - 1);
        SnapSeatCarousel();
    }

    private void OnSeatCarouselLoaded(object sender, RoutedEventArgs e)
    {
        _seatCarouselIndex = Math.Max(0, SeatCarousel.SelectedIndex);
        QueueSeatCarouselSnap();
    }

    private void OnSeatCarouselSizeChanged(object sender, SizeChangedEventArgs e) => QueueSeatCarouselSnap();

    private void OnSeatCarouselSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeatCarousel.SelectedIndex < 0)
            return;

        _seatCarouselIndex = SeatCarousel.SelectedIndex;
        QueueSeatCarouselSnap();
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
