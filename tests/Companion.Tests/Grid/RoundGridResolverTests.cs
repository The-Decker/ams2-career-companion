using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// Resolver semantics against the shipped reference packs (rounds-range membership, the 1988
/// Mansell/Brundle round-11 swap, aiOverrides precedence, player replacement) and against
/// synthetic packs for the paths the reference data does not exercise (guest entries,
/// trackForm clamping, duplicate liveries).
/// </summary>
public class RoundGridResolverTests
{
    // ---------- rounds-range membership (f1-1967) ----------

    [Fact]
    public void Resolve_1967Round1_SeatsExactlyTheEntriesCoveringRound1_InEntriesOrder()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        var plan = RoundGridResolver.Resolve(pack, 1);

        // entries.json order, filtered to rounds expressions containing 1.
        string[] expected =
        [
            "driver.jack_brabham",   // 1-11
            "driver.denny_hulme",    // 1-11
            "driver.jim_clark",      // 1-11
            "driver.graham_hill",    // 1-11
            "driver.jo_siffert",     // 1-11
            "driver.jackie_stewart", // 1-11
            "driver.mike_spence",    // 1-11
            "driver.john_surtees",   // 1-4,6-7,9-11
            "driver.jochen_rindt",   // 1-10
            "driver.pedro_rodriguez",// 1-7,11
            "driver.dan_gurney",     // 1-11
        ];
        Assert.Equal(expected, plan.Seats.Select(s => s.DriverId));

        // Split ranges exclude correctly: Bruce McLaren (2-3,8-11) and the round-2-only
        // Ferrari/Eagle one-offs are not in round 1.
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.bruce_mclaren");
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.lorenzo_bandini");
    }

    [Fact]
    public void Resolve_1967_ListAndRangeExpressionsGateEachRound()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        // driver.john_surtees has rounds "1-4,6-7,9-11": in for 4, out for 5 and 8.
        Assert.Contains(RoundGridResolver.Resolve(pack, 4).Seats, s => s.DriverId == "driver.john_surtees");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 5).Seats, s => s.DriverId == "driver.john_surtees");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 8).Seats, s => s.DriverId == "driver.john_surtees");
        Assert.Contains(RoundGridResolver.Resolve(pack, 9).Seats, s => s.DriverId == "driver.john_surtees");

        // driver.hubert_hahne is a single-round entry ("7").
        Assert.Contains(RoundGridResolver.Resolve(pack, 7).Seats, s => s.DriverId == "driver.hubert_hahne");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 6).Seats, s => s.DriverId == "driver.hubert_hahne");
    }

    // ---------- 1988 mid-season swap: Mansell out, Brundle in for round 11 ----------

    [Fact]
    public void Resolve_1988Round11_BrundleTakesTheWilliamsSeat()
    {
        var pack = GridTestData.LoadReferencePack("f1-1988");

        var round11 = RoundGridResolver.Resolve(pack, 11);

        Assert.DoesNotContain(round11.Seats, s => s.DriverId == "driver.nigel_mansell");
        var brundle = Assert.Single(round11.Seats, s => s.DriverId == "driver.martin_brundle");
        Assert.Equal("team.williams", brundle.TeamId);
        Assert.Equal("5", brundle.Number);
        Assert.Equal("1988 Williams #5 - M. Brundle", brundle.Ams2LiveryName);

        // Exactly one car #5 for Williams — the swap replaces, it does not add.
        Assert.Single(round11.Seats, s => s.TeamId == "team.williams" && s.Number == "5");
    }

    [Fact]
    public void Resolve_1988_TheWilliamsSeatChangesHandsAcrossTheSeason()
    {
        var pack = GridTestData.LoadReferencePack("f1-1988");

        Assert.Contains(RoundGridResolver.Resolve(pack, 1).Seats, s => s.DriverId == "driver.nigel_mansell");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 1).Seats, s => s.DriverId == "driver.martin_brundle");

        Assert.Single(RoundGridResolver.Resolve(pack, 12).Seats, s => s.DriverId == "driver.jean_louis_schlesser");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 12).Seats, s => s.DriverId == "driver.nigel_mansell");

        Assert.Contains(RoundGridResolver.Resolve(pack, 13).Seats, s => s.DriverId == "driver.nigel_mansell");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 13).Seats, s => s.DriverId == "driver.martin_brundle");
    }

    // ---------- guest entries ----------

    [Fact]
    public void Resolve_GuestEntries_AppendAfterRegularEntriesAndOnlyOnTheirRound()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A"), GridTestData.Team("team.guest", "Guest Racing")],
            drivers:
            [
                GridTestData.Driver("driver.one", "Driver One"),
                GridTestData.Driver("driver.two", "Driver Two"),
                GridTestData.Driver("driver.guest", "Guest Star"),
            ],
            entries:
            [
                GridTestData.Entry("team.a", "driver.one", "1", "1-2", "Team A #1"),
                GridTestData.Entry("team.a", "driver.two", "2", "1-2", "Team A #2"),
            ],
            rounds:
            [
                GridTestData.Round(1),
                GridTestData.Round(2, guestEntries:
                [
                    new PackGuestEntry
                    {
                        TeamId = "team.guest",
                        DriverId = "driver.guest",
                        Number = "99",
                        Ams2LiveryName = "Guest Racing #99",
                    },
                ]),
            ]);

        var round1 = RoundGridResolver.Resolve(pack, 1);
        Assert.Equal(2, round1.Seats.Count);
        Assert.DoesNotContain(round1.Seats, s => s.IsGuest);

        var round2 = RoundGridResolver.Resolve(pack, 2);
        Assert.Equal(["driver.one", "driver.two", "driver.guest"], round2.Seats.Select(s => s.DriverId));

        var guest = round2.Seats[^1];
        Assert.True(guest.IsGuest);
        Assert.Equal("Guest Racing", guest.TeamName);
        Assert.Equal("99", guest.Number);
        Assert.Equal("Guest Racing #99", guest.Ams2LiveryName);
        // Guests get the full merge/team treatment too.
        Assert.Equal(0.90, guest.Reliability);
    }

    // ---------- trackForm: additive, clamped 0..1, pace fields only ----------

    [Fact]
    public void Resolve_TrackFormNudge_IsAdditiveAndClampsAtOne()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers:
            [
                GridTestData.Driver(
                    "driver.ace", "Track Ace",
                    ratings: GridTestData.Ratings(raceSkill: 0.94, qualifyingSkill: 0.98),
                    trackForm: new Dictionary<string, double> { ["kyalami_historic"] = 0.05 }),
            ],
            entries: [GridTestData.Entry("team.a", "driver.ace", "1", "1-2", "Team A #1")],
            rounds: [GridTestData.Round(1, trackId: "kyalami_historic"), GridTestData.Round(2, trackId: "monza_1971")]);

        var atForm = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);
        Assert.Equal(0.99, atForm.Ratings.RaceSkill, precision: 10);   // 0.94 + 0.05
        Assert.Equal(1.0, atForm.Ratings.QualifyingSkill);              // 0.98 + 0.05 -> clamp 1.0
        Assert.Equal(0.50, atForm.Ratings.Aggression);                  // character fields untouched

        // The nudge is keyed by the round's track id — a different venue gets the baseline.
        var elsewhere = Assert.Single(RoundGridResolver.Resolve(pack, 2).Seats);
        Assert.Equal(0.94, elsewhere.Ratings.RaceSkill);
        Assert.Equal(0.98, elsewhere.Ratings.QualifyingSkill);
    }

    [Fact]
    public void Resolve_NegativeTrackFormNudge_ClampsAtZero()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers:
            [
                GridTestData.Driver(
                    "driver.struggler", "Struggler",
                    ratings: GridTestData.Ratings(raceSkill: 0.03, qualifyingSkill: 0.10),
                    trackForm: new Dictionary<string, double> { ["kyalami_historic"] = -0.05 }),
            ],
            entries: [GridTestData.Entry("team.a", "driver.struggler", "1", "1", "Team A #1")],
            rounds: [GridTestData.Round(1, trackId: "kyalami_historic")]);

        var seat = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);
        Assert.Equal(0.0, seat.Ratings.RaceSkill);                      // 0.03 - 0.05 -> clamp 0.0
        Assert.Equal(0.05, seat.Ratings.QualifyingSkill, precision: 10);
    }

    // ---------- precedence: round aiOverrides are absolute and beat trackForm ----------

    [Fact]
    public void Resolve_AiOverrides_BeatTheTrackFormNudge_FieldByField()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers:
            [
                GridTestData.Driver(
                    "driver.mixed", "Mixed Case",
                    ratings: GridTestData.Ratings(raceSkill: 0.90, qualifyingSkill: 0.80),
                    trackForm: new Dictionary<string, double> { ["kyalami_historic"] = 0.03 }),
            ],
            entries: [GridTestData.Entry("team.a", "driver.mixed", "1", "1", "Team A #1")],
            rounds:
            [
                GridTestData.Round(1, trackId: "kyalami_historic", aiOverrides:
                    new Dictionary<string, PackRatingsPatch>
                    {
                        ["driver.mixed"] = new() { RaceSkill = 0.85, Consistency = 0.99 },
                    }),
            ]);

        var seat = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);

        Assert.Equal(0.85, seat.Ratings.RaceSkill);                     // absolute override, not 0.93
        Assert.Equal(0.83, seat.Ratings.QualifyingSkill, precision: 10); // nudge survives on unpatched field
        Assert.Equal(0.99, seat.Ratings.Consistency);                   // patch reaches non-pace fields
        Assert.Equal(0.70, seat.Ratings.Stamina);                       // unpatched baseline
    }

    [Fact]
    public void Resolve_1967Round1_AiOverridesFromTheRealPackApplyAbsolutely()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        var plan = RoundGridResolver.Resolve(pack, 1);

        // season.json round 1 patches jack_brabham: qualifyingSkill 0.98, consistency 0.9
        // over a drivers.json baseline of 0.94 / 0.8; raceSkill stays at the 0.93 baseline.
        var brabham = Assert.Single(plan.Seats, s => s.DriverId == "driver.jack_brabham");
        Assert.Equal(0.98, brabham.Ratings.QualifyingSkill);
        Assert.Equal(0.90, brabham.Ratings.Consistency);
        Assert.Equal(0.93, brabham.Ratings.RaceSkill);

        // An unpatched driver keeps the baseline verbatim.
        var clark = Assert.Single(plan.Seats, s => s.DriverId == "driver.jim_clark");
        Assert.Equal(0.98, clark.Ratings.QualifyingSkill);
        Assert.Equal(0.94, clark.Ratings.RaceSkill);
    }

    // ---------- player seat: replace a historical driver by livery ----------

    [Fact]
    public void Resolve_PlayerSeat_MarksTheSeatAndChangesNothingElse()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        const string livery = "Lotus-Ford Cosworth #5 J. Clark";

        var without = RoundGridResolver.Resolve(pack, 1);
        var with = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = livery });

        Assert.Equal(without.Seats.Count, with.Seats.Count);

        for (int i = 0; i < with.Seats.Count; i++)
        {
            var expected = without.Seats[i];
            var actual = with.Seats[i];
            if (actual.Ams2LiveryName == livery)
            {
                Assert.True(actual.IsPlayer);
                // The player's seat keeps its livery, ratings, reliability, and team scalars —
                // the entry is written to the file so the scalars apply to the player's car.
                Assert.Equal(expected with { IsPlayer = true }, actual);
            }
            else
            {
                Assert.False(actual.IsPlayer);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void Resolve_PlayerSeat_UnknownLivery_ThrowsWithTheLiveryAndRound()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        // Bruce McLaren does not run round 1 (rounds "2-3,8-11") — his livery is a valid pack
        // string but not part of THIS round's grid.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "McLaren-BRM #14 B. McLaren" }));

        Assert.Contains("McLaren-BRM #14 B. McLaren", ex.Message);
        Assert.Contains("round-1", ex.Message);
    }

    [Fact]
    public void Resolve_PlayerSeat_LiveryMatchIsCaseSensitive()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        Assert.Throws<InvalidOperationException>(() =>
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "LOTUS-FORD COSWORTH #5 J. CLARK" }));
    }

    // ---------- errors: unknown round, duplicate liveries ----------

    [Fact]
    public void Resolve_UnknownRound_ThrowsWithTheCalendarBounds()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        var ex = Assert.Throws<InvalidOperationException>(() => RoundGridResolver.Resolve(pack, 12));

        Assert.Contains("Round 12", ex.Message);
        Assert.Contains("1-11", ex.Message);
    }

    [Fact]
    public void Resolve_DuplicateLiveriesAfterResolution_Throws()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers:
            [
                GridTestData.Driver("driver.one", "Driver One"),
                GridTestData.Driver("driver.two", "Driver Two"),
            ],
            entries:
            [
                // Legal on paper (different rounds ranges would make this a swap), but both
                // cover round 2 — the resolved grid would double-bind the livery.
                GridTestData.Entry("team.a", "driver.one", "1", "1-2", "Team A #1"),
                GridTestData.Entry("team.a", "driver.two", "1", "2-3", "Team A #1"),
            ],
            rounds: [GridTestData.Round(1), GridTestData.Round(2), GridTestData.Round(3)]);

        // Rounds 1 and 3 are fine — only one entry covers each.
        Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);
        Assert.Single(RoundGridResolver.Resolve(pack, 3).Seats);

        var ex = Assert.Throws<InvalidOperationException>(() => RoundGridResolver.Resolve(pack, 2));
        Assert.Contains("Team A #1", ex.Message);
        Assert.Contains("driver.one", ex.Message);
        Assert.Contains("driver.two", ex.Message);
    }

    [Fact]
    public void Resolve_GuestDuplicatingARegularEntryLivery_Throws()
    {
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers:
            [
                GridTestData.Driver("driver.one", "Driver One"),
                GridTestData.Driver("driver.guest", "Guest Star"),
            ],
            entries: [GridTestData.Entry("team.a", "driver.one", "1", "1", "Team A #1")],
            rounds:
            [
                GridTestData.Round(1, guestEntries:
                [
                    new PackGuestEntry
                    {
                        TeamId = "team.a",
                        DriverId = "driver.guest",
                        Ams2LiveryName = "Team A #1",
                    },
                ]),
            ]);

        var ex = Assert.Throws<InvalidOperationException>(() => RoundGridResolver.Resolve(pack, 1));
        Assert.Contains("Team A #1", ex.Message);
    }

    // ---------- seats carry the team's physics ----------

    [Fact]
    public void Resolve_SeatCarriesTeamReliabilityAndScalars()
    {
        var pack = GridTestData.Pack(
            teams:
            [
                GridTestData.Team("team.fast", "Fast Team",
                    reliability: 0.87, weightScalar: 0.98, powerScalar: 1.02, dragScalar: 0.99),
            ],
            drivers: [GridTestData.Driver("driver.one", "Driver One")],
            entries: [GridTestData.Entry("team.fast", "driver.one", "1", "1", "Fast Team #1")],
            rounds: [GridTestData.Round(1)]);

        var seat = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);

        Assert.Equal(0.87, seat.Reliability);
        Assert.Equal(0.98, seat.WeightScalar);
        Assert.Equal(1.02, seat.PowerScalar);
        Assert.Equal(0.99, seat.DragScalar);
        Assert.Equal("TST", seat.Country);
        Assert.Equal("Driver One", seat.DriverName);
    }
}
