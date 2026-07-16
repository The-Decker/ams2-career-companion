using Companion.Core.Character;
using Companion.Tests.Career;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The Driver dossier tab (character depth 3): a career with a character grows a Driver tab
/// in the hub showing the dossier; a character-free career does not.</summary>
public sealed class CharacterDossierHubTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dossier-hub-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterProfile Character() => new()
    {
        Name = "Nova Reyes",
        CountryCode = "BRA",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.65, ["oneLap"] = 0.55, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.50,
        },
        PerkIds = ["rain_man"],
        CpUnspent = 1,
    };

    private CareerSessionService CreateCareer(CharacterProfile? character)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = Path.Combine(_root, "pack"),
                CareerFilePath = Path.Combine(_root, "career.ams2career"),
                CareerName = "Dossier Career",
                MasterSeed = 7,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = character,
            },
            environment);
    }

    [Fact]
    public void CharacterCareer_HubHasADriverTab_ShowingTheDossier()
    {
        using var hub = new HubViewModel(CreateCareer(Character()));

        Assert.Contains(hub.Tabs, t => t.Key == HubViewModel.DriverTabKey);
        Assert.Equal(2, hub.Tabs.ToList().FindIndex(t => t.Key == HubViewModel.DriverTabKey)); // after Standings

        Assert.True(hub.Dossier.HasCharacter);
        Assert.Equal("Nova Reyes", hub.Dossier.Dossier!.Name);
        Assert.Equal("BRA", hub.Dossier.CountryCode);
        Assert.Equal("Brazil", hub.Dossier.CountryName);
        Assert.Equal("country.brazil", hub.Dossier.CountryFlagKey);
        Assert.True(hub.Dossier.HasCountry);
        Assert.Equal(1, hub.Dossier.Dossier.Level);
        Assert.Equal(7, hub.Dossier.Dossier.Stats.Count);
        Assert.Contains(hub.Dossier.Dossier.Perks, p => p.Id == "rain_man");
        Assert.Equal(9, hub.Dossier.SkillTree.Count);
        Assert.Equal(5, hub.Dossier.TalentStatsView.Count);
        Assert.Equal(2, hub.Dossier.MetaStatsView.Count);
        Assert.Equal("Fit", hub.Dossier.AvailabilityLabel);
        Assert.Equal(1, hub.Dossier.SkillPointsAvailable);
    }

    [Fact]
    public void Dossier_ShowsTheTeamAndSeasonTheDriverRacesFor()
    {
        using var session = CreateCareer(Character());
        var vm = new DossierViewModel(session);

        Assert.NotNull(vm.TeamLine);
        Assert.Contains("Brabham-Repco", vm.TeamLine); // the player's seat team (StockLivery2)
        Assert.Contains("1967", vm.TeamLine);
    }

    [Fact]
    public void RepeatedDriverSelectionAndBindingReads_DoNotRebuildSessionProjections()
    {
        var session = new FakeCareerSession
        {
            Dossier = DossierAt(level: 3),
            Cp = 7,
            RespecTokenCount = 2,
            TeamName = "Madonna",
        };
        using var hub = new HubViewModel(session);
        var driver = hub.Tabs.Single(tab => tab.Key == HubViewModel.DriverTabKey);
        var race = hub.Tabs.Single(tab => tab.Key == HubViewModel.RaceTabKey);

        int dossierReads = session.CharacterDossierReadCount;
        int treeReads = session.SkillTreeReadCount;
        int cpReads = session.AvailableCharacterCpReadCount;
        int respecReads = session.RespecTokenReadCount;
        int teamReads = session.PlayerTeamNameReadCount;
        var talentStats = hub.Dossier.TalentStatsView;
        var metaStats = hub.Dossier.MetaStatsView;

        // Mirror the duplicate binding reads WPF performs each time the large Driver DataTemplate is
        // reconstructed. Navigation/read-only rendering must use the last Refresh snapshot exclusively.
        for (int i = 0; i < 12; i++)
        {
            hub.SelectTabCommand.Execute(driver);
            Assert.Equal(7, hub.Dossier.SkillPointsAvailable);
            Assert.Equal(7, hub.Dossier.SkillPointsAfterReset);
            Assert.Equal(2, hub.Dossier.RespecTokens);
            Assert.Contains("Madonna", hub.Dossier.TeamLine);
            Assert.Same(talentStats, hub.Dossier.TalentStatsView);
            Assert.Same(metaStats, hub.Dossier.MetaStatsView);
            hub.SelectTabCommand.Execute(race);
        }

        Assert.Equal(dossierReads, session.CharacterDossierReadCount);
        Assert.Equal(treeReads, session.SkillTreeReadCount);
        Assert.Equal(cpReads, session.AvailableCharacterCpReadCount);
        Assert.Equal(respecReads, session.RespecTokenReadCount);
        Assert.Equal(teamReads, session.PlayerTeamNameReadCount);
    }

    [Fact]
    public void Dossier_SurfacesThePlayerPortraitCarAndSpecCard()
    {
        using var session = CreateCareer(Character());
        var vm = new DossierViewModel(session);

        Assert.Equal("BRA", session.CurrentPlayerCountryCode());
        // The team-coloured player portrait key (player.<team>) drives the dossier hero image.
        Assert.StartsWith("player.", vm.PlayerImageKey);
        // The car the player drives — its preview key is the seat's driver id.
        Assert.False(string.IsNullOrEmpty(vm.PlayerCarKey));
        // The test pack's car has no authored car-spec (only the five SMGP models ship one), so the
        // card is gracefully absent — proving the absent-tolerant wiring end to end.
        Assert.Null(vm.PlayerCarSpec);
    }

    [Fact]
    public void UpcomingRaceTab_IsRenamed_AndLeadsTheRail()
    {
        using var hub = new HubViewModel(CreateCareer(Character()));

        var raceTab = hub.Tabs.Single(t => t.Key == HubViewModel.RaceTabKey);
        Assert.Equal("Upcoming Race", raceTab.Title);
        // The Upcoming Race tab now shows in the rail (the header loop buttons are gone — the top is
        // reserved for the tycoon team mode) and leads it; its loop is walked with its own Continue.
        Assert.True(raceTab.ShowInRail);
        Assert.Same(raceTab, hub.Tabs[0]);

        // Every tab shows in the rail now.
        Assert.All(hub.Tabs, t => Assert.True(t.ShowInRail));
    }

    [Fact]
    public void CharacterFreeCareer_HubHasNoDriverTab()
    {
        using var hub = new HubViewModel(CreateCareer(character: null));

        Assert.DoesNotContain(hub.Tabs, t => t.Key == HubViewModel.DriverTabKey);
        Assert.False(hub.Dossier.HasCharacter);
    }

    [Fact]
    public void Dossier_LevelUpMomentAccumulatesUntilAcknowledged()
    {
        var session = new FakeCareerSession { Dossier = DossierAt(level: 1) };
        var vm = new DossierViewModel(session);

        session.Dossier = DossierAt(level: 3);
        vm.Refresh();

        Assert.True(vm.LevelUpPending);
        Assert.Equal(2, vm.LevelsGained);
        vm.AcknowledgeLevelUpCommand.Execute(null);
        Assert.False(vm.LevelUpPending);
        Assert.Equal(0, vm.LevelsGained);
    }

    [Fact]
    public void Dossier_UnlockAndRespecCommandsUseThePublishedSessionSeams()
    {
        var effect = new CharacterEffectLine
        {
            Kind = "benefit",
            Classification = CharacterEffectClass.Car,
            ClassificationLabel = "CAR",
            Text = "Lowers drag by 1%",
        };
        var node = new SkillNode
        {
            Id = "rain_man", Name = "Rain Man", Kind = "perk", Cost = 1, Tier = 1,
            UnlockLevel = 1, Requires = [], Benefits = ["Wet pace"], Drawbacks = [],
            Effects = [effect], State = SkillNodeState.Unlockable, LockReason = "",
        };
        var session = new FakeCareerSession
        {
            Dossier = DossierAt(2),
            Cp = 2,
            RespecTokenCount = 1,
            Tree = new SkillTreeSnapshot
            {
                Branches =
                [
                    new SkillBranch { Id = "weather", Name = "Weather", IsMeta = false, Nodes = [node] },
                ],
            },
        };
        var vm = new DossierViewModel(session);
        var branch = Assert.Single(vm.SkillTree);
        var projected = Assert.Single(branch.Nodes);

        Assert.Equal(["Wet pace"], projected.Benefits);
        Assert.Empty(projected.Drawbacks);
        Assert.Same(effect, Assert.Single(projected.Effects));
        Assert.Empty(branch.MasteryNodes);
        Assert.Equal(0, branch.OwnedMasteryCount);
        Assert.Equal(0, branch.TotalMasteryCount);
        Assert.Empty(vm.AttributeRails);
        vm.UnlockNodeCommand.Execute(projected);
        Assert.Equal(CharacterSpend.Perk("rain_man", 1), Assert.Single(session.Spends));

        session.Tree = new SkillTreeSnapshot
        {
            Branches =
            [
                new SkillBranch
                {
                    Id = "weather", Name = "Weather", IsMeta = false,
                    Nodes = [node with { State = SkillNodeState.Owned }],
                },
            ],
        };
        vm.Refresh();
        vm.RespecNodeCommand.Execute(Assert.Single(Assert.Single(vm.SkillTree).Nodes));
        Assert.Equal("rain_man", Assert.Single(session.Respecs));
    }

    [Fact]
    public void Dossier_SeparatesNineMasteryTreesFromSevenCompleteAttributeRails()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var dna = RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        var catalog = MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"), rules, dna);
        var session = new FakeCareerSession
        {
            Dossier = DossierAt(level: 1),
            Cp = 10,
            Tree = MasterySkillGraph.Build(V2Character(), 1, 10, catalog, false),
        };

        var vm = new DossierViewModel(session);

        Assert.Equal(catalog.FamilyOrder, vm.SkillTree.Select(branch => branch.Id));
        Assert.Equal(9, vm.SkillTree.Count);
        Assert.All(vm.SkillTree, branch =>
        {
            Assert.Equal(10, branch.MasteryNodes.Count);
            Assert.Equal(10, branch.TotalMasteryCount);
            Assert.Equal(0, branch.OwnedMasteryCount);
            Assert.All(branch.MasteryNodes, node => Assert.Equal("mastery", node.Kind));
        });
        Assert.Equal(90, vm.SkillTree.Sum(branch => branch.TotalMasteryCount));

        Assert.Equal(catalog.AttributeRails.Select(rail => rail.Id), vm.AttributeRails.Select(rail => rail.Id));
        Assert.Equal(7, vm.AttributeRails.Count);
        var projectedNodes = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        for (int index = 0; index < vm.AttributeRails.Count; index++)
        {
            var definition = catalog.AttributeRails[index];
            var rail = vm.AttributeRails[index];
            Assert.Equal(definition.Name, rail.Name);
            Assert.Equal(definition.Stat, rail.StatId);
            Assert.Equal(17, rail.TotalCount);
            Assert.Equal(0, rail.OwnedCount);
            Assert.Equal(17, rail.Nodes.Count);
            Assert.All(rail.Nodes, node =>
            {
                Assert.Equal(definition.Id, node.RailId);
                Assert.Equal(definition.Name, node.RailName);
                Assert.Equal(definition.Stat, node.AttributeStatId);
                Assert.Same(projectedNodes[node.Id], node);
            });
        }
        Assert.Equal(119, vm.AttributeRails.Sum(rail => rail.TotalCount));
    }

    [Fact]
    public void Dossier_ClassifiedEffectsAreAdditiveAndPreservedEndToEnd()
    {
        var effect = new CharacterEffectLine
        {
            Kind = "drawback",
            Classification = CharacterEffectClass.Expectation,
            ClassificationLabel = "EXPECTATION",
            Text = "Raises the expected finish by one place",
            Condition = "When qualifying outside the top ten",
        };
        var perk = new DossierPerk
        {
            Id = "pressure_cooker",
            Name = "Pressure Cooker",
            Category = "mental",
            Description = "High expectations follow visible potential.",
            Cost = 2,
            Benefits = ["Extra reputation after strong finishes"],
            Drawbacks = ["A harder expected finish"],
            Effects = [effect],
        };
        var session = new FakeCareerSession
        {
            Dossier = DossierAt(level: 2) with { Perks = [perk] },
        };

        var vm = new DossierViewModel(session);
        var projected = Assert.Single(vm.Dossier!.Perks);

        Assert.Equal(perk.Benefits, projected.Benefits);
        Assert.Equal(perk.Drawbacks, projected.Drawbacks);
        Assert.Same(effect, Assert.Single(projected.Effects));
        Assert.Empty(new SkillNodeViewModel
        {
            Id = "legacy",
            Name = "Legacy node",
            Kind = "perk",
            Cost = 1,
            Tier = 1,
            UnlockLevel = 1,
            RequiresLabels = [],
            Benefits = [],
            Drawbacks = [],
            State = SkillNodeState.Locked,
            LockReason = "Locked",
        }.Effects);
    }

    [Fact]
    public void Dossier_V2QueueIsLocal_ResetIsFree_AndConfirmSendsOneOrderedPlan()
    {
        var session = SkillPlanSession();
        var vm = new DossierViewModel(session);
        var first = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_rhythm");

        vm.OpenSkillNodeCommand.Execute(first);
        Assert.Same(first, vm.SelectedSkillNode);
        Assert.True(vm.SkillNodeDetailOpen);

        vm.QueueSkillNodeCommand.Execute(first);
        Assert.True(vm.SkillPlanDirty);
        Assert.Equal(1, vm.PendingSkillPointCost);
        Assert.Equal(4, vm.SkillPointsAfterPlan);
        Assert.Equal("pace_rhythm", Assert.Single(vm.PendingSkillNodes).Id);
        Assert.Empty(session.AppliedSkillPlans);

        var second = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_qualifying_sequence");
        Assert.True(second.CanUnlock);
        vm.QueueSkillNodeCommand.Execute(second);
        Assert.Equal(["pace_rhythm", "pace_qualifying_sequence"],
            vm.PendingSkillNodes.Select(node => node.Id));
        Assert.Equal(3, vm.PendingSkillPointCost);
        Assert.Equal(2, vm.SkillPointsAfterPlan);

        vm.RemovePendingSkillNodeCommand.Execute(vm.PendingSkillNodes[0]);
        Assert.False(vm.SkillPlanDirty);
        Assert.Empty(vm.PendingSkillNodes);
        Assert.Empty(session.AppliedSkillPlans);

        first = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_rhythm");
        vm.QueueSkillNodeCommand.Execute(first);
        vm.ConfirmSkillPlanCommand.Execute(null);

        Assert.Equal(["pace_rhythm"], Assert.Single(session.AppliedSkillPlans));
        Assert.False(vm.SkillPlanDirty);
        Assert.Empty(vm.PendingSkillNodes);
        Assert.False(vm.SkillNodeDetailOpen);
    }

    [Fact]
    public void Dossier_FailedConfirmRetainsRecoverableLocalPlan()
    {
        var session = SkillPlanSession();
        session.ApplySkillPlanThrows = new InvalidOperationException("career review changed");
        var vm = new DossierViewModel(session);
        var first = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_rhythm");
        vm.QueueSkillNodeCommand.Execute(first);

        vm.ConfirmSkillPlanCommand.Execute(null);

        Assert.True(vm.SkillPlanDirty);
        Assert.Equal("pace_rhythm", Assert.Single(vm.PendingSkillNodes).Id);
        Assert.Equal("career review changed", vm.SkillActionError);
        Assert.Empty(session.AppliedSkillPlans);

        vm.ResetSkillPlanCommand.Execute(null);
        Assert.False(vm.SkillPlanDirty);
        Assert.Null(vm.SkillActionError);
        Assert.Empty(session.AppliedSkillPlans);
    }

    [Fact]
    public void Dossier_CommittedResetHasSeparateConfirmationAndClearsLocalPlanOnlyOnSuccess()
    {
        var session = SkillPlanSession();
        session.SkillResetQuote = ResetQuote(canApply: true);
        var vm = new DossierViewModel(session);
        var first = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_rhythm");
        vm.QueueSkillNodeCommand.Execute(first);

        vm.OpenSkillResetConfirmationCommand.Execute(null);

        Assert.True(vm.SkillResetConfirmationOpen);
        Assert.True(vm.CanResetCommittedSkillTree);
        Assert.Equal(500, vm.SkillResetCost);
        Assert.Equal(4_500, vm.AvailableResetXpAfter);
        Assert.Equal(3, vm.SkillPointsRefunded);
        Assert.Equal(5, vm.SkillPointsAfterReset);
        Assert.Equal(0, session.AppliedSkillResetCount);
        Assert.True(vm.SkillPlanDirty);

        vm.CloseSkillResetConfirmationCommand.Execute(null);
        Assert.False(vm.SkillResetConfirmationOpen);
        Assert.Equal(0, session.AppliedSkillResetCount);

        vm.OpenSkillResetConfirmationCommand.Execute(null);
        vm.ConfirmSkillResetCommand.Execute(null);

        Assert.Equal(1, session.AppliedSkillResetCount);
        Assert.False(vm.SkillResetConfirmationOpen);
        Assert.False(vm.SkillPlanDirty);
        Assert.Empty(vm.PendingSkillNodes);
    }

    [Fact]
    public void Dossier_OpeningResetConfirmationNotifiesEveryComputedQuoteField()
    {
        var session = SkillPlanSession();
        session.SkillResetQuote = ResetQuote(canApply: false);
        var vm = new DossierViewModel(session);
        session.SkillResetQuote = ResetQuote(canApply: true) with
        {
            AvailableResetXp = 8_000,
            Cost = 750,
            AvailableResetXpAfter = 7_250,
            SkillPointsRefunded = 7,
            SkillPointsAfterReset = 9,
            AcquisitionCount = 4,
        };
        var changed = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } name)
                changed.Add(name);
        };

        vm.OpenSkillResetConfirmationCommand.Execute(null);

        Assert.True(vm.SkillResetConfirmationOpen);
        Assert.Equal(8_000, vm.AvailableResetXp);
        Assert.Equal(750, vm.SkillResetCost);
        Assert.Equal(7_250, vm.AvailableResetXpAfter);
        Assert.Equal(7, vm.SkillPointsRefunded);
        Assert.Equal(9, vm.SkillPointsAfterReset);
        Assert.Equal(4, vm.SkillResetAcquisitionCount);
        Assert.True(vm.CanResetCommittedSkillTree);
        Assert.Equal("", vm.SkillResetBlockReason);
        Assert.Contains(nameof(DossierViewModel.SkillResetPreview), changed);
        Assert.Contains(nameof(DossierViewModel.AvailableResetXp), changed);
        Assert.Contains(nameof(DossierViewModel.SkillResetCost), changed);
        Assert.Contains(nameof(DossierViewModel.AvailableResetXpAfter), changed);
        Assert.Contains(nameof(DossierViewModel.SkillPointsRefunded), changed);
        Assert.Contains(nameof(DossierViewModel.SkillPointsAfterReset), changed);
        Assert.Contains(nameof(DossierViewModel.SkillResetAcquisitionCount), changed);
        Assert.Contains(nameof(DossierViewModel.CanResetCommittedSkillTree), changed);
        Assert.Contains(nameof(DossierViewModel.SkillResetBlockReason), changed);
    }

    [Fact]
    public void Dossier_FailedCommittedResetPreservesLocalPlanAndConfirmation()
    {
        var session = SkillPlanSession();
        session.SkillResetQuote = ResetQuote(canApply: true);
        session.ApplySkillResetThrows = new InvalidOperationException("reset state changed");
        var vm = new DossierViewModel(session);
        var first = vm.SkillTree.SelectMany(branch => branch.Nodes)
            .Single(node => node.Id == "pace_rhythm");
        vm.QueueSkillNodeCommand.Execute(first);
        vm.OpenSkillResetConfirmationCommand.Execute(null);

        vm.ConfirmSkillResetCommand.Execute(null);

        Assert.Equal(0, session.AppliedSkillResetCount);
        Assert.True(vm.SkillResetConfirmationOpen);
        Assert.True(vm.SkillPlanDirty);
        Assert.Equal("pace_rhythm", Assert.Single(vm.PendingSkillNodes).Id);
        Assert.Equal("reset state changed", vm.SkillActionError);
    }

    private static SkillResetPreview ResetQuote(bool canApply) => new()
    {
        LifetimeXp = 5_000,
        AvailableResetXp = 5_000,
        Cost = 500,
        AvailableResetXpAfter = 4_500,
        SkillPointsRefunded = 3,
        SkillPointsAfterReset = 5,
        AcquisitionCount = 2,
        CanApply = canApply,
        BlockReason = canApply ? "" : "There is no committed skill tree to reset.",
    };

    private static FakeCareerSession SkillPlanSession()
    {
        var session = new FakeCareerSession
        {
            Dossier = DossierAt(level: 30),
            Cp = 5,
            Tree = PlanTree(firstPending: false, secondPending: false),
        };
        session.SkillPlanPreviewer = ordered =>
        {
            int cost = ordered.Sum(id => id == "pace_rhythm" ? 1 : 2);
            return new SkillPlanPreview
            {
                Input = new CharacterSkillPlanInput
                {
                    Entries = ordered.Select(id => new CharacterSkillPlanEntry
                    {
                        NodeId = id,
                        Kind = CharacterSkillPlanEntry.MasteryKind,
                        Cost = id == "pace_rhythm" ? 1 : 2,
                    }).ToArray(),
                    TotalCost = cost,
                },
                ProjectedTree = PlanTree(
                    firstPending: ordered.Contains("pace_rhythm", StringComparer.Ordinal),
                    secondPending: ordered.Contains("pace_qualifying_sequence", StringComparer.Ordinal)),
                SkillPointsAfterPlan = 5 - cost,
            };
        };
        return session;
    }

    private static SkillTreeSnapshot PlanTree(bool firstPending, bool secondPending)
    {
        var first = new SkillNode
        {
            Id = "pace_rhythm", Name = "Rhythm", Kind = "mastery", Cost = 1, Tier = 1,
            UnlockLevel = 1, Requires = [], Benefits = [], Drawbacks = [],
            State = firstPending ? SkillNodeState.Pending : SkillNodeState.Unlockable,
            LockReason = "",
        };
        var second = new SkillNode
        {
            Id = "pace_qualifying_sequence", Name = "Qualifying Sequence", Kind = "mastery",
            Cost = 2, Tier = 2, UnlockLevel = 30, Requires = [first.Id], Benefits = [], Drawbacks = [],
            State = secondPending
                ? SkillNodeState.Pending
                : firstPending ? SkillNodeState.Unlockable : SkillNodeState.Locked,
            LockReason = firstPending || secondPending ? "" : "Requires: Rhythm",
        };
        return new SkillTreeSnapshot
        {
            Branches =
            [
                new SkillBranch
                {
                    Id = "pace", Name = "Pace", IsMeta = false, Nodes = [first, second],
                },
            ],
        };
    }

    private static CharacterDossier DossierAt(int level) => new()
    {
        Name = "Nova Reyes",
        Level = level,
        Xp = 0,
        XpIntoLevel = 0,
        XpForNextLevel = 100,
        CpUnspent = 0,
        Stats =
        [
            new DossierStat("pace", "Pace", 0.5, Talent: true),
            new DossierStat("marketability", "Marketability", 0.5, Talent: false),
        ],
        Perks = [],
    };

    private static CharacterProfile V2Character()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.50,
            ["oneLap"] = 0.50,
            ["craft"] = 0.50,
            ["racecraft"] = 0.50,
            ["adaptability"] = 0.50,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.50,
        };
        return new CharacterProfile
        {
            Stats = talent.Concat(meta)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = [],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = [],
            },
        };
    }
}
