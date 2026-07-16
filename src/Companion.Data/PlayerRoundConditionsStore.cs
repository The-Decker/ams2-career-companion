using System.Collections.ObjectModel;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>
/// Append-only persistence for the versioned pre-race facts that conditional player-car effects
/// consume. A declaration is immutable once that round has a raw result, and every stored row is
/// revalidated against the supplied runtime pack before it can be used.
/// </summary>
public static class PlayerRoundConditionsStore
{
    /// <summary>
    /// Reads and validates every declaration in a season. Duplicate round keys are corruption,
    /// even when both payloads are byte-for-byte identical: the input stream is one row per round.
    /// </summary>
    public static IReadOnlyDictionary<int, PlayerRoundConditionsInput> ReadSeason(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(pack);

        var declarations = new Dictionary<int, PlayerRoundConditionsInput>();
        foreach (JournalRow row in JournalStore.ReadSeason(db, seasonId, transaction))
        {
            if (!string.Equals(
                    row.Phase,
                    JournalPhases.PlayerRoundConditions,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(row.Entity, "player", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Player round-conditions journal row {row.Seq} has entity '{row.Entity}', expected 'player'.");
            }

            if (row.Round is not { } journalRound)
            {
                throw new InvalidDataException(
                    $"Player round-conditions journal row {row.Seq} has no round key.");
            }

            ValidateRequiredPayloadFields(row);
            PlayerRoundConditionsInput input =
                DataJson.Deserialize<PlayerRoundConditionsInput>(row.DeltaJson);
            PlayerRoundConditions.Validate(input, pack, journalRound);

            if (!declarations.TryAdd(journalRound, input))
            {
                throw new InvalidDataException(
                    $"Season {seasonId} has duplicate player round-conditions rows for round {journalRound}.");
            }
        }

        return new ReadOnlyDictionary<int, PlayerRoundConditionsInput>(declarations);
    }

    public static PlayerRoundConditionsInput? ReadRound(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        int round,
        SqliteTransaction? transaction = null)
    {
        IReadOnlyDictionary<int, PlayerRoundConditionsInput> declarations =
            ReadSeason(db, seasonId, pack, transaction);
        return declarations.TryGetValue(round, out PlayerRoundConditionsInput? input)
            ? input
            : null;
    }

    /// <summary>
    /// Declares one already-prepared input, unless the same canonical declaration already
    /// exists. The result-presence check, corruption scan, conflict check, and append share one
    /// transaction. An ambient transaction remains owned by its caller.
    /// </summary>
    public static PlayerRoundConditionsInput Declare(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        PlayerRoundConditionsInput input,
        string utc,
        SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(utc);

        PlayerRoundConditions.Validate(input, pack, input.Round);

        using var scope = TransactionScope.Enter(db, transaction);

        using (var resultCheck = db.Command(
                   "SELECT 1 FROM round_result_raw WHERE season_id = @season AND round = @round LIMIT 1;",
                   scope.Transaction,
                   ("@season", seasonId),
                   ("@round", input.Round)))
        {
            if (resultCheck.ExecuteScalar() is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot declare player round conditions for season {seasonId}, round {input.Round}: " +
                    "a raw result already exists.");
            }
        }

        IReadOnlyDictionary<int, PlayerRoundConditionsInput> existing =
            ReadSeason(db, seasonId, pack, scope.Transaction);
        if (existing.TryGetValue(input.Round, out PlayerRoundConditionsInput? current))
        {
            if (current == input)
            {
                scope.Complete();
                return current;
            }

            throw new InvalidOperationException(
                $"Player round conditions for season {seasonId}, round {input.Round} " +
                "were already declared with different facts.");
        }

        JournalStore.Append(
            db,
            seasonId,
            input.Round,
            new JournalEvent
            {
                Phase = JournalPhases.PlayerRoundConditions,
                Entity = "player",
                DeltaJson = DataJson.Serialize(input),
                Cause = "pre-race",
            },
            utc,
            scope.Transaction);

        scope.Complete();
        return input;
    }

    private static void ValidateRequiredPayloadFields(JournalRow row)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(row.DeltaJson);
        }
        catch (JsonException ex)
        {
            throw new JsonException(
                $"Player round-conditions journal row {row.Seq} is malformed JSON.", ex);
        }
        using (document)
        {
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"Player round-conditions journal row {row.Seq} is not a JSON object.");

        foreach (string name in new[]
                 {
                     "version", "progressionVersion", "round", "trackId", "isWet", "lengthBand",
                 })
        {
            if (!root.TryGetProperty(name, out _))
                throw new InvalidDataException(
                    $"Player round-conditions journal row {row.Seq} is missing required field '{name}'.");
        }
        }
    }
}
