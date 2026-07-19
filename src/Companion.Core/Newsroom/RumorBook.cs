namespace Companion.Core.Newsroom;

public enum RumorResolutionKind
{
    /// <summary>Still open, no later fact settles it yet.</summary>
    Open,
    /// <summary>A later confirmed event settled the rumour true; linked by key.</summary>
    Confirmed,
    /// <summary>Later facts settled the rumour false (life moved on without it).</summary>
    Denied,
}

/// <summary>
/// One rumour with its honest lifecycle. The ORIGINAL story is preserved untouched (its key
/// still renders with <see cref="EditorialStatus.Rumour"/>/<see cref="EditorialStatus.Reported"/>);
/// resolution NEVER rewrites it, it links the confirming/denying story instead
/// (docs/dev/newsroom-history-overhaul.md D6: a rumour never silently becomes factual).
/// </summary>
public sealed record RumorRecord
{
    /// <summary>The originating story's dedupe key (the rumour article itself).</summary>
    public required string RumorKey { get; init; }
    public required string Subject { get; init; }
    public required string Claim { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required RumorResolutionKind Resolution { get; init; }
    /// <summary>The dedupe key of the story that settled it; empty while open/denied-by-time.</summary>
    public string ResolvedByKey { get; init; } = "";
    public string ResolutionNote { get; init; } = "";
}

/// <summary>
/// Derives the rumour ledger from the event spine. Only fact-backed rumours exist: a
/// retirement WHISPER is the journaled foreshadow row; a driver-market LINK is a real
/// extended offer. Resolution reads strictly later facts, deterministic, display-only.
/// </summary>
public static class RumorBook
{
    public static IReadOnlyList<RumorRecord> Build(IReadOnlyList<NewsEvent> events)
    {
        var rumors = new List<RumorRecord>();
        var maxOrdinal = events.Count > 0 ? events.Max(e => e.SeasonOrdinal) : 0;

        foreach (var whisper in events.Where(e => e.Kind == NewsEventKind.RetirementConsidered))
        {
            // Confirmed by the same driver's retirement in this or the following season-end;
            // denied once a season AFTER the whisper's window has started without it.
            var confirmation = events.FirstOrDefault(e =>
                e.Kind == NewsEventKind.DriverRetired
                && string.Equals(e.SubjectId, whisper.SubjectId, StringComparison.Ordinal)
                && e.SeasonOrdinal >= whisper.SeasonOrdinal
                && e.SeasonOrdinal <= whisper.SeasonOrdinal + 1);

            var resolution = confirmation is not null ? RumorResolutionKind.Confirmed
                : maxOrdinal > whisper.SeasonOrdinal + 1 ? RumorResolutionKind.Denied
                : RumorResolutionKind.Open;

            rumors.Add(new RumorRecord
            {
                RumorKey = whisper.DedupeKey,
                Subject = whisper.SubjectName,
                Claim = $"{whisper.SubjectName} is said to be weighing retirement",
                SeasonOrdinal = whisper.SeasonOrdinal,
                Resolution = resolution,
                ResolvedByKey = confirmation?.DedupeKey ?? "",
                ResolutionNote = resolution switch
                {
                    RumorResolutionKind.Confirmed => "Confirmed: the retirement followed.",
                    RumorResolutionKind.Denied => "The seasons rolled on; the helmet stayed on.",
                    _ => "",
                },
            });
        }

        foreach (var offer in events.Where(e => e.Kind == NewsEventKind.OfferReceived))
        {
            // "Player linked with {team}": confirmed by a move to that team at the next
            // rollover, denied once the next season starts with a different outcome.
            var move = events.FirstOrDefault(e =>
                e.Kind == NewsEventKind.PlayerTeamChanged
                && e.SeasonOrdinal == offer.SeasonOrdinal + 1);
            var nextSeasonStarted = events.Any(e =>
                e.Kind == NewsEventKind.SeasonStarted && e.SeasonOrdinal == offer.SeasonOrdinal + 1);

            var confirmed = move is not null
                && string.Equals(move.SubjectTeamName, offer.SubjectName, StringComparison.Ordinal);
            var resolution = confirmed ? RumorResolutionKind.Confirmed
                : nextSeasonStarted ? RumorResolutionKind.Denied
                : RumorResolutionKind.Open;

            rumors.Add(new RumorRecord
            {
                RumorKey = offer.DedupeKey,
                Subject = offer.SubjectName,
                Claim = $"A move to {offer.SubjectName} is on the table",
                SeasonOrdinal = offer.SeasonOrdinal,
                Resolution = resolution,
                ResolvedByKey = confirmed ? move!.DedupeKey : "",
                ResolutionNote = resolution switch
                {
                    RumorResolutionKind.Confirmed => "Confirmed: the move happened.",
                    RumorResolutionKind.Denied => "The new season answered: no move.",
                    _ => "",
                },
            });
        }

        return rumors;
    }
}
