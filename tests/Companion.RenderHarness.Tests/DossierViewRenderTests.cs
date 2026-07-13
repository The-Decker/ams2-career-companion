using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Companion.App.Views;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Driver dossier over a real <see cref="CharacterDossier"/> — the
/// view binds to a host's <c>Dossier</c> property, so a lightweight stand-in exercises the view layer
/// (stat bars, perk cards, the level progress bar) without a full session. Self-skips off Windows.</summary>
public sealed class DossierViewRenderTests
{
    /// <summary>The dossier view binds <c>{Binding Dossier}</c> on its DataContext — this stands in
    /// for the DossierViewModel.</summary>
    private sealed class DossierHost
    {
        public required CharacterDossier Dossier { get; init; }
        public string TeamLine { get; init; } = "Madonna · 1990";
        public string? PlayerImageKey => null;
        public string? PlayerCarKey => null;
        public object? PlayerCarSpec => null;
        public bool HasSmgpNarrative { get; init; }
        public string NarrativeIntro { get; init; } = "";
        public IReadOnlyList<SmgpCareerBeat> Timeline { get; init; } = [];

        public bool LevelUpPending { get; init; } = true;
        public int LevelsGained { get; init; } = 1;
        public int SkillPointsAvailable => 4;
        public int RespecTokens => 1;
        public required IReadOnlyList<SkillBranchViewModel> SkillTree { get; init; }
        public ICommand AcknowledgeLevelUpCommand { get; } = new StubCommand();
        public ICommand UnlockNodeCommand { get; } = new StubCommand();
        public ICommand RespecNodeCommand { get; } = new StubCommand();
        public required IReadOnlyList<DossierStat> TalentStatsView { get; init; }
        public required IReadOnlyList<DossierStat> MetaStatsView { get; init; }
        public string AvailabilityLabel => Dossier.AvailabilityLabel;
    }

    private sealed class StubCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    private static CharacterDossier Dossier() => new()
    {
        Name = "Nova Reyes",
        Age = 24,
        Level = 3,
        Xp = 250,
        XpIntoLevel = 15,
        XpForNextLevel = 182,
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
                Benefits = ["Stronger wet-weather pace — in the wet"],
                Drawbacks = ["Weaker one-lap pace — in the dry"],
            },
        ],
        InjuryRisk = "Moderate",
        Availability = AvailabilityStatus.Injured,
        AvailabilityLabel = "Injured — out 2 races",
    };

    private static SkillNodeViewModel Node(
        string id, string name, SkillNodeState state, int tier, string lockReason = "") => new()
    {
        Id = id,
        Name = name,
        Kind = "perk",
        Cost = tier,
        Tier = tier,
        UnlockLevel = tier * 2,
        RequiresLabels = tier > 1 ? ["Slipstream Artist"] : [],
        Benefits = ["Improves pace when the race is on the line"],
        Drawbacks = ["Carries a small consistency trade-off"],
        State = state,
        LockReason = lockReason,
    };

    private static IReadOnlyList<SkillBranchViewModel> Tree() =>
    [
        new SkillBranchViewModel
        {
            Id = "racecraft",
            Name = "Racecraft",
            IsMeta = false,
            Nodes =
            [
                Node("slipstream_artist", "Slipstream Artist", SkillNodeState.Owned, 1),
                Node("late_braker", "Late Braker", SkillNodeState.Unlockable, 2),
                Node("apex_hunter", "Apex Hunter", SkillNodeState.Locked, 3, "Reach level 6"),
            ],
        },
        new SkillBranchViewModel
        {
            Id = "media",
            Name = "Media Presence",
            IsMeta = true,
            Nodes =
            [
                Node("press_room", "Press Room Poise", SkillNodeState.Unlockable, 1),
            ],
        },
    ];

    private static DossierHost Host(bool withNarrative = false)
    {
        var dossier = Dossier();
        return new DossierHost
        {
            Dossier = dossier,
            SkillTree = Tree(),
            TalentStatsView = dossier.Stats.Where(stat => stat.Talent).ToArray(),
            MetaStatsView = dossier.Stats.Where(stat => !stat.Talent).ToArray(),
            HasSmgpNarrative = withNarrative,
            NarrativeIntro = withNarrative ? "A rookie season is becoming a real campaign." : "",
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
}
