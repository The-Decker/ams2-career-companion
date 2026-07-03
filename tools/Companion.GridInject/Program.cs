// Companion.GridInject — ADDITIVE per-round historical grid injector for the shipped packs.
//
// The season packs carry EVERY season entrant, but each round historically had far fewer actual
// starters, and AMS2 caps the grid per venue. This tool patches each packs/f1-YYYY/season.json in
// place, touching only two things per round:
//   1. adds a "grid" block { size, starterDriverIds } — the drivers who ACTUALLY started that round
//      (f1db RACE_RESULT rows minus DNQ/DNPQ/DNS/DNP/EX), mapped to the pack's driver ids and
//      intersected with the entries covering that round, in entries.json order; size = min(that
//      count, the track's Max AI participants from data/ams2/tracks.json).
//   2. sets setupGuide.session.opponents = size - 1 (the player replaces one historical starter).
// Everything else in the file — liveries, entries, ratings, points system, aiOverrides, notes — is
// preserved verbatim (JsonNode DOM round-trip with the same writer options PackGen uses). Rounds
// with no f1db starter data are left unchanged (defaulted: the resolver keeps pre-grid behaviour).
//
// Usage: Companion.GridInject <f1db.db> <ams2DataDir> <packsDir>

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: Companion.GridInject <f1db.db> <ams2DataDir> <packsDir>");
    return 2;
}

string dbPath = Path.GetFullPath(args[0]);
string ams2DataDir = Path.GetFullPath(args[1]);
string packsDir = Path.GetFullPath(args[2]);

var writer = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

// track id -> Max AI participants
var trackMaxAi = new Dictionary<string, int>(StringComparer.Ordinal);
{
    var tracks = JsonNode.Parse(File.ReadAllText(Path.Combine(ams2DataDir, "tracks.json")))!;
    foreach (var t in tracks["tracks"]!.AsArray())
        trackMaxAi[t!["id"]!.GetValue<string>()] = t["maxAiParticipants"]?.GetValue<int>() ?? 0;
}

using var con = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly,
}.ToString());
con.Open();

int exit = 0;
foreach (var packDir in Directory.GetDirectories(packsDir).OrderBy(d => d, StringComparer.Ordinal))
{
    string packId = Path.GetFileName(packDir);
    string seasonPath = Path.Combine(packDir, "season.json");
    string entriesPath = Path.Combine(packDir, "entries.json");
    if (!File.Exists(seasonPath) || !File.Exists(entriesPath))
    {
        Console.Error.WriteLine($"[{packId}] not a pack folder (missing season.json/entries.json) — skipped");
        continue;
    }

    try
    {
        InjectPack(packId, seasonPath, entriesPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{packId}] FAILED: {ex.Message}");
        exit = 1;
    }
}

return exit;

void InjectPack(string packId, string seasonPath, string entriesPath)
{
    var season = JsonNode.Parse(File.ReadAllText(seasonPath))!.AsObject();
    var entriesRoot = JsonNode.Parse(File.ReadAllText(entriesPath))!.AsObject();
    int year = season["year"]!.GetValue<int>();

    // entries in file order: driverId -> covering rounds (from the "rounds" expression).
    var entries = new List<(string DriverId, HashSet<int> Rounds)>();
    foreach (var e in entriesRoot["entries"]!.AsArray())
    {
        string driverId = e!["driverId"]!.GetValue<string>();
        entries.Add((driverId, ParseRounds(e["rounds"]!.GetValue<string>())));
    }

    var startersByRound = LoadStarters(year);

    var roundsArr = season["rounds"]!.AsArray();
    int patched = 0, skipped = 0;
    var newRounds = new JsonArray();

    foreach (var roundNode in roundsArr)
    {
        var round = roundNode!.AsObject();
        int roundNo = round["round"]!.GetValue<int>();
        string trackId = round["track"]!["id"]!.GetValue<string>();
        if (!trackMaxAi.TryGetValue(trackId, out int maxAi) || maxAi <= 0)
            throw new InvalidOperationException($"round {roundNo}: track '{trackId}' has no maxAiParticipants in tracks.json");

        // Historical starters mapped to pack driver ids, intersected with covering entries in
        // entries.json order.
        var starterPackIds = startersByRound.TryGetValue(roundNo, out var s)
            ? s.Select(id => "driver." + id.Replace('-', '_')).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var gridStarters = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int coveringCount = 0;
        foreach (var (driverId, rounds) in entries)
        {
            if (!rounds.Contains(roundNo)) continue;
            coveringCount++;
            if (starterPackIds.Contains(driverId) && seen.Add(driverId))
                gridStarters.Add(driverId);
        }

        if (gridStarters.Count == 0)
        {
            // No f1db starter mapping for this round — leave the round untouched (defaulted).
            newRounds.Add(round.DeepClone());
            skipped++;
            continue;
        }

        int gridSize = Math.Max(1, Math.Min(gridStarters.Count, maxAi));
        int opponents = gridSize - 1;

        var rebuilt = RebuildRoundWithGrid(round, gridSize, gridStarters, opponents);
        newRounds.Add(rebuilt);
        patched++;

        int extras = coveringCount - gridStarters.Count;
        Console.WriteLine(
            $"[{packId}] round {roundNo,2} {trackId,-24} starters={gridStarters.Count,2} " +
            $"cap={maxAi,2} -> size={gridSize,2} opponents={opponents,2} (covering={coveringCount}, +{extras} non-starters kept in pack)");
    }

    season["rounds"] = newRounds;
    File.WriteAllText(seasonPath, season.ToJsonString(writer) + Environment.NewLine);
    Console.WriteLine($"[{packId}] patched {patched} round(s), left {skipped} untouched (no f1db starters).");
}

// Rebuild a round object preserving original key order, inserting "grid" immediately after "laps"
// (or, if absent, before "setupGuide"), and overwriting setupGuide.session.opponents. Every other
// key and value is copied verbatim.
JsonObject RebuildRoundWithGrid(JsonObject round, int gridSize, List<string> gridStarters, int opponents)
{
    var gridObj = new JsonObject
    {
        ["size"] = gridSize,
        ["starterDriverIds"] = new JsonArray(gridStarters.Select(id => (JsonNode)id).ToArray()),
    };

    var result = new JsonObject();
    bool gridInserted = false;

    foreach (var (key, value) in round)
    {
        if (key == "grid")
            continue; // drop any prior grid; we re-add canonically

        // Insert grid before setupGuide if we have not already (covers files with no "laps").
        if (!gridInserted && key == "setupGuide")
        {
            result["grid"] = gridObj;
            gridInserted = true;
        }

        if (key == "setupGuide" && value is JsonObject setup)
        {
            result["setupGuide"] = RebuildSetupGuide(setup, opponents);
        }
        else
        {
            result[key] = value!.DeepClone();
        }

        // Prefer to place grid right after laps.
        if (!gridInserted && key == "laps")
        {
            result["grid"] = gridObj;
            gridInserted = true;
        }
    }

    if (!gridInserted)
        result["grid"] = gridObj;

    return result;
}

JsonObject RebuildSetupGuide(JsonObject setup, int opponents)
{
    var result = new JsonObject();
    foreach (var (key, value) in setup)
    {
        if (key == "session" && value is JsonObject session)
        {
            var newSession = new JsonObject();
            foreach (var (sk, sv) in session)
                newSession[sk] = sk == "opponents" ? opponents : sv!.DeepClone();
            if (!session.ContainsKey("opponents"))
                newSession["opponents"] = opponents;
            result["session"] = newSession;
        }
        else
        {
            result[key] = value!.DeepClone();
        }
    }
    return result;
}

Dictionary<int, List<string>> LoadStarters(int year)
{
    var byRound = new Dictionary<int, List<string>>();
    using var cmd = con.CreateCommand();
    cmd.CommandText = """
        SELECT r.round, rd.driver_id
        FROM race_data rd
        JOIN race r ON r.id = rd.race_id
        WHERE r.year = $y AND rd.type = 'RACE_RESULT'
          AND rd.position_text NOT IN ('DNQ','DNPQ','DNS','DNP','EX')
        ORDER BY r.round, rd.position_display_order
        """;
    cmd.Parameters.AddWithValue("$y", year);
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        int round = rd.GetInt32(0);
        if (!byRound.TryGetValue(round, out var list)) byRound[round] = list = [];
        list.Add(rd.GetString(1));
    }
    return byRound;
}

static HashSet<int> ParseRounds(string expr)
{
    var set = new HashSet<int>();
    foreach (var part in expr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int dash = part.IndexOf('-');
        if (dash < 0)
        {
            set.Add(int.Parse(part, CultureInfo.InvariantCulture));
        }
        else
        {
            int lo = int.Parse(part[..dash], CultureInfo.InvariantCulture);
            int hi = int.Parse(part[(dash + 1)..], CultureInfo.InvariantCulture);
            for (int r = lo; r <= hi; r++) set.Add(r);
        }
    }
    return set;
}
