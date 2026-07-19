using Companion.Core.Newsroom;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP universe's own divergence layer (newsroom-history overhaul D9): the career's actual
/// venue winners compared against the almanac's remembered rulers (<see cref="SmgpRaceLore.Champion"/>,
/// "who the world remembers ruling here"). Emits <see cref="NewsEventKind.SmgpCanonDiverged"/> /
/// <see cref="NewsEventKind.SmgpCanonHeld"/> events that carry <c>SmgpFiction</c> provenance, the
/// SEGA canon is fiction and must never read as verified history, and the career universe never
/// silently merges into it. A pure projection over shaped seasons + the display-only almanac:
/// no I/O, no RNG, never a fold input.
/// </summary>
public static class SmgpCanonDivergence
{
    public static IReadOnlyList<NewsEvent> Compare(
        IReadOnlyList<NewsroomSeason> seasons,
        SmgpWhatReallyHappened almanac)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(almanac);

        var events = new List<NewsEvent>();
        foreach (var season in seasons)
        {
            foreach (var round in season.Rounds)
            {
                if (round.WinnerId.Length == 0 || round.Venue.Length == 0)
                {
                    continue;
                }
                if (almanac.ForVenue(round.Venue) is not { Champion.Length: > 0 } lore)
                {
                    continue;
                }

                string canonName = CanonDriverName(lore.Champion);
                if (canonName.Length == 0)
                {
                    continue;
                }

                bool held = string.Equals(round.WinnerName, canonName, StringComparison.OrdinalIgnoreCase);
                events.Add(new NewsEvent
                {
                    Kind = held ? NewsEventKind.SmgpCanonHeld : NewsEventKind.SmgpCanonDiverged,
                    SeasonOrdinal = season.Ordinal,
                    SeasonYear = season.Year,
                    Round = round.Round,
                    SubjectId = round.WinnerId,
                    SubjectName = round.WinnerName,
                    SubjectTeamId = round.WinnerTeamId,
                    SubjectTeamName = round.WinnerTeamName,
                    VenueName = round.Venue,
                    Facts = new NewsEventFacts
                    {
                        // The canon's remembered ruler rides the rival token so templates can
                        // speak both names: "canon remembers {rival}; this career filed {winner}".
                        RivalName = canonName,
                        WinnerName = round.WinnerName,
                        WinnerTeamName = round.WinnerTeamName,
                        IsWet = round.IsWet == true,
                    },
                });
            }
        }
        return events;
    }

    /// <summary>The driver's display name out of an almanac champion line ("A. Senna · Madonna"
    /// → "A. Senna"); tolerates a plain name with no team suffix.</summary>
    private static string CanonDriverName(string champion)
    {
        int separator = champion.IndexOf('·');
        string name = separator >= 0 ? champion[..separator] : champion;
        return name.Trim();
    }
}
