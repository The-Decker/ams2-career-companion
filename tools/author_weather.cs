#:property JsonSerializerIsReflectionEnabledByDefault=true
// Apply researched per-round wet-race weather onto season packs, in place. SIM-INERT display data
// (same contract as author_weekend.cs — the career fold and the f1db oracle never read weather).
//
// Input JSON (built from the adversarially-verified research in docs/dev/wet-weather-research.md):
//   { "seasons": [ { "year": 1974, "rounds": [
//       { "round": 2, "raceSlots": ["Rain","Rain","Light Rain","Overcast"],
//         "qualifyingSlots": ["Rain","Rain","Rain","Rain"] } ] } ] }
//
// For each listed round: weekend.races[0].weatherSlots <- raceSlots, and (when present)
// weekend.qualifying.weatherSlots <- qualifyingSlots. Unlisted rounds are untouched (they keep the
// authored Clear x4 defaults). Slot tokens are validated against the AMS2 manual-weather vocabulary
// (docs/dev/ams2-custom-race-reference §2) — an unknown token aborts before anything is written.
//
// Usage:
//   dotnet run tools/author_weather.cs -- <wetJson> <packsDir> [--write]
//   (no --write => dry run: prints the plan, writes nothing)

using System.Text;
using System.Text.Json.Nodes;

var argList = args.ToList();
bool write = argList.Remove("--write");
if (argList.Count < 2)
{
    Console.Error.WriteLine("usage: author_weather.cs -- <wetJson> <packsDir> [--write]");
    return 1;
}
string wetJsonPath = argList[0];
string packsDir = argList[1];

var vocabulary = new HashSet<string>(StringComparer.Ordinal)
{
    "Clear", "Light Cloud", "Medium Cloud", "Heavy Cloud", "Overcast", "Foggy", "Hazy",
    "Light Rain", "Rain", "Heavy Rain", "Storm", "Thunderstorm",
};

var wetDoc = (JsonObject)JsonNode.Parse(File.ReadAllText(wetJsonPath))!;
int errors = 0, applied = 0;
// Validate + mutate in memory first; flush to disk only when EVERY season came through clean.
var pendingWrites = new List<(string Path, JsonObject Doc)>();

foreach (var s in wetDoc["seasons"]!.AsArray())
{
    var season = (JsonObject)s!;
    int year = (int)season["year"]!;
    string seasonPath = Path.Combine(packsDir, $"f1-{year}", "season.json");
    if (!File.Exists(seasonPath))
    {
        Console.Error.WriteLine($"  f1-{year}: season.json not found — SKIPPED");
        errors++;
        continue;
    }
    var seasonDoc = (JsonObject)JsonNode.Parse(File.ReadAllText(seasonPath))!;
    var roundsByNo = seasonDoc["rounds"]!.AsArray()
        .Cast<JsonObject>()
        .ToDictionary(r => (int)r["round"]!);

    Console.WriteLine($"=== f1-{year} ===");
    bool touched = false;
    foreach (var e in season["rounds"]!.AsArray())
    {
        var entry = (JsonObject)e!;
        int roundNo = (int)entry["round"]!;
        if (!roundsByNo.TryGetValue(roundNo, out var round))
        {
            Console.Error.WriteLine($"  R{roundNo}: not in pack — ERROR");
            errors++;
            continue;
        }
        if (round["weekend"] is not JsonObject weekend)
        {
            Console.Error.WriteLine($"  R{roundNo}: no weekend block — ERROR (author_weekend first)");
            errors++;
            continue;
        }

        string?[] race = Slots(entry, "raceSlots");
        string?[] quali = Slots(entry, "qualifyingSlots");
        if (race.Length is 0 or > 4 || race.Any(t => t is null || !vocabulary.Contains(t)) ||
            quali.Any(t => t is null || !vocabulary.Contains(t)) || quali.Length > 4)
        {
            Console.Error.WriteLine($"  R{roundNo}: invalid slots — ERROR");
            errors++;
            continue;
        }

        if (weekend["races"] is JsonArray races && races.Count > 0 && races[0] is JsonObject race0)
            race0["weatherSlots"] = WeatherArray(race!);
        else
        {
            Console.Error.WriteLine($"  R{roundNo}: no races[0] — ERROR");
            errors++;
            continue;
        }
        if (quali.Length > 0 && weekend["qualifying"] is JsonObject qualifying)
            qualifying["weatherSlots"] = WeatherArray(quali!);

        Console.WriteLine($"  R{roundNo} ({(string?)round["name"]}): race [{string.Join(", ", race)}]" +
                          (quali.Length > 0 ? $" + qualifying [{string.Join(", ", quali)}]" : ""));
        applied++;
        touched = true;
    }

    if (touched)
        pendingWrites.Add((seasonPath, seasonDoc));
}

Console.WriteLine($"\n  wet rounds applied: {applied}, errors: {errors}");
if (errors > 0)
{
    Console.Error.WriteLine("errors present — nothing written for safety.");
    return 1;
}
if (write)
    foreach (var (path, doc) in pendingWrites)
        WriteJson(path, doc);
Console.WriteLine(write ? $"written: {pendingWrites.Count} packs" : "(dry run — pass --write to apply)");
return 0;

// ---- helpers --------------------------------------------------------------

static string?[] Slots(JsonObject entry, string key) =>
    entry[key] is JsonArray a ? a.Select(n => (string?)n).ToArray() : [];

// A fresh array each call — a JsonNode cannot have two parents.
static JsonArray WeatherArray(string[] slots)
{
    var array = new JsonArray();
    foreach (var slot in slots)
        array.Add(JsonValue.Create(slot));
    return array;
}

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract (same as author_weekend.cs).
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    json = json.Replace("\r\n", "\n").Replace("\n", "\r\n");
    File.WriteAllText(path, json + "\r\n", new UTF8Encoding(false));
}
