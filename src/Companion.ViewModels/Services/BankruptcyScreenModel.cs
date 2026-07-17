using Companion.Data;

namespace Companion.ViewModels.Services;

/// <summary>
/// The Dynasty bankruptcy game-over screen's complete, DB-free projection — the economy's
/// <see cref="DeathScreenModel"/> (docs/dev/dynasty-tycoon-economy.md §7). Built lazily from the
/// intact DB the first time the shell asks (bankruptcy never deletes the career file) and
/// memoised: a bankrupt team takes no more rounds, so it never changes. The GUI lane renders it
/// with no session/DB access; when restore slots exist (a mortality-Normal career's autosaves)
/// the screen offers the same rewind escape the Normal death screen does.
/// </summary>
public sealed record BankruptcyScreenModel
{
    /// <summary>The owner-driver's display name (the character's chosen name when one exists).</summary>
    public required string DriverName { get; init; }

    /// <summary>The team that folded, as displayed on the season's grid.</summary>
    public required string TeamName { get; init; }

    /// <summary>The season year the money ran out.</summary>
    public required int Year { get; init; }

    /// <summary>The round whose settlement crossed the line, when the journal records it.</summary>
    public int? Round { get; init; }

    /// <summary>The final balance, rendered exactly (a negative rational, e.g. "-26400").</summary>
    public required string FinalBalance { get; init; }

    /// <summary>Consecutive deficit rounds at the collapse.</summary>
    public required int DeficitRounds { get; init; }

    /// <summary>The grace the rules allowed (for the screen's honest "how it ended" line).</summary>
    public required int GraceRounds { get; init; }

    /// <summary>The whole-career records book — the legacy the team leaves behind.</summary>
    public required CareerRecordsBook Record { get; init; }

    /// <summary>Season-by-season career cards (the campaign timeline's own data).</summary>
    public required IReadOnlyList<CareerSeasonCard> Seasons { get; init; }

    /// <summary>Restore points (mortality-Normal careers only; empty otherwise).</summary>
    public required IReadOnlyList<SaveSlotInfo> RestoreSlots { get; init; }

    /// <summary>The screen offers the save-restore escape hatch.</summary>
    public bool CanRestore => RestoreSlots.Count > 0;
}
