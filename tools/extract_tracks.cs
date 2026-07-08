#:property JsonSerializerIsReflectionEnabledByDefault=true
// extract_tracks — the missing analog of Companion.ContentExtract for TRACKS. Parses every loose
// Tracks\<folder>\*.trd in an AMS2 install (plain XML; the <data class="TrackDetails"> block) and
// emits the true track "tag" (the on-disk folder = what the app checks to confirm a mod is
// installed) alongside the in-game TrackName and metadata.
//
// WHY: base + DLC tracks are packed (no loose .trd), so this sees the LOOSE folders — i.e. the
// installed MOD tracks (RockyTM / PCMT etc.). Modders use code names you cannot guess
// (heusden = Zolder, moravia = Brno_GP, florence_gp = Mugello, lakeville_raceway_gp = Sonoma,
// emirates_raceway_gp = Dubai), so the true tag must be READ from the files, never guessed.
//
// Usage:
//   dotnet run tools/extract_tracks.cs -- "<AMS2 install dir>" [outJsonPath]
//   (no outJsonPath => prints the table to stdout only)

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: extract_tracks.cs -- <ams2InstallDir> [outJsonPath]");
    return 2;
}

string installDir = Path.GetFullPath(args[0]);
string tracksRoot = Path.Combine(installDir, "Tracks");
if (!Directory.Exists(tracksRoot))
{
    Console.Error.WriteLine($"'{tracksRoot}' does not exist — not an AMS2 install?");
    return 2;
}

// prop name -> our field. Everything else in TrackDetails is ignored.
var rows = new List<InstalledTrack>();
int trdCount = 0, failures = 0;

foreach (string dir in Directory.EnumerateDirectories(tracksRoot).Order(StringComparer.Ordinal))
{
    string folder = Path.GetFileName(dir);
    // Prefer <folder>.trd; fall back to any single .trd in the folder.
    string? trd = File.Exists(Path.Combine(dir, folder + ".trd"))
        ? Path.Combine(dir, folder + ".trd")
        : Directory.EnumerateFiles(dir, "*.trd").Order(StringComparer.Ordinal).FirstOrDefault();
    if (trd is null)
        continue;

    trdCount++;
    try
    {
        var props = ParseTrackDetails(trd);
        rows.Add(new InstalledTrack
        {
            Tag = folder,                                   // the on-disk tag (install path + preflight key)
            TrackName = props.GetValueOrDefault("TrackName"),
            ShortTrackName = props.GetValueOrDefault("ShortTrackName"),
            Location = props.GetValueOrDefault("Location"),
            Country = props.GetValueOrDefault("Country"),
            Year = IntProp(props, "Year"),
            LengthMeters = IntProp(props, "Length"),
            MaxAiParticipants = IntProp(props, "Max AI participants"),
            TrackType = props.GetValueOrDefault("Track Type"),
            TrackGrade = props.GetValueOrDefault("TrackGradeFilter"),
            EventTypes = props.GetValueOrDefault("Event Types"),
            OvalType = IntProp(props, "Oval Type"),
            IsClockwise = BoolProp(props, "Is Clockwise"),
            AllowedWeather = props.GetValueOrDefault("Allowed Weather"),
            DlcId = props.GetValueOrDefault("DLC ID"),
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"PARSE FAILURE {trd}: {ex.Message}");
        failures++;
    }
}

rows.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

// -- report -----------------------------------------------------------------
Console.WriteLine($"=== {rows.Count} loose track folders in {tracksRoot} ({failures} parse failures) ===\n");
Console.WriteLine($"{"TAG (folder)",-32} {"TrackName",-28} {"Location",-16} {"m",6} {"AI",3}  DLC");
foreach (var t in rows)
    Console.WriteLine($"{t.Tag,-32} {t.TrackName,-28} {t.Location,-16} {t.LengthMeters,6} {t.MaxAiParticipants,3}  {t.DlcId}");

if (args.Length == 2)
{
    string outPath = Path.GetFullPath(args[1]);
    var json = JsonSerializer.Serialize(
        new { extractedFrom = $"loose Tracks\\*.trd under {installDir}", tracks = rows },
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    File.WriteAllText(outPath, json + Environment.NewLine);
    Console.WriteLine($"\nwritten: {outPath}");
}
return failures > 0 ? 1 : 0;

// ---------------------------------------------------------------------------

static Dictionary<string, string> ParseTrackDetails(string trdPath)
{
    var doc = XDocument.Load(trdPath);
    var data = doc.Root!.Elements("data")
                   .FirstOrDefault(e => (string?)e.Attribute("class") == "TrackDetails")
               ?? throw new InvalidDataException("no <data class=\"TrackDetails\"> element");

    var props = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var prop in data.Elements("prop"))
    {
        string? name = (string?)prop.Attribute("name");
        string? value = (string?)prop.Attribute("data");
        if (name is not null && value is not null && !props.ContainsKey(name))
            props[name] = value;
    }
    return props;
}

static int IntProp(Dictionary<string, string> props, string name) =>
    props.TryGetValue(name, out string? v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
        ? n : 0;

static bool BoolProp(Dictionary<string, string> props, string name) =>
    props.TryGetValue(name, out string? v) && bool.TryParse(v, out bool b) && b;

sealed record InstalledTrack
{
    public required string Tag { get; init; }
    public string? TrackName { get; init; }
    public string? ShortTrackName { get; init; }
    public string? Location { get; init; }
    public string? Country { get; init; }
    public int Year { get; init; }
    public int LengthMeters { get; init; }
    public int MaxAiParticipants { get; init; }
    public string? TrackType { get; init; }
    public string? TrackGrade { get; init; }
    public string? EventTypes { get; init; }
    public int OvalType { get; init; }
    public bool IsClockwise { get; init; }
    public string? AllowedWeather { get; init; }
    public string? DlcId { get; init; }
}
