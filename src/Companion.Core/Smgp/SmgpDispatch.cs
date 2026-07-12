namespace Companion.Core.Smgp;

/// <summary>
/// One "living world" news dispatch for the SMGP mode — a short in-world story that reacts to the player's
/// ACTUAL career (a win, a first, a promotion, a title, a rivalry earned or lost, a setback) or to the grid
/// around them (a rival's win streak, the A. Senna benchmark, the title race tightening, a standings move).
/// A pure DISPLAY-ONLY projection over the folded results (never a fold input → replay byte-identical). The
/// GUI binds this directly as a dispatch card / news ticker. The <see cref="SortSeason"/>/<see cref="SortRound"/>/
/// <see cref="SortSeq"/> keys order the feed chronologically; the session returns it newest-first.
/// </summary>
public sealed record SmgpDispatch
{
    /// <summary>When it happened, e.g. "Season 3 · Monaco" or "Season 3".</summary>
    public required string WhenLabel { get; init; }

    /// <summary>The dispatch's category (drives the GUI's accent / icon / filter).</summary>
    public required SmgpDispatchKind Kind { get; init; }

    /// <summary>The bold arcade headline.</summary>
    public required string Headline { get; init; }

    /// <summary>The in-world story body (one to three sentences).</summary>
    public required string Body { get; init; }

    /// <summary>Optional driver art key — a driver id for <c>portraits/&lt;id&gt;.jpg</c> (the story's
    /// subject, e.g. the named rival or the streaking driver), or empty. Lets the GUI show a face.</summary>
    public string DriverArtKey { get; init; } = "";

    /// <summary>Optional team art key — a team id for <c>smgp/teams/&lt;team&gt;.jpg</c>, or empty.</summary>
    public string TeamArtKey { get; init; } = "";

    /// <summary>Chronological sort key: the 1-based season ordinal this dispatch belongs to.</summary>
    public int SortSeason { get; init; }

    /// <summary>Chronological sort key within a season: the round number, with two sentinels — a
    /// season-START item (e.g. arrival, "SEASON n of 17") uses <see cref="SeasonStartRound"/> (sorts first),
    /// a season-END item (the digest, a title, the finale) uses <see cref="SeasonEndRound"/> (sorts last).</summary>
    public int SortRound { get; init; }

    /// <summary>Tiebreak within a (season, round): the emission order of same-round items.</summary>
    public int SortSeq { get; init; }

    /// <summary>The round sentinel for a season-START dispatch (sorts before every scored round).</summary>
    public const int SeasonStartRound = 0;

    /// <summary>The round sentinel for a season-END dispatch (sorts after every scored round).</summary>
    public const int SeasonEndRound = 9999;
}

/// <summary>The kind of story a <see cref="SmgpDispatch"/> tells — the GUI keys its accent/icon on this.</summary>
public enum SmgpDispatchKind
{
    /// <summary>A player career milestone — a first, a promotion, a title, a rivalry won, the finale.</summary>
    Milestone,

    /// <summary>A player race result — a win, a podium, a points finish, a solid midfield run.</summary>
    RaceResult,

    /// <summary>A player setback — a DNF, a demotion, a rivalry lost, a brush with the LEVEL D floor.</summary>
    Setback,

    /// <summary>An AI-world story about a rival or the benchmark — a win streak, A. Senna out front.</summary>
    RivalWatch,

    /// <summary>A championship story — the title race tightening, a new leader, a standings move.</summary>
    TitleRace,

    /// <summary>A season-boundary digest.</summary>
    SeasonDigest,
}
