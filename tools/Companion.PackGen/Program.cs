// Companion.PackGen — generates the bundled reference season packs (F1 1967, F1 1969, F1 1988)
// as plain-JSON pack folders per docs/dev/season-pack-format.md (format v1 + the v1.1
// placeholder-venue addendum: every round carries realVenue/isPlaceholder, and placeholder
// rounds recompute laps to preserve the REAL race distance at the stand-in's lap length).
//
// Sources reconciled:
//   - f1db SQLite release (CC BY 4.0): season calendar (round/GP/date/real laps), the circuit
//     per race (realVenue + historical distance; race.distance is km), and entrants
//     (driver <-> constructor <-> rounds).
//   - Installed community custom-AI XMLs (jusk et al.): the PROVEN livery names + AI ratings
//     that bind against the deployed skinpacks on this machine, including per-track
//     (tracks="...") override entries carried into round aiOverrides where configured.
//   - data/ams2/{tracks,vehicles,classes}.json: content library used to verify every id emitted.
//   - data/rules/f1-points-systems.json: pointsSystem copied VERBATIM per season.
//   - data/rules/placeholder-venues.json: the single source of truth for placeholder stand-in
//     selection (first suggestion present in the track library wins; era overrides re-rank).
//
// Usage: Companion.PackGen <f1db.db> <ams2DataDir> <customAiDir> <outDir>
//   ams2DataDir  = repo data/ams2 (rules are resolved as ../rules relative to it)
//   customAiDir  = <AMS2 install>\UserData\CustomAIDrivers (livery override folders are resolved
//                  as ..\..\Vehicles\Textures\CustomLiveries\Overrides relative to it)

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

if (args.Length != 4)
{
    Console.Error.WriteLine("usage: Companion.PackGen <f1db.db> <ams2DataDir> <customAiDir> <outDir>");
    return 2;
}

string dbPath = Path.GetFullPath(args[0]);
string ams2DataDir = Path.GetFullPath(args[1]);
string customAiDir = Path.GetFullPath(args[2]);
string outDir = Path.GetFullPath(args[3]);

string rulesPath = Path.GetFullPath(Path.Combine(ams2DataDir, "..", "rules", "f1-points-systems.json"));
string overridesRoot = Path.GetFullPath(Path.Combine(customAiDir, "..", "..", "Vehicles", "Textures", "CustomLiveries", "Overrides"));

string placeholderVenuesPath = Path.GetFullPath(Path.Combine(ams2DataDir, "..", "rules", "placeholder-venues.json"));

var library = ContentLibrary.Load(ams2DataDir);
var rules = JsonNode.Parse(File.ReadAllText(rulesPath))!.AsObject();
var placeholderVenues = PlaceholderVenues.Load(placeholderVenuesPath, library);

var seasons = new[] { SeasonConfigs.F1_1967(), SeasonConfigs.F1_1969(), SeasonConfigs.F1_1988() };
int exitCode = 0;

foreach (var cfg in seasons)
{
    try
    {
        var report = PackBuilder.Build(cfg, dbPath, customAiDir, overridesRoot, library, rules, placeholderVenues, outDir);
        report.Print();
        if (report.HasErrors) exitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{cfg.PackId}] FAILED: {ex.Message}");
        exitCode = 1;
    }
}

return exitCode;

// ---------------------------------------------------------------------------------------------

/// <summary>Authored AMS2 binding for one grand prix. Non-placeholder bindings name the track
/// (the venue exists in AMS2, possibly as an era-different layout — NOT a placeholder; such
/// rounds keep the historical lap count because the venue is real and only the layout evolved).
/// Placeholder bindings (<see cref="IsPlaceholder"/> true, <see cref="TrackId"/> null) resolve
/// the stand-in from data/rules/placeholder-venues.json and recompute laps to preserve the real
/// race distance; <see cref="Note"/> is appended after the generated substitution sentence.</summary>
internal sealed record TrackBinding(string? TrackId, string[] Fallbacks, string Note, bool IsPlaceholder = false);

internal sealed record TeamMeta(int Prestige, int BudgetTier);

internal sealed class SeasonConfig
{
    public required int Year { get; init; }
    public required string PackId { get; init; }
    public required string PackName { get; init; }
    public required string SeriesName { get; init; }
    public required string Ams2Class { get; init; }
    public required string AiXmlFileName { get; init; }
    public required string SkinPackName { get; init; }
    public required string OverridesFolder { get; init; }
    public required string[] Attribution { get; init; }
    /// <summary>grandPrixId -> AMS2 track binding (all ids verified against tracks.json).</summary>
    public required Dictionary<string, TrackBinding> Tracks { get; init; }
    /// <summary>constructorId -> authored prestige/budget (1-5).</summary>
    public required Dictionary<string, TeamMeta> TeamMetas { get; init; }
    /// <summary>true = team display name comes from the livery prefix ("Brabham-Repco");
    /// false = "&lt;Constructor&gt;-&lt;Engine&gt;" composed from f1db.</summary>
    public required bool TeamNameFromLivery { get; init; }
    /// <summary>Vehicle model folders (relative to Overrides root) scanned to bind teams to car models.</summary>
    public required string[] ModelFolders { get; init; }
    /// <summary>true = DDS files are named &lt;Driver&gt;_&lt;number&gt;.dds and the car number picks the model;
    /// false = DDS files are named &lt;TeamPrefix&gt;&lt;number&gt;*.dds and the team prefix picks the model.</summary>
    public required bool ModelMapByNumber { get; init; }
    /// <summary>Authored fallback when the override folders cannot be scanned. Key = number (by-number
    /// strategy) or normalized team token (by-prefix strategy); value = vehicle id.</summary>
    public required Dictionary<string, string> ModelMapFallback { get; init; }

    /// <summary>f1db driver id -> authored country correction (IOC code) with the reason kept
    /// beside the value. Applied over the community XML's country; every application is
    /// documented in pack.json notes.</summary>
    public Dictionary<string, (string Country, string Reason)> CountryCorrections { get; init; } = new();

    /// <summary>true = carry the source XML's per-track (tracks="...") entries into the matching
    /// rounds' aiOverrides as partial ratings patches (absolute 0-1 values, same vocabulary).</summary>
    public bool CarryPerTrackOverrides { get; init; }

    /// <summary>Override-XML track id -> grand prix id, for per-track entries authored against a
    /// layout the calendar does not use (e.g. jusk's Azure_Circuit_2021 patches belong to the
    /// Monaco round even though the pack drives azure_circuit_88 — same venue).</summary>
    public Dictionary<string, string> OverrideTrackAliases { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Per-round variant XMLs beside the base file (jusk's F-Vintage_Gen2_01Kyalami.xml
    /// style), each mapped to a grand prix id. Every variant is mined the way the base file's
    /// tracks="..." entries are: rating values that DIFFER from the season baseline become that
    /// round's aiOverrides (patch vocabulary; anything else is reported as dropped), and a
    /// pack.json note documents the file -> round mapping and whether the variant turned out to
    /// be composition-only (a driver subset with no rating changes).</summary>
    public (string FileName, string GpId)[] VariantFiles { get; init; } = [];

    /// <summary>Authored pack.json notes appended after the generated coverage/correction notes.</summary>
    public string[] ExtraNotes { get; init; } = [];
}

internal static class SeasonConfigs
{
    public static SeasonConfig F1_1967() => new()
    {
        Year = 1967,
        PackId = "f1-1967",
        PackName = "Formula One 1967",
        SeriesName = "Formula One World Championship",
        Ams2Class = "F-Vintage_Gen1",
        AiXmlFileName = "F-Vintage_Gen1.xml",
        SkinPackName = "F1 1967 Season (Alain Fry)",
        OverridesFolder = "F1_Season_1967",
        Attribution =
        [
            "Historical data derived from f1db (github.com/f1db/f1db, CC BY 4.0)",
            "AI ratings from 'Custom AI by jusk - F1 1967 Season' (F-Vintage_Gen1.xml), matching Alain Fry's F1 1967 skinpack",
        ],
        Tracks = new()
        {
            ["south-africa"] = new("kyalami_historic", ["kyalami_2019"], ""),
            ["monaco"] = new("azure_circuit_88", ["azure_circuit"], ""),
            ["netherlands"] = new(null, [], "", IsPlaceholder: true),
            ["belgium"] = new("spa-francorchamps_1970", [], ""),
            ["france"] = new("le_mans_bugatti", ["rouen"],
                "The 1967 French GP ran at the Bugatti Circuit, Le Mans — AMS2's Le Mans Bugatti is the modern layout of the SAME venue, so this is not a placeholder and the historical lap count is kept; era-correct Rouen-les-Essarts is the fallback."),
            ["great-britain"] = new("silverstone_1975nc", ["silverstone_1975"], ""),
            ["germany"] = new("nurb_1971_nords", [], ""),
            ["canada"] = new("mosport_1971", [], ""),
            ["italy"] = new("monza_1971", [], ""),
            ["united-states"] = new("watkins_glen_1971_short", [],
                "AMS2's Watkins Glen layouts are 1971+; the venue is real, so the historical lap count is kept on the era-different layout."),
            ["mexico"] = new(null, [], "", IsPlaceholder: true),
        },
        TeamMetas = new()
        {
            ["ferrari"] = new(5, 5),
            ["lotus"] = new(5, 4),
            ["brabham"] = new(5, 4),
            ["brm"] = new(4, 4),
            ["cooper"] = new(4, 3),
            ["honda"] = new(3, 4),
            ["eagle"] = new(3, 3),
            ["matra"] = new(2, 3),
            ["mclaren"] = new(2, 2),
            ["lola"] = new(1, 2),
        },
        TeamNameFromLivery = true,
        ModelFolders = ["formula_vintage_g1m1", "formula_vintage_g1m2"],
        ModelMapByNumber = true,
        ModelMapFallback = new()
        {
            // From the deployed F1_Season_1967 DDS sets (car number -> model folder).
            ["1"] = "formula_vintage_g1m1", ["2"] = "formula_vintage_g1m1", ["3"] = "formula_vintage_g1m1",
            ["4"] = "formula_vintage_g1m1", ["5"] = "formula_vintage_g1m1", ["6"] = "formula_vintage_g1m1",
            ["15"] = "formula_vintage_g1m1", ["17"] = "formula_vintage_g1m1", ["20"] = "formula_vintage_g1m1",
            ["29"] = "formula_vintage_g1m1",
            ["7"] = "formula_vintage_g1m2", ["8"] = "formula_vintage_g1m2", ["10"] = "formula_vintage_g1m2",
            ["11"] = "formula_vintage_g1m2", ["12"] = "formula_vintage_g1m2", ["14"] = "formula_vintage_g1m2",
            ["18"] = "formula_vintage_g1m2", ["19"] = "formula_vintage_g1m2", ["22"] = "formula_vintage_g1m2",
            ["30"] = "formula_vintage_g1m2",
        },
        CarryPerTrackOverrides = true,
        OverrideTrackAliases = new(StringComparer.Ordinal)
        {
            // jusk authored several 1967 per-track patches against layouts this calendar does
            // not drive; each maps to the calendar round at the SAME venue.
            ["azure_circuit_2021"] = "monaco",
            ["watkins_glen_s"] = "united-states",
            ["spa-francorchamps_1993"] = "belgium",
            ["nordschleife_2020"] = "germany",
            ["nordschleife_2020_24hr"] = "germany",
        },
        ExtraNotes =
        [
            "Per-track AI overrides from the source custom-AI XML (tracks=\"...\" entries) are carried into the matching rounds' aiOverrides as absolute partial ratings patches. Entries authored against layouts this calendar does not drive (Azure_Circuit_2021, Watkins_Glen_S, Spa_Francorchamps_1993, Nordschleife_2020/_24hr) are mapped to the calendar round at the same venue (Monaco, Watkins Glen, Spa, Nurburgring).",
        ],
    };

    public static SeasonConfig F1_1969() => new()
    {
        Year = 1969,
        PackId = "f1-1969",
        PackName = "Formula One 1969",
        SeriesName = "Formula One World Championship",
        Ams2Class = "F-Vintage_Gen2",
        AiXmlFileName = "F-Vintage_Gen2.xml",
        SkinPackName = "F1 1969 Season (Alain Fry)",
        OverridesFolder = "F1_Season_1969",
        Attribution =
        [
            "Historical data derived from f1db (github.com/f1db/f1db, CC BY 4.0)",
            "AI ratings from 'Custom AI by jusk - F1 1969 Season' (F-Vintage_Gen2.xml), matching Alain Fry's F1 1969 skinpack",
        ],
        Tracks = new()
        {
            ["south-africa"] = new("kyalami_historic", ["kyalami_2019"], ""),
            ["spain"] = new(null, [], "", IsPlaceholder: true),          // Montjuïc — not in AMS2
            ["monaco"] = new("azure_circuit_88", ["azure_circuit"], ""),
            ["netherlands"] = new(null, [], "", IsPlaceholder: true),    // Zandvoort — not in AMS2
            ["france"] = new(null, [], "", IsPlaceholder: true),         // Charade — not in AMS2
            ["great-britain"] = new("silverstone_1975nc", ["silverstone_1975"], ""),
            ["germany"] = new("nurb_1971_nords", [], ""),
            ["italy"] = new("monza_1971", [], ""),
            ["canada"] = new("mosport_1971", [], ""),
            ["united-states"] = new("watkins_glen_1971_short", [],
                "AMS2's Watkins Glen layouts are 1971+; the venue is real, so the historical lap count is kept on the era-different layout."),
            ["mexico"] = new(null, [], "", IsPlaceholder: true),         // Mexico City — not in AMS2
        },
        TeamMetas = new()
        {
            // 1969: Matra-Ford (Tyrrell-run) is the class of the field; Ferrari is in its
            // one-car crisis year (skipped rounds 7 and 11); BMW/Tecno are the German GP's
            // works F2 efforts.
            ["matra"] = new(4, 5),
            ["lotus"] = new(5, 4),
            ["brabham"] = new(4, 4),
            ["mclaren"] = new(3, 4),
            ["ferrari"] = new(5, 3),
            ["brm"] = new(4, 3),
            ["bmw"] = new(2, 3),
            ["tecno"] = new(1, 2),
        },
        TeamNameFromLivery = true,
        ModelFolders = ["brabham_bt26", "formula_vintage_g2m1", "formula_vintage_g2m2", "lotus_49c"],
        ModelMapByNumber = true,
        ModelMapFallback = new()
        {
            // From the deployed F1_Season_1969 DDS sets (car number -> model folder).
            ["3"] = "brabham_bt26", ["4"] = "brabham_bt26", ["29"] = "brabham_bt26",
            ["32"] = "brabham_bt26",
            ["5"] = "formula_vintage_g2m1", ["6"] = "formula_vintage_g2m1",
            ["7"] = "formula_vintage_g2m1", ["8"] = "formula_vintage_g2m1",
            ["18"] = "formula_vintage_g2m1", ["20"] = "formula_vintage_g2m1",
            ["26"] = "formula_vintage_g2m1", ["28"] = "formula_vintage_g2m1",
            ["11"] = "formula_vintage_g2m2", ["12"] = "formula_vintage_g2m2",
            ["14"] = "formula_vintage_g2m2", ["15"] = "formula_vintage_g2m2",
            ["16"] = "formula_vintage_g2m2", ["17"] = "formula_vintage_g2m2",
            ["19"] = "formula_vintage_g2m2", ["22"] = "formula_vintage_g2m2",
            ["24"] = "formula_vintage_g2m2", ["25"] = "formula_vintage_g2m2",
            ["1"] = "lotus_49c", ["2"] = "lotus_49c", ["9"] = "lotus_49c", ["10"] = "lotus_49c",
        },
        CarryPerTrackOverrides = true,
        OverrideTrackAliases = new(StringComparer.Ordinal)
        {
            // jusk's Monaco patches are authored against the 2021 layout; the calendar drives
            // azure_circuit_88 — same venue. Silverstone_1975_No_Chicane and Monza_1971 resolve
            // directly to calendar rounds and need no alias.
            ["azure_circuit_2021"] = "monaco",
        },
        VariantFiles =
        [
            ("F-Vintage_Gen2_01Kyalami.xml", "south-africa"),
            ("F-Vintage_Gen2_02Silverstone.xml", "great-britain"),
            ("F-Vintage_Gen2_03Nordschleiffe.xml", "germany"),
            ("F-Vintage_Gen2_04WatkinsGlen.xml", "united-states"),
        ],
        ExtraNotes =
        [
            "Per-track AI overrides from the source custom-AI XML (tracks=\"...\" entries) are carried into the matching rounds' aiOverrides as absolute partial ratings patches. The Azure_Circuit_2021 patches map to the Monaco round (this calendar drives azure_circuit_88 — same venue); Silverstone_1975_No_Chicane and Monza_1971 map directly to the British and Italian rounds.",
            "The source XML's Monza entry for 'Ferrari #11 C. Amon' (tracks=\"Monza_1971\") is jusk's driver swap to Ernesto 'Tino' Brambilla, who stood in for the departed Amon at the Italian GP; the pack format has no per-round driver swap, so it is carried as a ratings patch on driver.chris_amon (f1db lists Brambilla as a round-8 Ferrari entrant, noted uncovered above).",
            "Source variant files not mapped to a round: F-Vintage_Gen2_05Full.xml is byte-identical to the base F-Vintage_Gen2.xml (all 26 slots); F-Vintage_Gen2_06RegularF1.xml is jusk's default 18-regular grid for rounds without extras. Variant grid composition is playability-oriented (season regulars kept everywhere); this pack's per-round grids follow the f1db entry list via each entry's rounds range instead.",
        ],
    };

    public static SeasonConfig F1_1988() => new()
    {
        Year = 1988,
        PackId = "f1-1988",
        PackName = "Formula One 1988",
        SeriesName = "Formula One World Championship",
        Ams2Class = "F-Classic_Gen2",
        AiXmlFileName = "F-Classic_Gen2_1988.xml",
        SkinPackName = "F1 1988 Season (jusk)",
        OverridesFolder = "F1_Season_1988",
        Attribution =
        [
            "Historical data derived from f1db (github.com/f1db/f1db, CC BY 4.0)",
            "AI ratings from the community F-Classic_Gen2_1988.xml custom-AI set, matching the F1_Season_1988 skinpack (jusk)",
        ],
        Tracks = new()
        {
            ["brazil"] = new("jacarepagua_historic", [], ""),
            ["san-marino"] = new("imola_88", [], ""),
            ["monaco"] = new("azure_circuit_88", ["azure_circuit"], ""),
            ["mexico"] = new(null, [],
                "Interlagos 1991 keeps the period Latin-American venue character.", IsPlaceholder: true),
            ["canada"] = new("montrealhistoric", [], ""),
            ["detroit"] = new(null, [],
                "Long Beach hosted the United States GP West 1976-1983 — the era's US street-race character.", IsPlaceholder: true),
            ["france"] = new(null, [],
                "Keeps the French GP at a French Grand Prix venue.", IsPlaceholder: true),
            ["great-britain"] = new("silverstone_1991", ["silverstone_1975"],
                "No 1988 Silverstone layout in AMS2; the venue is real, so the historical lap count is kept on the 1991 layout (Silverstone 1975 with chicane is the era-closest alternative)."),
            ["germany"] = new("hockenheim_1988", [], ""),
            ["hungary"] = new("hungaroring_gp_2025", [],
                "AMS2 ships the 2025 Hungaroring; the venue is real and largely unchanged in character since 1986, so the historical lap count is kept."),
            ["belgium"] = new("spa-francorchamps_1993", [],
                "No 1988 Spa layout in AMS2; the venue is real, so the historical lap count is kept on the 1993 layout."),
            ["italy"] = new("monza_1991", [],
                "No 1988 Monza in AMS2; the venue is real, so the historical lap count is kept on the era-matching 1991 layout."),
            ["portugal"] = new("estoril_1988", [], ""),
            ["spain"] = new("jerez_1988", [], ""),
            ["japan"] = new("kansai_gp", [],
                "Suzuka appears in AMS2 as Kansai; the venue is real, so the historical lap count is kept on the modern GP layout."),
            ["australia"] = new("adelaide_historic", [], ""),
        },
        TeamMetas = new()
        {
            ["mclaren"] = new(5, 5),
            ["ferrari"] = new(5, 5),
            ["lotus"] = new(4, 4),
            ["williams"] = new(4, 3),
            ["benetton"] = new(3, 4),
            ["march"] = new(3, 3),
            ["arrows"] = new(3, 3),
            ["tyrrell"] = new(3, 2),
            ["ligier"] = new(2, 3),
            ["minardi"] = new(2, 2),
            ["zakspeed"] = new(2, 2),
            ["lola"] = new(2, 2),
            ["rial"] = new(1, 2),
            ["osella"] = new(1, 1),
            ["ags"] = new(1, 1),
            ["coloni"] = new(1, 1),
            ["eurobrun"] = new(1, 1),
        },
        TeamNameFromLivery = false,
        ModelFolders = ["formula_classic_g2m1", "formula_classic_g2m2", "formula_classic_g2m3", "mclaren_mp44"],
        ModelMapByNumber = false,
        ModelMapFallback = new()
        {
            // From the deployed F1_Season_1988 DDS sets (team prefix -> model folder).
            ["benetton"] = "formula_classic_g2m1", ["march"] = "formula_classic_g2m1",
            ["osella"] = "formula_classic_g2m1", ["tyrrell"] = "formula_classic_g2m1",
            ["williams"] = "formula_classic_g2m1",
            ["ags"] = "formula_classic_g2m2", ["coloni"] = "formula_classic_g2m2",
            ["eurobrun"] = "formula_classic_g2m2", ["ligier"] = "formula_classic_g2m2",
            ["lola"] = "formula_classic_g2m2", ["minardi"] = "formula_classic_g2m2",
            ["rial"] = "formula_classic_g2m2",
            ["arrows"] = "formula_classic_g2m3", ["ferrari"] = "formula_classic_g2m3",
            ["lotus"] = "formula_classic_g2m3", ["zakspeed"] = "formula_classic_g2m3",
            ["mclaren"] = "mclaren_mp44",
        },
        CountryCorrections = new(StringComparer.Ordinal)
        {
            // Round-1 verification finding: the community XML lists Martini as SWE.
            ["pierluigi-martini"] = ("ITA", "Martini is Italian (f1db nationality: italy); the source XML's SWE is a data-entry error"),
        },
        ExtraNotes =
        [
            "The source custom-AI XML carries a large per-track (tracks=\"...\") override set; those patches are not carried into this pack's aiOverrides in this version.",
        ],
    };
}

// ---------------------------------------------------------------------------------------------

internal sealed class ContentLibrary
{
    public required Dictionary<string, int> TrackMaxAi { get; init; }   // track id -> maxAiParticipants
    public required Dictionary<string, int> TrackLengthMeters { get; init; } // track id -> lap length (m)
    public required Dictionary<string, string> TrackNames { get; init; }     // track id -> trackName
    public required Dictionary<string, string> TrackIdByName { get; init; }  // lowercased trackName -> id
    public required HashSet<string> VehicleIds { get; init; }
    public required Dictionary<string, HashSet<string>> ClassVehicles { get; init; } // class xmlName -> vehicle ids

    /// <summary>Human display for setupGuide notes: the game's trackName with underscores
    /// opened up ("Spielberg_Vintage" -> "Spielberg Vintage").</summary>
    public string TrackDisplayName(string trackId) =>
        TrackNames.TryGetValue(trackId, out var name) && name.Length > 0 ? name.Replace('_', ' ') : trackId;

    public static ContentLibrary Load(string ams2DataDir)
    {
        var tracks = JsonNode.Parse(File.ReadAllText(Path.Combine(ams2DataDir, "tracks.json")))!;
        var trackMaxAi = new Dictionary<string, int>(StringComparer.Ordinal);
        var trackLength = new Dictionary<string, int>(StringComparer.Ordinal);
        var trackNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var trackIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in tracks["tracks"]!.AsArray())
        {
            string id = t!["id"]!.GetValue<string>();
            trackMaxAi[id] = t["maxAiParticipants"]?.GetValue<int>() ?? 0;
            trackLength[id] = t["lengthMeters"]?.GetValue<int>() ?? 0;
            string name = t["trackName"]?.GetValue<string>() ?? "";
            trackNames[id] = name;
            if (name.Length > 0) trackIdByName.TryAdd(name.ToLowerInvariant(), id);
        }

        var vehicles = JsonNode.Parse(File.ReadAllText(Path.Combine(ams2DataDir, "vehicles.json")))!;
        var vehicleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in vehicles["vehicles"]!.AsArray())
            vehicleIds.Add(v!["id"]!.GetValue<string>());

        var classes = JsonNode.Parse(File.ReadAllText(Path.Combine(ams2DataDir, "classes.json")))!;
        var classVehicles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var c in classes["classes"]!.AsArray())
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in c!["vehicles"]!.AsArray()) set.Add(v!.GetValue<string>());
            classVehicles[c["xmlName"]!.GetValue<string>()] = set;
        }

        return new ContentLibrary
        {
            TrackMaxAi = trackMaxAi,
            TrackLengthMeters = trackLength,
            TrackNames = trackNames,
            TrackIdByName = trackIdByName,
            VehicleIds = vehicleIds,
            ClassVehicles = classVehicles,
        };
    }
}

// ---------------------------------------------------------------------------------------------

/// <summary>data/rules/placeholder-venues.json — the single source of truth for placeholder
/// stand-in selection, keyed by f1db circuit id. Every suggestion id (base and era-override)
/// is validated against the track library at load: the curated file must never dangle.</summary>
internal sealed class PlaceholderVenues
{
    private sealed record Venue(string Name, string[] Suggestions, List<(int FromYear, string[] Suggestions)> EraOverrides);

    private readonly Dictionary<string, Venue> _venues;

    private PlaceholderVenues(Dictionary<string, Venue> venues) => _venues = venues;

    public static PlaceholderVenues Load(string path, ContentLibrary library)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var venues = new Dictionary<string, Venue>(StringComparer.Ordinal);
        foreach (var (circuitId, node) in root["venues"]!.AsObject())
        {
            var obj = node!.AsObject();
            var suggestions = obj["suggestions"]!.AsArray().Select(s => s!.GetValue<string>()).ToArray();
            var eras = new List<(int, string[])>();
            if (obj["eraOverrides"] is JsonArray eraArr)
            {
                foreach (var era in eraArr)
                    eras.Add((era!["fromYear"]!.GetValue<int>(),
                        era["suggestions"]!.AsArray().Select(s => s!.GetValue<string>()).ToArray()));
            }

            foreach (var id in suggestions.Concat(eras.SelectMany(e => e.Item2)))
                if (!library.TrackMaxAi.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"placeholder-venues.json: '{circuitId}' suggestion '{id}' is not in tracks.json");

            venues[circuitId] = new Venue(obj["name"]!.GetValue<string>(), suggestions, eras);
        }
        return new PlaceholderVenues(venues);
    }

    /// <summary>Ranked stand-in suggestions for a venue as seen from <paramref name="year"/>:
    /// the era override with the greatest fromYear &lt;= year wins, else the base list.</summary>
    public string[] SuggestionsFor(string circuitId, int year)
    {
        if (!_venues.TryGetValue(circuitId, out var venue))
            throw new InvalidOperationException(
                $"no placeholder-venues.json entry for f1db circuit '{circuitId}' — add one (it is the single source of truth for stand-ins)");
        var era = venue.EraOverrides
            .Where(e => e.FromYear <= year)
            .OrderByDescending(e => e.FromYear)
            .Select(e => e.Suggestions)
            .FirstOrDefault();
        return era ?? venue.Suggestions;
    }
}

internal sealed record AiDriver(
    string LiveryName, string TeamToken, string? Number, string Name, string Country,
    List<KeyValuePair<string, double>> Ratings, double? VehicleReliability);

/// <summary>A per-track (tracks="...") override entry: an absolute partial ratings patch for
/// the driver bound to <paramref name="LiveryName"/> at the named track layouts. Fields the
/// pack's aiOverrides patch vocabulary lacks arrive in <paramref name="DroppedFields"/>.</summary>
internal sealed record AiTrackOverride(
    string LiveryName, string[] Tracks,
    List<KeyValuePair<string, double>> Ratings, List<string> DroppedFields);

internal static class AiXml
{
    // Canonical rating order for drivers.json (custom-AI vocabulary, camelCased).
    private static readonly string[] RatingOrder =
    [
        "raceSkill", "qualifyingSkill", "aggression", "defending", "stamina", "consistency",
        "startReactions", "wetSkill", "tyreManagement", "fuelManagement", "blueFlagConceding",
        "weatherTyreChanges", "avoidanceOfMistakes", "avoidanceOfForcedMistakes",
    ];

    // The subset the pack aiOverrides patch type (PackRatingsPatch) supports, in canonical order.
    private static readonly string[] PatchRatingOrder =
    [
        "raceSkill", "qualifyingSkill", "aggression", "defending", "stamina", "consistency",
        "startReactions", "wetSkill", "tyreManagement", "avoidanceOfMistakes",
    ];

    /// <summary>True when the camelCased rating is representable in the pack aiOverrides
    /// patch vocabulary (used by the per-round variant-file miner).</summary>
    public static bool IsPatchRating(string camelKey) =>
        PatchRatingOrder.Contains(camelKey, StringComparer.Ordinal);

    /// <summary>Parses a community custom-AI XML leniently (comments stripped first: several
    /// community files contain '--' inside comments, which strict XML rejects) and returns the
    /// base driver entries (elements carrying a &lt;name&gt; and no tracks= attribute).</summary>
    public static List<AiDriver> ParseBaseDrivers(string xmlPath)
    {
        string text = File.ReadAllText(xmlPath);
        text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
        var doc = XDocument.Parse(text);

        var result = new List<AiDriver>();
        foreach (var el in doc.Root!.Elements("driver"))
        {
            if (el.Attribute("tracks") is not null) continue;      // per-track override entry
            string? name = el.Element("name")?.Value.Trim();
            if (string.IsNullOrEmpty(name)) continue;               // not a base entry

            string livery = el.Attribute("livery_name")?.Value ?? "";
            var m = Regex.Match(livery, @"^(?:\d{4}\s+)?(?<team>.+?)\s*#(?<num>\d+)");
            string teamToken = m.Success ? m.Groups["team"].Value.Trim() : livery;
            string? number = m.Success ? m.Groups["num"].Value.TrimStart('0') : null;
            if (number is "") number = "0";

            var ratings = new Dictionary<string, double>(StringComparer.Ordinal);
            double? reliability = null;
            foreach (var child in el.Elements())
            {
                string key = child.Name.LocalName;
                if (key is "name" or "country") continue;
                if (!double.TryParse(child.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    continue;
                if (key == "vehicle_reliability") { reliability = value; continue; }
                ratings[SnakeToCamel(key)] = value;
            }

            var ordered = new List<KeyValuePair<string, double>>();
            foreach (var key in RatingOrder)
                if (ratings.Remove(key, out double v)) ordered.Add(new(key, v));
            foreach (var kv in ratings.OrderBy(k => k.Key, StringComparer.Ordinal))
                ordered.Add(kv); // unknown extras, appended deterministically

            result.Add(new AiDriver(
                livery, teamToken, number, name,
                el.Element("country")?.Value.Trim() ?? "", ordered, reliability));
        }

        return result;
    }

    /// <summary>Parses the per-track override entries (elements with a tracks= attribute).
    /// Ratings are kept in the pack patch vocabulary (canonical order); anything else the
    /// patch type lacks (fuel_management, blue_flag_conceding, weather_tyre_changes,
    /// avoidance_of_forced_mistakes, vehicle_reliability) is reported as dropped.</summary>
    public static List<AiTrackOverride> ParsePerTrackOverrides(string xmlPath)
    {
        string text = File.ReadAllText(xmlPath);
        text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
        var doc = XDocument.Parse(text);

        var result = new List<AiTrackOverride>();
        foreach (var el in doc.Root!.Elements("driver"))
        {
            string? tracksAttr = el.Attribute("tracks")?.Value;
            if (string.IsNullOrWhiteSpace(tracksAttr)) continue;   // base entry
            string livery = el.Attribute("livery_name")?.Value ?? "";
            var tracks = tracksAttr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var ratings = new Dictionary<string, double>(StringComparer.Ordinal);
            var dropped = new List<string>();
            foreach (var child in el.Elements())
            {
                string key = child.Name.LocalName;
                if (key is "name" or "country") continue;
                if (!double.TryParse(child.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    continue;
                string camel = SnakeToCamel(key);
                if (PatchRatingOrder.Contains(camel, StringComparer.Ordinal)) ratings[camel] = value;
                else dropped.Add(camel);
            }

            var ordered = new List<KeyValuePair<string, double>>();
            foreach (var key in PatchRatingOrder)
                if (ratings.Remove(key, out double v)) ordered.Add(new(key, v));

            result.Add(new AiTrackOverride(livery, tracks, ordered, dropped));
        }
        return result;
    }

    private static string SnakeToCamel(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(parts[0]);
        foreach (var p in parts.Skip(1))
            sb.Append(char.ToUpperInvariant(p[0])).Append(p.AsSpan(1));
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------------------------

/// <summary>One f1db race. <paramref name="CircuitFullName"/> is the realVenue emitted on every
/// round; <paramref name="DistanceKm"/> is f1db race.distance (kilometres — verified: distance =
/// laps x course_length), with <paramref name="CourseLengthKm"/> kept for the laps-x-length
/// fallback when distance is absent.</summary>
internal sealed record RaceRow(
    int Round, string GpId, string GpName, string Date, int Laps,
    string CircuitId, string CircuitFullName, double? DistanceKm, double? CourseLengthKm);

internal sealed record EntrantRow(
    string ConstructorId, string ConstructorName, string EngineId,
    string DriverId, string DriverName, string DriverFullName, int BornYear, SortedSet<int> Rounds);

internal static class F1Db
{
    public static List<RaceRow> Races(SqliteConnection con, int year)
    {
        var list = new List<RaceRow>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT r.round, r.grand_prix_id, gp.full_name, r.date, r.laps,
                   r.circuit_id, c.full_name, r.distance, r.course_length
            FROM race r
            JOIN grand_prix gp ON gp.id = r.grand_prix_id
            JOIN circuit c ON c.id = r.circuit_id
            WHERE r.year = $y ORDER BY r.round
            """;
        cmd.Parameters.AddWithValue("$y", year);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new RaceRow(
                rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4),
                rd.GetString(5), rd.GetString(6),
                rd.IsDBNull(7) ? null : rd.GetDouble(7),
                rd.IsDBNull(8) ? null : rd.GetDouble(8)));
        return list;
    }

    public static List<EntrantRow> Entrants(SqliteConnection con, int year)
    {
        var list = new List<EntrantRow>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT sed.constructor_id, c.name, sed.engine_manufacturer_id,
                   sed.driver_id, d.name, d.full_name, d.date_of_birth, sed.rounds
            FROM season_entrant_driver sed
            JOIN driver d ON d.id = sed.driver_id
            JOIN constructor c ON c.id = sed.constructor_id
            WHERE sed.year = $y AND sed.test_driver = 0
            """;
        cmd.Parameters.AddWithValue("$y", year);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var rounds = new SortedSet<int>();
            if (!rd.IsDBNull(7))
                foreach (var part in rd.GetString(7).Split(';', StringSplitOptions.RemoveEmptyEntries))
                    rounds.Add(int.Parse(part, CultureInfo.InvariantCulture));
            int born = int.Parse(rd.GetString(6).AsSpan(0, 4), CultureInfo.InvariantCulture);
            list.Add(new EntrantRow(rd.GetString(0), rd.GetString(1), rd.GetString(2),
                rd.GetString(3), rd.GetString(4), rd.GetString(5), born, rounds));
        }
        return list;
    }
}

// ---------------------------------------------------------------------------------------------

internal sealed class PackReport
{
    public required string PackId { get; init; }
    public int Rounds, Teams, Drivers, Entries;
    public List<string> UnmatchedLiveries { get; } = [];
    public List<string> ExcludedLiveries { get; } = [];
    public List<string> UncoveredEntrants { get; } = [];
    public List<string> SubstitutedVenues { get; } = [];
    public List<string> PlaceholderRounds { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool HasErrors => UnmatchedLiveries.Count > 0;

    public void Print()
    {
        Console.WriteLine($"[{PackId}] rounds={Rounds} teams={Teams} drivers={Drivers} entries={Entries}");
        foreach (var s in PlaceholderRounds) Console.WriteLine($"[{PackId}] placeholder: {s}");
        foreach (var s in SubstitutedVenues) Console.WriteLine($"[{PackId}] substituted: {s}");
        foreach (var s in ExcludedLiveries) Console.WriteLine($"[{PackId}] excluded livery: {s}");
        foreach (var s in UnmatchedLiveries) Console.WriteLine($"[{PackId}] UNMATCHED livery: {s}");
        foreach (var s in UncoveredEntrants) Console.WriteLine($"[{PackId}] f1db entrant without livery: {s}");
        foreach (var s in Warnings) Console.WriteLine($"[{PackId}] warning: {s}");
    }
}

internal static class PackBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A calendar round with its AMS2 binding fully resolved: placeholder stand-ins
    /// picked from the curated file, laps recomputed to preserve the real distance, and the
    /// setupGuide note composed.</summary>
    private sealed record ResolvedRound(
        RaceRow Race, string TrackId, string[] Fallbacks, bool IsPlaceholder,
        string RealVenue, int Laps, string Note);

    public static PackReport Build(
        SeasonConfig cfg, string dbPath, string customAiDir, string overridesRoot,
        ContentLibrary library, JsonObject rules, PlaceholderVenues placeholderVenues, string outDir)
    {
        var report = new PackReport { PackId = cfg.PackId };
        var manifestNotes = new List<string>();

        // -- inputs -------------------------------------------------------------------------
        var aiDrivers = AiXml.ParseBaseDrivers(Path.Combine(customAiDir, cfg.AiXmlFileName));

        using var con = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        con.Open();
        var races = F1Db.Races(con, cfg.Year);
        var entrants = F1Db.Entrants(con, cfg.Year);

        if (!library.ClassVehicles.ContainsKey(cfg.Ams2Class))
            throw new InvalidOperationException($"class '{cfg.Ams2Class}' not found in classes.json");

        var pointsSystem = rules["seasons"]?[cfg.Year.ToString(CultureInfo.InvariantCulture)]
            ?? throw new InvalidOperationException($"no {cfg.Year} entry in f1-points-systems.json");

        // -- livery -> car model map (scan the deployed override DDS sets) --------------------
        var modelMap = ScanModelMap(cfg, overridesRoot, report);

        // -- reconcile AI XML liveries with f1db entrants -------------------------------------
        var byName = new Dictionary<string, List<EntrantRow>>(StringComparer.Ordinal);
        foreach (var row in entrants)
        {
            Add(byName, Norm(row.DriverName), row);
            if (Norm(row.DriverFullName) != Norm(row.DriverName)) Add(byName, Norm(row.DriverFullName), row);
        }

        var teams = new List<TeamAccumulator>();
        var teamsByConstructor = new Dictionary<string, TeamAccumulator>(StringComparer.Ordinal);
        var driversOut = new List<(string Id, string Name, string Country, int Born, List<KeyValuePair<string, double>> Ratings)>();
        var driverIdsEmitted = new HashSet<string>(StringComparer.Ordinal);
        var entriesOut = new List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)>();
        var pairsSeen = new HashSet<(string, string)>();
        var coveredRows = new HashSet<EntrantRow>();

        foreach (var ai in aiDrivers)
        {
            if (!byName.TryGetValue(Norm(ai.Name), out var rows))
            {
                report.UnmatchedLiveries.Add($"{ai.LiveryName} (driver '{ai.Name}' not in f1db {cfg.Year} entrants)");
                continue;
            }

            string teamNorm = Norm(ai.TeamToken);
            var chosen = rows
                .Where(r => teamNorm.Contains(Norm(r.ConstructorId)) || teamNorm.Contains(Norm(r.ConstructorName)))
                .ToList();
            if (chosen.Count == 0)
            {
                report.ExcludedLiveries.Add(
                    $"{ai.LiveryName} (no f1db {cfg.Year} entry for '{ai.Name}' at a constructor matching '{ai.TeamToken}' — treated as a what-if livery)");
                continue;
            }

            var constructorIds = chosen.Select(r => r.ConstructorId).Distinct().ToList();
            if (constructorIds.Count > 1)
                report.Warnings.Add($"{ai.LiveryName}: team token matches constructors [{string.Join(", ", constructorIds)}]; picking '{constructorIds[0]}'");
            string constructorId = constructorIds[0];
            chosen = chosen.Where(r => r.ConstructorId == constructorId).ToList();
            if (!pairsSeen.Add((chosen[0].DriverId, constructorId)))
            {
                report.ExcludedLiveries.Add(
                    $"{ai.LiveryName} (duplicate livery for '{ai.Name}' at {constructorId} — first livery kept)");
                continue;
            }

            var rounds = new SortedSet<int>();
            foreach (var r in chosen) { rounds.UnionWith(r.Rounds); coveredRows.Add(r); }

            if (!teamsByConstructor.TryGetValue(constructorId, out var team))
            {
                team = new TeamAccumulator
                {
                    ConstructorId = constructorId,
                    ConstructorName = chosen[0].ConstructorName,
                    DisplayName = cfg.TeamNameFromLivery ? ai.TeamToken : ComposeTeamName(chosen[0], entrants),
                };
                teamsByConstructor[constructorId] = team;
                teams.Add(team);
            }
            if (ai.VehicleReliability is double rel) team.Reliabilities.Add(rel);
            if (ai.Number is string num) team.Numbers.Add(num);
            team.TeamTokens.Add(ai.TeamToken);

            string driverId = "driver." + chosen[0].DriverId.Replace('-', '_');
            if (driverIdsEmitted.Add(driverId))
            {
                string country = ai.Country;
                if (cfg.CountryCorrections.TryGetValue(chosen[0].DriverId, out var correction))
                {
                    manifestNotes.Add(
                        $"Authored correction: {chosen[0].DriverName} country '{ai.Country}' -> '{correction.Country}' — {correction.Reason}.");
                    country = correction.Country;
                }
                driversOut.Add((driverId, chosen[0].DriverName, country, chosen[0].BornYear, ai.Ratings));
            }

            entriesOut.Add((TeamId(constructorId), driverId, ai.Number ?? "", rounds, ai.LiveryName));
        }

        foreach (var row in entrants.Where(r => !coveredRows.Contains(r)))
        {
            string desc = $"{row.DriverName} ({row.ConstructorId}+{row.EngineId}, rounds {FormatRounds(row.Rounds)})";
            report.UncoveredEntrants.Add(desc);
            manifestNotes.Add(
                $"Coverage: f1db {cfg.Year} entrant {desc} has no livery in the referenced skinpack/AI set and is not included in this pack.");
        }

        // -- car model binding per team --------------------------------------------------------
        var classVehicles = library.ClassVehicles[cfg.Ams2Class];
        foreach (var team in teams)
        {
            var vehicles = new SortedSet<string>(StringComparer.Ordinal);
            if (cfg.ModelMapByNumber)
            {
                foreach (var num in team.Numbers)
                    if (modelMap.TryGetValue(num, out var vid)) vehicles.Add(vid);
            }
            else
            {
                foreach (var token in team.TeamTokens)
                    if (modelMap.TryGetValue(Norm(token), out var vid)) vehicles.Add(vid);
                if (vehicles.Count == 0 && modelMap.TryGetValue(Norm(team.ConstructorName), out var byCtor))
                    vehicles.Add(byCtor);
            }
            if (vehicles.Count == 0)
                report.Warnings.Add($"team {team.ConstructorId}: no car model derivable from override scan; check {cfg.OverridesFolder}");
            foreach (var v in vehicles)
            {
                if (!library.VehicleIds.Contains(v))
                    report.Warnings.Add($"team {team.ConstructorId}: vehicle id '{v}' not in vehicles.json");
                else if (!classVehicles.Contains(v))
                    report.Warnings.Add($"team {team.ConstructorId}: vehicle id '{v}' not in class {cfg.Ams2Class}");
            }
            team.VehicleIds.AddRange(vehicles);

            if (!cfg.TeamMetas.ContainsKey(team.ConstructorId))
                report.Warnings.Add($"team {team.ConstructorId}: no authored prestige/budgetTier; defaulting to 2/2");
        }

        // -- resolve tracks: placeholders from the curated file, laps preserving distance ------
        var resolvedRounds = new List<ResolvedRound>();
        foreach (var race in races)
        {
            if (!cfg.Tracks.TryGetValue(race.GpId, out var binding))
                throw new InvalidOperationException($"no track binding authored for grand prix '{race.GpId}'");
            resolvedRounds.Add(ResolveRound(cfg, race, binding, library, placeholderVenues, report));
        }

        // -- carry per-track AI override entries into round aiOverrides -------------------------
        var aiOverridesByRound = cfg.CarryPerTrackOverrides
            ? CarryPerTrackOverrides(cfg, customAiDir, library, resolvedRounds, entriesOut, report, manifestNotes)
            : new Dictionary<int, SortedDictionary<string, List<KeyValuePair<string, double>>>>();

        // -- mine per-round variant XMLs (rating diffs vs the base file) into aiOverrides -------
        MineVariantFiles(cfg, customAiDir, aiDrivers, resolvedRounds, aiOverridesByRound,
            entriesOut, report, manifestNotes);

        manifestNotes.AddRange(cfg.ExtraNotes);

        // -- emit ------------------------------------------------------------------------------
        string packDir = Path.Combine(outDir, cfg.PackId);
        Directory.CreateDirectory(packDir);

        WriteJson(Path.Combine(packDir, "pack.json"), BuildPackJson(cfg, manifestNotes));
        WriteJson(Path.Combine(packDir, "season.json"),
            BuildSeasonJson(cfg, resolvedRounds, pointsSystem, entriesOut, library, aiOverridesByRound));
        WriteJson(Path.Combine(packDir, "teams.json"), BuildTeamsJson(cfg, teams));
        WriteJson(Path.Combine(packDir, "drivers.json"), BuildDriversJson(driversOut));
        WriteJson(Path.Combine(packDir, "entries.json"), BuildEntriesJson(entriesOut));

        foreach (var file in new[] { "pack.json", "season.json", "teams.json", "drivers.json", "entries.json" })
            using (JsonDocument.Parse(File.ReadAllText(Path.Combine(packDir, file)))) { /* parse check */ }

        report.Rounds = races.Count;
        report.Teams = teams.Count;
        report.Drivers = driversOut.Count;
        report.Entries = entriesOut.Count;
        return report;
    }

    // -- v1.1 round resolution: real venues, placeholder stand-ins, distance-preserving laps ----

    private static ResolvedRound ResolveRound(
        SeasonConfig cfg, RaceRow race, TrackBinding binding,
        ContentLibrary library, PlaceholderVenues placeholderVenues, PackReport report)
    {
        string realVenue = race.CircuitFullName;

        if (!binding.IsPlaceholder)
        {
            // The AMS2 track IS the venue (possibly an era-different layout, possibly under a
            // fantasy name): keep the historical lap count — the venue is real, the layout evolved.
            if (binding.TrackId is null)
                throw new InvalidOperationException($"non-placeholder binding for '{race.GpId}' names no track id");
            foreach (var id in binding.Fallbacks.Prepend(binding.TrackId))
                if (!library.TrackMaxAi.ContainsKey(id))
                    throw new InvalidOperationException($"track id '{id}' (round {race.Round}) not in tracks.json");
            if (binding.Note.Length > 0)
                report.SubstitutedVenues.Add($"round {race.Round} {race.GpName}: {binding.TrackId} — {binding.Note}");
            return new ResolvedRound(race, binding.TrackId, binding.Fallbacks, false, realVenue, race.Laps, binding.Note);
        }

        // Placeholder: the venue does not exist in AMS2 at all. Stand-in comes from the curated
        // file (first suggestion in the track library; the rest become fallbacks) and laps are
        // recomputed so the REAL race distance is preserved at the stand-in's lap length.
        var suggestions = placeholderVenues.SuggestionsFor(race.CircuitId, cfg.Year);
        var present = suggestions.Where(library.TrackMaxAi.ContainsKey).ToArray();
        if (present.Length == 0)
            throw new InvalidOperationException(
                $"no placeholder suggestion for circuit '{race.CircuitId}' (round {race.Round}) exists in the track library");
        string trackId = present[0];

        double distanceKm = race.DistanceKm
            ?? race.Laps * (race.CourseLengthKm
                ?? throw new InvalidOperationException(
                    $"f1db has neither distance nor course_length for round {race.Round} ({race.GpName})"));
        int lengthMeters = library.TrackLengthMeters[trackId];
        if (lengthMeters <= 0)
            throw new InvalidOperationException($"track '{trackId}' has no lengthMeters in tracks.json");
        int laps = Math.Max(1, (int)Math.Round(distanceKm * 1000.0 / lengthMeters, MidpointRounding.AwayFromZero));

        string display = library.TrackDisplayName(trackId);
        string kmText = distanceKm.ToString("0.#", CultureInfo.InvariantCulture);
        string note = $"Placeholder for {realVenue} — {race.Laps} laps / {kmText} km reproduced as {laps} laps of {display}.";
        if (binding.Note.Length > 0) note += " " + binding.Note;

        report.PlaceholderRounds.Add(
            $"round {race.Round} {race.GpName}: {realVenue} -> {trackId} ({race.Laps} laps / {kmText} km -> {laps} laps)");
        return new ResolvedRound(race, trackId, present.Skip(1).ToArray(), true, realVenue, laps, note);
    }

    // -- per-track AI override carry-over (round-1 finding: jusk's tracks="..." entries) --------

    private static Dictionary<int, SortedDictionary<string, List<KeyValuePair<string, double>>>> CarryPerTrackOverrides(
        SeasonConfig cfg, string customAiDir, ContentLibrary library,
        List<ResolvedRound> rounds,
        List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)> entries,
        PackReport report, List<string> manifestNotes)
    {
        var result = new Dictionary<int, SortedDictionary<string, List<KeyValuePair<string, double>>>>();

        var driverByLivery = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in entries) driverByLivery.TryAdd(e.Livery, e.DriverId);

        // Primary track id -> round numbers driving it (a season could visit a layout twice).
        var roundsByTrackId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var roundsByGpId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rounds)
        {
            if (!roundsByTrackId.TryGetValue(r.TrackId, out var list)) roundsByTrackId[r.TrackId] = list = [];
            list.Add(r.Race.Round);
            roundsByGpId[r.Race.GpId] = r.Race.Round;
        }

        foreach (var ov in AiXml.ParsePerTrackOverrides(Path.Combine(customAiDir, cfg.AiXmlFileName)))
        {
            if (!driverByLivery.TryGetValue(ov.LiveryName, out var driverId))
            {
                report.Warnings.Add(
                    $"per-track override for livery '{ov.LiveryName}' matches no pack entry — skipped");
                continue;
            }
            if (ov.DroppedFields.Count > 0)
                manifestNotes.Add(
                    $"aiOverrides: field(s) {string.Join(", ", ov.DroppedFields)} from the source XML's per-track entry for '{ov.LiveryName}' are not in the pack patch vocabulary and were dropped.");
            if (ov.Ratings.Count == 0)
                continue;

            var targetRounds = new SortedSet<int>();
            foreach (var trackName in ov.Tracks)
            {
                if (!library.TrackIdByName.TryGetValue(trackName.ToLowerInvariant(), out var trackId))
                {
                    report.Warnings.Add(
                        $"per-track override for '{ov.LiveryName}': track '{trackName}' is not in the track library — skipped");
                    continue;
                }
                if (roundsByTrackId.TryGetValue(trackId, out var direct))
                    targetRounds.UnionWith(direct);
                else if (cfg.OverrideTrackAliases.TryGetValue(trackId, out var gpId)
                         && roundsByGpId.TryGetValue(gpId, out int aliased))
                    targetRounds.Add(aliased);
                else
                    report.Warnings.Add(
                        $"per-track override for '{ov.LiveryName}': track '{trackName}' ({trackId}) matches no calendar round — skipped");
            }

            foreach (int round in targetRounds)
            {
                if (!result.TryGetValue(round, out var byDriver))
                    result[round] = byDriver = new SortedDictionary<string, List<KeyValuePair<string, double>>>(StringComparer.Ordinal);
                if (!byDriver.TryGetValue(driverId, out var patch))
                    byDriver[driverId] = patch = [];
                foreach (var kv in ov.Ratings)
                {
                    int existing = patch.FindIndex(p => p.Key == kv.Key);
                    if (existing < 0) { patch.Add(kv); continue; }
                    if (Math.Abs(patch[existing].Value - kv.Value) > 1e-9)
                        report.Warnings.Add(
                            $"per-track override conflict for '{ov.LiveryName}' round {round} {kv.Key}: " +
                            $"{patch[existing].Value} vs {kv.Value} — the later XML entry wins");
                    patch[existing] = kv;
                }
            }
        }

        return result;
    }

    // -- per-round variant-file mining (jusk's <Base>_01Kyalami.xml style) ----------------------

    /// <summary>Mines each configured variant XML against the season baseline (the base file's
    /// entries): rating values that differ become the mapped round's aiOverrides, exactly like
    /// the base file's tracks="..." carry-over; differing fields outside the patch vocabulary
    /// are documented as dropped. Every variant gets a pack.json note stating the file -> round
    /// mapping and its composition (jusk's 1969 set turned out composition-only: the variants
    /// are driver subsets of the base file with zero rating changes).</summary>
    private static void MineVariantFiles(
        SeasonConfig cfg, string customAiDir, List<AiDriver> baseline,
        List<ResolvedRound> rounds,
        Dictionary<int, SortedDictionary<string, List<KeyValuePair<string, double>>>> aiOverridesByRound,
        List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)> entries,
        PackReport report, List<string> manifestNotes)
    {
        if (cfg.VariantFiles.Length == 0) return;

        var baselineByLivery = new Dictionary<string, AiDriver>(StringComparer.Ordinal);
        foreach (var d in baseline) baselineByLivery.TryAdd(d.LiveryName, d);

        var driverByLivery = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in entries) driverByLivery.TryAdd(e.Livery, e.DriverId);

        var raceByGpId = rounds.ToDictionary(r => r.Race.GpId, r => r.Race, StringComparer.Ordinal);

        foreach (var (fileName, gpId) in cfg.VariantFiles)
        {
            if (!raceByGpId.TryGetValue(gpId, out var race))
            {
                report.Warnings.Add($"variant file {fileName}: no calendar round for grand prix '{gpId}' — skipped");
                continue;
            }

            var variant = AiXml.ParseBaseDrivers(Path.Combine(customAiDir, fileName));
            int patchedValues = 0;
            var dropped = new List<string>();

            foreach (var v in variant)
            {
                if (!baselineByLivery.TryGetValue(v.LiveryName, out var b))
                {
                    report.Warnings.Add(
                        $"variant {fileName}: livery '{v.LiveryName}' is not in the base file — skipped");
                    continue;
                }
                if (v.Name != b.Name || v.Country != b.Country)
                    report.Warnings.Add(
                        $"variant {fileName}: '{v.LiveryName}' is {v.Name}/{v.Country} but the base file has " +
                        $"{b.Name}/{b.Country} — identity changes are not representable, ratings mined only");

                var baseRatings = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var kv in b.Ratings) baseRatings[kv.Key] = kv.Value;

                foreach (var kv in v.Ratings)
                {
                    if (baseRatings.TryGetValue(kv.Key, out double bv) && Math.Abs(bv - kv.Value) <= 1e-9)
                        continue;
                    if (!AiXml.IsPatchRating(kv.Key))
                    {
                        dropped.Add($"{kv.Key} for '{v.LiveryName}'");
                        continue;
                    }
                    if (!driverByLivery.TryGetValue(v.LiveryName, out var driverId))
                    {
                        report.Warnings.Add(
                            $"variant {fileName}: rating diff for livery '{v.LiveryName}' matches no pack entry — skipped");
                        continue;
                    }

                    if (!aiOverridesByRound.TryGetValue(race.Round, out var byDriver))
                        aiOverridesByRound[race.Round] = byDriver =
                            new SortedDictionary<string, List<KeyValuePair<string, double>>>(StringComparer.Ordinal);
                    if (!byDriver.TryGetValue(driverId, out var patch))
                        byDriver[driverId] = patch = [];

                    int existing = patch.FindIndex(p => p.Key == kv.Key);
                    if (existing < 0) patch.Add(kv);
                    else
                    {
                        if (Math.Abs(patch[existing].Value - kv.Value) > 1e-9)
                            report.Warnings.Add(
                                $"variant {fileName} conflicts with an earlier override for '{v.LiveryName}' " +
                                $"round {race.Round} {kv.Key}: {patch[existing].Value} vs {kv.Value} — the variant wins");
                        patch[existing] = kv;
                    }
                    patchedValues++;
                }

                if (v.VehicleReliability is double vr && b.VehicleReliability is double br
                    && Math.Abs(vr - br) > 1e-9)
                    dropped.Add($"vehicleReliability for '{v.LiveryName}'");
            }

            string composition = patchedValues == 0
                ? $"a composition-only subset of the base file ({variant.Count} of {baseline.Count} drivers, no rating changes)"
                : $"{variant.Count} of {baseline.Count} drivers with {patchedValues} differing rating value(s) carried into the round's aiOverrides";
            manifestNotes.Add(
                $"Per-round variant file {fileName} maps to round {race.Round} ({race.GpName}): {composition}.");
            if (dropped.Count > 0)
                manifestNotes.Add(
                    $"Variant {fileName}: differing field(s) not in the pack patch vocabulary were dropped: {string.Join("; ", dropped)}.");
        }
    }

    // -- json shapes (contract: docs/dev/season-pack-format.md) --------------------------------

    private static JsonObject BuildPackJson(SeasonConfig cfg, List<string> notes)
    {
        var pack = new JsonObject
        {
            ["packId"] = cfg.PackId,
            ["name"] = cfg.PackName,
            ["version"] = "1.1.0",
            ["formatVersion"] = 1,
            ["gameVersionTested"] = "1.6.9.82",
            ["license"] = "CC BY 4.0",
            ["attribution"] = new JsonArray(cfg.Attribution.Select(a => (JsonNode)a).ToArray()),
            ["requires"] = new JsonObject
            {
                ["dlc"] = new JsonArray(),
                ["skinPacks"] = new JsonArray(new JsonObject
                {
                    ["name"] = cfg.SkinPackName,
                    ["overridesFolder"] = cfg.OverridesFolder,
                }),
            },
        };
        if (notes.Count > 0)
            pack["notes"] = new JsonArray(notes.Select(n => (JsonNode)n).ToArray());
        return pack;
    }

    private static JsonObject BuildSeasonJson(
        SeasonConfig cfg, List<ResolvedRound> rounds, JsonNode pointsSystem,
        List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)> entries,
        ContentLibrary library,
        Dictionary<int, SortedDictionary<string, List<KeyValuePair<string, double>>>> aiOverridesByRound)
    {
        var roundsArr = new JsonArray();
        foreach (var round in rounds)
        {
            var race = round.Race;
            int entryCount = entries.Count(e => e.Rounds.Contains(race.Round));
            // Contract: setupGuide.session.opponents + 1 (player) must fit the venue AI cap.
            int opponents = Math.Min(entryCount, library.TrackMaxAi[round.TrackId] - 1);

            var aiOverrides = new JsonObject();
            if (aiOverridesByRound.TryGetValue(race.Round, out var byDriver))
            {
                foreach (var (driverId, patch) in byDriver)
                {
                    var patchObj = new JsonObject();
                    foreach (var kv in patch) patchObj[kv.Key] = kv.Value;
                    aiOverrides[driverId] = patchObj;
                }
            }

            roundsArr.Add(new JsonObject
            {
                ["round"] = race.Round,
                ["name"] = race.GpName,
                ["date"] = race.Date,
                ["championship"] = true,
                ["track"] = new JsonObject
                {
                    ["realVenue"] = round.RealVenue,
                    ["id"] = round.TrackId,
                    ["isPlaceholder"] = round.IsPlaceholder,
                    ["fallbacks"] = new JsonArray(round.Fallbacks.Select(f => (JsonNode)f).ToArray()),
                },
                ["laps"] = round.Laps,
                ["setupGuide"] = new JsonObject
                {
                    ["session"] = new JsonObject
                    {
                        ["opponents"] = opponents,
                        ["startTime"] = "14:00",
                        ["date"] = race.Date,
                        ["weatherSlots"] = new JsonArray("Clear"),
                        ["timeProgression"] = "1x",
                        ["mandatoryPitStop"] = false,
                    },
                    ["notes"] = round.Note,
                },
                ["guestEntries"] = new JsonArray(),
                ["aiOverrides"] = aiOverrides,
            });
        }

        return new JsonObject
        {
            ["year"] = cfg.Year,
            ["seriesName"] = cfg.SeriesName,
            ["ams2Class"] = cfg.Ams2Class,
            ["pointsSystem"] = pointsSystem.DeepClone(),
            ["rounds"] = roundsArr,
        };
    }

    private static JsonObject BuildTeamsJson(SeasonConfig cfg, List<TeamAccumulator> teams)
    {
        var arr = new JsonArray();
        foreach (var team in teams)
        {
            var meta = cfg.TeamMetas.GetValueOrDefault(team.ConstructorId, new TeamMeta(2, 2));
            arr.Add(new JsonObject
            {
                ["id"] = TeamId(team.ConstructorId),
                ["name"] = team.DisplayName,
                ["carVehicleIds"] = new JsonArray(team.VehicleIds.Select(v => (JsonNode)v).ToArray()),
                ["performance"] = new JsonObject
                {
                    ["weightScalar"] = 1.0,
                    ["powerScalar"] = 1.0,
                    ["dragScalar"] = 1.0,
                },
                ["reliability"] = team.Reliabilities.Count == 0
                    ? 0.5
                    : Math.Round(team.Reliabilities.Average(), 2),
                ["prestige"] = meta.Prestige,
                ["budgetTier"] = meta.BudgetTier,
            });
        }
        return new JsonObject { ["teams"] = arr };
    }

    private static JsonObject BuildDriversJson(
        List<(string Id, string Name, string Country, int Born, List<KeyValuePair<string, double>> Ratings)> drivers)
    {
        var arr = new JsonArray();
        foreach (var d in drivers)
        {
            var ratings = new JsonObject();
            foreach (var kv in d.Ratings) ratings[kv.Key] = kv.Value;
            arr.Add(new JsonObject
            {
                ["id"] = d.Id,
                ["name"] = d.Name,
                ["country"] = d.Country,
                ["born"] = d.Born,
                ["ratings"] = ratings,
            });
        }
        return new JsonObject { ["drivers"] = arr };
    }

    private static JsonObject BuildEntriesJson(
        List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)> entries)
    {
        var arr = new JsonArray();
        foreach (var e in entries)
        {
            arr.Add(new JsonObject
            {
                ["teamId"] = e.TeamId,
                ["driverId"] = e.DriverId,
                ["number"] = e.Number,
                ["rounds"] = FormatRounds(e.Rounds),
                ["ams2LiveryName"] = e.Livery,
            });
        }
        return new JsonObject { ["entries"] = arr };
    }

    // -- helpers --------------------------------------------------------------------------------

    private sealed class TeamAccumulator
    {
        public required string ConstructorId { get; init; }
        public required string ConstructorName { get; init; }
        public required string DisplayName { get; init; }
        public List<double> Reliabilities { get; } = [];
        public HashSet<string> Numbers { get; } = [];
        public HashSet<string> TeamTokens { get; } = [];
        public List<string> VehicleIds { get; } = [];
    }

    private static string TeamId(string constructorId) => "team." + constructorId.Replace('-', '_');

    /// <summary>"McLaren-Honda" style: constructor plus its dominant engine that season
    /// (suppressed when the engine is the constructor itself, e.g. Ferrari, Zakspeed, Osella).</summary>
    private static string ComposeTeamName(EntrantRow chosen, List<EntrantRow> all)
    {
        string engine = all
            .Where(r => r.ConstructorId == chosen.ConstructorId)
            .GroupBy(r => r.EngineId)
            .OrderByDescending(g => g.Sum(r => r.Rounds.Count))
            .First().Key;
        if (engine == chosen.ConstructorId) return chosen.ConstructorName;
        string engineName = string.Join(" ",
            engine.Split('-', StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return $"{chosen.ConstructorName}-{engineName}";
    }

    private static Dictionary<string, string> ScanModelMap(SeasonConfig cfg, string overridesRoot, PackReport report)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        bool scanned = false;
        foreach (var model in cfg.ModelFolders)
        {
            string dir = Path.Combine(overridesRoot, model, cfg.OverridesFolder);
            if (!Directory.Exists(dir)) continue;
            scanned = true;
            string vehicleId = model; // override folder name == vehicle id for these classes
            foreach (var file in Directory.EnumerateFiles(dir, "*.dds"))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                if (cfg.ModelMapByNumber)
                {
                    var m = Regex.Match(stem, @"^[A-Za-z]+_0*(\d+)$");
                    if (m.Success) map[m.Groups[1].Value] = vehicleId;
                }
                else
                {
                    var m = Regex.Match(stem, @"^([A-Za-z]+?)\d");
                    if (m.Success) map[Norm(m.Groups[1].Value)] = vehicleId;
                }
            }
        }
        if (!scanned)
        {
            report.Warnings.Add($"override folders for {cfg.OverridesFolder} not found under {overridesRoot}; using authored model map");
            return new Dictionary<string, string>(cfg.ModelMapFallback, StringComparer.Ordinal);
        }
        return map;
    }

    private static string FormatRounds(SortedSet<int> rounds)
    {
        if (rounds.Count == 0) return "";
        var parts = new List<string>();
        int start = -1, prev = -1;
        foreach (int r in rounds)
        {
            if (start < 0) { start = prev = r; continue; }
            if (r == prev + 1) { prev = r; continue; }
            parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            start = prev = r;
        }
        parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
        return string.Join(",", parts);
    }

    private static void Add(Dictionary<string, List<EntrantRow>> dict, string key, EntrantRow row)
    {
        if (!dict.TryGetValue(key, out var list)) dict[key] = list = [];
        list.Add(row);
    }

    /// <summary>Lowercase, diacritics stripped, alphanumerics only — for name/team matching.</summary>
    private static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static void WriteJson(string path, JsonObject node) =>
        File.WriteAllText(path, node.ToJsonString(JsonOptions) + Environment.NewLine);
}
