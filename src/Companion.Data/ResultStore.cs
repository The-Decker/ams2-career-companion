using System.Text;
using Companion.Core.Career;
using Companion.Core.Scoring;

namespace Companion.Data;

public sealed record ResultImport
{
    /// <summary>True when a result for this (season, round) already existed and was replaced.</summary>
    public required bool ReImported { get; init; }

    /// <summary>True when a re-import's payload differed from the stored bytes.</summary>
    public required bool PayloadChanged { get; init; }
}

public sealed record StoredRoundResult
{
    public required int Round { get; init; }

    public required string PayloadJson { get; init; }

    public required string EnteredUtc { get; init; }

    public required string Source { get; init; }

    public RoundResult ToRoundResult() => DataJson.Deserialize<RoundResult>(PayloadJson);
}

/// <summary>
/// Verbatim raw result payloads — the replay contract's ground truth. Import is idempotent
/// on (season, round): a re-import replaces the stored payload (same bytes or corrected ones)
/// and appends an audit journal row (<see cref="DataJournalPhases.ImportResult"/>) recording
/// that it happened. Raw results are never touched by re-simulation.
/// </summary>
public static class ResultStore
{
    public static ResultImport Append(
        CareerDatabase db,
        long seasonId,
        int round,
        string payloadJson,
        string enteredUtc,
        string source = "manual")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);
        byte[] payload = Encoding.UTF8.GetBytes(payloadJson);

        using var transaction = db.Connection.BeginTransaction();

        byte[]? existing;
        using (var select = db.Command(
                   "SELECT payload_json FROM round_result_raw WHERE season_id = @season AND round = @round;",
                   transaction,
                   ("@season", seasonId),
                   ("@round", round)))
        {
            existing = select.ExecuteScalar() as byte[];
        }

        if (existing is null)
        {
            db.Execute(
                """
                INSERT INTO round_result_raw (season_id, round, entered_utc, source, payload_json)
                VALUES (@season, @round, @utc, @source, @payload);
                """,
                transaction,
                ("@season", seasonId),
                ("@round", round),
                ("@utc", enteredUtc),
                ("@source", source),
                ("@payload", payload));
            transaction.Commit();
            return new ResultImport { ReImported = false, PayloadChanged = false };
        }

        bool changed = !existing.AsSpan().SequenceEqual(payload);
        db.Execute(
            """
            UPDATE round_result_raw
            SET payload_json = @payload, entered_utc = @utc, source = @source
            WHERE season_id = @season AND round = @round;
            """,
            transaction,
            ("@season", seasonId),
            ("@round", round),
            ("@utc", enteredUtc),
            ("@source", source),
            ("@payload", payload));

        JournalStore.Append(
            db, seasonId, round,
            new JournalEvent
            {
                Phase = DataJournalPhases.ImportResult,
                Entity = "race",
                DeltaJson = DataJson.Serialize(new { round, source, changed }),
                Cause = "re-import",
            },
            enteredUtc, transaction);

        transaction.Commit();
        return new ResultImport { ReImported = true, PayloadChanged = changed };
    }

    /// <summary>The season's stored raw results in round order.</summary>
    public static IReadOnlyList<StoredRoundResult> ReadSeasonResults(CareerDatabase db, long seasonId)
    {
        using var command = db.Command(
            """
            SELECT round, payload_json, entered_utc, source
            FROM round_result_raw WHERE season_id = @season ORDER BY round;
            """,
            null,
            ("@season", seasonId));
        using var reader = command.ExecuteReader();
        var results = new List<StoredRoundResult>();
        while (reader.Read())
        {
            results.Add(new StoredRoundResult
            {
                Round = reader.GetInt32(0),
                PayloadJson = Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(1)),
                EnteredUtc = reader.GetString(2),
                Source = reader.GetString(3),
            });
        }
        return results;
    }
}
