#:property JsonSerializerIsReflectionEnabledByDefault=true
// Import a jusk-style AMS2 Custom-AI XML into a season pack as the ratings SOURCE OF TRUTH.
//
//   drivers.json  <- each driver's BASE ratings (13 dims) from the XML's base <driver> block
//                    (matched pack driver via entries.json ams2LiveryName -> driverId).
//   season.json   <- each round's aiOverrides <- the XML's per-track <driver tracks="..."> blocks,
//                    mapped track->round by shared venue token, FILTERED to that round's
//                    grid.starterDriverIds (so an override for a driver not racing that round is
//                    dropped, never a dangling ref). Optionally the f1db driverForm block is removed
//                    (--drop-form) so jusk's hand-tuned per-track form is the only per-round variation.
//
// vehicle_reliability is per-DRIVER in the XML but per-TEAM in the pack model, so it is NOT imported
// (reported only). Sim-inert for existing careers (they pin their own pack copy) and oracle
// (fixtures, not bundled packs) — only NEW careers from this pack see the change.
//
// Usage:
//   dotnet run tools/import_jusk_ai.cs -- <aiXml> <packDir> [--drop-form] [--write]
//   (no --write => dry run: prints the full plan, writes nothing)

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

var argList = args.ToList();
bool write = argList.Remove("--write");
bool dropForm = argList.Remove("--drop-form");
if (argList.Count < 2)
{
    Console.Error.WriteLine("usage: import_jusk_ai.cs -- <aiXml> <packDir> [--drop-form] [--write]");
    return 1;
}
string xmlPath = argList[0];
string packDir = argList[1];

// jusk snake_case -> pack camelCase for the 13 rating dims the pack models (no fuelManagement in
// these vintage packs; vehicle_reliability is team-level, handled separately).
var FIELD = new (string Xml, string Json)[]
{
    ("race_skill", "raceSkill"), ("qualifying_skill", "qualifyingSkill"),
    ("aggression", "aggression"), ("defending", "defending"), ("stamina", "stamina"),
    ("consistency", "consistency"), ("start_reactions", "startReactions"), ("wet_skill", "wetSkill"),
    ("tyre_management", "tyreManagement"), ("blue_flag_conceding", "blueFlagConceding"),
    ("weather_tyre_changes", "weatherTyreChanges"), ("avoidance_of_mistakes", "avoidanceOfMistakes"),
    ("avoidance_of_forced_mistakes", "avoidanceOfForcedMistakes"),
};

// ---- parse the AI XML -----------------------------------------------------
// jusk's header comment contains a "----" rule that is invalid inside an XML comment (AMS2 tolerates
// it); strip all comments before the strict .NET parser sees them.
string xmlText = System.Text.RegularExpressions.Regex.Replace(
    File.ReadAllText(xmlPath), "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
var doc = XDocument.Parse(xmlText);
var baseByLivery = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
var overridesByLivery = new List<(string Livery, string[] Tracks, Dictionary<string, double> Patch)>();
foreach (var d in doc.Descendants("driver"))
{
    string livery = (string?)d.Attribute("livery_name") ?? "";
    if (livery.Length == 0) continue;
    var vals = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (var (xml, json) in FIELD)
        if (d.Element(xml) is { } e && double.TryParse(e.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            vals[json] = v;
    string? tracks = (string?)d.Attribute("tracks");
    if (tracks is null)
        baseByLivery[livery] = vals; // base block (last one wins if duplicated)
    else
        overridesByLivery.Add((livery, tracks.Split(',').Select(t => t.Trim()).ToArray(), vals));
}

// ---- read the pack --------------------------------------------------------
string driversPath = Path.Combine(packDir, "drivers.json");
string seasonPath = Path.Combine(packDir, "season.json");
string entriesPath = Path.Combine(packDir, "entries.json");
var driversDoc = JsonNode.Parse(File.ReadAllText(driversPath))!;
var seasonDoc = JsonNode.Parse(File.ReadAllText(seasonPath))!;
var entriesDoc = JsonNode.Parse(File.ReadAllText(entriesPath))!;

// livery -> driverId (from entries)
var driverIdByLivery = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var e in entriesDoc["entries"]!.AsArray())
{
    string livery = (string)e!["ams2LiveryName"]!;
    string driverId = (string)e["driverId"]!;
    driverIdByLivery.TryAdd(livery, driverId);
}

// rounds: index -> (roundNumber, trackId, starterSet)
var rounds = new List<(int Round, string TrackId, HashSet<string> Starters, JsonObject Node)>();
foreach (var r in seasonDoc["rounds"]!.AsArray())
{
    var node = (JsonObject)r!;
    int roundNo = (int)node["round"]!;
    string trackId = (string)node["track"]!["id"]!;
    var starters = new HashSet<string>(StringComparer.Ordinal);
    if (node["grid"]?["starterDriverIds"] is JsonArray sa)
        foreach (var s in sa) starters.Add((string)s!);
    rounds.Add((roundNo, trackId, starters, node));
}

// ---- venue token matching: jusk track name -> pack round ------------------
// Generic track descriptors that are NOT venue identity (e.g. two different venues both tagged
// "historic") — excluded so Kyalami_Historic never matches Interlagos_Historic.
var STOP = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "historic", "circuit", "short", "long", "national", "vintage", "reverse", "layout",
    "full", "club", "sprint", "oval", "24hr", "grand", "prix",
};
IEnumerable<string> Tokens(string s) =>
    s.ToLowerInvariant().Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
     .Where(t => t.Length >= 4 && !int.TryParse(t, out _) && !STOP.Contains(t)); // venue words only

bool SharesVenue(string juskTrack, string packTrackId)
{
    foreach (var a in Tokens(juskTrack))
        foreach (var b in Tokens(packTrackId))
        {
            int n = Math.Min(a.Length, b.Length);
            int common = 0;
            while (common < n && a[common] == b[common]) common++;
            if (common >= 4) return true; // shared >=4-char venue prefix (nords~nordschleife, etc.)
        }
    return false;
}

// ---- build the base-ratings plan (driverId -> new 13-dim map) --------------
int unmatched = 0;
var basePlan = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
foreach (var (livery, vals) in baseByLivery)
{
    if (!driverIdByLivery.TryGetValue(livery, out var driverId))
    {
        Console.WriteLine($"  [unmatched livery] {livery} -> no pack entry");
        unmatched++;
        continue;
    }
    basePlan[driverId] = vals;
}

// ---- build the per-round aiOverrides plan ---------------------------------
// roundNo -> (driverId -> partial patch)
var overridePlan = new SortedDictionary<int, SortedDictionary<string, Dictionary<string, double>>>();
var droppedOrphans = new List<string>();
foreach (var (livery, tracks, patch) in overridesByLivery)
{
    if (!driverIdByLivery.TryGetValue(livery, out var driverId)) continue;
    // which pack rounds do these jusk tracks resolve to?
    var targetRounds = rounds
        .Where(r => tracks.Any(t => SharesVenue(t, r.TrackId)))
        .Select(r => r.Round).Distinct();
    foreach (int roundNo in targetRounds)
    {
        var rr = rounds.First(r => r.Round == roundNo);
        if (!rr.Starters.Contains(driverId))
        {
            droppedOrphans.Add($"R{roundNo} {driverId} (not a starter that round)");
            continue;
        }
        if (!overridePlan.TryGetValue(roundNo, out var perDriver))
            overridePlan[roundNo] = perDriver = new SortedDictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        perDriver[driverId] = patch;
    }
}

// ---- report ---------------------------------------------------------------
Console.WriteLine($"=== jusk import plan: {Path.GetFileName(xmlPath)} -> {packDir} ===\n");
Console.WriteLine("-- base ratings (raceSkill/qualifyingSkill deltas vs current) --");
var driverArr = driversDoc["drivers"]!.AsArray();
foreach (var dn in driverArr)
{
    var o = (JsonObject)dn!;
    string id = (string)o["id"]!;
    if (!basePlan.TryGetValue(id, out var nv)) continue;
    var cur = o["ratings"]!;
    double curR = (double)cur["raceSkill"]!, curQ = (double)cur["qualifyingSkill"]!;
    double newR = nv.GetValueOrDefault("raceSkill", curR), newQ = nv.GetValueOrDefault("qualifyingSkill", curQ);
    string mark = (Math.Abs(curR - newR) > 0.001 || Math.Abs(curQ - newQ) > 0.001) ? "  *" : "";
    Console.WriteLine($"  {(string)o["name"]!,-24} race {curR:0.00}->{newR:0.00}   quali {curQ:0.00}->{newQ:0.00}{mark}");
}
Console.WriteLine($"\n  unmatched liveries: {unmatched}");

Console.WriteLine("\n-- per-round aiOverrides (jusk per-track form -> rounds, grid-filtered) --");
foreach (var (roundNo, perDriver) in overridePlan)
{
    var rr = rounds.First(r => r.Round == roundNo);
    Console.WriteLine($"  R{roundNo} ({rr.TrackId}): {string.Join(", ", perDriver.Select(kv => kv.Key.Replace("driver.", "") + "{" + string.Join(",", kv.Value.Select(x => x.Key + "=" + x.Value.ToString("0.00", CultureInfo.InvariantCulture))) + "}"))}");
}
if (droppedOrphans.Count > 0)
    Console.WriteLine($"\n  dropped orphan overrides (driver not racing that round): {string.Join("; ", droppedOrphans)}");
Console.WriteLine($"\n  driverForm block: {(dropForm ? "REMOVED" : "kept")}");

if (!write)
{
    Console.WriteLine("\n(dry run — pass --write to apply)");
    return 0;
}

// ---- apply ----------------------------------------------------------------
// drivers.json: set the 13 base dims per matched driver.
foreach (var dn in driverArr)
{
    var o = (JsonObject)dn!;
    string id = (string)o["id"]!;
    if (!basePlan.TryGetValue(id, out var nv)) continue;
    var ratings = (JsonObject)o["ratings"]!;
    foreach (var (_, json) in FIELD)
        if (nv.TryGetValue(json, out double v)) ratings[json] = JsonValue.Create(v);
}

// season.json: rebuild each round's aiOverrides; optionally drop driverForm.
foreach (var (roundNo, _, _, node) in rounds)
{
    var patch = new JsonObject();
    if (overridePlan.TryGetValue(roundNo, out var perDriver))
        foreach (var (driverId, vals) in perDriver)
        {
            var p = new JsonObject();
            foreach (var (k, v) in vals) p[k] = JsonValue.Create(v);
            patch[driverId] = p;
        }
    node["aiOverrides"] = patch;
}
if (dropForm && seasonDoc["driverForm"] is not null)
    ((JsonObject)seasonDoc).Remove("driverForm");

WriteJson(driversPath, driversDoc);
WriteJson(seasonPath, seasonDoc);
Console.WriteLine("\nwritten: drivers.json, season.json");
return 0;

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract.
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    json = json.Replace("\r\n", "\n").Replace("\n", "\r\n");
    File.WriteAllText(path, json + "\r\n", new UTF8Encoding(false));
}
