using System.Collections.ObjectModel;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Data;

namespace Companion.Tests.Data;

public sealed class PlayerRoundConditionsStoreTests
{
    [Fact]
    public void DeclarationRoundTripsAsOneValidatedReadOnlyEntry()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, isWet: true);

        PlayerRoundConditionsInput declared = PlayerRoundConditionsStore.Declare(
            db, seasonId, pack, input, DataCareerFixture.Utc);

        Assert.Equal(input, declared);
        Assert.Equal(input, PlayerRoundConditionsStore.ReadRound(db, seasonId, pack, 1));

        IReadOnlyDictionary<int, PlayerRoundConditionsInput> season =
            PlayerRoundConditionsStore.ReadSeason(db, seasonId, pack);
        var readOnly = Assert.IsType<ReadOnlyDictionary<int, PlayerRoundConditionsInput>>(season);
        Assert.Equal(input, readOnly[1]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<int, PlayerRoundConditionsInput>)readOnly).Add(2, input));

        JournalRow row = Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            candidate => candidate.Phase == JournalPhases.PlayerRoundConditions);
        Assert.Equal(1, row.Round);
        Assert.Equal("player", row.Entity);
        Assert.Equal("pre-race", row.Cause);
        Assert.Equal(input, JsonSerializer.Deserialize<PlayerRoundConditionsInput>(row.DeltaJson, CoreJson.Options));
    }

    [Fact]
    public void RepeatingSameDeclarationIsIdempotentAndKeepsOneRow()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, isWet: false);

        PlayerRoundConditionsStore.Declare(db, seasonId, pack, input, DataCareerFixture.Utc);
        PlayerRoundConditionsInput repeated = PlayerRoundConditionsStore.Declare(
            db, seasonId, pack, input, "2026-07-13T01:00:00Z");

        Assert.Equal(input, repeated);
        Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            candidate => candidate.Phase == JournalPhases.PlayerRoundConditions);
    }

    [Fact]
    public void ConflictingDeclarationIsRejectedWithoutAppending()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        PlayerRoundConditionsInput dry = PlayerRoundConditions.Prepare(pack, 1, isWet: false);
        PlayerRoundConditionsInput wet = PlayerRoundConditions.Prepare(pack, 1, isWet: true);
        PlayerRoundConditionsStore.Declare(db, seasonId, pack, dry, DataCareerFixture.Utc);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            PlayerRoundConditionsStore.Declare(db, seasonId, pack, wet, DataCareerFixture.Utc));

        Assert.Contains("different facts", error.Message, StringComparison.Ordinal);
        Assert.Equal(dry, PlayerRoundConditionsStore.ReadRound(db, seasonId, pack, 1));
        Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            candidate => candidate.Phase == JournalPhases.PlayerRoundConditions);
    }

    [Fact]
    public void DeclarationIsRejectedAfterRawResultEvenWhenItMatchesExistingInput()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, isWet: false);
        PlayerRoundConditionsStore.Declare(db, seasonId, pack, input, DataCareerFixture.Utc);
        ResultStore.Append(db, seasonId, 1, """{"round":1}""", DataCareerFixture.Utc);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            PlayerRoundConditionsStore.Declare(db, seasonId, pack, input, DataCareerFixture.Utc));

        Assert.Contains("raw result already exists", error.Message, StringComparison.Ordinal);
        Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            candidate => candidate.Phase == JournalPhases.PlayerRoundConditions);
    }

    [Fact]
    public void AmbientTransactionRemainsCallerOwned()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, isWet: true);

        using (var transaction = db.Connection.BeginTransaction())
        {
            PlayerRoundConditionsStore.Declare(
                db, seasonId, pack, input, DataCareerFixture.Utc, transaction);
            Assert.Equal(
                input,
                PlayerRoundConditionsStore.ReadRound(db, seasonId, pack, 1, transaction));
            transaction.Rollback();
        }

        Assert.Null(PlayerRoundConditionsStore.ReadRound(db, seasonId, pack, 1));
    }

    [Fact]
    public void MalformedPayloadIsRejected()
    {
        using var fixture = StoreFixture.Create();
        fixture.Append(round: 1, deltaJson: "{not-json}");

        Assert.Throws<JsonException>(() => fixture.Read());
    }

    [Fact]
    public void UnsupportedVersionIsRejected()
    {
        using var fixture = StoreFixture.Create();
        PlayerRoundConditionsInput unsupported = fixture.Prepared(1) with { Version = 999 };
        fixture.Append(round: 1, deltaJson: Serialize(unsupported));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => fixture.Read());
        Assert.Contains("Unsupported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadRoundThatDoesNotMatchJournalKeyIsRejected()
    {
        using var fixture = StoreFixture.Create();
        fixture.Append(round: 2, deltaJson: Serialize(fixture.Prepared(1)));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => fixture.Read());
        Assert.Contains("contains round 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackThatDoesNotMatchPinnedPackIsRejected()
    {
        using var fixture = StoreFixture.Create();
        PlayerRoundConditionsInput tampered = fixture.Prepared(1) with { TrackId = "tampered-track" };
        fixture.Append(round: 1, deltaJson: Serialize(tampered));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => fixture.Read());
        Assert.Contains("does not match pinned track", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRoundIsRejectedEvenWhenPayloadsAreIdentical()
    {
        using var fixture = StoreFixture.Create();
        string delta = Serialize(fixture.Prepared(1));
        fixture.Append(round: 1, deltaJson: delta);
        fixture.Append(round: 1, deltaJson: delta);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => fixture.Read());
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonPlayerEntityIsRejected()
    {
        using var fixture = StoreFixture.Create();
        fixture.Append(round: 1, deltaJson: Serialize(fixture.Prepared(1)), entity: "driver.p");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => fixture.Read());
        Assert.Contains("expected 'player'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingJournalRoundIsRejected()
    {
        using var fixture = StoreFixture.Create();
        fixture.Append(round: null, deltaJson: Serialize(fixture.Prepared(1)));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => fixture.Read());
        Assert.Contains("no round key", error.Message, StringComparison.Ordinal);
    }

    private static string Serialize(PlayerRoundConditionsInput input) =>
        JsonSerializer.Serialize(input, CoreJson.Options);

    private sealed class StoreFixture : IDisposable
    {
        private readonly TempDb _temp;

        public CareerDatabase Database { get; }
        public long SeasonId { get; }
        public SeasonPack Pack { get; }

        private StoreFixture(TempDb temp, CareerDatabase database, long seasonId, SeasonPack pack)
        {
            _temp = temp;
            Database = database;
            SeasonId = seasonId;
            Pack = pack;
        }

        public static StoreFixture Create()
        {
            var temp = new TempDb();
            CareerDatabase database = CareerDatabase.Open(temp.Path);
            var (seasonId, pack) = DataCareerFixture.SetupCareer(database);
            return new StoreFixture(temp, database, seasonId, pack);
        }

        public PlayerRoundConditionsInput Prepared(int round, bool isWet = false) =>
            PlayerRoundConditions.Prepare(Pack, round, isWet);

        public void Append(int? round, string deltaJson, string entity = "player")
        {
            JournalStore.Append(
                Database,
                SeasonId,
                round,
                new JournalEvent
                {
                    Phase = JournalPhases.PlayerRoundConditions,
                    Entity = entity,
                    DeltaJson = deltaJson,
                    Cause = "pre-race",
                },
                DataCareerFixture.Utc);
        }

        public IReadOnlyDictionary<int, PlayerRoundConditionsInput> Read() =>
            PlayerRoundConditionsStore.ReadSeason(Database, SeasonId, Pack);

        public void Dispose()
        {
            Database.Dispose();
            _temp.Dispose();
        }
    }
}
