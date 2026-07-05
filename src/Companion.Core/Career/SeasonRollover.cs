namespace Companion.Core.Career;

/// <summary>Start-of-season states for season N+1, derived from season N's end states.</summary>
public sealed record SeasonStartStates
{
    public required PlayerCareerState Player { get; init; }

    public required IReadOnlyList<DriverCareerState> Drivers { get; init; }

    public required IReadOnlyList<TeamCareerState> Teams { get; init; }
}

/// <summary>
/// THE season rollover: the one function that turns season N's 'end' states plus the player's
/// accepted offer into season N+1's 'start' states. The live path uses it when a new season
/// starts, and replay re-derives every follow-on season's start states through it and
/// compares against the stored rows — a mismatch is a divergence (docs/dev/m5-fix-integration.md,
/// "Multi-season starts"). v1 ships single-season careers; the mechanism is still honest.
///
/// Inputs are strictly: end states (derived data) + the two player CHOICES that are not
/// derivable — the accepted team and the livery the player takes there. Everything else
/// carries verbatim: driver states keep their order and retired flags (era transition and
/// pack changeover are M6 concerns layered on top of this function, not replacements for it).
/// </summary>
public static class SeasonRollover
{
    public static SeasonStartStates Derive(
        PlayerCareerState playerEnd,
        IReadOnlyList<DriverCareerState> driversEnd,
        IReadOnlyList<TeamCareerState> teamsEnd,
        string acceptedTeamId,
        string? playerLiveryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(acceptedTeamId);

        return new SeasonStartStates
        {
            Player = playerEnd with
            {
                CurrentTeamId = acceptedTeamId,
                LiveryName = playerLiveryName ?? playerEnd.LiveryName,
            },
            Drivers = driversEnd,
            Teams = teamsEnd,
        };
    }
}
