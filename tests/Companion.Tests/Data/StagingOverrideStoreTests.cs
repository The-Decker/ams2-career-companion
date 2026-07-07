using Companion.Core.Grid;
using Companion.Data;
using Microsoft.Data.Sqlite;

namespace Companion.Tests.Data;

/// <summary>The grid editor's per-seat override persistence (v4 staging_override table): cosmetic
/// rename/rebind overrides round-trip, upsert, and clear. Non-journaled — the sim never reads it.</summary>
public sealed class StagingOverrideStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "companion-staging-override", Guid.NewGuid().ToString("N") + ".ams2career");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private CareerDatabase OpenSeeded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
               }.ToString()))
        {
            c.Open();
            Migrations.Apply(c);
            using var seed = c.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO career (id, name, created_utc, master_seed, app_version)
                VALUES (1, 'Mike', '2026-01-01T00:00:00Z', 42, '0.5.0');
                INSERT INTO pinned_pack (pack_id, version, sha256, pack_json, pinned_utc)
                VALUES ('f1-1985', '1.0.0', 'cafe', x'7B7D', '2026-01-01T00:00:00Z');
                INSERT INTO season (year, pack_id, pack_version, status)
                VALUES (1985, 'f1-1985', '1.0.0', 'active');
                """;
            seed.ExecuteNonQuery();
        }
        return CareerDatabase.Open(_path);
    }

    [Fact]
    public void RoundTripsRenameAndRebind()
    {
        using var db = OpenSeeded();

        StagingOverrideStore.Set(db, 1, "Skoal #10",
            new SeatStagingOverride { DriverName = "Mike K.", LiveryName = "Ferrari #11 C. Amon" });
        StagingOverrideStore.Set(db, 1, "Skoal #9", new SeatStagingOverride { DriverName = "P. Alliot" });

        var map = StagingOverrideStore.Read(db, 1);

        Assert.Equal(2, map.Count);
        Assert.Equal("Mike K.", map["Skoal #10"].DriverName);
        Assert.Equal("Ferrari #11 C. Amon", map["Skoal #10"].LiveryName);
        Assert.Equal("P. Alliot", map["Skoal #9"].DriverName);
        Assert.Null(map["Skoal #9"].LiveryName);
    }

    [Fact]
    public void SetUpserts_AndEmptyClears()
    {
        using var db = OpenSeeded();

        StagingOverrideStore.Set(db, 1, "Skoal #10", new SeatStagingOverride { DriverName = "First" });
        StagingOverrideStore.Set(db, 1, "Skoal #10", new SeatStagingOverride { DriverName = "Second" });
        Assert.Equal("Second", StagingOverrideStore.Read(db, 1)["Skoal #10"].DriverName);

        StagingOverrideStore.Set(db, 1, "Skoal #10", new SeatStagingOverride()); // empty → row deleted
        Assert.Empty(StagingOverrideStore.Read(db, 1));
    }

    [Fact]
    public void OverridesAreScopedPerSeason()
    {
        using var db = OpenSeeded();
        StagingOverrideStore.Set(db, 1, "Skoal #10", new SeatStagingOverride { DriverName = "Season 1" });

        Assert.Single(StagingOverrideStore.Read(db, 1));
        Assert.Empty(StagingOverrideStore.Read(db, 2)); // a different season sees none
    }
}
