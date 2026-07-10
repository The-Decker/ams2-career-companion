// MAX-GRID pass (Mike's rule: "who's in the season = who's in the skinpack; no 10-car Kyalami"):
// regenerate every round's grid to the FULL cap-aware roster —
//
//   grid.starterDriverIds <- every entry whose rounds range covers the round (full roster; the
//                            resolver's CapToGridSize trims the slowest at staging when the list
//                            exceeds grid.size, and the player always keeps their seat)
//   grid.size             <- min(class livery cap, covering seats)  (+ guests ride separately)
//   setupGuide.opponents  <- grid.size - 1                          (the validator's contract)
//
// Optionally verifies every entry's ams2LiveryName against the skinpack extract XMLs
// (LIVERY_OVERRIDE NAME attributes, INCLUDING commented alternates — the app can activate those
// now) so a name drift between pack and skinpack is caught before it ships.
//
// Usage:
//   dotnet run tools/max_grid.cs -- <packDir> <cap> [--names <extractDir>]... [--write]
//   (no --write => dry run: prints the plan, writes nothing)

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var argList = args.ToList();
bool write = argList.Remove("--write");
var nameDirs = new List<string>();
for (int i = argList.IndexOf("--names"); i >= 0; i = argList.IndexOf("--names"))
{
    if (i + 1 >= argList.Count) { Console.Error.WriteLine("--names needs a directory"); return 1; }
    nameDirs.Add(argList[i + 1]);
    argList.RemoveRange(i, 2);
}
if (argList.Count < 2)
{
    Console.Error.WriteLine("usage: max_grid.cs -- <packDir> <cap> [--names <extractDir>]... [--write]");
    return 1;
}
string packDir = argList[0];
int cap = int.Parse(argList[1], CultureInfo.InvariantCulture);

// ---- entries + rounds ranges ----------------------------------------------
var entriesDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "entries.json")))!;
var seasonDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "season.json")))!;

var entries = new List<(string DriverId, string Livery, string Rounds)>();
foreach (var e in entriesDoc["entries"]!.AsArray())
    entries.Add(((string)e!["driverId"]!, (string)e["ams2LiveryName"]!, (string)e["rounds"]!));

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

// ---- optional livery-name verification -------------------------------------
if (nameDirs.Count > 0)
{
    var available = new HashSet<string>(StringComparer.Ordinal);
    var nameAttr = new Regex(@"<\s*LIVERY_OVERRIDE\b[^>]*?\bNAME\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    foreach (var dir in nameDirs)
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
            foreach (Match m in nameAttr.Matches(File.ReadAllText(file)))
                if (m.Groups[1].Value.Length > 0)
                    available.Add(m.Groups[1].Value);

    Console.WriteLine($"-- livery verification ({available.Count} names in the skinpack extracts) --");
    int missing = 0;
    foreach (var (driverId, livery, _) in entries)
        if (!available.Contains(livery))
        {
            Console.WriteLine($"  [NOT IN SKINPACK] {driverId}: \"{livery}\"");
            missing++;
        }
    Console.WriteLine(missing == 0
        ? "  every entry livery exists in the skinpack.\n"
        : $"  {missing} entr(y/ies) bind names the skinpack does not carry.\n");
}

// ---- plan + apply -----------------------------------------------------------
Console.WriteLine($"-- max-grid plan (cap {cap}) --");
int changed = 0;
foreach (var r in seasonDoc["rounds"]!.AsArray())
{
    var node = (JsonObject)r!;
    int roundNo = (int)node["round"]!;

    var covering = new List<string>();
    var liveriesSeen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (driverId, livery, rangeExpr) in entries)
    {
        if (!Covers(rangeExpr, roundNo))
            continue;
        if (!liveriesSeen.Add(livery))
        {
            Console.WriteLine($"  [WARN] R{roundNo}: livery \"{livery}\" covered by more than one entry — keeping the first.");
            continue;
        }
        covering.Add(driverId);
    }

    int size = Math.Min(cap, covering.Count);
    int oldSize = node["grid"]?["size"] is { } s ? (int)s : 0;
    int oldStarters = node["grid"]?["starterDriverIds"] is JsonArray sa ? sa.Count : 0;

    string note = covering.Count > cap ? $" (roster {covering.Count} > cap; resolver trims slowest)" : "";
    Console.WriteLine($"  R{roundNo}: size {oldSize}->{size}, starters {oldStarters}->{covering.Count}{note}");
    if (oldSize != size || oldStarters != covering.Count) changed++;

    if (!write)
        continue;

    var grid = node["grid"] as JsonObject;
    if (grid is null)
        node["grid"] = grid = new JsonObject();
    grid["size"] = size;
    var arr = new JsonArray();
    foreach (var id in covering) arr.Add(id);
    grid["starterDriverIds"] = arr;

    if (node["setupGuide"]?["session"] is JsonObject session && session["opponents"] is not null)
        session["opponents"] = size - 1;
}

if (!write)
{
    Console.WriteLine($"\n({changed} round(s) would change — pass --write to apply)");
    return 0;
}

WriteJson(Path.Combine(packDir, "season.json"), seasonDoc);
Console.WriteLine($"\nwritten: season.json ({changed} round(s) changed)");
return 0;

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract.
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        .Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
    File.WriteAllText(path, json, new UTF8Encoding(false));
}
