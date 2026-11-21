using System.Buffers;
using System.Globalization;
using System.Text;

namespace Iso20022.Pix.Internal;

/// <summary>
/// Formatting helpers that reproduce the Go verbs used in error and violation
/// messages (<c>%q</c>, <c>%U</c>, <c>%d</c>, <c>%v</c> of a string slice), so
/// that messages are identical to the Go implementation.
/// </summary>
internal static class GoFormat
{
    /// <summary>Go <c>%q</c> of a string: the UTF-8 form of <paramref name="s"/>, quoted.</summary>
    public static string Quote(string? s) => QuoteUtf8(Encoding.UTF8.GetBytes(s ?? string.Empty));

    /// <summary>
    /// Go <c>%q</c> of raw bytes: valid UTF-8 sequences are shown as characters
    /// (escaped when not printable) and invalid bytes as <c>\xNN</c>.
    /// </summary>
    public static string QuoteUtf8(ReadOnlySpan<byte> bytes)
    {
        var b = new StringBuilder(bytes.Length + 2);
        b.Append('"');
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                // Go decodes one invalid byte at a time.
                b.Append("\\x").Append(bytes[0].ToString("x2", CultureInfo.InvariantCulture));
                bytes = bytes.Slice(1);
                continue;
            }

            AppendRune(b, rune.Value, '"');
            bytes = bytes.Slice(consumed);
        }

        return b.Append('"').ToString();
    }

    /// <summary>Go <c>%q</c> of an integer code point (a byte or a rune), e.g. <c>'E'</c>.</summary>
    public static string QuoteRune(int codePoint)
    {
        var b = new StringBuilder(8);
        b.Append('\'');
        if (!Rune.IsValid(codePoint))
        {
            codePoint = 0xFFFD;
        }

        AppendRune(b, codePoint, '\'');
        return b.Append('\'').ToString();
    }

    /// <summary>Go <c>%U</c>: <c>U+</c> followed by at least four uppercase hexadecimal digits.</summary>
    public static string CodePoint(int codePoint) => "U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>Go <c>%d</c>.</summary>
    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Go <c>%v</c> of a string slice: <c>[A B C]</c>.</summary>
    public static string List(IEnumerable<string> values) => "[" + string.Join(" ", values) + "]";

    private static void AppendRune(StringBuilder b, int r, char quote)
    {
        if (r == quote || r == '\\')
        {
            b.Append('\\').Append((char)r);
            return;
        }

        if (IsPrint(r))
        {
            AppendUtf16(b, r);
            return;
        }

        switch (r)
        {
            case '\a': b.Append("\\a"); return;
            case '\b': b.Append("\\b"); return;
            case '\f': b.Append("\\f"); return;
            case '\n': b.Append("\\n"); return;
            case '\r': b.Append("\\r"); return;
            case '\t': b.Append("\\t"); return;
            case '\v': b.Append("\\v"); return;
        }

        if (r < ' ' || r == 0x7F)
        {
            b.Append("\\x").Append(r.ToString("x2", CultureInfo.InvariantCulture));
        }
        else if (r < 0x10000)
        {
            b.Append("\\u").Append(r.ToString("x4", CultureInfo.InvariantCulture));
        }
        else
        {
            b.Append("\\U").Append(r.ToString("x8", CultureInfo.InvariantCulture));
        }
    }

    private static void AppendUtf16(StringBuilder b, int r)
    {
        if (r < 0x10000)
        {
            b.Append((char)r);
        }
        else
        {
            b.Append(char.ConvertFromUtf32(r));
        }
    }

    // Go's strconv.IsPrint: letters, marks, numbers, punctuation, symbols and
    // the ASCII space. Other spaces, controls, format characters, surrogates,
    // private-use and unassigned code points are not printable.
    private static bool IsPrint(int r)
    {
        if (r < 0x7F)
        {
            return r >= 0x20;
        }

        if (!Rune.IsValid(r))
        {
            return false;
        }

        return Rune.GetUnicodeCategory(new Rune(r)) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter => true,
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => true,
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation => true,
            UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol => true,
            _ => false,
        };
    }
}
