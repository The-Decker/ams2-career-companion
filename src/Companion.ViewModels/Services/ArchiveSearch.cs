using Companion.Core.Newsroom;

namespace Companion.ViewModels.Services;

public enum ArchiveSearchScope
{
    All,
    News,
    History,
}

public enum ArchiveSearchProvenance
{
    All,
    RealHistory,
    CareerUniverse,
}

/// <summary>One search hit, with WHY it matched surfaced to the user.</summary>
public sealed record ArchiveSearchResult
{
    /// <summary>"article" | "thread" | "driver" | "team" | "circuit" | "subject" | "era" | "timeline".</summary>
    public required string Kind { get; init; }
    /// <summary>Cross-navigation key (article dedupe key, thread key, entity name/id...).</summary>
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    /// <summary>The field the query matched ("headline", "body", "name", "alias"...).</summary>
    public required string MatchedOn { get; init; }
    public required ContentProvenance Provenance { get; init; }
    public int Year { get; init; }
    internal int Rank { get; init; }
}

/// <summary>
/// The unified News+History search: an in-memory index built once per refresh over the derived
/// projections (nothing is stored, so there is nothing to SQL-index; this is the appropriate
/// indexed strategy per newsroom-history-overhaul.md D11). Matching is case-insensitive with
/// title-prefix > title > secondary-field ranking; callers debounce keystrokes.
/// </summary>
public sealed class ArchiveSearchIndex
{
    private sealed record Entry(
        string Kind,
        string Key,
        string Title,
        string Subtitle,
        ContentProvenance Provenance,
        int Year,
        IReadOnlyList<(string Field, string Text)> Haystacks);

    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    public static ArchiveSearchIndex Build(
        IReadOnlyList<NewsroomArticle> articles,
        IReadOnlyList<StoryThread> threads,
        HistoryArchiveIndex history)
    {
        var index = new ArchiveSearchIndex();

        foreach (var a in articles)
        {
            index._entries.Add(new Entry(
                "article", a.Key, a.Headline,
                $"{(a.DeskName.Length > 0 ? a.DeskName + " · " : "")}{a.SeasonYear}" +
                    (a.Round is > 0 and < CareerNewsEvents.SeasonEndRound ? $" · round {a.Round}" : ""),
                a.Provenance, a.SeasonYear,
                [
                    ("headline", a.Headline),
                    ("summary", a.Summary),
                    ("body", a.Body),
                    ("venue", a.VenueName),
                    ("subject", a.SubjectName),
                    ("team", a.TeamName),
                    ("category", a.Category.ToString()),
                ]));
        }

        foreach (var t in threads)
        {
            index._entries.Add(new Entry(
                "thread", t.Key, t.Title, $"{t.Type} · {t.State}",
                ContentProvenance.CareerUniverse, 0,
                [
                    ("title", t.Title),
                    .. t.Entries.Select(e => ("development", e.Summary)),
                ]));
        }

        foreach (var d in history.Drivers)
        {
            index._entries.Add(new Entry(
                "driver", d.Name, d.Name,
                $"{d.FirstYear}-{d.LastYear} · {d.Wins} wins · {d.Starts} starts",
                ContentProvenance.VerifiedHistorical, d.FirstYear,
                [
                    ("name", d.Name),
                    .. d.Stints.Select(s => ("team", s.Team)),
                ]));
        }

        foreach (var t in history.Teams)
        {
            index._entries.Add(new Entry(
                "team", t.Canonical, t.Canonical,
                $"{t.FirstYear}-{t.LastYear} · {t.Wins} wins",
                ContentProvenance.VerifiedHistorical, t.FirstYear,
                [
                    ("name", t.Canonical),
                    .. t.Aliases.Select(a => ("alias", a)),
                ]));
        }

        foreach (var c in history.Circuits)
        {
            index._entries.Add(new Entry(
                "circuit", c.LayoutId, c.Name,
                $"{c.Place} · {c.Editions.Count} editions",
                ContentProvenance.VerifiedHistorical,
                c.Editions.Count > 0 ? c.Editions[0].Year : 0,
                [
                    ("name", c.Name),
                    ("place", c.Place),
                    .. c.Editions.Select(e => ("race", e.RaceName)),
                ]));
        }

        foreach (var s in history.Reference.Subjects)
        {
            index._entries.Add(new Entry(
                "subject", $"subject:{s.Id}", s.Title, s.Category,
                ContentProvenance.VerifiedHistorical, s.FromYear,
                [
                    ("title", s.Title),
                    ("summary", s.Summary),
                    .. s.Body.Select(p => ("body", p)),
                ]));
        }

        foreach (var e in history.Reference.Eras)
        {
            index._entries.Add(new Entry(
                "era", $"era:{e.Key}", e.Name, $"{e.FromYear}-{e.ToYear}",
                ContentProvenance.VerifiedHistorical, e.FromYear,
                [("name", e.Name), ("overview", e.Overview)]));
        }

        foreach (var t in history.Timeline)
        {
            index._entries.Add(new Entry(
                "timeline", t.RelatedKey.Length > 0 ? t.RelatedKey : $"timeline:{t.Year}:{t.Title}",
                t.Title, $"{t.Year} · {t.Category}",
                ContentProvenance.VerifiedHistorical, t.Year,
                [("title", t.Title), ("summary", t.Summary)]));
        }

        return index;
    }

    public IReadOnlyList<ArchiveSearchResult> Search(
        string query,
        ArchiveSearchScope scope = ArchiveSearchScope.All,
        ArchiveSearchProvenance provenance = ArchiveSearchProvenance.All,
        int limit = 50)
    {
        var needle = query.Trim();
        if (needle.Length < 2)
        {
            return [];
        }

        var results = new List<ArchiveSearchResult>();
        foreach (var entry in _entries)
        {
            if (scope == ArchiveSearchScope.News && entry.Kind is not ("article" or "thread")
                || scope == ArchiveSearchScope.History && entry.Kind is "article" or "thread")
            {
                continue;
            }
            if (provenance == ArchiveSearchProvenance.RealHistory
                    && entry.Provenance != ContentProvenance.VerifiedHistorical
                || provenance == ArchiveSearchProvenance.CareerUniverse
                    && entry.Provenance == ContentProvenance.VerifiedHistorical)
            {
                continue;
            }

            var titlePrefix = entry.Title.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            var titleHit = titlePrefix || entry.Title.Contains(needle, StringComparison.OrdinalIgnoreCase);
            string? matchedField = titleHit ? "title" : null;
            if (matchedField is null)
            {
                foreach (var (field, text) in entry.Haystacks)
                {
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedField = field;
                        break;
                    }
                }
            }
            if (matchedField is null)
            {
                continue;
            }

            results.Add(new ArchiveSearchResult
            {
                Kind = entry.Kind,
                Key = entry.Key,
                Title = entry.Title,
                Subtitle = entry.Subtitle,
                MatchedOn = matchedField,
                Provenance = entry.Provenance,
                Year = entry.Year,
                Rank = titlePrefix ? 3 : titleHit ? 2 : 1,
            });
        }

        return results
            .OrderByDescending(r => r.Rank)
            .ThenByDescending(r => r.Year)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }
}
