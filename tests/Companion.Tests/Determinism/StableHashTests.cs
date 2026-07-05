using System.Text;
using Companion.Core.Determinism;

namespace Companion.Tests.Determinism;

public class StableHashTests
{
    /// <summary>Official FNV-1a 64-bit test vectors (Fowler/Noll/Vo reference test suite).</summary>
    [Theory]
    [InlineData("", 0xcbf29ce484222325UL)]
    [InlineData("a", 0xaf63dc4c8601ec8cUL)]
    [InlineData("b", 0xaf63df4c8601f1a5UL)]
    [InlineData("c", 0xaf63de4c8601eff2UL)]
    [InlineData("foobar", 0x85944171f73967e8UL)]
    public void MatchesFnv1a64ReferenceVectors(string text, ulong expected)
    {
        Assert.Equal(expected, StableHash.Fnv1a64(text));
    }

    [Fact]
    public void HashesUtf8BytesNotUtf16()
    {
        // "é" is 2 bytes in UTF-8 (0xC3 0xA9); a UTF-16 hash would differ.
        ulong viaString = StableHash.Fnv1a64("é");
        ulong viaBytes = StableHash.Fnv1a64(Encoding.UTF8.GetBytes("é"));
        Assert.Equal(viaBytes, viaString);
    }

    [Fact]
    public void StreamKeyStyleStringsDoNotCollide()
    {
        ulong a = StableHash.Fnv1a64("aging|1967|0|driver.j_clark");
        ulong b = StableHash.Fnv1a64("aging|1967|0|driver.d_hulme");
        ulong c = StableHash.Fnv1a64("retirement|1967|0|driver.j_clark");
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void LongInputsFallBackFromStackAllocCorrectly()
    {
        string longText = new('x', 2000);
        // FNV-1a is byte-sequential: computing via the bytes overload must agree.
        Assert.Equal(
            StableHash.Fnv1a64(Encoding.UTF8.GetBytes(longText)),
            StableHash.Fnv1a64(longText));
    }
}
