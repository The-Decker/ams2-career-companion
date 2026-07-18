using Companion.Core.Career;
using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>The rich "career over" projection behind the death screen (docs/dev/character-death-injury.md
/// §6): an in-world obituary, the driver's whole career record, the cause of death (the fatal accident's
/// severity + venue), and, in Normal, the restorable save slots that can UN-DO the death. A pure read
/// over the folded journal/state (no new persistence), so it never perturbs replay. On a Hardcore death it
/// is captured BEFORE the career file is deleted, so it renders with no DB (mirroring the DB-free
/// <see cref="PlayerMortalityStatus"/> path, the shell must never touch the DB after a permadeath).</summary>
public sealed record DeathScreenModel
{
    public required MortalityMode Mode { get; init; }

    public required string DriverName { get; init; }

    /// <summary>The driver's age at death (their created age advanced by seasons played), or null for a
    /// legacy character with no authored age.</summary>
    public int? Age { get; init; }

    /// <summary>The venue where the fatal accident happened (the fatal round's track), or null if unknown.</summary>
    public string? Venue { get; init; }

    /// <summary>The round number of the fatal accident, or null if unknown.</summary>
    public int? Round { get; init; }

    public AccidentSeverity? Severity { get; init; }

    /// <summary>The one-line cause-of-death headline, e.g. "Killed in a heavy accident at Monaco (round 6)."</summary>
    public required string CauseOfDeath { get; init; }

    /// <summary>A short in-world obituary, the driver's name, the accident, and the record left behind.</summary>
    public required string Obituary { get; init; }

    /// <summary>The driver's whole career record (seasons, wins, podiums, titles, best finish).</summary>
    public required CareerRecordsBook Record { get; init; }

    /// <summary>The per-season career recap cards, in the order <see cref="CareerTimeline"/> produces them.</summary>
    public required IReadOnlyList<CareerSeasonCard> Seasons { get; init; }

    /// <summary>The restorable save slots (Normal only; empty in Hardcore/Off). A non-empty list in Normal
    /// means the death can be undone by restoring a slot.</summary>
    public required IReadOnlyList<SaveSlotInfo> RestoreSlots { get; init; }

    /// <summary>Hardcore permadeath, the career file is (or is about to be) physically deleted; there is
    /// no restore, ever.</summary>
    public bool IsPermadeath => Mode == MortalityMode.Hardcore;

    /// <summary>A Normal death with at least one save to fall back to, the death can be reversed.</summary>
    public bool CanRestore => Mode == MortalityMode.Normal && RestoreSlots.Count > 0;

    /// <summary>Compose the model from the raw pieces the session gathers. Pure (no DB, no I/O) so the
    /// obituary/cause copy is unit-tested from plain values and the Hardcore path can capture it before the
    /// file is gone.</summary>
    public static DeathScreenModel Build(
        MortalityMode mode, string driverName, int? age,
        AccidentSeverity? severity, string? venue, int? round,
        CareerRecordsBook record, IReadOnlyList<CareerSeasonCard> seasons,
        IReadOnlyList<SaveSlotInfo> restoreSlots)
    {
        string sev = severity switch
        {
            AccidentSeverity.Light => "a light accident",
            AccidentSeverity.Heavy => "a heavy accident",
            _ => "an accident",
        };
        string where =
            venue is { Length: > 0 } v ? (round is int rn ? $"{v} (round {rn})" : v)
            : round is int rn2 ? $"round {rn2}"
            : "the race";

        string cause = $"Killed in {sev} at {where}.";
        string ageClause = age is int a ? $", aged {a}," : "";
        string obituary = $"{driverName}{ageClause} was killed in {sev} at {where}. {SummariseCareer(record)}";

        return new DeathScreenModel
        {
            Mode = mode,
            DriverName = driverName,
            Age = age,
            Venue = venue,
            Round = round,
            Severity = severity,
            CauseOfDeath = cause,
            Obituary = obituary,
            Record = record,
            Seasons = seasons,
            RestoreSlots = restoreSlots,
        };
    }

    /// <summary>One respectful sentence tallying the career left behind.</summary>
    private static string SummariseCareer(CareerRecordsBook r)
    {
        if (r.SeasonsRaced <= 0)
            return "A career ended before it truly began.";

        string seasons = r.SeasonsRaced == 1 ? "a single season" : $"{r.SeasonsRaced} seasons";

        if (r.Wins == 0 && r.Podiums == 0)
        {
            string best = r.BestFinish is int bf ? $", a best finish of P{bf}," : ",";
            return $"Across {seasons}{best} a story left unfinished. The paddock will not forget.";
        }

        var tally = new List<string>
        {
            r.Wins == 1 ? "1 win" : $"{r.Wins} wins",
            r.Podiums == 1 ? "1 podium" : $"{r.Podiums} podiums",
        };
        if (r.Championships > 0)
            tally.Add(r.Championships == 1 ? "1 title" : $"{r.Championships} titles");

        return $"Across {seasons}: {JoinOxford(tally)}. The paddock will not forget.";
    }

    /// <summary>"a", "a and b", "a, b, and c".</summary>
    private static string JoinOxford(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1],
    };
}
