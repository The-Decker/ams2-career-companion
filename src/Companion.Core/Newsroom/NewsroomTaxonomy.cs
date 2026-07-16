namespace Companion.Core.Newsroom;

/// <summary>
/// The newsroom's editorial category vocabulary. A superset of the shipped unified-wire
/// categories (<c>NewsStoryCategory</c> in the ViewModels layer keeps its eight values for
/// legacy stories; new stories map onto this richer set and the wire projects both).
/// Values are serialized camelCase by CoreJson wherever they appear in content packs —
/// treat the names as data-format identifiers: additive only, never rename.
/// </summary>
public enum NewsroomCategory
{
    RaceReport,
    QualifyingReport,
    WeekendPreview,
    PostRaceAnalysis,
    ChampionshipAnalysis,
    DriverPerformance,
    TeamPerformance,
    DriverTransfers,
    ContractNews,
    InjuriesAndReplacements,
    Rivalries,
    TechnicalDevelopments,
    RegulationChanges,
    PenaltiesAndStewarding,
    MechanicalReliability,
    RecordsAndMilestones,
    RookieWatch,
    VeteranWatch,
    TeamPolitics,
    OperationalPressure,
    HumanInterest,
    HistoricalRetrospective,
    AnniversaryFeature,
    Rumours,
    ConfirmedAnnouncements,
    EditorialOpinion,
    SeasonReview,
    CareerRetrospective,
    BreakingNews,
    WorldOfSmgp,
}

/// <summary>
/// Explicit editorial status. A rumour never silently becomes factual: resolution produces a
/// NEW linked story (Confirmed/denied) while the original keeps <see cref="Rumour"/>.
/// </summary>
public enum EditorialStatus
{
    Confirmed,
    Reported,
    Developing,
    Rumour,
    Analysis,
    Opinion,
    Retrospective,
}

/// <summary>
/// Content provenance — the visible boundary between what really happened and the player's
/// simulated universe. Every article, timeline entry, and history record carries one.
/// <list type="bullet">
/// <item><see cref="VerifiedHistorical"/> — real-world fact from a sourced dataset
/// (data/history, f1db CC BY 4.0). Never mixed with simulated outcomes.</item>
/// <item><see cref="CareerUniverse"/> — an outcome of the player's simulated career.</item>
/// <item><see cref="EditorialAnalysis"/> — desk-written interpretation over career facts.</item>
/// <item><see cref="SmgpFiction"/> — the SEGA-universe canon (almanac, driver lore); fictional
/// by definition, labeled so it can never be mistaken for real motorsport history.</item>
/// <item><see cref="SystemGenerated"/> — structural/system notices (migration, legacy).</item>
/// </list>
/// </summary>
public enum ContentProvenance
{
    VerifiedHistorical,
    CareerUniverse,
    EditorialAnalysis,
    SmgpFiction,
    SystemGenerated,
}

/// <summary>Editorial layout tier derived from the importance score — never random.</summary>
public enum EditorialTier
{
    Lead,
    Featured,
    Standard,
    Brief,
    ArchiveOnly,
}
