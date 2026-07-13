using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.ViewModels.Wizard;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen coverage for the new-career one-car-per-screen contract carousel.</summary>
public sealed class WizardSeatCarouselRenderTests
{
    [Fact]
    public void Breadcrumb_PutsDriverBetweenVerificationAndTeamCar()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var host = Host.Show(CarouselHost.Smgp(), 1500);
            var labels = host.Descendants<TextBlock>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            int verify = Array.FindIndex(labels, text => text.Contains("Verify content", StringComparison.Ordinal));
            int driver = Array.FindIndex(labels, text => text.Contains("Driver", StringComparison.Ordinal));
            int teamCar = Array.FindIndex(labels, text => text.Contains("Team & Car", StringComparison.Ordinal));

            Assert.True(verify >= 0 && driver > verify && teamCar > driver,
                "The breadcrumb must read Verify content, Driver, then Team & Car.");
        });
    }

    [Fact]
    public void Carousel_PagesOneFullCard_WithoutSelectingIt()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var data = CarouselHost.Smgp();
            using var host = Host.Show(data, 1500);

            var carousel = host.Find<ListBox>("SeatCarousel");
            var scroller = host.Find<ScrollViewer>("SeatCarouselScroller");
            var previous = host.Find<Button>("SeatCarouselPrevious");
            var next = host.Find<Button>("SeatCarouselNext");
            Assert.NotNull(carousel);
            Assert.NotNull(scroller);
            Assert.NotNull(previous);
            Assert.NotNull(next);
            Assert.Equal(3, carousel!.Items.Count);
            Assert.Null(data.SelectedSeat);

            var first = Assert.IsType<ListBoxItem>(carousel.ItemContainerGenerator.ContainerFromIndex(0));
            var second = Assert.IsType<ListBoxItem>(carousel.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.True(Math.Abs(first.ActualWidth - scroller!.ViewportWidth) < 1.5,
                $"Each contract should fill one viewport: {first.ActualWidth:0.0} vs {scroller.ViewportWidth:0.0}.");
            Assert.True(Math.Abs(second.TranslatePoint(new Point(), first).X - first.ActualWidth) < 2.0,
                "The second contract should begin exactly one page after the first.");

            // Even if external layout or an accessibility action leaves a partial pixel offset,
            // the arrow contract is index-based and must snap to one whole card.
            scroller.ScrollToHorizontalOffset(scroller.ViewportWidth * 0.35);
            host.PumpLayout();
            Assert.True(next!.IsEnabled);
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, next));
            host.PumpLayout();

            Assert.True(Math.Abs(scroller.HorizontalOffset - scroller.ViewportWidth) < 1.5,
                "Next should page to the next full contract.");
            Assert.Null(data.SelectedSeat);

            host.ResizeWidth(1260);
            Assert.True(Math.Abs(scroller.HorizontalOffset - scroller.ViewportWidth) < 1.5,
                "A resize should preserve the current full-card page.");

            Assert.True(previous!.IsEnabled);
            previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, previous));
            host.PumpLayout();
            Assert.True(scroller.HorizontalOffset < 1.5);
        });
    }

    [Fact]
    public void ClickingCard_SwapsIncumbentForYou_PlayerNameAndPlayerPortrait()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var data = CarouselHost.Smgp();
            using var host = Host.Show(data, 1500);
            var carousel = host.Find<ListBox>("SeatCarousel")!;
            var first = Assert.IsType<ListBoxItem>(carousel.ItemContainerGenerator.ContainerFromIndex(0));

            Assert.Equal(Visibility.Visible, host.FindNamed<TextBlock>(first, "SeatCardCurrentDriverLabel")!.Visibility);
            Assert.Equal(Visibility.Collapsed, host.FindNamed<TextBlock>(first, "SeatCardYouLabel")!.Visibility);
            Assert.Equal(Visibility.Visible, host.FindNamed<TextBlock>(first, "SeatCardDriverName")!.Visibility);

            // A ListBoxItem click sets SelectedItem; use the same selection path without bypassing
            // any VM logic, then let the bindings render the player identity.
            first.IsSelected = true;
            host.PumpLayout();

            Assert.Same(data.Seats[0], data.SelectedSeat);
            Assert.Equal("player.rigel", data.PlayerImageKey);
            Assert.Equal(Visibility.Collapsed, host.FindNamed<TextBlock>(first, "SeatCardCurrentDriverLabel")!.Visibility);
            Assert.Equal(Visibility.Visible, host.FindNamed<TextBlock>(first, "SeatCardYouLabel")!.Visibility);
            Assert.Equal("YOU", host.FindNamed<TextBlock>(first, "SeatCardYouLabel")!.Text);
            Assert.Equal(Visibility.Collapsed, host.FindNamed<TextBlock>(first, "SeatCardDriverName")!.Visibility);
            var playerName = host.FindNamed<TextBlock>(first, "SeatCardPlayerName")!;
            Assert.Equal(Visibility.Visible, playerName.Visibility);
            Assert.Equal("Nova Reyes", playerName.Text);
            Assert.NotNull(host.FindNamed<Image>(first, "SeatCardPlayerPortrait")!.Source);
            Assert.NotNull(host.FindNamed<Image>(first, "SeatCardCarPreview")!.Source);
        });
    }

    [Fact]
    public void HistoricalSeat_UsesTheSameExpandedCarouselAndPlayerIdentityFallback()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var data = CarouselHost.Historical();
            using var host = Host.Show(data, 1500);
            var carousel = host.Find<ListBox>("SeatCarousel")!;
            var first = Assert.IsType<ListBoxItem>(carousel.ItemContainerGenerator.ContainerFromIndex(0));

            Assert.True(first.ActualWidth > 900);
            first.IsSelected = true;
            host.PumpLayout();

            Assert.Equal(Visibility.Visible, host.FindNamed<TextBlock>(first, "SeatCardYouLabel")!.Visibility);
            Assert.Equal("Nova Reyes", host.FindNamed<TextBlock>(first, "SeatCardPlayerName")!.Text);
            // Historical player/car art is optional: the framed placeholders remain part of the
            // same full-size contract instead of collapsing the preview layout.
            Assert.True(host.FindNamed<Grid>(first, "SeatCardPortrait")!.ActualWidth >= 200);
        });
    }

    private sealed class CarouselHost : INotifyPropertyChanged
    {
        private SeatOption? _selectedSeat;

        public WizardStep Step => WizardStep.SeatPick;
        public bool HasCharacterStep => true;
        public required bool IsSmgpPack { get; init; }
        public DriverStub Character { get; } = new() { Name = "Nova Reyes" };
        public ObservableCollection<SeatOption> Seats { get; } = [];
        public string CustomLiveryName { get; set; } = "";

        public SeatOption? SelectedSeat
        {
            get => _selectedSeat;
            set
            {
                if (ReferenceEquals(_selectedSeat, value))
                    return;
                _selectedSeat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayerImageKey));
            }
        }

        public string PlayerImageKey => GridSeatChoice.PlayerImageKey(SelectedSeat?.TeamId ?? "");

        public static CarouselHost Smgp()
        {
            var host = new CarouselHost { IsSmgpPack = true };
            host.Seats.Add(Seat("driver.ryan_cotman", "Ryan Cotman", "team.rigel", "Rigel", "26"));
            host.Seats.Add(Seat("driver.tristan_chardin", "Tristan Chardin", "team.rigel", "Rigel", "27"));
            host.Seats.Add(Seat("driver.alef_delvaux", "Alef Delvaux", "team.cool", "Cool", "28"));
            return host;
        }

        public static CarouselHost Historical()
        {
            var host = new CarouselHost { IsSmgpPack = false };
            host.Seats.Add(Seat("driver.j_clark", "Jim Clark", "team.lotus", "Team Lotus", "5"));
            host.Seats.Add(Seat("driver.g_hill", "Graham Hill", "team.lotus", "Team Lotus", "6"));
            return host;
        }

        private static SeatOption Seat(string driverId, string driverName, string teamId, string teamName, string number) =>
            new()
            {
                LiveryName = $"{teamName} #{number}",
                DriverId = driverId,
                DriverName = driverName,
                TeamId = teamId,
                TeamName = teamName,
                Number = number,
                Rounds = "1-16",
                RaceSkill = 0.75,
                QualifyingSkill = 0.72,
                TeamTier = 2,
                Prestige = 2,
                Reliability = 0.8,
            };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class DriverStub
    {
        public required string Name { get; init; }
    }

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        public WizardView View { get; }

        private Host(Window window, WizardView view)
        {
            _window = window;
            View = view;
        }

        public static Host Show(object dataContext, double width)
        {
            var view = new WizardView { DataContext = dataContext };
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = 900,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            window.UpdateLayout();
            WpfRenderHarness.Pump();
            return new Host(window, view);
        }

        public T? Find<T>(string name) where T : FrameworkElement =>
            Descendants<T>().FirstOrDefault(element => element.Name == name);

        public T? FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement =>
            Descendants<T>(root).FirstOrDefault(element => element.Name == name);

        public IEnumerable<T> Descendants<T>() where T : DependencyObject => Descendants<T>(View);

        public void PumpLayout()
        {
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            _window.UpdateLayout();
            WpfRenderHarness.Pump();
        }

        public void ResizeWidth(double width)
        {
            _window.Width = width;
            PumpLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            PumpLayout();
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }
    }
}
