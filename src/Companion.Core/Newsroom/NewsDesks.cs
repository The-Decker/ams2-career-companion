using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Determinism;

namespace Companion.Core.Newsroom;

/// <summary>One fictional editorial desk/publication (docs/dev/newsroom-history-overhaul.md D6).
/// Data-driven from <c>data/rules/newsroom/desks.json</c>; ids are stable data-format keys.</summary>
public sealed record NewsDesk
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Two-letter masthead monogram for the badge.</summary>
    public string Monogram { get; init; } = "";
    public string Tone { get; init; } = "";
    /// <summary>Preferred <see cref="NewsroomCategory"/> keys (camelCase); empty = generalist.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Era keys this desk publishes in; empty = every era.</summary>
    public IReadOnlyList<string> Eras { get; init; } = [];
}

/// <summary>The desk roster + deterministic assignment: a story's desk is rendezvous-hashed
/// over the desks that cover its category and era, so the same story always carries the same
/// masthead and new desks only claim the stories they win.</summary>
public sealed class NewsDesks
{
    public required IReadOnlyList<NewsDesk> All { get; init; }

    public static NewsDesks Empty { get; } = new() { All = [] };

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record DesksDto
    {
        public int Version { get; init; } = 1;
        public IReadOnlyList<NewsDesk>? Desks { get; init; }
    }

    public static NewsDesks Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<DesksDto>(json, LoadOptions)
            ?? throw new JsonException("Empty desks document.");
        var desks = dto.Desks ?? [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var desk in desks)
        {
            if (!ids.Add(desk.Id))
            {
                throw new JsonException($"Duplicate desk id '{desk.Id}'.");
            }
        }
        return new NewsDesks { All = desks };
    }

    public static NewsDesks Load(string? directory)
    {
        if (directory is null)
        {
            return Empty;
        }
        var path = Path.Combine(directory, "desks.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public NewsDesk? Assign(NewsroomCategory category, string eraKey, string dedupeKey, ulong masterSeed)
    {
        var categoryKey = NewsroomCorpus.CamelCase(category.ToString());
        NewsDesk? best = null;
        ulong bestHash = 0;
        var anyPreferred = false;

        foreach (var pass in (ReadOnlySpan<bool>)[true, false])
        {
            foreach (var desk in All)
            {
                if (desk.Eras.Count > 0 && !desk.Eras.Contains(eraKey))
                {
                    continue;
                }
                var prefers = desk.Categories.Contains(categoryKey);
                if (pass && !prefers || !pass && desk.Categories.Count > 0)
                {
                    continue;
                }
                var hash = StableHash.Fnv1a64($"{masterSeed:x16}|{dedupeKey}|desk|{desk.Id}");
                if (best is null || hash > bestHash)
                {
                    best = desk;
                    bestHash = hash;
                    anyPreferred = pass;
                }
            }
            if (best is not null && anyPreferred)
            {
                break; // desks that prefer the category outrank generalists
            }
        }

        return best;
    }
}
