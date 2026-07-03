using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>Thin command plumbing for the explicit-SQL repositories — no ORM, just less
/// parameter boilerplate. Null argument values are bound as SQL NULL.</summary>
internal static class SqliteExtensions
{
    public static SqliteCommand Command(
        this CareerDatabase db,
        string sql,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] args)
    {
        var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static void Execute(
        this CareerDatabase db,
        string sql,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] args)
    {
        using var command = db.Command(sql, transaction, args);
        command.ExecuteNonQuery();
    }
}
