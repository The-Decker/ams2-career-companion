using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

public sealed class CharacterSkillResetReplayTests
{
    [Fact]
    public void SamePackSecondSeasonReplaysPlanResetAndReplacementByteIdentically()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var fixture = ResetReplayCareerFixture.Create(db);
        PlayerCareerState storedStart = StateStore.ReadPlayerState(
            db,
            fixture.Season2,
            StateStore.StageStart)!;
        string before = JsonSerializer.Serialize(storedStart, CoreJson.Options);

        IReadOnlyList<CharacterSkillDevelopmentAction> development =
            ReplayService.ReadCharacterSkillDevelopment(db, fixture.Season1);
        Assert.Collection(
            development,
            action => Assert.IsType<CharacterSkillPlanAction>(action),
            action => Assert.IsType<CharacterSkillResetAction>(action),
            action => Assert.IsType<CharacterSkillPlanAction>(action));
        Assert.DoesNotContain("pace_rhythm", storedStart.Character!.AcquiredSkillIds ?? []);
        Assert.DoesNotContain("attribute_pace_01", storedStart.Character.AcquiredAttributeNodeIds ?? []);
        Assert.Equal(["racecraft_clean_overtake"], storedStart.Character.AcquiredSkillIds);
        Assert.Equal(["attribute_racecraft_01"], storedStart.Character.AcquiredAttributeNodeIds);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, storedStart.Character.MasteryEffectsVersion);
        Assert.Equal(fixture.Replacement.TotalCost, storedStart.Character.SkillPointsSpent);
        Assert.Equal(fixture.Reset.XpCost, storedStart.Character.XpSpentOnResets);
        Assert.Equal(1, storedStart.Character.SkillResetCount);
        Assert.Equal(fixture.Season1End.Xp, storedStart.Xp);
        Assert.Equal(fixture.Season1End.Level, storedStart.Level);

        ReplayReport replay = ReplayService.Resimulate(
            db,
            fixture.Pack,
            DataCareerFixture.MasterSeed,
            fixture.Inputs);

        Assert.True(
            replay.Identical,
            $"diverged: {replay.FirstDivergence?.Reason} " +
            $"stored={replay.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={replay.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Equal(
            before,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, fixture.Season2, StateStore.StageStart),
                CoreJson.Options));
    }

    [Fact]
    public void TamperedResetCostFailsValidationWithoutChangingStoredSeasonStart()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var fixture = ResetReplayCareerFixture.Create(db);
        string before = JsonSerializer.Serialize(
            StateStore.ReadPlayerState(db, fixture.Season2, StateStore.StageStart),
            CoreJson.Options);
        CharacterSkillResetInput tampered = fixture.Reset with
        {
            XpCost = checked(fixture.Reset.XpCost + 50),
        };
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE journal
                SET delta_json = @json
                WHERE season_id = @season AND phase = @phase;
                """;
            command.Parameters.AddWithValue(
                "@json",
                JsonSerializer.Serialize(tampered, CoreJson.Options));
            command.Parameters.AddWithValue("@season", fixture.Season1);
            command.Parameters.AddWithValue("@phase", JournalPhases.PlayerSkillReset);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        ReplayReport replay = ReplayService.Resimulate(
            db,
            fixture.Pack,
            DataCareerFixture.MasterSeed,
            fixture.Inputs);

        Assert.False(replay.Identical);
        Assert.NotNull(replay.FirstDivergence);
        Assert.Equal("skill-reset-validation", replay.FirstDivergence.Reason);
        Assert.Contains(
            "XP cost",
            replay.FirstDivergence.RegeneratedDeltaJson,
            StringComparison.Ordinal);
        Assert.Equal(
            before,
            JsonSerializer.Serialize(
                StateStore.ReadPlayerState(db, fixture.Season2, StateStore.StageStart),
                CoreJson.Options));
    }
}

internal sealed record ResetReplayCareerFixture(
    long Season1,
    long Season2,
    Companion.Core.Packs.SeasonPack Pack,
    ReplaySimInputs Inputs,
    PlayerCareerState Season1End,
    CharacterSkillResetInput Reset,
    CharacterSkillPlanInput Replacement)
{
    public static ResetReplayCareerFixture Create(CareerDatabase db)
    {
        var (season1, pack) = DataCareerFixture.SetupCareer(db);
        CharacterRules rules = SkillPlanBoundaryTestData.Rules();
        MasterySkillCatalog catalog = SkillPlanBoundaryTestData.Catalog();
        ReplaySimInputs inputs = DataCareerFixture.Inputs() with
        {
            CharacterRules = rules,
            MasterySkills = catalog,
        };
        PlayerCareerState playerStart = SkillPlanBoundaryTestData.Player() with
        {
            CurrentTeamId = "team.mid",
            LiveryName = DataCareerFixture.PlayerLivery,
            Character = SkillPlanBoundaryTestData.Player().Character! with
            {
                RacingDnaId = "dna_prodigy",
                RacingDnaVersion = 1,
            },
        };
        StateStore.UpsertPlayerState(db, season1, StateStore.StageStart, playerStart);

        foreach (RoundResult round in DataCareerFixture.Rounds())
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

        PlayerCareerState playerEnd = StateStore.ReadPlayerState(
            db,
            season1,
            StateStore.StageEnd)!;
        CharacterSkillPlanInput first = MasterySkillPlan.Prepare(
            playerEnd.Character!,
            ["pace_rhythm", "attribute_pace_01"],
            SkillPlanBoundaryTestData.Facts(playerEnd),
            catalog);
        PlayerCareerState afterFirst = CharacterSkillDevelopmentTransition.Apply(
            playerEnd,
            [new CharacterSkillPlanAction(first)],
            rules,
            catalog);
        CharacterSkillResetInput reset = CharacterSkillReset.Prepare(afterFirst, rules, catalog);
        PlayerCareerState afterReset = CharacterSkillReset.Apply(afterFirst, reset, rules, catalog);
        CharacterSkillPlanInput replacement = MasterySkillPlan.Prepare(
            afterReset.Character!,
            ["racecraft_clean_overtake", "attribute_racecraft_01"],
            SkillPlanBoundaryTestData.Facts(afterReset),
            catalog);
        CharacterSkillDevelopmentAction[] actions =
        [
            new CharacterSkillPlanAction(first),
            new CharacterSkillResetAction(reset),
            new CharacterSkillPlanAction(replacement),
        ];
        foreach (CharacterSkillDevelopmentAction action in actions)
        {
            JournalStore.Append(
                db,
                season1,
                round: null,
                new JournalEvent
                {
                    Phase = action is CharacterSkillPlanAction
                        ? JournalPhases.PlayerSkillPlan
                        : JournalPhases.PlayerSkillReset,
                    Entity = "player",
                    DeltaJson = action switch
                    {
                        CharacterSkillPlanAction plan => JsonSerializer.Serialize(plan.Input, CoreJson.Options),
                        CharacterSkillResetAction skillReset => JsonSerializer.Serialize(
                            skillReset.Input,
                            CoreJson.Options),
                        _ => throw new NotSupportedException(),
                    },
                    Cause = "development",
                },
                DataCareerFixture.Utc);
        }

        SeasonStartStates derived = SeasonRollover.Derive(
            playerEnd,
            StateStore.ReadDriverStates(db, season1, StateStore.StageEnd),
            StateStore.ReadTeamStates(db, season1, StateStore.StageEnd),
            acceptedTeamId: "team.mid",
            playerLiveryName: DataCareerFixture.PlayerLivery,
            characterRules: rules,
            masterySkills: catalog,
            skillDevelopment: actions);
        long season2 = CareerStore.StartCarryoverSeason(
            db,
            derived,
            1968,
            pack.Manifest.PackId,
            pack.Manifest.Version,
            DataCareerFixture.Utc);
        RoundResult firstRound = DataCareerFixture.Rounds()[0];
        ReplayService.ImportAndFoldRound(
            db,
            season2,
            pack,
            DataCareerFixture.MasterSeed,
            inputs,
            firstRound.Round,
            DataCareerFixture.Envelope(firstRound),
            DataCareerFixture.Utc);

        return new ResetReplayCareerFixture(
            season1,
            season2,
            pack,
            inputs,
            playerEnd,
            reset,
            replacement);
    }
}
