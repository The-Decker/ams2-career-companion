#:package Microsoft.Data.Sqlite@10.0.9
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Systematic, reusable driver-ratings derivation from f1db.
// STATIC baseline: per driver / per season, derive raceSkill + qualifyingSkill and
// surgically write them into a pack's drivers.json (only those two fields change).
//
//   qualifyingSkill <- season mean QUALIFYING_RESULT position, mapped to a band.
//   raceSkill       <- composite of quali pace (field backbone) + championship result,
//                      mapped to a band. Encodes the full car+driver race pace gap.
//
// Field-relative calibration (anchors from "regular" drivers) => reusable across seasons
// with different field sizes / points systems. Rank/normalise, not hand-tuned.
//
// Usage:
//   dotnet run derive_ratings.cs -- <f1db.db> <packDir> <year> [--write]
//   (no --write => dry run: prints the table, writes nothing)
// Defaults (no args) => the 1988 pack, dry run.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

// ---- calibration constants ------------------------------------------------
const double Q_TOP = 1.00, Q_FLOOR = 0.68;     // qualifyingSkill band
const double Q_CLAMP_LO = 0.66, Q_CLAMP_HI = 1.00;
const double R_TOP = 0.99, R_FLOOR = 0.70;     // raceSkill band
const double W_QUALI = 0.55, W_CHAMP = 0.45;   // raceSkill composite weights
const double REGULAR_FRACTION = 0.34;          // >= this fraction of rounds => "regular" (anchors)
const double SHRINK_PSEUDO = 3.0;              // part-season drivers regress toward the field mean

// pack-id -> f1db-id aliases (f1db suffixes duplicate names; underscore->hyphen isn't enough)
var ALIAS = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["carlos-sainz"] = "carlos-sainz-jr",   // 2016+: pack uses father's slug; racer is the son
};

// ---- args -----------------------------------------------------------------
string repo = @"Z:\Claude Code\ams2-career-companion";
string dbPath = args.Length > 0 ? args[0] : Path.Combine(repo, @"tools\_f1db\f1db.db");
string packDir = args.Length > 1 ? args[1] : Path.Combine(repo, @"packs\f1-1988");
int year = args.Length > 2 ? int.Parse(args[2]) : 1988;
bool write = args.Contains("--write");

string driversPath = Path.Combine(packDir, "drivers.json");
Console.WriteLine($"f1db     : {dbPath}");
Console.WriteLine($"pack     : {packDir}  (year {year})  mode={(write ? "WRITE" : "dry-run")}");

// ---- pull f1db season aggregates ------------------------------------------
using var con = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
con.Open();

int rounds = ScalarInt(con, "SELECT COUNT(DISTINCT round) FROM race WHERE year=@y", year);
if (rounds == 0) { Console.WriteLine($"!! no races in f1db for {year}"); return; }

// quali: mean QUALIFYING_RESULT position + session count, per driver
var qAvg = new Dictionary<string, double>(StringComparer.Ordinal);
var qN = new Dictionary<string, int>(StringComparer.Ordinal);
Query(con, @"SELECT rd.driver_id, COUNT(*) n, AVG(rd.position_number) a
             FROM race_data rd JOIN race r ON rd.race_id=r.id
             WHERE r.year=@y AND rd.type='QUALIFYING_RESULT' AND rd.position_number IS NOT NULL
             GROUP BY rd.driver_id", year, rdr =>
{
    string id = rdr.GetString(0);
    qN[id] = rdr.GetInt32(1);
    qAvg[id] = rdr.GetDouble(2);
});

// participation: every f1db driver_id with ANY race_data that season (any session type).
// A pack driver with no quali but WITH participation = a genuine never-qualified backmarker
// (pre-qualifying wash-out) -> floor. No participation at all = a mapping failure -> leave alone.
var participated = new HashSet<string>(StringComparer.Ordinal);
Query(con, @"SELECT DISTINCT rd.driver_id FROM race_data rd JOIN race r ON rd.race_id=r.id
             WHERE r.year=@y", year, rdr => participated.Add(rdr.GetString(0)));

// race: mean classified finish + starts (logging only) per driver
var rAvg = new Dictionary<string, double>(StringComparer.Ordinal);
Query(con, @"SELECT rd.driver_id, AVG(rd.position_number) a
             FROM race_data rd JOIN race r ON rd.race_id=r.id
             WHERE r.year=@y AND rd.type='RACE_RESULT' AND rd.position_number IS NOT NULL
             GROUP BY rd.driver_id", year, rdr =>
{
    if (!rdr.IsDBNull(1)) rAvg[rdr.GetString(0)] = rdr.GetDouble(1);
});

// championship points per driver
var champ = new Dictionary<string, double>(StringComparer.Ordinal);
Query(con, "SELECT driver_id, points FROM season_driver_standing WHERE year=@y", year, rdr =>
{
    champ[rdr.GetString(0)] = Convert.ToDouble(rdr.GetValue(1), CultureInfo.InvariantCulture);
});
double maxPoints = champ.Count > 0 ? champ.Values.Max() : 1.0;
if (maxPoints <= 0) maxPoints = 1.0;

// anchors from regular drivers only (excludes 1-race subs distorting the band)
int regularMin = (int)Math.Ceiling(rounds * REGULAR_FRACTION);
var regularAvgs = qAvg.Where(kv => qN[kv.Key] >= regularMin).Select(kv => kv.Value).ToList();
if (regularAvgs.Count == 0) regularAvgs = qAvg.Values.ToList();
double bestReg = regularAvgs.Min();
double worstReg = regularAvgs.Max();
double span = worstReg - bestReg;
if (span <= 0) span = 1;
double fieldRefQ = regularAvgs.Average();   // mean quali position of regulars; part-timers shrink toward it

// per-driver metric. Regulars keep their real avg. Sub-regular samples get CONSERVATIVE
// (one-sided) regression: a thin sample can only be pulled toward a WORSE position, never
// rewarded for one lucky front-row lap (fixes a 1-race front-runner crowning the field) —
// and a known-slow one-off is not pulled UP to the mean either. => max(raw, shrunkToMean).
double Metric(string id)
{
    double raw = qAvg[id];
    if (qN[id] >= regularMin) return raw;
    double shrunk = (qN[id] * raw + SHRINK_PSEUDO * fieldRefQ) / (qN[id] + SHRINK_PSEUDO);
    return Math.Max(raw, shrunk);
}

Console.WriteLine($"rounds={rounds} regularMin={regularMin} anchors: best avgQ={bestReg:0.00} worst avgQ={worstReg:0.00}  fieldRefQ={fieldRefQ:0.00}  (maxChampPts={maxPoints})");
Console.WriteLine();

// ---- load pack drivers, compute, and (optionally) surgically edit ---------
string originalText = File.ReadAllText(driversPath);
JsonNode root = JsonNode.Parse(originalText)!;   // read-only: parse to iterate + read current values
var driversArr = root["drivers"]!.AsArray();

var rows = new List<Row>();
var newVals = new Dictionary<string, (string r, string q)>(StringComparer.Ordinal);  // packId -> formatted values
int missing = 0;
foreach (var d in driversArr)
{
    string packId = d!["id"]!.GetValue<string>();
    string name = d["name"]!.GetValue<string>();
    string fdbId = packId.StartsWith("driver.") ? packId["driver.".Length..] : packId;
    fdbId = fdbId.Replace('_', '-');
    if (ALIAS.TryGetValue(fdbId, out var alias)) fdbId = alias;

    var ratings = d["ratings"]!.AsObject();
    double oldR = ratings["raceSkill"]!.GetValue<double>();
    double oldQ = ratings["qualifyingSkill"]!.GetValue<double>();

    if (!qAvg.TryGetValue(fdbId, out double avgQ))
    {
        if (participated.Contains(fdbId))
        {
            // participated all season but never set a qualifying time = definitionally slowest -> floor
            double fR = Round2(R_FLOOR), fQ = Round2(Q_CLAMP_LO);
            Console.WriteLine($".. {packId} ({fdbId}) never qualified — floored (race {oldR}->{fR}, quali {oldQ}->{fQ})");
            newVals[packId] = (Fmt(fR), Fmt(fQ));
            rows.Add(new Row(name, fdbId, 0, double.NaN, champ.GetValueOrDefault(fdbId), oldR, fR, oldQ, fQ, true));
        }
        else
        {
            Console.WriteLine($"!! no f1db match for {packId} ({fdbId}) — left unchanged (race {oldR}, quali {oldQ})");
            missing++;
            rows.Add(new Row(name, fdbId, 0, double.NaN, champ.GetValueOrDefault(fdbId), oldR, oldR, oldQ, oldQ, false));
        }
        continue;
    }

    // qualifyingSkill: linear map metric -> band (best->TOP, worst->FLOOR), clamped
    double m = Metric(fdbId);
    double qNorm = Clamp01((worstReg - m) / span);
    double newQ = Round2(Clamp(Q_FLOOR + (Q_TOP - Q_FLOOR) * qNorm, Q_CLAMP_LO, Q_CLAMP_HI));

    // raceSkill: composite of quali pace backbone + championship result
    double cNorm = Clamp01(champ.GetValueOrDefault(fdbId) / maxPoints);
    double composite = Clamp01(W_QUALI * qNorm + W_CHAMP * cNorm);
    double newR = Round2(Clamp(R_FLOOR + (R_TOP - R_FLOOR) * composite, R_FLOOR, R_TOP));

    rows.Add(new Row(name, fdbId, qN[fdbId], avgQ, champ.GetValueOrDefault(fdbId), oldR, newR, oldQ, newQ, true));
    newVals[packId] = (Fmt(newR), Fmt(newQ));
}

// ---- report (sorted by new raceSkill desc) --------------------------------
Console.WriteLine($"{"driver",-22} {"f1dbId",-22} {"n",2} {"avgQ",5} {"pts",5}  race:old->new    quali:old->new");
Console.WriteLine(new string('-', 96));
foreach (var r in rows.OrderByDescending(r => r.NewR).ThenByDescending(r => r.NewQ))
{
    string avgQs = double.IsNaN(r.AvgQ) ? "  -  " : r.AvgQ.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(5);
    string dR = Delta(r.OldR, r.NewR), dQ = Delta(r.OldQ, r.NewQ);
    Console.WriteLine($"{Trunc(r.Name,22),-22} {r.FdbId,-22} {r.N,2} {avgQs} {r.Pts,5:0}  {r.OldR,4:0.00}->{r.NewR,4:0.00} {dR,3}   {r.OldQ,4:0.00}->{r.NewQ,4:0.00} {dQ,3}");
}
Console.WriteLine();
Console.WriteLine($"drivers={rows.Count}  matched={rows.Count - missing}  unmatched={missing}");

if (write)
{
    // SURGICAL textual replacement: only the raceSkill/qualifyingSkill numeric tokens change;
    // every other byte (names + their \uXXXX escaping, whitespace, ordering, trailing newline)
    // is preserved. Split/join on the file's CRLF is faithful (trailing "" round-trips the final NL).
    var idRe = new Regex("\"id\":\\s*\"(driver\\.[^\"]+)\"");
    var lines = originalText.Split("\r\n");
    string? cur = null;
    foreach (int i in Enumerable.Range(0, lines.Length))
    {
        var m = idRe.Match(lines[i]);
        if (m.Success) { cur = m.Groups[1].Value; continue; }
        if (cur is not null && newVals.TryGetValue(cur, out var nv))
        {
            lines[i] = ReplaceRating(lines[i], "raceSkill", nv.r);
            lines[i] = ReplaceRating(lines[i], "qualifyingSkill", nv.q);
        }
    }
    File.WriteAllText(driversPath, string.Join("\r\n", lines), new UTF8Encoding(false));
    Console.WriteLine($"WROTE {driversPath}");
}
else
{
    Console.WriteLine("(dry run — nothing written; pass --write to apply)");
}

// ---- helpers --------------------------------------------------------------
static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
static double Clamp01(double v) => Clamp(v, 0, 1);
static double Round2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
static string ReplaceRating(string line, string key, string val) =>
    Regex.Replace(line, $"^(\\s*\"{key}\":\\s*)[-0-9.]+(.*)$", m => m.Groups[1].Value + val + m.Groups[2].Value);
static string Delta(double a, double b)
{
    double d = Round2(b) - Round2(a);
    if (Math.Abs(d) < 0.005) return "  =";
    return (d > 0 ? "+" : "") + d.ToString("0.00", CultureInfo.InvariantCulture);
}

static int ScalarInt(SqliteConnection con, string sql, int year)
{
    using var cmd = con.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@y", year);
    var o = cmd.ExecuteScalar();
    return o is null || o is DBNull ? 0 : Convert.ToInt32(o);
}
static void Query(SqliteConnection con, string sql, int year, Action<SqliteDataReader> onRow)
{
    using var cmd = con.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@y", year);
    using var rdr = cmd.ExecuteReader();
    while (rdr.Read()) onRow(rdr);
}

record Row(string Name, string FdbId, int N, double AvgQ, double Pts,
           double OldR, double NewR, double OldQ, double NewQ, bool Matched);
