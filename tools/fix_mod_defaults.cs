#:property JsonSerializerIsReflectionEnabledByDefault=true
// fix_mod_defaults — enforce the rule "no round may DEFAULT to a mod track". For each of the 5 rounds
// that currently default to a community mod, swap the DEFAULT to a base/DLC AMS2 track (a
// distance-preserving placeholder) and move the mod into the OPT-IN track.alternate slot.
//
// - baseLaps = round(realKm*1000 / baseLen)  (distance preserved at the base stand-in)
// - altLaps  = real-venue alt -> the round's authentic lap count (you race the real circuit);
//              filler alt      -> round(realKm*1000 / modLen)  (distance preserved at the mod)
// - isPlaceholder -> true (the real venue isn't in AMS2 base); notes rewritten honestly (no more
//   "AMS2 ships ..." for a mod), keeping "<hist> laps / <km> km reproduced as <baseLaps> laps" +
//   the real venue name so the ReferencePack placeholder tests hold.
//
// Usage: dotnet run tools/fix_mod_defaults.cs -- <packsDir> <tracksJson> [--write]

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var argList = args.ToList();
bool write = argList.Remove("--write");
if (argList.Count < 2) { Console.Error.WriteLine("usage: fix_mod_defaults.cs -- <packsDir> <tracksJson> [--write]"); return 1; }
string packsDir = argList[0], tracksPath = argList[1];

var len = new Dictionary<string, int>(StringComparer.Ordinal);
var name = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var t in JsonNode.Parse(File.ReadAllText(tracksPath))!["tracks"]!.AsArray())
{
    string id = (string)t!["id"]!;
    len[id] = (int)t["lengthMeters"]!;
    name[id] = (string?)t["trackName"] ?? id;
}

// pack, round, baseTag, altMod, altIsRealVenue, realKm, histLaps, realVenue
var fixes = new (string Pack, int Round, string Base, string Alt, bool AltReal, string KmStr, int Hist, string Venue)[]
{
    ("f1-1985", 11, "hockenheim_1988",  "Heusden",                    false, "297.6", 70, "Circuit Zandvoort"),
    ("f1-2008", 3,  "jerez_2019",       "emirates_raceway_gp",        false, "308.5", 57, "Bahrain International Circuit"),
    ("f1-2008", 16, "kansai_gp",        "fuji",                       true,  "305.7", 67, "Fuji Speedway"),
    ("f1-2008", 17, "spielberg_gp",     "emirates_raceway_gp",        false, "305.3", 56, "Shanghai International Circuit"),
    ("f1-2016", 18, "donington_gp",     "circuit_of_the_americas_gp", true,  "308.7", 56, "Circuit of the Americas"),
};

static string Pretty(string trackName) => trackName.Replace('_', ' ');
static int DistLaps(double km, int metres) => Math.Max(1, (int)Math.Round(km * 1000.0 / metres, MidpointRounding.AwayFromZero));

foreach (var byPack in fixes.GroupBy(f => f.Pack))
{
    string seasonPath = Path.Combine(packsDir, byPack.Key, "season.json");
    var doc = JsonNode.Parse(File.ReadAllText(seasonPath))!;
    var rounds = doc["rounds"]!.AsArray();

    foreach (var f in byPack)
    {
        if (!len.ContainsKey(f.Base)) { Console.Error.WriteLine($"  {f.Pack} R{f.Round}: base '{f.Base}' not in library"); continue; }
        if (!len.ContainsKey(f.Alt)) { Console.Error.WriteLine($"  {f.Pack} R{f.Round}: alt '{f.Alt}' not in library"); continue; }
        var round = rounds.FirstOrDefault(r => (int)r!["round"]! == f.Round) as JsonObject;
        if (round is null) { Console.Error.WriteLine($"  {f.Pack} R{f.Round}: not found"); continue; }

        double km = double.Parse(f.KmStr, CultureInfo.InvariantCulture);
        int origLaps = (int)round["laps"]!;
        int baseLaps = DistLaps(km, len[f.Base]);
        int altLaps = f.AltReal ? origLaps : DistLaps(km, len[f.Alt]);

        var track = (JsonObject)round["track"]!;
        track["id"] = f.Base;
        track["isPlaceholder"] = true;
        round["laps"] = baseLaps;
        track["alternate"] = new JsonObject { ["id"] = f.Alt, ["laps"] = altLaps, ["isRealVenue"] = f.AltReal };

        string note = $"Placeholder for {f.Venue} (not in AMS2 base) — {f.Hist} laps / {f.KmStr} km " +
                      $"reproduced as {baseLaps} laps of {Pretty(name[f.Base])}." +
                      (f.AltReal
                          ? $" The real venue is available as an optional mod track ({f.Alt}); enable alternate tracks to race it at its authentic {origLaps} laps."
                          : $" Optional mod alternate: {f.Alt}.");
        var guide = round["setupGuide"] as JsonObject;
        if (guide is not null) guide["notes"] = note;

        Console.WriteLine($"  {f.Pack} R{f.Round,-2} default {f.Base,-16} {baseLaps} laps | alt {f.Alt,-28} {altLaps} laps {(f.AltReal ? "[real-venue]" : "[filler]")}");
    }

    if (write) WriteJson(seasonPath, doc);
    Console.WriteLine($"  == {byPack.Key}{(write ? " WRITTEN" : "")} ==");
}
if (!write) Console.WriteLine("\n(dry run — pass --write to apply)");
return 0;

static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    json = json.Replace("\r\n", "\n").Replace("\n", "\r\n");
    File.WriteAllText(path, json + "\r\n", new UTF8Encoding(false));
}
