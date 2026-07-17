using System.IO;
using System.Windows;
using Companion.Ams2.ContentLibrary;
using Companion.App.Views;
using Companion.ViewModels.Debug;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the developer debug menu (dynasty-passport-roadmap Piece 2) over a
/// real <see cref="DebugMenuViewModel"/> — catches a broken binding or missing resource in the Tier
/// buttons, pack list, level input and inspector panel. Self-skips off Windows.</summary>
public sealed class DebugMenuRenderTests
{
    [Fact]
    public void DebugMenuView_RendersOverItsViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var environment = new CareerEnvironment
            {
                ContentLibrary = new Ams2ContentLibrary
                {
                    ExtractedFrom = "render harness",
                    Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal),
                    Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal),
                    Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal),
                    Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal),
                },
                LocateInstall = static () => null,
                DocumentsDirectory = Path.GetTempPath(),
            };
            var vm = new DebugMenuViewModel(
                environment, new NoFactory(), Path.Combine(Path.GetTempPath(), "debug-render"));
            vm.InspectorText = "sample inspector output"; // exercise the inspector panel's visible path

            var view = new DebugMenuView { DataContext = vm };
            view.Measure(new Size(1000, 900));
            view.Arrange(new Rect(0, 0, 1000, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }

    private sealed class NoFactory : ICareerFactory
    {
        public ICareerSession Create(CareerCreationRequest request) => throw new NotSupportedException();
        public ICareerSession Open(string careerFilePath) => throw new NotSupportedException();
    }
}
