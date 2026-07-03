using System.Security.Cryptography;
using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Packs;

namespace Companion.Data;

public sealed record CareerRecord
{
    public required string Name { get; init; }

    public required string CreatedUtc { get; init; }

    /// <summary>The one master seed every named RNG stream derives from.</summary>
    public required ulong MasterSeed { get; init; }

    public required string AppVersion { get; init; }
}

public sealed record SeasonRecord
{
    public required long Id { get; init; }

    public required int Year { get; init; }

    public required string PackId { get; init; }

    public required string PackVersion { get; init; }

    public required string Status { get; init; }
}

/// <summary>Season lifecycle states. String values are part of the save format.</summary>
public static class SeasonStatus
{
    public const string Active = "active";

    /// <summary>The season-end pipeline has run; replay re-runs it for this season.</summary>
    public const string Complete = "complete";
}

/// <summary>A pack copy pinned into the career file: the exact bytes the career simulates
/// against, verified by sha256 on every read. The career never depends on the mutable Packs
/// folder after pinning.</summary>
public sealed record PinnedPackRecord
{
    public required string PackId { get; init; }

    public required string Version { get; init; }

    /// <summary>Lowercase hex sha256 of <see cref="PackJson"/>, recomputed and checked on read.</summary>
    public required string Sha256 { get; init; }

    public required byte[] PackJson { get; init; }

    public required string PinnedUtc { get; init; }

    public SeasonPack Load() =>
        JsonSerializer.Deserialize<SeasonPack>(PackJson, CoreJson.Options)
        ?? throw new InvalidDataException(
            $"Pinned pack {PackId} {Version} deserialized to null — the career file is damaged.");
}

/// <summary>Career identity, pack pinning, and season lifecycle. Thin explicit SQL; every
/// UTC timestamp comes from the caller so replay can pass a fixed clock.</summary>
public static class CareerStore
{
    public static void CreateCareer(
        CareerDatabase db, string name, ulong masterSeed, string appVersion, string createdUtc)
    {
        using var existing = db.Command("SELECT COUNT(*) FROM career;");
        if (Convert.ToInt32(existing.ExecuteScalar()) != 0)
            throw new InvalidOperationException(
                "This career file already has a career — one career per file.");

        db.Execute(
            """
            INSERT INTO career (id, name, created_utc, master_seed, app_version)
            VALUES (1, @name, @created, @seed, @app);
            """,
            null,
            ("@name", name),
            ("@created", createdUtc),
            ("@seed", unchecked((long)masterSeed)),
            ("@app", appVersion));
    }

    public static CareerRecord ReadCareer(CareerDatabase db)
    {
        using var command = db.Command(
            "SELECT name, created_utc, master_seed, app_version FROM career WHERE id = 1;");
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("This career file has no career row yet.");

        return new CareerRecord
        {
            Name = reader.GetString(0),
            CreatedUtc = reader.GetString(1),
            MasterSeed = unchecked((ulong)reader.GetInt64(2)),
            AppVersion = reader.GetString(3),
        };
    }

    /// <summary>Pins the pack's canonical serialization (CoreJson) into the career, keyed by
    /// (packId, version), and returns its sha256 (lowercase hex). Pinning the identical pack
    /// again is a no-op; pinning DIFFERENT content under an already-pinned (packId, version)
    /// throws — pinned packs are immutable, content changes require a version bump.</summary>
    public static string PinPack(CareerDatabase db, SeasonPack pack, string pinnedUtc)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(pack, CoreJson.Options);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using (var existing = db.Command(
                   "SELECT sha256 FROM pinned_pack WHERE pack_id = @id AND version = @version;",
                   null,
                   ("@id", pack.Manifest.PackId),
                   ("@version", pack.Manifest.Version)))
        {
            if (existing.ExecuteScalar() is string storedSha)
            {
                if (string.Equals(storedSha, sha256, StringComparison.Ordinal))
                    return sha256;
                throw new InvalidOperationException(
                    $"Pack {pack.Manifest.PackId} {pack.Manifest.Version} is already pinned with " +
                    "different content — pinned packs are immutable; bump the pack version instead.");
            }
        }

        db.Execute(
            """
            INSERT INTO pinned_pack (pack_id, version, sha256, pack_json, pinned_utc)
            VALUES (@id, @version, @sha, @json, @utc);
            """,
            null,
            ("@id", pack.Manifest.PackId),
            ("@version", pack.Manifest.Version),
            ("@sha", sha256),
            ("@json", bytes),
            ("@utc", pinnedUtc));
        return sha256;
    }

    /// <summary>Reads a pinned pack and verifies its bytes against the stored sha256 —
    /// a hash mismatch means the career file was corrupted or tampered with.</summary>
    public static PinnedPackRecord ReadPinnedPack(CareerDatabase db, string packId, string version)
    {
        using var command = db.Command(
            """
            SELECT sha256, pack_json, pinned_utc FROM pinned_pack
            WHERE pack_id = @id AND version = @version;
            """,
            null,
            ("@id", packId),
            ("@version", version));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Pack {packId} {version} is not pinned in this career.");

        string storedSha = reader.GetString(0);
        byte[] bytes = reader.GetFieldValue<byte[]>(1);
        string pinnedUtc = reader.GetString(2);

        string actualSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualSha, storedSha, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Pinned pack {packId} {version} failed hash verification " +
                $"(stored {storedSha}, actual {actualSha}) — the career file is damaged.");

        return new PinnedPackRecord
        {
            PackId = packId,
            Version = version,
            Sha256 = storedSha,
            PackJson = bytes,
            PinnedUtc = pinnedUtc,
        };
    }

    /// <summary>Starts a season against an already-pinned pack; returns the new season id.</summary>
    public static long StartSeason(CareerDatabase db, int year, string packId, string packVersion)
    {
        using var command = db.Command(
            """
            INSERT INTO season (year, pack_id, pack_version, status)
            VALUES (@year, @id, @version, @status);
            SELECT last_insert_rowid();
            """,
            null,
            ("@year", year),
            ("@id", packId),
            ("@version", packVersion),
            ("@status", SeasonStatus.Active));
        return (long)command.ExecuteScalar()!;
    }

    public static void CompleteSeason(CareerDatabase db, long seasonId) =>
        db.Execute(
            "UPDATE season SET status = @status WHERE id = @id;",
            null,
            ("@status", SeasonStatus.Complete),
            ("@id", seasonId));

    /// <summary>All seasons in career order (season id order).</summary>
    public static IReadOnlyList<SeasonRecord> ReadSeasons(CareerDatabase db)
    {
        using var command = db.Command(
            "SELECT id, year, pack_id, pack_version, status FROM season ORDER BY id;");
        using var reader = command.ExecuteReader();
        var seasons = new List<SeasonRecord>();
        while (reader.Read())
        {
            seasons.Add(new SeasonRecord
            {
                Id = reader.GetInt64(0),
                Year = reader.GetInt32(1),
                PackId = reader.GetString(2),
                PackVersion = reader.GetString(3),
                Status = reader.GetString(4),
            });
        }
        return seasons;
    }
}
