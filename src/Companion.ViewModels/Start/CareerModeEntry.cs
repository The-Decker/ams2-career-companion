namespace Companion.ViewModels.Start;

/// <summary>
/// Display-ready main-menu contract for one Alpha 1.0 career experience. <see cref="Id"/> is the
/// stable serialized mode id; every other string is presentation copy and may evolve without
/// changing a save boundary.
/// </summary>
public sealed record CareerModeEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Tagline { get; init; }
    public required string Description { get; init; }
    public required string PersistenceSummary { get; init; }
    public required string AvailabilityLabel { get; init; }
    public required bool IsAvailable { get; init; }
}

/// <summary>Navigation request carrying the stable Alpha experience id selected on the menu.</summary>
public sealed class CareerModeRequestedEventArgs(string experienceMode) : EventArgs
{
    public string ExperienceMode { get; } = experienceMode;
}
