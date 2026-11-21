using System.Text;

namespace Iso20022.Pix.Internal;

/// <summary>
/// String helpers with Go semantics. A .NET string that holds a lone
/// surrogate has no UTF-8 encoding; it is treated as Go treats invalid UTF-8.
/// </summary>
internal static class Text
{
    /// <summary>Reports whether every surrogate in <paramref name="s"/> is part of a pair.</summary>
    public static bool IsValidUtf16(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Number of Unicode code points (Go <c>utf8.RuneCountInString</c>) of a valid string.</summary>
    public static int RuneCount(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }

            n++;
        }

        return n;
    }

    /// <summary>Number of bytes of the UTF-8 form of <paramref name="s"/> (Go <c>len(s)</c>).</summary>
    public static int Utf8Length(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>Returns the first code point of a valid string that XML 1.0 cannot carry, or -1.</summary>
    public static int FirstNonXmlChar(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            int r = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                r = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }

            if (!IsXmlChar(r))
            {
                return r;
            }
        }

        return -1;
    }

    /// <summary>The XML 1.0 <c>Char</c> production.</summary>
    public static bool IsXmlChar(int r) =>
        r == 0x09 || r == 0x0A || r == 0x0D
        || (r >= 0x20 && r <= 0xD7FF)
        || (r >= 0xE000 && r <= 0xFFFD)
        || (r >= 0x10000 && r <= 0x10FFFF);

    /// <summary>Reports whether <paramref name="s"/> is non-empty and only ASCII digits.</summary>
    public static bool IsDigits(ReadOnlySpan<byte> s)
    {
        if (s.IsEmpty)
        {
            return false;
        }

        foreach (byte c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports whether <paramref name="s"/> is non-empty and only ASCII digits.</summary>
    public static bool IsDigits(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ASCII letter or digit.</summary>
    public static bool IsAlnum(int c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    /// <summary>Go <c>strings.TrimSpace</c>: trims Unicode white space as defined by Go's <c>unicode.IsSpace</c>.</summary>
    public static string GoTrimSpace(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsGoSpace(s[start]))
        {
            start++;
        }

        while (end > start && IsGoSpace(s[end - 1]))
        {
            end--;
        }

        return s.Substring(start, end - start);
    }

    // Code points are written as numbers so that the source stays ASCII.
    private static bool IsGoSpace(char c) => (int)c switch
    {
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 => true,
        0x85 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        _ => c >= 0x2000 && c <= 0x200A,
    };
}
