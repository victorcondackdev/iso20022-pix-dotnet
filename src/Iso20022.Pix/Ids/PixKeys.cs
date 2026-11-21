using System.Text;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix.Ids;

/// <summary>The type of a Pix key (chave Pix) as registered in the DICT.</summary>
public enum PixKeyType
{
    /// <summary>Not a Pix key.</summary>
    Unknown,

    /// <summary>An 11-digit CPF.</summary>
    Cpf,

    /// <summary>A 14-character CNPJ, numeric or alphanumeric.</summary>
    Cnpj,

    /// <summary>An e-mail address, lowercase, at most 77 characters.</summary>
    Email,

    /// <summary>An E.164 phone number, e.g. <c>+5521987654321</c>.</summary>
    Phone,

    /// <summary>A random key: a lowercase UUID with hyphens.</summary>
    Evp,
}

/// <summary>Classification of Pix keys as they appear in messages, for example in <c>CdtrAcct/Prxy/Id</c>.</summary>
public static class PixKeys
{
    /// <summary>The maximum length of an e-mail Pix key.</summary>
    public const int MaxEmailKeyLength = 77;

    /// <summary>
    /// Returns the type of a Pix key. Keys inside SPI messages are already in
    /// canonical form, so no normalisation is applied: formatted CPFs,
    /// uppercase e-mails or phone numbers without <c>+</c> are rejected.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The key type.</returns>
    /// <exception cref="InvalidIdentifierException">The key is not a valid Pix key.</exception>
    public static PixKeyType Classify(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string? error = CheckCore(key, out PixKeyType type);
        return error is null ? type : throw new InvalidIdentifierException(IdentifierKind.PixKey, error);
    }

    /// <summary>Classifies a Pix key without throwing.</summary>
    /// <param name="key">The key.</param>
    /// <param name="type">The key type, or <see cref="PixKeyType.Unknown"/>.</param>
    /// <returns><see langword="true"/> when the key is valid.</returns>
    public static bool TryClassify(string? key, out PixKeyType type)
    {
        type = PixKeyType.Unknown;
        return key is not null && CheckCore(key, out type) is null;
    }

    /// <summary>The DICT name of a key type: CPF, CNPJ, EMAIL, PHONE, EVP or UNKNOWN.</summary>
    /// <param name="type">The key type.</param>
    /// <returns>The name.</returns>
    public static string DictName(PixKeyType type) => type switch
    {
        PixKeyType.Cpf => "CPF",
        PixKeyType.Cnpj => "CNPJ",
        PixKeyType.Email => "EMAIL",
        PixKeyType.Phone => "PHONE",
        PixKeyType.Evp => "EVP",
        _ => "UNKNOWN",
    };

    /// <summary>Returns the reason the key is invalid (without the <c>ids: invalid Pix key:</c> prefix), or null.</summary>
    internal static string? CheckCore(string key, out PixKeyType type)
    {
        type = PixKeyType.Unknown;
        if (key.Length == 0)
        {
            return "empty";
        }

        // Lengths and positions are counted in UTF-8 bytes, as in Go.
        byte[] s = Encoding.UTF8.GetBytes(key);
        if (Array.IndexOf(s, (byte)'@') >= 0)
        {
            string? error = CheckEmail(key, s);
            if (error is not null)
            {
                return error;
            }

            type = PixKeyType.Email;
            return null;
        }

        if (s[0] == '+')
        {
            if (!ValidPhone(s))
            {
                return "phone key " + GoFormat.Quote(key) + " must be E.164 (+ and up to 15 digits)";
            }

            type = PixKeyType.Phone;
            return null;
        }

        if (s.Length == 36 && s[8] == '-')
        {
            if (!ValidUuid(s))
            {
                return "EVP key " + GoFormat.Quote(key) + " must be a lowercase UUID";
            }

            type = PixKeyType.Evp;
            return null;
        }

        if (s.Length == 11 && TaxIds.IsValidCpf(key))
        {
            type = PixKeyType.Cpf;
            return null;
        }

        if (s.Length == 14 && TaxIds.IsValidCnpj(key))
        {
            type = PixKeyType.Cnpj;
            return null;
        }

        return GoFormat.Quote(key) + " is not a CPF, CNPJ, e-mail, phone or EVP key";
    }

    private static string? CheckEmail(string key, byte[] s)
    {
        if (Internal.Text.RuneCount(key) > MaxEmailKeyLength)
        {
            return "e-mail key longer than " + GoFormat.Int(MaxEmailKeyLength) + " characters";
        }

        int at = Array.IndexOf(s, (byte)'@');
        ReadOnlySpan<byte> local = s.AsSpan(0, at);
        ReadOnlySpan<byte> domain = s.AsSpan(at + 1);
        if (local.IsEmpty || domain.IsEmpty || domain.IndexOf((byte)'@') >= 0
            || domain.IndexOf((byte)'.') < 0 || domain[0] == '.' || domain[domain.Length - 1] == '.')
        {
            return "malformed e-mail key " + GoFormat.Quote(key);
        }

        foreach (byte c in s)
        {
            if (c <= ' ' || c >= 0x7F || (c >= 'A' && c <= 'Z'))
            {
                return "e-mail key " + GoFormat.Quote(key) + " must be lowercase ASCII without spaces";
            }
        }

        return null;
    }

    // The E.164 shape used by the DICT: "+", a non-zero first digit, at most 15 digits.
    private static bool ValidPhone(byte[] s)
    {
        ReadOnlySpan<byte> d = s.AsSpan(1);
        return d.Length >= 2 && d.Length <= 15 && d[0] != '0' && Internal.Text.IsDigits(d);
    }

    private static bool ValidUuid(byte[] s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            byte c = s[i];
            if (i == 8 || i == 13 || i == 18 || i == 23)
            {
                if (c != '-')
                {
                    return false;
                }
            }
            else if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
