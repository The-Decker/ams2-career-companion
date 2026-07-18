using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>
/// One SQLite file per career (*.ams2career): relational career state, crash-safe (WAL),
/// versioned migrations. Raw result payloads are archived verbatim so standings can be
/// recomputed after engine fixes, and the append-only journal is the news feed's and the
/// re-simulation contract's source of truth.
/// </summary>
public sealed class CareerDatabase : IDisposable
{
    public SqliteConnection Connection { get; }

    private CareerDatabase(SqliteConnection connection)
    {
        Connection = connection;
    }

    public static CareerDatabase Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString());
        try
        {
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                pragma.ExecuteNonQuery();
            }

            var database = new CareerDatabase(connection);

            // Safety net before the schema chain runs: a genuine old-format file (version 1..N-1,
            // never a freshly created empty DB) gets a one-time consistent sibling snapshot
            // (`<name>.pre-v<N>.bak`) via VACUUM INTO, so even a future destructive migration —
            // or a power loss mid-chain, always leaves a restorable pre-upgrade copy.
            int preVersion = database.SchemaVersion;
            if (preVersion > 0 && preVersion < Migrations.CurrentVersion)
            {
                string backupPath = $"{path}.pre-v{preVersion}.bak";
                if (!File.Exists(backupPath))
                {
                    using var backup = connection.CreateCommand();
                    backup.CommandText = "VACUUM INTO @target;";
                    backup.Parameters.AddWithValue("@target", backupPath);
                    backup.ExecuteNonQuery();
                }
            }

            Migrations.Apply(connection);
            return database;
        }
        catch
        {
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            throw;
        }
    }

    public int SchemaVersion
    {
        get
        {
            using var command = Connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>Dispose returns the connection to Microsoft.Data.Sqlite's pool, which keeps the
    /// native file handle open, on Windows that blocks File.Delete/Move on the .ams2career (and
    /// holds -wal/-shm) until the pool prunes. Closing a career must actually release the file
    /// (the Start gallery's "Delete career file…" depends on it), so clear this connection's pool
    /// after disposing. Same discipline as the test infrastructure's TempDb.</summary>
    public void Dispose()
    {
        Connection.Dispose();
        SqliteConnection.ClearPool(Connection);
    }
}
