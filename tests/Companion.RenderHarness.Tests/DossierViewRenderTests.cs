using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Controls;
using Companion.App.Views;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Driver dossier over a real <see cref="CharacterDossier"/>, the
/// view binds to a host's <c>Dossier</c> property, so a lightweight stand-in exercises the view layer
/// (stat bars, perk cards, the level progress bar) without a full session. Self-skips off Windows.</summary>
public sealed class DossierViewRenderTests
{
    /// <summary>The dossier view binds <c>{Binding Dossier}</c> on its DataContext, this stands in
    /// for the DossierViewModel.</summary>
    private sealed class DossierHost : INotifyPropertyChanged
    {
        private SkillNodeViewModel? _selectedSkillNode;
        private bool _skillNodeDetailOpen;
        private IReadOnlyList<SkillNodeViewModel> _pendingSkillNodes = [];
        private int _pendingSkillPointCost;
        private int _skillPointsAfterPlan = 4;
        private bool _skillPlanDirty;
        private string? _skillActionError;
        private bool _skillResetConfirmationOpen;

        public DossierHost()
        {
            AcknowledgeLevelUpCommand = new DelegateCommand(_ => { });
            UnlockNodeCommand = new DelegateCommand(QueueNode);
            RespecNodeCommand = new DelegateCommand(_ => { });
            OpenSkillNodeCommand = new DelegateCommand(parameter =>
            {
                OpenSkillNodeExecutions++;
                SelectedSkillNode = parameter as SkillNodeViewModel;
                SkillNodeDetailOpen = SelectedSkillNode is not null;
                SkillActionError = null;
            });
            CloseSkillNodeCommand = new DelegateCommand(_ => SkillNodeDetailOpen = false);
            QueueSkillNodeCommand = new DelegateCommand(QueueNode);
            RemovePendingSkillNodeCommand = new DelegateCommand(RemovePendingNode);
            ResetSkillPlanCommand = new DelegateCommand(_ => ResetPlan());
            ConfirmSkillPlanCommand = new DelegateCommand(_ =>
            {
                ConfirmSkillPlanExecutions++;
                ResetPlan();
                SkillNodeDetailOpen = false;
            });
            OpenSkillResetConfirmationCommand = new DelegateCommand(_ => SkillResetConfirmationOpen = true);
            CloseSkillResetConfirmationCommand = new DelegateCommand(_ => SkillResetConfirmationOpen = false);
            ConfirmSkillResetCommand = new DelegateCommand(_ =>
            {
                ConfirmSkillResetExecutions++;
                SkillResetConfirmationOpen = false;
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public required CharacterDossier Dossier { get; init; }
        public string CountryName { get; init; } = "";
        public string? CountryFlagKey { get; init; }
        public bool HasCountry => CountryFlagKey is not null;
        public string TeamLine { get; init; } = "Madonna · 1990";
        public string? PlayerImageKey => null;
        public string? PlayerCarKey => null;
        public object? PlayerCarSpec => null;
        public bool HasSmgpNarrative { get; init; }
        public string NarrativeIntro { get; init; } = "";
        public IReadOnlyList<SmgpCareerBeat> Timeline { get; init; } = [];
        public string MortalityLabel { get; init; } = "MORTALITY: NORMAL";
        public IReadOnlyList<InjuryHistoryEntry> InjuryHistory { get; init; } = [];
        public bool HasInjuryHistory => InjuryHistory.Count > 0;

        public bool LevelUpPending { get; init; } = true;
        public int LevelsGained { get; init; } = 1;
        public int SkillPointsAvailable => 4;
        public int RespecTokens => 1;
        public required IReadOnlyList<SkillBranchViewModel> SkillTree { get; init; }
        public required IReadOnlyList<SkillAttributeRailViewModel> AttributeRails { get; init; }
        public SkillNodeViewModel? SelectedSkillNode
        {
            get => _selectedSkillNode;
            set => Set(ref _selectedSkillNode, value);
        }
        public bool SkillNodeDetailOpen
        {
            get => _skillNodeDetailOpen;
            set => Set(ref _skillNodeDetailOpen, value);
        }
        public IReadOnlyList<SkillNodeViewModel> PendingSkillNodes
        {
            get => _pendingSkillNodes;
            private set => Set(ref _pendingSkillNodes, value);
        }
        public int PendingSkillPointCost
        {
            get => _pendingSkillPointCost;
            private set => Set(ref _pendingSkillPointCost, value);
        }
        public int SkillPointsAfterPlan
        {
            get => _skillPointsAfterPlan;
            private set => Set(ref _skillPointsAfterPlan, value);
        }
        public bool SkillPlanDirty
        {
            get => _skillPlanDirty;
            private set => Set(ref _skillPlanDirty, value);
        }
        public string? SkillActionError
        {
            get => _skillActionError;
            private set => Set(ref _skillActionError, value);
        }
        public SkillResetPreview SkillResetPreview { get; } = new()
        {
            LifetimeXp = 18_500,
            AvailableResetXp = 12_500,
            Cost = 2_500,
            AvailableResetXpAfter = 10_000,
            SkillPointsRefunded = 7,
            SkillPointsAfterReset = 11,
            AcquisitionCount = 3,
            CanApply = true,
            BlockReason = "",
        };
        public bool SkillResetConfirmationOpen
        {
            get => _skillResetConfirmationOpen;
            private set => Set(ref _skillResetConfirmationOpen, value);
        }
        public long SkillResetCost => SkillResetPreview.Cost;
        public long AvailableResetXp => SkillResetPreview.AvailableResetXp;
        public long AvailableResetXpAfter => SkillResetPreview.AvailableResetXpAfter;
        public int SkillPointsRefunded => SkillResetPreview.SkillPointsRefunded;
        public int SkillPointsAfterReset => SkillResetPreview.SkillPointsAfterReset;
        public int SkillResetAcquisitionCount => SkillResetPreview.AcquisitionCount;
        public bool CanResetCommittedSkillTree => SkillResetPreview.CanApply;
        public string SkillResetBlockReason => SkillResetPreview.BlockReason;
        public int OpenSkillNodeExecutions { get; private set; }
        public int QueueSkillNodeExecutions { get; private set; }
        public int ConfirmSkillPlanExecutions { get; private set; }
        public int ConfirmSkillResetExecutions { get; private set; }
        public ICommand AcknowledgeLevelUpCommand { get; }
        public ICommand UnlockNodeCommand { get; }
        public ICommand RespecNodeCommand { get; }
        public ICommand OpenSkillNodeCommand { get; }
        public ICommand CloseSkillNodeCommand { get; }
        public ICommand QueueSkillNodeCommand { get; }
        public ICommand RemovePendingSkillNodeCommand { get; }
        public ICommand ResetSkillPlanCommand { get; }
        public ICommand ConfirmSkillPlanCommand { get; }
        public ICommand OpenSkillResetConfirmationCommand { get; }
        public ICommand CloseSkillResetConfirmationCommand { get; }
        public ICommand ConfirmSkillResetCommand { get; }
        public required IReadOnlyList<DossierStat> TalentStatsView { get; init; }
        public required IReadOnlyList<DossierStat> MetaStatsView { get; init; }
        public string AvailabilityLabel => Dossier.AvailabilityLabel;

        private void QueueNode(object? parameter)
        {
            var node = parameter as SkillNodeViewModel ?? SelectedSkillNode;
            if (node is null || PendingSkillNodes.Any(candidate => candidate.Id == node.Id))
                return;
            QueueSkillNodeExecutions++;
            PendingSkillNodes = [.. PendingSkillNodes, node];
            PendingSkillPointCost = PendingSkillNodes.Sum(candidate => candidate.Cost);
            SkillPointsAfterPlan = SkillPointsAvailable - PendingSkillPointCost;
            SkillPlanDirty = true;
            SkillActionError = null;
        }

        private void RemovePendingNode(object? parameter)
        {
            if (parameter is not SkillNodeViewModel node)
                return;
            int index = PendingSkillNodes.ToList().FindIndex(candidate => candidate.Id == node.Id);
            if (index < 0)
                return;
            PendingSkillNodes = PendingSkillNodes.Take(index).ToArray();
            PendingSkillPointCost = PendingSkillNodes.Sum(candidate => candidate.Cost);
            SkillPointsAfterPlan = SkillPointsAvailable - PendingSkillPointCost;
            SkillPlanDirty = PendingSkillNodes.Count > 0;
        }

        private void ResetPlan()
        {
            PendingSkillNodes = [];
            PendingSkillPointCost = 0;
            SkillPointsAfterPlan = SkillPointsAvailable;
            SkillPlanDirty = false;
            SkillActionError = null;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class DelegateCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }

    private static CharacterDossier Dossier(bool atLevelCap = false) => new()
    {
        Name = "Nova Reyes",
        CountryCode = "BRA",
        Age = 24,
        Level = atLevelCap ? 300 : 3,
        Xp = atLevelCap ? 1_250_000 : 250,
        AvailableResetXp = atLevelCap ? 1_100_000 : 250,
        XpIntoLevel = atLevelCap ? 0 : 15,
        XpForNextLevel = atLevelCap ? 0 : 182,
        LevelCap = 300,
        CpUnspent = 2,
        Stats =
        [
            new DossierStat("pace", "Pace", 0.70, Talent: true),
            new DossierStat("oneLap", "One-lap pace", 0.55, Talent: true),
            new DossierStat("marketability", "Marketability", 0.60, Talent: false),
        ],
        Perks =
        [
            new DossierPerk
            {
                Id = "rain_man", Name = "Rain Man", Category = "weather",
                Description = "Untouchable in the wet, ordinary in the dry.", Cost = 1,
                Benefits = ["Stronger wet-weather pace, in the wet"],
                Drawbacks = ["Weaker one-lap pace, in the dry"],
            },
        ],
        RacingDna = new DossierRacingDna
        {
            Id = "dna_circuit_specialist",
            Version = 1,
            Name = "The Circuit Specialist",
            Description = "A permanent technical-circuit identity with an exact authored trade-off.",
            PrimaryFamily = "pace",
            PrimaryFamilyLabel = "Pace",
            SecondaryFamily = "era",
            SecondaryFamilyLabel = "Era-flavor",
            PersistentEffects =
            [
                new RacingDnaEffect
                {
                    Key = "technicalMomentum",
                    Summary = "Technical-circuit results build additional career momentum.",
                },
            ],
            TradeoffEffects =
            [
                new RacingDnaEffect
                {
                    Key = "otherCircuitPressure",
                    Summary = "Results outside the chosen family carry a higher expectation.",
                },
            ],
            ChoiceKind = RacingDnaChoiceKind.TrackFamily,
            ChoicePrompt = "Choose the circuit family that defines this identity.",
            ChoiceValue = "technical",
        },
        InjuryRisk = "Moderate",
        ActiveModifiers =
        [
            new DossierModifierLine("+0.30 wet-weather pace", "Wet rounds"),
            new DossierModifierLine("+0.020 car power", null),
        ],
        Availability = AvailabilityStatus.Injured,
        AvailabilityLabel = "Injured, out 2 races",
    };

    private static SkillNodeViewModel Node(
        string id,
        string name,
        SkillNodeState state,
        int tier,
        int order,
        IReadOnlyList<string>? requiresIds = null,
        string lockReason = "",
        string kind = "mastery",
        string? railId = null,
        string railName = "",
        string attributeStatId = "",
        double? attributeValueAfter = null) => new()
    {
        Id = id,
        Name = name,
        Description = $"{name} changes how this driver develops between seasons.",
        Kind = kind,
        Cost = tier,
        Tier = tier,
        Order = order,
        UnlockLevel = tier * 2,
        RequiresIds = requiresIds ?? [],
        RequiresLabels = (requiresIds ?? []).Select(RequirementLabel).ToArray(),
        IconKey = $"skill-{id.Replace('_', '-')}",
        RailId = railId,
        RailName = railName,
        AttributeStatId = attributeStatId,
        AttributeValueAfter = attributeValueAfter,
        Benefits = ["Improves pace when the race is on the line"],
        Drawbacks = ["Carries a small consistency trade-off"],
        State = state,
        LockReason = lockReason,
    };

    private static IReadOnlyList<SkillBranchViewModel> Tree()
    {
        SkillNodeViewModel[] racecraft =
        [
            Node("slipstream_artist", "Slipstream Artist", SkillNodeState.Owned, 1, 1),
            Node("late_braker", "Late Braker", SkillNodeState.Unlockable, 1, 2),
            Node("apex_hunter", "Apex Hunter", SkillNodeState.Pending, 2, 3, ["slipstream_artist"]),
            Node("pressure_release", "Pressure Release", SkillNodeState.Locked, 2, 4,
                ["late_braker"], "Requires Late Braker"),
            Node("race_reader", "Race Reader", SkillNodeState.Unlockable, 3, 5, ["apex_hunter"]),
            Node("switchback_school", "Switchback School", SkillNodeState.Locked, 3, 6,
                ["pressure_release"], "Requires Pressure Release"),
            Node("closing_laps", "Closing Laps", SkillNodeState.Owned, 4, 7, ["race_reader"]),
            Node("traffic_mastery", "Traffic Mastery", SkillNodeState.Pending, 4, 8, ["switchback_school"]),
            Node("grandmaster", "Grandmaster", SkillNodeState.Mastery, 5, 9, ["closing_laps"]),
            Node("untouchable", "Untouchable", SkillNodeState.Locked, 5, 10,
                ["traffic_mastery"], "Complete the mastery checkpoint"),
        ];
        SkillNodeViewModel[] media =
        [
            Node("press_room", "Press Room Poise", SkillNodeState.Owned, 1, 1),
            Node("quiet_professional", "Quiet Professional", SkillNodeState.Unlockable, 1, 2),
            Node("crisis_manager", "Crisis Manager", SkillNodeState.Locked, 2, 3,
                ["press_room"], "Reach level 30"),
            Node("global_icon", "Global Icon", SkillNodeState.Mastery, 5, 4, ["crisis_manager"]),
        ];

        return
        [
            new SkillBranchViewModel
            {
                Id = "racecraft",
                Name = "Racecraft",
                IsMeta = false,
                Nodes = racecraft,
                MasteryNodes = racecraft,
            },
            new SkillBranchViewModel
            {
                Id = "media",
                Name = "Media Presence",
                IsMeta = true,
                Nodes = media,
                MasteryNodes = media,
            },
        ];
    }

    private static IReadOnlyList<SkillAttributeRailViewModel> AttributeRails()
    {
        SkillNodeViewModel[] pace =
        [
            Node("attribute_pace_1", "Pace +0.01", SkillNodeState.Owned, 1, 1,
                kind: "attribute", railId: "attribute.pace", railName: "Pace", attributeStatId: "pace",
                attributeValueAfter: 0.71),
            Node("attribute_pace_2", "Pace +0.01", SkillNodeState.Unlockable, 2, 2,
                ["attribute_pace_1"], kind: "attribute", railId: "attribute.pace", railName: "Pace",
                attributeStatId: "pace", attributeValueAfter: 0.72),
            Node("attribute_pace_3", "Pace +0.01", SkillNodeState.Locked, 3, 3,
                ["attribute_pace_2"], "Reach level 90", "attribute", "attribute.pace", "Pace", "pace", 0.73),
        ];
        SkillNodeViewModel[] durability =
        [
            Node("attribute_durability_1", "Durability +0.01", SkillNodeState.Pending, 1, 1,
                kind: "attribute", railId: "attribute.durability", railName: "Durability",
                attributeStatId: "durability", attributeValueAfter: 0.61),
            Node("attribute_durability_2", "Durability +0.01", SkillNodeState.Locked, 2, 2,
                ["attribute_durability_1"], "Confirm the previous step", "attribute",
                "attribute.durability", "Durability", "durability", 0.62),
        ];

        return
        [
            new SkillAttributeRailViewModel
            {
                Id = "attribute.pace",
                Name = "Pace",
                StatId = "pace",
                Nodes = pace,
                OwnedCount = 1,
                TotalCount = pace.Length,
            },
            new SkillAttributeRailViewModel
            {
                Id = "attribute.durability",
                Name = "Durability",
                StatId = "durability",
                Nodes = durability,
                OwnedCount = 0,
                TotalCount = durability.Length,
            },
        ];
    }

    private static string RequirementLabel(string id) => string.Join(' ', id.Split('_'));

    private static DossierHost Host(
        bool withNarrative = false,
        bool withCountry = true,
        bool atLevelCap = false,
        bool withInjuryHistory = true)
    {
        var dossier = Dossier(atLevelCap);
        return new DossierHost
        {
            Dossier = dossier,
            CountryName = withCountry ? "Brazil" : "",
            CountryFlagKey = withCountry ? "driver.ayrton_senna" : null,
            SkillTree = Tree(),
            AttributeRails = AttributeRails(),
            TalentStatsView = dossier.Stats.Where(stat => stat.Talent).ToArray(),
            MetaStatsView = dossier.Stats.Where(stat => !stat.Talent).ToArray(),
            HasSmgpNarrative = withNarrative,
            NarrativeIntro = withNarrative ? "A rookie season is becoming a real campaign." : "",
            InjuryHistory = withInjuryHistory
                ?
                [
                    new InjuryHistoryEntry
                    {
                        SeasonOrdinal = 2,
                        SeasonYear = 1991,
                        Round = 6,
                        Outcome = "minorInjury",
                        MissRaces = 2,
                        Label = "Injured - missed 2 races",
                        Description = "Bruised ribs",
                    },
                ]
                : [],
            Timeline = withNarrative
                ?
                [
                    new SmgpCareerBeat
                    {
                        WhenLabel = "Season 1 · San Marino",
                        Kind = SmgpBeatKind.FirstStart,
                        Headline = "FIRST START",
                        Detail = "Nova Reyes joined the SMGP grid and began the climb.",
                    },
                ]
                : [],
        };
    }

    [Fact]
    public void DossierView_LevelCapUsesMaxStateInsteadOfPhantomLevelProgress()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new DossierView { DataContext = Host(atLevelCap: true) };
            Arrange(view);

            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(view.FindName("MaxLevelBadge")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<ProgressBar>(view.FindName("LevelProgressBar")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<TextBlock>(view.FindName("NextLevelXpLine")).Visibility);
            Assert.Contains(Descendants<TextBlock>(view),
                block => InlineText(block).Contains("LEVEL 300", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void DossierView_RendersActiveEffectsAndMedicalRecord_AndCollapsesEmptyHistory()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var current = new DossierView { DataContext = Host() };
            Arrange(current);

            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(current.FindName("ActiveEffectsPanel")).Visibility);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(current.FindName("MedicalRecordPanel")).Visibility);
            Assert.Contains(Descendants<TextBlock>(current),
                block => block.Text.Contains("+0.30 wet-weather pace", StringComparison.Ordinal));
            Assert.Contains(Descendants<TextBlock>(current),
                block => block.Text.Contains("Bruised ribs", StringComparison.Ordinal));
            Assert.Contains(Descendants<TextBlock>(current),
                block => block.Text.Contains("MORTALITY: NORMAL", StringComparison.Ordinal));

            var clean = new DossierView
            {
                DataContext = Host(withInjuryHistory: false),
            };
            Arrange(clean);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(clean.FindName("MedicalRecordPanel")).Visibility);
        });
    }

    [Fact]
    public void DossierView_RendersOverACharacterDossier()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new DossierView { DataContext = Host() };
            view.Measure(new Size(1100, 900));
            view.Arrange(new Rect(0, 0, 1100, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("LevelUpBanner")).Visibility);
            Assert.Equal(2, ((ItemsControl)view.FindName("SkillTreeBranches")).Items.Count);
            var dnaPanel = Assert.IsType<ContentControl>(view.FindName("RacingDnaIdentityPanel"));
            Assert.Equal(Visibility.Visible, dnaPanel.Visibility);
            Assert.True(dnaPanel.ActualHeight > 0);
            var dna = Assert.IsType<DossierRacingDna>(dnaPanel.Content);
            Assert.Single(dna.PersistentEffects);
            Assert.Single(dna.TradeoffEffects);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("PendingSkillPlanPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("CommittedSkillResetPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SkillResetConfirmationPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("DossierCountryIdentity")).Visibility);
        });
    }

    [Fact]
    public void DossierView_CountryIdentity_RendersFlagAndAccessibleName_AndCollapsesForLegacy()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var current = new DossierView { DataContext = Host(withCountry: true) };
            Arrange(current);

            var identity = Assert.IsType<Border>(current.FindName("DossierCountryIdentity"));
            var flag = Assert.IsType<Image>(current.FindName("DossierCountryFlag"));
            var countryName = Assert.IsType<TextBlock>(current.FindName("DossierCountryName"));
            Assert.Equal(Visibility.Visible, identity.Visibility);
            Assert.Equal("Brazil", countryName.Text);
            Assert.Equal("Brazil", AutomationProperties.GetName(identity));
            Assert.Equal("Brazil flag", AutomationProperties.GetName(flag));
            Assert.NotNull(flag.Source);

            var legacy = new DossierView { DataContext = Host(withCountry: false) };
            Arrange(legacy);
            var legacyIdentity = Assert.IsType<Border>(legacy.FindName("DossierCountryIdentity"));
            var legacyFlag = Assert.IsType<Image>(legacy.FindName("DossierCountryFlag"));
            Assert.Equal(Visibility.Collapsed, legacyIdentity.Visibility);
            Assert.Null(legacyFlag.Source);
        });
    }

    [Fact]
    public void DossierView_SkillPlan_OpensQueuesResetsAndConfirms()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = Host();
            var view = new DossierView { DataContext = host };
            Arrange(view);

            var open = Descendants<SkillGraphNodeButton>(view).Single(button =>
                button.Name == "SkillGraphNodeButton" &&
                button.DataContext is SkillNodeViewModel { Id: "late_braker" });
            Assert.NotNull(open.Command);
            open.Command.Execute(open.CommandParameter);
            WpfRenderHarness.Pump();
            Arrange(view);

            Assert.Equal("late_braker", host.SelectedSkillNode?.Id);
            Assert.True(host.SkillNodeDetailOpen);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SkillNodeDetailPanel")).Visibility);

            var queue = Descendants<Button>(view).Single(button => button.Name == "QueueSkillNodeButton");
            Assert.NotNull(queue.Command);
            queue.Command.Execute(queue.CommandParameter);
            WpfRenderHarness.Pump();
            Arrange(view);

            Assert.True(host.SkillPlanDirty);
            Assert.Equal(1, host.PendingSkillPointCost);
            Assert.Equal(3, host.SkillPointsAfterPlan);
            Assert.Single(host.PendingSkillNodes);
            Assert.Single(((ItemsControl)view.FindName("PendingSkillNodesList")).Items);

            var reset = (Button)view.FindName("ResetSkillPlanButton");
            reset.Command.Execute(reset.CommandParameter);
            WpfRenderHarness.Pump();
            Assert.False(host.SkillPlanDirty);
            Assert.Empty(host.PendingSkillNodes);
            Assert.Equal(host.SkillPointsAvailable, host.SkillPointsAfterPlan);

            queue.Command.Execute(queue.CommandParameter);
            WpfRenderHarness.Pump();
            var confirm = (Button)view.FindName("ConfirmSkillPlanButton");
            confirm.Command.Execute(confirm.CommandParameter);
            WpfRenderHarness.Pump();

            Assert.Equal(1, host.ConfirmSkillPlanExecutions);
            Assert.False(host.SkillPlanDirty);
            Assert.False(host.SkillNodeDetailOpen);
        });
    }

    [Fact]
    public void DossierView_CommittedReset_RequiresSeparateConfirmation()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = Host();
            var view = new DossierView { DataContext = host };
            Arrange(view);

            var open = Descendants<Button>(view).Single(button => button.Name == "OpenSkillResetButton");
            Assert.True(open.IsEnabled);
            open.Command.Execute(open.CommandParameter);
            WpfRenderHarness.Pump();
            Arrange(view);

            Assert.True(host.SkillResetConfirmationOpen);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SkillResetConfirmationPanel")).Visibility);
            Assert.Equal(2_500, host.SkillResetCost);
            Assert.Equal(10_000, host.AvailableResetXpAfter);
            Assert.Equal(7, host.SkillPointsRefunded);

            var close = Descendants<Button>(view).Single(button => button.Name == "CloseSkillResetButton");
            close.Command.Execute(close.CommandParameter);
            WpfRenderHarness.Pump();
            Assert.False(host.SkillResetConfirmationOpen);

            open.Command.Execute(open.CommandParameter);
            WpfRenderHarness.Pump();
            var confirm = Descendants<Button>(view).Single(button => button.Name == "ConfirmSkillResetButton");
            confirm.Command.Execute(confirm.CommandParameter);
            WpfRenderHarness.Pump();

            Assert.Equal(1, host.ConfirmSkillResetExecutions);
            Assert.False(host.SkillResetConfirmationOpen);
        });
    }

    [Fact]
    public void DossierView_MasteryGraph_ArrangesAuthoredConnectorsAndExposesBothCommandSeams()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = Host();
            var view = new DossierView { DataContext = host };
            Arrange(view);

            var selector = Assert.IsType<TabControl>(view.FindName("SkillFamilySelector"));
            Assert.Equal(host.SkillTree.Count, selector.Items.Count);
            Assert.Equal("racecraft", Assert.IsType<SkillBranchViewModel>(selector.SelectedItem).Id);

            SkillGraphPanel graph = Graph(view, "MasterySkillGraph");
            ForceRender(graph);
            SkillBranchViewModel family = host.SkillTree[0];
            Assert.Equal(family.MasteryNodes.Count, graph.ArrangedNodeBounds.Count);
            Assert.Equal(family.MasteryNodes.Sum(node => node.RequiresIds.Count), graph.RenderedConnectorCount);

            var states = family.MasteryNodes.Select(node => node.State).ToHashSet();
            Assert.Contains(SkillNodeState.Owned, states);
            Assert.Contains(SkillNodeState.Unlockable, states);
            Assert.Contains(SkillNodeState.Pending, states);
            Assert.Contains(SkillNodeState.Locked, states);
            Assert.Contains(SkillNodeState.Mastery, states);

            foreach (SkillNodeViewModel node in family.MasteryNodes)
            {
                Rect target = graph.ArrangedNodeBounds[node.Id];
                foreach (string requirement in node.RequiresIds)
                {
                    Rect source = graph.ArrangedNodeBounds[requirement];
                    Assert.True(source.Right < target.Left,
                        $"Prerequisite {requirement} must be arranged before {node.Id}.");
                }
            }

            var buttons = Descendants<SkillGraphNodeButton>(graph).ToArray();
            Assert.Equal(family.MasteryNodes.Count, buttons.Length);
            Assert.All(buttons, button =>
            {
                Assert.True(button.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.NotNull(button.Command);
                Assert.NotNull(button.DoubleClickCommand);
            });

            SkillGraphNodeButton lateBraker = buttons.Single(button =>
                button.DataContext is SkillNodeViewModel { Id: "late_braker" });
            lateBraker.Command!.Execute(lateBraker.CommandParameter);
            WpfRenderHarness.Pump();
            Assert.Equal("late_braker", host.SelectedSkillNode?.Id);
            Assert.Equal(1, host.OpenSkillNodeExecutions);

            lateBraker.DoubleClickCommand!.Execute(lateBraker.DoubleClickCommandParameter);
            WpfRenderHarness.Pump();
            Assert.Equal(1, host.QueueSkillNodeExecutions);
            Assert.Contains(host.PendingSkillNodes, node => node.Id == "late_braker");
        });
    }

    [Fact]
    public void DossierView_FamilyAndAttributeSelectors_ReplaceTheVisibleGraph()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = Host();
            var view = new DossierView { DataContext = host };
            Arrange(view);

            var familySelector = Assert.IsType<TabControl>(view.FindName("SkillFamilySelector"));
            SkillGraphPanel mastery = Graph(view, "MasterySkillGraph");
            Assert.Contains("late_braker", mastery.ArrangedNodeBounds.Keys);

            familySelector.SelectedIndex = 1;
            WpfRenderHarness.Pump();
            Arrange(view);
            mastery = Graph(view, "MasterySkillGraph");
            ForceRender(mastery);
            Assert.Equal("media", Assert.IsType<SkillBranchViewModel>(familySelector.SelectedItem).Id);
            Assert.Equal(host.SkillTree[1].MasteryNodes.Count, mastery.ArrangedNodeBounds.Count);
            Assert.Contains("global_icon", mastery.ArrangedNodeBounds.Keys);
            Assert.DoesNotContain("late_braker", mastery.ArrangedNodeBounds.Keys);

            var railSelector = Assert.IsType<TabControl>(view.FindName("SkillAttributeRailSelector"));
            Assert.Equal(host.AttributeRails.Count, railSelector.Items.Count);
            SkillGraphPanel rail = Graph(view, "AttributeSkillGraph");
            ForceRender(rail);
            Assert.Equal(host.AttributeRails[0].Nodes.Count, rail.ArrangedNodeBounds.Count);
            Assert.Equal(2, rail.RenderedConnectorCount);

            railSelector.SelectedIndex = 1;
            WpfRenderHarness.Pump();
            Arrange(view);
            rail = Graph(view, "AttributeSkillGraph");
            ForceRender(rail);
            Assert.Equal("attribute.durability",
                Assert.IsType<SkillAttributeRailViewModel>(railSelector.SelectedItem).Id);
            Assert.Equal(host.AttributeRails[1].Nodes.Count, rail.ArrangedNodeBounds.Count);
            Assert.Equal(1, rail.RenderedConnectorCount);
        });
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(1.00)]
    [InlineData(1.10)]
    [InlineData(1.25)]
    [InlineData(1.30)]
    public void DossierView_GraphFitsAtSupportedUiScalesWithoutNestedScrolling(double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new DossierView { DataContext = Host() };
            Border root = ArrangeAtScale(view, scale);

            ScrollViewer[] scrollViewers = Descendants<ScrollViewer>(view).ToArray();
            Assert.Single(scrollViewers);
            Assert.Equal(ScrollBarVisibility.Disabled, scrollViewers[0].HorizontalScrollBarVisibility);

            SkillGraphPanel mastery = Graph(view, "MasterySkillGraph");
            ForceRender(mastery);
            Rect graphBounds = mastery.TransformToAncestor(view)
                .TransformBounds(new Rect(new Point(), mastery.RenderSize));
            Assert.True(graphBounds.Width > 0);
            Assert.True(graphBounds.Left >= -1);
            Assert.True(graphBounds.Right <= view.ActualWidth + 1,
                $"Mastery graph overflowed at {scale:P0}: {graphBounds.Right:0.##} > {view.ActualWidth:0.##}.");
            Assert.All(mastery.ArrangedNodeBounds.Values, bounds =>
            {
                Assert.True(bounds.Width > 0);
                Assert.True(bounds.Height > 0);
                Assert.True(bounds.Right <= mastery.ActualWidth + 1);
                Assert.True(bounds.Bottom <= mastery.ActualHeight + 1);
            });

            Assert.True(root.ActualWidth > 0);
            Assert.True(root.ActualHeight > 0);
        });
    }

    [Fact]
    public void DossierView_StoryStartsExpanded_AndCanCollapseAndReopen()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var view = new DossierView { DataContext = Host(withNarrative: true) };
            Arrange(view);

            var expander = (Expander)view.FindName("StoryExpander");
            var content = (FrameworkElement)view.FindName("StoryContent");
            Assert.Equal(Visibility.Visible, expander.Visibility);
            Assert.True(expander.IsExpanded);
            Assert.True(content.ActualHeight > 0);

            expander.IsExpanded = false;
            Arrange(view);
            Assert.Equal(Visibility.Collapsed, content.Visibility);

            expander.IsExpanded = true;
            Arrange(view);
            Assert.Equal(Visibility.Visible, content.Visibility);
            Assert.True(content.ActualHeight > 0);
        });
    }

    private static void Arrange(FrameworkElement view)
    {
        view.Measure(new Size(1100, 900));
        view.Arrange(new Rect(0, 0, 1100, 900));
        view.UpdateLayout();
    }

    private static Border ArrangeAtScale(DossierView view, double scale)
    {
        view.LayoutTransform = new ScaleTransform(scale, scale);
        var root = new Border
        {
            Width = 1100,
            Height = 900,
            Child = view,
        };
        root.Measure(new Size(1100, 900));
        root.Arrange(new Rect(0, 0, 1100, 900));
        root.UpdateLayout();
        return root;
    }

    private static SkillGraphPanel Graph(DependencyObject root, string name) =>
        Descendants<SkillGraphPanel>(root).Single(panel => panel.Name == name);

    private static void ForceRender(FrameworkElement element)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
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

    private static string InlineText(TextBlock block) =>
        block.Text + string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text));
}
