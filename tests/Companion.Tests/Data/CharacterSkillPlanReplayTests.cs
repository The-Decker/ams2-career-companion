using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

public sealed class CharacterSkillPlanReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamePackSecondSeasonReplaysStoredSkillPlanByteIdentically(bool legacyEffectsEnvelope)
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, pack) = DataCareerFixture.SetupCareer(db);
        var rules = SkillPlanBoundaryTestData.Rules();
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var inputs = DataCareerFixture.Inputs() with
        {
            CharacterRules = rules,
            MasterySkills = catalog,
        };
        var playerStart = SkillPlanBoundaryTestData.Player() with
        {
            CurrentTeamId = "team.mid",
            LiveryName = DataCareerFixture.PlayerLivery,
        };
        StateStore.UpsertPlayerState(db, season1, StateStore.StageStart, playerStart);

        foreach (var round in DataCareerFixture.Rounds())
        {
            ReplayService.ImportAndFoldRound(
                db,
                season1,
                pack,
                DataCareerFixture.MasterSeed,
                inputs,
                round.Round,
                DataCareerFixture.Envelope(round),
                DataCareerFixture.Utc);
        }
        ReplayService.RunSeasonEnd(
            db,
            season1,
            pack,
            DataCareerFixture.MasterSeed,
            inputs,
            DataCareerFixture.Utc);
        StateStore.SetOfferAccepted(db, season1, "team.mid");

        var playerEnd = StateStore.ReadPlayerState(db, season1, StateStore.StageEnd)!;
        var facts = SkillPlanBoundaryTestData.Facts(playerEnd);
        Assert.True(facts.AvailableSkillPoints >= 3);
        var preparedPlan = MasterySkillPlan.Prepare(
            playerEnd.Character!,
            ["pace_rhythm", "v2_media_darling"],
            facts,
            catalog);
        var plan = legacyEffectsEnvelope
            ? preparedPlan with { EffectsVersion = 0 }
            : preparedPlan;
        JournalStore.Append(
            db,
            season1,
            round: null,
            new JournalEvent
            {
                Phase = JournalPhases.PlayerSkillPlan,
                Entity = "player",
                DeltaJson = JsonSerializer.Serialize(plan, CoreJson.Options),
                Cause = "development",
            },
            DataCareerFixture.Utc);

        var derived = SeasonRollover.Derive(
            playerEnd,
            StateStore.ReadDriverStates(db, season1, StateStore.StageEnd),
            StateStore.ReadTeamStates(db, season1, StateStore.StageEnd),
            acceptedTeamId: "team.mid",
            playerLiveryName: DataCareerFixture.PlayerLivery,
            skillPlans: [plan],
            masterySkills: catalog);
        long season2 = CareerStore.StartCarryoverSeason(
            db,
            derived,
            1968,
            pack.Manifest.PackId,
            pack.Manifest.Version,
            DataCareerFixture.Utc);
        foreach (RoundResult round in DataCareerFixture.Rounds())
        {
            ReplayService.ImportAndFoldRound(
                db,
                season2,
                pack,
                DataCareerFixture.MasterSeed,
                inputs,
                round.Round,
                DataCareerFixture.Envelope(round),
                DataCareerFixture.Utc);
        }
        ReplayService.RunSeasonEnd(
            db,
            season2,
            pack,
            DataCareerFixture.MasterSeed,
            inputs,
            DataCareerFixture.Utc);

        var storedStart = StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!;
        var storedEnd = StateStore.ReadPlayerState(db, season2, StateStore.StageEnd)!;
        string startBefore = JsonSerializer.Serialize(storedStart, CoreJson.Options);
        string endBefore = JsonSerializer.Serialize(storedEnd, CoreJson.Options);
        Assert.Equal(
            ["pace_rhythm", "v2_media_darling"],
            storedStart.Character!.AcquiredSkillIds);
        Assert.Equal(
            legacyEffectsEnvelope ? 0 : CharacterProfile.CurrentMasteryEffectsVersion,
            storedStart.Character.MasteryEffectsVersion);
        CharacterSkillPlanInput storedPlan = Assert.Single(
            ReplayService.ReadCharacterSkillPlans(db, season1));
        Assert.Equal(legacyEffectsEnvelope ? 0 : CharacterSkillPlanInput.CurrentEffectsVersion, storedPlan.EffectsVersion);

        var missingCatalog = ReplayService.Resimulate(
            db,
            pack,
            DataCareerFixture.MasterSeed,
            inputs with { MasterySkills = null });
        Assert.False(missingCatalog.Identical);
        Assert.Equal("skill-plan-validation", missingCatalog.FirstDivergence!.Reason);
        Assert.Contains(
            "pinned mastery-skill catalog",
            missingCatalog.FirstDivergence.RegeneratedDeltaJson,
            StringComparison.Ordinal);
        Assert.Equal(
            startBefore,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, season2, StateStore.StageStart),
                CoreJson.Options));
        Assert.Equal(
            endBefore,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, season2, StateStore.StageEnd),
                CoreJson.Options));

        var replay = ReplayService.Resimulate(
            db,
            pack,
            DataCareerFixture.MasterSeed,
            inputs);

        Assert.True(
            replay.Identical,
            $"diverged: {replay.FirstDivergence?.Reason} " +
            $"stored={replay.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={replay.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Equal(
            startBefore,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, season2, StateStore.StageStart),
                CoreJson.Options));
        Assert.Equal(
            endBefore,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, season2, StateStore.StageEnd),
                CoreJson.Options));
    }
}
