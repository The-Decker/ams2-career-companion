// Companion.PackGen — generates the bundled reference season packs (F1 1967, F1 1988) as
// plain-JSON pack folders per docs/dev/season-pack-format.md (format v1).
//
// Sources reconciled:
//   - f1db SQLite release (CC BY 4.0): season calendar (round/GP/date/real laps) + entrants
//     (driver <-> constructor <-> rounds).
//   - Installed community custom-AI XMLs (jusk et al.): the PROVEN livery names + AI ratings
//     that bind against the deployed skinpacks on this machine.
//   - data/ams2/{tracks,vehicles,classes}.json: content library used to verify every id emitted.
//   - data/rules/f1-points-systems.json: pointsSystem copied VERBATIM per season.
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

var library = ContentLibrary.Load(ams2DataDir);
var rules = JsonNode.Parse(File.ReadAllText(rulesPath))!.AsObject();

var seasons = new[] { SeasonConfigs.F1_1967(), SeasonConfigs.F1_1988() };
int exitCode = 0;

foreach (var cfg in seasons)
{
    try
    {
        var report = PackBuilder.Build(cfg, dbPath, customAiDir, overridesRoot, library, rules, outDir);
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

internal sealed record TrackBinding(string TrackId, string[] Fallbacks, string Note);

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
            ["netherlands"] = new("silverstone_1975", [],
                "Zandvoort is not available in AMS2; Silverstone 1975 stands in for the Dutch GP."),
            ["belgium"] = new("spa-francorchamps_1970", [], ""),
            ["france"] = new("rouen", ["le_mans_bugatti"],
                "The 1967 French GP ran at the Bugatti Circuit, Le Mans; era-correct Rouen-les-Essarts stands in (AMS2's Le Mans Bugatti is the modern layout)."),
            ["great-britain"] = new("silverstone_1975nc", ["silverstone_1975"], ""),
            ["germany"] = new("nurb_1971_nords", [], ""),
            ["canada"] = new("mosport_1971", [], ""),
            ["italy"] = new("monza_1971", [], ""),
            ["united-states"] = new("watkins_glen_1971_short", [], ""),
            ["mexico"] = new("interlagos_historic", [],
                "Mexico City is not available in AMS2; historic Interlagos stands in for the Mexican GP."),
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
            ["mexico"] = new("interlagos_1991", [],
                "Mexico City (Hermanos Rodriguez) is not available in AMS2; Interlagos 1991 stands in as a period Latin-American venue."),
            ["canada"] = new("montrealhistoric", [], ""),
            ["detroit"] = new("long_beach", [],
                "The Detroit street circuit is not available in AMS2; Long Beach stands in as the era's US street-circuit substitute."),
            ["france"] = new("le_mans_bugatti", [],
                "Paul Ricard is not available in AMS2; Le Mans Bugatti stands in as the French venue."),
            ["great-britain"] = new("silverstone_1991", ["silverstone_1975"],
                "No 1988 Silverstone layout in AMS2; the 1991 layout is used (Silverstone 1975 with chicane is the era-closest alternative)."),
            ["germany"] = new("hockenheim_1988", [], ""),
            ["hungary"] = new("hungaroring_gp_2025", [],
                "AMS2 ships the 2025 Hungaroring; the layout is largely unchanged in character since 1986."),
            ["belgium"] = new("spa-francorchamps_1993", [],
                "No 1988 Spa layout in AMS2; the 1993 layout is the closest era."),
            ["italy"] = new("monza_1991", [],
                "No 1988 Monza in AMS2; the 1991 layout matches the era."),
            ["portugal"] = new("estoril_1988", [], ""),
            ["spain"] = new("jerez_1988", [], ""),
            ["japan"] = new("kansai_gp", [],
                "Suzuka appears in AMS2 as Kansai; the modern GP layout stands in for 1988."),
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
    };
}

// ---------------------------------------------------------------------------------------------

internal sealed class ContentLibrary
{
    public required Dictionary<string, int> TrackMaxAi { get; init; }   // track id -> maxAiParticipants
    public required HashSet<string> VehicleIds { get; init; }
    public required Dictionary<string, HashSet<string>> ClassVehicles { get; init; } // class xmlName -> vehicle ids

    public static ContentLibrary Load(string ams2DataDir)
    {
        var tracks = JsonNode.Parse(File.ReadAllText(Path.Combine(ams2DataDir, "tracks.json")))!;
        var trackMaxAi = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tracks["tracks"]!.AsArray())
            trackMaxAi[t!["id"]!.GetValue<string>()] = t["maxAiParticipants"]?.GetValue<int>() ?? 0;

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

        return new ContentLibrary { TrackMaxAi = trackMaxAi, VehicleIds = vehicleIds, ClassVehicles = classVehicles };
    }
}

internal sealed record AiDriver(
    string LiveryName, string TeamToken, string? Number, string Name, string Country,
    List<KeyValuePair<string, double>> Ratings, double? VehicleReliability);

internal static class AiXml
{
    // Canonical rating order for drivers.json (custom-AI vocabulary, camelCased).
    private static readonly string[] RatingOrder =
    [
        "raceSkill", "qualifyingSkill", "aggression", "defending", "stamina", "consistency",
        "startReactions", "wetSkill", "tyreManagement", "fuelManagement", "blueFlagConceding",
        "weatherTyreChanges", "avoidanceOfMistakes", "avoidanceOfForcedMistakes",
    ];

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

internal sealed record RaceRow(int Round, string GpId, string GpName, string Date, int Laps);

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
            SELECT r.round, r.grand_prix_id, gp.full_name, r.date, r.laps
            FROM race r JOIN grand_prix gp ON gp.id = r.grand_prix_id
            WHERE r.year = $y ORDER BY r.round
            """;
        cmd.Parameters.AddWithValue("$y", year);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new RaceRow(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4)));
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
    public List<string> Warnings { get; } = [];
    public bool HasErrors => UnmatchedLiveries.Count > 0;

    public void Print()
    {
        Console.WriteLine($"[{PackId}] rounds={Rounds} teams={Teams} drivers={Drivers} entries={Entries}");
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

    public static PackReport Build(
        SeasonConfig cfg, string dbPath, string customAiDir, string overridesRoot,
        ContentLibrary library, JsonObject rules, string outDir)
    {
        var report = new PackReport { PackId = cfg.PackId };

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
                driversOut.Add((driverId, chosen[0].DriverName, ai.Country, chosen[0].BornYear, ai.Ratings));

            entriesOut.Add((TeamId(constructorId), driverId, ai.Number ?? "", rounds, ai.LiveryName));
        }

        foreach (var row in entrants.Where(r => !coveredRows.Contains(r)))
            report.UncoveredEntrants.Add($"{row.DriverName} ({row.ConstructorId}+{row.EngineId}, rounds {FormatRounds(row.Rounds)})");

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

        // -- verify tracks + compute per-round opponents ---------------------------------------
        foreach (var race in races)
        {
            if (!cfg.Tracks.TryGetValue(race.GpId, out var binding))
                throw new InvalidOperationException($"no track binding authored for grand prix '{race.GpId}'");
            foreach (var id in binding.Fallbacks.Prepend(binding.TrackId))
                if (!library.TrackMaxAi.ContainsKey(id))
                    throw new InvalidOperationException($"track id '{id}' (round {race.Round}) not in tracks.json");
            if (binding.Note.Length > 0)
                report.SubstitutedVenues.Add($"round {race.Round} {race.GpName}: {binding.TrackId} — {binding.Note}");
        }

        // -- emit ------------------------------------------------------------------------------
        string packDir = Path.Combine(outDir, cfg.PackId);
        Directory.CreateDirectory(packDir);

        WriteJson(Path.Combine(packDir, "pack.json"), BuildPackJson(cfg));
        WriteJson(Path.Combine(packDir, "season.json"), BuildSeasonJson(cfg, races, pointsSystem, entriesOut, library));
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

    // -- json shapes (contract: docs/dev/season-pack-format.md) --------------------------------

    private static JsonObject BuildPackJson(SeasonConfig cfg) => new()
    {
        ["packId"] = cfg.PackId,
        ["name"] = cfg.PackName,
        ["version"] = "1.0.0",
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

    private static JsonObject BuildSeasonJson(
        SeasonConfig cfg, List<RaceRow> races, JsonNode pointsSystem,
        List<(string TeamId, string DriverId, string Number, SortedSet<int> Rounds, string Livery)> entries,
        ContentLibrary library)
    {
        var roundsArr = new JsonArray();
        foreach (var race in races)
        {
            var binding = cfg.Tracks[race.GpId];
            int entryCount = entries.Count(e => e.Rounds.Contains(race.Round));
            // Contract: setupGuide.session.opponents + 1 (player) must fit the venue AI cap.
            int opponents = Math.Min(entryCount, library.TrackMaxAi[binding.TrackId] - 1);

            roundsArr.Add(new JsonObject
            {
                ["round"] = race.Round,
                ["name"] = race.GpName,
                ["date"] = race.Date,
                ["championship"] = true,
                ["track"] = new JsonObject
                {
                    ["id"] = binding.TrackId,
                    ["fallbacks"] = new JsonArray(binding.Fallbacks.Select(f => (JsonNode)f).ToArray()),
                },
                ["laps"] = race.Laps,
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
                    ["notes"] = binding.Note,
                },
                ["guestEntries"] = new JsonArray(),
                ["aiOverrides"] = new JsonObject(),
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
