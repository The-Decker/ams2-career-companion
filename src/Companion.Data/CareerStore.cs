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
            $"Pinned pack {PackId} {Version} deserialized to null, the career file is damaged.");
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
                "This career file already has a career, one career per file.");

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
    /// throws, pinned packs are immutable, content changes require a version bump.</summary>
    public static string PinPack(
        CareerDatabase db,
        SeasonPack pack,
        string pinnedUtc,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(pack, CoreJson.Options);
        return PinPackEnvelope(
            db, pack.Manifest.PackId, pack.Manifest.Version, bytes, pinnedUtc, transaction);
    }

    /// <summary>Pins already-enveloped pack bytes verbatim. Creation uses this for its five-file
    /// envelopes, including future packs in a v2 campaign plan. The identity is immutable: the same
    /// id/version may be reused only when its byte hash is identical.</summary>
    public static string PinPackEnvelope(
        CareerDatabase db,
        string packId,
        string packVersion,
        byte[] bytes,
        string pinnedUtc,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packVersion);
        ArgumentNullException.ThrowIfNull(bytes);

        var embedded = PinnedPackEnvelope.LoadSeasonPack(bytes);
        if (!string.Equals(embedded.Manifest.PackId, packId, StringComparison.Ordinal) ||
            !string.Equals(embedded.Manifest.Version, packVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Pinned bytes contain {embedded.Manifest.PackId} {embedded.Manifest.Version}, not " +
                $"the requested key {packId} {packVersion}.",
                nameof(bytes));
        }

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using (var existing = db.Command(
                   "SELECT sha256 FROM pinned_pack WHERE pack_id = @id AND version = @version;",
                   transaction,
                   ("@id", packId),
                   ("@version", packVersion)))
        {
            if (existing.ExecuteScalar() is string storedSha)
            {
                if (string.Equals(storedSha, sha256, StringComparison.Ordinal))
                    return sha256;
                throw new InvalidOperationException(
                    $"Pack {packId} {packVersion} is already pinned with " +
                    "different content, pinned packs are immutable; bump the pack version instead.");
            }
        }

        db.Execute(
            """
            INSERT INTO pinned_pack (pack_id, version, sha256, pack_json, pinned_utc)
            VALUES (@id, @version, @sha, @json, @utc);
            """,
            transaction,
            ("@id", packId),
            ("@version", packVersion),
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
                $"(stored {storedSha}, actual {actualSha}), the career file is damaged.");

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
    public static long StartSeason(
        CareerDatabase db,
        int year,
        string packId,
        string packVersion,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        using var command = db.Command(
            """
            INSERT INTO season (year, pack_id, pack_version, status)
            VALUES (@year, @id, @version, @status);
            SELECT last_insert_rowid();
            """,
            transaction,
            ("@year", year),
            ("@id", packId),
            ("@version", packVersion),
            ("@status", SeasonStatus.Active));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// Era transition v1 (PLAN M6): starts the first season of the NEXT era pack from a
    /// <see cref="TransitionPlan"/>, atomically pins the new pack, creates the season row,
    /// writes the plan's stage-'start' states (rollover + transition carryover), and journals
    /// the era.transition header plus the plan's era.bridge / era.departed / era.economy
    /// events under the new season. Refuses plans carrying validation errors (the UI must
    /// surface them) and transitions from anything but the career's completed last season.
    /// Returns the new season id.
    /// </summary>
    public static long StartNextSeason(
        CareerDatabase db, Companion.Core.Career.TransitionPlan plan, SeasonPack toPack, string utc)
        => StartNextSeasonCore(db, plan, toPack, utc, pinPack: true);

    /// <summary>Starts a v2 plan entry from bytes already pinned at career creation. The expected
    /// hash is checked before the transition and the mutable pack directory is never consulted.</summary>
    public static long StartNextPinnedSeason(
        CareerDatabase db,
        Companion.Core.Career.TransitionPlan plan,
        string packVersion,
        string expectedSha256,
        string utc)
    {
        var pinned = ReadPinnedPack(db, plan.ToPackId, packVersion);
        if (!string.Equals(pinned.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Pinned pack {plan.ToPackId} {packVersion} does not match the campaign plan " +
                $"(plan {expectedSha256}, stored {pinned.Sha256}).");
        var toPack = PinnedPackEnvelope.LoadSeasonPack(pinned.PackJson);
        return StartNextSeasonCore(db, plan, toPack, utc, pinPack: false);
    }

    private static long StartNextSeasonCore(
        CareerDatabase db,
        Companion.Core.Career.TransitionPlan plan,
        SeasonPack toPack,
        string utc,
        bool pinPack)
    {
        if (plan.ValidationErrors.Count > 0)
            throw new InvalidOperationException(
                "The transition plan has validation errors, surface them to the user instead of " +
                "starting the season: " + string.Join(" | ", plan.ValidationErrors));
        if (!string.Equals(toPack.Manifest.PackId, plan.ToPackId, StringComparison.Ordinal) ||
            toPack.Season.Year != plan.ToYear)
            throw new ArgumentException(
                $"The supplied pack is {toPack.Manifest.PackId} ({toPack.Season.Year}) but the plan " +
                $"targets {plan.ToPackId} ({plan.ToYear}), build the plan against the pack you start.",
                nameof(toPack));

        var seasons = ReadSeasons(db);
        var previous = seasons.Count > 0 ? seasons[^1] : null;
        if (previous is null || previous.Year != plan.FromYear)
            throw new InvalidOperationException(
                $"The plan transitions from a {plan.FromYear} season but the career's latest season " +
                (previous is null ? "does not exist." : $"is {previous.Year}."));
        if (!string.Equals(previous.Status, SeasonStatus.Complete, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Season {previous.Id} ({previous.Year}) is still {previous.Status}, finish it " +
                "before transitioning into the next era.");

        using var transaction = db.Connection.BeginTransaction();
        if (pinPack)
            PinPack(db, toPack, utc, transaction);
        long seasonId = StartSeason(
            db, plan.ToYear, toPack.Manifest.PackId, toPack.Manifest.Version, transaction);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, plan.Player, transaction);
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart, plan.Drivers, transaction);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageStart, plan.Teams, transaction);
        JournalStore.AppendMany(
            db, seasonId, round: null, EraTransitionJournal.Rows(plan), utc, transaction);
        transaction.Commit();
        return seasonId;
    }

    /// <summary>
    /// Season CARRYOVER: starts the next calendar year on the SAME pack (same car/liveries) when
    /// no dedicated later-year pack exists yet. The next season reuses the already-pinned pack and
    /// takes its start states straight from <see cref="Companion.Core.Career.SeasonRollover"/>
    /// (season N's aged/retired/market-filled END states, player re-seated at the accepted team) —
    /// exactly the same-pack rollover replay re-derives (<c>VerifyRolloverStartStates</c>), so a
    /// carryover replays byte-identically. No era-transition journal rows: a rollover has none.
    /// Refuses anything but a same-pack step forward from the career's completed last season.
    /// </summary>
    public static long StartCarryoverSeason(
        CareerDatabase db,
        Companion.Core.Career.SeasonStartStates startStates,
        int year,
        string packId,
        string packVersion,
        string utc)
    {
        ArgumentNullException.ThrowIfNull(startStates);
        _ = utc; // a rollover writes no journal rows, so no timestamp is consumed (kept for call parity)

        var seasons = ReadSeasons(db);
        var previous = seasons.Count > 0 ? seasons[^1] : null;
        if (previous is null)
            throw new InvalidOperationException("A carryover needs a previous season to roll over from.");
        if (!string.Equals(previous.Status, SeasonStatus.Complete, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Season {previous.Id} ({previous.Year}) is still {previous.Status}, finish it before " +
                "carrying the career into the next year.");
        if (year <= previous.Year)
            throw new InvalidOperationException(
                $"A carryover must move forward in time: the last season is {previous.Year} and the " +
                $"carryover targets {year}.");
        if (!string.Equals(previous.PackId, packId, StringComparison.Ordinal) ||
            !string.Equals(previous.PackVersion, packVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "A carryover reuses the SAME pack as the previous season (that is what makes it a " +
                "rollover, not an era changeover).");

        using var transaction = db.Connection.BeginTransaction();
        // The pack is already pinned by the previous season (one pin serves every season that
        // reuses it), the carryover just adds a season row that references it.
        long seasonId = StartSeason(db, year, packId, packVersion, transaction);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, startStates.Player, transaction);
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart, startStates.Drivers, transaction);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageStart, startStates.Teams, transaction);
        transaction.Commit();
        return seasonId;
    }

    public static void CompleteSeason(
        CareerDatabase db, long seasonId, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null) =>
        db.Execute(
            "UPDATE season SET status = @status WHERE id = @id;",
            transaction,
            ("@status", SeasonStatus.Complete),
            ("@id", seasonId));

    /// <summary>All seasons in career order (season id order).</summary>
    public static IReadOnlyList<SeasonRecord> ReadSeasons(
        CareerDatabase db, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        using var command = db.Command(
            "SELECT id, year, pack_id, pack_version, status FROM season ORDER BY id;",
            transaction);
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
