// Applies a researched SEAT-COVERAGE PLAN to a season pack (the M2 max-grid rollout):
//
//   entries.json  <- one entry per CAR STINT from the plan. A seat ("car") is a team+number; each
//                    stint gives the driver, the rounds, and the EXACT skinpack livery NAME active
//                    for those rounds (mid-season variant sets rename cars: Jordan #32 B. Gachot
//                    becomes Jordan #32 M. Schumacher — the race-by-race variant binder activates
//                    the right file, the entry binds the matching name). Cars absent from the plan
//                    are DROPPED (the roster IS the skinpack roster — Mike's rule), loudly.
//   drivers.json  <- plan newDrivers appended (real substitutes researched from data/history).
//   season.json   <- every round's grid regenerated: starterDriverIds = all covering cars,
//                    grid.size = min(cap, covering), setupGuide opponents = size - 1.
//
// Plan shape:
//   { "seats": [ { "car": "Jordan #32", "teamId": "team.jordan", "number": "32",
//                  "stints": [ { "driverId": "driver.bertrand_gachot", "rounds": "1-10",
//                                "livery": "Jordan #32 B. Gachot" }, ... ] } ],
//     "newDrivers": [ { "id": "driver.x", "name": "...", "country": "GBR", "born": 1942,
//                       "raceSkill": 0.72, "qualifyingSkill": 0.70, "basis": "..." } ] }
//   teamId/number may be omitted when some stint livery matches an existing entry (inherited).
//
// The plan is produced by per-pack research (real substitute > alive-but-absent extension >
// deliberate gap) so no seat races a driver past their real exit. Pack data => NEW careers only.
//
// Usage:
//   dotnet run tools/apply_roster.cs -- <packDir> <planJson> <cap> [--write]
//   (no --write => dry run: prints the plan, writes nothing; cap 99 = class cap unknown/uncapped)

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

var argList = args.ToList();
bool write = argList.Remove("--write");
if (argList.Count < 3)
{
    Console.Error.WriteLine("usage: apply_roster.cs -- <packDir> <planJson> <cap> [--write]");
    return 1;
}
string packDir = argList[0];
string planPath = argList[1];
int cap = int.Parse(argList[2], CultureInfo.InvariantCulture);

var plan = JsonNode.Parse(File.ReadAllText(planPath))!;
var entriesDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "entries.json")))!;
var driversDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "drivers.json")))!;
var seasonDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "season.json")))!;

// ---- current entries: livery -> (teamId, number) ----------------------------
var current = new Dictionary<string, (string TeamId, string? Number)>(StringComparer.Ordinal);
foreach (var e in entriesDoc["entries"]!.AsArray())
{
    string livery = (string)e!["ams2LiveryName"]!;
    current.TryAdd(livery, ((string)e["teamId"]!, (string?)e["number"]));
}

var knownDrivers = new HashSet<string>(StringComparer.Ordinal);
foreach (var d in driversDoc["drivers"]!.AsArray())
    knownDrivers.Add((string)d!["id"]!);

var knownTeams = new HashSet<string>(StringComparer.Ordinal);
var teamsDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "teams.json")))!;
foreach (var t in teamsDoc["teams"]!.AsArray())
    knownTeams.Add((string)t!["id"]!);

// ---- new drivers ------------------------------------------------------------
int addedDrivers = 0;
if (plan["newDrivers"] is JsonArray newDrivers)
{
    foreach (var nd in newDrivers)
    {
        string id = (string)nd!["id"]!;
        if (knownDrivers.Contains(id))
        {
            Console.WriteLine($"  [driver exists, skipped] {id}");
            continue;
        }
        var driver = new JsonObject
        {
            ["id"] = id,
            ["name"] = (string)nd["name"]!,
            ["country"] = (string?)nd["country"],
            ["born"] = nd["born"] is { } b ? (int)b : null,
            ["ratings"] = new JsonObject
            {
                ["raceSkill"] = (double)nd["raceSkill"]!,
                ["qualifyingSkill"] = (double)nd["qualifyingSkill"]!,
            },
        };
        driversDoc["drivers"]!.AsArray().Add(driver);
        knownDrivers.Add(id);
        addedDrivers++;
        Console.WriteLine($"  [new driver] {id} race {(double)nd["raceSkill"]!:0.00} quali {(double)nd["qualifyingSkill"]!:0.00} — {(string?)nd["basis"]}");
    }
}

// ---- rebuild entries ----------------------------------------------------------
var seats = plan["seats"]!.AsArray();
var newEntries = new JsonArray();
// per car: (car label, list of (driverId, rounds))
var coverage = new List<(string Car, string DriverId, string Rounds)>();
var keptLiveries = new HashSet<string>(StringComparer.Ordinal);
int problems = 0;

foreach (var seat in seats)
{
    string car = (string)seat!["car"]!;
    string? teamId = (string?)seat["teamId"];
    string? number = (string?)seat["number"];
    var stints = seat["stints"]!.AsArray();

    if (teamId is null || number is null)
        foreach (var stint in stints)
            if (current.TryGetValue((string)stint!["livery"]!, out var cur))
            {
                teamId ??= cur.TeamId;
                number ??= cur.Number;
                break;
            }
    if (teamId is null)
    {
        Console.WriteLine($"  [ERROR] car \"{car}\": no stint livery matches an existing entry and no teamId given");
        problems++;
        continue;
    }
    if (!knownTeams.Contains(teamId))
    {
        Console.WriteLine($"  [ERROR] car \"{car}\": unknown team '{teamId}'");
        problems++;
        continue;
    }

    foreach (var stint in stints)
    {
        string driverId = (string)stint!["driverId"]!;
        string rounds = (string)stint["rounds"]!;
        string livery = (string)stint["livery"]!;
        keptLiveries.Add(livery);
        if (!knownDrivers.Contains(driverId))
        {
            Console.WriteLine($"  [ERROR] car \"{car}\": unknown driver '{driverId}' (add it to newDrivers)");
            problems++;
            continue;
        }
        newEntries.Add(new JsonObject
        {
            ["teamId"] = teamId,
            ["driverId"] = driverId,
            ["number"] = number,
            ["rounds"] = rounds,
            ["ams2LiveryName"] = livery,
        });
        coverage.Add((car, driverId, rounds));
    }
}

foreach (var livery in current.Keys)
    if (!keptLiveries.Contains(livery))
        Console.WriteLine($"  [dropped livery] \"{livery}\" — not in the plan roster");

// ---- regenerate grids ---------------------------------------------------------
bool Covers(string rangeExpr, int round)
{
    foreach (var token in rangeExpr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        var dash = token.Split('-', 2);
        if (dash.Length == 2)
        {
            if (int.Parse(dash[0], CultureInfo.InvariantCulture) <= round &&
                round <= int.Parse(dash[1], CultureInfo.InvariantCulture))
                return true;
        }
        else if (int.Parse(token, CultureInfo.InvariantCulture) == round)
            return true;
    }
    return false;
}

Console.WriteLine($"\n-- grids (cap {cap}) --");
foreach (var r in seasonDoc["rounds"]!.AsArray())
{
    var node = (JsonObject)r!;
    int roundNo = (int)node["round"]!;
    var carsSeen = new HashSet<string>(StringComparer.Ordinal);
    var covering = new List<string>();
    foreach (var (car, driverId, rangeExpr) in coverage)
    {
        if (!Covers(rangeExpr, roundNo)) continue;
        if (!carsSeen.Add(car))
        {
            Console.WriteLine($"  [WARN] R{roundNo}: car \"{car}\" covered twice — overlapping stints?");
            problems++;
            continue;
        }
        covering.Add(driverId);
    }

    int size = Math.Min(cap, covering.Count);
    int oldSize = node["grid"]?["size"] is { } s ? (int)s : 0;
    Console.WriteLine($"  R{roundNo}: size {oldSize}->{size}, starters {covering.Count}" +
        (covering.Count > cap ? " (roster > cap; resolver trims slowest)" : ""));

    if (!write) continue;
    var grid = node["grid"] as JsonObject;
    if (grid is null) node["grid"] = grid = new JsonObject();
    grid["size"] = size;
    var arr = new JsonArray();
    foreach (var id in covering) arr.Add(id);
    grid["starterDriverIds"] = arr;
    if (node["setupGuide"]?["session"] is JsonObject session && session["opponents"] is not null)
        session["opponents"] = size - 1;
}

if (problems > 0)
{
    Console.WriteLine($"\n{problems} problem(s) — fix the plan first{(write ? "; NOTHING WRITTEN" : "")}.");
    return 1;
}
if (!write)
{
    Console.WriteLine($"\n(dry run — {seats.Count} cars, {newEntries.Count} entries, {addedDrivers} new drivers; pass --write to apply)");
    return 0;
}

entriesDoc["entries"] = newEntries;
WriteJson(Path.Combine(packDir, "entries.json"), entriesDoc);
WriteJson(Path.Combine(packDir, "drivers.json"), driversDoc);
WriteJson(Path.Combine(packDir, "season.json"), seasonDoc);
Console.WriteLine($"\nwritten: entries.json ({newEntries.Count} entries), drivers.json (+{addedDrivers}), season.json");
return 0;

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract.
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        .Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
    File.WriteAllText(path, json, new UTF8Encoding(false));
}
