using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-024 canon lock, made executable. Encodes the mission's 24-row table as the
/// expectation and asserts the shipped registry (<c>data/rules/smgp/canon.json</c>, parsed by
/// <see cref="SmgpCanon"/>) reproduces it exactly, for every one of the 408 team-season
/// identity combinations (24 teams x 17 seasons). The lock rule: team name, car name, and
/// engine name are permanent across all 17 seasons; only the season/year changes. This test
/// table is the mission's source of truth expressed as a guard; app code must never read it,
/// runtime identity flows through <see cref="SmgpCanon"/> alone.
/// </summary>
public sealed class SmgpCanonLockTests
{
    /// <summary>The mission's canonical 24-row table: pack team id, team, permanent car,
    /// permanent engine. Order is the mission's numbering, tiers A to D then the SMGP II era.</summary>
    private static readonly (string Id, string Team, string Car, string Engine)[] Lock =
    [
        ("team.madonna", "MADONNA", "MADONNA 456", "PALM 190 V10"),
        ("team.firenze", "FIRENZE", "FIRENZE 500", "FIRENZE 99 V12"),
        ("team.millions", "MILLIONS", "MILLIONS 189", "DICK MD V10"),
        ("team.bestowal", "BESTOWAL", "BESTOWAL 167", "VAPOR DN"),
        ("team.blanche", "BLANCHE", "BLANCHE 582", "DELTA 103 V10"),
        ("team.tyrant", "TYRANT", "TYRANT 548", "LIZZIE 24 V8"),
        ("team.losel", "LOSEL", "LOSEL 125", "VAPOR DNPQ V8"),
        ("team.may", "MAY", "MAY 555", "LORRY 32 V8"),
        ("team.bullets", "BULLETS", "BULLETS 560", "LIZZIE 24 V8"),
        ("team.dardan", "DARDAN", "DARDAN 700", "VAPOR DNPQ V8"),
        ("team.linden", "LINDEN", "LINDEN LN198", "LORRY 32 V8"),
        ("team.minarae", "MINARAE", "MINARAE 594", "SEGA SG1000 V8"),
        ("team.rigel", "RIGEL", "RIGEL 3000", "LORRY 32 V8"),
        ("team.comet", "COMET", "COMET 323", "LIZZIE 24 V8"),
        ("team.orchis", "ORCHIS", "ORCHIS 056", "MISFIRE 50 V8"),
        ("team.zeroforce", "ZEROFORCE", "ZEROFORCE 231", "LORRY 32 V8"),
        ("team.joke", "JOKE", "JOKE 777", "POND V8"),
        ("team.lares", "LARES", "LARES 92", "RAM V12"),
        ("team.feet", "FEET", "FEET 13", "YOUGEN V10"),
        ("team.serga", "SERGA", "SERGA 1000", "SC3000 F12"),
        ("team.cool", "COOL", "COOL 05", "CORSE V8"),
        ("team.moon", "MOON", "MOON 292", "RAM V12"),
        ("team.iris", "IRIS", "IRIS 717", "PRISM 90 V10"),
        ("team.azalea", "AZALEA", "AZALEA 808", "BLOOM 88 V8"),
    ];

    public static IEnumerable<object[]> All408Combinations
    {
        get
        {
            foreach (var row in Lock)
                for (int season = 1; season <= 17; season++)
                    yield return [row.Id, row.Team, row.Car, row.Engine, season];
        }
    }

    private static SmgpCanon LoadShippedCanon()
    {
        string path = Path.Combine(RepoRoot(), "data", "rules", "smgp", "canon.json");
        Assert.True(File.Exists(path), $"canon.json missing at {path}");
        return SmgpCanon.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void TheShippedRegistry_IsStructurallyValid()
    {
        var canon = LoadShippedCanon();
        Assert.Equal("smgp-24-v1", canon.CanonVersion);
        Assert.Equal("smgp", canon.Mode);
        Assert.Equal(17, canon.Seasons);
        Assert.Equal(24, canon.Teams.Count);
        Assert.Equal(17, canon.Engines.Count);
        Assert.Equal(24, canon.Teams.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(24, canon.Teams.Select(t => t.CarId).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(canon.Validate());
    }

    [Fact]
    public void TheMissionTable_MatchesTheRegistryExactly_OncePerTeam()
    {
        var canon = LoadShippedCanon();
        Assert.Equal(
            Lock.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal),
            canon.Teams.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal));
        foreach (var (id, team, car, engine) in Lock)
        {
            var row = Assert.Single(canon.Teams, t => t.Id == id);
            Assert.Equal(team, row.DisplayName);
            Assert.Equal(car, row.CarDisplayName);
            Assert.Equal(engine, row.EngineDisplayName);
            Assert.True(row.IdentityLocked);
            Assert.Equal(1, row.FirstSeason);
            Assert.Equal(17, row.LastSeason);
        }
    }

    [Theory]
    [MemberData(nameof(All408Combinations))]
    public void TeamSeasonIdentity_NeverChangesAcrossTheSeventeenSeasons(
        string teamId, string team, string car, string engine, int season)
    {
        var canon = LoadShippedCanon();
        var identity = canon.SeasonIdentity(teamId, season);
        Assert.NotNull(identity);
        Assert.Equal(season, identity!.Season);
        Assert.Equal(teamId, identity.TeamId);
        Assert.Equal(team, identity.TeamDisplayName);
        Assert.Equal(car, identity.CarDisplayName);
        Assert.Equal(engine, identity.EngineDisplayName);
        Assert.DoesNotContain($"{1989 + season}", identity.CarDisplayName);
        Assert.DoesNotContain($"{1989 + season}", identity.EngineDisplayName);
    }

    [Fact]
    public void ExactnessPins_SurviveNormalizationPressure()
    {
        var canon = LoadShippedCanon();
        Assert.Equal("ORCHIS 056", canon.ForTeam("team.orchis")!.CarDisplayName);
        Assert.Equal("COOL 05", canon.ForTeam("team.cool")!.CarDisplayName);
        Assert.Equal("VAPOR DN", canon.ForTeam("team.bestowal")!.EngineDisplayName);
        Assert.Equal("SC3000 F12", canon.ForTeam("team.serga")!.EngineDisplayName);
        Assert.Equal("SERGA 1000", canon.ForTeam("team.serga")!.CarDisplayName);
        Assert.Equal("SEGA SG1000 V8", canon.ForTeam("team.minarae")!.EngineDisplayName);
        Assert.Equal("LINDEN LN198", canon.ForTeam("team.linden")!.CarDisplayName);
        Assert.Equal("RIGEL 3000", canon.ForTeam("team.rigel")!.CarDisplayName);
        Assert.Equal("ZEROFORCE 231", canon.ForTeam("team.zeroforce")!.CarDisplayName);
    }

    [Fact]
    public void SharedEngines_StayOneEngine_WithSeparateTeams()
    {
        var canon = LoadShippedCanon();
        var lizzie = canon.Teams.Where(t => t.EngineId == "lizzie-24-v8").Select(t => t.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(["team.bullets", "team.comet", "team.tyrant"], lizzie);
        var lorry = canon.Teams.Where(t => t.EngineId == "lorry-32-v8").Select(t => t.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(["team.linden", "team.may", "team.rigel", "team.zeroforce"], lorry);
        var vaporDnpq = canon.Teams.Where(t => t.EngineId == "vapor-dnpq-v8").Select(t => t.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(["team.dardan", "team.losel"], vaporDnpq);
        var ram = canon.Teams.Where(t => t.EngineId == "ram-v12").Select(t => t.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(["team.lares", "team.moon"], ram);
        // VAPOR DN and VAPOR DNPQ V8 are distinct official specifications.
        Assert.NotNull(canon.ForEngine("vapor-dn"));
        Assert.NotNull(canon.ForEngine("vapor-dnpq-v8"));
        Assert.Single(canon.Teams, t => t.EngineId == "vapor-dn");
    }

    [Fact]
    public void IrisAndAzalea_ExistExactlyOnce_AndAliasesNormalize()
    {
        var canon = LoadShippedCanon();
        Assert.Single(canon.Teams, t => t.DisplayName == "IRIS");
        Assert.Single(canon.Teams, t => t.DisplayName == "AZALEA");
        Assert.DoesNotContain(canon.Teams, t =>
            t.DisplayName.Contains("LOTUS", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.Contains("AZELIA", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.Contains("AZALIA", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("team.azalea", canon.NormalizeTeamName("AZELIA"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("Azalia"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("azaleah"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("Team Azalea"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("AZALEA RACING"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("Azalea"));
        Assert.Equal("team.azalea", canon.NormalizeTeamName("team.azalea"));
        Assert.Equal("team.iris", canon.NormalizeTeamName("IRIS"));
        Assert.Equal("team.iris", canon.NormalizeTeamName("lotus")); // scoped SMGP alias
        Assert.Null(canon.NormalizeTeamName("Ferrari"));
        Assert.Null(canon.NormalizeTeamName(null));
        Assert.Null(canon.NormalizeTeamName(""));
    }

    [Fact]
    public void EveryPackTeam_IsInTheCanon_ExactlyOnce()
    {
        string teamsJson = File.ReadAllText(
            Path.Combine(RepoRoot(), "packs", "smgp-1", "teams.json"));
        var canon = LoadShippedCanon();
        using var doc = System.Text.Json.JsonDocument.Parse(teamsJson);
        var packTeamIds = doc.RootElement.GetProperty("teams").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString()!)
            .ToArray();
        Assert.Equal(24, packTeamIds.Length);
        foreach (string id in packTeamIds)
            Assert.Single(canon.Teams, t => t.Id == id);
        foreach (var team in canon.Teams)
            Assert.Contains(team.Id, packTeamIds);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Companion.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Companion.slnx not found above the test output directory.");
        return dir.FullName;
    }
}
