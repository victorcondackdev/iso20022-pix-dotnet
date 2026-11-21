namespace Iso20022.Pix.Components;

// The ISO 20022 building blocks shared by the message models. Property names
// mirror the XML element names (MsgId, Dbtr, Othr, ...) so that code reads like
// the message definition report. Only the elements Pix uses are modelled; when
// a message is parsed, other elements are ignored.
//
// As in the Go implementation, an empty string means "absent" and a null
// reference means an absent optional component. Collections are never null.
// Record equality compares collections by reference; compare the serialized
// XML for structural equality.

/// <summary>Pix account types, carried in the account <c>Tp/Prtry</c>.</summary>
public static class AccountTypes
{
    /// <summary>Conta corrente.</summary>
    public const string Current = "CACC";

    /// <summary>Conta poupança.</summary>
    public const string Savings = "SVGS";

    /// <summary>Conta salário.</summary>
    public const string Salary = "SLRY";

    /// <summary>Conta de pagamento.</summary>
    public const string Payments = "TRAN";
}

/// <summary>
/// The ActiveCurrencyAndAmount component: a decimal amount with a <c>Ccy</c>
/// attribute.
/// </summary>
public sealed record CurrencyAmount
{
    /// <summary>The ISO 4217 code of the Brazilian real, the only currency settled by the SPI.</summary>
    public const string Brl = "BRL";

    /// <summary>The <c>Ccy</c> attribute.</summary>
    public string Ccy { get; set; } = string.Empty;

    /// <summary>The amount.</summary>
    public BrlAmount Value { get; set; }

    /// <summary>Returns an amount in Brazilian reais.</summary>
    /// <param name="value">The amount.</param>
    /// <returns>The component.</returns>
    public static CurrencyAmount InBrl(BrlAmount value) => new() { Ccy = Brl, Value = value };

    /// <summary>Formats the amount as <c>10.50 BRL</c>.</summary>
    /// <returns>The formatted amount.</returns>
    public override string ToString() => Value.ToString() + " " + Ccy;
}

/// <summary>
/// The many ISO 20022 choice components made of an external code (<c>Cd</c>)
/// or a proprietary value (<c>Prtry</c>), such as <c>LclInstrm</c>,
/// <c>CtgyPurp</c>, <c>Purp</c>, account <c>Tp</c> and reason <c>Rsn</c>.
/// Exactly one of the two must be present.
/// </summary>
public sealed record CodeOrProprietary
{
    /// <summary>The external code.</summary>
    public string Cd { get; set; } = string.Empty;

    /// <summary>The proprietary value.</summary>
    public string Prtry { get; set; } = string.Empty;

    /// <summary><see cref="Cd"/> if set, otherwise <see cref="Prtry"/>.</summary>
    public string Value => !string.IsNullOrEmpty(Cd) ? Cd : Prtry ?? string.Empty;

    /// <summary>Returns a choice holding an external code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The component.</returns>
    public static CodeOrProprietary Code(string code) => new() { Cd = code };

    /// <summary>Returns a choice holding a proprietary value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The component.</returns>
    public static CodeOrProprietary Proprietary(string value) => new() { Prtry = value };
}

/// <summary>The PartyIdentification component used for <c>Dbtr</c> and <c>Cdtr</c>.</summary>
public sealed record Party
{
    /// <summary>The name (Max140Text).</summary>
    public string Nm { get; set; } = string.Empty;

    /// <summary>The identification: a CPF or a CNPJ.</summary>
    public PartyId? Id { get; set; }

    /// <summary>
    /// The first <c>Othr/Id</c> of the party's <c>PrvtId</c> or <c>OrgId</c>,
    /// which in Pix is the CPF or CNPJ, or an empty string.
    /// </summary>
    public string TaxId
    {
        get
        {
            if (Id is null)
            {
                return string.Empty;
            }

            foreach (OtherIds? o in new[] { Id.PrvtId, Id.OrgId })
            {
                if (o is not null && o.Othr.Count > 0)
                {
                    return o.Othr[0].Id;
                }
            }

            return string.Empty;
        }
    }

    /// <summary>Returns a person identified by CPF (<c>PrvtId/Othr/Id</c>).</summary>
    /// <param name="name">The name.</param>
    /// <param name="cpf">The 11-digit CPF.</param>
    /// <returns>The party.</returns>
    public static Party Person(string name, string cpf) =>
        new() { Nm = name, Id = new PartyId { PrvtId = OtherIds.Of(cpf) } };

    /// <summary>Returns an organisation identified by CNPJ (<c>OrgId/Othr/Id</c>).</summary>
    /// <param name="name">The name.</param>
    /// <param name="cnpj">The 14-character CNPJ.</param>
    /// <returns>The party.</returns>
    public static Party Organisation(string name, string cnpj) =>
        new() { Nm = name, Id = new PartyId { OrgId = OtherIds.Of(cnpj) } };
}

/// <summary>The Party38Choice component: an organisation (<c>OrgId</c>) or a private person (<c>PrvtId</c>).</summary>
public sealed record PartyId
{
    /// <summary>The organisation identification.</summary>
    public OtherIds? OrgId { get; set; }

    /// <summary>The private person identification.</summary>
    public OtherIds? PrvtId { get; set; }
}

/// <summary>The <c>Othr</c> identifications of an <c>OrgId</c> or <c>PrvtId</c>.</summary>
public sealed record OtherIds
{
    /// <summary>The identifications; in Pix the first is the CPF or CNPJ.</summary>
    public List<GenericId> Othr { get; set; } = new();

    internal static OtherIds Of(string id) => new() { Othr = { new GenericId { Id = id } } };
}

/// <summary>The GenericOrganisationIdentification / GenericPersonIdentification component.</summary>
public sealed record GenericId
{
    /// <summary>The identification.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The scheme name.</summary>
    public CodeOrProprietary? SchmeNm { get; set; }

    /// <summary>The issuer.</summary>
    public string Issr { get; set; } = string.Empty;
}

/// <summary>The CashAccount component used for <c>DbtrAcct</c>, <c>CdtrAcct</c> and the camt.054 <c>Acct</c>.</summary>
public sealed record Account
{
    /// <summary>The account identification.</summary>
    public AccountId Id { get; set; } = new();

    /// <summary>The account type (<see cref="AccountTypes"/>).</summary>
    public CodeOrProprietary? Tp { get; set; }

    /// <summary>The account currency.</summary>
    public string Ccy { get; set; } = string.Empty;

    /// <summary>The account name.</summary>
    public string Nm { get; set; } = string.Empty;

    /// <summary>The proxy: in Pix, the Pix key that addresses the account.</summary>
    public Proxy? Prxy { get; set; }

    /// <summary><c>Id/Othr/Id</c> (the account number), or an empty string.</summary>
    public string Number => Id?.Othr?.Id ?? string.Empty;

    /// <summary><c>Id/Othr/Issr</c> (the branch, agência), or an empty string.</summary>
    public string Branch => Id?.Othr?.Issr ?? string.Empty;

    /// <summary><c>Prxy/Id</c> (the Pix key), or an empty string.</summary>
    public string PixKey => Prxy?.Id ?? string.Empty;

    /// <summary>
    /// Returns a Pix account with the given branch (<c>Issr</c>, may be empty
    /// for payment accounts), number and type (<c>Tp/Prtry</c>).
    /// </summary>
    /// <param name="branch">The branch.</param>
    /// <param name="number">The account number.</param>
    /// <param name="accountType">The account type, one of <see cref="AccountTypes"/>.</param>
    /// <returns>The account.</returns>
    public static Account Create(string branch, string number, string accountType) => new()
    {
        Id = new AccountId { Othr = new GenericAccountId { Id = number, Issr = branch } },
        Tp = CodeOrProprietary.Proprietary(accountType),
    };
}

/// <summary>The AccountIdentification4Choice component. Pix uses <c>Othr</c>.</summary>
public sealed record AccountId
{
    /// <summary>The IBAN.</summary>
    public string IBAN { get; set; } = string.Empty;

    /// <summary>The generic identification: account number and branch.</summary>
    public GenericAccountId? Othr { get; set; }
}

/// <summary>The GenericAccountIdentification1 component. In Pix, <c>Id</c> is the account number and <c>Issr</c> the branch.</summary>
public sealed record GenericAccountId
{
    /// <summary>The account number.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The scheme name.</summary>
    public CodeOrProprietary? SchmeNm { get; set; }

    /// <summary>The branch (agência).</summary>
    public string Issr { get; set; } = string.Empty;
}

/// <summary>The ProxyAccountIdentification1 component (<c>Prxy</c>).</summary>
public sealed record Proxy
{
    /// <summary>The proxy type.</summary>
    public CodeOrProprietary? Tp { get; set; }

    /// <summary>The proxy: in Pix, the Pix key.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// The BranchAndFinancialInstitutionIdentification component used for
/// <c>DbtrAgt</c>, <c>CdtrAgt</c> and the head.001 <c>FIId</c>. Pix
/// identifies participants by ISPB in <c>FinInstnId/ClrSysMmbId/MmbId</c>.
/// </summary>
public sealed record Agent
{
    /// <summary>The financial institution identification.</summary>
    public FinancialInstitutionId FinInstnId { get; set; } = new();

    /// <summary><c>FinInstnId/ClrSysMmbId/MmbId</c>, or an empty string.</summary>
    public string Ispb => FinInstnId?.ClrSysMmbId?.MmbId ?? string.Empty;

    /// <summary>Returns an agent identified by ISPB.</summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <returns>The agent.</returns>
    public static Agent FromIspb(string ispb) =>
        new() { FinInstnId = new FinancialInstitutionId { ClrSysMmbId = new ClearingSystemMemberId { MmbId = ispb } } };
}

/// <summary>The FinancialInstitutionIdentification component.</summary>
public sealed record FinancialInstitutionId
{
    /// <summary>The clearing system member identification.</summary>
    public ClearingSystemMemberId? ClrSysMmbId { get; set; }
}

/// <summary>The ClearingSystemMemberIdentification2 component.</summary>
public sealed record ClearingSystemMemberId
{
    /// <summary>The clearing system identification.</summary>
    public CodeOrProprietary? ClrSysId { get; set; }

    /// <summary>The member identification: in Pix, the ISPB.</summary>
    public string MmbId { get; set; } = string.Empty;
}

/// <summary>The <c>RmtInf</c> component. Pix uses unstructured remittance information, the message shown to the payee.</summary>
public sealed record RemittanceInformation
{
    /// <summary>The unstructured lines (Max140Text each).</summary>
    public List<string> Ustrd { get; set; } = new();
}

/// <summary>The DateAndDateTime2Choice component: exactly one of <c>Dt</c> or <c>DtTm</c>.</summary>
public sealed record DateAndDateTime
{
    /// <summary>The ISODate.</summary>
    public string Dt { get; set; } = string.Empty;

    /// <summary>The ISODateTime.</summary>
    public string DtTm { get; set; } = string.Empty;
}

/// <summary>An ISO 20022 external code, such as a status or return reason, with its descriptions.</summary>
/// <param name="Code">The code, e.g. <c>AM04</c>.</param>
/// <param name="Name">The ISO 20022 code name, e.g. <c>InsufficientFunds</c>.</param>
/// <param name="English">The English description.</param>
/// <param name="Portuguese">The Portuguese description.</param>
public sealed record CodeInfo(string Code, string Name, string English, string Portuguese);

/// <summary>A lookup table of external codes.</summary>
public sealed class CodeSet
{
    private readonly Dictionary<string, CodeInfo> _codes;

    /// <summary>Builds a table from its entries.</summary>
    /// <param name="entries">The entries.</param>
    public CodeSet(IEnumerable<CodeInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _codes = new Dictionary<string, CodeInfo>(StringComparer.Ordinal);
        foreach (CodeInfo e in entries)
        {
            _codes[e.Code] = e;
        }
    }

    /// <summary>The number of codes.</summary>
    public int Count => _codes.Count;

    /// <summary>
    /// Looks up a code. Unknown codes are reported with <see langword="false"/>;
    /// messages carrying them are still valid and the code is kept as-is.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="info">The description, when known.</param>
    /// <returns><see langword="true"/> when the code is known.</returns>
    public bool TryLookup(string? code, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CodeInfo? info)
    {
        if (code is not null && _codes.TryGetValue(code, out CodeInfo? found))
        {
            info = found;
            return true;
        }

        info = null;
        return false;
    }

    /// <summary>Reports whether the code is known.</summary>
    /// <param name="code">The code.</param>
    /// <returns><see langword="true"/> when known.</returns>
    public bool Contains(string? code) => code is not null && _codes.ContainsKey(code);

    /// <summary>Returns the entries ordered by code.</summary>
    /// <returns>The sorted entries.</returns>
    public IReadOnlyList<CodeInfo> Sorted() => _codes.Values.OrderBy(c => c.Code, StringComparer.Ordinal).ToArray();
}
