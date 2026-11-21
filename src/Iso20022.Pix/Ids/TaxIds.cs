namespace Iso20022.Pix.Ids;

/// <summary>CPF and CNPJ validation in the unformatted form carried in ISO 20022 messages.</summary>
public static class TaxIds
{
    /// <summary>
    /// Reports whether <paramref name="s"/> is a valid CPF: 11 digits with
    /// correct check digits, as carried in <c>PrvtId/Othr/Id</c>. Sequences of
    /// one repeated digit, which pass the arithmetic but are not issued, are
    /// rejected.
    /// </summary>
    /// <param name="s">The candidate CPF.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValidCpf(string? s)
    {
        if (s is null || s.Length != 11 || !Internal.Text.IsDigits(s) || Repeated(s))
        {
            return false;
        }

        return CpfDigit(s.AsSpan(0, 9), 10) == s[9] && CpfDigit(s.AsSpan(0, 10), 11) == s[10];
    }

    /// <summary>
    /// Reports whether <paramref name="s"/> is a valid CNPJ in the
    /// 14-character form carried in <c>OrgId/Othr/Id</c>. Both the numeric
    /// CNPJ and the alphanumeric CNPJ introduced by the Receita Federal in
    /// 2026 are accepted: the first 12 characters may be digits or uppercase
    /// letters, the last two are numeric check digits.
    /// </summary>
    /// <param name="s">The candidate CNPJ.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValidCnpj(string? s)
    {
        if (s is null || s.Length != 14 || Repeated(s))
        {
            return false;
        }

        for (int i = 0; i < 12; i++)
        {
            char c = s[i];
            if (!(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'Z'))
            {
                return false;
            }
        }

        if (!Internal.Text.IsDigits(s.Substring(12)))
        {
            return false;
        }

        return CnpjDigit(s.AsSpan(0, 12)) == s[12] && CnpjDigit(s.AsSpan(0, 13)) == s[13];
    }

    private static char CpfDigit(ReadOnlySpan<char> s, int weight)
    {
        int sum = 0;
        for (int i = 0; i < s.Length; i++)
        {
            sum += (s[i] - '0') * (weight - i);
        }

        int r = sum % 11;
        return r < 2 ? '0' : (char)('0' + 11 - r);
    }

    // Each character is valued as its ASCII code minus 48, which maps digits to
    // themselves and letters to 17..42, per the alphanumeric CNPJ specification.
    private static char CnpjDigit(ReadOnlySpan<char> s)
    {
        int sum = 0;
        int w = 2;
        for (int i = s.Length - 1; i >= 0; i--)
        {
            sum += (s[i] - '0') * w;
            w++;
            if (w > 9)
            {
                w = 2;
            }
        }

        int r = sum % 11;
        return r < 2 ? '0' : (char)('0' + 11 - r);
    }

    private static bool Repeated(string s)
    {
        foreach (char c in s)
        {
            if (c != s[0])
            {
                return false;
            }
        }

        return true;
    }
}
