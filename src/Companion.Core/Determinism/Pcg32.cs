namespace Companion.Core.Determinism;

/// <summary>
/// PCG-XSH-RR 32-bit generator (64-bit state, 32-bit output), a direct port of M.E.
/// O'Neill's reference implementation (pcg32_srandom_r / pcg32_random_r /
/// pcg32_boundedrand_r from the PCG "basic C" library, pcg-random.org).
///
/// API guarantee: the output sequence for a given (initState, initSeq) pair is byte-stable
/// across processes, machines, architectures, and app versions, career save files depend on
/// it. Any change to this class is a breaking save-format change.
/// </summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _inc;

    /// <summary>Seeds exactly like the reference <c>pcg32_srandom_r(initstate, initseq)</c>:
    /// state 0, inc = (initSeq &lt;&lt; 1) | 1, step, add initstate, step.</summary>
    public Pcg32(ulong initState, ulong initSeq)
    {
        _state = 0UL;
        _inc = (initSeq << 1) | 1UL;
        NextUInt32();
        _state = unchecked(_state + initState);
        NextUInt32();
    }

    /// <summary>The reference <c>pcg32_random_r</c>: LCG step, XSH-RR output permutation.</summary>
    public uint NextUInt32()
    {
        ulong oldState = _state;
        _state = unchecked(oldState * Multiplier + _inc);
        uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rot = (int)(oldState >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Uniform double in [0, 1): one 32-bit draw scaled by 2^-32. Deterministic —
    /// no platform-dependent floating point is involved in producing the draw.</summary>
    public double NextDouble() => NextUInt32() * (1.0 / 4294967296.0);

    /// <summary>Uniform integer in [minInclusive, maxExclusive) using the reference
    /// <c>pcg32_boundedrand_r</c> threshold-rejection scheme (exactly unbiased).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentException(
                $"Empty range: [{minInclusive}, {maxExclusive}).", nameof(maxExclusive));

        uint bound = (uint)((long)maxExclusive - minInclusive);
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint r = NextUInt32();
            if (r >= threshold)
                return (int)(minInclusive + (long)(r % bound));
        }
    }
}
