using Companion.Core.Scoring;

namespace Companion.Tests.Oracle;

/// <summary>
/// Maps f1db oracle fixture DTOs onto the engine's input types. Fixtures are self-contained
/// (catalog round overrides already resolved to per-round flags at generation time), so this
/// is a straight structural translation, no rules logic lives here.
/// </summary>
public static class F1DbFixtureMapper
{
    public static IReadOnlyList<RoundResult> MapRounds(F1DbSeasonFixture fixture) =>
        fixture.Rounds
            .OrderBy(r => r.Round)
            .Select(MapRound)
            .ToList();

    public static RoundResult MapRound(F1DbRoundFixture round) => new()
    {
        Round = round.Round,
        CountsForConstructors = round.CountsForConstructors,
        PointsFactor = round.PointsFactor,
        AlternateRaceTableId = round.AlternateRaceTableId,
        Sessions = round.Sessions.Select(MapSession).ToList(),
    };

    public static SessionResult MapSession(F1DbSessionFixture session) => new()
    {
        Kind = session.Kind,
        Entries = session.Entries.Select(MapEntry).ToList(),
        FastestLapDriverIds = session.FastestLapDriverIds,
    };

    public static ClassifiedEntry MapEntry(F1DbEntryFixture entry) => new()
    {
        DriverId = entry.DriverId,
        ConstructorId = entry.ConstructorId,
        Position = entry.Position,
        Status = entry.Status,
        SharedDrive = entry.SharedDrive,
        PointsEligible = entry.PointsEligible,
        PointsPosition = entry.PointsPosition,
    };
}
