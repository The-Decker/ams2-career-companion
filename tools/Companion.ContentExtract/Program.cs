// Companion.ContentExtract — regenerates data/ams2/vehicles.json + classes.json from a local
// AMS2 install by parsing every Vehicles\<dir>\*.crd (plain XML; see CLAUDE.md).
// tracks.json and liveries.json have separate sources and are NOT touched here.
//
// One id = one vehicle. The install genuinely ships duplicate .crd basenames — e.g.
// stock_corolla_23.crd exists in BOTH Vehicles\stock_corolla\ (a leftover that internally
// references Stock_Corolla_21 assets) and Vehicles\stock_corolla_23\ — so emitting every .crd
// verbatim produces duplicate ids and crashes Ams2ContentLibrary.Load. The copy whose parent
// folder matches the .crd basename wins; if no copy is dir-named, the lexicographically first
// folder wins. Every dropped copy is reported.
//
// Usage: Companion.ContentExtract <ams2InstallDir> <outDir> [extractedFromLabel]
//   ams2InstallDir = e.g. Y:\SteamLibrary\steamapps\common\Automobilista 2
//   outDir         = repo data/ams2
//   label          = optional provenance override; default is derived from the Steam
//                    appmanifest buildid next to the install plus today's date.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Companion.Ams2.ContentLibrary;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: Companion.ContentExtract <ams2InstallDir> <outDir> [extractedFromLabel]");
    return 2;
}

string installDir = Path.GetFullPath(args[0]);
string outDir = Path.GetFullPath(args[1]);
string vehiclesRoot = Path.Combine(installDir, "Vehicles");

if (!Directory.Exists(vehiclesRoot))
{
    Console.Error.WriteLine($"'{vehiclesRoot}' does not exist — not an AMS2 install?");
    return 2;
}

string extractedFrom = args.Length == 3
    ? args[2]
    : $"AMS2 build {SteamBuildId(installDir)}, {DateTime.Now:yyyy-MM-dd} (all .crd files incl. engine/track-config variants)";

// -- parse every .crd -------------------------------------------------------------------------

var parsed = new List<Ams2Vehicle>();
int crdCount = 0, failures = 0;

foreach (string dir in Directory.EnumerateDirectories(vehiclesRoot).Order(StringComparer.Ordinal))
{
    foreach (string crd in Directory.EnumerateFiles(dir, "*.crd").Order(StringComparer.Ordinal))
    {
        crdCount++;
        try
        {
            parsed.Add(ParseCrd(crd));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PARSE FAILURE {crd}: {ex.Message}");
            failures++;
        }
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} of {crdCount} .crd files failed to parse; aborting (no files written).");
    return 1;
}
if (parsed.Count == 0)
{
    Console.Error.WriteLine($"no .crd files found under '{vehiclesRoot}'; aborting.");
    return 1;
}

// -- deduplicate ids: dir-named copy wins -----------------------------------------------------

var vehicles = new List<Ams2Vehicle>();
foreach (var group in parsed.GroupBy(v => v.Id, StringComparer.Ordinal))
{
    var copies = group.OrderBy(v => v.Dir, StringComparer.Ordinal).ToList();
    var winner = copies.FirstOrDefault(v => string.Equals(v.Dir, v.Id, StringComparison.OrdinalIgnoreCase))
                 ?? copies[0];
    vehicles.Add(winner);
    foreach (var dropped in copies.Where(v => !ReferenceEquals(v, winner)))
        Console.WriteLine($"duplicate id '{group.Key}': kept Vehicles\\{winner.Dir}\\, dropped leftover copy in Vehicles\\{dropped.Dir}\\");
}
vehicles.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

// -- derive classes ---------------------------------------------------------------------------

var classes = vehicles
    .GroupBy(v => v.VehicleClass, StringComparer.Ordinal)
    .Select(g => new Ams2Class
    {
        XmlName = g.Key,
        VehicleCount = g.Count(),
        Years = [g.Min(v => v.Year), g.Max(v => v.Year)],
        Vehicles = g.Select(v => v.Id).Order(StringComparer.Ordinal).ToList(),
    })
    .OrderBy(c => c.XmlName, StringComparer.Ordinal)
    .ToList();

// -- emit -------------------------------------------------------------------------------------

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

Directory.CreateDirectory(outDir);
WriteJson(Path.Combine(outDir, "vehicles.json"), new { extractedFrom, vehicles });
WriteJson(Path.Combine(outDir, "classes.json"), new { extractedFrom, classes });

Console.WriteLine($"{crdCount} .crd files -> {vehicles.Count} vehicles, {classes.Count} classes ({extractedFrom})");
return 0;

// ---------------------------------------------------------------------------------------------

static Ams2Vehicle ParseCrd(string crdPath)
{
    var doc = XDocument.Load(crdPath);
    var data = doc.Root!.Elements("data")
                   .FirstOrDefault(e => (string?)e.Attribute("class") == "VehicleDetails")
               ?? throw new InvalidDataException("no <data class=\"VehicleDetails\"> element");

    var props = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var prop in data.Elements("prop"))
    {
        string? name = (string?)prop.Attribute("name");
        string? value = (string?)prop.Attribute("data");
        if (name is not null && value is not null && !props.ContainsKey(name))
            props[name] = value;
    }

    return new Ams2Vehicle
    {
        Id = Path.GetFileNameWithoutExtension(crdPath),
        Dir = Path.GetFileName(Path.GetDirectoryName(crdPath))!,
        Name = props.GetValueOrDefault("Name"),
        VehicleName = props.GetValueOrDefault("Vehicle Name"),
        Manufacturer = props.GetValueOrDefault("VehicleManufacturer"),
        Model = props.GetValueOrDefault("VehicleModel"),
        Year = IntProp(props, "Vehicle Year"),
        VehicleClass = props.GetValueOrDefault("Vehicle Class")
                       ?? throw new InvalidDataException("missing 'Vehicle Class' prop"),
        Group = props.GetValueOrDefault("Vehicle Group"),
        AiOnly = BoolProp(props, "AI ONLY"),
        IsOpenWheeler = BoolProp(props, "Is Open Wheeler"),
        PerformanceIndex = IntProp(props, "Vehicle Initial Performance Index"),
    };
}

static int IntProp(Dictionary<string, string> props, string name) =>
    props.TryGetValue(name, out string? v)
        ? int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture)
        : 0;

static bool BoolProp(Dictionary<string, string> props, string name) =>
    props.TryGetValue(name, out string? v) && bool.Parse(v);

static string SteamBuildId(string installDir)
{
    // <lib>\steamapps\common\Automobilista 2 -> <lib>\steamapps\appmanifest_1066890.acf
    string manifest = Path.GetFullPath(Path.Combine(installDir, "..", "..", "appmanifest_1066890.acf"));
    if (!File.Exists(manifest)) return "unknown";
    var match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
    return match.Success ? match.Groups[1].Value : "unknown";
}

void WriteJson(string path, object payload) =>
    File.WriteAllText(path, JsonSerializer.Serialize(payload, jsonOptions) + Environment.NewLine);
