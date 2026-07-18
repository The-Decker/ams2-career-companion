using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Debug;

/// <summary>
/// TIER-2 preview host (dynasty-passport-roadmap.md Piece 2, §4 of the build brief): a shippable,
/// DEV-ONLY <see cref="ICareerSession"/> that returns CANNED display projections and is
/// <b>never resimulated, never serialized to a <c>.ams2career</c>, and never touches a database</b>.
/// It is the render-harness stand-in pattern promoted out of the test suite so the app can preview
/// states that cannot be reached through the real fold, Racing Passport (unbuildable), an arbitrary
/// level, or a death/career-over/sit-out screen without a fatal round.
///
/// FOLD SAFETY: this class holds no journal, no seed, and no DB. <see cref="Apply"/> and
/// <see cref="Preview"/> do NOT fold anything and write nothing; every read returns a value handed to
/// it at construction. It exists solely to drive Views. Because it can never produce a persisted
/// career, it can never diverge on replay, there is nothing to replay.
///
/// The default interface methods on <see cref="ICareerSession"/> cover every projection this host
/// does not override; the overrides below are the ones a preview deliberately seeds.
/// </summary>
public sealed class PreviewCareerSession : ICareerSession, IDisposable
{
    public PreviewCareerSession(SeasonPack pack, CareerSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Pack = pack;
        Summary = summary ?? new CareerSummary
        {
            CareerName = pack.Manifest.Name,
            SeasonYear = pack.Season.Year,
            SeriesName = pack.Season.SeriesName,
            CurrentRound = 1,
            RoundCount = pack.Season.Rounds.Count,
            PlayerDriverId = "driver.player-donor",
            PlayerLiveryName = DebugPreviewPack.PlayerLivery,
        };
        Briefing = BuildBriefing(pack);
    }

    // ---- required (no-default) members ----

    public CareerSummary Summary { get; set; }

    public SeasonPack Pack { get; }

    /// <summary>The briefing the Race tab shows (a sensible default is built from round 1).</summary>
    public BriefingModel? Briefing { get; set; }

    public BriefingModel? CurrentBriefing() => Summary.SeasonComplete ? null : Briefing;

    public StageOutcome StageCurrentGrid() => new()
    {
        Success = false,
        Messages = ["Preview session, staging is disabled (nothing is written to the game or disk)."],
    };

    /// <summary>The grid returned to result-entry (built from the preview pack). A preview never
    /// applies a result, but a functional grid keeps every View that reads it happy.</summary>
    public IReadOnlyList<GridSeat> Grid { get; set; } = [];

    public IReadOnlyList<GridSeat> CurrentGrid() => Grid;

    /// <summary>DISPLAY-ONLY: never folds, never writes. Returns a harmless canned confirm model.</summary>
    public ConfirmModel Preview(ResultDraft draft) => new()
    {
        RoundPoints = Array.Empty<(string, Rational)>(),
        Movements = Array.Empty<(string, int?, int?)>(),
        Headline = "Preview, results are not scored.",
    };

    /// <summary>NO-OP by contract: a preview session must never fold or persist a round. Advancing
    /// the round here would imply a fold; instead the preview stays exactly where it was seeded.</summary>
    public void Apply(ResultDraft draft)
    {
        // Intentionally empty, a Tier-2 preview is display-only and never mutates career state.
    }

    public StandingsSnapshot? Standings { get; set; }

    public StandingsSnapshot? CurrentStandings() => Standings;

    public IReadOnlyList<StandingsSnapshot> Snapshots { get; set; } = [];

    public IReadOnlyList<StandingsSnapshot> AllSnapshots() => Snapshots;

    public int? SliderRecommendation { get; set; }

    public int? CurrentSliderRecommendation() => SliderRecommendation;

    public SeasonReviewModel? Review { get; set; }

    public SeasonReviewModel? SeasonReview() => Review;

    public void AcceptOffer(string teamId)
    {
        // Preview, offer acceptance is not persisted.
    }

    // ---- seeded projections (each overrides an ICareerSession default) ----

    public PlayerMortalityStatus MortalityStatus { get; set; } = new()
    {
        Mode = Companion.Core.Career.MortalityMode.Off,
        Deceased = false,
        SeasonEndingInjury = false,
        RaceSuspensionRemaining = 0,
        CareerFileDeleted = false,
    };

    /// <summary>Implements <see cref="ICareerSession.Mortality"/> (the mode) from the seeded status,
    /// so a preview and its status never drift.</summary>
    public Companion.Core.Career.MortalityMode Mortality => MortalityStatus.Mode;

    public PlayerMortalityStatus PlayerMortality() => MortalityStatus;

    public DeathScreenModel? Death { get; set; }

    public DeathScreenModel? DeathScreen() => Death;

    public SitOutStatus? SitOut { get; set; }

    public SitOutStatus? CurrentSitOut() => SitOut;

    public SmgpBriefingModel? SmgpBriefing { get; set; }

    public SmgpBriefingModel? CurrentSmgpBriefing() => SmgpBriefing;

    public SmgpFinaleModel? Finale { get; set; }

    public SmgpFinaleModel? SmgpFinale() => Finale;

    public SmgpPromotionModel? Promotion { get; set; }

    public SmgpPromotionModel? CurrentSmgpPromotion() => Promotion;

    public SmgpPromotionModel? Demotion { get; set; }

    public SmgpPromotionModel? CurrentSmgpDemotion(string? previousTeamId) => Demotion;

    public SmgpPaddockModel? Paddock { get; set; }

    public SmgpPaddockModel? SmgpPaddock() => Paddock;

    public CharacterDossier? Dossier { get; set; }

    public CharacterDossier? CharacterDossier() => Dossier;

    public SkillTreeSnapshot? Tree { get; set; }

    public SkillTreeSnapshot? SkillTree() => Tree ?? SkillTreeSnapshot.Empty;

    public int SkillPoints { get; set; }

    public int AvailableCharacterCp() => SkillPoints;

    public IReadOnlyList<CampaignTimelineEntry> Timeline { get; set; } = [];

    public IReadOnlyList<CampaignTimelineEntry> CampaignTimeline() => Timeline;

    public IReadOnlyList<SeasonScheduleEntry> Schedule { get; set; } = [];

    public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() => Schedule;

    public Companion.Core.Smgp.SmgpSeasonLoreEntry? SeasonLore { get; set; }

    public Companion.Core.Smgp.SmgpSeasonLoreEntry? CurrentSeasonLore() => SeasonLore;

    public (string DriverId, string DisplayName)? Identity { get; set; }

    public (string DriverId, string DisplayName)? PlayerIdentity() => Identity;

    public string? TeamName { get; set; }

    public string? PlayerTeamName() => TeamName;

    // ---- display-only mutators: safe no-ops so no preview button can throw ----
    // A Tier-2 preview is never resimulated and never persisted, so the INPUT mutators (which the
    // real session throws or folds on) are inert here. Overriding the throwing interface defaults
    // means a View hosted over a preview, e.g. the sit-out screen's Continue, the Driver tab's
    // skill-tree commands, advances harmlessly instead of raising an unhandled exception.

    /// <summary>Advances the injured sit-out screen: clears the seeded sit-out (no fold happens) so
    /// the shell moves on to the briefing instead of hitting the throwing interface default.</summary>
    public void AutoSimulateRound() => SitOut = null;

    public void ApplySkillPlan(IReadOnlyList<string> orderedNodeIds) { }

    public void ApplySkillReset() { }

    public void SpendCharacterPoint(CharacterSpend spend) { }

    public void RespecNode(string nodeId) { }

    public void RestoreSlot(string slotId) { }

    public void DeclareCurrentRoundWeather(bool isWet) { }

    public void StartNextSeason(string teamId) { }

    /// <summary>Builds the two seats of the preview grid from the pack entries.</summary>
    public PreviewCareerSession WithGridFromPack()
    {
        var teamName = Pack.Teams.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
        var driverName = Pack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
        var ratings = Pack.Drivers.ToDictionary(d => d.Id, d => d.Ratings, StringComparer.Ordinal);
        Grid = Pack.Entries.Select(e => new GridSeat
        {
            DriverId = e.DriverId,
            DriverName = driverName.GetValueOrDefault(e.DriverId, e.DriverId),
            Number = e.Number,
            TeamId = e.TeamId,
            TeamName = teamName.GetValueOrDefault(e.TeamId, e.TeamId),
            Ams2LiveryName = e.Ams2LiveryName,
            Ratings = ratings.GetValueOrDefault(e.DriverId) ?? Pack.Drivers[0].Ratings,
            Reliability = 0.9,
            WeightScalar = 1.0,
            PowerScalar = 1.0,
            DragScalar = 1.0,
            IsPlayer = string.Equals(e.Ams2LiveryName, Summary.PlayerLiveryName, StringComparison.Ordinal),
        }).ToArray();
        return this;
    }

    private static BriefingModel BuildBriefing(SeasonPack pack) => new()
    {
        Round = pack.Season.Rounds[0],
        VenueDisplayName = pack.Season.Rounds[0].Name,
        IsPlaceholder = false,
        Settings = [new CopyableSetting("Track", pack.Season.Rounds[0].Track.Id) { Section = "Event" }],
    };

    public void Dispose()
    {
        // No DB, no watcher, no unmanaged handle, a preview owns nothing to release.
    }
}
