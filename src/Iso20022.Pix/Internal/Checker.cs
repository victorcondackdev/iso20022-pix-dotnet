using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Internal;

/// <summary>
/// The validation rules shared by the message models. A checker accumulates
/// every violation instead of stopping at the first one. Paths and messages
/// are identical to the Go implementation.
/// </summary>
internal sealed class Checker
{
    private static readonly string[] PixAccountTypes =
        { AccountTypes.Current, AccountTypes.Savings, AccountTypes.Salary, AccountTypes.Payments };

    private readonly List<Violation> _violations = new();

    public IReadOnlyList<Violation> Result => _violations.ToArray();

    public static string Idx(string path, int i) => path + "[" + GoFormat.Int(i) + "]";

    public void Add(string path, string message) => _violations.Add(new Violation(path, message));

    public void AddAll(IEnumerable<Violation> violations) => _violations.AddRange(violations);

    /// <summary>An ISO 20022 MaxNText value: valid, XML-safe and at most <paramref name="max"/> characters.</summary>
    public void Text(string path, string? s, int max, bool required)
    {
        if (string.IsNullOrEmpty(s))
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        if (!Internal.Text.IsValidUtf16(s))
        {
            Add(path, "is not valid UTF-8");
            return;
        }

        int bad = Internal.Text.FirstNonXmlChar(s);
        if (bad >= 0)
        {
            Add(path, "contains " + GoFormat.CodePoint(bad) + ", which XML 1.0 does not allow");
            return;
        }

        int n = Internal.Text.RuneCount(s);
        if (n > max)
        {
            Add(path, "must be at most " + GoFormat.Int(max) + " characters, got " + GoFormat.Int(n));
        }
    }

    public void Max35(string path, string? s, bool required) => Text(path, s, 35, required);

    public void Code(string path, string? s, bool required) => Text(path, s, 4, required);

    public void OneOf(string path, string? s, bool required, params string[] allowed)
    {
        if (string.IsNullOrEmpty(s))
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        if (Array.IndexOf(allowed, s) >= 0)
        {
            return;
        }

        Add(path, "must be one of " + GoFormat.List(allowed) + ", got " + GoFormat.Quote(s));
    }

    public void DateTime(string path, string? d, bool required)
    {
        if (string.IsNullOrEmpty(d))
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        if (!IsoTime.IsDateTime(d))
        {
            Add(path, "must be an ISO 8601 date and time, got " + GoFormat.Quote(d));
        }
    }

    public void Date(string path, string? d, bool required)
    {
        if (string.IsNullOrEmpty(d))
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        if (!IsoTime.IsDate(d))
        {
            Add(path, "must be an ISO 8601 date (YYYY-MM-DD), got " + GoFormat.Quote(d));
        }
    }

    public void DateOrDateTime(string path, DateAndDateTime? d, bool required)
    {
        if (d is null)
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        bool hasDate = !string.IsNullOrEmpty(d.Dt);
        bool hasDateTime = !string.IsNullOrEmpty(d.DtTm);
        if (hasDate && hasDateTime)
        {
            Add(path, "must contain exactly one of Dt or DtTm, got both");
        }
        else if (!hasDate && !hasDateTime)
        {
            Add(path, "must contain exactly one of Dt or DtTm");
        }
        else if (hasDate)
        {
            Date(path + "/Dt", d.Dt, true);
        }
        else
        {
            DateTime(path + "/DtTm", d.DtTm, true);
        }
    }

    /// <summary>The amount is in BRL and, when <paramref name="positive"/>, greater than zero.</summary>
    public void Brl(string path, CurrencyAmount a, bool positive)
    {
        if (a.Ccy != CurrencyAmount.Brl)
        {
            Add(path + "/@Ccy", "must be " + GoFormat.Quote(CurrencyAmount.Brl) + ", got " + GoFormat.Quote(a.Ccy));
        }

        if (a.Value.Cents < 0)
        {
            Add(path, "must not be negative");
        }
        else if (a.Value > BrlAmount.MaxValue)
        {
            Add(path, "exceeds 18 digits");
        }
        else if (positive && a.Value.IsZero)
        {
            Add(path, "must be greater than zero");
        }
    }

    /// <summary>A Cd/Prtry choice: exactly one present, Cd at most 4 characters, Prtry at most <paramref name="maxPrtry"/>.</summary>
    public void Choice(string path, CodeOrProprietary? cp, int maxPrtry)
    {
        if (cp is null)
        {
            return;
        }

        bool hasCode = !string.IsNullOrEmpty(cp.Cd);
        bool hasPrtry = !string.IsNullOrEmpty(cp.Prtry);
        if (hasCode && hasPrtry)
        {
            Add(path, "must contain exactly one of Cd or Prtry, got both");
        }
        else if (!hasCode && !hasPrtry)
        {
            Add(path, "must contain exactly one of Cd or Prtry");
        }
        else if (hasCode)
        {
            Code(path + "/Cd", cp.Cd, true);
        }
        else
        {
            Text(path + "/Prtry", cp.Prtry, maxPrtry, true);
        }
    }

    public void EndToEndId(string path, string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            Add(path, "is required");
            return;
        }

        string? error = PixIdentifiers.CheckId(s, PixIdentifiers.PrefixEndToEnd, out _);
        if (error is not null)
        {
            Add(path, error);
        }
    }

    public void ReturnId(string path, string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            Add(path, "is required");
            return;
        }

        string? error = PixIdentifiers.CheckId(s, PixIdentifiers.PrefixReturn, out _);
        if (error is not null)
        {
            Add(path, error);
        }
    }

    /// <summary>A DbtrAgt/CdtrAgt identified by ISPB.</summary>
    public void Agent(string path, Agent a)
    {
        string p = path + "/FinInstnId/ClrSysMmbId/MmbId";
        string ispb = a.Ispb;
        if (ispb.Length == 0)
        {
            Add(p, "is required (ISPB)");
        }
        else if (!PixIdentifiers.IsValidIspb(ispb))
        {
            Add(p, "must be an 8-digit ISPB, got " + GoFormat.Quote(ispb));
        }

        ClearingSystemMemberId? member = a.FinInstnId?.ClrSysMmbId;
        if (member is not null)
        {
            Choice(path + "/FinInstnId/ClrSysMmbId/ClrSysId", member.ClrSysId, 35);
        }
    }

    /// <summary>A Dbtr/Cdtr: name (Max140Text) and, when <paramref name="idRequired"/>, a CPF or a CNPJ.</summary>
    public void Party(string path, Party p, bool nameRequired, bool idRequired)
    {
        Text(path + "/Nm", p.Nm, 140, nameRequired);
        if (p.Id is null)
        {
            if (idRequired)
            {
                Add(path + "/Id", "is required (CPF or CNPJ)");
            }

            return;
        }

        if (p.Id.OrgId is not null && p.Id.PrvtId is not null)
        {
            Add(path + "/Id", "must contain exactly one of OrgId or PrvtId, got both");
        }
        else if (p.Id.PrvtId is not null)
        {
            OtherIdentifications(path + "/Id/PrvtId", p.Id.PrvtId, "CPF", TaxIds.IsValidCpf);
        }
        else if (p.Id.OrgId is not null)
        {
            OtherIdentifications(path + "/Id/OrgId", p.Id.OrgId, "CNPJ", TaxIds.IsValidCnpj);
        }
        else
        {
            Add(path + "/Id", "must contain OrgId or PrvtId");
        }
    }

    /// <summary>A Pix account: number (<c>Id/Othr/Id</c>, Max34Text) and type required, optional valid Pix key.</summary>
    public void Account(string path, Account? a, bool required)
    {
        if (a is null)
        {
            if (required)
            {
                Add(path, "is required");
            }

            return;
        }

        AccountId id = a.Id ?? new AccountId();
        bool hasIban = !string.IsNullOrEmpty(id.IBAN);
        if (hasIban && id.Othr is not null)
        {
            Add(path + "/Id", "must contain exactly one of IBAN or Othr, got both");
        }
        else if (hasIban)
        {
            Text(path + "/Id/IBAN", id.IBAN, 34, true);
        }
        else if (id.Othr is null)
        {
            Add(path + "/Id/Othr/Id", "is required (account number)");
        }
        else
        {
            GenericAccountId o = id.Othr;
            Text(path + "/Id/Othr/Id", o.Id, 34, true);
            Choice(path + "/Id/Othr/SchmeNm", o.SchmeNm, 35);
            Max35(path + "/Id/Othr/Issr", o.Issr, false);
        }

        if (a.Tp is null)
        {
            Add(path + "/Tp", "is required (CACC, SVGS, SLRY or TRAN)");
        }
        else
        {
            Choice(path + "/Tp", a.Tp, 35);
            if (string.IsNullOrEmpty(a.Tp.Cd) != string.IsNullOrEmpty(a.Tp.Prtry))
            {
                string field = !string.IsNullOrEmpty(a.Tp.Cd) ? "Cd" : "Prtry";
                OneOf(path + "/Tp/" + field, a.Tp.Value, false, PixAccountTypes);
            }
        }

        if (!string.IsNullOrEmpty(a.Ccy) && a.Ccy != CurrencyAmount.Brl)
        {
            Add(path + "/Ccy", "must be " + GoFormat.Quote(CurrencyAmount.Brl) + ", got " + GoFormat.Quote(a.Ccy));
        }

        Text(path + "/Nm", a.Nm, 70, false);
        if (a.Prxy is not null)
        {
            Choice(path + "/Prxy/Tp", a.Prxy.Tp, 35);
            if (string.IsNullOrEmpty(a.Prxy.Id))
            {
                Add(path + "/Prxy/Id", "is required");
            }
            else
            {
                string? error = PixKeys.CheckCore(a.Prxy.Id, out _);
                if (error is not null)
                {
                    Add(path + "/Prxy/Id", error);
                }
            }
        }
    }

    /// <summary><c>RmtInf/Ustrd</c>, Max140Text each.</summary>
    public void Remittance(string path, RemittanceInformation? r)
    {
        if (r is null)
        {
            return;
        }

        for (int i = 0; i < r.Ustrd.Count; i++)
        {
            Text(Idx(path + "/Ustrd", i), r.Ustrd[i], 140, true);
        }
    }

    /// <summary>A Max15NumericText transaction count, compared with the actual number of transactions.</summary>
    public void NbOfTxs(string path, string? s, int actual)
    {
        if (string.IsNullOrEmpty(s))
        {
            Add(path, "is required");
            return;
        }

        if (!TryGoAtoi(s, out long n) || Internal.Text.Utf8Length(s) > 15 || n < 0 || s[0] == '+' || s[0] == '-')
        {
            Add(path, "must be a number of up to 15 digits, got " + GoFormat.Quote(s));
            return;
        }

        if (n != actual)
        {
            Add(path, "is " + GoFormat.Int(n) + " but the message has " + GoFormat.Int(actual) + " transactions");
        }
    }

    private void OtherIdentifications(string path, OtherIds o, string kind, Func<string, bool> valid)
    {
        if (o.Othr.Count == 0)
        {
            Add(path + "/Othr", "is required (" + kind + ")");
            return;
        }

        for (int i = 0; i < o.Othr.Count; i++)
        {
            GenericId g = o.Othr[i];
            string p = Idx(path + "/Othr", i);
            Choice(p + "/SchmeNm", g.SchmeNm, 35);
            Max35(p + "/Issr", g.Issr, false);
            if (i == 0)
            {
                if (!valid(g.Id ?? string.Empty))
                {
                    Add(p + "/Id", "must be a valid " + kind + ", got " + GoFormat.Quote(g.Id));
                }

                continue;
            }

            Max35(p + "/Id", g.Id, true);
        }
    }

    // Go's strconv.Atoi: an optional sign followed by ASCII digits, within 64 bits.
    private static bool TryGoAtoi(string s, out long n)
    {
        n = 0;
        int i = 0;
        bool negative = false;
        if (s[0] == '+' || s[0] == '-')
        {
            negative = s[0] == '-';
            i = 1;
        }

        if (i == s.Length)
        {
            return false;
        }

        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9')
            {
                return false;
            }

            if (n > (long.MaxValue - (c - '0')) / 10)
            {
                return false;
            }

            n = (n * 10) + (c - '0');
        }

        if (negative)
        {
            n = -n;
        }

        return true;
    }
}
