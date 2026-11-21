using System.Globalization;
using System.Text;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix.Ids;

/// <summary>
/// A parsed EndToEndId or RtrId. Both share one 32-character layout:
/// a prefix (<c>E</c> or <c>D</c>), the 8-digit ISPB of the participant that
/// created the identifier, the creation time as <c>yyyyMMddHHmm</c> in UTC and
/// 11 alphanumeric characters. Identifiers are case-sensitive.
/// </summary>
/// <param name="Prefix"><see cref="PixIdentifiers.PrefixEndToEnd"/> or <see cref="PixIdentifiers.PrefixReturn"/>.</param>
/// <param name="Ispb">The ISPB of the participant that created the identifier.</param>
/// <param name="Time">The creation time, UTC, minute precision.</param>
/// <param name="Sequence">The 11 alphanumeric characters.</param>
public readonly record struct PixId(char Prefix, string Ispb, DateTime Time, string Sequence)
{
    /// <summary>Returns the 32-character identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() =>
        Prefix + Ispb + Time.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + Sequence;
}

/// <summary>
/// Parses, validates and generates the identifiers of Pix ISO 20022
/// messages: EndToEndId, the return identification RtrId, message
/// identifications (MsgId) and ISPB codes.
/// </summary>
public static class PixIdentifiers
{
    /// <summary>The length of an EndToEndId or RtrId.</summary>
    public const int Length = 32;

    /// <summary>The prefix of an EndToEndId.</summary>
    public const char PrefixEndToEnd = 'E';

    /// <summary>The prefix of a return identification (RtrId).</summary>
    public const char PrefixReturn = 'D';

    /// <summary>The maximum length of a Max35Text identification such as MsgId, BizMsgIdr or TxId.</summary>
    public const int MaxMsgIdLength = 35;

    private const int SequenceLength = 11;

    /// <summary>
    /// Parses and validates an EndToEndId. The timestamp must be a real
    /// calendar date and time.
    /// </summary>
    /// <param name="s">The identifier.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="InvalidIdentifierException">The identifier is invalid.</exception>
    public static PixId ParseEndToEndId(string s) => Parse(s, PrefixEndToEnd, IdentifierKind.EndToEndId);

    /// <summary>Parses and validates a return identification (RtrId) as used in pacs.004.</summary>
    /// <param name="s">The identifier.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="InvalidIdentifierException">The identifier is invalid.</exception>
    public static PixId ParseReturnId(string s) => Parse(s, PrefixReturn, IdentifierKind.ReturnId);

    /// <summary>Parses an EndToEndId without throwing.</summary>
    /// <param name="s">The identifier.</param>
    /// <param name="id">The parsed identifier, when valid.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParseEndToEndId(string? s, out PixId id) => TryParse(s, PrefixEndToEnd, out id);

    /// <summary>Parses a return identification without throwing.</summary>
    /// <param name="s">The identifier.</param>
    /// <param name="id">The parsed identifier, when valid.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParseReturnId(string? s, out PixId id) => TryParse(s, PrefixReturn, out id);

    /// <summary>
    /// Creates an EndToEndId for the participant with the given ISPB at the
    /// given time (converted to UTC, truncated to the minute). The sequence is
    /// drawn uniformly from [0-9A-Za-z], from <paramref name="random"/> or from
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/> when it is null.
    /// </summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <param name="at">The creation time.</param>
    /// <param name="random">An optional source of random bytes, read 32 bytes at a time.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="InvalidIdentifierException">The ISPB is not 8 digits.</exception>
    /// <exception cref="InvalidOperationException">The random source fails or yields no usable bytes.</exception>
    public static string NewEndToEndId(string ispb, DateTimeOffset at, Stream? random = null) =>
        NewId(PrefixEndToEnd, ispb, at, random, IdentifierKind.EndToEndId);

    /// <summary>Creates a return identification (RtrId); see <see cref="NewEndToEndId"/>.</summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <param name="at">The creation time.</param>
    /// <param name="random">An optional source of random bytes.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="InvalidIdentifierException">The ISPB is not 8 digits.</exception>
    /// <exception cref="InvalidOperationException">The random source fails or yields no usable bytes.</exception>
    public static string NewReturnId(string ispb, DateTimeOffset at, Stream? random = null) =>
        NewId(PrefixReturn, ispb, at, random, IdentifierKind.ReturnId);

    /// <summary>
    /// Returns a 32-character message identification: <c>M</c>, the sender's
    /// 8-digit ISPB and 23 random alphanumeric characters. This is the layout
    /// commonly used for SPI messages; check the current BCB catalog before
    /// relying on it.
    /// </summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <param name="random">An optional source of random bytes.</param>
    /// <returns>The message identification.</returns>
    /// <exception cref="InvalidIdentifierException">The ISPB is not 8 digits.</exception>
    /// <exception cref="InvalidOperationException">The random source fails or yields no usable bytes.</exception>
    public static string NewMsgId(string ispb, Stream? random = null)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        if (!IsValidIspb(ispb))
        {
            throw new InvalidIdentifierException(IdentifierKind.MsgId, "ISPB " + GoFormat.Quote(ispb) + " must be 8 digits");
        }

        Span<char> output = stackalloc char[32];
        output[0] = 'M';
        ispb.AsSpan().CopyTo(output.Slice(1));
        RandomSource.FillAlphanumeric(output.Slice(9), random);
        return output.ToString();
    }

    /// <summary>
    /// Checks <paramref name="s"/> against the Max35Text type used by MsgId:
    /// 1 to 35 characters, not blank. No SPI-specific layout is enforced.
    /// </summary>
    /// <param name="s">The message identification.</param>
    /// <exception cref="InvalidIdentifierException">The identification is invalid.</exception>
    public static void ValidateMsgId(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string? error = CheckMsgId(s);
        if (error is not null)
        {
            throw new InvalidIdentifierException(IdentifierKind.MsgId, error);
        }
    }

    /// <summary>Reports whether <paramref name="s"/> is a valid message identification; see <see cref="ValidateMsgId"/>.</summary>
    /// <param name="s">The message identification.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValidMsgId(string? s) => s is not null && CheckMsgId(s) is null;

    /// <summary>
    /// Reports whether <paramref name="s"/> has the shape of an ISPB: exactly
    /// 8 ASCII digits. Whether it belongs to an SPI participant can only be
    /// checked against the BCB participant list.
    /// </summary>
    /// <param name="s">The candidate ISPB.</param>
    /// <returns><see langword="true"/> when it is 8 digits.</returns>
    public static bool IsValidIspb(string? s) => s is not null && s.Length == 8 && Text.IsDigits(s);

    /// <summary>
    /// Returns the reason <paramref name="s"/> is not a valid identifier with
    /// <paramref name="prefix"/> (without the <c>ids: invalid ...:</c> prefix),
    /// or null. Lengths and positions are counted in UTF-8 bytes, as in Go.
    /// </summary>
    internal static string? CheckId(string s, char prefix, out PixId id)
    {
        id = default;
        byte[] b = Encoding.UTF8.GetBytes(s);
        if (b.Length != Length)
        {
            return "must be " + GoFormat.Int(Length) + " characters, got " + GoFormat.Int(b.Length);
        }

        if (b[0] != prefix)
        {
            return "must start with " + GoFormat.QuoteRune(prefix);
        }

        ReadOnlySpan<byte> ispb = b.AsSpan(1, 8);
        ReadOnlySpan<byte> ts = b.AsSpan(9, 12);
        ReadOnlySpan<byte> seq = b.AsSpan(21, SequenceLength);
        if (!Text.IsDigits(ispb))
        {
            return "ISPB " + GoFormat.QuoteUtf8(ispb) + " must be 8 digits";
        }

        if (!Text.IsDigits(ts))
        {
            return "timestamp " + GoFormat.QuoteUtf8(ts) + " must be 12 digits";
        }

        // Year 0000 is valid in Go's calendar but cannot be represented by
        // System.DateTime; such identifiers are rejected (a documented difference).
        if (!IsoTime.IsPixTimestamp(ts, out int year, out int month, out int day, out int hour, out int minute) || year == 0)
        {
            return "timestamp " + GoFormat.QuoteUtf8(ts) + " is not a valid date and time";
        }

        foreach (byte c in seq)
        {
            if (!Text.IsAlnum(c))
            {
                return "sequence " + GoFormat.QuoteUtf8(seq) + " must be alphanumeric";
            }
        }

        id = new PixId(
            prefix,
            Encoding.ASCII.GetString(ispb),
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc),
            Encoding.ASCII.GetString(seq));
        return null;
    }

    internal static string? CheckMsgId(string s)
    {
        if (!Text.IsValidUtf16(s))
        {
            return "not valid UTF-8";
        }

        int n = Text.RuneCount(s);
        if (n == 0 || IsBlank(s))
        {
            return "empty";
        }

        if (n > MaxMsgIdLength)
        {
            return GoFormat.Int(n) + " characters, maximum is " + GoFormat.Int(MaxMsgIdLength);
        }

        return null;
    }

    private static PixId Parse(string s, char prefix, IdentifierKind kind)
    {
        ArgumentNullException.ThrowIfNull(s);
        string? error = CheckId(s, prefix, out PixId id);
        return error is null ? id : throw new InvalidIdentifierException(kind, error);
    }

    private static bool TryParse(string? s, char prefix, out PixId id)
    {
        if (s is not null && CheckId(s, prefix, out id) is null)
        {
            return true;
        }

        id = default;
        return false;
    }

    private static string NewId(char prefix, string ispb, DateTimeOffset at, Stream? random, IdentifierKind kind)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        if (!IsValidIspb(ispb))
        {
            throw new InvalidIdentifierException(kind, "ISPB " + GoFormat.Quote(ispb) + " must be 8 digits");
        }

        Span<char> output = stackalloc char[Length];
        output[0] = prefix;
        ispb.AsSpan().CopyTo(output.Slice(1));
        at.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture).AsSpan().CopyTo(output.Slice(9));
        RandomSource.FillAlphanumeric(output.Slice(21), random);
        return output.ToString();
    }

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
            {
                return false;
            }
        }

        return true;
    }
}
