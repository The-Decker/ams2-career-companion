#:property JsonSerializerIsReflectionEnabledByDefault=true
// Authors packs/smgp-1 — the SUPER MONACO GP replica season pack — from the SMGP skinpack's own
// CustomAIDrivers XML + the verified design (docs/dev/smgp-design.md, manual-sourced).
//
// v2 (the 32-skin integration): the season fields TWENTY-SIX cars — the F-Classic_Gen3 class
// livery cap — covering ALL 22 teams the skinpack paints (SMGP1's sixteen + SMGP II's Joke,
// Lares, Feet, Serga, Cool, Moon) plus the four strongest second cars (Madonna #1 A. Senna,
// Firenze #4 I. Germi, Millions #5 N. Jones, Losel #14 W. Dehehe). Everyone drives THEIR OWN
// painted car now: G. Ceara RACES at Bullets #17 from round 1 (and remains the title-defense
// challenger), B. Miller drives Minarae #20, E. Sambena drives Serga #25. The six weakest
// second cars stay authored in drivers.json as RESERVES with no season entry (Blume #8,
// White #10, Gould #12, Alfven #19, Nono #21, Chardin #27 — all shipped slot-inactive anyway).
// Data contract: each team's LADDER/champion car is authored FIRST in its entries block, and
// ZEROFORCE is the last authored team (the mode's career-over floor).
//
//   drivers.json  <- all 32 fictional drivers with the XML's full rating set; per-driver
//                    vehicle_reliability lands in the v1.3 car block. Name corrections applied
//                    (Elsser->Elssler, Kilnger->Klinger) — LIVERY strings stay verbatim.
//   teams.json    <- the 22 teams in the game's LEVEL A-D tiers (new teams slotted by their
//                    drivers' pack ratings); carVehicleIds = the models their fielded cars use.
//   entries.json  <- the 26 fielded cars.
//   season.json   <- 16 country-named rounds in the GAME's order (San Marino -> Monaco finale),
//                    modelled on the 1989 F1 circuits (per-round history pointers to the 1989
//                    reference); per-round grid.size = min(26, the track's Max AI cap) read from
//                    data/ams2/tracks.json; points 9-6-4-3-2-1, no drops; Warm Up + "Preliminary
//                    Race" qualifying + Grand Prix, always Clear; no refuelling.
//   pack.json     <- manifest with skinSeason "smgp" + careerStyle "smgp" (the mode gate).
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

// The class livery cap: AMS2's F-Classic_Gen3 supports at most 26 DISTINCT custom liveries on a
// grid (data/ams2/livery-caps.json). The BASE field is 24 generic-model SMGP cars; the two
// McLaren MP4/5B teams (Iris + Azalea) by Kobra Fleetworks are an OPT-IN modded field that rounds
// it to 26 (at the cap) — gated on that car mod being installed (a wizard tick). 24 + 2 = 26.
const int BaseFieldSize = 24;

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
// The two McLaren MP4/5B mod drivers (Kobra Fleetworks' "Iris & Azalea" skins) are not in the
// skinpack XML — author them explicitly. They are always in drivers.json (inert without an entry;
// the modded-field transform adds their entries only when the car mod is installed).
(string Id, string Name, string Country, double[] R, double Rel)[] MCLAREN_DRIVERS =
{
    ("driver.bruno_salgado", "Bruno Salgado", "BRA",
        [0.98, 0.98, 0.90, 0.82, 0.93, 0.88, 0.92, 0.94, 0.90, 0.62, 0.82, 0.58, 0.66, 0.55], 1.10),
    ("driver.mika_larssen", "Mika Larssen", "FIN",
        [0.95, 0.96, 0.80, 0.78, 0.90, 0.86, 0.85, 0.83, 0.87, 0.58, 0.84, 0.56, 0.60, 0.50], 0.88),
};
foreach (var (id, name, country, r, rel) in MCLAREN_DRIVERS)
{
    var ratings = new JsonObject();
    for (int i = 0; i < RATING.Length; i++)
        ratings[RATING[i].Json] = JsonValue.Create(r[i]);
    drivers.Add(new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["country"] = country,
        ["ratings"] = ratings,
        ["car"] = new JsonObject { ["vehicleReliability"] = JsonValue.Create(rel) },
    });
}
WriteJson(Path.Combine(outDir, "drivers.json"), new JsonObject { ["drivers"] = drivers });
Console.WriteLine($"drivers.json: {drivers.Count} drivers");

// ---------------- teams.json + entries.json (the 26-car field, 22 teams) ----------------
// Teams in LADDER order: the design doc's canon tier listing with the SMGP II teams appended to
// their rating-appropriate tiers; ZEROFORCE stays LAST (the career-over floor). Each team's
// cars in LADDER order — champion/primary car FIRST (Madonna's #1 title plate leads).
var TEAMS = new (string Team, string Display, char Tier, (string Number, string Driver, string Livery, string Model)[] Cars)[]
{
    // LEVEL A
    ("madonna",   "Madonna",   'A', [("1",  "driver.ayrton_senna",       "Madonna #1 A. Senna",   "formula_classic_g3m1"),
                                     ("2",  "driver.alain_asselin",      "Madonna #2 A. Asselin", "formula_classic_g3m1")]),
    ("firenze",   "Firenze",   'A', [("3",  "driver.felipe_elssler",     "Firenze #3 F. Elsser",  "formula_classic_g3m1"),
                                     ("4",  "driver.ivanazzio_germi",    "Firenze #4 I. Germi",   "formula_classic_g3m1")]),
    ("millions",  "Millions",  'A', [("6",  "driver.giorgio_alberti",    "Millions #6 G. Alberti", "formula_classic_g3m3")]),
    ("bestowal",  "Bestowal",  'A', [("7",  "driver.alex_picos",         "Bestowal #7 A. Picos",  "formula_classic_g3m1")]),
    // LEVEL B
    ("blanche",   "Blanche",   'B', [("9",  "driver.jean_herbin",        "Blanche #9 J. Herbin",  "formula_classic_g3m1")]),
    ("tyrant",    "Tyrant",    'B', [("11", "driver.miyagi_hamano",      "Tyrant #11 M. Hamano",  "formula_classic_g3m4")]),
    ("losel",     "Losel",     'B', [("13", "driver.esteban_pacheco",    "Losel #13 E. Pacheco",  "formula_classic_g3m4")]),
    ("may",       "May",       'B', [("15", "driver.george_turner",      "May #15 G. Turner",     "formula_classic_g3m4")]),
    ("joke",      "Joke",      'B', [("16", "driver.luca_dufay",         "Joke #16 L. Dufay",     "formula_classic_g3m4")]),
    // LEVEL C
    ("bullets",   "Bullets",   'C', [("17", "driver.gilberto_ceara",     "Bullets #17 G. Ceara",  "formula_classic_g3m4")]),
    ("dardan",    "Dardan",    'C', [("18", "driver.eddie_bellini",      "Dardan #18 E. Bellini", "formula_classic_g3m4")]),
    ("linden",    "Linden",    'C', [("22", "driver.marcel_moreau",      "Linden #22 M. Moreau",  "formula_classic_g3m2")]),
    ("minarae",   "Minarae",   'C', [("20", "driver.bernie_miller",      "Minarae #20 B. Miller", "formula_classic_g3m2")]),
    ("lares",     "Lares",     'C', [("23", "driver.park_arai",          "Lares #23 P. Arai",     "formula_classic_g3m2")]),
    ("feet",      "Feet",      'C', [("24", "driver.jean_rampal",        "Feet #24 J. Rampal",    "formula_classic_g3m4")]),
    ("serga",     "Serga",     'C', [("25", "driver.eric_sambena",       "Serga #25 E. Sambena",  "formula_classic_g3m4")]),
    // LEVEL D (the player's rookie tier; Zeroforce is the floor — keep it LAST)
    ("rigel",     "Rigel",     'D', [("26", "driver.ryan_cotman",        "Rigel #26 R. Cotman",   "formula_classic_g3m2")]),
    ("cool",      "Cool",      'D', [("28", "driver.alef_delvaux",       "Cool #28 A. Delvaux",   "formula_classic_g3m4")]),
    ("comet",     "Comet",     'D', [("29", "driver.ethan_tornio",       "Comet #29 E. Tornio",   "formula_classic_g3m2")]),
    ("orchis",    "Orchis",    'D', [("31", "driver.christopher_tegner", "Orchis #31 C. Tegner",  "formula_classic_g3m2")]),
    ("moon",      "Moon",      'D', [("30", "driver.kevin_yepes",        "Moon #30 K. Yepes",     "formula_classic_g3m4")]),
    ("zeroforce", "Zeroforce", 'D', [("32", "driver.paul_klinger",       "Zeroforce #32 P. Kilnger", "formula_classic_g3m2")]),
};

// The two McLaren MP4/5B mod TEAMS (Iris, Azalea) — LEVEL A, so they sit among the top teams on
// the ladder (never last — Zeroforce must stay the floor). Always in teams.json (inert without an
// entry); their entries are the modded field, added only when the car mod is installed.
(string Team, string Display, string Driver, string Number, string Livery)[] MCLAREN_TEAMS =
{
    ("iris",   "Iris",   "driver.bruno_salgado", "1", "Iris #1 B. Salgado"),
    ("azalea", "Azalea", "driver.mika_larssen",  "8", "Azalea #8 M. Larssen"),
};

JsonObject Team(string id, string display, JsonArray models, double reliability, int prestige) => new()
{
    ["id"] = "team." + id,
    ["name"] = display,
    ["carVehicleIds"] = models,
    ["performance"] = new JsonObject { ["weightScalar"] = 1, ["powerScalar"] = 1, ["dragScalar"] = 1 },
    ["reliability"] = reliability,
    ["prestige"] = prestige,
    ["budgetTier"] = prestige,
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
    var models = new JsonArray();
    foreach (var model in t.Cars.Select(c => c.Model).Distinct())
        models.Add(model);
    teams.Add(Team(t.Team, t.Display, models, reliability, prestige));

    // Weave the McLaren A-teams in right after Bestowal (the last LEVEL A generic team) so the
    // ladder keeps them at LEVEL A and Zeroforce stays the last (floor) team.
    if (t.Team == "bestowal")
        foreach (var m in MCLAREN_TEAMS)
            teams.Add(Team(m.Team, m.Display, new JsonArray("mclaren_mp45b"), 0.92, 5));
}
WriteJson(Path.Combine(outDir, "teams.json"), new JsonObject { ["teams"] = teams });
Console.WriteLine($"teams.json: {teams.Count} teams");

var entries = new JsonArray();
foreach (var t in TEAMS)
    foreach (var car in t.Cars)
        entries.Add(new JsonObject
        {
            ["teamId"] = "team." + t.Team,
            ["driverId"] = car.Driver,
            ["number"] = car.Number,
            ["rounds"] = "1-16",
            ["ams2LiveryName"] = car.Livery,
        });
if (entries.Count != BaseFieldSize)
    throw new InvalidOperationException($"base field is {entries.Count} cars, expected {BaseFieldSize}");
WriteJson(Path.Combine(outDir, "entries.json"), new JsonObject { ["entries"] = entries });
Console.WriteLine($"entries.json: {entries.Count} base entries");

// ---------------- season.json (the game's 16-round order) ----------------
// (name, trackId, realVenue, isPlaceholder, laps, fallbacks, historyRound) — track choices
// mirror our 1990/1991 packs; the courses model the 1989 F1 circuits, so each round carries a
// history pointer at the 1989 event its venue models (the Calendar/briefing ORIGINAL CIRCUIT).
var ROUNDS = new (string Name, string TrackId, string Venue, bool Placeholder, int Laps, string[] Fallbacks, int HistoryRound)[]
{
    ("San Marino",    "imola_88",               "Autodromo Internazionale Enzo e Dino Ferrari", false, 61, [], 2),
    ("Brazil",        "jacarepagua_historic",   "Autódromo Internacional do Rio de Janeiro (Jacarepaguá)", false, 61, [], 1),
    ("France",        "le_mans_bugatti",        "Circuit Paul Ricard", true, 73, [], 7),
    ("Hungary",       "hungaroring_gp_2025",    "Hungaroring", false, 77, [], 10),
    ("West Germany",  "hockenheim_1988",        "Hockenheimring", false, 45, [], 9),
    ("U.S.A.",        "long_beach",             "Phoenix Street Circuit", true, 86, ["adelaide_historic"], 5),
    ("Canada",        "montrealhistoric",       "Circuit Gilles Villeneuve", false, 70, [], 6),
    ("Great Britain", "silverstone_1991",       "Silverstone Circuit", false, 64, [], 8),
    ("Italy",         "monza_1991",             "Autodromo Nazionale Monza", false, 53, [], 12),
    ("Portugal",      "estoril_1988",           "Autódromo do Estoril", false, 61, [], 13),
    ("Spain",         "jerez_1988",             "Circuito de Jerez", false, 73, [], 14),
    ("Mexico",        "interlagos_1991",        "Autódromo Hermanos Rodríguez", true, 71, [], 4),
    ("Japan",         "kansai_gp",              "Suzuka Circuit", false, 53, [], 15),
    ("Belgium",       "spa-francorchamps_1993", "Circuit de Spa-Francorchamps", false, 44, [], 11),
    ("Australia",     "adelaide_historic",      "Adelaide Street Circuit", false, 81, [], 16),
    ("Monaco",        "azure_circuit_2021",     "Circuit de Monaco", false, 78, [], 3),
};

// Per-track Max AI caps from the extracted library — the per-round base grid is min(BaseFieldSize, cap).
var trackCaps = new Dictionary<string, int>(StringComparer.Ordinal);
var tracksJson = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "data", "ams2", "tracks.json")))!;
foreach (var track in tracksJson["tracks"]!.AsArray())
    if (track!["id"] is { } id && track["maxAiParticipants"] is { } cap)
        trackCaps[id.GetValue<string>()] = cap.GetValue<int>();

JsonArray Weather() => new("Clear", "Clear", "Clear", "Clear");

var rounds = new JsonArray();
var start = new DateTime(1990, 3, 11);

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
    // <laps> laps') — the SMGP courses model the 1989 F1 circuits, so 1989/1990-era distances
    // are the reference.
    string? note = r.Name switch
    {
        "San Marino" => "The game runs 5-lap sprints; full Grand Prix distance is authored here — race shorter in AMS2 for the arcade feel.",
        "France" => "Placeholder for Circuit Paul Ricard - 80 laps / 305 km reproduced as 73 laps of Le Mans Circuit Bugatti. Keeps the French round at a French Grand Prix venue.",
        "U.S.A." => "Placeholder for Phoenix Street Circuit - 72 laps / 273.6 km reproduced as 86 laps of Long Beach. Long Beach hosted the United States GP West 1976-1983 - the era's US street-race character; Adelaide already hosts this season's Australian round.",
        "Mexico" => "Placeholder for Autódromo Hermanos Rodríguez - 69 laps / 305 km reproduced as 71 laps of Interlagos 1991. The era-correct Latin-American venue AMS2 actually has — Jacarepaguá stays unique to the Brazilian round.",
        _ => null,
    };

    if (!trackCaps.TryGetValue(r.TrackId, out int cap))
        throw new InvalidOperationException($"{r.TrackId} not in tracks.json");
    int gridSize = Math.Min(BaseFieldSize, cap);

    var starterCopy = new JsonArray();
    foreach (var t in TEAMS)
        foreach (var car in t.Cars)
            starterCopy.Add(car.Driver);

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
        ["history"] = new JsonObject { ["year"] = 1989, ["round"] = r.HistoryRound },
        ["grid"] = new JsonObject { ["size"] = gridSize, ["starterDriverIds"] = starterCopy },
        ["setupGuide"] = new JsonObject
        {
            ["session"] = new JsonObject
            {
                ["opponents"] = gridSize - 1,
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
    ["version"] = "2.0.0",
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
    // OPT-IN modded field: the two McLaren MP4/5B teams (Iris, Azalea) by Kobra Fleetworks round
    // the base 24-car field out to 26. The wizard tick verifies the mclaren_mp45b car mod is
    // installed and, when it is, the creation-time transform adds these entries + bumps the grids.
    ["moddedField"] = new JsonObject
    {
        ["vehicleId"] = "mclaren_mp45b",
        ["modName"] = "SMGP Iris & Azalea McLaren teams (Kobra Fleetworks)",
        ["entries"] = new JsonArray(MCLAREN_TEAMS.Select(m => (JsonNode)new JsonObject
        {
            ["teamId"] = "team." + m.Team,
            ["driverId"] = m.Driver,
            ["number"] = m.Number,
            ["rounds"] = "1-16",
            ["ams2LiveryName"] = m.Livery,
        }).ToArray()),
    },
    ["notes"] = new JsonArray(
        "The 16 rounds run in the GAME's order (San Marino first, Monaco the finale), not any real F1 calendar; courses model the 1989 F1 circuits (per-round history pointers reference the 1989 season).",
        "Points 9-6-4-3-2-1, top six, NO dropped scores — the raw leader after 16 races wins.",
        "Qualifying is the game's one-lap \"Preliminary Race\"; weather is always ideal (verified).",
        "The BASE season fields 24 generic-model SMGP cars covering ALL 22 painted teams (SMGP1's sixteen plus SMGP II's Joke, Lares, Feet, Serga, Cool, Moon) and two second cars (Madonna #1 A. Senna, Firenze #4 I. Germi).",
        "OPT-IN modded field: the two McLaren MP4/5B teams by Kobra Fleetworks (Iris #1 B. Salgado, Azalea #8 M. Larssen) round the grid to 26 — tick 'Add the ... cars' at career creation when the mclaren_mp45b car mod is installed; without it the base 24-car field is used.",
        "Everyone drives their own painted car: G. Ceara RACES at Bullets #17 from round 1 and is still the title-defense challenger; B. Miller drives Minarae #20; E. Sambena drives Serga #25.",
        "SKIN ACTIVATION: Lares #23 P. Arai and Feet #24 J. Rampal ship slot-INACTIVE in the skinpack — activate both in the Skins tab (cap-safe, backup-first) so their cars show in-game.",
        "Reserves (authored drivers, no season entry): M. Blume #8, P. White #10, G. Gould #12, K. Alfven #19, J. Nono #21, T. Chardin #27, plus N. Jones #5 and W. Dehehe #14 (second cars dropped so the McLarens fit the class 26-livery cap).",
        "Per-round grids run the full field wherever the venue allows; Monaco's cap is 25, so its slowest qualifier sits out (the game's own limit).",
        "New careers start in a LEVEL D car only (Rigel, Cool, Comet, Orchis, Moon, Zeroforce) — climb via the rival ladder.",
        "Driver-name corrections per the design doc: F. Elssler (pack livery label \"Elsser\"), P. Klinger (pack livery label \"Kilnger\") — livery binding strings stay verbatim.",
        "careerStyle \"smgp\" gates the replica mode (rival battles, two-wins seat swaps, the Ceara title defense, Zeroforce career-over)."),
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
