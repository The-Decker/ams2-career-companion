using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.ViewModels.Wizard;

namespace Companion.RenderHarness.Tests;

/// <summary>Render contract for the Racing Passport pure-racing route: the four-step chrome,
/// the optional player-name input on SeatPick, and the honest confirm block. The stand-in host
/// mirrors <see cref="NewCareerWizardViewModel"/>'s mode gates exactly.</summary>
public sealed class WizardPassportRenderTests
{
    [Fact]
    public void WizardView_PassportRoute_RendersExactlyFourSteps()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new PassportHost { Step = WizardStep.SeasonPick };
            var view = Render(host);

            Assert.Equal(Visibility.Visible,
                ((TextBlock)view.FindName("SeasonStepLabel")).Visibility);
            Assert.Equal(Visibility.Visible,
                ((TextBlock)view.FindName("VerificationStepLabel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                ((TextBlock)view.FindName("DriverStepLabel")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                ((TextBlock)view.FindName("SeasonGridStepLabel")).Visibility);

            var teamCar = (TextBlock)view.FindName("TeamCarStepLabel");
            var confirm = (TextBlock)view.FindName("ConfirmStepLabel");
            Assert.Equal(Visibility.Visible, teamCar.Visibility);
            Assert.Equal(Visibility.Visible, confirm.Visibility);
            Assert.Equal("3 · Team & Car", teamCar.Text);
            Assert.Equal("4 · Confirm", confirm.Text);
        });
    }

    [Fact]
    public void WizardView_PassportSeatPick_NameInputBindsAndFlagsOverLongInput()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new PassportHost { Step = WizardStep.SeatPick };
            var view = Render(host);

            Assert.Equal(Visibility.Visible,
                ((Border)view.FindName("PassportNamePanel")).Visibility);
            // The own-entrant box is a management surface, Passport always takes a pack seat.
            Assert.Equal(Visibility.Collapsed,
                ((Border)view.FindName("OwnEntrantPanel")).Visibility);

            var input = (TextBox)view.FindName("PlayerDisplayNameInput");
            var error = (TextBlock)view.FindName("PlayerDisplayNameErrorText");
            Assert.Equal(Visibility.Visible, input.Visibility);
            Assert.Equal(Visibility.Collapsed, error.Visibility);

            input.Text = "Ayrton";
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            Assert.Equal("Ayrton", host.PlayerDisplayName);
            Assert.Equal(Visibility.Collapsed, error.Visibility);

            input.Text = new string('X', PassportHost.MaxPlayerDisplayNameLength + 1);
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            view.UpdateLayout();

            Assert.Equal("Keep it to 40 characters or fewer.", host.PlayerDisplayNameError);
            Assert.Equal(Visibility.Visible, error.Visibility);
            Assert.Equal(host.PlayerDisplayNameError, error.Text);

            // Back under the limit, the inline error clears again (blank stays valid).
            input.Text = "";
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            Assert.Equal("", host.PlayerDisplayNameError);
            Assert.Equal(Visibility.Collapsed, error.Visibility);
        });
    }

    [Fact]
    public void WizardView_PassportSeatPick_NationalityPickerBindsAndClears()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new PassportHost { Step = WizardStep.SeatPick };
            var view = Render(host);

            var picker = (ComboBox)view.FindName("PassportCountryPicker");
            Assert.Equal(Visibility.Visible, picker.Visibility);
            Assert.Null(host.SelectedPassportCountry); // unset = the seat's authored country
            Assert.Equal("", host.PassportNationalitySummary);

            // Pick Brazil: the selection flows to the wizard and the confirm summary shows it.
            object brazil = Assert.Single(host.PassportCountryOptions, option => option.Code == "BRA");
            picker.SelectedItem = brazil;
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            Assert.Equal(brazil, host.SelectedPassportCountry);
            Assert.Contains("BRA", host.PassportNationalitySummary);

            // The Clear affordance returns to the authored-country default.
            var clear = Descendants<Button>(view).Single(button =>
                AutomationProperties.GetName(button) == "Clear nationality pick");
            clear.Command.Execute(null);
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            Assert.Null(host.SelectedPassportCountry);
            Assert.Equal("", host.PassportNationalitySummary);
        });
    }

    [Fact]
    public void WizardView_PassportConfirm_RendersHonestSummaryWithoutMortality()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new PassportHost { Step = WizardStep.Confirm };
            var view = Render(host);

            var panel = (Border)view.FindName("PassportConfirmPanel");
            Assert.Equal(Visibility.Visible, panel.Visibility);

            var lines = (ItemsControl)view.FindName("PassportConfirmLinesList");
            Assert.Equal(host.PassportConfirmLines.Count, lines.Items.Count);
            var rendered = lines.Items.Cast<string>().ToArray();
            Assert.Contains("Progression: None, pure racing", rendered);
            Assert.Contains("Team management: None", rendered);
            Assert.DoesNotContain(rendered,
                line => line.Contains("Level", StringComparison.OrdinalIgnoreCase)
                     || line.Contains("mastery", StringComparison.OrdinalIgnoreCase)
                     || line.Contains("economy", StringComparison.OrdinalIgnoreCase));

            // Mortality stays Off for Passport, so the picker never renders.
            Assert.Equal(Visibility.Collapsed,
                ((Border)view.FindName("MortalityModePanel")).Visibility);
        });
    }

    [Fact]
    public void WizardView_DynastyRoute_RendersUnchanged()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new PassportHost { IsRacingPassportMode = false, Step = WizardStep.SeasonPick };
            var view = Render(host);

            // Every legacy step indicator stays, with the legacy numbering.
            Assert.Equal(Visibility.Visible,
                ((TextBlock)view.FindName("DriverStepLabel")).Visibility);
            Assert.Equal(Visibility.Visible,
                ((TextBlock)view.FindName("SeasonGridStepLabel")).Visibility);
            Assert.Equal("6 · Confirm",
                ((TextBlock)view.FindName("ConfirmStepLabel")).Text);

            host.Step = WizardStep.SeatPick;
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            view.UpdateLayout();

            Assert.Equal(Visibility.Collapsed,
                ((Border)view.FindName("PassportNamePanel")).Visibility);
            Assert.Equal(Visibility.Visible,
                ((Border)view.FindName("OwnEntrantPanel")).Visibility);

            host.Step = WizardStep.Confirm;
            WpfRenderHarness.Pump(DispatcherPriority.DataBind);
            view.UpdateLayout();

            Assert.Equal(Visibility.Collapsed,
                ((Border)view.FindName("PassportConfirmPanel")).Visibility);
            Assert.Equal(Visibility.Visible,
                ((Border)view.FindName("MortalityModePanel")).Visibility);
        });
    }

    private static WizardView Render(PassportHost host)
    {
        var view = new WizardView { DataContext = host };
        view.Measure(new Size(1100, 820));
        view.Arrange(new Rect(0, 0, 1100, 820));
        view.UpdateLayout();
        WpfRenderHarness.Pump(DispatcherPriority.DataBind);
        return view;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>Stand-in for <see cref="NewCareerWizardViewModel"/> exposing exactly the Passport
    /// bind contract: the mode gates, the step-chrome flags, the name field with its validation,
    /// and the confirm lines. <see cref="IsRacingPassportMode"/> flips it to the Dynasty route.</summary>
    private sealed class PassportHost : INotifyPropertyChanged
    {
        private WizardStep _step = WizardStep.SeasonPick;
        private string _playerDisplayName = "";

        public const int MaxPlayerDisplayNameLength = 40;

        public PassportHost()
        {
            ClearPassportCountryCommand = new DelegateCommand(() => SelectedPassportCountry = null);
        }

        public System.Windows.Input.ICommand ClearPassportCountryCommand { get; }

        private sealed class DelegateCommand(Action execute) : System.Windows.Input.ICommand
        {
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => execute();
        }

        public bool IsRacingPassportMode { get; init; } = true;

        public WizardStep Step
        {
            get => _step;
            set
            {
                if (_step == value)
                    return;
                _step = value;
                OnPropertyChanged();
            }
        }

        public string ExperienceMode => IsRacingPassportMode ? "racingPassport" : "grandPrixDynasty";
        public bool IsRacingPassport => IsRacingPassportMode;
        public bool HasCharacterStep => !IsRacingPassportMode;
        public bool HasGridStep => !IsRacingPassportMode;
        public bool ShowsOwnEntrant => !IsRacingPassportMode;
        public bool ShowsMortalityChoice => !IsRacingPassportMode;
        public bool IsSmgpPack => false;

        public string CareerName { get; set; } = "Passport career";
        public string MasterSeedText { get; set; } = "1991";

        public string PlayerDisplayName
        {
            get => _playerDisplayName;
            set
            {
                if (_playerDisplayName == value)
                    return;
                _playerDisplayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayerDisplayNameError));
            }
        }

        public string PlayerDisplayNameError =>
            PlayerDisplayName.Trim().Length > MaxPlayerDisplayNameLength
                ? $"Keep it to {MaxPlayerDisplayNameLength} characters or fewer."
                : "";

        public IReadOnlyList<Companion.ViewModels.Wizard.CharacterCountryOption> PassportCountryOptions { get; } =
        [
            new("BRA", "Brazil", "bra"),
            new("GBR", "United Kingdom", "gbr"),
            new("ITA", "Italy", "ita"),
        ];

        private Companion.ViewModels.Wizard.CharacterCountryOption? _selectedPassportCountry;

        public Companion.ViewModels.Wizard.CharacterCountryOption? SelectedPassportCountry
        {
            get => _selectedPassportCountry;
            set
            {
                if (Equals(_selectedPassportCountry, value))
                    return;
                _selectedPassportCountry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PassportNationalitySummary));
            }
        }

        public string PassportNationalitySummary =>
            SelectedPassportCountry is { } country ? $"{country.Name} ({country.Code})" : "";

        public IReadOnlyList<string> PassportConfirmLines => IsRacingPassportMode
            ?
            [
                "RACING PASSPORT",
                "Series: FIA Formula One World Championship",
                "Season: 1991",
                "Team: Brabham-Repco",
                "Seat: Brabham-Repco · replacing N. Piquet · #1",
                "Driver: the seat's authored driver",
                "Career format: One complete faithful season",
                "Progression: None, pure racing",
                "Team management: None",
                "Field: Historical season grid locked to the selected pack",
            ]
            : [];

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
