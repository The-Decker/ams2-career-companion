using Companion.Core.Determinism;

namespace Companion.Tests.Determinism;

public class Pcg32Tests
{
    /// <summary>The PCG reference check values: pcg32-demo seeded with
    /// pcg32_srandom_r(&amp;rng, 42u, 54u) prints exactly these six words first
    /// (O'Neill's pcg-c-basic sample output). Any deviation means the port is not the
    /// reference generator.</summary>
    [Fact]
    public void MatchesTheOneillReferenceOutput_Seed42Seq54()
    {
        var rng = new Pcg32(42, 54);

        uint[] expected =
        [
            0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e,
        ];
        foreach (uint value in expected)
            Assert.Equal(value, rng.NextUInt32());
    }

    [Fact]
    public void SameSeedsProduceIdenticalSequences()
    {
        var a = new Pcg32(0xDEADBEEFUL, 0x0123456789ABCDEFUL);
        var b = new Pcg32(0xDEADBEEFUL, 0x0123456789ABCDEFUL);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt32(), b.NextUInt32());
    }

    [Fact]
    public void DifferentStreamSelectorsDiverge()
    {
        var a = new Pcg32(42, 54);
        var b = new Pcg32(42, 55);

        bool anyDifferent = false;
        for (int i = 0; i < 16 && !anyDifferent; i++)
            anyDifferent = a.NextUInt32() != b.NextUInt32();
        Assert.True(anyDifferent, "Different initSeq values must select different streams.");
    }

    [Fact]
    public void NextDoubleStaysInUnitInterval()
    {
        var rng = new Pcg32(7, 11);
        for (int i = 0; i < 10_000; i++)
        {
            double value = rng.NextDouble();
            Assert.InRange(value, 0.0, 1.0 - double.Epsilon);
            Assert.True(value < 1.0);
        }
    }

    [Fact]
    public void NextDoubleIsTheDrawScaledByTwoToTheMinus32()
    {
        var draws = new Pcg32(42, 54);
        var doubles = new Pcg32(42, 54);
        for (int i = 0; i < 100; i++)
            Assert.Equal(draws.NextUInt32() * (1.0 / 4294967296.0), doubles.NextDouble());
    }

    [Fact]
    public void NextIntStaysInRangeAndHitsEveryValue()
    {
        var rng = new Pcg32(2024, 1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(-3, 4);
            Assert.InRange(value, -3, 3);
            seen.Add(value);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void NextIntWithSingleValueRangeAlwaysReturnsIt()
    {
        var rng = new Pcg32(1, 1);
        for (int i = 0; i < 10; i++)
            Assert.Equal(5, rng.NextInt(5, 6));
    }

    [Fact]
    public void NextIntRejectsEmptyRanges()
    {
        var rng = new Pcg32(1, 1);
        Assert.Throws<ArgumentException>(() => rng.NextInt(3, 3));
        Assert.Throws<ArgumentException>(() => rng.NextInt(4, 3));
    }
}
