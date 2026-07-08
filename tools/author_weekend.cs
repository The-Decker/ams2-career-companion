#:property JsonSerializerIsReflectionEnabledByDefault=true
// Author the per-session weekend model (durations + 4-slot weather) + the season refuelling flag
// onto every round of a season pack, in place. SIM-INERT display data — the career fold and the
// f1db oracle never read any of it, so this changes no result and needs no determinism gate; the
// only contract is that existing packs keep loading (every field is additive/optional).
//
// For each round's `weekend`:
//   practice.durationMinutes  = --practice   (default 60)   + practice.weatherSlots  = --weather
//   qualifying.durationMinutes = --qualifying (default 60)   + qualifying.weatherSlots = --weather
//   races[0].weatherSlots     = --weather   (default Clear x4)
// and season.refuellingAllowed = false (unless --refuel), inserted right after ams2Class.
//
// AMS2 facts this encodes (docs/dev/ams2-custom-race-reference §1/§2/§5): practice + qualifying are
// ALWAYS time-limited (never lap-based); each session has up to 4 independent weather slots; 1967
// cars ran the distance on one tank (no refuelling — it only arrived ~1982).
//
// Usage:
//   dotnet run tools/author_weekend.cs -- <packDir> [--practice 60] [--qualifying 60]
//                                          [--weather Clear,Clear,Clear,Clear] [--refuel] [--write]
//   (no --write => dry run: prints the plan, writes nothing)

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

var argList = args.ToList();
bool write = argList.Remove("--write");
bool refuel = argList.Remove("--refuel");

int practiceMinutes = TakeInt("--practice", 60);
int qualifyingMinutes = TakeInt("--qualifying", 60);
string[] weather = TakeString("--weather", "Clear,Clear,Clear,Clear")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (argList.Count < 1)
{
    Console.Error.WriteLine(
        "usage: author_weekend.cs -- <packDir> [--practice 60] [--qualifying 60] " +
        "[--weather Clear,Clear,Clear,Clear] [--refuel] [--write]");
    return 1;
}
string packDir = argList[0];
if (weather.Length is 0 or > 4)
{
    Console.Error.WriteLine($"--weather must list 1..4 slots (got {weather.Length}).");
    return 1;
}

string seasonPath = Path.Combine(packDir, "season.json");
var seasonDoc = (JsonObject)JsonNode.Parse(File.ReadAllText(seasonPath))!;

Console.WriteLine($"=== author weekend: {seasonPath} ===");
Console.WriteLine($"  practice {practiceMinutes} min, qualifying {qualifyingMinutes} min, " +
                  $"weather [{string.Join(", ", weather)}], refuellingAllowed {refuel.ToString().ToLowerInvariant()}\n");

int touched = 0;
foreach (var r in seasonDoc["rounds"]!.AsArray())
{
    var round = (JsonObject)r!;
    int roundNo = (int)round["round"]!;
    if (round["weekend"] is not JsonObject weekend)
    {
        Console.WriteLine($"  R{roundNo}: no weekend block — SKIPPED");
        continue;
    }

    SetSession(weekend, "practice", practiceMinutes, weather);
    SetSession(weekend, "qualifying", qualifyingMinutes, weather);

    if (weekend["races"] is JsonArray races && races.Count > 0 && races[0] is JsonObject race0)
        race0["weatherSlots"] = WeatherArray(weather);
    else
        Console.WriteLine($"  R{roundNo}: no races[0] — race weather not set");

    Console.WriteLine($"  R{roundNo} ({(string?)round["name"]}): practice+qualifying durations + " +
                      $"{weather.Length}-slot weather on practice/qualifying/race");
    touched++;
}

// season-wide refuelling flag, placed right after ams2Class for a readable file.
InsertAfter(seasonDoc, "ams2Class", "refuellingAllowed", JsonValue.Create(refuel));

Console.WriteLine($"\n  rounds touched: {touched}");
Console.WriteLine($"  season.refuellingAllowed = {refuel.ToString().ToLowerInvariant()} (after ams2Class)");

if (!write)
{
    Console.WriteLine("\n(dry run — pass --write to apply)");
    return 0;
}

WriteJson(seasonPath, seasonDoc);
Console.WriteLine($"\nwritten: {seasonPath}");
return 0;

// ---- helpers --------------------------------------------------------------

void SetSession(JsonObject weekend, string name, int minutes, string[] slots)
{
    if (weekend[name] is not JsonObject session)
        return; // a weekend without that session — leave it alone
    session["durationMinutes"] = minutes;
    session["weatherSlots"] = WeatherArray(slots);
}

// A fresh array each call — a JsonNode cannot have two parents.
static JsonArray WeatherArray(string[] slots)
{
    var array = new JsonArray();
    foreach (var slot in slots)
        array.Add(JsonValue.Create(slot));
    return array;
}

// Insert newKey right after afterKey, preserving every other key's order (JsonObject has no
// insert-at-index). Overwrites in place if newKey already exists.
static void InsertAfter(JsonObject obj, string afterKey, string newKey, JsonNode? value)
{
    if (obj.ContainsKey(newKey))
    {
        obj[newKey] = value;
        return;
    }
    var entries = obj.ToList(); // snapshot (key + node) in order
    obj.Clear();                // detaches every node so it can be re-parented
    foreach (var (key, node) in entries)
    {
        obj[key] = node;
        if (key == afterKey)
            obj[newKey] = value;
    }
    if (!obj.ContainsKey(newKey)) // afterKey absent — append at end rather than drop it
        obj[newKey] = value;
}

int TakeInt(string flag, int fallback)
{
    int i = argList.IndexOf(flag);
    if (i < 0 || i + 1 >= argList.Count)
        return fallback;
    int value = int.Parse(argList[i + 1], CultureInfo.InvariantCulture);
    argList.RemoveRange(i, 2);
    return value;
}

string TakeString(string flag, string fallback)
{
    int i = argList.IndexOf(flag);
    if (i < 0 || i + 1 >= argList.Count)
        return fallback;
    string value = argList[i + 1];
    argList.RemoveRange(i, 2);
    return value;
}

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract (same as import_jusk_ai.cs).
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    json = json.Replace("\r\n", "\n").Replace("\n", "\r\n");
    File.WriteAllText(path, json + "\r\n", new UTF8Encoding(false));
}
