using System.Text;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Scoring;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

public sealed record ResultImport
{
    /// <summary>True when a result for this (season, round) already existed and was replaced.</summary>
    public required bool ReImported { get; init; }

    /// <summary>True when a re-import's payload differed from the stored bytes.</summary>
    public required bool PayloadChanged { get; init; }
}

/// <summary>
/// The versioned round_result_raw payload (docs/dev/m5-fix-integration.md, "unified fold"
/// step 1): the ResultDraft-mapped raw classification plus the round context that is
/// otherwise unre-derivable — the Opponent Skill slider the player actually raced at, the
/// player's DNF cause, and (v3, Increment 2) the round's qualifying order. Grid, teammate
/// finish, and expected finish are re-derived from pack + seed + round, never stored.
/// Version-1 payloads (a bare RoundResult) read with defaults: slider unknown (fold substitutes
/// the last recommendation), DNF cause unknown (fold substitutes the no-blame default). Version-2
/// payloads read with <see cref="QualifyingOrder"/> null (no qualifying) — so every pre-weekend
/// save parses unchanged.
/// </summary>
public sealed record RoundResultEnvelope
{
    public const int CurrentVersion = 6;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The SMGP replica mode's rival declaration for this round (v6): who the player
    /// named (or was force-challenged by) and, when this round's battle triggered a seat-swap
    /// offer, the player's answer. Raw player-choice INPUTS the sim cannot re-derive, so they
    /// are stored; the battle OUTCOME itself derives from <see cref="Result"/>. Null = no rival
    /// this round (every pre-v6 save, every non-smgp career, every declined prompt) — the fold
    /// then runs no battle, so those rounds replay byte-identically. (M3, careerStyle "smgp".)</summary>
    public SmgpRivalCall? SmgpRival { get; init; }

    /// <summary>Whether the round was run in the WET (character depth: weather-conditional perks).
    /// A raw INPUT the sim cannot re-derive, so it is stored. Null = unknown/legacy (every pre-v4
    /// save) — the fold then evaluates neither wetRound nor dryRound, so weather perks stay dormant
    /// and old careers replay byte-identically. true = wet, false = dry (explicitly captured).</summary>
    public bool? IsWet { get; init; }

    /// <summary>The Setup Gamble (called shot) the player committed to before the race — the
    /// finishing position they bet on (1-based). A raw player-choice INPUT the sim cannot re-derive,
    /// so it is stored. Null = no bet (every pre-v5 save and every round the player did not gamble)
    /// — the fold then resolves no call, so those rounds replay byte-identically. (Setup Gamble, 4b.)</summary>
    public int? CalledShot { get; init; }

    /// <summary>The raw classification as imported (the engine's round-result shape).</summary>
    public required RoundResult Result { get; init; }

    /// <summary>The in-game Opponent Skill slider the round was driven at (asked on the
    /// result screen, prefilled with the last recommendation). Null = unknown (legacy).</summary>
    public double? SliderUsed { get; init; }

    /// <summary>Why the player's race ended early, when it did. Null = unknown or n/a;
    /// the fold defaults a retired player to <see cref="DnfCause.Mechanical"/> (no blame)
    /// and a disqualified player to <see cref="DnfCause.DriverError"/>.</summary>
    public DnfCause? PlayerDnfCause { get; init; }

    /// <summary>The round's qualifying order — pack driver ids, pole first — when the pack's
    /// weekend ran a qualifying session (Increment 2). Null = no qualifying (every pre-weekend
    /// save and every single-race pack). It is a raw INPUT the sim cannot re-derive, so it is
    /// stored; later slices consume it for the grid (grid-from-qualy) and the qualifying pace
    /// anchor. It is deliberately NOT part of <see cref="RoundResult"/>, so it never reaches the
    /// standings engine — scoring and the f1db oracle are untouched.</summary>
    public IReadOnlyList<string>? QualifyingOrder { get; init; }

    /// <summary>Parses a stored payload, accepting both the current envelope shape and the
    /// version-1 bare-RoundResult shape (read with defaults).</summary>
    public static RoundResultEnvelope Parse(string payloadJson)
    {
        using (var probe = JsonDocument.Parse(payloadJson))
        {
            if (probe.RootElement.ValueKind == JsonValueKind.Object &&
                probe.RootElement.TryGetProperty("result", out _))
                return DataJson.Deserialize<RoundResultEnvelope>(payloadJson);
        }

        return new RoundResultEnvelope
        {
            Version = 1,
            Result = DataJson.Deserialize<RoundResult>(payloadJson),
        };
    }
}

/// <summary>The SMGP rival declaration stored on a round's envelope (see
/// <see cref="RoundResultEnvelope.SmgpRival"/>).</summary>
public sealed record SmgpRivalCall
{
    /// <summary>The named rival's pack driver id.</summary>
    public required string RivalDriverId { get; init; }

    /// <summary>True when the challenge was FORCED on the player (a rival's own challenge, or
    /// the Ceara title-defense rounds) rather than freely picked.</summary>
    public bool Forced { get; init; }

    /// <summary>The player's answer to a seat-swap offer TRIGGERED by this round's battle
    /// (the two-wins rule): true = accepted (the swap applies from the next round), false =
    /// declined. Null = no offer arose this round.</summary>
    public bool? SeatSwapAccepted { get; init; }
}

/// <summary>The DeltaJson of an <c>smgp.swap</c> journal input row (3c-2): the player's post-race
/// promotion-screen decision for a two-phase career. Provenance-excluded from the byte-compare and
/// read back at re-fold to resolve the round's pending offer. Carries the rival + offered car for
/// the Why? inspector; the resolution itself reads only <see cref="Accepted"/> (the pending offer's
/// seat rides on the folded state).</summary>
public sealed record SmgpSwapInput
{
    /// <summary>The rival whose seat was offered (pack driver id) — provenance only.</summary>
    public string? Rival { get; init; }

    /// <summary>The offered car (ams2LiveryName) — provenance only.</summary>
    public string? OfferedSeat { get; init; }

    /// <summary>True = the player ACCEPTED the promotion (move into the offered car); false =
    /// declined (keep the current seat).</summary>
    public required bool Accepted { get; init; }
}

public sealed record StoredRoundResult
{
    public required int Round { get; init; }

    public required string PayloadJson { get; init; }

    public required string EnteredUtc { get; init; }

    public required string Source { get; init; }

    public RoundResultEnvelope ToEnvelope() => RoundResultEnvelope.Parse(PayloadJson);

    public RoundResult ToRoundResult() => ToEnvelope().Result;
}

/// <summary>
/// Verbatim raw result payloads — the replay contract's ground truth. Import is idempotent
/// on (season, round): a re-import replaces the stored payload (same bytes or corrected ones)
/// and appends an audit journal row (<see cref="DataJournalPhases.ImportResult"/>) recording
/// that it happened. Raw results are never touched by re-simulation.
///
/// NEW rounds on the live path go through <see cref="ReplayService.ImportAndFoldRound"/> so
/// the raw store and the fold are one atomic unit; calling <see cref="Append"/> directly is
/// for corrected re-imports (followed by <see cref="ReplayService.Resimulate"/>) and tooling.
/// </summary>
public static class ResultStore
{
    public static ResultImport Append(
        CareerDatabase db,
        long seasonId,
        int round,
        string payloadJson,
        string enteredUtc,
        string source = "manual",
        SqliteTransaction? transaction = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);
        byte[] payload = Encoding.UTF8.GetBytes(payloadJson);

        using var scope = TransactionScope.Enter(db, transaction);

        byte[]? existing;
        using (var select = db.Command(
                   "SELECT payload_json FROM round_result_raw WHERE season_id = @season AND round = @round;",
                   scope.Transaction,
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
                scope.Transaction,
                ("@season", seasonId),
                ("@round", round),
                ("@utc", enteredUtc),
                ("@source", source),
                ("@payload", payload));
            scope.Complete();
            return new ResultImport { ReImported = false, PayloadChanged = false };
        }

        bool changed = !existing.AsSpan().SequenceEqual(payload);
        db.Execute(
            """
            UPDATE round_result_raw
            SET payload_json = @payload, entered_utc = @utc, source = @source
            WHERE season_id = @season AND round = @round;
            """,
            scope.Transaction,
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
            enteredUtc, scope.Transaction);

        scope.Complete();
        return new ResultImport { ReImported = true, PayloadChanged = changed };
    }

    /// <summary>The season's stored raw results in round order.</summary>
    public static IReadOnlyList<StoredRoundResult> ReadSeasonResults(
        CareerDatabase db, long seasonId, SqliteTransaction? transaction = null)
    {
        using var command = db.Command(
            """
            SELECT round, payload_json, entered_utc, source
            FROM round_result_raw WHERE season_id = @season ORDER BY round;
            """,
            transaction,
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
