using Companion.Core.Career;

namespace Companion.Tests.Career;

public class OpiMathTests
{
    [Fact]
    public void UpdateIsTheContractEwma()
    {
        // OPI ← 0.8·OPI + 0.2·(expected − actual)
        Assert.Equal(0.4, OpiMath.Update(0.0, 5.0, 3.0), 12);
        Assert.Equal(1.6, OpiMath.Update(2.0, 5.0, 5.0), 12);
        Assert.Equal(0.8 * -1.0 + 0.2 * (4.0 - 10.0), OpiMath.Update(-1.0, 4.0, 10.0), 12);
    }

    [Fact]
    public void ClassifiedFinishIsChargedAsIs()
    {
        Assert.Equal(7.0, OpiMath.EffectiveFinish(3.0, 7, null, 20));
    }

    [Fact]
    public void MechanicalDnfScoresAsExpectedFinish_NoBlame()
    {
        double effective = OpiMath.EffectiveFinish(5.0, null, DnfCause.Mechanical, 20);
        Assert.Equal(5.0, effective);

        // Zero delta: the EWMA just decays toward zero, no punishment.
        Assert.Equal(0.8 * 1.5, OpiMath.Update(1.5, 5.0, effective), 12);
    }

    [Fact]
    public void DriverErrorDnfScoresAsGridSize_FullBlame()
    {
        double effective = OpiMath.EffectiveFinish(5.0, null, DnfCause.DriverError, 20);
        Assert.Equal(20.0, effective);

        Assert.Equal(0.2 * (5.0 - 20.0), OpiMath.Update(0.0, 5.0, effective), 12);
    }

    [Fact]
    public void DnfWithoutCauseIsRejected()
    {
        Assert.Throws<ArgumentException>(() => OpiMath.EffectiveFinish(5.0, null, null, 20));
    }

    [Fact]
    public void PositionsAreOneBased()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OpiMath.EffectiveFinish(5.0, 0, null, 20));
    }
}
