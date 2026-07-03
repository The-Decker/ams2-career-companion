using Companion.Core.Career;
using Companion.Data;
using Microsoft.Data.Sqlite;

namespace Companion.Tests.Data;

public class MigrationsV2Tests
{
    /// <summary>Builds a genuine schema-v1 career file with rows in every v1 table, exactly
    /// like an older app version would have left it.</summary>
    private static void CreateV1File(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();

        Migrations.Apply(connection, targetVersion: 1);

        using var seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO career (id, name, created_utc, master_seed, app_version)
            VALUES (1, 'Mike', '2026-01-01T00:00:00Z', 42, '0.4.0');
            INSERT INTO pinned_pack (pack_id, version, sha256, pack_json, pinned_utc)
            VALUES ('f1-1967', '1.0.0', 'cafe', x'7B7D', '2026-01-01T00:00:00Z');
            INSERT INTO season (year, pack_id, pack_version, status)
            VALUES (1967, 'f1-1967', '1.0.0', 'active');
            INSERT INTO round_result_raw (season_id, round, entered_utc, source, payload_json)
            VALUES (1, 1, '2026-01-02T00:00:00Z', 'manual', x'7B7D');
            INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause)
            VALUES ('2026-01-02T00:00:00Z', 1, 1, 'race.result', 'player', '{}', 'midfield');
            """;
        seed.ExecuteNonQuery();
    }

    [Fact]
    public void V1CareerFileUpgradesInPlaceToV2()
    {
        using var tmp = new TempDb();
        CreateV1File(tmp.Path);

        using var db = CareerDatabase.Open(tmp.Path);

        Assert.Equal(2, Migrations.CurrentVersion);
        Assert.Equal(2, db.SchemaVersion);

        // Every v1 row survived the upgrade untouched.
        var career = CareerStore.ReadCareer(db);
        Assert.Equal("Mike", career.Name);
        Assert.Equal(42UL, career.MasterSeed);
        Assert.Single(JournalStore.ReadAll(db));
        Assert.Single(ResultStore.ReadSeasonResults(db, 1));
        Assert.Equal("{}", ResultStore.ReadSeasonResults(db, 1)[0].PayloadJson);

        // And the v2 tables are live and usable against the existing season.
        StateStore.UpsertPlayerState(db, 1, StateStore.StageStart, new PlayerCareerState { Reputation = 10.0 });
        Assert.Equal(10.0, StateStore.ReadPlayerState(db, 1, StateStore.StageStart)!.Reputation);
        StateStore.UpsertDriverStates(db, 1, StateStore.StageStart,
            [new DriverCareerState { DriverId = "driver.x", Age = 30 }]);
        Assert.Single(StateStore.ReadDriverStates(db, 1, StateStore.StageStart));
        Assert.Empty(StateStore.ReadOffers(db, 1));
    }

    [Fact]
    public void ReopeningAnUpgradedFileIsANoOp()
    {
        using var tmp = new TempDb();
        CreateV1File(tmp.Path);

        using (var first = CareerDatabase.Open(tmp.Path))
            Assert.Equal(2, first.SchemaVersion);
        using var second = CareerDatabase.Open(tmp.Path);
        Assert.Equal(2, second.SchemaVersion);
        Assert.Equal("Mike", CareerStore.ReadCareer(second).Name);
    }

    [Fact]
    public void NewerSchemaThanTheAppUnderstandsIsRefusedLoudly()
    {
        using var tmp = new TempDb();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = tmp.Path,
                   Mode = SqliteOpenMode.ReadWriteCreate,
                   Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var bump = connection.CreateCommand();
            bump.CommandText = "PRAGMA user_version = 99;";
            bump.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => CareerDatabase.Open(tmp.Path));
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
