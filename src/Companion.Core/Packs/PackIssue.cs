namespace Companion.Core.Packs;

/// <summary>Severity of a structural pack issue. Core-local mirror of the Ams2 preflight
/// severity (Core must not reference Companion.Ams2).</summary>
public enum PackIssueSeverity
{
    /// <summary>The pack cannot run a career as authored.</summary>
    Error,

    /// <summary>Something looks off but the pack is still usable (proceed-anyway territory).</summary>
    Warning,
}

public sealed record PackIssue
{
    public required PackIssueSeverity Severity { get; init; }

    public required string Message { get; init; }
}

public sealed record PackValidationReport
{
    public required IReadOnlyList<PackIssue> Issues { get; init; }

    public bool HasErrors => Issues.Any(i => i.Severity == PackIssueSeverity.Error);
}
