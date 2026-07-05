using System.Text.Encodings.Web;
using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Numerics;
using Companion.Core.Scoring;
using Companion.FixtureGen;
using Microsoft.Data.Sqlite;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: Companion.FixtureGen <f1db.db> <f1-points-systems.json> <outDir>");
    return 1;
}

string dbPath = Path.GetFullPath(args[0]);
string catalogPath = Path.GetFullPath(args[1]);
string outDir = Path.GetFullPath(args[2]);

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"f1db database not found: {dbPath}");
    return 1;
}
if (!File.Exists(catalogPath))
{
    Console.Error.WriteLine($"Points-system catalog not found: {catalogPath}");
    return 1;
}

var catalog = PointsSystemCatalog.Parse(File.ReadAllText(catalogPath));
Directory.CreateDirectory(outDir);

// CoreJson.Options plus relaxed escaping so constructor ids serialize as literal
// "cooper+maserati" rather than an escaped-plus form (fixtures are hand-read and diffed).
var jsonOptions = new JsonSerializerOptions(CoreJson.Options)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly,
}.ConnectionString);
connection.Open();

var droppedByPositionText = new Dictionary<string, int>(StringComparer.Ordinal);
var anomalies = new List<string>();
var derivedPointsNotes = new List<string>();
int written = 0;

foreach (int year in QueryYears(connection))
{
    if (!catalog.Seasons.TryGetValue(year, out var season))
    {
        anomalies.Add($"{year}: no catalog season — skipped.");
        Console.Error.WriteLine($"WARN {year}: not in the points-system catalog, skipping.");
        continue;
    }

    var races = QueryRaces(connection, year);
    var rounds = new List<FixtureRound>(races.Count);

    foreach (var race in races)
    {
        var overrides = season.RoundOverrides.Where(o => o.GrandPrix == race.GrandPrixId).ToList();
        var pointsFactor = overrides.Select(o => o.PointsFactor).FirstOrDefault(f => f is not null) ?? Rational.One;
        bool countsForConstructors = overrides.Select(o => o.CountsForConstructors).FirstOrDefault(c => c is not null) ?? true;
        string? alternateRaceTableId = overrides.Select(o => o.AlternateRaceTableId).FirstOrDefault(id => id is not null);

        // The drivers table actually paid at this round: the alternate (shortened-race) table
        // when the catalog selects one, the season table otherwise. Drives the v2
        // pointsEligible/pointsPosition derivation.
        var raceTable = alternateRaceTableId is null
            ? season.RacePoints
            : season.AlternateRaceTables?[alternateRaceTableId]
              ?? throw new InvalidOperationException(
                  $"{year} {race.GrandPrixId}: alternate table '{alternateRaceTableId}' not in catalog.");

        var sessions = BuildSessions(
            connection, race, year, season.ConstructorEntitySplits, raceTable, pointsFactor,
            droppedByPositionText, derivedPointsNotes);
        if (sessions is null)
        {
            anomalies.Add($"{year} round {race.Round} ({race.GrandPrixId}): no race result rows — round omitted.");
            continue;
        }

        rounds.Add(new FixtureRound
        {
            Round = race.Round,
            GrandPrixId = race.GrandPrixId,
            PointsFactor = pointsFactor,
            CountsForConstructors = countsForConstructors,
            AlternateRaceTableId = alternateRaceTableId,
            Sessions = sessions,
        });
    }

    if (rounds.Count == 0)
    {
        anomalies.Add($"{year}: no rounds with results — season skipped.");
        continue;
    }

    var fixture = new Fixture
    {
        Year = year,
        RoundCount = races.Count,
        Rounds = rounds,
        ExpectedDrivers = QueryExpectedDrivers(connection, year),
        ExpectedConstructors = QueryExpectedConstructors(connection, year, season.ConstructorEntitySplits),
    };

    string path = Path.Combine(outDir, $"{year}.json");
    File.WriteAllText(path, JsonSerializer.Serialize(fixture, jsonOptions) + Environment.NewLine);
    written++;

    int sprints = rounds.Count(r => r.Sessions.Any(s => s.Kind == SessionKind.Sprint));
    Console.WriteLine(
        $"{year}: {rounds.Count}/{races.Count} rounds" +
        (sprints > 0 ? $", {sprints} sprint" : "") +
        $", {fixture.ExpectedDrivers.Count} drivers" +
        (fixture.ExpectedConstructors is { } c ? $", {c.Count} constructors" : ""));
}

Console.WriteLine($"\n{written} fixture(s) written to {outDir}");
foreach (var (text, count) in droppedByPositionText.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    Console.WriteLine($"dropped {count} row(s) with positionText '{text}' (never started the race)");
Console.WriteLine($"\n{derivedPointsNotes.Count} derived points-divergence row(s):");
foreach (var note in derivedPointsNotes)
    Console.WriteLine($"derived: {note}");
foreach (var anomaly in anomalies)
    Console.WriteLine($"note: {anomaly}");
return 0;

static List<int> QueryYears(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT DISTINCT year FROM race ORDER BY year";
    using var reader = command.ExecuteReader();
    var years = new List<int>();
    while (reader.Read())
        years.Add(reader.GetInt32(0));
    return years;
}

static List<RaceRow> QueryRaces(SqliteConnection connection, int year)
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT id, round, grand_prix_id FROM race WHERE year = $year ORDER BY round";
    command.Parameters.AddWithValue("$year", year);
    using var reader = command.ExecuteReader();
    var races = new List<RaceRow>();
    while (reader.Read())
        races.Add(new RaceRow(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2)));
    return races;
}

/// <summary>Race session first, then the sprint when the round had one. Null when the round has
/// no race-result rows at all (a scheduled but not yet run race).</summary>
static List<FixtureSession>? BuildSessions(
    SqliteConnection connection, RaceRow race, int year,
    IReadOnlyList<CatalogEntitySplit> entitySplits, IReadOnlyList<Rational> raceTable,
    Rational pointsFactor, Dictionary<string, int> droppedByPositionText, List<string> derivedPointsNotes)
{
    var raceRows = QueryEntries(connection, race, "RACE_RESULT", entitySplits, droppedByPositionText);
    if (raceRows.Count == 0)
        return null;

    var fastestLapDriverIds = QueryFastestLapHolders(connection, race.Id);
    var sessions = new List<FixtureSession>
    {
        new()
        {
            Kind = SessionKind.Race,
            FastestLapDriverIds = fastestLapDriverIds,
            Entries = ToRaceEntries(
                raceRows, raceTable, pointsFactor, fastestLapDriverIds, year, race, derivedPointsNotes),
        },
    };

    var sprintRows = QueryEntries(connection, race, "SPRINT_RACE_RESULT", entitySplits, droppedByPositionText);
    if (sprintRows.Count > 0)
        sessions.Add(new FixtureSession
        {
            Kind = SessionKind.Sprint,
            FastestLapDriverIds = [], // sprint fastest laps are never emitted
            // Sprint rows do carry race_points in f1db, but no sprint has ever officially
            // diverged from position-based scoring, so the v2 eligibility/redistribution
            // derivation is race-only by design.
            Entries = sprintRows.Select(row => row.ToEntry(pointsEligible: true, pointsPosition: null)).ToList(),
        });

    return sessions;
}

static List<RawEntry> QueryEntries(
    SqliteConnection connection, RaceRow race, string type,
    IReadOnlyList<CatalogEntitySplit> entitySplits, Dictionary<string, int> droppedByPositionText)
{
    using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT position_number, position_text, driver_id, constructor_id, engine_manufacturer_id,
               race_shared_car, race_points
        FROM race_data
        WHERE race_id = $raceId AND type = $type
        ORDER BY position_display_order
        """;
    command.Parameters.AddWithValue("$raceId", race.Id);
    command.Parameters.AddWithValue("$type", type);

    using var reader = command.ExecuteReader();
    var entries = new List<RawEntry>();
    while (reader.Read())
    {
        string positionText = reader.GetString(1);
        var status = MapStatus(positionText, race);
        if (status is null)
        {
            // DNQ/DNPQ/DNP: never took the start, not part of the classification.
            droppedByPositionText[positionText] = droppedByPositionText.GetValueOrDefault(positionText) + 1;
            continue;
        }

        string constructorId = reader.GetString(3) + "+" + reader.GetString(4);
        foreach (var split in entitySplits)
        {
            // Mid-season entity change (2018 Force India): from the split round onward the
            // same chassis+engine races as the successor championship entity.
            if (race.Round >= split.FromRound && constructorId == split.Constructor + "+" + split.Engine)
                constructorId = split.NewId;
        }

        entries.Add(new RawEntry(
            DriverId: reader.GetString(2),
            ConstructorId: constructorId,
            Position: status == FinishStatus.Classified
                ? reader.IsDBNull(0) ? int.Parse(positionText) : reader.GetInt32(0)
                : null,
            Status: status.Value,
            SharedDrive: !reader.IsDBNull(5) && reader.GetBoolean(5),
            RacePoints: reader.IsDBNull(6) ? null : reader.GetDouble(6)));
    }
    return entries;
}

/// <summary>Derives the v2 pointsEligible/pointsPosition fields from f1db's per-row
/// race_points, per the contract's Mapping rules (docs/dev/oracle-fixtures.md).</summary>
static List<FixtureEntry> ToRaceEntries(
    List<RawEntry> rows, IReadOnlyList<Rational> raceTable, Rational pointsFactor,
    IReadOnlyList<string> fastestLapDriverIds, int year, RaceRow race, List<string> derivedPointsNotes)
{
    // Shared-drive groups: any same-position row flagged sharedCar, or >1 rows at the position.
    // Their NULL/split race_points express the shared-drive policy, not points divergence.
    var positionGroups = rows
        .Where(r => r.Status == FinishStatus.Classified && r.Position is not null)
        .GroupBy(r => r.Position!.Value)
        .ToDictionary(g => g.Key, g => g.ToList());

    var entries = new List<FixtureEntry>(rows.Count);
    foreach (var row in rows)
    {
        bool pointsEligible = true;
        int? pointsPosition = null;

        if (row is { Status: FinishStatus.Classified, Position: { } position })
        {
            var group = positionGroups[position];
            bool sharedGroup = group.Count > 1 || group.Any(r => r.SharedDrive);

            if (!sharedGroup && position <= raceTable.Count && row.RacePoints is null or 0)
            {
                // Classified inside the points places, yet officially scored nothing:
                // ineligible entry (F2 cars, unregistered second cars, annulled results).
                pointsEligible = false;
                derivedPointsNotes.Add($"{year} {race.GrandPrixId} P{position} {row.DriverId}: pointsEligible=false");
            }
            else if (!sharedGroup
                     && pointsFactor == Rational.One
                     && row.RacePoints is { } paid
                     && paid > 0
                     && paid == Math.Floor(paid)
                     && !fastestLapDriverIds.Contains(row.DriverId, StringComparer.Ordinal))
            {
                // Integer points exactly matching a DIFFERENT position's table value: official
                // scoring paid a divergent rank (1967/1969 German GPs). Fastest-lap holders are
                // exempt — 1950s race_points may embed the fastest-lap point.
                for (int i = 0; i < raceTable.Count; i++)
                {
                    if (!raceTable[i].IsInteger || raceTable[i].ToDouble() != paid)
                        continue;
                    if (i + 1 != position)
                    {
                        pointsPosition = i + 1;
                        derivedPointsNotes.Add(
                            $"{year} {race.GrandPrixId} P{position} {row.DriverId}: pointsPosition={i + 1}");
                    }
                    break;
                }
            }
        }

        entries.Add(row.ToEntry(pointsEligible, pointsPosition));
    }
    return entries;
}

/// <summary>Maps f1db positionText per the contract; null means "drop the row entirely".
/// Unknown values fail generation so nothing is ever silently misclassified.</summary>
static FinishStatus? MapStatus(string positionText, RaceRow race) => positionText switch
{
    _ when positionText.Length > 0 && positionText.All(char.IsAsciiDigit) => FinishStatus.Classified,
    "DNF" => FinishStatus.Retired,
    "DSQ" => FinishStatus.Disqualified,
    "NC" => FinishStatus.NotClassified,
    "DNS" => FinishStatus.DidNotStart,
    "EX" => FinishStatus.Excluded,
    "DNQ" or "DNPQ" or "DNP" => null,
    _ => throw new InvalidOperationException(
        $"Unmapped positionText '{positionText}' at race {race.Id} ({race.GrandPrixId}, round {race.Round})."),
};

/// <summary>Every holder of the round's fastest time: rank-1 rows of the FASTEST_LAP data
/// (ties share position_number 1 — the 1954 British GP has seven).</summary>
static List<string> QueryFastestLapHolders(SqliteConnection connection, long raceId)
{
    using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT driver_id FROM race_data
        WHERE race_id = $raceId AND type = 'FASTEST_LAP' AND position_number = 1
        ORDER BY position_display_order
        """;
    command.Parameters.AddWithValue("$raceId", raceId);
    using var reader = command.ExecuteReader();
    var holders = new List<string>();
    while (reader.Read())
        holders.Add(reader.GetString(0));
    return holders;
}

static List<ExpectedCompetitor> QueryExpectedDrivers(SqliteConnection connection, int year)
{
    using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT driver_id, position_number, points FROM season_driver_standing
        WHERE year = $year ORDER BY position_display_order
        """;
    command.Parameters.AddWithValue("$year", year);
    using var reader = command.ExecuteReader();
    var expected = new List<ExpectedCompetitor>();
    while (reader.Read())
        expected.Add(new ExpectedCompetitor
        {
            Id = reader.GetString(0),
            Position = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            Points = reader.GetDouble(2),
        });
    return expected;
}

/// <summary>Null (property omitted) for seasons before the constructors championship existed —
/// f1db's season_constructor_standing simply has no rows before 1958.</summary>
static List<ExpectedCompetitor>? QueryExpectedConstructors(
    SqliteConnection connection, int year, IReadOnlyList<CatalogEntitySplit> entitySplits)
{
    using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT constructor_id, engine_manufacturer_id, position_number, points
        FROM season_constructor_standing
        WHERE year = $year ORDER BY position_display_order
        """;
    command.Parameters.AddWithValue("$year", year);
    using var reader = command.ExecuteReader();
    var expected = new List<ExpectedCompetitor>();
    while (reader.Read())
    {
        string id = reader.GetString(0) + "+" + reader.GetString(1);
        int? position = reader.IsDBNull(2) ? null : reader.GetInt32(2);
        double points = reader.GetDouble(3);

        // f1db keys both halves of a split entity identically (2018: two force-india+mercedes
        // rows). The scoring row belongs to the successor entity; the 0-point/positionless
        // excluded row keeps the original id.
        var split = entitySplits.FirstOrDefault(s => s.Constructor + "+" + s.Engine == id);
        if (split is not null && (points != 0 || position is not null))
            id = split.NewId;

        expected.Add(new ExpectedCompetitor
        {
            Id = id,
            Position = position,
            Points = points,
        });
    }
    return expected.Count > 0 ? expected : null;
}

internal sealed record RaceRow(long Id, int Round, string GrandPrixId);

/// <summary>One race_data row after status mapping, before the v2 points-divergence
/// derivation (which needs the whole session to see shared-drive groups).</summary>
internal sealed record RawEntry(
    string DriverId, string ConstructorId, int? Position, FinishStatus Status,
    bool SharedDrive, double? RacePoints)
{
    public FixtureEntry ToEntry(bool pointsEligible, int? pointsPosition) => new()
    {
        DriverId = DriverId,
        ConstructorId = ConstructorId,
        Position = Position,
        Status = Status,
        SharedDrive = SharedDrive,
        PointsEligible = pointsEligible,
        PointsPosition = pointsPosition,
    };
}
