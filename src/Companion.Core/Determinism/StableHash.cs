using System.Text;

namespace Companion.Core.Determinism;

/// <summary>
/// FNV-1a 64-bit over UTF-8 bytes. Used everywhere a string must hash identically across
/// runs, processes, and .NET versions, never <see cref="string.GetHashCode()"/>, which is
/// randomized per process and unspecified across versions.
///
/// API guarantee: byte-stable forever; stream seeds derived from it live in career saves.
/// </summary>
public static class StableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL; // 0xcbf29ce484222325
    private const ulong Prime = 1099511628211UL;              // 0x100000001b3

    public static ulong Fnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int byteCount = Encoding.UTF8.GetByteCount(text);
        Span<byte> buffer = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(text, buffer);
        return Fnv1a64(buffer);
    }

    public static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        ulong hash = OffsetBasis;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash = unchecked(hash * Prime);
        }
        return hash;
    }
}
