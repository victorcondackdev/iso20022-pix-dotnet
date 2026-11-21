using System.Globalization;
using System.Text;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>
/// An amount of Brazilian reais held exactly as an integer number of
/// centavos. Floating point is never involved: <c>0.1 + 0.2</c> is exactly
/// 30 centavos.
/// </summary>
/// <remarks>
/// ISO 20022 amounts are <c>xs:decimal</c> values restricted to at most 18
/// total digits and, for BRL, two fraction digits. <see cref="Parse"/>
/// accepts amounts up to <see cref="MaxValue"/> (9999999999999999.99), the
/// largest value whose canonical form has 18 digits. Negative amounts do not
/// exist in ISO 20022 amount types; they can arise from arithmetic and are
/// rejected by <see cref="ToIsoString"/>.
/// </remarks>
/// <param name="Cents">The amount in centavos.</param>
public readonly record struct BrlAmount(long Cents) : IComparable<BrlAmount>
{
    /// <summary>The ISO 20022 <c>totalDigits</c> facet of amount types.</summary>
    public const int MaxTotalDigits = 18;

    /// <summary>The number of minor units of BRL.</summary>
    public const int MaxFractionDigits = 2;

    /// <summary>The largest amount accepted by <see cref="Parse"/>: 9999999999999999.99.</summary>
    public static readonly BrlAmount MaxValue = new(999_999_999_999_999_999);

    /// <summary>R$ 0,00.</summary>
    public static readonly BrlAmount Zero;

    /// <summary>Reports whether the amount is zero.</summary>
    public bool IsZero => Cents == 0;

    /// <summary>Reports whether the amount is greater than zero.</summary>
    public bool IsPositive => Cents > 0;

    /// <summary>Returns the amount of the given number of centavos.</summary>
    /// <param name="cents">The number of centavos.</param>
    /// <returns>The amount.</returns>
    public static BrlAmount FromCents(long cents) => new(cents);

    /// <summary>
    /// Parses an ISO 20022 decimal amount such as <c>10</c>, <c>10.5</c>,
    /// <c>10.50</c> or <c>0.01</c>: one or more ASCII digits, optionally a
    /// point and one or two digits, at most 18 digits in total (leading zeros
    /// count). No sign, exponent, white space, thousands separator or comma.
    /// </summary>
    /// <param name="s">The lexical amount.</param>
    /// <returns>The amount.</returns>
    /// <exception cref="InvalidAmountException">The text is not a valid amount.</exception>
    public static BrlAmount Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string? error = ParseCore(s, out long cents);
        return error is null ? new BrlAmount(cents) : throw new InvalidAmountException(error);
    }

    /// <summary>Parses an amount without throwing; see <see cref="Parse"/>.</summary>
    /// <param name="s">The lexical amount.</param>
    /// <param name="amount">The amount, when valid.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is a valid amount.</returns>
    public static bool TryParse(string? s, out BrlAmount amount)
    {
        if (s is not null && ParseCore(s, out long cents) is null)
        {
            amount = new BrlAmount(cents);
            return true;
        }

        amount = default;
        return false;
    }

    /// <summary>Returns the reason <paramref name="s"/> is invalid (without the <c>money: invalid amount:</c> prefix), or null.</summary>
    internal static string? ParseCore(string s, out long result)
    {
        result = 0;
        if (s.Length == 0)
        {
            return "empty";
        }

        // The grammar is checked byte by byte over the UTF-8 form, as in Go,
        // so that error messages name the same offending byte.
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        long cents = 0;
        int digits = 0;
        int fracDigit = -1; // -1 while in the integer part
        for (int i = 0; i < bytes.Length; i++)
        {
            byte c = bytes[i];
            if (c >= '0' && c <= '9')
            {
                if (fracDigit >= 0)
                {
                    if (fracDigit == MaxFractionDigits)
                    {
                        return GoFormat.Quote(s) + " has more than " + GoFormat.Int(MaxFractionDigits) + " fraction digits";
                    }

                    fracDigit++;
                }

                digits++;
                if (digits > MaxTotalDigits)
                {
                    return GoFormat.Quote(s) + " has more than " + GoFormat.Int(MaxTotalDigits) + " digits";
                }

                cents = (cents * 10) + (c - '0');
            }
            else if (c == '.')
            {
                if (fracDigit >= 0)
                {
                    return GoFormat.Quote(s) + " has more than one decimal point";
                }

                if (i == 0)
                {
                    return GoFormat.Quote(s) + " has no integer digits";
                }

                fracDigit = 0;
            }
            else
            {
                return GoFormat.Quote(s) + " contains " + GoFormat.QuoteRune(c);
            }
        }

        if (fracDigit == 0)
        {
            return GoFormat.Quote(s) + " has no digits after the decimal point";
        }

        // Scale to centavos, checking the bound before multiplying.
        long scale = 1;
        for (int f = Math.Max(fracDigit, 0); f < MaxFractionDigits; f++)
        {
            scale *= 10;
        }

        if (cents > MaxValue.Cents / scale)
        {
            return GoFormat.Quote(s) + " exceeds " + MaxValue.ToString();
        }

        result = cents * scale;
        return null;
    }

    /// <summary>
    /// Formats the amount in the canonical ISO 20022 form, always with two
    /// fraction digits (<c>10.50</c>, <c>0.01</c>). Negative amounts are
    /// prefixed with <c>-</c>.
    /// </summary>
    /// <returns>The formatted amount.</returns>
    public override string ToString()
    {
        ulong u = Cents < 0 ? (ulong)(-(Cents + 1)) + 1 : (ulong)Cents;
        string text = (u / 100).ToString(CultureInfo.InvariantCulture) + "." + (u % 100).ToString("D2", CultureInfo.InvariantCulture);
        return Cents < 0 ? "-" + text : text;
    }

    /// <summary>
    /// Formats the amount for an ISO 20022 amount element. Unlike
    /// <see cref="ToString"/>, fails for amounts that the element cannot carry.
    /// </summary>
    /// <returns>The canonical form.</returns>
    /// <exception cref="InvalidAmountException">The amount is negative or above <see cref="MaxValue"/>.</exception>
    public string ToIsoString()
    {
        if (Cents < 0)
        {
            throw new InvalidAmountException("negative amount " + ToString());
        }

        if (Cents > MaxValue.Cents)
        {
            throw new InvalidAmountException(ToString() + " exceeds " + MaxValue.ToString());
        }

        return ToString();
    }

    /// <summary>Returns this + <paramref name="other"/>.</summary>
    /// <param name="other">The amount to add.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum does not fit in 64 bits.</exception>
    public BrlAmount Add(BrlAmount other) => new(checked(Cents + other.Cents));

    /// <summary>Returns this - <paramref name="other"/>.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference does not fit in 64 bits.</exception>
    public BrlAmount Subtract(BrlAmount other) => new(checked(Cents - other.Cents));

    /// <summary>Adds without throwing.</summary>
    /// <param name="other">The amount to add.</param>
    /// <param name="sum">The sum, when it fits.</param>
    /// <returns><see langword="false"/> on overflow.</returns>
    public bool TryAdd(BrlAmount other, out BrlAmount sum)
    {
        long s = unchecked(Cents + other.Cents);
        if ((other.Cents > 0 && s < Cents) || (other.Cents < 0 && s > Cents))
        {
            sum = default;
            return false;
        }

        sum = new BrlAmount(s);
        return true;
    }

    /// <summary>Subtracts without throwing.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <param name="difference">The difference, when it fits.</param>
    /// <returns><see langword="false"/> on overflow.</returns>
    public bool TrySubtract(BrlAmount other, out BrlAmount difference)
    {
        long d = unchecked(Cents - other.Cents);
        if ((other.Cents > 0 && d > Cents) || (other.Cents < 0 && d < Cents))
        {
            difference = default;
            return false;
        }

        difference = new BrlAmount(d);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(BrlAmount other) => Cents.CompareTo(other.Cents);

    /// <summary>Checked addition.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum.</returns>
    public static BrlAmount operator +(BrlAmount left, BrlAmount right) => left.Add(right);

    /// <summary>Checked subtraction.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The difference.</returns>
    public static BrlAmount operator -(BrlAmount left, BrlAmount right) => left.Subtract(right);

    /// <summary>Checked negation.</summary>
    /// <param name="value">The amount.</param>
    /// <returns>The negated amount.</returns>
    public static BrlAmount operator -(BrlAmount value) => new(checked(-value.Cents));

    /// <summary>Compares two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(BrlAmount left, BrlAmount right) => left.Cents < right.Cents;

    /// <summary>Compares two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(BrlAmount left, BrlAmount right) => left.Cents > right.Cents;

    /// <summary>Compares two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(BrlAmount left, BrlAmount right) => left.Cents <= right.Cents;

    /// <summary>Compares two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(BrlAmount left, BrlAmount right) => left.Cents >= right.Cents;
}
