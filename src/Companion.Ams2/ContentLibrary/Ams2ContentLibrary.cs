using System.Text.Json;

namespace Companion.Ams2.ContentLibrary;

public sealed record Ams2Vehicle
{
    /// <summary>The .crd filename base — unique per car variant (engine/track-config variants
    /// are distinct vehicles).</summary>
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public string? Name { get; init; }
    public string? VehicleName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public int Year { get; init; }
    /// <summary>Exact case-sensitive class name = CustomAIDrivers filename base.</summary>
    public required string VehicleClass { get; init; }
    public string? Group { get; init; }
    public bool AiOnly { get; init; }
    public bool IsOpenWheeler { get; init; }
    public int PerformanceIndex { get; init; }
}

public sealed record Ams2Class
{
    public required string XmlName { get; init; }
    public int VehicleCount { get; init; }
    public IReadOnlyList<int> Years { get; init; } = [];
    public IReadOnlyList<string> Vehicles { get; init; } = [];
}

public sealed record Ams2Track
{
    /// <summary>Folder name = internal track id used by custom-AI "tracks=" attributes.</summary>
    public required string Id { get; init; }
    public string? TrackName { get; init; }
    public string? ShortTrackName { get; init; }
    public string? Location { get; init; }
    public string? Country { get; init; }
    public int Year { get; init; }
    public int LengthMeters { get; init; }
    /// <summary>The grid cap preflight checks generated grids against. As low as 5 on
    /// rallycross/dirt layouts — never assume a floor.</summary>
    public int MaxAiParticipants { get; init; }
    public string? TrackType { get; init; }
    public string? TrackGrade { get; init; }
    public string? EventTypes { get; init; }
    public int? OvalType { get; init; }
}

public sealed record Ams2LiveryClassEntry
{
    public string? Name { get; init; }
    public string? VariantOf { get; init; }
    public IReadOnlyList<string> StockLib1563 { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ObservedInUserFiles { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>
/// The machine-extracted AMS2 content library (data/ams2/*.json): classes, vehicles, tracks,
/// and known livery names. Refreshable data extracted from a local install — never compiled
/// in, so game updates only require re-extraction.
/// </summary>
public sealed class Ams2ContentLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public required IReadOnlyDictionary<string, Ams2Class> Classes { get; init; }
    public required IReadOnlyDictionary<string, Ams2Vehicle> Vehicles { get; init; }
    public required IReadOnlyDictionary<string, Ams2Track> Tracks { get; init; }
    public required IReadOnlyDictionary<string, Ams2LiveryClassEntry> Liveries { get; init; }
    public required string ExtractedFrom { get; init; }

    public static Ams2ContentLibrary Load(string dataDirectory)
    {
        var classes = ReadFile<ClassesFile>(Path.Combine(dataDirectory, "classes.json"));
        var vehicles = ReadFile<VehiclesFile>(Path.Combine(dataDirectory, "vehicles.json"));
        var tracks = ReadFile<TracksFile>(Path.Combine(dataDirectory, "tracks.json"));
        var liveries = ReadFile<LiveriesFile>(Path.Combine(dataDirectory, "liveries.json"));

        return new Ams2ContentLibrary
        {
            // Class and livery names are case-SENSITIVE: the game matches CustomAIDrivers
            // filenames and livery_name attributes exactly, so the library must too.
            Classes = classes.Classes.ToDictionary(c => c.XmlName, StringComparer.Ordinal),
            Vehicles = DeduplicateVehicles(vehicles.Vehicles),
            Tracks = tracks.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal),
            Liveries = liveries.Classes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            ExtractedFrom = classes.ExtractedFrom ?? "unknown",
        };
    }

    /// <summary>
    /// The install genuinely ships duplicate .crd basenames (stock_corolla_23.crd exists in both
    /// Vehicles\stock_corolla\ and Vehicles\stock_corolla_23\), so an extracted vehicles.json may
    /// carry duplicate ids. Resolve deterministically instead of throwing: the entry whose dir
    /// matches its id wins (the canonical copy — same rule as Companion.ContentExtract); with no
    /// dir-named entry, the first occurrence wins.
    /// </summary>
    private static Dictionary<string, Ams2Vehicle> DeduplicateVehicles(IReadOnlyList<Ams2Vehicle> vehicles)
    {
        var byId = new Dictionary<string, Ams2Vehicle>(vehicles.Count, StringComparer.Ordinal);
        foreach (var vehicle in vehicles)
        {
            if (!byId.TryGetValue(vehicle.Id, out var kept))
            {
                byId.Add(vehicle.Id, vehicle);
            }
            else if (IsDirNamed(vehicle) && !IsDirNamed(kept))
            {
                byId[vehicle.Id] = vehicle;
            }
        }
        return byId;

        // Paths on the (Windows) install are case-insensitive, unlike the game's class names.
        static bool IsDirNamed(Ams2Vehicle v) =>
            string.Equals(v.Dir, v.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static T ReadFile<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
               ?? throw new JsonException($"{path} deserialized to null.");
    }

    private sealed record ClassesFile
    {
        public string? ExtractedFrom { get; init; }
        public required IReadOnlyList<Ams2Class> Classes { get; init; }
    }

    private sealed record VehiclesFile
    {
        public string? ExtractedFrom { get; init; }
        public required IReadOnlyList<Ams2Vehicle> Vehicles { get; init; }
    }

    private sealed record TracksFile
    {
        public string? ExtractedFrom { get; init; }
        public required IReadOnlyList<Ams2Track> Tracks { get; init; }
    }

    private sealed record LiveriesFile
    {
        public string? ExtractedFrom { get; init; }
        public required IReadOnlyDictionary<string, Ams2LiveryClassEntry> Classes { get; init; }
    }
}
