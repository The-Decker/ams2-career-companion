using Companion.Core.Character;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The Driver dossier tab (character depth 3): a career with a character grows a Driver tab
/// in the hub showing the dossier; a character-free career does not.</summary>
public sealed class CharacterDossierHubTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dossier-hub-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterProfile Character() => new()
    {
        Name = "Nova Reyes",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.65, ["oneLap"] = 0.55, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.50,
        },
        PerkIds = ["rain_man"],
        CpUnspent = 1,
    };

    private CareerSessionService CreateCareer(CharacterProfile? character)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = Path.Combine(_root, "pack"),
                CareerFilePath = Path.Combine(_root, "career.ams2career"),
                CareerName = "Dossier Career",
                MasterSeed = 7,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = character,
            },
            environment);
    }

    [Fact]
    public void CharacterCareer_HubHasADriverTab_ShowingTheDossier()
    {
        using var hub = new HubViewModel(CreateCareer(Character()));

        Assert.Contains(hub.Tabs, t => t.Key == HubViewModel.DriverTabKey);
        Assert.Equal(2, hub.Tabs.ToList().FindIndex(t => t.Key == HubViewModel.DriverTabKey)); // after Standings

        Assert.True(hub.Dossier.HasCharacter);
        Assert.Equal("Nova Reyes", hub.Dossier.Dossier!.Name);
        Assert.Equal(1, hub.Dossier.Dossier.Level);
        Assert.Equal(7, hub.Dossier.Dossier.Stats.Count);
        Assert.Contains(hub.Dossier.Dossier.Perks, p => p.Id == "rain_man");
    }

    [Fact]
    public void Dossier_ShowsTheTeamAndSeasonTheDriverRacesFor()
    {
        using var session = CreateCareer(Character());
        var vm = new DossierViewModel(session);

        Assert.NotNull(vm.TeamLine);
        Assert.Contains("Brabham-Repco", vm.TeamLine); // the player's seat team (StockLivery2)
        Assert.Contains("1967", vm.TeamLine);
    }

    [Fact]
    public void CharacterFreeCareer_HubHasNoDriverTab()
    {
        using var hub = new HubViewModel(CreateCareer(character: null));

        Assert.DoesNotContain(hub.Tabs, t => t.Key == HubViewModel.DriverTabKey);
        Assert.False(hub.Dossier.HasCharacter);
    }
}
