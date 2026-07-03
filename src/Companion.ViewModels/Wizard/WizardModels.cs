using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Wizard;

/// <summary>The four one-screen wizard steps (app-shell contract).</summary>
public enum WizardStep
{
    SeasonPick = 0,
    Verification = 1,
    SeatPick = 2,
    Confirm = 3,
}

/// <summary>One line of the verification step: a structural/content/scan finding. Info
/// items (e.g. the livery-scan summary when every file was readable) are purely
/// informational — they never trigger the proceed-anyway gate.</summary>
public sealed record VerificationItem(bool IsError, string Message, bool IsInfo = false)
{
    public string Severity => IsError ? "Error" : IsInfo ? "Info" : "Warning";
}

/// <summary>One selectable seat: a pack entry with the ratings and team context that make
/// the choice informed (the player REPLACES this driver — v1 locked decision).</summary>
public sealed record SeatOption
{
    public required string LiveryName { get; init; }
    public required string DriverId { get; init; }
    public required string DriverName { get; init; }
    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string Number { get; init; }

    /// <summary>The entry's rounds-range expression ("1-11", "2-3,8-11").</summary>
    public required string Rounds { get; init; }

    public required double RaceSkill { get; init; }
    public required double QualifyingSkill { get; init; }

    /// <summary>Team budget tier — the "team tier" shown at seat pick.</summary>
    public required int TeamTier { get; init; }

    public required int Prestige { get; init; }
    public required double Reliability { get; init; }
}

/// <summary>Composes the confirm step's rules-summary chip strings from the pack's
/// <see cref="CatalogSeason"/> — points table, best-N drops, shared-drive policy,
/// constructors rule, fastest lap.</summary>
public static class RulesSummaryComposer
{
    public static IReadOnlyList<string> Compose(CatalogSeason season, int roundCount)
    {
        var lines = new List<string>
        {
            $"Points: {string.Join("-", season.RacePoints)}",
        };

        if (season.FastestLap is { } fastestLap)
            lines.Add($"Fastest lap: {fastestLap.Points} point{(fastestLap.Points == Companion.Core.Numerics.Rational.One ? "" : "s")}");

        if (season.DriversBestN is { } bestN)
        {
            if (bestN.WholeSeason is { } wholeSeason)
                lines.Add($"Drivers count their best {wholeSeason} results");
            else if (bestN.Split is { } split)
                lines.Add(
                    $"Drivers count their best {split.FirstCount} results from rounds 1-{split.FirstRounds} " +
                    $"plus their best {split.SecondCount} from rounds {split.FirstRounds + 1}-{roundCount}");
        }
        else
        {
            lines.Add("Every round counts (no dropped results)");
        }

        lines.Add(season.SharedDrivePolicy == SharedDrivePolicy.Split
            ? "Shared drives split the points between the drivers"
            : "Shared drives score no points");

        if (season.Constructors is { } constructors)
        {
            string constructorsLine = constructors.BestCarOnly
                ? "Constructors: only the best-placed car scores"
                : "Constructors: every car scores";
            if (constructors.BestN == "sameAsDrivers")
                constructorsLine += " (same dropped-results rule as drivers)";
            lines.Add(constructorsLine);
        }
        else
        {
            lines.Add("No constructors championship");
        }

        return lines;
    }
}
