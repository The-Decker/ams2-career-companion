using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Career;

/// <summary>
/// One career state change, journal-event-first: the sim emits these in a deterministic
/// order and the Data layer persists them verbatim into the append-only journal table,
/// adding seq + utc. Value equality on all four fields is the byte-identical replay check.
/// </summary>
public sealed record JournalEvent
{
    /// <summary>Journal phase, one of <see cref="JournalPhases"/>.</summary>
    public required string Phase { get; init; }

    /// <summary>The entity the change applies to: a lineage id ("driver.j_clark",
    /// "team.lotus"), "player", "race", or "season".</summary>
    public required string Entity { get; init; }

    /// <summary>The state delta as JSON (CoreJson conventions, single line).</summary>
    public required string DeltaJson { get; init; }

    /// <summary>Why it happened, the phase+cause pair keys the headline template bank.</summary>
    public required string Cause { get; init; }
}

/// <summary>Delta-JSON serialization for journal events: CoreJson conventions (camelCase,
/// enums as camelCase strings, Rational as string) but single-line, so a delta is one
/// readable DB cell. Serialization output is deterministic for the DTO shapes the sim emits.</summary>
internal static class CareerJson
{
    private static readonly JsonSerializerOptions Delta = new(CoreJson.Options)
    {
        WriteIndented = false,
    };

    public static string Serialize<T>(T dto) => JsonSerializer.Serialize(dto, Delta);
}
