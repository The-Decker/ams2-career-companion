using Companion.Core.Career;

namespace Companion.Data;

/// <summary>A stored offer letter: the sim's terms plus the player's accepted flag.</summary>
public sealed record OfferRow
{
    public required PlayerOffer Terms { get; init; }

    public required bool Accepted { get; init; }
}

/// <summary>
/// Season-keyed driver/team/player state snapshots and offer letters (schema v2). Stage
/// 'start' rows are sim inputs; stage 'end' rows and offers are derived pipeline output —
/// <see cref="WipeDerived"/> deletes exactly the derived rows for re-simulation. Whole-set
/// upserts persist the caller's list order verbatim (ord column) because journal event order
/// follows it: reading states back MUST reproduce the original sim context ordering.
/// </summary>
public static class StateStore
{
    /// <summary>State entering the season: a sim INPUT (wizard choices, accepted offers).</summary>
    public const string StageStart = "start";

    /// <summary>State after the season-end pipeline: DERIVED, wiped + rebuilt by replay.</summary>
    public const string StageEnd = "end";

    // ---------- drivers ----------

    public static void UpsertDriverStates(
        CareerDatabase db, long seasonId, string stage, IReadOnlyList<DriverCareerState> drivers)
    {
        ValidateStage(stage);
        using var transaction = db.Connection.BeginTransaction();
        db.Execute(
            "DELETE FROM driver_state WHERE season_id = @season AND stage = @stage;",
            transaction, ("@season", seasonId), ("@stage", stage));
        for (int i = 0; i < drivers.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO driver_state (season_id, stage, driver_id, ord, state_json)
                VALUES (@season, @stage, @driver, @ord, @state);
                """,
                transaction,
                ("@season", seasonId),
                ("@stage", stage),
                ("@driver", drivers[i].DriverId),
                ("@ord", i),
                ("@state", DataJson.Serialize(drivers[i])));
        }
        transaction.Commit();
    }

    public static IReadOnlyList<DriverCareerState> ReadDriverStates(
        CareerDatabase db, long seasonId, string stage)
    {
        ValidateStage(stage);
        return ReadStates<DriverCareerState>(db, "driver_state", seasonId, stage);
    }

    // ---------- teams ----------

    public static void UpsertTeamStates(
        CareerDatabase db, long seasonId, string stage, IReadOnlyList<TeamCareerState> teams)
    {
        ValidateStage(stage);
        using var transaction = db.Connection.BeginTransaction();
        db.Execute(
            "DELETE FROM team_state WHERE season_id = @season AND stage = @stage;",
            transaction, ("@season", seasonId), ("@stage", stage));
        for (int i = 0; i < teams.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO team_state (season_id, stage, team_id, lineage_id, ord, state_json)
                VALUES (@season, @stage, @team, @lineage, @ord, @state);
                """,
                transaction,
                ("@season", seasonId),
                ("@stage", stage),
                ("@team", teams[i].TeamId),
                ("@lineage", teams[i].LineageId),
                ("@ord", i),
                ("@state", DataJson.Serialize(teams[i])));
        }
        transaction.Commit();
    }

    public static IReadOnlyList<TeamCareerState> ReadTeamStates(
        CareerDatabase db, long seasonId, string stage)
    {
        ValidateStage(stage);
        return ReadStates<TeamCareerState>(db, "team_state", seasonId, stage);
    }

    // ---------- player ----------

    public static void UpsertPlayerState(
        CareerDatabase db, long seasonId, string stage, PlayerCareerState player)
    {
        ValidateStage(stage);
        db.Execute(
            """
            INSERT INTO player_state (season_id, stage, state_json)
            VALUES (@season, @stage, @state)
            ON CONFLICT (season_id, stage) DO UPDATE SET state_json = excluded.state_json;
            """,
            null,
            ("@season", seasonId),
            ("@stage", stage),
            ("@state", DataJson.Serialize(player)));
    }

    public static PlayerCareerState? ReadPlayerState(CareerDatabase db, long seasonId, string stage)
    {
        ValidateStage(stage);
        using var command = db.Command(
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = @stage;",
            null, ("@season", seasonId), ("@stage", stage));
        return command.ExecuteScalar() is string json
            ? DataJson.Deserialize<PlayerCareerState>(json)
            : null;
    }

    // ---------- offers ----------

    /// <summary>Replaces the season's offer letters, preserving the sim's ranking order.
    /// Accepted flags reset — callers preserving a player's choice across re-simulation
    /// re-apply it afterwards (see ReplayService).</summary>
    public static void UpsertOffers(
        CareerDatabase db, long seasonId, IReadOnlyList<PlayerOffer> offers)
    {
        using var transaction = db.Connection.BeginTransaction();
        db.Execute(
            "DELETE FROM offer WHERE season_id = @season;",
            transaction, ("@season", seasonId));
        for (int i = 0; i < offers.Count; i++)
        {
            db.Execute(
                """
                INSERT INTO offer (season_id, team_id, ord, terms_json, accepted)
                VALUES (@season, @team, @ord, @terms, 0);
                """,
                transaction,
                ("@season", seasonId),
                ("@team", offers[i].TeamId),
                ("@ord", i),
                ("@terms", DataJson.Serialize(offers[i])));
        }
        transaction.Commit();
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
    public static void SetOfferAccepted(CareerDatabase db, long seasonId, string teamId)
    {
        using var transaction = db.Connection.BeginTransaction();
        db.Execute(
            "UPDATE offer SET accepted = 0 WHERE season_id = @season;",
            transaction, ("@season", seasonId));
        db.Execute(
            "UPDATE offer SET accepted = 1 WHERE season_id = @season AND team_id = @team;",
            transaction, ("@season", seasonId), ("@team", teamId));
        transaction.Commit();
    }

    // ---------- replay support ----------

    /// <summary>Deletes every DERIVED row — stage-'end' states and all offers — across all
    /// seasons. Raw results, pinned packs, the journal, and stage-'start' states survive:
    /// they are the inputs re-simulation refolds from.</summary>
    public static void WipeDerived(CareerDatabase db)
    {
        using var transaction = db.Connection.BeginTransaction();
        db.Execute("DELETE FROM offer;", transaction);
        db.Execute("DELETE FROM driver_state WHERE stage = @stage;", transaction, ("@stage", StageEnd));
        db.Execute("DELETE FROM team_state WHERE stage = @stage;", transaction, ("@stage", StageEnd));
        db.Execute("DELETE FROM player_state WHERE stage = @stage;", transaction, ("@stage", StageEnd));
        transaction.Commit();
    }

    // ---------- helpers ----------

    private static IReadOnlyList<T> ReadStates<T>(
        CareerDatabase db, string table, long seasonId, string stage)
    {
        using var command = db.Command(
            $"SELECT state_json FROM {table} WHERE season_id = @season AND stage = @stage ORDER BY ord;",
            null, ("@season", seasonId), ("@stage", stage));
        using var reader = command.ExecuteReader();
        var states = new List<T>();
        while (reader.Read())
            states.Add(DataJson.Deserialize<T>(reader.GetString(0)));
        return states;
    }

    private static void ValidateStage(string stage)
    {
        if (stage is not (StageStart or StageEnd))
            throw new ArgumentException($"Unknown state stage '{stage}' — use 'start' or 'end'.", nameof(stage));
    }
}
