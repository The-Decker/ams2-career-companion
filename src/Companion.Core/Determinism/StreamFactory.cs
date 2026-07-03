using System.Globalization;

namespace Companion.Core.Determinism;

/// <summary>
/// Creates named, independent PCG32 streams per the career-sim determinism contract
/// (docs/dev/career-sim.md): stream seed =
/// <c>SplitMix64(Fnv1a64(subsystem + "|" + year + "|" + round + "|" + entityId) XOR masterSeed)</c>,
/// expanded through SplitMix64 into the generator's (initState, initSeq) pair.
///
/// Every call creates a FRESH generator positioned at the start of its stream: consuming
/// numbers from one stream never shifts any other stream's sequence, and re-creating a
/// stream replays it from the beginning — this is what makes "re-simulate from round 1"
/// byte-identical.
///
/// API guarantee: for a given (masterSeed, subsystem, year, round, entityId) the stream's
/// output sequence is byte-stable across processes, machines, and app versions. Changing the
/// key derivation, the hash, the mixer, or the generator is a breaking save-format change.
///
/// Conventions: season-level streams (not tied to a round or entity) use round 0 and
/// entityId "" — <see cref="CreateSeasonStream"/> encodes that convention.
/// </summary>
public sealed class StreamFactory(ulong masterSeed)
{
    public ulong MasterSeed { get; } = masterSeed;

    public Pcg32 CreateStream(string subsystem, int year, int round, string entityId)
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(entityId);

        string key = string.Create(CultureInfo.InvariantCulture,
            $"{subsystem}|{year}|{round}|{entityId}");
        var mix = new SplitMix64(StableHash.Fnv1a64(key) ^ MasterSeed);
        ulong initState = mix.Next();
        ulong initSeq = mix.Next();
        return new Pcg32(initState, initSeq);
    }

    /// <summary>Season-level stream: the round-0 / entityId-"" convention.</summary>
    public Pcg32 CreateSeasonStream(string subsystem, int year) =>
        CreateStream(subsystem, year, 0, "");
}
