#:package Microsoft.Data.Sqlite@10.0.9
#:property JsonSerializerIsReflectionEnabledByDefault=true
// STAGING-ONLY per-race form overlay derivation (phase 2). For each round R and pack driver D:
//   deltaPos  = seasonMeanQ(D) - qualiPos(D,R)      (positive => qualified better than usual)
//   ratingDelta = clamp(deltaPos * bandSlope, +-FORM_MAX)
//   qualifyingSkill += ratingDelta ; raceSkill += ratingDelta * RACE_SCALE
// Written into season.json as a top-level "driverForm" map: round -> driverId -> {raceSkill,qualifyingSkill}.
// The fold NEVER reads it (sim-inert); it only nudges the staged AMS2 custom-AI file for that round.
//
// Usage: dotnet run derive_form.cs -- <f1db.db> <packDir> <year> [--write]  (no --write => dry run)

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// ---- calibration (mirror derive_ratings' qual band so form is on the same scale) ----
const double Q_TOP = 1.00, Q_FLOOR = 0.68;
const double REGULAR_FRACTION = 0.34;
const double FORM_MAX = 0.06;      // max per-round nudge (keep the baseline dominant)
const double RACE_SCALE = 0.8;     // race pace is a touch less volatile than one-lap
const double STORE_MIN = 0.02;     // omit imperceptible deltas; keep real form swings (>= ~2 positions)

string repo = @"Z:\Claude Code\ams2-career-companion";
string dbPath = args.Length > 0 ? args[0] : Path.Combine(repo, @"tools\_f1db\f1db.db");
string packDir = args.Length > 1 ? args[1] : Path.Combine(repo, @"packs\f1-1988");
int year = args.Length > 2 ? int.Parse(args[2]) : 1988;
bool write = args.Contains("--write");
string seasonPath = Path.Combine(packDir, "season.json");
string driversPath = Path.Combine(packDir, "drivers.json");
Console.WriteLine($"pack {packDir} (year {year}) mode={(write ? "WRITE" : "dry-run")}");

var ALIAS = new Dictionary<string, string>(StringComparer.Ordinal) { ["carlos-sainz"] = "carlos-sainz-jr" };

using var con = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
con.Open();
int rounds = Scalar(con, "SELECT COUNT(DISTINCT round) FROM race WHERE year=@y", year);

// per (round, f1dbId) quali position, and per f1dbId season mean + session count
var roundQ = new Dictionary<(int round, string id), int>();
var qSum = new Dictionary<string, double>(StringComparer.Ordinal);
var qN = new Dictionary<string, int>(StringComparer.Ordinal);
Query(con, @"SELECT r.round, rd.driver_id, rd.position_number
             FROM race_data rd JOIN race r ON rd.race_id=r.id
             WHERE r.year=@y AND rd.type='QUALIFYING_RESULT' AND rd.position_number IS NOT NULL", year, rdr =>
{
    int rnd = rdr.GetInt32(0); string id = rdr.GetString(1); int pos = rdr.GetInt32(2);
    roundQ[(rnd, id)] = pos;
    qSum[id] = qSum.GetValueOrDefault(id) + pos;
    qN[id] = qN.GetValueOrDefault(id) + 1;
});
var meanQ = qN.ToDictionary(kv => kv.Key, kv => qSum[kv.Key] / kv.Value, StringComparer.Ordinal);

// band slope from the regulars' quali spread (same anchors as the static tool)
int regularMin = (int)Math.Ceiling(rounds * REGULAR_FRACTION);
var regAvgs = meanQ.Where(kv => qN[kv.Key] >= regularMin).Select(kv => kv.Value).ToList();
if (regAvgs.Count == 0) regAvgs = meanQ.Values.ToList();
double span = Math.Max(1.0, regAvgs.Max() - regAvgs.Min());
double slope = (Q_TOP - Q_FLOOR) / span;   // rating change per grid position
Console.WriteLine($"rounds={rounds} span={span:0.0} slope={slope:0.0000}/pos formMax={FORM_MAX}");

// pack drivers -> f1db id
var driversRoot = JsonNode(driversPath);
var packIds = new List<(string packId, string fdbId, string name)>();
foreach (var d in driversRoot["drivers"]!.AsArray())
{
    string packId = d!["id"]!.GetValue<string>();
    string fdb = (packId.StartsWith("driver.") ? packId["driver.".Length..] : packId).Replace('_', '-');
    if (ALIAS.TryGetValue(fdb, out var a)) fdb = a;
    packIds.Add((packId, fdb, d["name"]!.GetValue<string>()));
}

// build round -> driverId -> {raceSkill?, qualifyingSkill?}
var form = new SortedDictionary<int, SortedDictionary<string, Dictionary<string, double>>>();
int totalDeltas = 0; double maxAbs = 0;
for (int rnd = 1; rnd <= rounds; rnd++)
{
    foreach (var (packId, fdb, _) in packIds)
    {
        if (!roundQ.TryGetValue((rnd, fdb), out int pos) || !meanQ.TryGetValue(fdb, out double mean)) continue;
        double raw = (mean - pos) * slope;
        double q = Round2(Clamp(raw, -FORM_MAX, FORM_MAX));
        double r = Round2(Clamp(raw * RACE_SCALE, -FORM_MAX, FORM_MAX));
        if (Math.Abs(q) < STORE_MIN && Math.Abs(r) < STORE_MIN) continue;
        var entry = new Dictionary<string, double>();
        if (Math.Abs(r) >= STORE_MIN) entry["raceSkill"] = r;
        if (Math.Abs(q) >= STORE_MIN) entry["qualifyingSkill"] = q;
        if (!form.TryGetValue(rnd, out var byDriver)) form[rnd] = byDriver = new(StringComparer.Ordinal);
        byDriver[packId] = entry;
        totalDeltas++;
        maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(q), Math.Abs(r)));
    }
}
Console.WriteLine($"rounds-with-form={form.Count} driver-deltas={totalDeltas} maxAbsDelta={maxAbs:0.00}");

// biggest movers (eyeball)
foreach (var (rnd, byDriver) in form.Take(2))
{
    var top = byDriver.OrderByDescending(kv => kv.Value.GetValueOrDefault("qualifyingSkill")).Take(2);
    var bot = byDriver.OrderBy(kv => kv.Value.GetValueOrDefault("qualifyingSkill")).Take(2);
    Console.WriteLine($"  R{rnd}: up={string.Join(",", top.Select(kv => $"{Short(kv.Key)}{Sign(kv.Value)}"))}  " +
                      $"down={string.Join(",", bot.Select(kv => $"{Short(kv.Key)}{Sign(kv.Value)}"))}");
}

if (!write) { Console.WriteLine("(dry run — nothing written)"); return; }

// ---- surgical insert: top-level "driverForm" as the last key (after the rounds array) ----
string block = JsonSerializer.Serialize(form, new JsonSerializerOptions { WriteIndented = true });
// re-indent every line after the first by 2 spaces (nested one level under the season object)
var lines = block.Replace("\r\n", "\n").Split('\n');
string indented = string.Join("\r\n", lines.Select((l, i) => i == 0 ? l : "  " + l));

string text = File.ReadAllText(seasonPath);
bool trailingNl = text.EndsWith("\n");
string body = text.TrimEnd('\r', '\n');
int lastBrace = body.LastIndexOf('}');
int roundsClose = body.LastIndexOf(']', lastBrace);   // the rounds array close (rounds is the last key)
string outText = body[..(roundsClose + 1)] + ",\r\n  \"driverForm\": " + indented + "\r\n}" + (trailingNl ? "\r\n" : "");
File.WriteAllText(seasonPath, outText, new UTF8Encoding(false));
Console.WriteLine($"WROTE {seasonPath}");

// ---- helpers ----
static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
static double Round2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
static string Short(string packId) => packId.StartsWith("driver.") ? packId["driver.".Length..] : packId;
static string Sign(Dictionary<string, double> e) { double q = e.GetValueOrDefault("qualifyingSkill"); return (q >= 0 ? "+" : "") + q.ToString("0.00", CultureInfo.InvariantCulture); }
static System.Text.Json.Nodes.JsonNode JsonNode(string path) => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
static int Scalar(SqliteConnection con, string sql, int year)
{ using var c = con.CreateCommand(); c.CommandText = sql; c.Parameters.AddWithValue("@y", year); var o = c.ExecuteScalar(); return o is null or DBNull ? 0 : Convert.ToInt32(o); }
static void Query(SqliteConnection con, string sql, int year, Action<SqliteDataReader> onRow)
{ using var c = con.CreateCommand(); c.CommandText = sql; c.Parameters.AddWithValue("@y", year); using var r = c.ExecuteReader(); while (r.Read()) onRow(r); }
