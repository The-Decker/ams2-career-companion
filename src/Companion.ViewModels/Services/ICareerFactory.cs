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
