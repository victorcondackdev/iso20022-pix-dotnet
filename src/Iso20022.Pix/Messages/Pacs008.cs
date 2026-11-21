using System.Text;
using System.Xml;
using Iso20022.Pix.Components;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>Constants of the pacs.008 message (FIToFICustomerCreditTransfer).</summary>
public static class Pacs008
{
    /// <summary>The namespace used when serializing a document whose namespace is empty.</summary>
    public const string DefaultNamespace = MessageIds.NamespacePrefix + "pacs.008.001.08";

    /// <summary><c>GrpHdr/SttlmInf/SttlmMtd</c>: settlement through the clearing system.</summary>
    public const string SettlementClearing = "CLRG";

    /// <summary><c>ChrgBr</c>: charges per service level.</summary>
    public const string ChargesSlev = "SLEV";

    /// <summary>Initiation (<c>PmtTpInf/LclInstrm/Prtry</c>): account details typed by the payer.</summary>
    public const string InitiationManual = "MANU";

    /// <summary>Initiation: Pix key resolved through the DICT.</summary>
    public const string InitiationDict = "DICT";

    /// <summary>Initiation: static QR code.</summary>
    public const string InitiationStaticQr = "QRES";

    /// <summary>Initiation: dynamic QR code.</summary>
    public const string InitiationDynamicQr = "QRDN";

    /// <summary>Initiation: payment initiation service provider.</summary>
    public const string InitiationPaymentInitiator = "INIC";
}

/// <summary>
/// A pacs.008 message (FIToFICustomerCreditTransfer), which carries a Pix
/// payment between participants through the SPI.
/// </summary>
/// <remarks>
/// The model covers the elements Pix uses in the element order of
/// pacs.008.001.08. <see cref="Parse(string)"/> accepts any
/// pacs.008.001.xx namespace; elements that are not modelled are ignored.
/// The BCB SPI catalog further restricts the message; <see cref="Validate"/>
/// enforces the ISO 20022 rules and the stable Pix rules and is not a
/// substitute for validating against the current catalog.
/// </remarks>
public sealed record Pacs008Document
{
    private const string Root = "/Document/FIToFICstmrCdtTrf";

    /// <summary>The Document namespace, as parsed; empty means <see cref="Pacs008.DefaultNamespace"/>.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The message root.</summary>
    public FIToFICustomerCreditTransfer FIToFICstmrCdtTrf { get; set; } = new();

    /// <summary>The <c>CdtTrfTxInf</c> entries.</summary>
    public List<CreditTransferTransaction> Transactions => FIToFICstmrCdtTrf.CdtTrfTxInf;

    /// <summary>The message version from the namespace, e.g. <c>08</c>, or an empty string.</summary>
    public string Version => MessageType.TryParse(Namespace, out MessageType t) ? t.Version : string.Empty;

    /// <summary>
    /// The full message identifier, e.g. <c>pacs.008.001.08</c>, using the
    /// default namespace when none is set. A namespace that is not a message
    /// identifier yields <c>...</c>, as in the Go implementation.
    /// </summary>
    public string MessageName =>
        MessageType.TryParse(string.IsNullOrEmpty(Namespace) ? Pacs008.DefaultNamespace : Namespace, out MessageType t)
            ? t.ToString()
            : "...";

    /// <summary>
    /// Parses a pacs.008 message, either a bare Document or a business message
    /// wrapping an AppHdr and a Document. Checks well-formedness, the namespace
    /// (pacs.008, any version) and the lexical form of amounts; call
    /// <see cref="Validate"/> for the business rules.
    /// </summary>
    /// <param name="xml">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.008 message.</exception>
    public static Pacs008Document Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return XmlInput.ReadDocument<Pacs008Document>(r, MessageIds.Pacs008, Read);
    }

    /// <summary>Parses a pacs.008 message from a stream; see <see cref="Parse(string)"/>.</summary>
    /// <param name="stream">The message. It is not closed.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.008 message.</exception>
    public static Pacs008Document Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return XmlInput.ReadDocument<Pacs008Document>(r, MessageIds.Pacs008, Read);
    }

    /// <summary>Parses a pacs.008 message from UTF-8 bytes; see <see cref="Parse(string)"/>.</summary>
    /// <param name="utf8">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.008 message.</exception>
    public static Pacs008Document Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var stream = new MemoryStream(utf8, writable: false);
        return Parse(stream);
    }

    /// <summary>
    /// Serializes the document as indented XML with an XML declaration,
    /// byte-identical to the Go implementation. Does not validate.
    /// </summary>
    /// <returns>The XML.</returns>
    /// <exception cref="InvalidAmountException">An amount is negative or too large to serialize.</exception>
    public string ToXml()
    {
        var w = new XmlOutput();
        w.Start("Document", string.IsNullOrEmpty(Namespace) ? Pacs008.DefaultNamespace : Namespace);
        FIToFICstmrCdtTrf.Write(w);
        w.End("Document");
        return w.Finish();
    }

    /// <summary>Serializes the document as UTF-8 bytes; see <see cref="ToXml"/>.</summary>
    /// <returns>The XML bytes, without a byte order mark.</returns>
    public byte[] ToXmlBytes() => Encoding.UTF8.GetBytes(ToXml());

    /// <summary>
    /// Checks the document and returns every violation, in document order:
    /// the namespace (when set), the group header (MsgId, CreDtTm, NbOfTxs
    /// matching the transactions, SttlmMtd CLRG) and, for each transaction, a
    /// valid EndToEndId, a positive BRL amount, AccptncDtTm, ChrgBr SLEV,
    /// debtor and creditor with a valid CPF or CNPJ, accounts with number and
    /// type, agents identified by ISPB, a valid Pix key when present and
    /// well-formed optional elements.
    /// </summary>
    /// <returns>The violations; empty when the document is valid.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        var c = new Checker();
        if (!string.IsNullOrEmpty(Namespace)
            && (!MessageType.TryParse(Namespace, out MessageType id) || id.Id != MessageIds.Pacs008))
        {
            c.Add("/Document/@xmlns", "must be a pacs.008 namespace, got " + GoFormat.Quote(Namespace));
        }

        FIToFICustomerCreditTransfer m = FIToFICstmrCdtTrf;
        const string g = Root + "/GrpHdr";
        c.Max35(g + "/MsgId", m.GrpHdr.MsgId, true);
        c.DateTime(g + "/CreDtTm", m.GrpHdr.CreDtTm, true);
        c.NbOfTxs(g + "/NbOfTxs", m.GrpHdr.NbOfTxs, m.CdtTrfTxInf.Count);
        c.OneOf(g + "/SttlmInf/SttlmMtd", m.GrpHdr.SttlmInf.SttlmMtd, true, Pacs008.SettlementClearing);
        if (m.CdtTrfTxInf.Count == 0)
        {
            c.Add(Root + "/CdtTrfTxInf", "at least one transaction is required");
        }

        for (int i = 0; i < m.CdtTrfTxInf.Count; i++)
        {
            m.CdtTrfTxInf[i].Validate(c, Checker.Idx(Root + "/CdtTrfTxInf", i));
        }

        return c.Result;
    }

    private static Pacs008Document Read(XmlReader r, string ns)
    {
        var d = new Pacs008Document { Namespace = ns };
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "FIToFICstmrCdtTrf")
            {
                d.FIToFICstmrCdtTrf.ReadInto(r);
            }
            else
            {
                r.Skip();
            }
        }

        return d;
    }
}

/// <summary>The pacs.008 message root, <c>FIToFICstmrCdtTrf</c>.</summary>
public sealed record FIToFICustomerCreditTransfer
{
    /// <summary>The group header.</summary>
    public GroupHeader GrpHdr { get; set; } = new();

    /// <summary>The credit transfers, one per Pix payment.</summary>
    public List<CreditTransferTransaction> CdtTrfTxInf { get; set; } = new();

    internal void Write(XmlOutput w)
    {
        w.Start("FIToFICstmrCdtTrf");
        GrpHdr.Write(w);
        foreach (CreditTransferTransaction tx in CdtTrfTxInf)
        {
            tx.Write(w);
        }

        w.End("FIToFICstmrCdtTrf");
    }

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "GrpHdr":
                    GrpHdr.ReadInto(r);
                    break;
                case "CdtTrfTxInf":
                    var tx = new CreditTransferTransaction();
                    tx.ReadInto(r);
                    CdtTrfTxInf.Add(tx);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }
}

/// <summary>The group header (<c>GrpHdr</c>) of pacs.008 and pacs.004.</summary>
public sealed record GroupHeader
{
    /// <summary>The message identification (Max35Text).</summary>
    public string MsgId { get; set; } = string.Empty;

    /// <summary>The creation date and time (ISODateTime).</summary>
    public string CreDtTm { get; set; } = string.Empty;

    /// <summary>The number of transactions (Max15NumericText).</summary>
    public string NbOfTxs { get; set; } = string.Empty;

    /// <summary>The settlement instruction.</summary>
    public SettlementInstruction SttlmInf { get; set; } = new();

    internal void Write(XmlOutput w)
    {
        w.Start("GrpHdr");
        w.Element("MsgId", MsgId);
        w.Element("CreDtTm", CreDtTm);
        w.Element("NbOfTxs", NbOfTxs);
        w.Start("SttlmInf");
        w.Element("SttlmMtd", SttlmInf.SttlmMtd);
        w.End("SttlmInf");
        w.End("GrpHdr");
    }

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "MsgId": MsgId = XmlInput.ReadText(r); break;
                case "CreDtTm": CreDtTm = XmlInput.ReadText(r); break;
                case "NbOfTxs": NbOfTxs = XmlInput.ReadText(r); break;
                case "SttlmInf": SttlmInf.ReadInto(r); break;
                default: r.Skip(); break;
            }
        }
    }
}

/// <summary>The settlement instruction, <c>GrpHdr/SttlmInf</c>.</summary>
public sealed record SettlementInstruction
{
    /// <summary>The settlement method; Pix uses <see cref="Pacs008.SettlementClearing"/>.</summary>
    public string SttlmMtd { get; set; } = string.Empty;

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "SttlmMtd")
            {
                SttlmMtd = XmlInput.ReadText(r);
            }
            else
            {
                r.Skip();
            }
        }
    }
}

/// <summary>One <c>CdtTrfTxInf</c>: a single Pix payment.</summary>
public sealed record CreditTransferTransaction
{
    /// <summary>The payment identification.</summary>
    public PaymentIdentification PmtId { get; set; } = new();

    /// <summary>The payment type information.</summary>
    public PaymentTypeInformation? PmtTpInf { get; set; }

    /// <summary>The interbank settlement amount.</summary>
    public CurrencyAmount IntrBkSttlmAmt { get; set; } = new();

    /// <summary>When the payer's participant accepted the order (ISODateTime).</summary>
    public string AccptncDtTm { get; set; } = string.Empty;

    /// <summary>The charge bearer; Pix uses <see cref="Pacs008.ChargesSlev"/>.</summary>
    public string ChrgBr { get; set; } = string.Empty;

    /// <summary>The debtor (payer).</summary>
    public Party Dbtr { get; set; } = new();

    /// <summary>The debtor account.</summary>
    public Account? DbtrAcct { get; set; }

    /// <summary>The debtor agent (payer's participant).</summary>
    public Agent DbtrAgt { get; set; } = new();

    /// <summary>The creditor agent (payee's participant).</summary>
    public Agent CdtrAgt { get; set; } = new();

    /// <summary>The creditor (payee).</summary>
    public Party Cdtr { get; set; } = new();

    /// <summary>The creditor account, with the Pix key in <c>Prxy/Id</c>.</summary>
    public Account? CdtrAcct { get; set; }

    /// <summary>The purpose.</summary>
    public CodeOrProprietary? Purp { get; set; }

    /// <summary>The remittance information.</summary>
    public RemittanceInformation? RmtInf { get; set; }

    /// <summary>The interbank settlement amount.</summary>
    public BrlAmount Amount => IntrBkSttlmAmt.Value;

    /// <summary><c>PmtTpInf/LclInstrm/Prtry</c> (or <c>Cd</c>), or an empty string.</summary>
    public string Initiation => PmtTpInf?.LclInstrm?.Value ?? string.Empty;

    /// <summary>The unstructured remittance lines joined by newlines.</summary>
    public string RemittanceInfo => RmtInf is null ? string.Empty : string.Join("\n", RmtInf.Ustrd);

    internal void Write(XmlOutput w)
    {
        w.Start("CdtTrfTxInf");
        w.Start("PmtId");
        w.Element("EndToEndId", PmtId.EndToEndId);
        w.Optional("TxId", PmtId.TxId);
        w.End("PmtId");
        if (PmtTpInf is not null)
        {
            w.Start("PmtTpInf");
            w.Optional("InstrPrty", PmtTpInf.InstrPrty);
            Xsd.WriteChoice(w, "LclInstrm", PmtTpInf.LclInstrm);
            Xsd.WriteChoice(w, "CtgyPurp", PmtTpInf.CtgyPurp);
            w.End("PmtTpInf");
        }

        w.Amount("IntrBkSttlmAmt", IntrBkSttlmAmt);
        w.Optional("AccptncDtTm", AccptncDtTm);
        w.Element("ChrgBr", ChrgBr);
        Xsd.WriteParty(w, "Dbtr", Dbtr);
        if (DbtrAcct is not null)
        {
            Xsd.WriteAccount(w, "DbtrAcct", DbtrAcct);
        }

        Xsd.WriteAgent(w, "DbtrAgt", DbtrAgt);
        Xsd.WriteAgent(w, "CdtrAgt", CdtrAgt);
        Xsd.WriteParty(w, "Cdtr", Cdtr);
        if (CdtrAcct is not null)
        {
            Xsd.WriteAccount(w, "CdtrAcct", CdtrAcct);
        }

        Xsd.WriteChoice(w, "Purp", Purp);
        Xsd.WriteRemittance(w, "RmtInf", RmtInf);
        w.End("CdtTrfTxInf");
    }

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "PmtId":
                    PmtId.ReadInto(r);
                    break;
                case "PmtTpInf":
                    PmtTpInf ??= new PaymentTypeInformation();
                    PmtTpInf.ReadInto(r);
                    break;
                case "IntrBkSttlmAmt":
                    XmlInput.ReadAmount(r, IntrBkSttlmAmt);
                    break;
                case "AccptncDtTm":
                    AccptncDtTm = XmlInput.ReadText(r);
                    break;
                case "ChrgBr":
                    ChrgBr = XmlInput.ReadText(r);
                    break;
                case "Dbtr":
                    Xsd.ReadParty(r, Dbtr);
                    break;
                case "DbtrAcct":
                    DbtrAcct ??= new Account();
                    Xsd.ReadAccount(r, DbtrAcct);
                    break;
                case "DbtrAgt":
                    Xsd.ReadAgent(r, DbtrAgt);
                    break;
                case "CdtrAgt":
                    Xsd.ReadAgent(r, CdtrAgt);
                    break;
                case "Cdtr":
                    Xsd.ReadParty(r, Cdtr);
                    break;
                case "CdtrAcct":
                    CdtrAcct ??= new Account();
                    Xsd.ReadAccount(r, CdtrAcct);
                    break;
                case "Purp":
                    Purp = Xsd.ReadChoiceInto(r, Purp);
                    break;
                case "RmtInf":
                    RmtInf = Xsd.ReadRemittance(r, RmtInf);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    internal void Validate(Checker c, string p)
    {
        c.EndToEndId(p + "/PmtId/EndToEndId", PmtId.EndToEndId);
        c.Max35(p + "/PmtId/TxId", PmtId.TxId, false);
        if (PmtTpInf is not null)
        {
            c.Code(p + "/PmtTpInf/InstrPrty", PmtTpInf.InstrPrty, false);
            c.Choice(p + "/PmtTpInf/LclInstrm", PmtTpInf.LclInstrm, 35);
            c.Choice(p + "/PmtTpInf/CtgyPurp", PmtTpInf.CtgyPurp, 35);
        }

        c.Brl(p + "/IntrBkSttlmAmt", IntrBkSttlmAmt, true);
        c.DateTime(p + "/AccptncDtTm", AccptncDtTm, true);
        c.OneOf(p + "/ChrgBr", ChrgBr, true, Pacs008.ChargesSlev);
        c.Party(p + "/Dbtr", Dbtr, true, true);
        c.Account(p + "/DbtrAcct", DbtrAcct, true);
        c.Agent(p + "/DbtrAgt", DbtrAgt);
        c.Agent(p + "/CdtrAgt", CdtrAgt);
        c.Party(p + "/Cdtr", Cdtr, false, true);
        c.Account(p + "/CdtrAcct", CdtrAcct, true);
        c.Choice(p + "/Purp", Purp, 35);
        c.Remittance(p + "/RmtInf", RmtInf);
    }
}

/// <summary>The payment identification, <c>PmtId</c>.</summary>
public sealed record PaymentIdentification
{
    /// <summary>The Pix EndToEndId.</summary>
    public string EndToEndId { get; set; } = string.Empty;

    /// <summary>The transaction identifier of a QR code charge (txid), when there is one.</summary>
    public string TxId { get; set; } = string.Empty;

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "EndToEndId": EndToEndId = XmlInput.ReadText(r); break;
                case "TxId": TxId = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }
}

/// <summary>The payment type information, <c>PmtTpInf</c>.</summary>
public sealed record PaymentTypeInformation
{
    /// <summary>The instruction priority, e.g. <c>HIGH</c>.</summary>
    public string InstrPrty { get; set; } = string.Empty;

    /// <summary>The local instrument: in Pix, how the payment was initiated.</summary>
    public CodeOrProprietary? LclInstrm { get; set; }

    /// <summary>The category purpose.</summary>
    public CodeOrProprietary? CtgyPurp { get; set; }

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "InstrPrty": InstrPrty = XmlInput.ReadText(r); break;
                case "LclInstrm": LclInstrm = Xsd.ReadChoiceInto(r, LclInstrm); break;
                case "CtgyPurp": CtgyPurp = Xsd.ReadChoiceInto(r, CtgyPurp); break;
                default: r.Skip(); break;
            }
        }
    }
}
