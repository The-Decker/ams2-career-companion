using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Companion.App.Views;
using Companion.Core.Career;
using Companion.ViewModels.Wizard;

namespace Companion.RenderHarness.Tests;

/// <summary>Pins the creation-time mortality contract and its unmistakable Hardcore treatment.</summary>
public sealed class WizardMortalityRenderTests
{
    [Fact]
    public void WizardView_MortalityPicker_TwoWayBindsHardcoreAndShowsWarning()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new WizardHost();
            var view = new WizardView { DataContext = host };
            view.Measure(new Size(1100, 820));
            view.Arrange(new Rect(0, 0, 1100, 820));
            view.UpdateLayout();

            var panel = (FrameworkElement)view.FindName("MortalityModePanel");
            var picker = (ListBox)view.FindName("MortalityModePicker");
            var summary = (TextBlock)view.FindName("MortalityModeSummaryText");
            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(MortalityMode.Off, picker.SelectedItem);

            picker.SelectedItem = MortalityMode.Hardcore;
            WpfRenderHarness.Pump();

            Assert.Equal(MortalityMode.Hardcore, host.MortalityMode);
            Assert.Contains("delete", summary.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no restore", summary.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(false, Visibility.Visible)]
    [InlineData(true, Visibility.Collapsed)]
    public void WizardView_SmgpHidesInstalledBaselineImplementationDetails(
        bool isSmgpPack,
        Visibility expectedVisibility)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new WizardHost { IsSmgpPack = isSmgpPack };
            var view = new WizardView { DataContext = host };
            view.Measure(new Size(1100, 820));
            view.Arrange(new Rect(0, 0, 1100, 820));
            view.UpdateLayout();

            var baselineOptions = (FrameworkElement)view.FindName("InstalledBaselineOptions");

            Assert.Equal(expectedVisibility, baselineOptions.Visibility);
        });
    }

    private sealed class WizardHost : INotifyPropertyChanged
    {
        private MortalityMode _mortalityMode;

        public WizardStep Step => WizardStep.Confirm;
        public string CareerName { get; set; } = "Nova's career";
        public string MasterSeedText { get; set; } = "1967";
        public bool IsSmgpPack { get; init; }
        public IReadOnlyList<MortalityMode> MortalityOptions { get; } =
            [MortalityMode.Off, MortalityMode.Normal, MortalityMode.Hardcore];

        public MortalityMode MortalityMode
        {
            get => _mortalityMode;
            set
            {
                if (_mortalityMode == value)
                    return;
                _mortalityMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MortalityModeSummary));
            }
        }

        public string MortalityModeSummary => MortalityMode switch
        {
            MortalityMode.Normal => "Injuries and fatal accidents are possible. Saves may restore the career.",
            MortalityMode.Hardcore => "Fatal accidents permanently delete the career and every save. There is no restore.",
            _ => "Fatal accidents and race-missing injuries are disabled.",
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
