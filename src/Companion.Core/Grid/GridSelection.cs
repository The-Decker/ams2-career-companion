namespace Companion.Core.Grid;

/// <summary>
/// The field the player chose for the season — which of the pack's seats (by livery) are on the
/// grid. This is the "choose the entire grid before a season" input: the sim folds exactly this
/// field, the staged custom-AI file carries exactly these drivers, and the briefing tells the
/// player to set AMS2's opponent count to match (AMS2 assembles the grid from a pool of liveries;
/// there is no writable grid file, so the field == the liveries you override + the opponent count).
///
/// A deterministic creation-time INPUT (like the character): journaled once, seeded into the season
/// start state, replayed byte-for-byte. <see cref="IncludesEverything"/> (null / empty include list)
/// is the identity: the full pack field, byte-identical to a career created before this feature.
/// </summary>
public sealed record GridSelection
{
    /// <summary>The whole-pack field — no filtering. The default and the byte-identical identity.</summary>
    public static readonly GridSelection Everything = new();

    /// <summary>The EXACT livery names the player included as the season field (case-sensitive), or
    /// null for "the whole pack". An entry seats only when its <c>ams2LiveryName</c> is in this set
    /// (the player's own seat is always kept regardless, so a chosen field can never bench the
    /// player).</summary>
    public IReadOnlyList<string>? IncludedLiveries { get; init; }

    /// <summary>True when no filtering applies — the full pack field (identity).</summary>
    public bool IncludesEverything => IncludedLiveries is null or { Count: 0 };

    /// <summary>Whether a given livery is on the chosen field (always true when
    /// <see cref="IncludesEverything"/>).</summary>
    public bool Includes(string liveryName) =>
        IncludesEverything ||
        IncludedLiveries!.Contains(liveryName, StringComparer.Ordinal);

    // STRUCTURAL equality over the livery list — the record default compares IncludedLiveries by
    // REFERENCE, which would make a re-derived selection (from the journal) unequal to the
    // deserialized one and fail the byte-identical replay gate at a season boundary (the exact bug
    // CharacterProfile hit). Compare contents ordinally, order-sensitive.
    public bool Equals(GridSelection? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (IncludesEverything && other.IncludesEverything)
            return true;
        if (IncludesEverything != other.IncludesEverything)
            return false;
        return IncludedLiveries!.SequenceEqual(other.IncludedLiveries!, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        if (IncludesEverything)
            return 0;
        var hash = new HashCode();
        foreach (var livery in IncludedLiveries!)
            hash.Add(livery, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
