using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The paddock MACHINE dossier wiring (SMGP-024): a real career on the shipped smgp-1 pack must
/// surface every team's permanent car and engine identity from the canon registry, with the
/// authored dossier lore attached. Proves the team card's machine block follows the canon even
/// after the winter reshuffle moves seats, since identity flows from the registry, not the grid.
/// </summary>
public sealed class SmgpMachineDossierTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-machine-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void EveryTeamCard_CarriesItsCanonicalMachineDossier()
    {
        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1"),
            CareerFilePath = Path.Combine(_root, "career.ams2career"),
            CareerName = "Machine Dossier",
            MasterSeed = 20260718,
            PlayerLiveryName = "Minarae #21 J. Nono",
            SmgpMode = true,
        }, Environment());

        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var paddock = session.SmgpPaddock();
        Assert.NotNull(paddock);
        Assert.Equal(24, paddock!.Teams.Count);

        foreach (var team in paddock.Teams)
        {
            var canonTeam = rules.SmgpCanon.ForTeam(team.TeamId);
            Assert.NotNull(canonTeam);
            var machine = team.Machine;
            Assert.NotNull(machine);
            Assert.Equal(canonTeam!.CarDisplayName, machine!.CarName);
            Assert.Equal(canonTeam.EngineDisplayName, machine.EngineName);
            Assert.False(string.IsNullOrWhiteSpace(machine.CarNaming), $"{team.TeamId}: empty car naming");
            Assert.False(string.IsNullOrWhiteSpace(machine.CarCharacter), $"{team.TeamId}: empty car character");
            Assert.Equal(3, machine.CarHistory.Count);
            Assert.False(string.IsNullOrWhiteSpace(machine.EngineNaming), $"{team.TeamId}: empty engine naming");
            Assert.False(string.IsNullOrWhiteSpace(machine.EngineCharacter), $"{team.TeamId}: empty engine character");
            Assert.Equal(3, machine.EngineHistory.Count);

            // The seventeen-season arc reveals as the career completes seasons (Mike's rule):
            // a fresh season-1 career shows no capsules yet, the future stays unspoiled.
            Assert.Empty(team.SeasonArc);
        }

        // Play season 1 out: exactly one capsule line (S01) unlocks per team, no more.
        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
            session.Apply(new ResultDraft
            {
                Classified = grid,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        foreach (var team in session.SmgpPaddock()!.Teams)
        {
            var arc = Assert.Single(team.SeasonArc);
            Assert.Equal(1, arc.Season);
            Assert.False(string.IsNullOrWhiteSpace(arc.Summary));
        }
    }

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = ViewModelTestData.RealLibrary.Value,
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [Path.Combine(AppContext.BaseDirectory, "packs")],
    };
}
