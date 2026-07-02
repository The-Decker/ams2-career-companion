using Companion.Core.Numerics;

namespace Companion.Tests.Scoring;

public class RationalTests
{
    // ---------- construction, normalization, sign ----------

    [Theory]
    [InlineData(2, 4, 1, 2)]     // reduced to lowest terms
    [InlineData(6, 4, 3, 2)]
    [InlineData(-6, 4, -3, 2)]   // sign lives on the numerator
    [InlineData(6, -4, -3, 2)]   // negative denominator normalized away
    [InlineData(-6, -4, 3, 2)]   // double negative is positive
    [InlineData(0, 7, 0, 1)]     // zero always stored as 0/1
    [InlineData(5, 1, 5, 1)]
    [InlineData(9, 3, 3, 1)]
    public void Constructor_NormalizesToLowestTermsWithPositiveDenominator(
        long numerator, long denominator, long expectedNumerator, long expectedDenominator)
    {
        var value = new Rational(numerator, denominator);

        Assert.Equal(expectedNumerator, value.Numerator);
        Assert.Equal(expectedDenominator, value.Denominator);
    }

    [Fact]
    public void Constructor_ZeroDenominator_ThrowsDivideByZero() =>
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 0));

    [Fact]
    public void SingleArgumentConstructor_AndImplicitConversion_AreIntegers()
    {
        var five = new Rational(5);
        Rational minusThree = -3;

        Assert.True(five.IsInteger);
        Assert.Equal(new Rational(5, 1), five);
        Assert.Equal(-3, minusThree.Numerator);
        Assert.Equal(1, minusThree.Denominator);
    }

    [Fact]
    public void Constants_HaveCanonicalValues()
    {
        Assert.Equal(new Rational(0), Rational.Zero);
        Assert.Equal(new Rational(1), Rational.One);
        Assert.Equal(new Rational(1, 2), Rational.Half);
        Assert.True(Rational.Zero.IsZero);
        Assert.False(Rational.Half.IsInteger);
        Assert.True(new Rational(4, 2).IsInteger);
        Assert.True(new Rational(0, 9).IsZero);
    }

    // ---------- default value ----------

    [Fact]
    public void Default_IsAValidCanonicalZero()
    {
        Rational value = default;

        Assert.Equal(Rational.Zero, value);
        Assert.True(value.IsZero);
        Assert.True(value.IsInteger);
        Assert.Equal(0, value.Numerator);
        Assert.Equal(1, value.Denominator); // the offset storage keeps the invariant
        Assert.Equal(Rational.Zero.GetHashCode(), value.GetHashCode());
        Assert.Equal("0", value.ToString());
        Assert.Equal(0.0, value.ToDouble());
    }

    [Fact]
    public void Default_ComparesConsistentlyWithConstructedValues()
    {
        Rational value = default;

        Assert.Equal(0, value.CompareTo(Rational.Zero));
        Assert.Equal(0, Rational.Zero.CompareTo(value));
        Assert.True(value == Rational.Zero);
        Assert.True(value < Rational.One);
        Assert.True(Rational.One > value);
        Assert.True(new Rational(-1) < value);
        Assert.Equal(Rational.One, value + Rational.One); // arithmetic on default is safe
    }

    // ---------- long.MinValue edges ----------

    [Fact]
    public void LongMinValueNumerator_ConstructsAndRoundTripsThroughToString()
    {
        var value = new Rational(long.MinValue);

        Assert.Equal(long.MinValue, value.Numerator);
        Assert.Equal(1, value.Denominator);
        Assert.True(value.IsInteger);
        Assert.Equal("-9223372036854775808", value.ToString());
        Assert.Equal(value, Rational.Parse(value.ToString()));
    }

    [Fact]
    public void LongMinValueOverMinusOne_ThrowsOverflow() =>
        // -(long.MinValue) does not fit in a long; normalizing the sign must fail loudly
        // rather than wrap.
        Assert.Throws<OverflowException>(() => new Rational(long.MinValue, -1));

    [Fact]
    public void Parse_LongMinValue_Works() =>
        Assert.Equal(new Rational(long.MinValue), Rational.Parse("-9223372036854775808"));

    // ---------- equality ----------

    [Fact]
    public void Equality_IsValueEqualityOnTheNormalizedForm()
    {
        Assert.Equal(new Rational(1, 2), new Rational(2, 4));
        Assert.True(new Rational(1, 2) == new Rational(3, 6));
        Assert.True(new Rational(1, 2) != new Rational(1, 3));
        Assert.Equal(new Rational(1, 2).GetHashCode(), new Rational(2, 4).GetHashCode());
        Assert.True(new Rational(1, 2).Equals((object)new Rational(2, 4)));
        Assert.False(new Rational(1, 2).Equals("1/2"));
    }

    // ---------- arithmetic ----------

    [Fact]
    public void Addition_And_Subtraction_AreExact()
    {
        Assert.Equal(new Rational(5, 6), new Rational(1, 2) + new Rational(1, 3));
        Assert.Equal(new Rational(-1, 4), new Rational(1, 2) - new Rational(3, 4));
        Assert.Equal(Rational.Zero, new Rational(1, 7) - new Rational(1, 7));
    }

    [Fact]
    public void Multiplication_Division_AndNegation_AreExact()
    {
        Assert.Equal(new Rational(1, 2), new Rational(2, 3) * new Rational(3, 4));
        Assert.Equal(new Rational(1, 4), new Rational(-1, 2) * new Rational(-1, 2));
        Assert.Equal(new Rational(2), new Rational(1, 2) / new Rational(1, 4));
        Assert.Equal(new Rational(-3, 2), -new Rational(3, 2));
        Assert.Equal(new Rational(3, 2), -new Rational(-3, 2));
    }

    [Fact]
    public void Division_ByZeroRational_ThrowsDivideByZero() =>
        Assert.Throws<DivideByZeroException>(() => Rational.One / Rational.Zero);

    [Fact]
    public void SevenSevenths_SumExactlyToOne()
    {
        // The 1954 British GP identity: seven 1/7 fastest-lap shares are exactly one point.
        var seventh = Rational.One / 7;
        Assert.Equal(new Rational(1, 7), seventh);

        var sum = Enumerable.Range(0, 7).Aggregate(Rational.Zero, (acc, _) => acc + seventh);
        Assert.Equal(Rational.One, sum);
        Assert.True(sum.IsInteger);
    }

    // ---------- comparison ----------

    [Fact]
    public void Comparison_OrdersMixedSignFractions()
    {
        var values = new[]
        {
            new Rational(1), new Rational(1, 2), new Rational(-3, 2),
            Rational.Zero, new Rational(1, 7), new Rational(8),
        };

        var sorted = values.OrderBy(v => v).ToArray();

        Assert.Equal(
            new[]
            {
                new Rational(-3, 2), Rational.Zero, new Rational(1, 7),
                new Rational(1, 2), new Rational(1), new Rational(8),
            },
            sorted);

        Assert.True(new Rational(1, 3) < new Rational(1, 2));
        Assert.True(new Rational(1, 2) > new Rational(1, 3));
        Assert.True(new Rational(-1, 2) < new Rational(1, 3));
        Assert.True(new Rational(1, 2) <= new Rational(2, 4));
        Assert.True(new Rational(1, 2) >= new Rational(2, 4));
        Assert.Equal(0, new Rational(1, 2).CompareTo(new Rational(2, 4)));
    }

    [Fact]
    public void Comparison_CrossMultiplication_DoesNotOverflowLong()
    {
        // M/(M-1) vs (M-1)/(M-2): cross products are ~8.5e37, far beyond long.MaxValue.
        // Both fractions are already coprime, so construction keeps the huge terms.
        var smaller = new Rational(long.MaxValue, long.MaxValue - 1);
        var larger = new Rational(long.MaxValue - 1, long.MaxValue - 2);

        Assert.True(smaller < larger);
        Assert.True(larger > smaller);
        Assert.True(smaller.CompareTo(larger) < 0);
        Assert.True(larger.CompareTo(smaller) > 0);
        Assert.NotEqual(smaller, larger);

        // Sign-crossing comparison with huge magnitudes stays safe too.
        Assert.True(new Rational(-long.MaxValue, 3) < new Rational(long.MaxValue, 3));
    }

    // ---------- Parse / ToString ----------

    [Theory]
    [InlineData("1/7", 1, 7)]
    [InlineData("-3/2", -3, 2)]
    [InlineData("4", 4, 1)]
    [InlineData("0", 0, 1)]
    public void Parse_CanonicalForms_RoundTripThroughToString(string text, long numerator, long denominator)
    {
        var parsed = Rational.Parse(text);

        Assert.Equal(new Rational(numerator, denominator), parsed);
        Assert.Equal(text, parsed.ToString());
    }

    [Theory]
    [InlineData("6/4", "3/2")]   // non-canonical input normalizes
    [InlineData("3/-2", "-3/2")] // negative denominator moves to the numerator
    [InlineData("-8/4", "-2")]   // integers drop the denominator
    public void Parse_NonCanonicalForms_NormalizeToCanonicalText(string text, string canonical) =>
        Assert.Equal(canonical, Rational.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ThrowsArgumentException(string text) =>
        Assert.ThrowsAny<ArgumentException>(() => Rational.Parse(text));

    [Fact]
    public void Parse_NonNumericText_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => Rational.Parse("abc"));

    [Fact]
    public void Parse_ZeroDenominator_ThrowsDivideByZero() =>
        Assert.Throws<DivideByZeroException>(() => Rational.Parse("1/0"));

    [Fact]
    public void ToDouble_ConvertsExactly()
    {
        Assert.Equal(0.25, new Rational(1, 4).ToDouble());
        Assert.Equal(-1.5, new Rational(-3, 2).ToDouble());
        Assert.Equal(1.0 / 7.0, new Rational(1, 7).ToDouble(), precision: 15);
    }
}
