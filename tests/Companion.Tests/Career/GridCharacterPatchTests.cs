using Companion.Core.Character;
using Companion.Core.Grid;

namespace Companion.Tests.Career;

/// <summary>The player seat's character patch at grid resolve — the last step of the merge chain.
/// A null character (every pre-character career) resolves a byte-identical grid; a character patches
/// only the player seat's ratings + scalars, never an AI seat.</summary>
public sealed class GridCharacterPatchTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static PlayerCharacterPatch Patch(
        IReadOnlyList<string> perkIds, double pace = 0.5, double oneLap = 0.5)
    {
        var rules = Rules();
        var profile = new CharacterProfile
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = pace, ["oneLap"] = oneLap, ["craft"] = 0.5,
                ["racecraft"] = 0.5, ["adaptability"] = 0.5,
                ["marketability"] = 0.5, ["durability"] = 0.5,
            },
            PerkIds = perkIds,
        };
        return new PlayerCharacterPatch
        {
            Profile = profile,
            Modifiers = PerkResolver.Resolve(perkIds, rules),
            Rules = rules,
        };
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
        foreach (var ai in patched.Seats.Where(s => !s.IsPlayer))
        {
            var baseAi = baseline.Seats.Single(s => s.DriverId == ai.DriverId);
            Assert.Equal(baseAi, ai);
        }
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
}
