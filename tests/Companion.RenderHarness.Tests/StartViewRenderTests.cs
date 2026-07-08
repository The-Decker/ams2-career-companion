using System.Windows;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Start screen's YEAR-PIC career gallery: a real StartView over a
/// real StartViewModel whose fake MRU store returns a few careers across eras (1967/1988/2020, plus a
/// legacy entry with no stored year). Exercises the 16:9 hero DataTemplate — the era-accent fallback,
/// the ImageBrush painted into the rounded band, the scrim, the era label and the remove button — the
/// binding/crash surface a compiled-XAML build cannot. Self-skips off Windows.</summary>
public sealed class StartViewRenderTests
{
    private sealed class FakeRecentStore : IRecentCareersStore
    {
        private readonly List<RecentCareer> _careers =
        [
            new() { Path = @"C:\c\a.ams2career", CareerName = "Formula One 1967", LastOpenedUtc = DateTimeOffset.UnixEpoch, SeasonYear = 1967 },
            new() { Path = @"C:\c\b.ams2career", CareerName = "Turbo Years", LastOpenedUtc = DateTimeOffset.UnixEpoch, SeasonYear = 1988 },
            new() { Path = @"C:\c\c.ams2career", CareerName = "Hybrid Era", LastOpenedUtc = DateTimeOffset.UnixEpoch, SeasonYear = 2020 },
            // legacy entry: no stored year → the gallery reads the year out of the name
            new() { Path = @"C:\c\d.ams2career", CareerName = "My 1974 run", LastOpenedUtc = DateTimeOffset.UnixEpoch },
        ];

        public IReadOnlyList<RecentCareer> Load() => _careers;
        public void Touch(string path, string careerName, int seasonYear = 0) { }
        public void Remove(string path) { }
    }

    [Fact]
    public void StartView_RendersTheYearPicGallery()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new FakeRecentStore());
            Assert.True(vm.HasRecentCareers);
            Assert.Equal(4, vm.RecentCareers.Count);

            var view = new StartView { DataContext = vm };
            view.Measure(new Size(900, 800));
            view.Arrange(new Rect(0, 0, 900, 800));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
