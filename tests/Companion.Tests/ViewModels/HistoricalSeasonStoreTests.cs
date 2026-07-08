using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The shipped historical-results loader (data/history/&lt;year&gt;.json, f1db-derived):
/// parses a season on demand, caches it, and degrades to null on a missing dir / file / corrupt JSON —
/// the History tab must never crash on reference data. Camel-case JSON, exactly as the derivation tool
/// writes it.</summary>
public sealed class HistoricalSeasonStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("companion-history-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private const string Sample = """
        {"year":1967,"source":"f1db (CC BY 4.0)",
         "driversChampion":{"driver":"Denny Hulme","team":"Brabham","points":"51"},
         "constructorsChampion":{"team":"Brabham","points":"63"},
         "rounds":[
           {"round":1,"name":"South African Grand Prix","winner":"Pedro Rodríguez","winnerTeam":"Cooper","fastestLap":"Denny Hulme",
            "results":[{"pos":"1","driver":"Pedro Rodríguez","team":"Cooper"},
                       {"pos":"DNF","driver":"Jim Clark","team":"Lotus","status":"Engine"}]}]}
        """;

    [Fact]
    public void ForYear_parses_a_shipped_season_file()
    {
        File.WriteAllText(Path.Combine(_dir, "1967.json"), Sample);
        var store = new HistoricalSeasonStore(_dir);

        var season = store.ForYear(1967);

        Assert.NotNull(season);
        Assert.Equal(1967, season!.Year);
        Assert.Equal("Denny Hulme", season.DriversChampion!.Driver);
        Assert.Equal("Brabham", season.DriversChampion.Team);
        Assert.Equal("Brabham", season.ConstructorsChampion!.Team);

        var round = Assert.Single(season.Rounds);
        Assert.Equal("South African Grand Prix", round.Name);
        Assert.Equal("Pedro Rodríguez", round.Winner);
        Assert.Equal("Denny Hulme", round.FastestLap);
        Assert.Equal(2, round.Results.Count);
        Assert.Equal("Engine", round.Results[1].Status);
    }

    [Fact]
    public void ForYear_is_null_for_a_year_with_no_file()
    {
        var store = new HistoricalSeasonStore(_dir);
        Assert.Null(store.ForYear(1999));
    }

    [Fact]
    public void ForYear_is_null_for_a_missing_or_null_directory()
    {
        Assert.Null(new HistoricalSeasonStore(null).ForYear(1967));
        Assert.Null(new HistoricalSeasonStore(Path.Combine(_dir, "does-not-exist")).ForYear(1967));
    }

    [Fact]
    public void ForYear_is_null_for_corrupt_json_and_never_throws()
    {
        File.WriteAllText(Path.Combine(_dir, "1967.json"), "{ this is not json");
        var store = new HistoricalSeasonStore(_dir);

        Assert.Null(store.ForYear(1967));
    }

    [Fact]
    public void ForYear_caches_so_a_deleted_file_stays_resolved()
    {
        string path = Path.Combine(_dir, "1967.json");
        File.WriteAllText(path, Sample);
        var store = new HistoricalSeasonStore(_dir);

        Assert.NotNull(store.ForYear(1967)); // first read caches
        File.Delete(path);
        Assert.NotNull(store.ForYear(1967)); // still served from cache
    }
}
