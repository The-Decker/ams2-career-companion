using Companion.Core.Character;

namespace Companion.ViewModels.Services;

/// <summary>Everything needed to create a new career from a season pack.</summary>
public sealed record CareerCreationRequest
{
    public required string PackDirectory { get; init; }

    /// <summary>Where the new *.ams2career SQLite file is created. Must not already exist.</summary>
    public required string CareerFilePath { get; init; }

    public required string CareerName { get; init; }

    public required long MasterSeed { get; init; }

    /// <summary>EXACT livery display name of the entry the player takes over (v1 locked
    /// decision: the player replaces that historical driver).</summary>
    public required string PlayerLiveryName { get; init; }

    /// <summary>Raw XML text of the user's installed class file, to import as the season's
    /// ratings/name/country baseline (NAMeS-first, locked decision #7). Null = pack baseline.
    /// The IMPORTED result is pinned into the career DB — the career never re-reads the
    /// mutable installed file afterwards.</summary>
    public string? CommunityBaselineXml { get; init; }

    /// <summary>Where <see cref="CommunityBaselineXml"/> was read from — journaled provenance
    /// only, never re-read.</summary>
    public string? CommunityBaselineSourcePath { get; init; }

    /// <summary>The player's authored character (stats + perks), or null for a career with no
    /// character. Written once at creation as the <c>player.character</c> INPUT row and seeded into
    /// the start player state; the sim derives the rating writes + perk modifier from it
    /// deterministically. (Increment 4a.)</summary>
    public CharacterProfile? Character { get; init; }

    /// <summary>The season field the player chose (the liveries on the grid), or null for the whole
    /// pack. A creation-time deterministic INPUT seeded into the season start state; the sim folds
    /// exactly this field and the staged custom-AI file carries exactly these drivers. (v0.6.0.)</summary>
    public Companion.Core.Grid.GridSelection? GridSelection { get; init; }

    /// <summary>Ratings Phase 3: when true, the sim FOLD reacts to the pack's per-race form (a hot
    /// rival shifts the player's expected finish / OPI / pace anchor). Seeded into the season start
    /// state (<see cref="Companion.Core.Career.PlayerCareerState.FormAware"/>) and carried forward.
    /// Default false so existing creation callers (and every test that does not opt in) fold exactly
    /// as before — byte-identical. The new-career wizard sets it true for all new careers.</summary>
    public bool FormAware { get; init; }

    /// <summary>OPT-IN alternate mod tracks (Mike's "RockyTM track switch"): when true AND every mod
    /// track the pack's alternates need is installed, the creation-time transform swaps each round
    /// with a <c>track.alternate</c> to that alternate BEFORE pinning — so the pinned pack drives the
    /// mod venues and replays stay byte-identical. When false (default) — or true but a required mod
    /// is missing — the season is pinned on its base/DLC defaults and NO mod track is used, so the
    /// default never depends on a mod. Seed-driven per-round variety is a later slice.</summary>
    public bool UseAlternateTracks { get; init; }
}

/// <summary>
/// Creates/opens career sessions. The wizard and start screen depend on this instead of the
/// concrete <see cref="CareerSessionService"/> so tests can substitute fakes.
/// </summary>
public interface ICareerFactory
{
    ICareerSession Create(CareerCreationRequest request);

    ICareerSession Open(string careerFilePath);
}

/// <summary>Default factory over <see cref="CareerSessionService"/>.</summary>
public sealed class CareerSessionFactory(CareerEnvironment environment) : ICareerFactory
{
    public ICareerSession Create(CareerCreationRequest request) =>
        CareerSessionService.CreateCareer(request, environment);

    public ICareerSession Open(string careerFilePath) =>
        CareerSessionService.OpenCareer(careerFilePath, environment);
}

/// <summary>
/// Additive staging extension of the <see cref="ICareerSession"/> seam: staging over a
/// custom-AI file the app did not generate (the user's curated community file) fails by
/// default; <see cref="StageCurrentGrid"/> with <c>force: true</c> is the explicit
/// user choice to proceed — a timestamped backup is still taken first.
/// </summary>
public interface IForceStaging
{
    StageOutcome StageCurrentGrid(bool force);
}

/// <summary>
/// The explicit "apply this grid to AMS2" action: ALWAYS writes an app-marked custom-AI file
/// (backup-first), bypassing the diff-aware no-op and the community-file gate, so a grid the user
/// deliberately chose lands on disk and is verifiable there. The AMS2 diagnosis (2026-07-07) found
/// the ordinary staging flow frequently wrote 0 bytes (NAMeS-primary no-op + force-gate) — which is
/// why the user's edits never reached the game. This path guarantees a real write.
/// </summary>
public interface IExplicitGridApply
{
    StageOutcome ApplyGridToAms2();
}
