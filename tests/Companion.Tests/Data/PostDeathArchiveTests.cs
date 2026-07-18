using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Newsroom;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The POST-DEATH ARCHIVE: a NORMAL-mode death is terminal but never destructive, the career file
/// reopens as a read-only memorial (mortality status, the newsroom's stories including the PlayerDied
/// event, and the career timeline all stay viewable), Apply stays refused, and an entirely NEW career
/// remains creatable afterward. The death is forced deterministically with an out-of-range durability
/// (a huge negative safety offset), the AccidentFoldDeterminismTests pattern.
/// </summary>
public sealed class PostDeathArchiveTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-postdeath-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterProfile Character(double durability) => new()
    {
        Name = "Crash McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = durability,
        },
        PerkIds = [],
        CpUnspent = 0,
    };

    private CareerEnvironment Environment(string name) => ViewModelTestData.Environment(
        documentsDirectory: Path.Combine(_root, name, "docs"),
        library: TestPackBuilder.Library());

    private CareerSessionService Create(string name, double durability)
    {
        string packDirectory = Path.Combine(_root, name, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, name, name + ".ams2career"),
                CareerName = name,
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = Character(durability),
                Mortality = MortalityMode.Normal,
            },
            Environment(name));
    }

    private static void ApplyFatalAccident(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Where(s => s.DriverId != PlayerId).Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string> { [PlayerId] = "a" },
            Disqualified = [],
            PlayerAccidentSeverity = AccidentSeverity.Heavy,
        });
    }

    private static ResultDraft NormalRoundDraft(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        return new ResultDraft
        {
            Classified = seats.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        };
    }

    [Fact]
    public void NormalDeath_TheArchiveReopensViewable_ApplyStaysRefused_AndNewCareersRemainCreatable()
    {
        // ---- force a Normal-mode death on round 1 (durability -50 ⇒ the d500 lands fatal) ----
        string careerPath;
        using (var session = Create("memorial", durability: -50.0))
        {
            careerPath = session.CareerFilePath;
            ApplyFatalAccident(session);
            Assert.True(session.PlayerMortality().Deceased);
            Assert.False(session.PlayerMortality().CareerFileDeleted); // Normal keeps the file
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.True(File.Exists(careerPath));

        // ---- (a) the career file reopens as a viewable archive ----
        using (var reopened = CareerSessionService.OpenCareer(careerPath, Environment("memorial")))
        {
            var status = reopened.PlayerMortality();
            Assert.True(status.Deceased);
            Assert.Equal(MortalityMode.Normal, status.Mode);

            // The newsroom still tells the career's whole story, including the death itself.
            var events = reopened.NewsroomEvents();
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.Kind == NewsEventKind.PlayerDied);

            var feed = reopened.NewsroomFeed();
            Assert.NotEmpty(feed);
            Assert.Contains(feed, a => a.EventKind == NewsEventKind.PlayerDied);

            // The scrapbook stays readable: the season the driver raced is still in the timeline.
            var timeline = reopened.CareerTimeline();
            Assert.NotEmpty(timeline.Seasons);
            Assert.Equal(1967, timeline.Seasons[0].SeasonYear);

            // ---- (b) but the career takes no more rounds, terminal, not resumable ----
            var draft = NormalRoundDraft(reopened);
            var ex = Assert.Throws<InvalidOperationException>(() => reopened.Apply(draft));
            Assert.Contains("died", ex.Message);
            Assert.Throws<InvalidOperationException>(() => reopened.Preview(draft));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // ---- (c) a death never poisons the well: a brand-new career creates fit and playable ----
        using var fresh = Create("secondlife", durability: 50.0); // hugely durable, always survives
        Assert.False(fresh.PlayerMortality().Deceased);
        Assert.Equal(0, fresh.PlayerMortality().RaceSuspensionRemaining);
        Assert.NotNull(fresh.CurrentBriefing());

        fresh.Apply(NormalRoundDraft(fresh));                      // plays normally
        Assert.Equal(2, fresh.Summary.CurrentRound);
        Assert.False(fresh.PlayerMortality().Deceased);
    }
}
