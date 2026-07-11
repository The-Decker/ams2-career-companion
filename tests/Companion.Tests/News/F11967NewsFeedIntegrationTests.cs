using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.News;

/// <summary>
/// End-to-end guard for the real 1967 pack: the empty pre-race feed becomes a composed 1960s
/// dispatch only after a result is imported and folded.
/// </summary>
public sealed class F11967NewsFeedIntegrationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("companion-1967-news-").FullName;

    [Fact]
    public void Real1967Round_FoldsIntoThe1960sNewsFeed()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        Assert.Equal("1960s", environment.Rules.NewsArticles.ResolveEra(1967));

        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = ViewModelTestData.RealPackDirectory,
                CareerFilePath = Path.Combine(_root, "career.ams2career"),
                CareerName = "1967 news integration",
                MasterSeed = 42,
                PlayerLiveryName = "Brabham-Repco #2 D. Hulme",
            },
            environment);

        Assert.Equal(1967, session.Summary.SeasonYear);
        Assert.Empty(session.ReadFeed());

        string player = session.Summary.PlayerDriverId;
        var grid = session.CurrentGrid().Select(seat => seat.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = [player, .. grid.Where(driverId => driverId != player)],
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        var dispatch = Assert.Single(
            session.ReadFeed(), item => item.Round == 1 && item.Kind == "race");
        Assert.False(string.IsNullOrWhiteSpace(dispatch.Headline));
        Assert.False(string.IsNullOrWhiteSpace(dispatch.Body));
        Assert.Contains("South African Grand Prix", dispatch.Body);
        Assert.DoesNotContain("{", dispatch.Body);
        Assert.DoesNotContain("}", dispatch.Body);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can briefly retain a Windows handle.
        }
    }
}
