using System.Globalization;

namespace Companion.Core.Numerics;

/// <summary>
/// Exact rational number over 64-bit integers, always stored in lowest terms with a
/// positive denominator. Championship points are tiny (fractions like 1/7 from shared
/// fastest laps), so <see cref="long"/> never overflows in practice; comparisons still
/// cross-multiply in <see cref="Int128"/> so they are safe unconditionally.
/// <c>default(Rational)</c> is a valid zero: the denominator is stored offset by one so the
/// zero-initialized struct keeps the invariant instead of violating the comparer contract.
/// </summary>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    private readonly long _denominatorMinusOne;

    public long Numerator { get; }
    public long Denominator => _denominatorMinusOne + 1;

    public static readonly Rational Zero = new(0);
    public static readonly Rational One = new(1);
    public static readonly Rational Half = new(1, 2);

    public Rational(long numerator) : this(numerator, 1) { }

    public Rational(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException("Rational denominator cannot be zero.");
        if (denominator < 0)
        {
            // -(long.MinValue) genuinely does not fit in a long; everything else negates fine.
            numerator = checked(-numerator);
            denominator = checked(-denominator);
        }
        long gcd = Gcd(numerator, denominator);
        Numerator = numerator / gcd;
        _denominatorMinusOne = denominator / gcd - 1;
    }

    public bool IsZero => Numerator == 0;
    public bool IsInteger => Denominator == 1;

    public static Rational operator +(Rational a, Rational b) =>
        new(checked(a.Numerator * b.Denominator + b.Numerator * a.Denominator), checked(a.Denominator * b.Denominator));

    public static Rational operator -(Rational a, Rational b) =>
        new(checked(a.Numerator * b.Denominator - b.Numerator * a.Denominator), checked(a.Denominator * b.Denominator));

    public static Rational operator -(Rational a) => new(checked(-a.Numerator), a.Denominator);

    public static Rational operator *(Rational a, Rational b) =>
        new(checked(a.Numerator * b.Numerator), checked(a.Denominator * b.Denominator));

    public static Rational operator /(Rational a, Rational b) =>
        b.IsZero
            ? throw new DivideByZeroException("Division of Rational by zero.")
            : new(checked(a.Numerator * b.Denominator), checked(a.Denominator * b.Numerator));

    public static implicit operator Rational(long value) => new(value);

    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    public bool Equals(Rational other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is Rational r && Equals(r);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public int CompareTo(Rational other) =>
        ((Int128)Numerator * other.Denominator).CompareTo((Int128)other.Numerator * Denominator);

    /// <summary>Formats as "n" for integers, "n/d" otherwise, the canonical round-trip form.</summary>
    public override string ToString() =>
        IsInteger
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    public static Rational Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int slash = text.IndexOf('/');
        if (slash < 0)
            return new Rational(long.Parse(text, CultureInfo.InvariantCulture));
        return new Rational(
            long.Parse(text.AsSpan(0, slash), CultureInfo.InvariantCulture),
            long.Parse(text.AsSpan(slash + 1), CultureInfo.InvariantCulture));
    }

    public double ToDouble() => (double)Numerator / Denominator;

    /// <summary>Binary GCD over magnitudes in ulong so long.MinValue needs no Math.Abs.
    /// Inputs: any numerator, positive denominator. Result is always positive.</summary>
    private static long Gcd(long numerator, long denominator)
    {
        ulong a = numerator < 0 ? unchecked(0UL - (ulong)numerator) : (ulong)numerator;
        ulong b = (ulong)denominator;
        while (b != 0)
            (a, b) = (b, a % b);
        return a == 0 ? 1 : (long)a;
    }
}
