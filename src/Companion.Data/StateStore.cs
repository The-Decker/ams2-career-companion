using Companion.Core.Career;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>A stored offer letter: the sim's terms plus the player's accepted flag.</summary>
public sealed record OfferRow
{
    public required PlayerOffer Terms { get; init; }

    public required bool Accepted { get; init; }
}

/// <summary>The post-round player state persisted by the unified fold (schema v3,
/// round_player_state): the folded <see cref="PlayerCareerState"/> plus the Opponent Skill
/// slider recommendation for the NEXT round (0 = no recommendation yet, uncalibrated).</summary>
public sealed record RoundPlayerState
{
    public required PlayerCareerState Player { get; init; }

    public required int RecommendedSlider { get; init; }
}

/// <summary>
/// Season-keyed driver/team/player state snapshots, per-round folded player states, and offer
/// letters (schema v2/v3). Stage 'start' rows are sim inputs; stage 'end' rows, offers, and
/// round_player_state rows are derived pipeline/fold output, <see cref="WipeDerived"/>
/// deletes exactly the derived rows for re-simulation. Whole-set upserts persist the caller's
/// list order verbatim (ord column) because journal event order follows it: reading states
/// back MUST reproduce the original sim context ordering.
///
/// Every write (and the reads the fold needs mid-transaction) takes an optional transaction:
/// callers composing atomic units, FoldRound, Resimulate, pass theirs; standalone callers
/// omit it and each method is atomic on its own.
/// </summary>
public static class StateStore
{
    /// <summary>State entering the season: a sim INPUT (wizard choices, accepted offers).</summary>
    public const string StageStart = "start";

    /// <summary>State after the season-end pipeline: DERIVED, wiped + rebuilt by replay.</summary>
    public const string StageEnd = "end";

    // ---------- drivers ----------

    public static void UpsertDriverStates(
        CareerDatabase db,
        long seasonId,
        string stage,
        IReadOnlyList<DriverCareerState> drivers,
        SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        using var scope = TransactionScope.Enter(db, transaction);
        db.Execute(
            "DELETE FROM driver_state WHERE season_id = @season AND stage = @stage;",
            scope.Transaction, ("@season", seasonId), ("@stage", stage));
        for (int i = 0; i < drivers.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO driver_state (season_id, stage, driver_id, ord, state_json)
                VALUES (@season, @stage, @driver, @ord, @state);
                """,
                scope.Transaction,
                ("@season", seasonId),
                ("@stage", stage),
                ("@driver", drivers[i].DriverId),
                ("@ord", i),
                ("@state", DataJson.Serialize(drivers[i])));
        }
        scope.Complete();
    }

    public static IReadOnlyList<DriverCareerState> ReadDriverStates(
        CareerDatabase db, long seasonId, string stage, SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        return ReadStates<DriverCareerState>(db, "driver_state", seasonId, stage, transaction);
    }

    // ---------- teams ----------

    public static void UpsertTeamStates(
        CareerDatabase db,
        long seasonId,
        string stage,
        IReadOnlyList<TeamCareerState> teams,
        SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        using var scope = TransactionScope.Enter(db, transaction);
        db.Execute(
            "DELETE FROM team_state WHERE season_id = @season AND stage = @stage;",
            scope.Transaction, ("@season", seasonId), ("@stage", stage));
        for (int i = 0; i < teams.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO team_state (season_id, stage, team_id, lineage_id, ord, state_json)
                VALUES (@season, @stage, @team, @lineage, @ord, @state);
                """,
                scope.Transaction,
                ("@season", seasonId),
                ("@stage", stage),
                ("@team", teams[i].TeamId),
                ("@lineage", teams[i].LineageId),
                ("@ord", i),
                ("@state", DataJson.Serialize(teams[i])));
        }
        scope.Complete();
    }

    public static IReadOnlyList<TeamCareerState> ReadTeamStates(
        CareerDatabase db, long seasonId, string stage, SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        return ReadStates<TeamCareerState>(db, "team_state", seasonId, stage, transaction);
    }

    // ---------- player ----------

    public static void UpsertPlayerState(
        CareerDatabase db,
        long seasonId,
        string stage,
        PlayerCareerState player,
        SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        db.Execute(
            """
            INSERT INTO player_state (season_id, stage, state_json)
            VALUES (@season, @stage, @state)
            ON CONFLICT (season_id, stage) DO UPDATE SET state_json = excluded.state_json;
            """,
            transaction,
            ("@season", seasonId),
            ("@stage", stage),
            ("@state", DataJson.Serialize(player)));
    }

    public static PlayerCareerState? ReadPlayerState(
        CareerDatabase db, long seasonId, string stage, SqliteTransaction? transaction = null)
    {
        ValidateStage(stage);
        using var command = db.Command(
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = @stage;",
            transaction, ("@season", seasonId), ("@stage", stage));
        return command.ExecuteScalar() is string json
            ? DataJson.Deserialize<PlayerCareerState>(json)
            : null;
    }

    // ---------- per-round folded player state (schema v3, derived) ----------

    /// <summary>Persists the fold's post-round player state. STRICT insert: a duplicate
    /// (season, round) means a round was folded twice, a bug, never silently replaced.</summary>
    public static void InsertRoundPlayerState(
        CareerDatabase db,
        long seasonId,
        int round,
        RoundPlayerState state,
        SqliteTransaction? transaction = null)
    {
        db.Execute(
            """
            INSERT INTO round_player_state (season_id, round, state_json)
            VALUES (@season, @round, @state);
            """,
            transaction,
            ("@season", seasonId),
            ("@round", round),
            ("@state", DataJson.Serialize(state)));
    }

    /// <summary>Replace an already-folded round's player state, the promotion screen's forward
    /// resolution (3c-2) re-persists the round it belongs to after the deferred seat swap resolves.
    /// The re-fold path re-derives this exact state, so replay stays byte-identical.</summary>
    public static void UpdateRoundPlayerState(
        CareerDatabase db,
        long seasonId,
        int round,
        RoundPlayerState state,
        SqliteTransaction? transaction = null)
    {
        db.Execute(
            """
            UPDATE round_player_state SET state_json = @state
            WHERE season_id = @season AND round = @round;
            """,
            transaction,
            ("@season", seasonId),
            ("@round", round),
            ("@state", DataJson.Serialize(state)));
    }

    public static RoundPlayerState? ReadRoundPlayerState(
        CareerDatabase db, long seasonId, int round, SqliteTransaction? transaction = null)
    {
        using var command = db.Command(
            "SELECT state_json FROM round_player_state WHERE season_id = @season AND round = @round;",
            transaction, ("@season", seasonId), ("@round", round));
        return command.ExecuteScalar() is string json
            ? DataJson.Deserialize<RoundPlayerState>(json)
            : null;
    }

    /// <summary>Every folded round state of the season in round order, the home header's
    /// reputation/OPI trend reads this.</summary>
    public static IReadOnlyList<(int Round, RoundPlayerState State)> ReadRoundPlayerStates(
        CareerDatabase db, long seasonId)
    {
        using var command = db.Command(
            "SELECT round, state_json FROM round_player_state WHERE season_id = @season ORDER BY round;",
            null, ("@season", seasonId));
        using var reader = command.ExecuteReader();
        var states = new List<(int, RoundPlayerState)>();
        while (reader.Read())
            states.Add((reader.GetInt32(0), DataJson.Deserialize<RoundPlayerState>(reader.GetString(1))));
        return states;
    }

    // ---------- offers ----------

    /// <summary>Replaces the season's offer letters, preserving the sim's ranking order.
    /// Accepted flags reset, callers preserving a player's choice across re-simulation
    /// re-apply it afterwards (see ReplayService).</summary>
    public static void UpsertOffers(
        CareerDatabase db,
        long seasonId,
        IReadOnlyList<PlayerOffer> offers,
        SqliteTransaction? transaction = null)
    {
        using var scope = TransactionScope.Enter(db, transaction);
        db.Execute(
            "DELETE FROM offer WHERE season_id = @season;",
            scope.Transaction, ("@season", seasonId));
        for (int i = 0; i < offers.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO offer (season_id, team_id, ord, terms_json, accepted)
                VALUES (@season, @team, @ord, @terms, 0);
                """,
                scope.Transaction,
                ("@season", seasonId),
                ("@team", offers[i].TeamId),
                ("@ord", i),
                ("@terms", DataJson.Serialize(offers[i])));
        }
        scope.Complete();
    }

    public static IReadOnlyList<OfferRow> ReadOffers(CareerDatabase db, long seasonId)
    {
        using var command = db.Command(
            "SELECT terms_json, accepted FROM offer WHERE season_id = @season ORDER BY ord;",
            null, ("@season", seasonId));
        using var reader = command.ExecuteReader();
        var offers = new List<OfferRow>();
        while (reader.Read())
        {
            offers.Add(new OfferRow
            {
                Terms = DataJson.Deserialize<PlayerOffer>(reader.GetString(0)),
                Accepted = reader.GetInt64(1) != 0,
            });
        }
        return offers;
    }

    /// <summary>Marks one offer accepted (clearing any previous acceptance in the season) —
    /// at most one accepted offer per season.</summary>
    public static void SetOfferAccepted(
        CareerDatabase db, long seasonId, string teamId, SqliteTransaction? transaction = null)
    {
        using var scope = TransactionScope.Enter(db, transaction);
        db.Execute(
            "UPDATE offer SET accepted = 0 WHERE season_id = @season;",
            scope.Transaction, ("@season", seasonId));
        db.Execute(
            "UPDATE offer SET accepted = 1 WHERE season_id = @season AND team_id = @team;",
            scope.Transaction, ("@season", seasonId), ("@team", teamId));
        scope.Complete();
    }

    // ---------- replay support ----------

    /// <summary>Deletes every DERIVED row, stage-'end' states, all offers, and all
    /// round_player_state rows, across all seasons. Raw results, pinned packs, the journal,
    /// and stage-'start' states survive: they are the inputs re-simulation refolds from.</summary>
    public static void WipeDerived(CareerDatabase db, SqliteTransaction? transaction = null)
    {
        using var scope = TransactionScope.Enter(db, transaction);
        db.Execute("DELETE FROM offer;", scope.Transaction);
        db.Execute("DELETE FROM round_player_state;", scope.Transaction);
        db.Execute("DELETE FROM driver_state WHERE stage = @stage;", scope.Transaction, ("@stage", StageEnd));
        db.Execute("DELETE FROM team_state WHERE stage = @stage;", scope.Transaction, ("@stage", StageEnd));
        db.Execute("DELETE FROM player_state WHERE stage = @stage;", scope.Transaction, ("@stage", StageEnd));
        scope.Complete();
    }

    // ---------- helpers ----------

    private static IReadOnlyList<T> ReadStates<T>(
        CareerDatabase db, string table, long seasonId, string stage, SqliteTransaction? transaction)
    {
        using var command = db.Command(
            $"SELECT state_json FROM {table} WHERE season_id = @season AND stage = @stage ORDER BY ord;",
            transaction, ("@season", seasonId), ("@stage", stage));
        using var reader = command.ExecuteReader();
        var states = new List<T>();
        while (reader.Read())
            states.Add(DataJson.Deserialize<T>(reader.GetString(0)));
        return states;
    }

    private static void ValidateStage(string stage)
    {
        if (stage is not (StageStart or StageEnd))
            throw new ArgumentException($"Unknown state stage '{stage}', use 'start' or 'end'.", nameof(stage));
    }
}
