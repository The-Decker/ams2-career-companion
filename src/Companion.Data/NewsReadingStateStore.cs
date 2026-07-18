using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>One story's user-relationship row: when it was read, whether it is bookmarked.</summary>
public sealed record NewsReadingState
{
    public string? ReadUtc { get; init; }
    public bool Bookmarked { get; init; }
    public string? BookmarkedUtc { get; init; }
    public bool IsRead => ReadUtc is { Length: > 0 };
}

/// <summary>
/// The news_reading_state table (schema v6): USER PREFERENCE persistence in the
/// staging_override category, never journaled, never a fold input, never wiped by
/// <see cref="StateStore.WipeDerived"/>. Keys are the stories' stable dedupe keys; articles
/// themselves are never stored. Utc strings are caller-supplied (the Data layer never reads
/// the machine clock).
/// </summary>
public static class NewsReadingStateStore
{
    public static IReadOnlyDictionary<string, NewsReadingState> ReadAll(
        CareerDatabase db, SqliteTransaction? tx = null)
    {
        var states = new Dictionary<string, NewsReadingState>(StringComparer.Ordinal);
        using var command = db.Command(
            "SELECT story_key, read_utc, bookmarked, bookmarked_utc FROM news_reading_state;", tx);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            states[reader.GetString(0)] = new NewsReadingState
            {
                ReadUtc = reader.IsDBNull(1) ? null : reader.GetString(1),
                Bookmarked = reader.GetInt64(2) != 0,
                BookmarkedUtc = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
        return states;
    }

    /// <summary>Marks a story read (idempotent, the FIRST read timestamp is kept).</summary>
    public static void MarkRead(CareerDatabase db, string storyKey, string utc, SqliteTransaction? tx = null)
    {
        using var scope = TransactionScope.Enter(db, tx);
        db.Execute(
            """
            INSERT INTO news_reading_state (story_key, read_utc) VALUES ($key, $utc)
            ON CONFLICT (story_key) DO UPDATE SET read_utc = COALESCE(read_utc, excluded.read_utc);
            """,
            scope.Transaction,
            ("$key", storyKey), ("$utc", utc));
        scope.Complete();
    }

    public static void SetBookmark(
        CareerDatabase db, string storyKey, bool bookmarked, string utc, SqliteTransaction? tx = null)
    {
        using var scope = TransactionScope.Enter(db, tx);
        db.Execute(
            """
            INSERT INTO news_reading_state (story_key, bookmarked, bookmarked_utc)
            VALUES ($key, $on, $utc)
            ON CONFLICT (story_key) DO UPDATE SET
                bookmarked = excluded.bookmarked,
                bookmarked_utc = CASE WHEN excluded.bookmarked = 1 THEN excluded.bookmarked_utc ELSE NULL END;
            """,
            scope.Transaction,
            ("$key", storyKey), ("$on", bookmarked ? 1 : 0), ("$utc", bookmarked ? utc : null));
        scope.Complete();
    }
}
