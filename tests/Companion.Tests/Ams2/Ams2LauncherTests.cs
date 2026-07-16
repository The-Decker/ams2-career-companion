using System.Diagnostics;
using Companion.Ams2;

namespace Companion.Tests.Ams2;

public sealed class Ams2LauncherTests
{
    [Fact]
    public void Launch_UsesTheExactSteamUriThroughShellExecution()
    {
        ProcessStartInfo? captured = null;
        var launcher = new SteamAms2Launcher(startInfo => captured = startInfo);

        Ams2LaunchResult result = launcher.Launch();

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("steam://run/1066890", captured.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Empty(captured.ArgumentList);
    }

    [Fact]
    public void Launch_WhenTheShellRefuses_ReturnsActionableFailureWithoutThrowing()
    {
        int calls = 0;
        var launcher = new SteamAms2Launcher(_ =>
        {
            calls++;
            throw new InvalidOperationException("No URI handler is registered.");
        });

        Ams2LaunchResult result = launcher.Launch();

        Assert.Equal(1, calls);
        Assert.False(result.Success);
        Assert.Contains("Steam", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installed and running", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No URI handler", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableLauncher_IsASafeNoOsDefault()
    {
        Ams2LaunchResult result = UnavailableAms2Launcher.Instance.Launch();

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
