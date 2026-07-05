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
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        var database = new CareerDatabase(connection);
        Migrations.Apply(connection);
        return database;
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

    public void Dispose() => Connection.Dispose();
}
