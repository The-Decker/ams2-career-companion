namespace Companion.Core.Determinism;

/// <summary>
/// SplitMix64 (Steele, Lea &amp; Flood; the java.util.SplittableRandom finalizer), used as the
/// canonical seed expander: state advances by the 64-bit golden ratio and each output runs
/// the variant-13 murmur-style finalizer. This is the standard recipe for turning one 64-bit
/// seed into the several independent words another generator needs.
///
/// API guarantee: byte-stable forever — stream seeding depends on it (see
/// <see cref="StreamFactory"/>).
/// </summary>
public struct SplitMix64
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong Next()
    {
        ulong z = unchecked(_state += GoldenGamma);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
