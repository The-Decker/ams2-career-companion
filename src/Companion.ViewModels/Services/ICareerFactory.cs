using Companion.Ams2;
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

    /// <summary>Explicit v2 Alpha experience id. Null preserves the exact legacy single-career
    /// creation path; the current wizard intentionally leaves it null until the three-mode flow is
    /// ready to create a complete progression-v2 profile and campaign atomically.</summary>
    public string? ExperienceMode { get; init; }

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

    /// <summary>The SMGP replica mode (M3): when true AND the pack declares <c>careerStyle "smgp"</c>,
    /// the career's start state seeds <see cref="Companion.Core.Smgp.SmgpState"/> — the per-career gate
    /// every mode mechanic (rival battles, seat swaps, the title defense) hangs off. Mirrors
    /// <see cref="FormAware"/>: default false, so existing creation callers (and every test that does
    /// not opt in) seed nothing and fold byte-identically; the wizard sets it for smgp packs. On a
    /// normal pack the flag is ignored — the pack's style is the other half of the gate.</summary>
    public bool SmgpMode { get; init; }

    /// <summary>OPT-IN modded field (v1.4): when true AND the pack declares a modded field AND its
    /// required car mod is installed, the creation-time transform appends the mod's grid entries
    /// and bumps the round grid sizes BEFORE pinning — so the pinned pack fields the fuller grid
    /// (the SMGP McLaren teams) and replays stay byte-identical. False (default) — or true but the
    /// mod missing — pins the base field only, so the default never depends on the mod. The wizard
    /// sets it for a pack that has a modded field.</summary>
    public bool UseModdedField { get; init; }

    /// <summary>OPT-IN alternate mod tracks (Mike's "RockyTM track switch"): when true AND every mod
    /// track the pack's alternates need is installed, the creation-time transform swaps each round
    /// with a <c>track.alternate</c> to that alternate BEFORE pinning — so the pinned pack drives the
    /// mod venues and replays stay byte-identical. When false (default) — or true but a required mod
    /// is missing — the season is pinned on its base/DLC defaults and NO mod track is used, so the
    /// default never depends on a mod. Seed-driven per-round variety is a later slice.</summary>
    public bool UseAlternateTracks { get; init; }

    /// <summary>The career's MORTALITY mode (character death &amp; injury, Slice 1;
    /// docs/dev/character-death-injury.md §2). Default <see cref="Companion.Core.Career.MortalityMode.Off"/>
    /// so existing creation callers (and every test that does not opt in) create a career with no
    /// injury/death — byte-identical to before. The wizard surfaces Off/Normal/Hardcore as an explicit
    /// creation choice. Persisted on the <c>career</c> table AND mirrored into the start player state.</summary>
    public Companion.Core.Career.MortalityMode Mortality { get; init; }

    /// <summary>The Dynasty owner economy (docs/dev/dynasty-tycoon-economy.md): when true AND the
    /// campaign mode is <c>grandPrixDynasty</c>, the start state seeds
    /// <see cref="Companion.Core.Dynasty.DynastyEconomyState"/> — the per-career gate the whole
    /// team ledger hangs off. Mirrors <see cref="SmgpMode"/>: default false, so existing creation
    /// callers (and every test that does not opt in) seed nothing and fold byte-identically; the
    /// wizard sets it for new Dynasty careers. On any other mode the flag is ignored — the mode is
    /// the other half of the gate (a legacy/SMGP/Passport career can never gain the economy).</summary>
    public bool DynastyEconomy { get; init; }
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
/// Narrow machine-integration extension of <see cref="ICareerSession"/>. Keeping launch outside
/// the base session contract preserves every existing fake while the real session delegates to
/// the launcher configured by <see cref="CareerEnvironment"/>.
/// </summary>
public interface IAms2GameLaunch
{
    Ams2LaunchResult LaunchAms2();
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
