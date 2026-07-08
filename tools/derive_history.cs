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

for (int year = startYear; year <= endYear; year++)
{
    var rounds = Rows(conn,
        "SELECT r.id, r.round, gp.full_name, r.circuit_layout_id, c.name, c.place_name, c.type, c.direction, " +
        "cl.length, cl.turns " +
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
            };
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
