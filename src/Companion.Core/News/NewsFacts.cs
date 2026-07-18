namespace Companion.Core.News;

/// <summary>
/// The round facts a news article is generated FROM, a read-only projection over the folded
/// journal + standings snapshots (never new persistence). Every field is optional: a template
/// only fills the slots it names, and the article builder degrades a missing fact to a neutral
/// phrase, so a thin <c>race.result</c> row still yields a complete body.
///
/// This record is a pure DATA carrier: producing it from the journal is the caller's job
/// (<c>CareerSessionService.ReadFeed</c>), keeping <c>Companion.Core.News</c> free of I/O.
/// </summary>
public sealed record NewsFacts
{
    /// <summary>Journal phase of the source row (e.g. <c>race.result</c>), with
    /// <see cref="Cause"/> it keys the article bank, exactly like the headline bank.</summary>
    public required string Phase { get; init; }

    /// <summary>Journal cause (win / podium / points / overperformed / underperformed /
    /// dnf-mechanical / dnf-driver-error / midfield / …).</summary>
    public required string Cause { get; init; }

    /// <summary>Season year, selects the era (unless <see cref="PreferredEra"/> overrides) and
    /// fills <c>{year}</c>.</summary>
    public required int Year { get; init; }

    /// <summary>An explicit era key that overrides the year→era resolution, the SMGP replica mode
    /// routes to its own fictional-world corpus ("smgp") regardless of the 1990 career year, so the
    /// SEGA universe never borrows the historical 1990s outlet. Null = resolve by year as normal.</summary>
    public string? PreferredEra { get; init; }

    /// <summary>Calendar round number (fills <c>{round}</c>).</summary>
    public int Round { get; init; }

    /// <summary>The event's display name (fills <c>{race}</c>).</summary>
    public string RaceName { get; init; } = "";

    /// <summary>The player's display name (fills <c>{player}</c>).</summary>
    public string PlayerName { get; init; } = "";

    /// <summary>The player's team display name (fills <c>{team}</c>).</summary>
    public string TeamName { get; init; } = "";

    /// <summary>The player's classified finish; null on a DNF (fills <c>{position}</c> as an
    /// ordinal, else "out").</summary>
    public int? PlayerFinish { get; init; }

    /// <summary>The player's pre-race expected finish from the seat-strength model (fills
    /// <c>{expected}</c> as an ordinal).</summary>
    public int? ExpectedFinish { get; init; }

    /// <summary>True when the player retired.</summary>
    public bool Dnf { get; init; }

    /// <summary>Race winner's display name, when known from the round provenance / grid
    /// (fills <c>{winner}</c>).</summary>
    public string? WinnerName { get; init; }

    /// <summary>How many cars started the race (fills <c>{fieldSize}</c>).</summary>
    public int? FieldSize { get; init; }

    /// <summary>The player's championship position AFTER this round (fills
    /// <c>{champPosition}</c> as an ordinal).</summary>
    public int? ChampionshipPosition { get; init; }

    /// <summary>Championship-position movement across this round (positive = climbed,
    /// negative = dropped, 0 = held). Null before any prior snapshot exists.</summary>
    public int? ChampionshipDelta { get; init; }

    /// <summary>The season's championship leader after this round (fills
    /// <c>{champLeader}</c>); may equal the player.</summary>
    public string? ChampionshipLeaderName { get; init; }

    /// <summary>Whether the player leads the championship after this round.</summary>
    public bool PlayerLeadsChampionship { get; init; }
}
