using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>
/// Forward-only schema migrations keyed on PRAGMA user_version. Each script runs in a
/// transaction; a career file created by an older app version upgrades in place, and a
/// newer file than the app understands is refused loudly.
/// </summary>
public static class Migrations
{
    /// <summary>Ordered migration scripts; index + 1 is the resulting schema version.</summary>
    private static readonly string[] Scripts =
    [
        // v1 — career skeleton: identity, pinned packs, seasons, raw results, journal.
        """
        CREATE TABLE career (
            id           INTEGER PRIMARY KEY CHECK (id = 1),
            name         TEXT NOT NULL,
            created_utc  TEXT NOT NULL,
            master_seed  INTEGER NOT NULL,
            app_version  TEXT NOT NULL
        );

        -- Season packs are copied and hashed into the career at season start (immutable,
        -- pinned): the career never depends on the mutable Packs folder afterwards.
        CREATE TABLE pinned_pack (
            pack_id     TEXT NOT NULL,
            version     TEXT NOT NULL,
            sha256      TEXT NOT NULL,
            pack_json   BLOB NOT NULL,
            pinned_utc  TEXT NOT NULL,
            PRIMARY KEY (pack_id, version)
        );

        CREATE TABLE season (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            year       INTEGER NOT NULL,
            pack_id    TEXT NOT NULL,
            pack_version TEXT NOT NULL,
            status     TEXT NOT NULL DEFAULT 'active',
            FOREIGN KEY (pack_id, pack_version) REFERENCES pinned_pack (pack_id, version)
        );

        -- The verbatim result payload as entered/captured, BEFORE any scoring: standings are
        -- always recomputable from these after engine or rules-data fixes.
        CREATE TABLE round_result_raw (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            season_id    INTEGER NOT NULL REFERENCES season (id),
            round        INTEGER NOT NULL,
            entered_utc  TEXT NOT NULL,
            source       TEXT NOT NULL DEFAULT 'manual',
            payload_json BLOB NOT NULL,
            UNIQUE (season_id, round)
        );

        -- Append-only. Powers the news feed, the "why?" inspector, and byte-identical
        -- re-simulation; rows are never updated or deleted.
        CREATE TABLE journal (
            seq        INTEGER PRIMARY KEY AUTOINCREMENT,
            utc        TEXT NOT NULL,
            season_id  INTEGER REFERENCES season (id),
            round      INTEGER,
            phase      TEXT NOT NULL,
            entity     TEXT NOT NULL,
            delta_json TEXT NOT NULL,
            cause      TEXT NOT NULL
        );
        """,
    ];

    public static int CurrentVersion => Scripts.Length;

    public static void Apply(SqliteConnection connection)
    {
        int version = GetUserVersion(connection);

        if (version > CurrentVersion)
            throw new InvalidOperationException(
                $"Career file schema v{version} is newer than this app understands (v{CurrentVersion}) — " +
                "update the app instead of opening the file.");

        for (int next = version; next < CurrentVersion; next++)
        {
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Scripts[next];
                command.ExecuteNonQuery();
            }
            using (var bump = connection.CreateCommand())
            {
                bump.Transaction = transaction;
                bump.CommandText = $"PRAGMA user_version = {next + 1};";
                bump.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
