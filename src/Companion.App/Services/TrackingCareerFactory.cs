using System.Runtime.CompilerServices;
using System.IO;
using Companion.ViewModels.Services;

namespace Companion.App.Services;

/// <summary>
/// App-layer decorator that remembers the authoritative file path for every session created by the
/// composition root. <see cref="ICareerSession"/> intentionally does not expose its path, while a
/// whole-file restore must hand that path back to the shell so it can reopen the spent session.
/// Keeping the association here preserves the view-model seam and avoids casting production sessions
/// to <c>CareerSessionService</c> in a view.
/// </summary>
public sealed class TrackingCareerFactory : ICareerFactory
{
    private readonly ICareerFactory _inner;
    private readonly ConditionalWeakTable<ICareerSession, PathRegistration> _paths = new();

    public TrackingCareerFactory(ICareerFactory inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public ICareerSession Create(CareerCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Track(_inner.Create(request), request.CareerFilePath);
    }

    public ICareerSession Open(string careerFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(careerFilePath);
        return Track(_inner.Open(careerFilePath), careerFilePath);
    }

    /// <summary>Gets the file that owns <paramref name="session"/>, if this decorator opened it.</summary>
    public bool TryGetCareerPath(ICareerSession session, out string? careerFilePath)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_paths.TryGetValue(session, out var registration))
        {
            careerFilePath = registration.Path;
            return true;
        }

        careerFilePath = null;
        return false;
    }

    /// <summary>Gets the tracked path or fails with an actionable integration error.</summary>
    public string GetCareerPath(ICareerSession session) =>
        TryGetCareerPath(session, out string? path)
            ? path!
            : throw new InvalidOperationException(
                "This career session was not opened through the app's tracking factory.");

    private ICareerSession Track(ICareerSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        string fullPath = Path.GetFullPath(path);
        _paths.Remove(session);
        _paths.Add(session, new PathRegistration(fullPath));
        return session;
    }

    private sealed record PathRegistration(string Path);
}
