// Companion.StageGrid — resolve one round of a season pack into an AMS2 custom-AI file and
// DRY-RUN write it into an output folder. This tool NEVER writes into the game install: the
// --ams2 directory is read-only input (livery override scanning), and any outDir that resolves
// inside it is refused. Actual staging into UserData\CustomAIDrivers is the app's job, with
// the user in the loop.
//
// Usage:
//   Companion.StageGrid <packDir> <round> <outDir> [--ams2 <installDir>] [--player <liveryName>] [--data <ams2DataDir>]

using System.Globalization;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Grid;
using Companion.Ams2.Preflight;
using Companion.Core.Grid;
using Companion.Core.Packs;

const int ExitOk = 0;
const int ExitUsageOrLoadError = 1;
const int ExitPreflightErrors = 2;

// ---------- argument parsing ----------

var positional = new List<string>();
string? ams2Dir = null;
string? playerLivery = null;
string? dataDirArg = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ams2":
            if (++i >= args.Length) return Usage("--ams2 needs a value.");
            ams2Dir = args[i];
            break;
        case "--player":
            if (++i >= args.Length) return Usage("--player needs a value.");
            playerLivery = args[i];
            break;
        case "--data":
            if (++i >= args.Length) return Usage("--data needs a value.");
            dataDirArg = args[i];
            break;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                return Usage($"Unknown option '{args[i]}'.");
            positional.Add(args[i]);
            break;
    }
}

if (positional.Count != 3)
    return Usage($"Expected 3 positional arguments (packDir round outDir), got {positional.Count}.");

string packDir = Path.GetFullPath(positional[0]);
string outDir = Path.GetFullPath(positional[2]);
if (!int.TryParse(positional[1], NumberStyles.None, CultureInfo.InvariantCulture, out int round) || round < 1)
    return Usage($"'{positional[1]}' is not a round number (1 or greater).");

// ---------- hard rule: never write into the game install ----------

if (ams2Dir is not null)
{
    string installFull = Path.GetFullPath(ams2Dir);
    if (!Directory.Exists(installFull))
        return Fail($"--ams2 directory '{installFull}' does not exist.");
    if (IsUnder(outDir, installFull))
        return Fail(
            $"Refusing outDir '{outDir}': it is inside the AMS2 install '{installFull}'. " +
            "This tool is dry-run only — it never writes into the game install.");
    ams2Dir = installFull;
}

// ---------- load the pack ----------

SeasonPack pack;
try
{
    pack = PackLoader.Parse(
        ReadPackFile(packDir, "pack.json"),
        ReadPackFile(packDir, "season.json"),
        ReadPackFile(packDir, "teams.json"),
        ReadPackFile(packDir, "drivers.json"),
        ReadPackFile(packDir, "entries.json"));
}
catch (Exception ex) when (ex is IOException or FileNotFoundException or System.Text.Json.JsonException)
{
    return Fail($"Could not load the pack from '{packDir}': {ex.Message}");
}

// ---------- load the content library ----------

string dataDir = dataDirArg is not null
    ? Path.GetFullPath(dataDirArg)
    : Path.GetFullPath(Path.Combine(packDir, "..", "..", "data", "ams2"));

if (!File.Exists(Path.Combine(dataDir, "classes.json")))
    return Fail(
        $"AMS2 content library not found under '{dataDir}' (no classes.json). " +
        "Pass --data <repo>\\data\\ams2 explicitly.");

Ams2ContentLibrary library;
try
{
    library = Ams2ContentLibrary.Load(dataDir);
}
catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
{
    return Fail($"Could not load the content library from '{dataDir}': {ex.Message}");
}

// ---------- resolve, build, scan, preflight ----------

GridPlan plan;
try
{
    var player = playerLivery is null ? null : new PlayerSeat { Ams2LiveryName = playerLivery };
    plan = RoundGridResolver.Resolve(pack, round, player);
}
catch (InvalidOperationException ex)
{
    return Fail(ex.Message);
}

string headerComment =
    $"{pack.Manifest.Name} ({pack.Manifest.PackId} v{pack.Manifest.Version}) — " +
    $"round {plan.Round}: {plan.RoundName}. " +
    $"Generated {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}. DRY RUN.";
var file = GridStager.Build(plan, headerComment);

IReadOnlyCollection<InstalledLivery> installedLiveries = [];
var scanWarnings = new List<string>();
if (ams2Dir is not null)
{
    var roots = LiveryOverrideScanner.CandidateOverrideRoots(
        ams2Dir, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    (installedLiveries, var warnings) = LiveryOverrideScanner.Scan(roots);
    scanWarnings.AddRange(warnings);
}

// Preflight with the round's intended in-game grid (setup guide opponents + player) — the
// same number the M2 content validator checks against the venue cap. The generated file may
// legitimately carry MORE entries than grid slots (historical entry lists beat race grids);
// the game simply binds whichever liveries make the grid.
var packRound = pack.Season.Rounds.First(r => r.Round == plan.Round);
int gridSize = packRound.SetupGuide is { } guide ? guide.Session.Opponents + 1 : plan.Seats.Count;
var preflight = GridStager.Preflight(file, library, installedLiveries, plan.TrackId, gridSize);

// ---------- dry-run write ----------

string writtenPath = GridStager.DryRun(file, outDir);

// ---------- report ----------

Console.WriteLine($"== {pack.Manifest.Name} — round {plan.Round}: {plan.RoundName} ==");
Console.WriteLine($"Class {plan.Ams2Class} | track {plan.TrackId} | {plan.Seats.Count} seats | in-game grid {gridSize}");
if (plan.Seats.Count > gridSize)
    Console.WriteLine($"   note: {plan.Seats.Count} entries in the file, {gridSize} cars gridded in-game — extra entries are inert.");
Console.WriteLine();

foreach (var seat in plan.Seats)
{
    string flags = (seat.IsPlayer ? " [PLAYER]" : "") + (seat.IsGuest ? " [GUEST]" : "");
    string scalars = seat.WeightScalar == 1.0 && seat.PowerScalar == 1.0 && seat.DragScalar == 1.0
        ? ""
        : string.Format(CultureInfo.InvariantCulture,
            " | w={0:0.0##} p={1:0.0##} d={2:0.0##}", seat.WeightScalar, seat.PowerScalar, seat.DragScalar);
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  #{0,-3} {1,-24} {2,-18} race={3:0.00} qual={4:0.00} rel={5:0.00}{6}{7}",
        seat.Number ?? "-", seat.DriverName, seat.TeamName,
        seat.Ratings.RaceSkill, seat.Ratings.QualifyingSkill, seat.Reliability, scalars, flags));
    Console.WriteLine($"       livery: {seat.Ams2LiveryName}");
}

Console.WriteLine();
if (ams2Dir is null)
{
    Console.WriteLine("No --ams2 install given: livery bindings checked against the stock library only.");
}
else
{
    Console.WriteLine($"Scanned {installedLiveries.Count} installed livery override(s) from '{ams2Dir}' + Documents.");
    foreach (var warning in scanWarnings)
        Console.WriteLine($"  scan warning: {warning}");
}

Console.WriteLine();
if (preflight.Issues.Count == 0)
{
    Console.WriteLine("Preflight: clean — no issues.");
}
else
{
    Console.WriteLine($"Preflight: {preflight.Issues.Count} issue(s):");
    foreach (var issue in preflight.Issues)
        Console.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant()}: {issue.Message}");
}

Console.WriteLine();
Console.WriteLine($"Dry-run file written: {writtenPath}");
if (preflight.HasErrors)
{
    Console.WriteLine("PREFLIGHT ERRORS — this grid would not work as intended in-game.");
    return ExitPreflightErrors;
}
return ExitOk;

// ---------- helpers ----------

static string ReadPackFile(string dir, string filePart)
{
    string path = Path.Combine(dir, filePart);
    if (!File.Exists(path))
        throw new FileNotFoundException($"'{path}' is missing — a season pack is five files.", path);
    return File.ReadAllText(path);
}

static bool IsUnder(string path, string root)
{
    string relative = Path.GetRelativePath(root, path);
    return relative == "." ||
           (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return ExitUsageOrLoadError;
}

static int Usage(string message)
{
    Console.Error.WriteLine($"error: {message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("usage: Companion.StageGrid <packDir> <round> <outDir>");
    Console.Error.WriteLine("           [--ams2 <installDir>] [--player <liveryName>] [--data <ams2DataDir>]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  packDir   season pack folder (pack.json, season.json, teams.json, drivers.json, entries.json)");
    Console.Error.WriteLine("  round     1-based calendar round to resolve");
    Console.Error.WriteLine("  outDir    dry-run output folder for <class>.xml (NEVER the game install)");
    Console.Error.WriteLine("  --ams2    AMS2 install dir, read-only: scan installed livery overrides for preflight");
    Console.Error.WriteLine("  --player  livery name of the historical entry the player replaces");
    Console.Error.WriteLine("  --data    data/ams2 library dir (default: <packDir>\\..\\..\\data\\ams2)");
    return ExitUsageOrLoadError;
}
