namespace Companion.Core.Smgp;

/// <summary>One beat in the player's evolving career story, a milestone detected from the folded results
/// + the SMGP state (first start/points/pole/podium/win, each promotion or demotion, each title, a rivalry
/// earned, a career-over near-miss, season progress, the finale). DISPLAY-ONLY, a pure projection over the
/// immutable results. Lives in Core so the ordering/firsts logic is unit-testable; the ViewModels shape the
/// per-round input from the career database, exactly like <see cref="SmgpLiveStats"/>.</summary>
public sealed record SmgpCareerBeat
{
    /// <summary>When it happened, e.g. "Season 3 · Monaco" or "Season 1".</summary>
    public required string WhenLabel { get; init; }

    /// <summary>The beat's category (drives the GUI's icon / accent).</summary>
    public required SmgpBeatKind Kind { get; init; }

    /// <summary>The arcade headline, e.g. "FIRST WIN" / "PROMOTED TO MADONNA".</summary>
    public required string Headline { get; init; }

    /// <summary>A sentence of colour/detail.</summary>
    public required string Detail { get; init; }

    // ---- Structured ordering + subject (additive, default 0/"" so existing beat consumers/tests are
    //      unaffected). Populated by Detect so the living-world dispatch feed can sort chronologically and
    //      attach a rival's face/voice; the terse timeline card ignores them. ----

    /// <summary>The 1-based campaign season this beat belongs to (0 on a beat built without season context).</summary>
    public int Season { get; init; }

    /// <summary>The round within the season (0 for a season-level beat, arrival, title, finale, which
    /// have no single round).</summary>
    public int Round { get; init; }

    /// <summary>The beat's subject driver id when it names one (the rival for a
    /// <see cref="SmgpBeatKind.RivalryEarned"/>/<see cref="SmgpBeatKind.RivalryLost"/> beat), else empty —
    /// lets a consumer attach the rival's portrait / their own words.</summary>
    public string SubjectId { get; init; } = "";
}

/// <summary>The kind of career milestone a <see cref="SmgpCareerBeat"/> marks.</summary>
public enum SmgpBeatKind
{
    /// <summary>The player joined the SMGP world (the very first beat).</summary>
    Arrived,
    /// <summary>Their first race start.</summary>
    FirstStart,
    /// <summary>Their first championship points.</summary>
    FirstPoints,
    /// <summary>Their first top-5 finish.</summary>
    FirstTop5,
    /// <summary>Their first pole position.</summary>
    FirstPole,
    /// <summary>Their first podium.</summary>
    FirstPodium,
    /// <summary>Their first race win.</summary>
    FirstWin,
    /// <summary>A seat move UP the ladder (an accepted climb).</summary>
    Promotion,
    /// <summary>A forced move DOWN a tier (a forfeit / a lost title defense), or out of F1 SMGP entirely.</summary>
    Demotion,
    /// <summary>A championship won.</summary>
    Title,
    /// <summary>A two-wins battle earned over a named rival (a seat offer).</summary>
    RivalryEarned,
    /// <summary>A two-losses battle lost to a rival (reserved, currently surfaced through the demotion it
    /// causes; kept for the GUI/future finer detection).</summary>
    RivalryLost,
    /// <summary>A brush with the LEVEL D floor, one loss from career-over.</summary>
    NearMiss,
    /// <summary>Reaching a new season of the 17-season campaign (SEASON n/17).</summary>
    SeasonMilestone,
    /// <summary>The locked 17-season campaign finale, the summit.</summary>
    Finale,
    /// <summary>An accident injured the driver, they must sit out one or more rounds (character death &amp;
    /// injury §6).</summary>
    Injured,
    /// <summary>An accident ended the driver's season (they return next year).</summary>
    SeasonEndingInjury,
    /// <summary>The driver was KILLED in an accident, the SMGP career ends here.</summary>
    Died,
}

/// <summary>One round's player-facing facts, shaped by the ViewModels from the folded state + results.</summary>
public sealed record SmgpNarrativeRound
{
    /// <summary>The venue label for this round (the WhenLabel's location part).</summary>
    public required string Venue { get; init; }

    /// <summary>The pack round number, additive (default 0) so the dispatch feed can key a beat to its
    /// round for chronological ordering; the beat-content tests leave it 0.</summary>
    public int Round { get; init; }

    /// <summary>The player's race finishing position, or null when they did not finish / were unclassified.</summary>
    public int? Finish { get; init; }

    /// <summary>True when the player qualified on pole for this round.</summary>
    public bool Pole { get; init; }

    /// <summary>True once the player's CUMULATIVE season counted-points passed zero (through this round) —
    /// the first true value across the whole career marks the first-points beat.</summary>
    public bool ScoredPointsCumulative { get; init; }

    /// <summary>The player's seat TEAM after this round's fold (for detecting promotions/demotions).</summary>
    public required string SeatTeamName { get; init; }

    /// <summary>The player's seat team prestige after this round (5 = top house … 2 = the floor).</summary>
    public required int SeatPrestige { get; init; }

    /// <summary>The named rival the player beat twice this round to EARN a seat offer (journal
    /// <c>trigger == seatSwapOfferToPlayer</c>), or null. Read from the journal, never the battle streak
    /// (which resets to 0 in the same fold that fires the trigger).</summary>
    public string? RivalryWonOver { get; init; }

    /// <summary>The named rival the player LOST to twice this round, forfeiting the battle (journal
    /// <c>trigger == playerSeatForfeit</c>), or null. Its consequence (a tier drop, or a step toward the
    /// floor) is folded into this one beat rather than also emitting a bare demotion.</summary>
    public string? RivalryLostTo { get; init; }

    /// <summary>The DRIVER ID of the rival named in <see cref="RivalryWonOver"/>, additive (default null)
    /// so the dispatch feed can attach the rival's portrait + their own trash-talk to the story.</summary>
    public string? RivalryWonOverId { get; init; }

    /// <summary>The DRIVER ID of the rival named in <see cref="RivalryLostTo"/>, additive (default null).</summary>
    public string? RivalryLostToId { get; init; }

    /// <summary>Cumulative LEVEL-D floor losses after this round (a near-miss approaches the floor limit).</summary>
    public int FloorLosses { get; init; }

    /// <summary>True once the career has ended (the floor kicked the player out of F1 SMGP).</summary>
    public bool CareerOver { get; init; }

    /// <summary>The injuring outcome of the player's accident this round, <c>"minorInjury"</c> /
    /// <c>"seasonEnding"</c> / <c>"death"</c> from the <c>player.accident</c> journal row (character death &amp;
    /// injury §6), or null when the round had no injuring accident (a survived accident is skipped). Drives
    /// the living-world Setback dispatch. Additive (default null) so the beat-content tests leave it unset.</summary>
    public string? AccidentOutcome { get; init; }

    /// <summary>Races the driver is sidelined by a minor accident injury this round (0 unless
    /// <see cref="AccidentOutcome"/> is <c>"minorInjury"</c>). Additive (default 0).</summary>
    public int AccidentMissRaces { get; init; }
}

/// <summary>One season's player-facing facts, shaped by the ViewModels.</summary>
public sealed record SmgpNarrativeSeason
{
    /// <summary>1-based campaign ordinal (Season n of 17).</summary>
    public required int Ordinal { get; init; }

    /// <summary>The player's seat TEAM at the season START (before any round), catches a between-seasons
    /// promotion (an accepted season-review offer) or a lost-defense drop.</summary>
    public required string StartSeatTeamName { get; init; }

    /// <summary>The season-start seat prestige.</summary>
    public required int StartSeatPrestige { get; init; }

    /// <summary>Each scored round, in round order.</summary>
    public required IReadOnlyList<SmgpNarrativeRound> Rounds { get; init; }

    /// <summary>True once the season is complete.</summary>
    public bool Complete { get; init; }

    /// <summary>True when the player was the season champion (a completed season only).</summary>
    public bool PlayerChampion { get; init; }

    /// <summary>True when completing this season completes the 17-season campaign (the finale unlocks).</summary>
    public bool CampaignComplete { get; init; }

    /// <summary>True when that completed campaign was flawless (champion in all 17).</summary>
    public bool CampaignFlawless { get; init; }
}

/// <summary>
/// Detects the player's ordered career-milestone timeline from the shaped per-season/per-round facts. Pure
/// and deterministic: it walks seasons in ordinal order, then rounds in round order, emitting each beat once
/// in true chronological order. The career "firsts" (first start/points/top-5/pole/podium/win) fire once
/// across the whole career; promotions/demotions come from seat-tier transitions (which the folded state
/// exposes reliably, unlike the battle streak, which resets to 0 in the same fold that fires its trigger,
/// so a two-wins offer is read from the PENDING rival, and a forfeit from the tier drop it causes).
/// </summary>
public static class SmgpCareerBeats
{
    /// <summary>The LEVEL-D loss count that marks a career-over near-miss (one short of the floor limit).</summary>
    private static readonly int NearMissThreshold = SmgpRules.FloorLossLimit - 1;

    /// <summary>A genuine climb: both seats resolved (prestige &gt; 0) and the new one is higher. Requiring
    /// both resolved suppresses a false move when a seat's team could not be resolved (prestige 0).</summary>
    private static bool Climbed(int prev, int cur) => prev > 0 && cur > 0 && cur > prev;

    /// <summary>A genuine drop: both seats resolved and the new one is lower.</summary>
    private static bool Dropped(int prev, int cur) => prev > 0 && cur > 0 && cur < prev;

    public static IReadOnlyList<SmgpCareerBeat> Detect(IReadOnlyList<SmgpNarrativeSeason> seasons)
    {
        var beats = new List<SmgpCareerBeat>();

        // Career-wide "firsts", fire once each, the first time they occur across every season.
        bool firstStart = false, firstPoints = false, firstTop5 = false, firstPole = false,
             firstPodium = false, firstWin = false;

        int prevPrestige = int.MinValue;
        bool arrived = false;
        bool careerEnded = false;

        // The (season, round) the current beat belongs to, stamped onto every emitted beat so the
        // living-world dispatch feed can order it chronologically. A season-level beat uses the START/END
        // round sentinels so arrivals sort before, and titles/finales after, the season's scored rounds.
        int curSeason = 0, curRound = 0;
        void Emit(SmgpCareerBeat beat) => beats.Add(beat with { Season = curSeason, Round = curRound });

        foreach (var season in seasons)
        {
            if (careerEnded)
                break; // nothing happens after the floor kicks the player out

            string seasonLabel = $"Season {season.Ordinal}";
            curSeason = season.Ordinal;
            curRound = SmgpDispatch.SeasonStartRound;

            // Season-boundary seat: arrival (season 1) or a between-seasons move (a season-review promotion
            // that climbed a tier, or a lost title defense that dropped one).
            if (!arrived)
            {
                Emit(new SmgpCareerBeat
                {
                    WhenLabel = seasonLabel,
                    Kind = SmgpBeatKind.Arrived,
                    Headline = "ARRIVED",
                    Detail = string.IsNullOrWhiteSpace(season.StartSeatTeamName)
                        ? "A seat in the SEGA world, a number on the car, and everything still to prove."
                        : $"A seat at {season.StartSeatTeamName}, a number on the car, and everything still to prove.",
                });
                arrived = true;
            }
            else
            {
                Emit(new SmgpCareerBeat
                {
                    WhenLabel = seasonLabel,
                    Kind = SmgpBeatKind.SeasonMilestone,
                    Headline = $"SEASON {season.Ordinal} OF {SmgpRules.CampaignSeasons}",
                    Detail = string.IsNullOrWhiteSpace(season.StartSeatTeamName)
                        ? "A new season of the long climb begins."
                        : $"A new season begins, still with {season.StartSeatTeamName}.",
                });
                if (Climbed(prevPrestige, season.StartSeatPrestige))
                    Emit(SeatMove(seasonLabel, up: true, season.StartSeatTeamName, offSeason: true));
                else if (Dropped(prevPrestige, season.StartSeatPrestige))
                    Emit(SeatMove(seasonLabel, up: false, season.StartSeatTeamName, offSeason: true));
            }
            prevPrestige = season.StartSeatPrestige;

            int prevFloorLosses = 0; // resets each season (WithSeasonReset)

            foreach (var round in season.Rounds)
            {
                string when = $"{seasonLabel} · {round.Venue}";
                curRound = round.Round;

                if (!firstStart)
                {
                    firstStart = true;
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.FirstStart, Headline = "FIRST START",
                        Detail = "Lights out on the first race of the career.",
                    });
                }

                // Escalating firsts (points → top-5 → podium → win), plus the first pole. A breakout debut
                // can trip several in one round, each is a genuine, separate first.
                if (!firstPoints && round.ScoredPointsCumulative)
                {
                    firstPoints = true;
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.FirstPoints, Headline = "FIRST POINTS",
                        Detail = "On the championship board for the first time.",
                    });
                }
                if (round.Finish is { } pos)
                {
                    if (!firstTop5 && pos <= 5)
                    {
                        firstTop5 = true;
                        Emit(new SmgpCareerBeat
                        {
                            WhenLabel = when, Kind = SmgpBeatKind.FirstTop5, Headline = "FIRST TOP 5",
                            Detail = $"A first top-five, P{pos}.",
                        });
                    }
                    if (!firstPodium && pos <= 3)
                    {
                        firstPodium = true;
                        Emit(new SmgpCareerBeat
                        {
                            WhenLabel = when, Kind = SmgpBeatKind.FirstPodium, Headline = "FIRST PODIUM",
                            Detail = $"The rostrum, at last, P{pos}.",
                        });
                    }
                    if (!firstWin && pos == 1)
                    {
                        firstWin = true;
                        Emit(new SmgpCareerBeat
                        {
                            WhenLabel = when, Kind = SmgpBeatKind.FirstWin, Headline = "FIRST WIN",
                            Detail = "The first victory the SEGA world will remember.",
                        });
                    }
                }
                if (!firstPole && round.Pole)
                {
                    firstPole = true;
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.FirstPole, Headline = "FIRST POLE",
                        Detail = "Fastest of all in qualifying for the first time.",
                    });
                }

                // A two-wins offer earned this round (from the journal trigger, since the streak resets).
                if (round.RivalryWonOver is { Length: > 0 } wonOver)
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.RivalryEarned,
                        Headline = "RIVALRY WON",
                        Detail = $"Beat {wonOver} twice, the offer of his seat is on the table.",
                        SubjectId = round.RivalryWonOverId ?? "",
                    });

                // A two-losses forfeit this round: emit the rivalry-lost beat, and FOLD the tier drop it
                // causes into it (so we don't also emit a bare demotion for the same round).
                bool droppedTier = Dropped(prevPrestige, round.SeatPrestige);
                if (round.RivalryLostTo is { Length: > 0 } lostTo)
                {
                    string dest = droppedTier && !string.IsNullOrWhiteSpace(round.SeatTeamName)
                        ? $", forced down to {round.SeatTeamName}"
                        : "";
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.RivalryLost,
                        Headline = "RIVALRY LOST",
                        Detail = $"Lost to {lostTo} twice{dest}.",
                        SubjectId = round.RivalryLostToId ?? "",
                    });
                }
                else
                {
                    // Seat moves not tied to a rivalry loss this round (a battle-fold climb, or an
                    // off-a-round move that the state sequence exposes).
                    if (Climbed(prevPrestige, round.SeatPrestige))
                        Emit(SeatMove(when, up: true, round.SeatTeamName, offSeason: false));
                    else if (droppedTier)
                        Emit(SeatMove(when, up: false, round.SeatTeamName, offSeason: false));
                }
                prevPrestige = round.SeatPrestige;

                // A brush with the floor: crossing to one loss from career-over.
                if (round.FloorLosses >= NearMissThreshold && prevFloorLosses < NearMissThreshold && !round.CareerOver)
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.NearMiss,
                        Headline = "ON THE BRINK",
                        Detail = $"{round.FloorLosses} floor losses, one more ends the career.",
                    });
                prevFloorLosses = round.FloorLosses;

                // An accident this round injured, ended the season, or KILLED the driver (character death &
                // injury §6). A death ends the timeline like the floor knock-out.
                if (round.AccidentOutcome == "minorInjury")
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.Injured,
                        Headline = "SIDELINED",
                        Detail = round.AccidentMissRaces == 1
                            ? "A crash leaves the driver hurt, out of the next race."
                            : $"A crash leaves the driver hurt, out for {round.AccidentMissRaces} races.",
                    });
                else if (round.AccidentOutcome == "seasonEnding")
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.SeasonEndingInjury,
                        Headline = "SEASON IN THE BARRIERS",
                        Detail = "A heavy crash ends the season, the driver returns next year.",
                    });
                else if (round.AccidentOutcome == "death")
                {
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.Died,
                        Headline = "TRAGEDY",
                        Detail = "The driver was killed in an accident, the career ends here.",
                    });
                    careerEnded = true;
                    break;
                }

                if (round.CareerOver)
                {
                    Emit(new SmgpCareerBeat
                    {
                        WhenLabel = when, Kind = SmgpBeatKind.Demotion,
                        Headline = "OUT OF THE SMGP",
                        Detail = "The LEVEL D floor claimed the career, knocked out of F1 SMGP.",
                    });
                    careerEnded = true;
                    break;
                }
            }

            // Season-END beats (a title, the finale) sort AFTER every scored round of the season.
            curRound = SmgpDispatch.SeasonEndRound;

            if (season.Complete && season.PlayerChampion)
                Emit(new SmgpCareerBeat
                {
                    WhenLabel = seasonLabel, Kind = SmgpBeatKind.Title,
                    Headline = "CHAMPION",
                    Detail = $"Season {season.Ordinal} won, a championship banked.",
                });

            if (season.Complete && season.CampaignComplete)
                Emit(new SmgpCareerBeat
                {
                    WhenLabel = seasonLabel, Kind = SmgpBeatKind.Finale,
                    Headline = season.CampaignFlawless ? "THE EMPEROR RUN" : "SEVENTEEN CONQUERED",
                    Detail = season.CampaignFlawless
                        ? "Champion in all seventeen seasons, the SEGA world never forgets this name."
                        : "Seventeen seasons survived, the campaign is complete.",
                });
        }

        return beats;
    }

    private static SmgpCareerBeat SeatMove(string when, bool up, string teamName, bool offSeason)
    {
        string team = string.IsNullOrWhiteSpace(teamName) ? "a new team" : teamName;
        return up
            ? new SmgpCareerBeat
            {
                WhenLabel = when, Kind = SmgpBeatKind.Promotion,
                Headline = $"PROMOTED TO {team.ToUpperInvariant()}",
                Detail = offSeason
                    ? $"Signed for {team}, a rung up the ladder for the new season."
                    : $"Moved up into {team}, a rung up the ladder.",
            }
            : new SmgpCareerBeat
            {
                WhenLabel = when, Kind = SmgpBeatKind.Demotion,
                Headline = $"DROPPED TO {team.ToUpperInvariant()}",
                Detail = offSeason
                    ? $"Relegated to {team} for the new season, the seat could not be held."
                    : $"Forced down into {team}, the seat could not be held.",
            };
    }
}
