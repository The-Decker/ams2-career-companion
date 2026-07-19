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

        // entries.json order, filtered to rounds expressions containing 1, since the max-grid
        // roster pass, Kyalami fields the FULL 20-car skinpack roster (no more 11-car round 1).
        string[] expected =
        [
            "driver.jack_brabham",       // 1-11
            "driver.denny_hulme",        // 1-11
            "driver.bob_anderson",       // 1-5 (the real privateer Brabham; died Aug 1967)
            "driver.jim_clark",          // 1-11
            "driver.graham_hill",        // 1-11
            "driver.jackie_stewart",     // 1-11
            "driver.mike_spence",        // 1-11
            "driver.pedro_rodriguez",    // 1-7,11
            "driver.jo_siffert",         // 1-11
            "driver.jochen_rindt",       // 1-10
            "driver.chris_amon",         // 1-11
            "driver.lorenzo_bandini",    // 1-2 (fatal Monaco accident)
            "driver.ludovico_scarfiotti",// 1-11
            "driver.john_surtees",       // 1-11
            "driver.dan_gurney",         // 1-11
            "driver.richie_ginther",     // 1-2 (retired from driving)
            "driver.bruce_mclaren",      // 1-11
            "driver.hubert_hahne",       // 1-11
            "driver.jean_pierre_beltoise", // 1-6,8-11
            "driver.jacky_ickx",         // 1,3-11
        ];
        Assert.Equal(expected, plan.Seats.Select(s => s.DriverId));

        // Mid-season stints exclude correctly: Parkes (3-4) and Servoz-Gavin (2) are not in round 1.
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.mike_parkes");
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.johnny_servoz_gavin");
    }

    [Fact]
    public void Resolve_1967_ListAndRangeExpressionsGateEachRound()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        // driver.pedro_rodriguez has rounds "1-7,11" (injured mid-season): in for 4, out for 8-10.
        Assert.Contains(RoundGridResolver.Resolve(pack, 4).Seats, s => s.DriverId == "driver.pedro_rodriguez");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 8).Seats, s => s.DriverId == "driver.pedro_rodriguez");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 9).Seats, s => s.DriverId == "driver.pedro_rodriguez");
        Assert.Contains(RoundGridResolver.Resolve(pack, 11).Seats, s => s.DriverId == "driver.pedro_rodriguez");

        // driver.richard_attwood is his single-round stand-in ("8") in the same car.
        Assert.Contains(RoundGridResolver.Resolve(pack, 8).Seats, s => s.DriverId == "driver.richard_attwood");
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 7).Seats, s => s.DriverId == "driver.richard_attwood");

        // driver.jo_bonnier covers two DIFFERENT Cooper seats: #11 for "9-10", #30 for "11".
        Assert.Equal("Cooper-Maserati #11 P. Rodriguez",
            Assert.Single(RoundGridResolver.Resolve(pack, 9).Seats, s => s.DriverId == "driver.jo_bonnier").Ams2LiveryName);
        Assert.Equal("Cooper-Maserati #30 J. Rindt",
            Assert.Single(RoundGridResolver.Resolve(pack, 11).Seats, s => s.DriverId == "driver.jo_bonnier").Ams2LiveryName);
    }

    // ---------- the player races the FULL season, even when their seat has a coverage gap ----------

    [Fact]
    public void Resolve_PlayerSeat_RacesEveryRound_EvenWhenTheirHistoricalDriverSatOut()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        // Ferrari #18: Bandini (1-2), Parkes (3-4), then a REAL gap 5-10 (the second works Ferrari
        // did not exist those rounds), Williams (11).
        var player = new PlayerSeat { Ams2LiveryName = "Ferrari #18 L. Bandini" };

        // Round 3: the livery is covered (Parkes), so the player takes that seat (replacement).
        var seat3 = Assert.Single(RoundGridResolver.Resolve(pack, 3, player).Seats, s => s.IsPlayer);
        Assert.Equal("Ferrari #18 L. Bandini", seat3.Ams2LiveryName);
        Assert.Equal("driver.mike_parkes", seat3.DriverId);

        // Round 5: nobody covers the seat, but the player still races, seated from their own
        // livery entry, instead of being benched (the old behaviour returned no player seat).
        var seat5 = Assert.Single(RoundGridResolver.Resolve(pack, 5, player).Seats, s => s.IsPlayer);
        Assert.Equal("Ferrari #18 L. Bandini", seat5.Ams2LiveryName);
        Assert.Equal("driver.lorenzo_bandini", seat5.DriverId);

        // Without a player seat, round 5's historical field still leaves the seat empty (unchanged).
        Assert.DoesNotContain(RoundGridResolver.Resolve(pack, 5).Seats,
            s => s.Ams2LiveryName == "Ferrari #18 L. Bandini");
    }

    [Fact]
    public void Resolve_PlayerSeat_LiveryMatchingNoEntry_SeatsAnOwnEntrant()
    {
        // Player-as-own-entrant: a custom livery no pack driver holds seats the player as their own
        // independent synthetic entrant (stable id) rather than throwing, so custom skins work.
        var pack = GridTestData.LoadReferencePack("f1-1967");
        var seat = Assert.Single(
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "My Custom Skin #99" }).Seats,
            s => s.IsPlayer);
        Assert.Equal("My Custom Skin #99", seat.Ams2LiveryName);
        Assert.Equal(RoundGridResolver.SyntheticPlayerDriverId, seat.DriverId);
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

        // Exactly one car #5 for Williams, the swap replaces, it does not add.
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

        // The nudge is keyed by the round's track id, a different venue gets the baseline.
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

        // season.json round 1 (Kyalami) patches jack_brabham: qualifyingSkill 0.98, consistency 0.9
        // over jusk's drivers.json baseline of 0.94 / (base consistency); raceSkill stays at the
        // jusk baseline 0.93 (raceSkill is not overridden at Kyalami).
        var brabham = Assert.Single(plan.Seats, s => s.DriverId == "driver.jack_brabham");
        Assert.Equal(0.98, brabham.Ratings.QualifyingSkill);
        Assert.Equal(0.90, brabham.Ratings.Consistency);
        Assert.Equal(0.93, brabham.Ratings.RaceSkill);

        // An unpatched driver keeps jusk's baseline verbatim (Clark has no per-track override).
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
    public void Resolve_PlayerSeat_DriverOutThisRound_SeatsThePlayerAnyway()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");

        // Bruce McLaren does not run round 1 (rounds "2-3,8-11"), but the player who took his seat
        // still races round 1, seated from McLaren's entry, rather than being benched. (The player
        // races the full season regardless of the historical driver's schedule.)
        var seat = Assert.Single(
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "McLaren-BRM #14 B. McLaren" }).Seats,
            s => s.IsPlayer);
        Assert.Equal("McLaren-BRM #14 B. McLaren", seat.Ams2LiveryName);
        Assert.Equal("driver.bruce_mclaren", seat.DriverId);
    }

    [Fact]
    public void Resolve_1988Round1_PlayerOnADnqCar_IsSeated_HoldingTheHardcodedGridSize()
    {
        // 1988 was a pre-qualifying year: ~30 cars for 26 slots, so 4-5 DNQ each round. Coloni's
        // Tarquini (#31, entry "1-16") did NOT qualify for round 1 (Brazil), his driver id is not in
        // that round's starterDriverIds. A player who picked his car must still appear on the grid AMS2
        // loads (the grid size is hardcoded), so the resolver seats the player and CapToGridSize drops
        // the slowest qualifier, a peer backmarker, never a front-runner.
        var pack = GridTestData.LoadReferencePack("f1-1988");
        int size = pack.Season.Rounds.First(r => r.Round == 1).Grid!.Size;   // 26
        const string tarquini = "driver.gabriele_tarquini";
        string livery = pack.Entries.First(e => e.DriverId == tarquini).Ams2LiveryName;

        var bare = RoundGridResolver.Resolve(pack, 1);
        Assert.DoesNotContain(bare.Seats, s => s.DriverId == tarquini);   // DNQ => not in the bare field
        Assert.Equal(size, bare.Seats.Count);

        var withPlayer = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = livery });
        var player = Assert.Single(withPlayer.Seats, s => s.IsPlayer);
        Assert.Equal(tarquini, player.DriverId);
        Assert.Equal(size, withPlayer.Seats.Count);          // hardcoded grid size held (one qualifier dropped)

        // Exactly one qualifier was replaced, and it is a peer (its raceSkill is <= every AI kept) —
        // no higher-tier team was bumped to make room for the player.
        var dropped = Assert.Single(bare.Seats.Select(s => s.DriverId)
            .Except(withPlayer.Seats.Select(s => s.DriverId)));
        double droppedSkill = bare.Seats.First(s => s.DriverId == dropped).Ratings.RaceSkill;
        double slowestKeptAi = withPlayer.Seats.Where(s => !s.IsPlayer).Min(s => s.Ratings.RaceSkill);
        Assert.True(droppedSkill <= slowestKeptAi,
            $"dropped {dropped} ({droppedSkill}) should be no faster than the slowest kept AI ({slowestKeptAi}).");
    }

    [Fact]
    public void Resolve_Uncapped_KeepsTheCarTheGridCapWouldDrop()
    {
        // The zero-stock staging pass resolves the full qualified field (capToGridSize:false) so it can
        // name every active livery, including the peer the cap dropped to seat a DNQ player, instead of
        // leaving AMS2 to stock-fill that slot. The sim always uses the capped grid.
        var pack = GridTestData.LoadReferencePack("f1-1988");
        var player = new PlayerSeat
        {
            Ams2LiveryName = pack.Entries.First(e => e.DriverId == "driver.gabriele_tarquini").Ams2LiveryName,
        };

        var capped = RoundGridResolver.Resolve(pack, 1, player);                     // 26, one qualifier dropped
        var full = RoundGridResolver.Resolve(pack, 1, player, capToGridSize: false); // nothing dropped

        Assert.True(full.Seats.Count > capped.Seats.Count);
        Assert.All(capped.Seats, s => Assert.Contains(full.Seats, f => f.DriverId == s.DriverId));
        var dropped = full.Seats.Select(s => s.DriverId).Except(capped.Seats.Select(s => s.DriverId)).ToList();
        Assert.NotEmpty(dropped); // the cap-dropped car the finalize will name
    }

    [Fact]
    public void Resolve_PlayerSeat_LiveryMatchIsCaseSensitive_WrongCaseBecomesAnOwnEntrant()
    {
        // Binding stays exact/case-sensitive: an almost-right livery (wrong case) does NOT bind the real
        // seat, it is treated as a custom livery and seats the player as their own entrant.
        var pack = GridTestData.LoadReferencePack("f1-1967");
        var seat = Assert.Single(
            RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "LOTUS-FORD COSWORTH #5 J. CLARK" }).Seats,
            s => s.IsPlayer);
        Assert.Equal(RoundGridResolver.SyntheticPlayerDriverId, seat.DriverId); // NOT driver.jim_clark
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
                // cover round 2, the resolved grid would double-bind the livery.
                GridTestData.Entry("team.a", "driver.one", "1", "1-2", "Team A #1"),
                GridTestData.Entry("team.a", "driver.two", "1", "2-3", "Team A #1"),
            ],
            rounds: [GridTestData.Round(1), GridTestData.Round(2), GridTestData.Round(3)]);

        // Rounds 1 and 3 are fine, only one entry covers each.
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

    // ---------- historical grid: starterDriverIds gate the round's seats ----------

    /// <summary>Six entries cover the round; the grid lists only three as historical starters.
    /// The resolved grid seats exactly those three (in entries.json order), and the other three —
    /// pre-qualifiers / non-starters, stay OUT of the grid though they remain in the pack.</summary>
    [Fact]
    public void Resolve_GridStarters_SeatOnlyTheStartersAmongCoveringEntries()
    {
        var pack = SixEntryPack(GridTestData.Grid(3,
            "driver.two", "driver.four", "driver.six"));

        var plan = RoundGridResolver.Resolve(pack, 1);

        Assert.Equal(["driver.two", "driver.four", "driver.six"], plan.Seats.Select(s => s.DriverId));
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.one");
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.three");
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.five");
    }

    [Fact]
    public void Resolve_GridStarters_PlusPlayer_ThreeSeatsWithThePlayerMarked()
    {
        var pack = SixEntryPack(GridTestData.Grid(3,
            "driver.two", "driver.four", "driver.six"));

        var plan = RoundGridResolver.Resolve(pack, 1,
            new PlayerSeat { Ams2LiveryName = "Team A #4" });

        Assert.Equal(3, plan.Seats.Count);
        var player = Assert.Single(plan.Seats, s => s.IsPlayer);
        Assert.Equal("driver.four", player.DriverId);
    }

    /// <summary>The player always takes a real seat, even if the driver they replace did NOT start
    /// that round: the player's livery is added to the starter set so it survives the intersection.
    /// Here driver.one is a non-starter, but the player drives Team A #1 and must be on the grid.</summary>
    [Fact]
    public void Resolve_GridStarters_PlayerReplacingANonStarter_IsStillOnTheGrid()
    {
        var pack = SixEntryPack(GridTestData.Grid(3,
            "driver.two", "driver.four", "driver.six"));

        var plan = RoundGridResolver.Resolve(pack, 1,
            new PlayerSeat { Ams2LiveryName = "Team A #1" });

        var player = Assert.Single(plan.Seats, s => s.IsPlayer);
        Assert.Equal("driver.one", player.DriverId);
        // Total cars stay at grid.size: the player replaces the lowest-priority AI to stay within
        // the cap (three starters + the player is four; grid.size is three).
        Assert.Equal(3, plan.Seats.Count);
    }

    /// <summary>Absent grid block -> the pre-grid behaviour is unchanged: every covering entry
    /// fills the grid, nothing is trimmed.</summary>
    [Fact]
    public void Resolve_NoGridBlock_SeatsEveryCoveringEntry()
    {
        var pack = SixEntryPack(grid: null);

        var plan = RoundGridResolver.Resolve(pack, 1);

        Assert.Equal(6, plan.Seats.Count);
        Assert.Equal(
            ["driver.one", "driver.two", "driver.three", "driver.four", "driver.five", "driver.six"],
            plan.Seats.Select(s => s.DriverId));
    }

    /// <summary>Empty starterDriverIds behaves like an absent list for seating (every covering
    /// entry seats), but grid.size still caps the field, the trim keeps the highest raceSkill.</summary>
    [Fact]
    public void Resolve_GridSizeSmallerThanField_CapsKeepingHighestRaceSkill()
    {
        // Six entries, no starter list, grid.size 4 -> trim to the four fastest.
        var drivers = new[]
        {
            GridTestData.Driver("driver.one", "One", GridTestData.Ratings(raceSkill: 0.10)),
            GridTestData.Driver("driver.two", "Two", GridTestData.Ratings(raceSkill: 0.95)),
            GridTestData.Driver("driver.three", "Three", GridTestData.Ratings(raceSkill: 0.20)),
            GridTestData.Driver("driver.four", "Four", GridTestData.Ratings(raceSkill: 0.90)),
            GridTestData.Driver("driver.five", "Five", GridTestData.Ratings(raceSkill: 0.80)),
            GridTestData.Driver("driver.six", "Six", GridTestData.Ratings(raceSkill: 0.05)),
        };
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers: drivers,
            entries:
            [
                GridTestData.Entry("team.a", "driver.one", "1", "1", "Team A #1"),
                GridTestData.Entry("team.a", "driver.two", "2", "1", "Team A #2"),
                GridTestData.Entry("team.a", "driver.three", "3", "1", "Team A #3"),
                GridTestData.Entry("team.a", "driver.four", "4", "1", "Team A #4"),
                GridTestData.Entry("team.a", "driver.five", "5", "1", "Team A #5"),
                GridTestData.Entry("team.a", "driver.six", "6", "1", "Team A #6"),
            ],
            rounds: [GridTestData.Round(1, grid: GridTestData.Grid(4))]);

        var plan = RoundGridResolver.Resolve(pack, 1);

        Assert.Equal(4, plan.Seats.Count);
        // Kept: the four highest raceSkill (0.95, 0.90, 0.80, 0.20) -> two, four, five, three,
        // restored to entries.json order.
        Assert.Equal(["driver.two", "driver.three", "driver.four", "driver.five"],
            plan.Seats.Select(s => s.DriverId));
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.one"); // 0.10, dropped
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.six"); // 0.05, dropped
    }

    /// <summary>Safety cap with a starter list AND the venue trimming below the starter count:
    /// five started, grid.size is 4 (the track capped it). The player is kept unconditionally; the
    /// slowest non-player starter is dropped.</summary>
    [Fact]
    public void Resolve_GridSizeBelowStarterCount_KeepsPlayerAndDropsSlowestStarter()
    {
        var drivers = new[]
        {
            GridTestData.Driver("driver.one", "One", GridTestData.Ratings(raceSkill: 0.30)),
            GridTestData.Driver("driver.two", "Two", GridTestData.Ratings(raceSkill: 0.95)),
            GridTestData.Driver("driver.three", "Three", GridTestData.Ratings(raceSkill: 0.90)),
            GridTestData.Driver("driver.four", "Four", GridTestData.Ratings(raceSkill: 0.85)),
            GridTestData.Driver("driver.five", "Five", GridTestData.Ratings(raceSkill: 0.80)),
            GridTestData.Driver("driver.six", "Six", GridTestData.Ratings(raceSkill: 0.05)),
        };
        var pack = GridTestData.Pack(
            teams: [GridTestData.Team("team.a", "Team A")],
            drivers: drivers,
            entries:
            [
                GridTestData.Entry("team.a", "driver.one", "1", "1", "Team A #1"),
                GridTestData.Entry("team.a", "driver.two", "2", "1", "Team A #2"),
                GridTestData.Entry("team.a", "driver.three", "3", "1", "Team A #3"),
                GridTestData.Entry("team.a", "driver.four", "4", "1", "Team A #4"),
                GridTestData.Entry("team.a", "driver.five", "5", "1", "Team A #5"),
                GridTestData.Entry("team.a", "driver.six", "6", "1", "Team A #6"),
            ],
            // driver.six (0.05) is a starter but the slowest; grid.size 4 must drop it.
            rounds:
            [
                GridTestData.Round(1, grid: GridTestData.Grid(4,
                    "driver.one", "driver.two", "driver.three", "driver.five", "driver.six")),
            ]);

        // Player drives the slow #1 (0.30), kept regardless of rating.
        var plan = RoundGridResolver.Resolve(pack, 1, new PlayerSeat { Ams2LiveryName = "Team A #1" });

        Assert.Equal(4, plan.Seats.Count);
        Assert.Contains(plan.Seats, s => s.DriverId == "driver.one" && s.IsPlayer);
        Assert.DoesNotContain(plan.Seats, s => s.DriverId == "driver.six"); // slowest, dropped
        // Kept the player plus the three fastest remaining starters (two, three, five).
        Assert.Equal(["driver.one", "driver.two", "driver.three", "driver.five"],
            plan.Seats.Select(s => s.DriverId));
    }

    /// <summary>The real 1988 pack now carries a per-round grid: round 1 seats exactly the 26
    /// historical starters, not the 30 season entrants the entry list covers.</summary>
    [Fact]
    public void Resolve_1988Round1_SeatsTheHistoricalStartersNotEverySeasonEntrant()
    {
        var pack = GridTestData.LoadReferencePack("f1-1988");

        var round1 = RoundGridResolver.Resolve(pack, 1);
        int covering = pack.Entries.Count(e =>
            RoundsRange.TryParse(e.Rounds, out var r, out _) && r.Contains(1));

        Assert.True(covering > round1.Seats.Count,
            "the entry list should cover more drivers than actually started round 1");
        Assert.Equal(pack.Season.Rounds[0].Grid!.Size, round1.Seats.Count);
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

    // ---------- shared fixture: one team, six drivers, all covering round 1 ----------

    private static SeasonPack SixEntryPack(PackRoundGrid? grid) => GridTestData.Pack(
        teams: [GridTestData.Team("team.a", "Team A")],
        drivers:
        [
            GridTestData.Driver("driver.one", "One"),
            GridTestData.Driver("driver.two", "Two"),
            GridTestData.Driver("driver.three", "Three"),
            GridTestData.Driver("driver.four", "Four"),
            GridTestData.Driver("driver.five", "Five"),
            GridTestData.Driver("driver.six", "Six"),
        ],
        entries:
        [
            GridTestData.Entry("team.a", "driver.one", "1", "1", "Team A #1"),
            GridTestData.Entry("team.a", "driver.two", "2", "1", "Team A #2"),
            GridTestData.Entry("team.a", "driver.three", "3", "1", "Team A #3"),
            GridTestData.Entry("team.a", "driver.four", "4", "1", "Team A #4"),
            GridTestData.Entry("team.a", "driver.five", "5", "1", "Team A #5"),
            GridTestData.Entry("team.a", "driver.six", "6", "1", "Team A #6"),
        ],
        rounds: [GridTestData.Round(1, grid: grid)]);
}
