using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>Thin command plumbing for the explicit-SQL repositories, no ORM, just less
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

/// <summary>Owned-or-ambient transaction scope for multi-statement store methods: with an
/// ambient (caller) transaction the scope is a pass-through, the caller owns commit and
/// rollback; without one the scope owns a fresh transaction, <see cref="Complete"/> commits
/// it, and disposing without completing rolls it back.</summary>
internal readonly struct TransactionScope : IDisposable
{
    private readonly SqliteTransaction? _owned;

    public SqliteTransaction Transaction { get; }

    private TransactionScope(SqliteTransaction transaction, SqliteTransaction? owned)
    {
        Transaction = transaction;
        _owned = owned;
    }

    public static TransactionScope Enter(CareerDatabase db, SqliteTransaction? ambient)
    {
        if (ambient is not null)
            return new TransactionScope(ambient, owned: null);
        var owned = db.Connection.BeginTransaction();
        return new TransactionScope(owned, owned);
    }

    public void Complete() => _owned?.Commit();

    public void Dispose() => _owned?.Dispose();
}
