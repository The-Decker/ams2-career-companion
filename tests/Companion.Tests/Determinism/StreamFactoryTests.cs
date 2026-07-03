using Companion.Core.Determinism;

namespace Companion.Tests.Determinism;

public class StreamFactoryTests
{
    private const ulong Seed = 0x5EED_CAFE_F00D_1234UL;

    private static uint[] Take(Pcg32 stream, int count)
    {
        var values = new uint[count];
        for (int i = 0; i < count; i++)
            values[i] = stream.NextUInt32();
        return values;
    }

    [Fact]
    public void ConsumingStreamANeverShiftsStreamB()
    {
        var factory = new StreamFactory(Seed);

        var pristineB = Take(factory.CreateStream("aging", 1967, 0, "driver.b"), 20);

        // Drain a lot of stream A, then re-create B: its sequence must be untouched.
        var a = factory.CreateStream("aging", 1967, 0, "driver.a");
        for (int i = 0; i < 1000; i++)
            a.NextUInt32();

        var bAfter = Take(factory.CreateStream("aging", 1967, 0, "driver.b"), 20);
        Assert.Equal(pristineB, bAfter);
    }

    [Fact]
    public void CrossInstanceReproducibility_SameMasterSeed()
    {
        var first = new StreamFactory(Seed);
        var second = new StreamFactory(Seed);

        Assert.Equal(
            Take(first.CreateStream("offers", 1988, 3, "team.mclaren"), 50),
            Take(second.CreateStream("offers", 1988, 3, "team.mclaren"), 50));
    }

    [Fact]
    public void DifferentMasterSeedsSelectDifferentStreams()
    {
        var a = Take(new StreamFactory(1).CreateStream("headlines", 1967, 1, "race"), 8);
        var b = Take(new StreamFactory(2).CreateStream("headlines", 1967, 1, "race"), 8);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("aging", "retirement", 1967, 1967, 0, 0, "driver.x", "driver.x")] // subsystem differs
    [InlineData("aging", "aging", 1967, 1968, 0, 0, "driver.x", "driver.x")]      // year differs
    [InlineData("aging", "aging", 1967, 1967, 1, 2, "driver.x", "driver.x")]      // round differs
    [InlineData("aging", "aging", 1967, 1967, 0, 0, "driver.x", "driver.y")]      // entity differs
    public void EveryKeyComponentSelectsADistinctStream(
        string subsystemA, string subsystemB, int yearA, int yearB,
        int roundA, int roundB, string entityA, string entityB)
    {
        var factory = new StreamFactory(Seed);
        Assert.NotEqual(
            Take(factory.CreateStream(subsystemA, yearA, roundA, entityA), 8),
            Take(factory.CreateStream(subsystemB, yearB, roundB, entityB), 8));
    }

    [Fact]
    public void SeasonStreamIsTheRoundZeroEmptyEntityConvention()
    {
        var factory = new StreamFactory(Seed);
        Assert.Equal(
            Take(factory.CreateStream("tier-drift", 1967, 0, ""), 10),
            Take(factory.CreateSeasonStream("tier-drift", 1967), 10));
    }

    [Fact]
    public void RecreatingAStreamReplaysItFromTheStart()
    {
        var factory = new StreamFactory(Seed);
        var once = Take(factory.CreateStream("form", 1970, 5, "driver.z"), 10);
        var again = Take(factory.CreateStream("form", 1970, 5, "driver.z"), 10);
        Assert.Equal(once, again);
    }

    /// <summary>Byte-stability regression pin: these constants are part of the save format
    /// (streams seed careers). If this test ever fails, the key derivation / hash / mixer /
    /// generator changed and existing careers break — that is a breaking save-format change,
    /// not a test to update casually.</summary>
    [Fact]
    public void StreamDerivationIsPinned()
    {
        var stream = new StreamFactory(42).CreateStream("aging", 1967, 0, "driver.j_clark");
        Assert.Equal(0x5d311e6fu, stream.NextUInt32());
        Assert.Equal(0x2043397au, stream.NextUInt32());
        Assert.Equal(0x818a20f1u, stream.NextUInt32());
    }
}
