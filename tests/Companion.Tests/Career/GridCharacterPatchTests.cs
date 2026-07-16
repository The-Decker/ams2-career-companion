using Companion.Core.Character;
using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Career;

/// <summary>The player seat's character patch at grid resolve — the last step of the merge chain.
/// A null character (every pre-character career) resolves a byte-identical grid; a character patches
/// only the player seat's ratings + scalars, never an AI seat.</summary>
public sealed class GridCharacterPatchTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static MasterySkillCatalog Catalog(CharacterRules rules) =>
        MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"),
            rules,
            RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules));

    private static PlayerCharacterPatch Patch(
        IReadOnlyList<string> perkIds, double pace = 0.5, double oneLap = 0.5,
        int progressionVersion = CharacterLevelProgression.LegacyVersion,
        PlayerRoundConditionsInput? roundConditions = null,
        IReadOnlyList<string>? acquiredSkillIds = null,
        int masteryEffectsVersion = 0,
        MasterySkillCatalog? masterySkills = null,
        PlayerPerkModifiers? suppliedModifiers = null)
    {
        var rules = Rules();
        var profile = new CharacterProfile
        {
            ProgressionVersion = progressionVersion,
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = pace, ["oneLap"] = oneLap, ["craft"] = 0.5,
                ["racecraft"] = 0.5, ["adaptability"] = 0.5,
                ["marketability"] = 0.5, ["durability"] = 0.5,
            },
            PerkIds = perkIds,
            AcquiredSkillIds = acquiredSkillIds,
            MasteryEffectsVersion = masteryEffectsVersion,
        };
        IReadOnlySet<string>? active = roundConditions is null
            ? null
            : PlayerRoundConditions.ActiveConditions(roundConditions);
        return new PlayerCharacterPatch
        {
            Profile = profile,
            // Missing/invalid mastery inputs must cross the grid resolver's trust boundary rather
            // than failing while this test helper constructs the caller-supplied bundle.
            Modifiers = suppliedModifiers ??
                (masteryEffectsVersion == CharacterProfile.CurrentMasteryEffectsVersion &&
                 acquiredSkillIds is { Count: > 0 } && masterySkills is not null
                    ? CharacterModifierResolver.Resolve(profile, rules, masterySkills, active)
                    : PerkResolver.Resolve(profile, rules, active)),
            Rules = rules,
            MasterySkills = masterySkills,
            RoundConditions = roundConditions,
        };
    }

    private static SeasonPack PackWithPlayerCar(
        PackTeamPerformance teamPerformance,
        PackDriverCar? playerCar = null)
    {
        var pack = CareerTestData.Pack();
        return pack with
        {
            Teams = pack.Teams
                .Select(team => team.Id == "team.mid"
                    ? team with { Performance = teamPerformance }
                    : team)
                .ToList(),
            Drivers = pack.Drivers
                .Select(driver => driver.Id == CareerTestData.PlayerDriverId
                    ? driver with { Car = playerCar }
                    : driver)
                .ToList(),
        };
    }

    private static void AssertAiSeatsUnchanged(GridPlan baseline, GridPlan patched)
    {
        foreach (var ai in patched.Seats.Where(seat => !seat.IsPlayer))
        {
            var baseAi = baseline.Seats.Single(seat => seat.DriverId == ai.DriverId);
            Assert.Equal(baseAi, ai);
        }
    }

    [Fact]
    public void Resolve_WithoutCharacter_IsByteIdenticalToTheShippedGrid()
    {
        var pack = CareerTestData.Pack();
        var plain = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery });
        var nullChar = RoundGridResolver.Resolve(pack, 1,
            new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery, Character = null });

        // Seat-by-seat value equality (GridPlan.Seats is a List, so GridPlan.Equals would compare
        // the list by reference — the meaningful comparison is per-seat, where GridSeat is a record).
        Assert.Equal(plain.Seats, nullChar.Seats);
    }

    [Fact]
    public void Resolve_WithCharacter_PatchesOnlyThePlayerSeatRatings()
    {
        var pack = CareerTestData.Pack();
        var baseline = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery });
        var patched = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(["sunday_driver"], pace: 0.8, oneLap: 0.4),
        });

        var basePlayer = baseline.Seats.Single(s => s.IsPlayer);
        var patchedPlayer = patched.Seats.Single(s => s.IsPlayer);

        // pace 0.8 → raceSkill 0.35 + 0.55*0.8 = 0.79, + sunday_driver +0.06 = 0.85.
        Assert.Equal(0.85, patchedPlayer.Ratings.RaceSkill, 6);
        // oneLap 0.4 → qualifyingSkill 0.57, − sunday_driver 0.06 = 0.51.
        Assert.Equal(0.51, patchedPlayer.Ratings.QualifyingSkill, 6);
        Assert.NotEqual(basePlayer.Ratings.RaceSkill, patchedPlayer.Ratings.RaceSkill);

        // Every AI seat is untouched by the character.
        AssertAiSeatsUnchanged(baseline, patched);
    }

    [Fact]
    public void Resolve_WithCarScalarPerk_PatchesOnlyThePlayerSeatScalars()
    {
        var pack = CareerTestData.Pack();
        var baseline = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery });
        var patched = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(["engineers_favorite"]), // power +0.010, drag -0.008
        });

        var basePlayer = baseline.Seats.Single(s => s.IsPlayer);
        var patchedPlayer = patched.Seats.Single(s => s.IsPlayer);

        Assert.Equal(basePlayer.PowerScalar + 0.010, patchedPlayer.PowerScalar, 6);
        Assert.Equal(basePlayer.DragScalar - 0.008, patchedPlayer.DragScalar, 6);
        Assert.Equal(basePlayer.WeightScalar, patchedPlayer.WeightScalar, 6);
    }

    [Theory]
    [InlineData(CharacterLevelProgression.LegacyVersion)]
    [InlineData(CharacterLevelProgression.EraCappedVersion)]
    public void Resolve_LegacyProgression_PreservesUnclampedNonAuthoritativeScalars(
        int progressionVersion)
    {
        var pack = PackWithPlayerCar(
            new PackTeamPerformance
            {
                WeightScalar = 1.0,
                PowerScalar = 1.095,
                DragScalar = 0.905,
            },
            new PackDriverCar
            {
                PowerScalar = 1.095,
                DragScalar = 0.905,
                VehicleReliability = 0.48,
            });
        var baseline = RoundGridResolver.Resolve(
            pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery });
        var patched = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(["engineers_favorite"], progressionVersion: progressionVersion),
        });

        var player = patched.Seats.Single(seat => seat.IsPlayer);

        // Legacy versions keep their original additive behavior, including values outside the
        // v2 AMS2 safety band. They also never claim authority over installed NAMeS scalars.
        Assert.Equal(1.105, player.PowerScalar, 6);
        Assert.Equal(0.897, player.DragScalar, 6);
        Assert.Equal(1.105, player.CarTuning!.PowerScalar!.Value, 6);
        Assert.Equal(0.897, player.CarTuning.DragScalar!.Value, 6);
        Assert.False(player.PlayerCarScalarsAuthoritative);
        AssertAiSeatsUnchanged(baseline, patched);
    }

    [Fact]
    public void Resolve_V2_ComposesPerAxisClampsAndMirrorsThePlayerPhysicsTruth()
    {
        var pack = PackWithPlayerCar(
            new PackTeamPerformance
            {
                WeightScalar = 0.950,
                PowerScalar = 0.960,
                DragScalar = 1.030,
            },
            new PackDriverCar
            {
                WeightScalar = 0.905,
                PowerScalar = 1.095,
                // No authored drag: this axis must fall back to the team value.
                VehicleReliability = 0.48,
            });
        var baseline = RoundGridResolver.Resolve(
            pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery });
        var patched = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            // featherweight: weight -0.010, drag +0.010; engineer: power +0.010,
            // drag -0.008. Aggregate drag delta is therefore +0.002.
            Character = Patch(
                ["featherweight_setup", "engineers_favorite"],
                progressionVersion: CharacterLevelProgression.Level300Version),
        });

        var player = patched.Seats.Single(seat => seat.IsPlayer);

        // Driver tuning wins weight/power per axis and crosses both safety bounds after the
        // aggregate perk delta. Missing driver drag falls back to the team before composition.
        Assert.Equal(PlayerCarScalarPolicy.Minimum, player.WeightScalar, 6);
        Assert.Equal(PlayerCarScalarPolicy.Maximum, player.PowerScalar, 6);
        Assert.Equal(1.032, player.DragScalar, 6);
        Assert.True(player.PlayerCarScalarsAuthoritative);

        // The expectation model and AMS2 staging receive one identical set of final scalars.
        Assert.NotNull(player.CarTuning);
        Assert.Equal(player.WeightScalar, player.CarTuning!.WeightScalar!.Value, 12);
        Assert.Equal(player.PowerScalar, player.CarTuning.PowerScalar!.Value, 12);
        Assert.Equal(player.DragScalar, player.CarTuning.DragScalar!.Value, 12);
        Assert.Equal(0.48, player.CarTuning.VehicleReliability);
        AssertAiSeatsUnchanged(baseline, patched);
    }

    [Fact]
    public void Resolve_V2_SeatStrengthUsesTheSameComposedFinalScalars()
    {
        var pack = PackWithPlayerCar(
            new PackTeamPerformance
            {
                WeightScalar = 0.970,
                PowerScalar = 0.980,
                DragScalar = 1.005,
            },
            new PackDriverCar
            {
                WeightScalar = 0.990,
                PowerScalar = 1.010,
                // Drag deliberately falls back to the team axis.
            });
        var plan = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(
                ["engineers_favorite"],
                progressionVersion: CharacterLevelProgression.Level300Version),
        });
        var player = plan.Seats.Single(seat => seat.IsPlayer);

        Assert.Equal(0.990, player.WeightScalar, 6);
        Assert.Equal(1.020, player.PowerScalar, 6);
        Assert.Equal(0.997, player.DragScalar, 6);
        Assert.Equal(1.033, SeatStrengthModel.CarRating(player), 6);
        Assert.Equal(0.830, SeatStrengthModel.CarScore(player), 6);

        double expectedStrength =
            0.6 * 0.830 + 0.3 * player.Ratings.RaceSkill + 0.1 * player.Reliability;
        Assert.Equal(expectedStrength, SeatStrengthModel.Strength(player), 6);
    }

    [Fact]
    public void Resolve_V2_FailsClosedForConditionalPlayerCarPhysics()
    {
        var pack = CareerTestData.Pack();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    ["rain_man"],
                    progressionVersion: CharacterLevelProgression.Level300Version),
            }));

        Assert.Contains("conditional player-car physics", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_V2_ConditionalPlayerCarPhysics_UsesValidatedWetDryFacts()
    {
        var pack = CareerTestData.Pack();
        var wetFacts = PlayerRoundConditions.Prepare(pack, 1, isWet: true);
        var dryFacts = PlayerRoundConditions.Prepare(pack, 1, isWet: false);

        GridSeat Player(PlayerRoundConditionsInput facts) =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    ["rain_man"],
                    progressionVersion: CharacterLevelProgression.Level300Version,
                    roundConditions: facts),
            }).Seats.Single(seat => seat.IsPlayer);

        var wet = Player(wetFacts);
        var dry = Player(dryFacts);

        Assert.True(wet.PlayerCarScalarsAuthoritative);
        Assert.True(dry.PlayerCarScalarsAuthoritative);
        Assert.Equal(0.028, wet.PowerScalar - dry.PowerScalar, 6);
        Assert.Equal(wet.WeightScalar, dry.WeightScalar, 6);
        Assert.Equal(wet.DragScalar, dry.DragScalar, 6);
    }

    [Fact]
    public void Resolve_V0MasteryOwnership_IsInertWithoutACatalog()
    {
        var pack = CareerTestData.Pack();

        GridSeat Player(IReadOnlyList<string>? acquired) =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    [],
                    progressionVersion: CharacterLevelProgression.Level300Version,
                    acquiredSkillIds: acquired,
                    masteryEffectsVersion: 0,
                    masterySkills: null),
            }).Seats.Single(seat => seat.IsPlayer);

        Assert.Equal(Player([]), Player(["physical_core_strength"]));
    }

    [Fact]
    public void Resolve_ActiveCoreStrength_ChangesOnlyThePlayerWeightAndStaminaRating()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        var pack = CareerTestData.Pack();
        var baseline = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(
                [], progressionVersion: CharacterLevelProgression.Level300Version,
                masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                masterySkills: catalog),
        });
        var patched = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(
                [], progressionVersion: CharacterLevelProgression.Level300Version,
                acquiredSkillIds: ["physical_core_strength"],
                masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                masterySkills: catalog),
        });

        GridSeat before = baseline.Seats.Single(seat => seat.IsPlayer);
        GridSeat after = patched.Seats.Single(seat => seat.IsPlayer);
        Assert.Equal(before.WeightScalar + 0.002, after.WeightScalar, 6);
        Assert.NotNull(before.Ratings.Stamina);
        Assert.NotNull(after.Ratings.Stamina);
        Assert.Equal(before.Ratings.Stamina.Value + 0.06, after.Ratings.Stamina.Value, 6);
        Assert.Equal(before.PowerScalar, after.PowerScalar, 6);
        Assert.Equal(before.DragScalar, after.DragScalar, 6);
        AssertAiSeatsUnchanged(baseline, patched);
    }

    [Fact]
    public void Resolve_ConditionalMasteryCarPhysics_FailsWithoutTypedRoundFacts()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(CareerTestData.Pack(), 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    [], progressionVersion: CharacterLevelProgression.Level300Version,
                    acquiredSkillIds: ["v2_rain_man"],
                    masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                    masterySkills: catalog),
            }));

        Assert.Contains("conditional player-car physics", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ConditionalMasteryCarPhysics_UsesOnlyValidatedWetDryFacts()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        var pack = CareerTestData.Pack();

        GridSeat Player(bool wet)
        {
            PlayerRoundConditionsInput facts = PlayerRoundConditions.Prepare(pack, 1, wet);
            return RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    [], progressionVersion: CharacterLevelProgression.Level300Version,
                    roundConditions: facts,
                    acquiredSkillIds: ["v2_rain_man"],
                    masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                    masterySkills: catalog),
            }).Seats.Single(seat => seat.IsPlayer);
        }

        GridSeat wet = Player(true);
        GridSeat dry = Player(false);
        Assert.Equal(0.028, wet.PowerScalar - dry.PowerScalar, 6);
        Assert.Equal(wet.WeightScalar, dry.WeightScalar, 6);
        Assert.Equal(wet.DragScalar, dry.DragScalar, 6);
    }

    [Fact]
    public void Resolve_ActiveMasteryCarScalars_IgnoreCallerForgedModifiers()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        var pack = CareerTestData.Pack();
        GridSeat baseline = RoundGridResolver.Resolve(
            pack, 1, new PlayerSeat { Ams2LiveryName = CareerTestData.PlayerLivery })
            .Seats.Single(seat => seat.IsPlayer);
        var forged = PlayerPerkModifiers.Identity with
        {
            WeightScalarDelta = -0.20,
            PowerScalarDelta = 0.20,
            DragScalarDelta = -0.20,
        };

        GridSeat player = RoundGridResolver.Resolve(pack, 1, new PlayerSeat
        {
            Ams2LiveryName = CareerTestData.PlayerLivery,
            Character = Patch(
                [], progressionVersion: CharacterLevelProgression.Level300Version,
                acquiredSkillIds: ["physical_core_strength"],
                masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                masterySkills: catalog,
                suppliedModifiers: forged),
        }).Seats.Single(seat => seat.IsPlayer);

        Assert.Equal(baseline.WeightScalar + 0.002, player.WeightScalar, 6);
        Assert.Equal(baseline.PowerScalar, player.PowerScalar, 6);
        Assert.Equal(baseline.DragScalar, player.DragScalar, 6);
    }

    [Fact]
    public void Resolve_ActiveMastery_FailsClosedForMissingCatalogAndUnknownOwnership()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        var pack = CareerTestData.Pack();

        var missing = Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    [], progressionVersion: CharacterLevelProgression.Level300Version,
                    acquiredSkillIds: ["physical_core_strength"],
                    masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion),
            }));
        Assert.Contains("mastery-skill catalog", missing.Message, StringComparison.Ordinal);

        var unknown = Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat
            {
                Ams2LiveryName = CareerTestData.PlayerLivery,
                Character = Patch(
                    [], progressionVersion: CharacterLevelProgression.Level300Version,
                    acquiredSkillIds: ["mastery.not-real"],
                    masteryEffectsVersion: CharacterProfile.CurrentMasteryEffectsVersion,
                    masterySkills: catalog,
                    suppliedModifiers: PlayerPerkModifiers.Identity),
            }));
        Assert.Contains("unknown mastery skill", unknown.Message, StringComparison.Ordinal);
    }
}
