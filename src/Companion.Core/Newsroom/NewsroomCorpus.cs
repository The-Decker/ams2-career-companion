using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Determinism;

namespace Companion.Core.Newsroom;

/// <summary>One article template: a headline + deck + named body sections voiced for an event
/// kind, optionally narrowed to a SITUATION (guards over <see cref="NewsEventFacts"/>), desks,
/// and eras. <see cref="Id"/> is the rendezvous-selection key: stable ids keep existing careers'
/// picks steady as the library grows (docs/dev/newsroom-history-overhaul.md D5).</summary>
public sealed record NewsroomTemplate
{
    public required string Id { get; init; }
    /// <summary>NewsEventKind, camelCase (e.g. "raceWon").</summary>
    public required string Event { get; init; }
    /// <summary>Situation guards; ALL must pass. Known keys: isFirstEver, clinchedTitle,
    /// tookChampionshipLead, lostChampionshipLead, isWet, isFinalRound, isSeasonOpener,
    /// rivalInvolved (booleans, matched exactly); minStreak, minDrought, minUpset,
    /// maxUpset (ints); milestoneCounter (string equality).</summary>
    public IReadOnlyDictionary<string, JsonElement> When { get; init; } =
        new Dictionary<string, JsonElement>();
    /// <summary>Eligible desk ids; empty = any desk.</summary>
    public IReadOnlyList<string> Desks { get; init; } = [];
    /// <summary>Eligible era keys; empty = any era.</summary>
    public IReadOnlyList<string> Eras { get; init; } = [];
    public required string Headline { get; init; }
    public string Deck { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>Named body sections; rendered in the canonical order
    /// (<see cref="NewsroomCorpus.SectionOrder"/>), skipping absent names.</summary>
    public IReadOnlyDictionary<string, string> Sections { get; init; } =
        new Dictionary<string, string>();
}

public sealed record NewsroomEra
{
    public required string Key { get; init; }
    public required int FromYear { get; init; }
    public required int ToYear { get; init; }
}

/// <summary>
/// The merged newsroom content library: templates + era-voiced pools, loaded additively from
/// every <c>data/rules/newsroom/*.json</c> pack (ordinal filename order, the NewsArticleBank
/// convention). Read-side only, never a fold input, so packs are safe to edit and grow;
/// rendezvous selection keeps existing articles' template choices stable under appends.
/// </summary>
public sealed class NewsroomCorpus
{
    /// <summary>Canonical body-section order for every article.</summary>
    public static readonly IReadOnlyList<string> SectionOrder =
        ["lead", "context", "stats", "impact", "rivalry", "championship", "reliability", "next", "close"];

    public const string DefaultEra = "default";

    public required IReadOnlyList<NewsroomEra> Eras { get; init; }
    public required IReadOnlyList<NewsroomTemplate> Templates { get; init; }
    /// <summary>pool name → era key (or "default") → fragments.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Pools { get; init; }

    public static NewsroomCorpus Empty { get; } = new()
    {
        Eras = [],
        Templates = [],
        Pools = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
    };

    public bool IsEmpty => Templates.Count == 0;

    public string ResolveEra(int year)
    {
        foreach (var era in Eras)
        {
            if (year >= era.FromYear && year <= era.ToYear)
            {
                return era.Key;
            }
        }
        return DefaultEra;
    }

    public IReadOnlyList<string>? Pool(string name, string eraKey)
    {
        if (!Pools.TryGetValue(name, out var byEra))
        {
            return null;
        }
        if (byEra.TryGetValue(eraKey, out var fragments) && fragments.Count > 0)
        {
            return fragments;
        }
        return byEra.TryGetValue(DefaultEra, out var fallback) && fallback.Count > 0 ? fallback : null;
    }

    /// <summary>
    /// Picks the template for an event: filter to eligible (event kind, guards, era, desk),
    /// prefer the MOST SPECIFIC situation (highest guard count, a first-win special always
    /// beats the generic when it applies), then rendezvous-hash among that tier so adding
    /// templates later only re-picks the events the newcomer actually wins.
    /// </summary>
    public NewsroomTemplate? Select(NewsEvent e, string eraKey, string deskId, ulong masterSeed)
    {
        var kindKey = CamelCase(e.Kind.ToString());
        List<NewsroomTemplate>? eligible = null;
        foreach (var t in Templates)
        {
            if (!string.Equals(t.Event, kindKey, StringComparison.Ordinal)
                || t.Eras.Count > 0 && !t.Eras.Contains(eraKey)
                || t.Desks.Count > 0 && deskId.Length > 0 && !t.Desks.Contains(deskId)
                || !GuardsPass(t.When, e.Facts))
            {
                continue;
            }
            (eligible ??= []).Add(t);
        }

        if (eligible is null)
        {
            return null;
        }

        var maxGuards = eligible.Max(t => t.When.Count);
        NewsroomTemplate? best = null;
        ulong bestHash = 0;
        foreach (var t in eligible)
        {
            if (t.When.Count != maxGuards)
            {
                continue;
            }
            var hash = StableHash.Fnv1a64($"{masterSeed:x16}|{e.DedupeKey}|{t.Id}");
            if (best is null || hash > bestHash)
            {
                best = t;
                bestHash = hash;
            }
        }
        return best;
    }

    internal static bool GuardsPass(IReadOnlyDictionary<string, JsonElement> when, NewsEventFacts f)
    {
        foreach (var (key, value) in when)
        {
            var pass = key switch
            {
                "isFirstEver" => value.ValueKind is JsonValueKind.True == f.IsFirstEver,
                "clinchedTitle" => value.ValueKind is JsonValueKind.True == f.ClinchedTitle,
                "tookChampionshipLead" => value.ValueKind is JsonValueKind.True == f.TookChampionshipLead,
                "lostChampionshipLead" => value.ValueKind is JsonValueKind.True == f.LostChampionshipLead,
                "isWet" => value.ValueKind is JsonValueKind.True == f.IsWet,
                "isFinalRound" => value.ValueKind is JsonValueKind.True == f.IsFinalRound,
                "isSeasonOpener" => value.ValueKind is JsonValueKind.True == f.IsSeasonOpener,
                "rivalInvolved" => value.ValueKind is JsonValueKind.True == f.RivalInvolved,
                "minStreak" => value.ValueKind is JsonValueKind.Number && f.StreakLength >= value.GetInt32(),
                "minDrought" => value.ValueKind is JsonValueKind.Number && f.DroughtLength >= value.GetInt32(),
                "minUpset" => value.ValueKind is JsonValueKind.Number && f.UpsetMagnitude >= value.GetInt32(),
                "maxUpset" => value.ValueKind is JsonValueKind.Number && f.UpsetMagnitude <= value.GetInt32(),
                "milestoneCounter" => value.ValueKind is JsonValueKind.String
                    && string.Equals(value.GetString(), f.MilestoneCounter, StringComparison.Ordinal),
                _ => throw new InvalidOperationException($"Unknown template guard '{key}'."),
            };
            if (!pass)
            {
                return false;
            }
        }
        return true;
    }

    internal static string CamelCase(string pascal) =>
        pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];

    // ---------- loading ----------

    private sealed record CorpusDto
    {
        public int Version { get; init; } = 1;
        public IReadOnlyList<NewsroomEra>? Eras { get; init; }
        public IReadOnlyList<TemplateDto>? Templates { get; init; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>? Pools { get; init; }
    }

    private sealed record TemplateDto
    {
        public required string Id { get; init; }
        public required string Event { get; init; }
        public IReadOnlyDictionary<string, JsonElement>? When { get; init; }
        public IReadOnlyList<string>? Desks { get; init; }
        public IReadOnlyList<string>? Eras { get; init; }
        public required string Headline { get; init; }
        public string? Deck { get; init; }
        public string? Summary { get; init; }
        public IReadOnlyDictionary<string, string>? Sections { get; init; }
    }

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static NewsroomCorpus Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<CorpusDto>(json, LoadOptions)
            ?? throw new JsonException("Empty newsroom corpus document.");
        return Merge([dto]);
    }

    /// <summary>Merges every <c>*.json</c> in the directory (ordinal filename order): eras
    /// dedupe by key keeping the first declaration; templates and pool fragment lists append.
    /// Missing directory → <see cref="Empty"/> (the feed degrades, never crashes).</summary>
    public static NewsroomCorpus LoadDirectory(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return Empty;
        }

        var dtos = new List<CorpusDto>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            // desks.json is its own document shape, loaded by NewsDesks.
            if (string.Equals(Path.GetFileName(file), "desks.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var dto = JsonSerializer.Deserialize<CorpusDto>(File.ReadAllText(file), LoadOptions);
            if (dto is not null)
            {
                dtos.Add(dto);
            }
        }
        return Merge(dtos);
    }

    private static NewsroomCorpus Merge(IReadOnlyList<CorpusDto> dtos)
    {
        var eras = new List<NewsroomEra>();
        var eraKeys = new HashSet<string>(StringComparer.Ordinal);
        var templates = new List<NewsroomTemplate>();
        var pools = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        foreach (var dto in dtos)
        {
            foreach (var era in dto.Eras ?? [])
            {
                if (eraKeys.Add(era.Key))
                {
                    eras.Add(era);
                }
            }

            foreach (var t in dto.Templates ?? [])
            {
                templates.Add(new NewsroomTemplate
                {
                    Id = t.Id,
                    Event = t.Event,
                    When = t.When ?? new Dictionary<string, JsonElement>(),
                    Desks = t.Desks ?? [],
                    Eras = t.Eras ?? [],
                    Headline = t.Headline,
                    Deck = t.Deck ?? "",
                    Summary = t.Summary ?? "",
                    Sections = t.Sections ?? new Dictionary<string, string>(),
                });
            }

            foreach (var (poolName, byEra) in dto.Pools ?? new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>())
            {
                if (!pools.TryGetValue(poolName, out var target))
                {
                    pools[poolName] = target = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                }
                foreach (var (eraKey, fragments) in byEra)
                {
                    if (!target.TryGetValue(eraKey, out var list))
                    {
                        target[eraKey] = list = [];
                    }
                    list.AddRange(fragments);
                }
            }
        }

        return new NewsroomCorpus
        {
            Eras = eras,
            Templates = templates,
            Pools = pools.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, IReadOnlyList<string>>)kv.Value.ToDictionary(
                    p => p.Key,
                    p => (IReadOnlyList<string>)p.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
        };
    }

    /// <summary>Authoring validation, run by the content tests over every shipped pack:
    /// duplicate ids, unknown event kinds, unknown guard keys, empty headline/sections, and
    /// unknown section names all throw with the offending id.</summary>
    public void Validate()
    {
        var known = new HashSet<string>(
            Enum.GetNames<NewsEventKind>().Select(CamelCase), StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Templates)
        {
            if (!ids.Add(t.Id))
            {
                throw new InvalidOperationException($"Duplicate template id '{t.Id}'.");
            }
            if (!known.Contains(t.Event))
            {
                throw new InvalidOperationException($"Template '{t.Id}' names unknown event '{t.Event}'.");
            }
            if (string.IsNullOrWhiteSpace(t.Headline))
            {
                throw new InvalidOperationException($"Template '{t.Id}' has an empty headline.");
            }
            foreach (var section in t.Sections)
            {
                if (!SectionOrder.Contains(section.Key))
                {
                    throw new InvalidOperationException(
                        $"Template '{t.Id}' names unknown section '{section.Key}'.");
                }
                if (string.IsNullOrWhiteSpace(section.Value))
                {
                    throw new InvalidOperationException(
                        $"Template '{t.Id}' section '{section.Key}' is empty.");
                }
            }
            // Each guard key validates alone (unknown keys throw), a failing known guard
            // must not short-circuit past an unknown one hiding behind it.
            foreach (var guard in t.When)
            {
                GuardsPass(
                    new Dictionary<string, JsonElement> { [guard.Key] = guard.Value },
                    new NewsEventFacts());
            }
        }
    }
}
