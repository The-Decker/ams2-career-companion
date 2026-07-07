using Companion.Core.Grid;

namespace Companion.Data;

/// <summary>
/// Reads/writes the per-season grid-editor staging overrides (v4 <c>staging_override</c> table):
/// cosmetic per-seat driver-name and livery (skin) rebinds, keyed by the seat's original
/// <c>ams2LiveryName</c>, applied to the staged custom-AI file. NON-journaled and non-derived — the
/// deterministic fold/replay never reads this table and <c>WipeDerived</c> never clears it, so
/// re-simulation stays byte-identical whether or not a career carries edits.
/// </summary>
public static class StagingOverrideStore
{
    public static IReadOnlyDictionary<string, SeatStagingOverride> Read(CareerDatabase database, long seasonId)
    {
        var result = new Dictionary<string, SeatStagingOverride>(StringComparer.Ordinal);
        using var command = database.Connection.CreateCommand();
        command.CommandText =
            "SELECT livery_key, driver_name, livery_name FROM staging_override WHERE season_id = $s;";
        command.Parameters.AddWithValue("$s", seasonId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string key = reader.GetString(0);
            string? name = reader.IsDBNull(1) ? null : reader.GetString(1);
            string? livery = reader.IsDBNull(2) ? null : reader.GetString(2);
            result[key] = new SeatStagingOverride { DriverName = name, LiveryName = livery };
        }
        return result;
    }

    /// <summary>Upserts one seat's override (keyed by its original livery), or DELETES the row when
    /// the override is empty (both fields cleared) — so clearing an edit removes it entirely.</summary>
    public static void Set(CareerDatabase database, long seasonId, string liveryKey, SeatStagingOverride ov)
    {
        using var command = database.Connection.CreateCommand();
        if (ov.IsEmpty)
        {
            command.CommandText =
                "DELETE FROM staging_override WHERE season_id = $s AND livery_key = $k;";
            command.Parameters.AddWithValue("$s", seasonId);
            command.Parameters.AddWithValue("$k", liveryKey);
        }
        else
        {
            command.CommandText =
                """
                INSERT INTO staging_override (season_id, livery_key, driver_name, livery_name)
                VALUES ($s, $k, $n, $l)
                ON CONFLICT(season_id, livery_key) DO UPDATE SET driver_name = $n, livery_name = $l;
                """;
            command.Parameters.AddWithValue("$s", seasonId);
            command.Parameters.AddWithValue("$k", liveryKey);
            command.Parameters.AddWithValue("$n", (object?)ov.DriverName ?? DBNull.Value);
            command.Parameters.AddWithValue("$l", (object?)ov.LiveryName ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }
}
