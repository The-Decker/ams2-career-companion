using System.Diagnostics;

namespace Companion.Ams2;

/// <summary>The structured result of asking Steam to launch AMS2.</summary>
public sealed record Ams2LaunchResult
{
    public required bool Success { get; init; }

    /// <summary>User-facing status or actionable failure guidance.</summary>
    public required string Message { get; init; }

    public static Ams2LaunchResult Succeeded(string message = "Launch request sent to Steam.") => new()
    {
        Success = true,
        Message = message,
    };

    public static Ams2LaunchResult Failed(string message) => new()
    {
        Success = false,
        Message = message,
    };
}

/// <summary>OS-launch seam. Tests inject a process starter and never invoke the shell.</summary>
public interface IAms2Launcher
{
    Ams2LaunchResult Launch();
}

/// <summary>
/// Launches AMS2 directly through Steam. The URI is the stable Steam AppId contract; shell
/// execution lets Windows hand the URI to the registered Steam client.
/// </summary>
public sealed class SteamAms2Launcher : IAms2Launcher
{
    public const string LaunchUri = "steam://run/1066890";

    private readonly Action<ProcessStartInfo> _startProcess;

    public SteamAms2Launcher()
        : this(static startInfo => _ = Process.Start(startInfo))
    {
    }

    /// <summary>
    /// Injectable process-start seam for tests. Production uses the parameterless constructor.
    /// </summary>
    public SteamAms2Launcher(Action<ProcessStartInfo> startProcess)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        _startProcess = startProcess;
    }

    public Ams2LaunchResult Launch()
    {
        var startInfo = new ProcessStartInfo(LaunchUri)
        {
            UseShellExecute = true,
        };

        try
        {
            _startProcess(startInfo);
            return Ams2LaunchResult.Succeeded();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return Ams2LaunchResult.Failed(
                "Steam could not launch AMS2. Make sure Steam is installed and running, then try again. " +
                $"Details: {ex.Message}");
        }
    }
}

/// <summary>
/// Safe default for test/custom environments that did not configure machine launching. It never
/// touches the OS and fails with guidance if a caller nevertheless requests a launch.
/// </summary>
public sealed class UnavailableAms2Launcher : IAms2Launcher
{
    public static UnavailableAms2Launcher Instance { get; } = new();

    private UnavailableAms2Launcher()
    {
    }

    public Ams2LaunchResult Launch() => Ams2LaunchResult.Failed(
        "Direct AMS2 launch is not configured for this session. Stage the grid, then launch AMS2 through Steam.");
}
