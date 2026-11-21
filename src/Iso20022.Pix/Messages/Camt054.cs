using System.Text;
using System.Xml;
using Iso20022.Pix.Components;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>Constants of the camt.054 message (BankToCustomerDebitCreditNotification).</summary>
public static class Camt054
{
    /// <summary>The namespace used when serializing a document whose namespace is empty.</summary>
    public const string DefaultNamespace = MessageIds.NamespacePrefix + "camt.054.001.08";

    /// <summary>Credit indicator (<c>CdtDbtInd</c>).</summary>
    public const string Credit = "CRDT";

    /// <summary>Debit indicator (<c>CdtDbtInd</c>).</summary>
    public const string Debit = "DBIT";

    /// <summary>Entry status (<c>Sts/Cd</c>): booked.</summary>
    public const string StatusBooked = "BOOK";

    /// <summary>Entry status: pending.</summary>
    public const string StatusPending = "PDNG";

    /// <summary>Entry status: information only.</summary>
    public const string StatusInfo = "INFO";
}

/// <summary>
/// A camt.054 message (BankToCustomerDebitCreditNotification): notifications
/// of entries booked to an account, such as the credits and debits of Pix
/// payments and returns, with the EndToEndId of each underlying transaction.
/// The model follows the element order of camt.054.001.08.
/// </summary>
public sealed record Camt054Document
{
    private const string Root = "/Document/BkToCstmrDbtCdtNtfctn";

    /// <summary>The Document namespace, as parsed; empty means <see cref="Camt054.DefaultNamespace"/>.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The message root.</summary>
    public Notification BkToCstmrDbtCdtNtfctn { get; set; } = new();

    /// <summary>The message version from the namespace, e.g. <c>08</c>, or an empty string.</summary>
    public string Version => MessageType.TryParse(Namespace, out MessageType t) ? t.Version : string.Empty;

    /// <summary>The entries of every notification, in order.</summary>
    public IReadOnlyList<Entry> Entries => BkToCstmrDbtCdtNtfctn.Ntfctn.SelectMany(n => n.Ntry).ToArray();

    /// <summary>Parses a camt.054 message (bare Document or business message).</summary>
    /// <param name="xml">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a camt.054 message.</exception>
    public static Camt054Document Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return XmlInput.ReadDocument<Camt054Document>(r, MessageIds.Camt054, Read);
    }

    /// <summary>Parses a camt.054 message from a stream, which is not closed.</summary>
    /// <param name="stream">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a camt.054 message.</exception>
    public static Camt054Document Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return XmlInput.ReadDocument<Camt054Document>(r, MessageIds.Camt054, Read);
    }

    /// <summary>Parses a camt.054 message from UTF-8 bytes.</summary>
    /// <param name="utf8">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a camt.054 message.</exception>
    public static Camt054Document Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var stream = new MemoryStream(utf8, writable: false);
        return Parse(stream);
    }

    /// <summary>Serializes the document as indented XML, byte-identical to the Go implementation. Does not validate.</summary>
    /// <returns>The XML.</returns>
    /// <exception cref="InvalidAmountException">An amount is negative or too large to serialize.</exception>
    public string ToXml()
    {
        var w = new XmlOutput();
        w.Start("Document", string.IsNullOrEmpty(Namespace) ? Camt054.DefaultNamespace : Namespace);
        Notification m = BkToCstmrDbtCdtNtfctn;
        w.Start("BkToCstmrDbtCdtNtfctn");
        w.Start("GrpHdr");
        w.Element("MsgId", m.GrpHdr.MsgId);
        w.Element("CreDtTm", m.GrpHdr.CreDtTm);
        w.End("GrpHdr");
        foreach (AccountNotification n in m.Ntfctn)
        {
            w.Start("Ntfctn");
            w.Element("Id", n.Id);
            w.Optional("CreDtTm", n.CreDtTm);
            Xsd.WriteAccount(w, "Acct", n.Acct);
            foreach (Entry e in n.Ntry)
            {
                WriteEntry(w, e);
            }

            w.End("Ntfctn");
        }

        w.End("BkToCstmrDbtCdtNtfctn");
        w.End("Document");
        return w.Finish();
    }

    /// <summary>Serializes the document as UTF-8 bytes; see <see cref="ToXml"/>.</summary>
    /// <returns>The XML bytes, without a byte order mark.</returns>
    public byte[] ToXmlBytes() => Encoding.UTF8.GetBytes(ToXml());

    /// <summary>
    /// Checks the notification and returns every violation: MsgId and
    /// CreDtTm, at least one <c>Ntfctn</c> with an Id and an account, and for
    /// each entry a BRL amount, CdtDbtInd CRDT or DBIT, a status, a booking
    /// date when booked, a bank transaction code, well-formed references (an
    /// EndToEndId must be a valid Pix EndToEndId) and, when every transaction
    /// detail carries an amount, details that add up to the entry amount.
    /// </summary>
    /// <returns>The violations; empty when the notification is valid.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        var c = new Checker();
        if (!string.IsNullOrEmpty(Namespace)
            && (!MessageType.TryParse(Namespace, out MessageType id) || id.Id != MessageIds.Camt054))
        {
            c.Add("/Document/@xmlns", "must be a camt.054 namespace, got " + GoFormat.Quote(Namespace));
        }

        Notification m = BkToCstmrDbtCdtNtfctn;
        c.Max35(Root + "/GrpHdr/MsgId", m.GrpHdr.MsgId, true);
        c.DateTime(Root + "/GrpHdr/CreDtTm", m.GrpHdr.CreDtTm, true);
        if (m.Ntfctn.Count == 0)
        {
            c.Add(Root + "/Ntfctn", "at least one notification is required");
        }

        for (int i = 0; i < m.Ntfctn.Count; i++)
        {
            AccountNotification n = m.Ntfctn[i];
            string p = Checker.Idx(Root + "/Ntfctn", i);
            c.Max35(p + "/Id", n.Id, true);
            c.DateTime(p + "/CreDtTm", n.CreDtTm, false);
            AccountId acctId = n.Acct.Id ?? new AccountId();
            if (acctId.Othr is not null)
            {
                c.Text(p + "/Acct/Id/Othr/Id", acctId.Othr.Id, 34, true);
                c.Max35(p + "/Acct/Id/Othr/Issr", acctId.Othr.Issr, false);
            }
            else if (!string.IsNullOrEmpty(acctId.IBAN))
            {
                c.Text(p + "/Acct/Id/IBAN", acctId.IBAN, 34, true);
            }
            else
            {
                c.Add(p + "/Acct/Id", "is required");
            }

            for (int j = 0; j < n.Ntry.Count; j++)
            {
                ValidateEntry(c, Checker.Idx(p + "/Ntry", j), n.Ntry[j]);
            }
        }

        return c.Result;
    }

    private static void ValidateEntry(Checker c, string p, Entry e)
    {
        c.Max35(p + "/NtryRef", e.NtryRef, false);
        c.Brl(p + "/Amt", e.Amt, false);
        c.OneOf(p + "/CdtDbtInd", e.CdtDbtInd, true, Camt054.Credit, Camt054.Debit);
        if (string.IsNullOrEmpty(e.Sts.Cd) && string.IsNullOrEmpty(e.Sts.Prtry))
        {
            c.Add(p + "/Sts", "is required");
        }
        else
        {
            c.Choice(p + "/Sts", e.Sts, 35);
        }

        if (e.Sts.Cd == Camt054.StatusBooked && e.BookgDt is null)
        {
            c.Add(p + "/BookgDt", "is required for booked entries");
        }

        c.DateOrDateTime(p + "/BookgDt", e.BookgDt, false);
        c.DateOrDateTime(p + "/ValDt", e.ValDt, false);
        c.Max35(p + "/AcctSvcrRef", e.AcctSvcrRef, false);
        BankTransactionCode b = e.BkTxCd;
        if (b.Domn is null && b.Prtry is null)
        {
            c.Add(p + "/BkTxCd", "must contain Domn or Prtry");
        }
        else
        {
            if (b.Domn is not null)
            {
                c.Code(p + "/BkTxCd/Domn/Cd", b.Domn.Cd, true);
                c.Code(p + "/BkTxCd/Domn/Fmly/Cd", b.Domn.Fmly.Cd, true);
                c.Code(p + "/BkTxCd/Domn/Fmly/SubFmlyCd", b.Domn.Fmly.SubFmlyCd, true);
            }

            if (b.Prtry is not null)
            {
                c.Max35(p + "/BkTxCd/Prtry/Cd", b.Prtry.Cd, true);
                c.Max35(p + "/BkTxCd/Prtry/Issr", b.Prtry.Issr, false);
            }
        }

        BrlAmount sum = BrlAmount.Zero;
        bool allAmounts = true;
        int details = 0;
        for (int i = 0; i < e.NtryDtls.Count; i++)
        {
            List<TransactionDetails> txs = e.NtryDtls[i].TxDtls;
            for (int j = 0; j < txs.Count; j++)
            {
                TransactionDetails t = txs[j];
                details++;
                string q = Checker.Idx(Checker.Idx(p + "/NtryDtls", i) + "/TxDtls", j);
                References? refs = t.Refs;
                if (refs is not null)
                {
                    c.Max35(q + "/Refs/MsgId", refs.MsgId, false);
                    c.Max35(q + "/Refs/AcctSvcrRef", refs.AcctSvcrRef, false);
                    c.Max35(q + "/Refs/InstrId", refs.InstrId, false);
                    if (!string.IsNullOrEmpty(refs.EndToEndId))
                    {
                        c.EndToEndId(q + "/Refs/EndToEndId", refs.EndToEndId);
                    }

                    c.Max35(q + "/Refs/TxId", refs.TxId, false);
                }

                if (t.Amt is null)
                {
                    allAmounts = false;
                }
                else
                {
                    c.Brl(q + "/Amt", t.Amt, false);
                    if (sum.TryAdd(t.Amt.Value, out BrlAmount s))
                    {
                        sum = s;
                    }
                    else
                    {
                        allAmounts = false;
                    }
                }

                c.OneOf(q + "/CdtDbtInd", t.CdtDbtInd, false, Camt054.Credit, Camt054.Debit);
                c.Remittance(q + "/RmtInf", t.RmtInf);
                c.Text(q + "/AddtlTxInf", t.AddtlTxInf, 500, false);
            }
        }

        if (details > 0 && allAmounts && sum != e.Amt.Value)
        {
            c.Add(p + "/Amt", "is " + e.Amt.Value.ToString() + " but the transaction details add up to " + sum.ToString());
        }

        c.Text(p + "/AddtlNtryInf", e.AddtlNtryInf, 500, false);
    }

    private static void WriteEntry(XmlOutput w, Entry e)
    {
        w.Start("Ntry");
        w.Optional("NtryRef", e.NtryRef);
        w.Amount("Amt", e.Amt);
        w.Element("CdtDbtInd", e.CdtDbtInd);
        if (e.RvslInd is bool reversal)
        {
            w.Element("RvslInd", reversal ? "true" : "false");
        }

        w.Start("Sts");
        w.Optional("Cd", e.Sts.Cd);
        w.Optional("Prtry", e.Sts.Prtry);
        w.End("Sts");
        Xsd.WriteDateAndDateTime(w, "BookgDt", e.BookgDt);
        Xsd.WriteDateAndDateTime(w, "ValDt", e.ValDt);
        w.Optional("AcctSvcrRef", e.AcctSvcrRef);
        w.Start("BkTxCd");
        if (e.BkTxCd.Domn is not null)
        {
            Domain d = e.BkTxCd.Domn;
            w.Start("Domn");
            w.Element("Cd", d.Cd);
            w.Start("Fmly");
            w.Element("Cd", d.Fmly.Cd);
            w.Element("SubFmlyCd", d.Fmly.SubFmlyCd);
            w.End("Fmly");
            w.End("Domn");
        }

        if (e.BkTxCd.Prtry is not null)
        {
            w.Start("Prtry");
            w.Element("Cd", e.BkTxCd.Prtry.Cd);
            w.Optional("Issr", e.BkTxCd.Prtry.Issr);
            w.End("Prtry");
        }

        w.End("BkTxCd");
        foreach (EntryDetails nd in e.NtryDtls)
        {
            w.Start("NtryDtls");
            foreach (TransactionDetails t in nd.TxDtls)
            {
                w.Start("TxDtls");
                if (t.Refs is not null)
                {
                    w.Start("Refs");
                    w.Optional("MsgId", t.Refs.MsgId);
                    w.Optional("AcctSvcrRef", t.Refs.AcctSvcrRef);
                    w.Optional("InstrId", t.Refs.InstrId);
                    w.Optional("EndToEndId", t.Refs.EndToEndId);
                    w.Optional("TxId", t.Refs.TxId);
                    w.End("Refs");
                }

                if (t.Amt is not null)
                {
                    w.Amount("Amt", t.Amt);
                }

                w.Optional("CdtDbtInd", t.CdtDbtInd);
                Xsd.WriteRemittance(w, "RmtInf", t.RmtInf);
                w.Optional("AddtlTxInf", t.AddtlTxInf);
                w.End("TxDtls");
            }

            w.End("NtryDtls");
        }

        w.Optional("AddtlNtryInf", e.AddtlNtryInf);
        w.End("Ntry");
    }

    private static Camt054Document Read(XmlReader r, string ns)
    {
        var d = new Camt054Document { Namespace = ns };
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName != "BkToCstmrDbtCdtNtfctn")
            {
                r.Skip();
                continue;
            }

            Notification m = d.BkToCstmrDbtCdtNtfctn;
            int d2 = XmlInput.Enter(r);
            while (XmlInput.NextChild(r, d2))
            {
                switch (r.LocalName)
                {
                    case "GrpHdr": m.GrpHdr.ReadInto(r); break;
                    case "Ntfctn": m.Ntfctn.Add(ReadNotification(r)); break;
                    default: r.Skip(); break;
                }
            }
        }

        return d;
    }

    private static AccountNotification ReadNotification(XmlReader r)
    {
        var n = new AccountNotification();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Id": n.Id = XmlInput.ReadText(r); break;
                case "CreDtTm": n.CreDtTm = XmlInput.ReadText(r); break;
                case "Acct": Xsd.ReadAccount(r, n.Acct); break;
                case "Ntry": n.Ntry.Add(ReadEntry(r)); break;
                default: r.Skip(); break;
            }
        }

        return n;
    }

    private static Entry ReadEntry(XmlReader r)
    {
        var e = new Entry();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "NtryRef": e.NtryRef = XmlInput.ReadText(r); break;
                case "Amt": XmlInput.ReadAmount(r, e.Amt); break;
                case "CdtDbtInd": e.CdtDbtInd = XmlInput.ReadText(r); break;
                case "RvslInd": e.RvslInd = XmlInput.ReadBoolean(r); break;
                case "Sts": Xsd.ReadChoice(r, e.Sts); break;
                case "BookgDt": e.BookgDt = Xsd.ReadDateAndDateTime(r, e.BookgDt); break;
                case "ValDt": e.ValDt = Xsd.ReadDateAndDateTime(r, e.ValDt); break;
                case "AcctSvcrRef": e.AcctSvcrRef = XmlInput.ReadText(r); break;
                case "BkTxCd": ReadBankTransactionCode(r, e.BkTxCd); break;
                case "NtryDtls": e.NtryDtls.Add(ReadEntryDetails(r)); break;
                case "AddtlNtryInf": e.AddtlNtryInf = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }

        return e;
    }

    private static void ReadBankTransactionCode(XmlReader r, BankTransactionCode b)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Domn":
                    b.Domn ??= new Domain();
                    ReadDomain(r, b.Domn);
                    break;
                case "Prtry":
                    b.Prtry ??= new ProprietaryBankTransactionCode();
                    ReadProprietary(r, b.Prtry);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static void ReadDomain(XmlReader r, Domain d)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Cd":
                    d.Cd = XmlInput.ReadText(r);
                    break;
                case "Fmly":
                    int d2 = XmlInput.Enter(r);
                    while (XmlInput.NextChild(r, d2))
                    {
                        switch (r.LocalName)
                        {
                            case "Cd": d.Fmly.Cd = XmlInput.ReadText(r); break;
                            case "SubFmlyCd": d.Fmly.SubFmlyCd = XmlInput.ReadText(r); break;
                            default: r.Skip(); break;
                        }
                    }

                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static void ReadProprietary(XmlReader r, ProprietaryBankTransactionCode p)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Cd": p.Cd = XmlInput.ReadText(r); break;
                case "Issr": p.Issr = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }

    private static EntryDetails ReadEntryDetails(XmlReader r)
    {
        var nd = new EntryDetails();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "TxDtls")
            {
                nd.TxDtls.Add(ReadTransactionDetails(r));
            }
            else
            {
                r.Skip();
            }
        }

        return nd;
    }

    private static TransactionDetails ReadTransactionDetails(XmlReader r)
    {
        var t = new TransactionDetails();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Refs":
                    t.Refs ??= new References();
                    ReadReferences(r, t.Refs);
                    break;
                case "Amt":
                    t.Amt = Xsd.ReadAmountInto(r, t.Amt);
                    break;
                case "CdtDbtInd":
                    t.CdtDbtInd = XmlInput.ReadText(r);
                    break;
                case "RmtInf":
                    t.RmtInf = Xsd.ReadRemittance(r, t.RmtInf);
                    break;
                case "AddtlTxInf":
                    t.AddtlTxInf = XmlInput.ReadText(r);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }

        return t;
    }

    private static void ReadReferences(XmlReader r, References refs)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "MsgId": refs.MsgId = XmlInput.ReadText(r); break;
                case "AcctSvcrRef": refs.AcctSvcrRef = XmlInput.ReadText(r); break;
                case "InstrId": refs.InstrId = XmlInput.ReadText(r); break;
                case "EndToEndId": refs.EndToEndId = XmlInput.ReadText(r); break;
                case "TxId": refs.TxId = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }
}

/// <summary>The camt.054 message root, <c>BkToCstmrDbtCdtNtfctn</c>.</summary>
public sealed record Notification
{
    /// <summary>The group header.</summary>
    public MessageHeader GrpHdr { get; set; } = new();

    /// <summary>The notifications, one per account.</summary>
    public List<AccountNotification> Ntfctn { get; set; } = new();
}

/// <summary>One <c>Ntfctn</c>: the entries of one account.</summary>
public sealed record AccountNotification
{
    /// <summary>The notification identification.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The creation date and time.</summary>
    public string CreDtTm { get; set; } = string.Empty;

    /// <summary>The account.</summary>
    public Account Acct { get; set; } = new();

    /// <summary>The entries.</summary>
    public List<Entry> Ntry { get; set; } = new();
}

/// <summary>One <c>Ntry</c>: an entry booked to the account.</summary>
public sealed record Entry
{
    /// <summary>The entry reference.</summary>
    public string NtryRef { get; set; } = string.Empty;

    /// <summary>The amount.</summary>
    public CurrencyAmount Amt { get; set; } = new();

    /// <summary>The credit/debit indicator, <see cref="Camt054.Credit"/> or <see cref="Camt054.Debit"/>.</summary>
    public string CdtDbtInd { get; set; } = string.Empty;

    /// <summary>The reversal indicator.</summary>
    public bool? RvslInd { get; set; }

    /// <summary>The status, e.g. <see cref="Camt054.StatusBooked"/>.</summary>
    public CodeOrProprietary Sts { get; set; } = new();

    /// <summary>The booking date.</summary>
    public DateAndDateTime? BookgDt { get; set; }

    /// <summary>The value date.</summary>
    public DateAndDateTime? ValDt { get; set; }

    /// <summary>The account servicer reference.</summary>
    public string AcctSvcrRef { get; set; } = string.Empty;

    /// <summary>The bank transaction code.</summary>
    public BankTransactionCode BkTxCd { get; set; } = new();

    /// <summary>The entry details.</summary>
    public List<EntryDetails> NtryDtls { get; set; } = new();

    /// <summary>Additional entry information.</summary>
    public string AddtlNtryInf { get; set; } = string.Empty;

    /// <summary>The EndToEndId of every transaction detail of the entry, in order, skipping details without one.</summary>
    public IReadOnlyList<string> EndToEndIds =>
        NtryDtls.SelectMany(d => d.TxDtls)
            .Where(t => !string.IsNullOrEmpty(t.Refs?.EndToEndId))
            .Select(t => t.Refs!.EndToEndId)
            .ToArray();

    /// <summary>The entry amount, negative for debits.</summary>
    public BrlAmount Signed => CdtDbtInd == Camt054.Debit ? new BrlAmount(-Amt.Value.Cents) : Amt.Value;
}

/// <summary><c>BkTxCd</c>: a structured domain code, a proprietary code, or both.</summary>
public sealed record BankTransactionCode
{
    /// <summary>The structured code.</summary>
    public Domain? Domn { get; set; }

    /// <summary>The proprietary code.</summary>
    public ProprietaryBankTransactionCode? Prtry { get; set; }
}

/// <summary><c>BkTxCd/Domn</c>.</summary>
public sealed record Domain
{
    /// <summary>The domain code, e.g. <c>PMNT</c>.</summary>
    public string Cd { get; set; } = string.Empty;

    /// <summary>The family.</summary>
    public Family Fmly { get; set; } = new();
}

/// <summary><c>BkTxCd/Domn/Fmly</c>.</summary>
public sealed record Family
{
    /// <summary>The family code, e.g. <c>RCDT</c>.</summary>
    public string Cd { get; set; } = string.Empty;

    /// <summary>The sub-family code, e.g. <c>DMCT</c>.</summary>
    public string SubFmlyCd { get; set; } = string.Empty;
}

/// <summary><c>BkTxCd/Prtry</c>.</summary>
public sealed record ProprietaryBankTransactionCode
{
    /// <summary>The code.</summary>
    public string Cd { get; set; } = string.Empty;

    /// <summary>The issuer.</summary>
    public string Issr { get; set; } = string.Empty;
}

/// <summary><c>NtryDtls</c>.</summary>
public sealed record EntryDetails
{
    /// <summary>The transaction details.</summary>
    public List<TransactionDetails> TxDtls { get; set; } = new();
}

/// <summary><c>NtryDtls/TxDtls</c>.</summary>
public sealed record TransactionDetails
{
    /// <summary>The references.</summary>
    public References? Refs { get; set; }

    /// <summary>The amount of the transaction.</summary>
    public CurrencyAmount? Amt { get; set; }

    /// <summary>The credit/debit indicator.</summary>
    public string CdtDbtInd { get; set; } = string.Empty;

    /// <summary>The remittance information.</summary>
    public RemittanceInformation? RmtInf { get; set; }

    /// <summary>Additional transaction information.</summary>
    public string AddtlTxInf { get; set; } = string.Empty;
}

/// <summary><c>TxDtls/Refs</c>.</summary>
public sealed record References
{
    /// <summary>The message identification.</summary>
    public string MsgId { get; set; } = string.Empty;

    /// <summary>The account servicer reference.</summary>
    public string AcctSvcrRef { get; set; } = string.Empty;

    /// <summary>The instruction identification.</summary>
    public string InstrId { get; set; } = string.Empty;

    /// <summary>The Pix EndToEndId.</summary>
    public string EndToEndId { get; set; } = string.Empty;

    /// <summary>The transaction identification.</summary>
    public string TxId { get; set; } = string.Empty;
}
