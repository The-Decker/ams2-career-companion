#:property JsonSerializerIsReflectionEnabledByDefault=true
// author_alternates — write the curated OPTIONAL alternate mod-track for placeholder rounds into the
// season packs (data only; inert until the player opts into alternate tracks at career creation).
//
// For each recommendation {pack, round, tag, category}: open packs/<pack>/season.json, find the round,
// read the real race distance from its setupGuide.notes ("... / <km> km reproduced as ..." — the same
// distance-preservation string placeholder rounds already carry), and precompute the alternate's lap
// count = round(km*1000 / altTrackLengthMeters) from the content library. Then set:
//   round.track.alternate = { id: <tag>, laps: <altLaps>, isRealVenue: category=="real-venue" }
// A "real-venue" alternate (the authentic circuit now available as a mod) will clear isPlaceholder
// when applied; an "era-filler" stays a labelled placeholder.
//
// Usage: dotnet run tools/author_alternates.cs -- <recsJson> <packsDir> <tracksJson> [--write]
//   (no --write => dry run)

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var argList = args.ToList();
bool write = argList.Remove("--write");
if (argList.Count < 3)
{
    Console.Error.WriteLine("usage: author_alternates.cs -- <recsJson> <packsDir> <tracksJson> [--write]");
    return 1;
}
string recsPath = argList[0], packsDir = argList[1], tracksPath = argList[2];

var recs = JsonNode.Parse(File.ReadAllText(recsPath))!.AsArray();

// alt track id -> length metres (from the refreshed library)
var lengthById = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var t in JsonNode.Parse(File.ReadAllText(tracksPath))!["tracks"]!.AsArray())
    lengthById[(string)t!["id"]!] = (int)t["lengthMeters"]!;

var kmRegex = new Regex(@"/\s*(?<km>\d+(?:\.\d+)?)\s*km reproduced", RegexOptions.IgnoreCase);

// group recs by pack so each season.json is loaded + written once
foreach (var byPack in recs.GroupBy(r => (string)r!["pack"]!))
{
    string pack = byPack.Key;
    string seasonPath = Path.Combine(packsDir, pack, "season.json");
    if (!File.Exists(seasonPath))
    {
        Console.Error.WriteLine($"  {pack}: season.json missing — SKIPPED");
        continue;
    }
    var seasonDoc = JsonNode.Parse(File.ReadAllText(seasonPath))!;
    var rounds = seasonDoc["rounds"]!.AsArray();
    int applied = 0;

    foreach (var rec in byPack)
    {
        int roundNo = (int)rec!["round"]!;
        string tag = (string)rec["tag"]!;
        bool isRealVenue = (string?)rec["category"] == "real-venue";

        var round = rounds.FirstOrDefault(r => (int)r!["round"]! == roundNo) as JsonObject;
        if (round is null) { Console.Error.WriteLine($"  {pack} R{roundNo}: not found"); continue; }
        if (!lengthById.TryGetValue(tag, out int altLen))
        { Console.Error.WriteLine($"  {pack} R{roundNo}: alt tag '{tag}' not in library"); continue; }

        string notes = (string?)round["setupGuide"]?["notes"] ?? "";
        var m = kmRegex.Match(notes);
        if (!m.Success)
        { Console.Error.WriteLine($"  {pack} R{roundNo}: no '<km> km reproduced' in notes — cannot preserve distance; SKIPPED"); continue; }

        double km = double.Parse(m.Groups["km"].Value, CultureInfo.InvariantCulture);
        int altLaps = Math.Max(1, (int)Math.Round(km * 1000.0 / altLen, MidpointRounding.AwayFromZero));

        var track = (JsonObject)round["track"]!;
        track["alternate"] = new JsonObject
        {
            ["id"] = tag,
            ["laps"] = altLaps,
            ["isRealVenue"] = isRealVenue,
        };
        Console.WriteLine($"  {pack} R{roundNo,-2} {(string?)round["name"],-24} -> {tag,-28} " +
                          $"{km} km / {altLen} m = {altLaps} laps  {(isRealVenue ? "[real-venue]" : "[filler]")}");
        applied++;
    }

    if (write && applied > 0)
        WriteJson(seasonPath, seasonDoc);
    Console.WriteLine($"  == {pack}: {applied} alternate(s){(write ? " WRITTEN" : "")} ==\n");
}

if (!write)
    Console.WriteLine("(dry run — pass --write to apply)");
return 0;

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract.
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    json = json.Replace("\r\n", "\n").Replace("\n", "\r\n");
    File.WriteAllText(path, json + "\r\n", new UTF8Encoding(false));
}
