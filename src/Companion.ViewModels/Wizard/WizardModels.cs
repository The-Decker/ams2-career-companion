using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Wizard;

/// <summary>The one-screen wizard steps (app-shell contract). The Character step (Increment 4a)
/// sits between seat pick and confirm; it is present only when character rules are loaded, so a
/// build without them (some tests) flows straight seat-pick → confirm.</summary>
public enum WizardStep
{
    SeasonPick = 0,
    Verification = 1,
    SeatPick = 2,
    /// <summary>Create your driver. Present only when character rules are loaded. Comes BEFORE the
    /// Season's Grid (Mike's flow: select car → create character → see the grid → confirm), so the
    /// grid reveal — where YOU already have a car and a character — is the last look before confirm.</summary>
    Character = 3,
    /// <summary>Choose the season's field — which seats are on the grid (v0.6.0). Always present;
    /// defaults to the whole pack, so leaving it untouched is byte-identical to before.</summary>
    Grid = 4,
    Confirm = 5,
}

/// <summary>One includable seat on the grid-choice step: a seat (by livery) the player can toggle
/// on/off for the season field. The player's own seat is <see cref="IsLocked"/> (always included).</summary>
public sealed partial class GridSeatChoice : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>The seat's primary (longest-tenure) livery, shown on the row.</summary>
    public required string LiveryName { get; init; }

    /// <summary>Every livery this seat uses across the season — one per driver when the seat changed
    /// hands mid-year (e.g. Williams #5 = Mansell / Brundle / Schlesser). Excluding the seat drops all
    /// of them from the field; the grid selection is built from these, not the single primary livery.</summary>
    public required IReadOnlyList<string> Liveries { get; init; }

    public required string DriverName { get; init; }
    public required string TeamName { get; init; }

    /// <summary>Uppercase name/team for the grid card's racing-style labels (WPF TextBlock has no
    /// text-transform, so the casing is done here).</summary>
    public string DriverNameUpper => DriverName.ToUpperInvariant();
    public string TeamNameUpper => TeamName.ToUpperInvariant();

    /// <summary>The seat's primary driver id — keys the optional drop-in portrait
    /// (<c>data/ams2/portraits/&lt;driverId&gt;.jpg</c>) on the grid card. Empty (the own-entrant
    /// row) = no portrait slot.</summary>
    public string DriverId { get; init; } = "";

    /// <summary>The seat's team id — keys the per-team PLAYER portrait on the locked "You" card.</summary>
    public string TeamId { get; init; } = "";

    /// <summary>The player's own seat — always on the grid, its checkbox disabled.</summary>
    public bool IsLocked { get; init; }

    /// <summary>The portrait key for this card's hero image. The player's own (locked) card shows
    /// the TEAM's player image — <c>data/ams2/portraits/player.&lt;team&gt;.jpg</c>, the team-coloured
    /// helmet (Mike: a different player image per team; "player.minarae") — instead of the AI driver's
    /// face; every other card shows the seat driver's own portrait. Falls back to a plain "player" key
    /// for the team-less own-entrant row.</summary>
    public string PortraitKey => IsLocked ? PlayerImageKey(TeamId) : DriverId;

    /// <summary>The per-team player-image key: <c>player.&lt;team&gt;</c> (the team id without its
    /// "team." prefix), or plain <c>player</c> when there is no team.</summary>
    public static string PlayerImageKey(string teamId)
    {
        string t = teamId.StartsWith("team.", StringComparison.Ordinal) ? teamId["team.".Length..] : teamId;
        return t.Length > 0 ? "player." + t : "player";
    }

    /// <summary>True when the seat can be toggled (i.e. NOT the locked player seat) — the checkbox's
    /// enabled state.</summary>
    public bool IsUnlocked => !IsLocked;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isIncluded = true;
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
