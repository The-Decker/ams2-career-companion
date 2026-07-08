#:package Microsoft.Data.Sqlite@10.0.9
// Downloads the f1db circuit-layout SVGs (white style) for every circuit layout used in the year
// range, extracts the path geometry, NORMALIZES it into WPF's path mini-language (WPF's Geometry
// parser needs explicit separators that SVG's compact number notation omits, e.g. "-5.126.772" =>
// "-5.126 .772"), and writes data/ams2/circuits/<layoutId>.json = { w, h, paths:[...] } — the shipped
// circuit maps the race-setup + History screens render. f1db SVGs are NOT in the release artifacts, so
// this pulls them from the repo (CC BY 4.0).
//
//   dotnet run tools/derive_circuits.cs [f1db.db] [outDir] [startYear] [endYear]
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

string dbPath = args.Length > 0 ? args[0] : "tools/_f1db/f1db.db";
string outDir = args.Length > 1 ? args[1] : "data/ams2/circuits";
int startYear = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1967;
int endYear = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 2026;

const string BaseUrl = "https://raw.githubusercontent.com/f1db/f1db/main/src/assets/circuits/white/";

Directory.CreateDirectory(outDir);
using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
conn.Open();

var layoutIds = new List<string>();
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText =
        "SELECT DISTINCT circuit_layout_id FROM race WHERE year BETWEEN $a AND $b " +
        "AND circuit_layout_id IS NOT NULL ORDER BY circuit_layout_id;";
    cmd.Parameters.AddWithValue("$a", startYear);
    cmd.Parameters.AddWithValue("$b", endYear);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
        layoutIds.Add(reader.GetString(0));
}
Console.WriteLine($"{layoutIds.Count} circuit layouts referenced {startYear}-{endYear}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ams2-career-companion-circuit-import");

var jsonOptions = new JsonSerializerOptions { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
int ok = 0, missing = 0;

foreach (var id in layoutIds)
{
    string svg;
    try
    {
        svg = await http.GetStringAsync(BaseUrl + id + ".svg");
    }
    catch (HttpRequestException)
    {
        missing++;
        Console.WriteLine($"  MISSING {id}");
        continue;
    }

    double w = ReadDouble(svg, "width") ?? 500;
    double h = ReadDouble(svg, "height") ?? 500;

    // Every <path d="..."> in document order; dedupe identical geometry (white-outline layers the same
    // path twice — the white style is usually a single path, but keep any distinct sub-paths).
    var paths = new JsonArray();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (Match m in Regex.Matches(svg, "\\bd=\"([^\"]+)\""))
    {
        string normalized = NormalizePathData(m.Groups[1].Value);
        if (normalized.Length > 0 && seen.Add(normalized))
            paths.Add(JsonValue.Create(normalized));
    }
    if (paths.Count == 0)
    {
        missing++;
        Console.WriteLine($"  NOPATH  {id}");
        continue;
    }

    var root = new JsonObject
    {
        ["source"] = "f1db circuit assets (CC BY 4.0)",
        ["w"] = w,
        ["h"] = h,
        ["paths"] = paths,
    };
    File.WriteAllText(Path.Combine(outDir, id + ".json"), root.ToJsonString(jsonOptions));
    ok++;
}

Console.WriteLine($"Wrote {ok} circuit files to {outDir}; {missing} missing/empty.");

static double? ReadDouble(string svg, string attr)
{
    var m = Regex.Match(svg, "\\b" + attr + "=\"([0-9.]+)");
    return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
}

// SVG path "d" -> WPF path mini-language: same commands (M/L/C/c/… + Z), but WPF's parser wants
// explicit separators. SVG omits them ("2.386-9.828" = two numbers; "-5.126.772" = "-5.126" then
// ".772"). Tokenize numbers correctly and re-emit space-separated.
static string NormalizePathData(string d)
{
    const string Commands = "MmLlHhVvCcSsQqTtAaZz";
    var sb = new StringBuilder(d.Length * 2);
    bool inNumber = false, seenDot = false;

    for (int i = 0; i < d.Length; i++)
    {
        char ch = d[i];
        if (Commands.IndexOf(ch) >= 0)
        {
            if (inNumber) { sb.Append(' '); inNumber = false; seenDot = false; }
            sb.Append(ch).Append(' ');
        }
        else if (ch is '-' or '+')
        {
            char prev = i > 0 ? d[i - 1] : ' ';
            if (inNumber && prev is not ('e' or 'E')) { sb.Append(' '); seenDot = false; } // new number (not an exponent sign)
            sb.Append(ch);
            inNumber = true;
        }
        else if (ch == '.')
        {
            if (inNumber && seenDot) { sb.Append(' '); }                                    // second dot => new number
            sb.Append(ch);
            seenDot = true;
            inNumber = true;
        }
        else if (char.IsDigit(ch) || ch is 'e' or 'E')
        {
            sb.Append(ch);
            inNumber = true;
        }
        else // whitespace, comma, or anything else -> a separator
        {
            if (inNumber) { sb.Append(' '); inNumber = false; seenDot = false; }
        }
    }

    return Regex.Replace(sb.ToString().Trim(), "\\s+", " ");
}
