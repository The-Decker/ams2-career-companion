#:property JsonSerializerIsReflectionEnabledByDefault=true
// Authors packs/smgp-1 — the SUPER MONACO GP replica season pack — from the SMGP skinpack's own
// CustomAIDrivers XML + the verified design (docs/dev/smgp-design.md, manual-sourced):
//
//   drivers.json  <- all 32 fictional drivers (SMGP1 + SMGP II union) with the XML's full rating
//                    set; per-driver vehicle_reliability lands in the v1.3 car block. Name
//                    corrections applied (Elsser->Elssler, Kilnger->Klinger) — LIVERY strings stay
//                    verbatim as the pack paints them.
//   teams.json    <- the 16 one-driver teams in the game's LEVEL A-D tiers.
//   entries.json  <- the SMGP1 season-1 sixteen. B. Miller drives BULLETS (design correction; the
//                    car is painted "Bullets #17 G. Ceara" — the staged AI file names Miller).
//                    MINARAE is the player's team (default persona: E. Sambena, the pack author's
//                    designated player-slot invention). G. Ceara is reserved for the title-defense
//                    event (M3 mode logic).
//   season.json   <- 16 country-named rounds in the GAME's order (San Marino -> Monaco finale),
//                    modelled on the 1989 F1 circuits via the same track choices our 1990/1991
//                    packs use; points 9-6-4-3-2-1, no drops; weekend = Warm Up + "PreLIMINARY
//                    RACE" qualifying + Grand Prix, always Clear (verified); no refuelling.
//   pack.json     <- manifest with skinSeason "smgp" (the Skin Season Manager swaps the
//                    F-Classic_Gen3 pointers vs the 1990 pack) and careerStyle "smgp" (the M3
//                    mode gate; inert until the mode ships).
//
// Usage: dotnet run tools/author_smgp.cs   (writes packs/smgp-1/*, idempotent)

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repo = AppContext.BaseDirectory.Contains("tools")
    ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
    : Directory.GetCurrentDirectory();
string xmlPath = Path.Combine(repo, "scratchpad", "skins-study", "smgp", "F-Classic_Gen3.xml");
string outDir = Path.Combine(repo, "packs", "smgp-1");
Directory.CreateDirectory(outDir);

// ---------------- drivers.json (from the skinpack's own AI XML) ----------------
var RATING = new (string Xml, string Json)[]
{
    ("race_skill", "raceSkill"), ("qualifying_skill", "qualifyingSkill"),
    ("aggression", "aggression"), ("defending", "defending"), ("stamina", "stamina"),
    ("consistency", "consistency"), ("start_reactions", "startReactions"), ("wet_skill", "wetSkill"),
    ("tyre_management", "tyreManagement"), ("fuel_management", "fuelManagement"),
    ("blue_flag_conceding", "blueFlagConceding"), ("weather_tyre_changes", "weatherTyreChanges"),
    ("avoidance_of_mistakes", "avoidanceOfMistakes"), ("avoidance_of_forced_mistakes", "avoidanceOfForcedMistakes"),
};

string text = Regex.Replace(File.ReadAllText(xmlPath), "<!--.*?-->", "", RegexOptions.Singleline);
var doc = XDocument.Parse(text);
var drivers = new JsonArray();
var seen = new HashSet<string>(StringComparer.Ordinal);
foreach (var d in doc.Descendants("driver"))
{
    if (d.Attribute("tracks") is not null) continue;
    string name = (string?)d.Element("name") ?? "";
    if (name.Length == 0) continue;
    if (name == "Felipe Elsser") name = "Felipe Elssler"; // design-doc correction

    string id = "driver." + Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    if (!seen.Add(id)) continue;

    var ratings = new JsonObject();
    foreach (var (xmlName, jsonName) in RATING)
        if (d.Element(xmlName) is { } e &&
            double.TryParse(e.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            ratings[jsonName] = JsonValue.Create(v);

    var driver = new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["country"] = (string?)d.Element("country"),
        ["ratings"] = ratings,
    };
    if (d.Element("vehicle_reliability") is { } rel &&
        double.TryParse(rel.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double relV))
        driver["car"] = new JsonObject { ["vehicleReliability"] = JsonValue.Create(relV) };
    drivers.Add(driver);
}
WriteJson(Path.Combine(outDir, "drivers.json"), new JsonObject { ["drivers"] = drivers });
Console.WriteLine($"drivers.json: {drivers.Count} drivers");

// ---------------- teams.json (LEVEL A-D) ----------------
// (team, display, tier, car model, seat number, season-1 driver, seat livery)
var TEAMS = new (string Team, string Display, char Tier, string Model, string Number, string Driver, string Livery)[]
{
    ("madonna",   "Madonna",   'A', "formula_classic_g3m1", "2",  "driver.alain_asselin",      "Madonna #2 A. Asselin"),
    ("firenze",   "Firenze",   'A', "formula_classic_g3m1", "3",  "driver.felipe_elssler",     "Firenze #3 F. Elsser"),
    ("millions",  "Millions",  'A', "formula_classic_g3m3", "6",  "driver.giorgio_alberti",    "Millions #6 G. Alberti"),
    ("bestowal",  "Bestowal",  'A', "formula_classic_g3m1", "7",  "driver.alex_picos",         "Bestowal #7 A. Picos"),
    ("blanche",   "Blanche",   'B', "formula_classic_g3m1", "9",  "driver.jean_herbin",        "Blanche #9 J. Herbin"),
    ("tyrant",    "Tyrant",    'B', "formula_classic_g3m4", "11", "driver.miyagi_hamano",      "Tyrant #11 M. Hamano"),
    ("losel",     "Losel",     'B', "formula_classic_g3m4", "13", "driver.esteban_pacheco",    "Losel #13 E. Pacheco"),
    ("may",       "May",       'B', "formula_classic_g3m4", "15", "driver.george_turner",      "May #15 G. Turner"),
    ("bullets",   "Bullets",   'C', "formula_classic_g3m4", "17", "driver.bernie_miller",      "Bullets #17 G. Ceara"),
    ("dardan",    "Dardan",    'C', "formula_classic_g3m4", "18", "driver.eddie_bellini",      "Dardan #18 E. Bellini"),
    ("linden",    "Linden",    'C', "formula_classic_g3m2", "22", "driver.marcel_moreau",      "Linden #22 M. Moreau"),
    ("minarae",   "Minarae",   'C', "formula_classic_g3m2", "20", "driver.eric_sambena",       "Minarae #20 B. Miller"),
    ("rigel",     "Rigel",     'D', "formula_classic_g3m2", "26", "driver.ryan_cotman",        "Rigel #26 R. Cotman"),
    ("comet",     "Comet",     'D', "formula_classic_g3m2", "29", "driver.ethan_tornio",       "Comet #29 E. Tornio"),
    ("orchis",    "Orchis",    'D', "formula_classic_g3m2", "31", "driver.christopher_tegner", "Orchis #31 C. Tegner"),
    ("zeroforce", "Zeroforce", 'D', "formula_classic_g3m2", "32", "driver.paul_klinger",       "Zeroforce #32 P. Kilnger"),
};

var teams = new JsonArray();
foreach (var t in TEAMS)
{
    (double reliability, int prestige) = t.Tier switch
    {
        'A' => (0.92, 5),
        'B' => (0.88, 4),
        'C' => (0.84, 3),
        _ => (0.80, 2),
    };
    teams.Add(new JsonObject
    {
        ["id"] = "team." + t.Team,
        ["name"] = t.Display,
        ["carVehicleIds"] = new JsonArray(t.Model),
        ["performance"] = new JsonObject { ["weightScalar"] = 1, ["powerScalar"] = 1, ["dragScalar"] = 1 },
        ["reliability"] = reliability,
        ["prestige"] = prestige,
        ["budgetTier"] = prestige,
    });
}
WriteJson(Path.Combine(outDir, "teams.json"), new JsonObject { ["teams"] = teams });
Console.WriteLine($"teams.json: {teams.Count} teams");

// ---------------- entries.json (the SMGP1 sixteen) ----------------
var entries = new JsonArray();
foreach (var t in TEAMS)
    entries.Add(new JsonObject
    {
        ["teamId"] = "team." + t.Team,
        ["driverId"] = t.Driver,
        ["number"] = t.Number,
        ["rounds"] = "1-16",
        ["ams2LiveryName"] = t.Livery,
    });
WriteJson(Path.Combine(outDir, "entries.json"), new JsonObject { ["entries"] = entries });
Console.WriteLine($"entries.json: {entries.Count} entries");

// ---------------- season.json (the game's 16-round order) ----------------
// (name, trackId, realVenue, isPlaceholder, laps, fallbacks) — track choices mirror our
// 1990/1991 packs (the courses model the 1989 F1 circuits).
var ROUNDS = new (string Name, string TrackId, string Venue, bool Placeholder, int Laps, string[] Fallbacks)[]
{
    ("San Marino",    "imola_88",               "Autodromo Internazionale Enzo e Dino Ferrari", false, 61, []),
    ("Brazil",        "jacarepagua_historic",   "Autódromo Internacional do Rio de Janeiro (Jacarepaguá)", false, 61, []),
    ("France",        "le_mans_bugatti",        "Circuit Paul Ricard", true, 73, []),
    ("Hungary",       "hungaroring_gp_2025",    "Hungaroring", false, 77, []),
    ("West Germany",  "hockenheim_1988",        "Hockenheimring", false, 45, []),
    ("U.S.A.",        "long_beach",             "Phoenix Street Circuit", true, 86, ["adelaide_historic"]),
    ("Canada",        "montrealhistoric",       "Circuit Gilles Villeneuve", false, 70, []),
    ("Great Britain", "silverstone_1991",       "Silverstone Circuit", false, 64, []),
    ("Italy",         "monza_1991",             "Autodromo Nazionale Monza", false, 53, []),
    ("Portugal",      "estoril_1988",           "Autódromo do Estoril", false, 61, []),
    ("Spain",         "jerez_1988",             "Circuito de Jerez", false, 73, []),
    ("Mexico",        "jacarepagua_historic",   "Autódromo Hermanos Rodríguez", true, 61, []),
    ("Japan",         "kansai_gp",              "Suzuka Circuit", false, 53, []),
    ("Belgium",       "spa-francorchamps_1993", "Circuit de Spa-Francorchamps", false, 44, []),
    ("Australia",     "adelaide_historic",      "Adelaide Street Circuit", false, 81, []),
    ("Monaco",        "azure_circuit_88",       "Circuit de Monaco", false, 78, []),
};

JsonArray Weather() => new("Clear", "Clear", "Clear", "Clear");

var rounds = new JsonArray();
var start = new DateTime(1990, 3, 11);
var starterIds = new JsonArray();
foreach (var t in TEAMS) starterIds.Add(t.Driver);

for (int i = 0; i < ROUNDS.Length; i++)
{
    var r = ROUNDS[i];
    string date = start.AddDays(14 * i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var track = new JsonObject
    {
        ["realVenue"] = r.Venue,
        ["id"] = r.TrackId,
        ["isPlaceholder"] = r.Placeholder,
        ["fallbacks"] = new JsonArray(r.Fallbacks.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
    };
    // Placeholder notes follow the pack contract ('<historical> laps / <km> km reproduced as
    // <laps> laps of <track>'), mirroring the 1990 pack's identical stand-ins — the SMGP courses
    // model the 1989 F1 circuits, so the 1989/1990-era distances are the reference.
    string? note = r.Name switch
    {
        "San Marino" => "The game runs 5-lap sprints; full Grand Prix distance is authored here — race shorter in AMS2 for the arcade feel.",
        "France" => "Placeholder for Circuit Paul Ricard - 80 laps / 305 km reproduced as 73 laps of Le Mans Circuit Bugatti. Keeps the French round at a French Grand Prix venue.",
        "U.S.A." => "Placeholder for Phoenix Street Circuit - 72 laps / 273.6 km reproduced as 86 laps of Long Beach. Long Beach hosted the United States GP West 1976-1983 - the era's US street-race character; Adelaide already hosts this season's Australian round.",
        "Mexico" => "Placeholder for Autódromo Hermanos Rodríguez - 69 laps / 305 km reproduced as 61 laps of Jacarepagua Historic. Jacarepaguá keeps the era's Latin-American venue character; the game's Brazilian round runs there too, exactly as Super Monaco GP reuses course scenery.",
        _ => null,
    };
    var starterCopy = new JsonArray();
    foreach (var t in TEAMS) starterCopy.Add(t.Driver);
    rounds.Add(new JsonObject
    {
        ["round"] = i + 1,
        ["weekend"] = new JsonObject
        {
            ["practice"] = new JsonObject
            {
                ["present"] = true,
                ["label"] = "Warm Up",
                ["durationMinutes"] = 30,
                ["weatherSlots"] = Weather(),
            },
            ["qualifying"] = new JsonObject
            {
                ["present"] = true,
                ["label"] = "Preliminary Race",
                ["durationMinutes"] = 15,
                ["weatherSlots"] = Weather(),
            },
            ["races"] = new JsonArray(new JsonObject
            {
                ["id"] = "race",
                ["label"] = "Grand Prix",
                ["weatherSlots"] = Weather(),
            }),
        },
        ["name"] = r.Name,
        ["date"] = date,
        ["championship"] = true,
        ["track"] = track,
        ["laps"] = r.Laps,
        ["grid"] = new JsonObject { ["size"] = 16, ["starterDriverIds"] = starterCopy },
        ["setupGuide"] = new JsonObject
        {
            ["session"] = new JsonObject
            {
                ["opponents"] = 15,
                ["startTime"] = "14:00",
                ["date"] = date,
                ["weatherSlots"] = new JsonArray("Clear"),
                ["timeProgression"] = "1x",
                ["mandatoryPitStop"] = false,
            },
            ["notes"] = note ?? "",
        },
        ["guestEntries"] = new JsonArray(),
        ["aiOverrides"] = new JsonObject(),
    });
}

var season = new JsonObject
{
    ["year"] = 1990,
    ["seriesName"] = "Super Monaco GP World Championship",
    ["ams2Class"] = "F-Classic_Gen3",
    ["refuellingAllowed"] = false,
    ["pointsSystem"] = new JsonObject
    {
        ["racePoints"] = new JsonArray(9, 6, 4, 3, 2, 1),
        ["sharedDrivePolicy"] = "zero",
        ["constructors"] = new JsonObject { ["bestCarOnly"] = false, ["bestN"] = null },
    },
    ["rounds"] = rounds,
};
WriteJson(Path.Combine(outDir, "season.json"), season);
Console.WriteLine($"season.json: {rounds.Count} rounds");

// ---------------- pack.json ----------------
var pack = new JsonObject
{
    ["packId"] = "smgp-1",
    ["name"] = "Super Monaco GP",
    ["version"] = "1.0.0",
    ["formatVersion"] = 1,
    ["skinSeason"] = "smgp",
    ["careerStyle"] = "smgp",
    ["gameVersionTested"] = "1.6.9.82",
    ["license"] = "CC BY 4.0",
    ["attribution"] = new JsonArray(
        "Roster, ratings and liveries from SMGP SKINS V1 by rafaelcsanti (F-Classic_Gen3)",
        "Season structure replicates Sega's Super Monaco GP (Mega Drive, 1990) — see docs/dev/smgp-design.md for the manual-sourced design"),
    ["requires"] = new JsonObject
    {
        ["dlc"] = new JsonArray(),
        ["skinPacks"] = new JsonArray(new JsonObject
        {
            ["name"] = "SMGP SKINS V1 (rafaelcsanti)",
            ["overridesFolder"] = "SMGP",
        }),
    },
    ["notes"] = new JsonArray(
        "The 16 rounds run in the GAME's order (San Marino first, Monaco the finale), not any real F1 calendar; courses model the 1989 F1 circuits.",
        "Points 9-6-4-3-2-1, top six, NO dropped scores — the raw leader after 16 races wins.",
        "Qualifying is the game's one-lap \"Preliminary Race\"; weather is always ideal (verified).",
        "B. Miller drives BULLETS in season 1 (design correction) — the car is painted \"Bullets #17 G. Ceara\"; the staged AI file names Miller. G. Ceara himself is reserved for the title-defense event.",
        "MINARAE is the player's team (the game assigns you there, Level C). Its default persona E. Sambena is the skinpack author's designated player-slot invention (non-canon).",
        "Driver-name corrections per the design doc: F. Elssler (pack livery label \"Elsser\"), P. Klinger (pack livery label \"Kilnger\") — livery binding strings stay verbatim.",
        "careerStyle \"smgp\" gates the replica mode (rival battles, seat swaps, the Ceara title defense); until that ships the pack plays as a normal 16-round season."),
};
WriteJson(Path.Combine(outDir, "pack.json"), pack);
Console.WriteLine("pack.json written");
return 0;

// 2-space indent + CRLF + UTF8 no-BOM, matching the pack file contract.
static void WriteJson(string path, JsonNode node)
{
    string json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
    File.WriteAllText(path, json, new UTF8Encoding(false));
}
