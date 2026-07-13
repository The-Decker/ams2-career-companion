using Companion.Core.Character;
using Companion.Core.Determinism;

namespace Companion.Core.Career;

/// <summary>The inputs to one accident fold (character death &amp; injury §3.2/§3.3): the carried player
/// state, the round's captured severity, the driver's safety profile (durability + injury perk mods),
/// the tunable bands, and the determinism key (masterSeed + year + round).</summary>
public sealed record AccidentFoldContext
{
    public required PlayerCareerState Player { get; init; }
    public required AccidentSeverity Severity { get; init; }
    public required double Durability { get; init; }
    public required PlayerPerkModifiers Modifiers { get; init; }
    public required AccidentRules Rules { get; init; }
    public required ulong MasterSeed { get; init; }
    public required int Year { get; init; }
    public required int Round { get; init; }
    public required string PlayerName { get; init; }
}

/// <summary>The result of an accident fold: the (possibly injured/deceased) player state, the derived
/// journal rows, and the resolved outcome (for the caller's death-transition detection).</summary>
public sealed record AccidentFoldResult
{
    public required PlayerCareerState State { get; init; }
    public required IReadOnlyList<JournalEvent> Events { get; init; }
    public required AccidentOutcome Outcome { get; init; }
}

/// <summary>
/// The DERIVED per-round accident fold (character death &amp; injury §3.2/§3.3), mirroring
/// <c>SmgpBattleFold</c>: draw the d500 from a fresh keyed stream, resolve it against the driver's safety
/// profile + bands, apply the injury/death state change, and emit a byte-compared <c>player.accident</c>
/// row (plus a news headline on a consequential outcome). A pure function of (masterSeed, year, round,
/// state, severity, rules) → the fold re-derives it identically on replay. The CALLER gates it (mortality
/// on + a character + an accident severity present) so an ordinary round never reaches here and stays
/// byte-identical.
/// </summary>
public static class AccidentFold
{
    public static AccidentFoldResult Apply(AccidentFoldContext ctx)
    {
        // ONE draw, from a fresh per-round stream — independent of every other stream, so re-creating it
        // on replay rolls the same d500 and nothing else shifts.
        int roll = new StreamFactory(ctx.MasterSeed)
            .CreateStream(CareerStreams.Accident, ctx.Year, ctx.Round, "player")
            .NextInt(1, 501); // d500: 1..500 inclusive
        var outcome = AccidentModel.Resolve(ctx.Severity, roll, ctx.Durability, ctx.Modifiers, ctx.Rules);

        var player = ctx.Player;
        player = outcome.Kind switch
        {
            AccidentOutcomeKind.MinorInjury => player with
            {
                RaceSuspensionRemaining = Math.Max(player.RaceSuspensionRemaining, outcome.MissRaces),
            },
            AccidentOutcomeKind.SeasonEnding => player with { SeasonEndingInjury = true },
            AccidentOutcomeKind.Death => player with { Deceased = true },
            _ => player,
        };

        var events = new List<JournalEvent>
        {
            new()
            {
                Phase = JournalPhases.PlayerAccident,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    severity = ctx.Severity,
                    roll,
                    effectiveRoll = outcome.EffectiveRoll,
                    outcome = OutcomeName(outcome.Kind),
                    missRaces = outcome.MissRaces,
                }),
                Cause = OutcomeCause(outcome.Kind),
            },
        };

        // A consequential outcome surfaces in the news feed (the stake is felt, not silent). A harmless
        // scare emits no headline.
        if (Headline(outcome, ctx.PlayerName) is { } text)
        {
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.Headline,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new { text }),
                Cause = OutcomeCause(outcome.Kind),
            });
        }

        return new AccidentFoldResult { State = player, Events = events, Outcome = outcome };
    }

    private static string OutcomeName(AccidentOutcomeKind kind) => kind switch
    {
        AccidentOutcomeKind.MinorInjury => "minorInjury",
        AccidentOutcomeKind.SeasonEnding => "seasonEnding",
        AccidentOutcomeKind.Death => "death",
        _ => "none",
    };

    private static string OutcomeCause(AccidentOutcomeKind kind) => kind switch
    {
        AccidentOutcomeKind.MinorInjury => "accident-injury",
        AccidentOutcomeKind.SeasonEnding => "accident-season-ending",
        AccidentOutcomeKind.Death => "accident-death",
        _ => "accident-none",
    };

    private static string? Headline(AccidentOutcome outcome, string playerName)
    {
        string who = string.IsNullOrEmpty(playerName) ? "The driver" : playerName;
        return outcome.Kind switch
        {
            AccidentOutcomeKind.MinorInjury => outcome.MissRaces == 1
                ? $"{who} shaken in a crash — ruled out of the next race"
                : $"{who} injured in a crash — out for the next {outcome.MissRaces} races",
            AccidentOutcomeKind.SeasonEnding => $"{who}'s season ends in the barriers — out until next year",
            AccidentOutcomeKind.Death => $"Tragedy: {who} killed in a racing accident",
            _ => null,
        };
    }
}
