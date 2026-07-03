using Companion.Core.Career;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>Journal phases the Data layer itself emits (the sim's phases live in
/// <see cref="JournalPhases"/>). String values are part of the save format — never rename.</summary>
public static class DataJournalPhases
{
    /// <summary>Per-round standings rows, refolded from raw results after every import and
    /// regenerated verbatim by replay.</summary>
    public const string RoundStandings = "round.standings";

    /// <summary>Import provenance (re-import audit rows). Everything under this prefix is
    /// bookkeeping about the OUTSIDE world, not derived sim state, so replay excludes it
    /// from the byte-compare.</summary>
    public const string AuditPrefix = "import.";

    /// <summary>A raw result re-import for an already-imported round.</summary>
    public const string ImportResult = "import.result";
}

/// <summary>One persisted journal row: a <see cref="JournalEvent"/> plus the storage-assigned
/// seq and the caller-supplied utc/season/round.</summary>
public sealed record JournalRow
{
    public required long Seq { get; init; }

    public required string Utc { get; init; }

    public long? SeasonId { get; init; }

    public int? Round { get; init; }

    public required string Phase { get; init; }

    public required string Entity { get; init; }

    public required string DeltaJson { get; init; }

    public required string Cause { get; init; }

    public JournalEvent ToEvent() => new()
    {
        Phase = Phase,
        Entity = Entity,
        DeltaJson = DeltaJson,
        Cause = Cause,
    };
}

/// <summary>Append-only journal persistence. seq is storage-assigned (AUTOINCREMENT,
/// monotonic); utc ALWAYS comes from the caller so replay can pass a fixed clock — the
/// Data layer never reads the machine clock.</summary>
public static class JournalStore
{
    public static long Append(
        CareerDatabase db,
        long? seasonId,
        int? round,
        JournalEvent journalEvent,
        string utc,
        SqliteTransaction? transaction = null)
    {
        using var command = db.Command(
            """
            INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause)
            VALUES (@utc, @season, @round, @phase, @entity, @delta, @cause);
            SELECT last_insert_rowid();
            """,
            transaction,
            ("@utc", utc),
            ("@season", seasonId),
            ("@round", round),
            ("@phase", journalEvent.Phase),
            ("@entity", journalEvent.Entity),
            ("@delta", journalEvent.DeltaJson),
            ("@cause", journalEvent.Cause));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Appends a batch atomically, in enumeration order (contiguous seq range).</summary>
    public static void AppendMany(
        CareerDatabase db, long? seasonId, int? round, IEnumerable<JournalEvent> events, string utc)
    {
        using var transaction = db.Connection.BeginTransaction();
        foreach (var journalEvent in events)
            Append(db, seasonId, round, journalEvent, utc, transaction);
        transaction.Commit();
    }

    public static IReadOnlyList<JournalRow> ReadSeason(CareerDatabase db, long seasonId)
    {
        using var command = db.Command(
            """
            SELECT seq, utc, season_id, round, phase, entity, delta_json, cause
            FROM journal WHERE season_id = @season ORDER BY seq;
            """,
            null,
            ("@season", seasonId));
        return ReadRows(command);
    }

    public static IReadOnlyList<JournalRow> ReadAll(CareerDatabase db)
    {
        using var command = db.Command(
            """
            SELECT seq, utc, season_id, round, phase, entity, delta_json, cause
            FROM journal ORDER BY seq;
            """);
        return ReadRows(command);
    }

    private static List<JournalRow> ReadRows(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<JournalRow>();
        while (reader.Read())
        {
            rows.Add(new JournalRow
            {
                Seq = reader.GetInt64(0),
                Utc = reader.GetString(1),
                SeasonId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                Round = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Phase = reader.GetString(4),
                Entity = reader.GetString(5),
                DeltaJson = reader.GetString(6),
                Cause = reader.GetString(7),
            });
        }
        return rows;
    }
}
