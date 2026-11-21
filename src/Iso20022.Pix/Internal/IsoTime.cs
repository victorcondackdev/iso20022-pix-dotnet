namespace Iso20022.Pix.Internal;

/// <summary>
/// Lexical checks for ISO 20022 dates and times that accept exactly what
/// the Go implementation accepts: <c>time.Parse</c> with RFC 3339 (nanosecond)
/// or the same layout without zone for ISODateTime, <c>2006-01-02</c> for
/// ISODate and <c>200601021504</c> for the Pix identifier timestamp.
/// </summary>
/// <remarks>
/// Go's general parser is slightly more permissive than RFC 3339: a one-digit
/// hour, a comma before the fraction, any number of fraction digits and zone
/// offsets up to 24:60 are accepted. Year 0000 is a valid (leap) year.
/// </remarks>
internal static class IsoTime
{
    /// <summary>Reports whether <paramref name="s"/> is a valid ISODateTime.</summary>
    public static bool IsDateTime(string s) => ParseDateTime(s, withZone: true) || ParseDateTime(s, withZone: false);

    /// <summary>Reports whether <paramref name="s"/> is a valid ISODate (YYYY-MM-DD).</summary>
    public static bool IsDate(string s)
    {
        if (!IsAscii(s))
        {
            return false;
        }

        int i = 0;
        if (!Year(s, ref i, out int year)
            || !Literal(s, ref i, '-')
            || !GetNum(s, ref i, true, out int month) || month < 1 || month > 12
            || !Literal(s, ref i, '-')
            || !GetNum(s, ref i, true, out int day))
        {
            return false;
        }

        return i == s.Length && day >= 1 && day <= DaysIn(month, year);
    }

    /// <summary>Validates the 12-digit <c>yyyyMMddHHmm</c> timestamp of a Pix identifier.</summary>
    public static bool IsPixTimestamp(ReadOnlySpan<byte> ts, out int year, out int month, out int day, out int hour, out int minute)
    {
        year = month = day = hour = minute = 0;
        if (ts.Length != 12 || !Text.IsDigits(ts))
        {
            return false;
        }

        year = Num(ts, 0, 4);
        month = Num(ts, 4, 2);
        day = Num(ts, 6, 2);
        hour = Num(ts, 8, 2);
        minute = Num(ts, 10, 2);
        return month >= 1 && month <= 12 && hour < 24 && minute < 60 && day >= 1 && day <= DaysIn(month, year);
    }

    /// <summary>Days in a month of the proleptic Gregorian calendar (year 0 is a leap year).</summary>
    public static int DaysIn(int month, int year) => month switch
    {
        2 => (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static bool ParseDateTime(string s, bool withZone)
    {
        if (!IsAscii(s))
        {
            return false;
        }

        int i = 0;
        if (!Year(s, ref i, out int year)
            || !Literal(s, ref i, '-')
            || !GetNum(s, ref i, true, out int month) || month < 1 || month > 12
            || !Literal(s, ref i, '-')
            || !GetNum(s, ref i, true, out int day)
            || !Literal(s, ref i, 'T')
            || !GetNum(s, ref i, false, out int hour) || hour >= 24
            || !Literal(s, ref i, ':')
            || !GetNum(s, ref i, true, out int minute) || minute >= 60
            || !Literal(s, ref i, ':')
            || !GetNum(s, ref i, true, out int second) || second >= 60)
        {
            return false;
        }

        // Optional fraction: a point or comma and any number of digits.
        if (s.Length - i >= 2 && (s[i] == '.' || s[i] == ',') && IsDigit(s, i + 1))
        {
            i += 2;
            while (IsDigit(s, i))
            {
                i++;
            }
        }

        if (withZone)
        {
            if (i < s.Length && s[i] == 'Z')
            {
                i++;
            }
            else
            {
                if (s.Length - i < 6 || s[i + 3] != ':')
                {
                    return false;
                }

                char sign = s[i];
                int h = i + 1;
                int m = i + 4;
                if (!IsDigit(s, h) || !IsDigit(s, h + 1) || !IsDigit(s, m) || !IsDigit(s, m + 1))
                {
                    return false;
                }

                int offsetHour = ((s[h] - '0') * 10) + (s[h + 1] - '0');
                int offsetMinute = ((s[m] - '0') * 10) + (s[m + 1] - '0');
                if (offsetHour > 24 || offsetMinute > 60 || (sign != '+' && sign != '-'))
                {
                    return false;
                }

                i += 6;
            }
        }

        return i == s.Length && day >= 1 && day <= DaysIn(month, year);
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDigit(string s, int i) => i < s.Length && s[i] >= '0' && s[i] <= '9';

    private static bool Year(string s, ref int i, out int year)
    {
        year = 0;
        if (s.Length - i < 4)
        {
            return false;
        }

        for (int k = 0; k < 4; k++)
        {
            if (!IsDigit(s, i + k))
            {
                return false;
            }

            year = (year * 10) + (s[i + k] - '0');
        }

        i += 4;
        return true;
    }

    private static bool Literal(string s, ref int i, char c)
    {
        if (i < s.Length && s[i] == c)
        {
            i++;
            return true;
        }

        return false;
    }

    // Go's getnum: one or two digits; fixed requires two.
    private static bool GetNum(string s, ref int i, bool fixedWidth, out int value)
    {
        value = 0;
        if (!IsDigit(s, i))
        {
            return false;
        }

        if (!IsDigit(s, i + 1))
        {
            if (fixedWidth)
            {
                return false;
            }

            value = s[i] - '0';
            i++;
            return true;
        }

        value = ((s[i] - '0') * 10) + (s[i + 1] - '0');
        i += 2;
        return true;
    }

    private static int Num(ReadOnlySpan<byte> s, int start, int length)
    {
        int n = 0;
        for (int k = start; k < start + length; k++)
        {
            n = (n * 10) + (s[k] - '0');
        }

        return n;
    }
}
