namespace Companion.Data;

/// <summary>
/// A cheap monotonic fingerprint of the stored career state that display projections (the
/// newsroom event spine, history feeds) are computed over. Any fold, auto-sim, season rollover,
/// or resimulation appends/rewrites journal rows (MAX seq moves), stores results, or adds
/// seasons — so fingerprint equality proves a cached projection's inputs are unchanged.
/// </summary>
public readonly record struct ProjectionFingerprint(long JournalSeq, long Results, long Seasons)
{
    public static ProjectionFingerprint Read(CareerDatabase db)
    {
        using var command = db.Command(
            """
            SELECT (SELECT COALESCE(MAX(seq), 0) FROM journal),
                   (SELECT COUNT(*) FROM round_result_raw),
                   (SELECT COUNT(*) FROM season);
            """,
            null);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ProjectionFingerprint(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : default;
    }
}
