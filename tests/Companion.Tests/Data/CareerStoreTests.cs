using System.Security.Cryptography;
using System.Text.Json;
using Companion.Core.Json;
using Companion.Data;

namespace Companion.Tests.Data;

public class CareerStoreTests
{
    private static object? Scalar(CareerDatabase db, string sql)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void CareerRoundTripsIncludingHighBitMasterSeeds()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);

        ulong seed = ulong.MaxValue - 12345; // exercises the signed-storage round-trip
        CareerStore.CreateCareer(db, "High Seed", seed, "0.5.0", "2026-07-02T00:00:00Z");

        var career = CareerStore.ReadCareer(db);
        Assert.Equal("High Seed", career.Name);
        Assert.Equal(seed, career.MasterSeed);
        Assert.Equal("2026-07-02T00:00:00Z", career.CreatedUtc);

        Assert.Throws<InvalidOperationException>(
            () => CareerStore.CreateCareer(db, "Second", 1, "0.5.0", "2026-07-02T00:00:00Z"));
    }

    [Fact]
    public void PinnedPackRoundTripsWithVerifiedHash()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var pack = DataCareerFixture.Pack();

        string sha = CareerStore.PinPack(db, pack, DataCareerFixture.Utc);

        var pinned = CareerStore.ReadPinnedPack(db, pack.Manifest.PackId, pack.Manifest.Version);
        Assert.Equal(sha, pinned.Sha256);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(pinned.PackJson)),
            pinned.Sha256);

        // The loaded pack re-serializes to the exact pinned bytes — the real byte contract.
        var loaded = pinned.Load();
        Assert.Equal(pinned.PackJson, JsonSerializer.SerializeToUtf8Bytes(loaded, CoreJson.Options));
        Assert.Equal(pack.Manifest.PackId, loaded.Manifest.PackId);
        Assert.Equal(pack.Manifest.Version, loaded.Manifest.Version);
        Assert.Equal(pack.Season.Rounds.Count, loaded.Season.Rounds.Count);
    }

    [Fact]
    public void RePinningTheIdenticalPackIsIdempotent()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var pack = DataCareerFixture.Pack();

        string first = CareerStore.PinPack(db, pack, DataCareerFixture.Utc);
        string second = CareerStore.PinPack(db, pack, "2026-07-03T00:00:00Z");
        Assert.Equal(first, second);

        Assert.Equal(1, Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM pinned_pack;")));
    }

    [Fact]
    public void RePinningDifferentContentUnderTheSameVersionThrows()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var pack = DataCareerFixture.Pack();
        CareerStore.PinPack(db, pack, DataCareerFixture.Utc);

        var mutated = pack with { Manifest = pack.Manifest with { Name = "Tampered Name" } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => CareerStore.PinPack(db, mutated, DataCareerFixture.Utc));
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TamperedPinnedBytesFailHashVerificationOnRead()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var pack = DataCareerFixture.Pack();
        CareerStore.PinPack(db, pack, DataCareerFixture.Utc);

        using (var tamper = db.Connection.CreateCommand())
        {
            tamper.CommandText = "UPDATE pinned_pack SET pack_json = x'7B7D' WHERE pack_id = @id;";
            tamper.Parameters.AddWithValue("@id", pack.Manifest.PackId);
            tamper.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidDataException>(
            () => CareerStore.ReadPinnedPack(db, pack.Manifest.PackId, pack.Manifest.Version));
        Assert.Contains("hash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeasonLifecycleStartsActiveAndCompletes()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var pack = DataCareerFixture.Pack();
        CareerStore.PinPack(db, pack, DataCareerFixture.Utc);

        long first = CareerStore.StartSeason(db, 1967, pack.Manifest.PackId, pack.Manifest.Version);
        long second = CareerStore.StartSeason(db, 1968, pack.Manifest.PackId, pack.Manifest.Version);
        Assert.True(second > first);

        CareerStore.CompleteSeason(db, first);

        var seasons = CareerStore.ReadSeasons(db);
        Assert.Equal(2, seasons.Count);
        Assert.Equal(SeasonStatus.Complete, seasons[0].Status);
        Assert.Equal(1967, seasons[0].Year);
        Assert.Equal(SeasonStatus.Active, seasons[1].Status);
    }
}
