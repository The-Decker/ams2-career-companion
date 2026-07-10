using Companion.Core.Character;

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
        string? playerLiveryName,
        IReadOnlyList<CharacterSpend>? spends = null,
        CharacterRules? characterRules = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(acceptedTeamId);

        // The SMGP replica owns its player's seat — the rival ladder and the title defense
        // decide it in the season-end fold, so the accepted offer never reseats an smgp player
        // (offers remain the season-advance trigger only). Every other career: the shipped
        // offer-driven reseat, byte-identical.
        var player = playerEnd.Smgp is not null
            ? playerEnd
            : playerEnd with
            {
                CurrentTeamId = acceptedTeamId,
                LiveryName = playerLiveryName ?? playerEnd.LiveryName,
            };

        // Between-season development (character depth 4): apply the player's journaled statSpend
        // INPUTs to the carried character as the career rolls into the next year — the exact same
        // application the era-transition path makes, so a same-pack CARRYOVER develops the driver
        // just like a changeover. No spends (or no character) → unchanged, so ordinary rollovers
        // stay byte-identical.
        if (spends is { Count: > 0 } && characterRules is not null && player.Character is { } character)
            player = player with { Character = CharacterProgress.ApplyAll(character, spends, characterRules) };

        return new SeasonStartStates
        {
            Player = player,
            Drivers = driversEnd,
            Teams = teamsEnd,
        };
    }
}
