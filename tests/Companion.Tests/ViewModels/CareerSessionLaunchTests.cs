using Companion.Ams2;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

public sealed class CareerSessionLaunchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-launch-session-").FullName;

    [Fact]
    public void RealSession_DelegatesLaunchToTheEnvironmentSeam_WithoutCallingTheOs()
    {
        var launcher = new RecordingLauncher(Ams2LaunchResult.Succeeded("Captured by test."));
        var environment = new CareerEnvironment
        {
            ContentLibrary = ViewModelTestData.RealLibrary.Value,
            Ams2Launcher = launcher,
            LocateInstall = static () => null,
            DocumentsDirectory = Path.Combine(_root, "docs"),
            RulesDirectory = ViewModelTestData.RulesDirectory,
        };
        var request = new CareerCreationRequest
        {
            PackDirectory = ViewModelTestData.RealPackDirectory,
            CareerFilePath = Path.Combine(_root, "career.ams2career"),
            CareerName = "Launcher seam",
            MasterSeed = 42,
            PlayerLiveryName = "Brabham-Repco #2 D. Hulme",
        };

        using var session = CareerSessionService.CreateCareer(request, environment);
        var launchSession = Assert.IsAssignableFrom<IAms2GameLaunch>(session);

        Ams2LaunchResult result = launchSession.LaunchAms2();

        Assert.True(result.Success);
        Assert.Equal("Captured by test.", result.Message);
        Assert.Equal(1, launcher.Calls);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite sidecars may briefly outlive the connection on Windows.
        }
    }

    private sealed class RecordingLauncher(Ams2LaunchResult result) : IAms2Launcher
    {
        public int Calls { get; private set; }

        public Ams2LaunchResult Launch()
        {
            Calls++;
            return result;
        }
    }
}
