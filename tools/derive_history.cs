#:package Microsoft.Data.Sqlite@10.0.9
// Bakes real historical F1 results from f1db.db into the shipped data/history/<year>.json files
// the History tab reads ("what really happened"). Reference content only — the sim/fold never scores
// it. f1db is a DEV tool (not shipped); this tool projects a trimmed, display-shaped subset.
//
//   dotnet run scratchpad/derive_history.cs -- <f1db.db> <outDir> [startYear=1967] [endYear=2026]
//
// Source: f1db dataset, CC BY 4.0 (https://github.com/f1db/f1db).
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

string dbPath = args[0];
string outDir = args[1];
int startYear = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1967;
int endYear = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 2026;

Directory.CreateDirectory(outDir);
using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
conn.Open();

// Compact + literal UTF-8 (accents preserved, not \uXXXX): these files are generated + machine-read,
// so small beats pretty, and literal accents match the app's other data files.
var jsonOptions = new JsonSerializerOptions { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
int written = 0;
// "circuit_id|seasonYear" -> (first year, last year, count) of GPs held BEFORE that season.
// Era-capped by construction (year < seasonYear) so no file ever leaks the future.
var spanCache = new Dictionary<string, (int Min, int Max, int Count)?>();
// "circuit_id|layout_id|seasonYear" -> composed fun-fact strings (same era cap).
var factsCache = new Dictionary<string, List<string>>();

for (int year = startYear; year <= endYear; year++)
{
    var rounds = Rows(conn,
        "SELECT r.id, r.round, gp.full_name, r.circuit_layout_id, c.name, c.place_name, c.type, c.direction, " +
        "cl.length, cl.turns, c.id, c.previous_names " +
        "FROM race r JOIN grand_prix gp ON r.grand_prix_id = gp.id " +
        "JOIN circuit c ON r.circuit_id = c.id " +
        "LEFT JOIN circuit_layout cl ON r.circuit_layout_id = cl.id " +
        "WHERE r.year = $y ORDER BY r.round;", ("$y", year));
    if (rounds.Count == 0)
        continue;

    var root = new JsonObject
    {
        ["year"] = year,
        ["source"] = "Derived from the f1db dataset (CC BY 4.0) — https://github.com/f1db/f1db",
    };

    // Drivers' + constructors' champions (season standings, position 1).
    var champ = Rows(conn,
        "SELECT d.name, sds.points FROM season_driver_standing sds JOIN driver d ON sds.driver_id = d.id " +
        "WHERE sds.year = $y AND sds.position_number = 1 LIMIT 1;", ("$y", year));
    if (champ.Count == 1)
    {
        // The champion's primary team that year = the constructor they took the most race results with.
        var team = Rows(conn,
            "SELECT c.name FROM race_data rd JOIN race r ON rd.race_id = r.id JOIN constructor c ON rd.constructor_id = c.id " +
            "JOIN season_driver_standing sds ON sds.driver_id = rd.driver_id AND sds.year = r.year " +
            "WHERE r.year = $y AND rd.type = 'RACE_RESULT' AND sds.position_number = 1 " +
            "GROUP BY c.name ORDER BY COUNT(*) DESC LIMIT 1;", ("$y", year));
        root["driversChampion"] = new JsonObject
        {
            ["driver"] = champ[0][0],
            ["team"] = team.Count == 1 ? team[0][0] : null,
            ["points"] = TrimPoints(champ[0][1]),
        };
    }

    var consChamp = Rows(conn,
        "SELECT c.name, scs.points FROM season_constructor_standing scs JOIN constructor c ON scs.constructor_id = c.id " +
        "WHERE scs.year = $y AND scs.position_number = 1 LIMIT 1;", ("$y", year));
    if (consChamp.Count == 1)
        root["constructorsChampion"] = new JsonObject { ["team"] = consChamp[0][0], ["points"] = TrimPoints(consChamp[0][1]) };

    // The championship runner-up (for a factual, data-grounded season summary the History panel
    // composes — "X took the title, N pts ahead of Y").
    var runnerUp = Rows(conn,
        "SELECT d.name, sds.points FROM season_driver_standing sds JOIN driver d ON sds.driver_id = d.id " +
        "WHERE sds.year = $y AND sds.position_number = 2 LIMIT 1;", ("$y", year));
    if (runnerUp.Count == 1)
        root["runnerUp"] = new JsonObject { ["driver"] = runnerUp[0][0], ["points"] = TrimPoints(runnerUp[0][1]) };

    var roundsArray = new JsonArray();
    foreach (var round in rounds)
    {
        long raceId = long.Parse(round[0]!, CultureInfo.InvariantCulture);
        int roundNumber = int.Parse(round[1]!, CultureInfo.InvariantCulture);
        string gpName = round[2] ?? $"Round {roundNumber}";

        var results = Rows(conn,
            "SELECT rd.position_text, d.name, c.name, rd.race_reason_retired, rd.race_fastest_lap " +
            "FROM race_data rd JOIN driver d ON rd.driver_id = d.id JOIN constructor c ON rd.constructor_id = c.id " +
            "WHERE rd.race_id = $r AND rd.type = 'RACE_RESULT' ORDER BY rd.position_display_order;",
            ("$r", raceId));
        if (results.Count == 0)
            continue;

        var resultsArray = new JsonArray();
        string? winner = null, winnerTeam = null, fastestLap = null;
        foreach (var row in results)
        {
            string pos = row[0] ?? "";
            string driver = row[1] ?? "";
            string team = row[2] ?? "";
            string? status = string.IsNullOrWhiteSpace(row[3]) ? null : row[3];
            bool fl = row[4] == "1";

            var entry = new JsonObject { ["pos"] = pos, ["driver"] = driver, ["team"] = team };
            if (status is not null)
                entry["status"] = status;
            resultsArray.Add(entry);

            if (pos == "1") { winner = driver; winnerTeam = team; }
            if (fl) fastestLap = driver;
        }

        // Circuit info (round[3..9]) for the race preview + the circuit-map key. layoutId keys the
        // shipped SVG geometry (data/ams2/circuits/<layoutId>.json); the rest is preview detail.
        JsonObject? circuit = null;
        if (round[3] is { Length: > 0 } layoutId)
        {
            circuit = new JsonObject
            {
                ["layoutId"] = layoutId,
                ["name"] = round[4],
                ["place"] = round[5],
                ["type"] = round[6],           // RACE / STREET
                ["direction"] = round[7],      // CLOCKWISE / ANTI_CLOCKWISE
                ["lengthKm"] = TrimPoints(round[8]),
                ["turns"] = round[9] is { Length: > 0 } t ? int.Parse(t, CultureInfo.InvariantCulture) : null,
                // A brief, DATA-GROUNDED circuit history (former name + place + WC races + year span),
                // era-capped to races BEFORE this season so the sentence never spoils the future.
                ["history"] = CircuitHistory(conn, round[10], round[4], round[5], round[11], year, spanCache),
            };

            // Era-capped fun facts (pure f1db aggregations — counts, records, spans; no free text).
            var facts = CircuitFacts(conn, round[10]!, layoutId, year, spanCache, factsCache);
            if (facts.Count > 0)
            {
                var factsArray = new JsonArray();
                foreach (var fact in facts)
                    factsArray.Add(JsonValue.Create(fact));
                circuit["facts"] = factsArray;
            }
        }

        roundsArray.Add(new JsonObject
        {
            ["round"] = roundNumber,
            ["name"] = gpName,
            ["winner"] = winner,
            ["winnerTeam"] = winnerTeam,
            ["fastestLap"] = fastestLap,
            ["circuit"] = circuit,
            ["results"] = resultsArray,
        });
    }

    root["rounds"] = roundsArray;
    File.WriteAllText(Path.Combine(outDir, $"{year}.json"), root.ToJsonString(jsonOptions));
    written++;
    Console.WriteLine($"  {year}: {roundsArray.Count} rounds");
}

Console.WriteLine($"Wrote {written} season files to {outDir}");

static string CircuitHistory(SqliteConnection conn, string? circuitId, string? name, string? place,
    string? prevNames, int seasonYear, Dictionary<string, (int Min, int Max, int Count)?> cache)
{
    if (string.IsNullOrWhiteSpace(name))
        return "";
    string former = !string.IsNullOrWhiteSpace(prevNames) ? $" (formerly {prevNames})" : "";
    string inPlace = !string.IsNullOrWhiteSpace(place) && !place!.Equals(name, StringComparison.OrdinalIgnoreCase)
        ? $" in {place}"
        : "";
    string hosted = "";
    if (!string.IsNullOrWhiteSpace(circuitId)
        && EraSpan(conn, circuitId!, seasonYear, cache) is (int min, int max, int total) && total > 0)
    {
        string gp = total == 1 ? "F1 World Championship Grand Prix" : "F1 World Championship Grands Prix";
        string years = min == max ? $"in {min}" : $"between {min} and {max}";
        hosted = $" hosted {total} {gp} {years}";
    }
    return $"The {name} circuit{former}{inPlace}{hosted}.";
}

// GPs this circuit hosted BEFORE seasonYear — the era cap every history sentence and fact shares.
static (int Min, int Max, int Count)? EraSpan(SqliteConnection conn, string circuitId, int seasonYear,
    Dictionary<string, (int Min, int Max, int Count)?> cache)
{
    string key = $"{circuitId}|{seasonYear}";
    if (cache.TryGetValue(key, out var cached))
        return cached;
    (int, int, int)? span = null;
    var rows = Rows(conn, "SELECT MIN(year), MAX(year), COUNT(*) FROM race WHERE circuit_id = $c AND year < $y;",
        ("$c", circuitId), ("$y", seasonYear));
    if (rows.Count == 1 && rows[0][0] is { } minS && rows[0][1] is { } maxS && rows[0][2] is { } countS
        && int.TryParse(minS, out int min) && int.TryParse(maxS, out int max) && int.TryParse(countS, out int count))
        span = (min, max, count);
    cache[key] = span;
    return span;
}

// Era-capped fun facts for a circuit "coming into this season": pure COUNT/MIN/MAX/GROUP-BY
// aggregations composed into fixed templates — no invented or editorial text, ties joined
// "A / B (3 each)", capped at 6 per round. Every query filters race.year < seasonYear.
static List<string> CircuitFacts(SqliteConnection conn, string circuitId, string layoutId, int seasonYear,
    Dictionary<string, (int Min, int Max, int Count)?> spanCache, Dictionary<string, List<string>> cache)
{
    string key = $"{circuitId}|{layoutId}|{seasonYear}";
    if (cache.TryGetValue(key, out var cached))
        return cached;

    var facts = new List<string>();
    var y = ("$y", (object)seasonYear);
    var c = ("$c", (object)circuitId);

    // 1. First GP + count (debut circuits get the special line and nothing else can exist yet).
    if (EraSpan(conn, circuitId, seasonYear, spanCache) is not (int firstYear, _, int held) || held == 0)
    {
        facts.Add("Hosts its first World Championship Grand Prix this season.");
        cache[key] = facts;
        return facts;
    }
    facts.Add($"First Grand Prix here: {firstYear} — {held} World Championship " +
              $"{(held == 1 ? "GP" : "GPs")} held coming into this season.");

    // 2. Most wins (driver) — only when someone has won here more than once.
    string? wins = Leaders(Rows(conn,
        "SELECT d.name, COUNT(*) AS n FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "JOIN driver d ON rd.driver_id = d.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1 " +
        "GROUP BY rd.driver_id ORDER BY n DESC, d.name;", c, y));
    if (wins is not null)
        facts.Add($"Most wins here: {wins}.");

    // 3. Most poles.
    string? poles = Leaders(Rows(conn,
        "SELECT d.name, COUNT(*) AS n FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "JOIN driver d ON rd.driver_id = d.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.race_pole_position = 1 " +
        "GROUP BY rd.driver_id ORDER BY n DESC, d.name;", c, y));
    if (poles is not null)
        facts.Add($"Most pole positions: {poles}.");

    // 4. Most successful constructor (by wins).
    string? teams = Leaders(Rows(conn,
        "SELECT co.name, COUNT(*) AS n FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "JOIN constructor co ON rd.constructor_id = co.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1 " +
        "GROUP BY rd.constructor_id ORDER BY n DESC, co.name;", c, y), singularSuffix: " wins");
    if (teams is not null)
        facts.Add($"Most successful constructor: {teams}.");

    // 5. Winner variety.
    var variety = Rows(conn,
        "SELECT COUNT(DISTINCT rd.driver_id), COUNT(DISTINCT r.id) FROM race_data rd " +
        "JOIN race r ON rd.race_id = r.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1;", c, y);
    if (variety.Count == 1 && int.TryParse(variety[0][0], out int winners) && int.TryParse(variety[0][1], out int races)
        && winners >= 2 && races >= 2)
        facts.Add($"{winners} different winners in {races} Grands Prix.");

    // 6. Race lap record on THIS layout.
    var record = Rows(conn,
        "SELECT rd.fastest_lap_time, d.name, r.year FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "JOIN driver d ON rd.driver_id = d.id " +
        "WHERE r.circuit_layout_id = $l AND r.year < $y AND rd.type = 'FASTEST_LAP' " +
        "AND rd.fastest_lap_time_millis IS NOT NULL ORDER BY rd.fastest_lap_time_millis LIMIT 1;",
        ("$l", layoutId), y);
    if (record.Count == 1 && record[0][0] is { Length: > 0 } lapTime)
        facts.Add($"Race lap record on this layout: {lapTime} — {record[0][1]} ({record[0][2]}).");

    // 7. Pole conversion.
    var fromPole = Rows(conn,
        "SELECT COUNT(*) FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1 " +
        "AND rd.race_grid_position_number = 1;", c, y);
    if (held >= 2 && fromPole.Count == 1 && int.TryParse(fromPole[0][0], out int poleWins))
        facts.Add($"{poleWins} of {held} races here {(poleWins == 1 ? "was" : "were")} won from pole.");

    // 8. Deepest winning grid slot (interesting from P3 back).
    var deepest = Rows(conn,
        "SELECT rd.race_grid_position_number, d.name, r.year FROM race_data rd " +
        "JOIN race r ON rd.race_id = r.id JOIN driver d ON rd.driver_id = d.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1 " +
        "AND rd.race_grid_position_number IS NOT NULL " +
        "ORDER BY rd.race_grid_position_number DESC, r.year LIMIT 1;", c, y);
    if (deepest.Count == 1 && int.TryParse(deepest[0][0], out int slot) && slot >= 3)
        facts.Add($"Furthest back a winner has started: P{slot} ({deepest[0][1]}, {deepest[0][2]}).");

    // 9. Drivers' title deciders.
    var deciders = Rows(conn,
        "SELECT COUNT(*) FROM race WHERE circuit_id = $c AND year < $y AND drivers_championship_decider = 1;", c, y);
    if (deciders.Count == 1 && int.TryParse(deciders[0][0], out int decided) && decided > 0)
    {
        string times = decided switch { 1 => "once", 2 => "twice", _ => $"{decided} times" };
        facts.Add($"The drivers' championship has been decided here {times}.");
    }

    // 10. Home-crowd wins (winner's nationality = circuit's country).
    var home = Rows(conn,
        "SELECT COUNT(*) FROM race_data rd JOIN race r ON rd.race_id = r.id " +
        "JOIN driver d ON rd.driver_id = d.id JOIN circuit ci ON r.circuit_id = ci.id " +
        "WHERE r.circuit_id = $c AND r.year < $y AND rd.type = 'RACE_RESULT' AND rd.position_number = 1 " +
        "AND d.nationality_country_id = ci.country_id;", c, y);
    if (home.Count == 1 && int.TryParse(home[0][0], out int homeWins) && homeWins > 0)
        facts.Add($"Home-crowd wins: {homeWins}.");

    if (facts.Count > 6)
        facts.RemoveRange(6, facts.Count - 6);
    cache[key] = facts;
    return facts;
}

// "A (5)" / "A / B (3 each)" from name+count rows sorted by count desc, or null when the leader
// has fewer than 2 — a table of ones is noise, the winner-variety fact covers it.
static string? Leaders(List<string?[]> rows, string singularSuffix = "")
{
    if (rows.Count == 0 || !int.TryParse(rows[0][1], out int max) || max < 2)
        return null;
    var names = rows.TakeWhile(r => int.TryParse(r[1], out int n) && n == max).Select(r => r[0]).ToList();
    return names.Count == 1
        ? $"{names[0]} ({max}{singularSuffix})"
        : $"{string.Join(" / ", names)} ({max} each)";
}

static string? TrimPoints(string? points)
{
    if (string.IsNullOrWhiteSpace(points))
        return null;
    // "51.0" -> "51", "51.5" -> "51.5".
    if (decimal.TryParse(points, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        return d.ToString("0.##", CultureInfo.InvariantCulture);
    return points;
}

static List<string?[]> Rows(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    foreach (var (name, value) in ps)
        cmd.Parameters.AddWithValue(name, value);
    using var reader = cmd.ExecuteReader();
    var rows = new List<string?[]>();
    while (reader.Read())
    {
        var row = new string?[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
        rows.Add(row);
    }
    return rows;
}
