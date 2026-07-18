using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Career;
using Companion.Core.Determinism;

namespace Companion.Tests.Career;

public class RoundUpdateTests
{
    private static CampaignProgressionPlan VersionTwoPlan() =>
        CampaignProgressionPlan.CreateSmgp(new PinnedCampaignSeason
        {
            PackId = "smgp-1",
            PackVersion = "1.0.0",
            Sha256 = new string('a', 64),
            Year = 1990,
            ChampionshipRoundCount = 16,
        });

    private static CampaignProgressionPlan FractionalVersionTwoPlan() =>
        CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            1967,
            2020,
            [
                new PinnedCampaignSeason
                {
                    PackId = "f1-1967", PackVersion = "1.0.0", Sha256 = new string('a', 64),
                    Year = 1967, ChampionshipRoundCount = 11,
                },
                new PinnedCampaignSeason
                {
                    PackId = "f1-1968", PackVersion = "1.0.0", Sha256 = new string('b', 64),
                    Year = 1968, ChampionshipRoundCount = 12,
                },
                new PinnedCampaignSeason
                {
                    PackId = "f1-2020", PackVersion = "1.0.0", Sha256 = new string('c', 64),
                    Year = 2020, ChampionshipRoundCount = 17,
                },
            ]);

    private static CharacterProfile VersionTwoCharacter() => new()
    {
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.5,
            ["oneLap"] = 0.5,
            ["craft"] = 0.5,
            ["racecraft"] = 0.5,
            ["adaptability"] = 0.5,
            ["marketability"] = 0.5,
            ["durability"] = 0.5,
        },
        PerkIds = [],
        ProgressionVersion = CharacterLevelProgression.Level300Version,
    };

    private static RoundUpdateContext Context(
        int? finish,
        DnfCause? dnf = null,
        int? teammateFinish = 3,
        double reputation = 40.0,
        double opi = 0.0,
        double anchor = 0.0,
        int pointsPositions = 6,
        ulong seed = 42) => new()
    {
        Grid = CareerTestData.PlayerGrid(),
        Player = new PlayerCareerState
        {
            Reputation = reputation,
            Opi = opi,
            PaceAnchor = anchor,
            SeasonsCompleted = 1,
            CurrentTeamId = "team.mid",
            LiveryName = CareerTestData.PlayerLivery,
        },
        PlayerTeamTier = 3,
        PlayerFinish = finish,
        PlayerDnf = dnf,
        HasTeammate = true,
        TeammateFinish = teammateFinish,
        SliderUsed = 90.0,
        PointsPositions = pointsPositions,
        IsChampionshipRound = true,
        IsPrimaryRace = true,
        Streams = new StreamFactory(seed),
        Headlines = CareerTestData.LoadHeadlines(),
        PlayerName = "Pat Player",
    };

    [Fact]
    public void WinUpdatesEverythingAndEmitsAHeadline()
    {
        var result = RoundUpdate.Apply(Context(finish: 1));

        // Expected finish on this grid is 2; winning beats it.
        Assert.Equal(2, result.ExpectedFinish);
        Assert.Equal(OpiMath.Update(0.0, 2.0, 1.0), result.Player.Opi, 12);
        Assert.True(result.Player.Reputation > 40.0);
        Assert.True(result.Player.PaceAnchor > 0.0);
        Assert.NotNull(result.Headline);
        Assert.Contains("Pat Player", result.Headline);

        Assert.Equal(
            [
                JournalPhases.RaceResult,
                JournalPhases.PlayerOpi,
                JournalPhases.PlayerReputation,
                JournalPhases.PlayerPaceAnchor,
                JournalPhases.Headline,
            ],
            result.Events.Select(e => e.Phase).ToArray());
        Assert.All(result.Events, e => Assert.Equal("win", e.Cause));
    }

    [Fact]
    public void QualifyingAnchor_CalibratesAndEmits_OnlyWhenQualifyingRan()
    {
        // No qualifying (single-race): the anchor stays 0 and no qualiAnchor row is emitted —
        // the journal sequence is exactly what it was before Increment 2.
        var noQuali = RoundUpdate.Apply(Context(finish: 2));
        Assert.Equal(0.0, noQuali.Player.QualifyingAnchor);
        Assert.DoesNotContain(JournalPhases.PlayerQualiAnchor, noQuali.Events.Select(e => e.Phase));

        // With a qualifying position: the one-lap anchor calibrates and emits its row.
        var withQuali = RoundUpdate.Apply(Context(finish: 2) with { PlayerQualifyingPosition = 1 });
        double expected = PaceAnchorMath.Update(
            0.0, PaceAnchorMath.ImpliedPlayerQualiPace(CareerTestData.PlayerGrid(), 1, 90.0));
        Assert.Equal(expected, withQuali.Player.QualifyingAnchor, 12);
        Assert.Contains(JournalPhases.PlayerQualiAnchor, withQuali.Events.Select(e => e.Phase));

        // The qualiAnchor row sits in a fixed position, right after the pace anchor.
        var phases = withQuali.Events.Select(e => e.Phase).ToList();
        Assert.Equal(
            phases.IndexOf(JournalPhases.PlayerPaceAnchor) + 1,
            phases.IndexOf(JournalPhases.PlayerQualiAnchor));
    }

    [Fact]
    public void ApplyIsDeterministic()
    {
        var first = RoundUpdate.Apply(Context(finish: 2));
        var second = RoundUpdate.Apply(Context(finish: 2));

        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.Player, second.Player);
        Assert.Equal(first.Headline, second.Headline);
        Assert.Equal(first.RecommendedSlider, second.RecommendedSlider);
    }

    [Fact]
    public void MechanicalDnfLeavesThePaceAnchorAlone()
    {
        var result = RoundUpdate.Apply(Context(finish: null, dnf: DnfCause.Mechanical, anchor: 91.0));

        Assert.Equal(91.0, result.Player.PaceAnchor, 12);
        Assert.Equal("dnf-mechanical", result.Events[0].Cause);
        // No blame: OPI decays from 0 stays 0.
        Assert.Equal(0.0, result.Player.Opi, 12);
    }

    [Fact]
    public void DriverErrorDnfChargesTheFullGrid()
    {
        var result = RoundUpdate.Apply(Context(finish: null, dnf: DnfCause.DriverError));

        // expected 2, charged as grid size 4 ⇒ OPI = 0.2·(2−4) = −0.4.
        Assert.Equal(-0.4, result.Player.Opi, 12);
        Assert.Equal("dnf-driver-error", result.Events[0].Cause);
        Assert.True(result.Player.Reputation < 40.0);
    }

    [Fact]
    public void BeatingTheTeammateEarnsTheFlatBonus()
    {
        var beat = RoundUpdate.Apply(Context(finish: 2, teammateFinish: 3));
        var beaten = RoundUpdate.Apply(Context(finish: 2, teammateFinish: 1));

        Assert.Equal(1.0, beat.Player.Reputation - beaten.Player.Reputation, 12);
    }

    [Fact]
    public void FirstClassifiedRoundSeedsTheAnchorAndRecommendsASlider()
    {
        var result = RoundUpdate.Apply(Context(finish: 2, anchor: 0.0));

        // P2 ⇒ yardstick is the second-fastest AI (0.70) at slider 90 ⇒ 92.0.
        Assert.Equal(92.0, result.Player.PaceAnchor, 12);
        // Recommendation aims the median AI (0.70) at the anchor: 92 + 5 − 7 = 90.
        Assert.Equal(90, result.RecommendedSlider);
        Assert.InRange(result.RecommendedSlider, DifficultyModel.MinSlider, DifficultyModel.MaxSlider);
    }

    [Fact]
    public void UncalibratedDnfRoundRecommendsTheSliderUsed()
    {
        var result = RoundUpdate.Apply(Context(finish: null, dnf: DnfCause.Mechanical, anchor: 0.0));
        Assert.Equal(90, result.RecommendedSlider);
    }

    [Fact]
    public void PointsCauseFollowsTheRoundsResolvedScoringNotATopSix()
    {
        // Expected finish on this grid is 2; P4 is within |delta| < 3 of it, so the cause
        // falls through to the points cutoff, which comes from the round's resolved scoring
        // definition, not a hard-coded top-6.
        var sixScorers = RoundUpdate.Apply(Context(finish: 4, teammateFinish: null, pointsPositions: 6));
        Assert.Equal("points", sixScorers.Events[0].Cause);

        var threeScorers = RoundUpdate.Apply(Context(finish: 4, teammateFinish: null, pointsPositions: 3));
        Assert.Equal("midfield", threeScorers.Events[0].Cause);
    }

    [Fact]
    public void Level300Progression_ReportsLevel300InThePlayerXpJournalRow()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var character = VersionTwoCharacter();
        var context = Context(finish: 1);

        var result = RoundUpdate.Apply(context with
        {
            Player = context.Player with
            {
                Character = character,
                Level = 299,
                Xp = 14_950,
                ExperienceMode = CareerExperienceModes.Smgp,
                CampaignProgressionPlan = VersionTwoPlan(),
            },
            CharacterRules = rules,
        });

        var xpEvent = Assert.Single(result.Events, e => e.Phase == JournalPhases.PlayerXp);
        using var payload = JsonDocument.Parse(xpEvent.DeltaJson);
        Assert.True(payload.RootElement.GetProperty("round").GetInt32() > 0);
        Assert.Equal(300, payload.RootElement.GetProperty("level").GetInt32());
        Assert.Equal(300, result.Player.Level);
    }

    [Fact]
    public void LegacyXpRow_IgnoresVersionTwoEligibilityGate_AndKeepsItsExactShape()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var character = VersionTwoCharacter() with
        {
            ProgressionVersion = CharacterLevelProgression.EraCappedVersion,
        };
        var context = Context(finish: 1);

        var result = RoundUpdate.Apply(context with
        {
            IsChampionshipRound = false,
            IsPrimaryRace = false,
            Player = context.Player with { Character = character, Level = 1 },
            CharacterRules = rules,
        });

        var xpEvent = Assert.Single(result.Events, e => e.Phase == JournalPhases.PlayerXp);
        using var payload = JsonDocument.Parse(xpEvent.DeltaJson);
        Assert.Equal(
            ["from", "to", "round", "level"],
            payload.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.True(payload.RootElement.GetProperty("round").GetInt32() > 0);
        Assert.True(result.Player.Xp > context.Player.Xp);
    }

    [Fact]
    public void VersionTwoChampionshipPrimaryRace_AppliesPinnedScaleAndPersistsCarry()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var plan = FractionalVersionTwoPlan();
        var context = Context(finish: 1);
        var player = context.Player with
        {
            Character = VersionTwoCharacter(),
            Level = 1,
            Xp = 0,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            CampaignProgressionPlan = plan,
            XpScaleRemainder = 3,
        };

        var result = RoundUpdate.Apply(context with
        {
            Player = player,
            CharacterRules = rules,
        });

        var xpEvent = Assert.Single(result.Events, e => e.Phase == JournalPhases.PlayerXp);
        using var payload = JsonDocument.Parse(xpEvent.DeltaJson);
        var root = payload.RootElement;
        int signedRawXp = root.GetProperty("signedRawXp").GetInt32();
        var expected = CharacterProgressionV2Math.NormalizeXpAward(
            signedRawXp, true, player.XpScaleRemainder, plan);

        Assert.Equal(
            ["from", "to", "round", "level", "signedRawXp", "eligibleRawXp", "appliedXp",
                "remainderBefore", "remainderAfter"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(signedRawXp, root.GetProperty("round").GetInt32());
        Assert.Equal(expected.EligibleRawXp, root.GetProperty("eligibleRawXp").GetInt64());
        Assert.Equal(expected.AppliedXp, root.GetProperty("appliedXp").GetInt64());
        Assert.Equal(expected.RemainderAfter, result.Player.XpScaleRemainder);
        Assert.Equal(player.Xp + expected.AppliedXp, result.Player.Xp);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void VersionTwoIneligibleRace_JournalsRawButAppliesZeroAndPreservesCarry(
        bool isChampionshipRound,
        bool isPrimaryRace)
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var plan = FractionalVersionTwoPlan();
        var context = Context(finish: 1);
        var player = context.Player with
        {
            Character = VersionTwoCharacter(),
            Level = 1,
            Xp = 0,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            CampaignProgressionPlan = plan,
            XpScaleRemainder = 3,
        };

        var result = RoundUpdate.Apply(context with
        {
            Player = player,
            CharacterRules = rules,
            IsChampionshipRound = isChampionshipRound,
            IsPrimaryRace = isPrimaryRace,
        });

        var xpEvent = Assert.Single(result.Events, e => e.Phase == JournalPhases.PlayerXp);
        using var payload = JsonDocument.Parse(xpEvent.DeltaJson);
        var root = payload.RootElement;
        Assert.True(root.GetProperty("signedRawXp").GetInt64() > 0);
        Assert.Equal(0, root.GetProperty("eligibleRawXp").GetInt64());
        Assert.Equal(0, root.GetProperty("appliedXp").GetInt64());
        Assert.Equal(player.Xp, result.Player.Xp);
        Assert.Equal(player.XpScaleRemainder, result.Player.XpScaleRemainder);
    }

    [Fact]
    public void VersionTwoXpFailsClosedWithoutMatchingPinnedPlan()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var context = Context(finish: 1);
        var player = context.Player with
        {
            Character = VersionTwoCharacter(),
            Level = 1,
            ExperienceMode = CareerExperienceModes.Smgp,
        };

        Assert.Throws<InvalidOperationException>(() => RoundUpdate.Apply(context with
        {
            Player = player,
            CharacterRules = rules,
        }));
        Assert.Throws<InvalidOperationException>(() => RoundUpdate.Apply(context with
        {
            Player = player with { CampaignProgressionPlan = VersionTwoPlan(), ExperienceMode = CareerExperienceModes.GrandPrixDynasty },
            CharacterRules = rules,
        }));
    }
}
