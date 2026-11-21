using System.Text;
using System.Xml;
using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>Options of <see cref="Pacs004.FromPacs008"/>.</summary>
public sealed record Pacs004Options
{
    /// <summary>The creation time used for <c>GrpHdr/CreDtTm</c> and the generated RtrId; defaults to the current time.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary><c>GrpHdr/MsgId</c>; generated when null or empty.</summary>
    public string? MsgId { get; init; }

    /// <summary><c>TxInf/RtrId</c>; generated when null or empty.</summary>
    public string? ReturnId { get; init; }

    /// <summary>Selects the original transaction by EndToEndId; by default the first is used.</summary>
    public string? EndToEndId { get; init; }

    /// <summary>The source of random bytes for generated identifiers.</summary>
    public Stream? Random { get; init; }

    /// <summary><c>RtrRsnInf/AddtlInf</c> lines (Max105Text).</summary>
    public IReadOnlyList<string> AdditionalInfo { get; init; } = Array.Empty<string>();

    /// <summary>The total already returned for the original payment, so that partial returns never exceed it.</summary>
    public BrlAmount PriorReturns { get; init; }

    /// <summary>Lets <see cref="Pacs004.FromPacs008"/> use a code that is not in <see cref="Pacs004.Reasons"/>.</summary>
    public bool AllowUnknownReason { get; init; }
}

/// <summary>
/// Constants, reason codes and the builder of the pacs.004 message
/// (PaymentReturn), used for Pix returns (devoluções), including returns
/// requested through the Mecanismo Especial de Devolução (MED).
/// </summary>
public static class Pacs004
{
    /// <summary>The namespace used when serializing a document whose namespace is empty.</summary>
    public const string DefaultNamespace = MessageIds.NamespacePrefix + "pacs.004.001.09";

    /// <summary>Returned because of a participant error.</summary>
    public const string ReasonBankError = "BE08";

    /// <summary>Returned because of fraud (e.g. MED).</summary>
    public const string ReasonFraud = "FR01";

    /// <summary>Refund requested by the end customer.</summary>
    public const string ReasonCustomerRefund = "MD06";

    /// <summary>Specific service offered by the creditor agent.</summary>
    public const string ReasonCreditorService = "SL02";

    /// <summary>
    /// The return reason codes (ExternalReturnReason1Code) this library is
    /// confident about. <see cref="FromPacs008"/> requires one of them unless
    /// <see cref="Pacs004Options.AllowUnknownReason"/> is set; parsing and
    /// validation accept any code.
    /// </summary>
    public static readonly CodeSet Reasons = new(new[]
    {
        new CodeInfo(ReasonBankError, "BankError", "Returned as a result of a bank error.", "Devolução por erro operacional do participante."),
        new CodeInfo(ReasonFraud, "Fraud", "Returned as a result of fraud.", "Devolução por fundada suspeita de fraude."),
        new CodeInfo(ReasonCustomerRefund, "RefundRequestByEndCustomer", "Return of funds requested by the end customer.", "Devolução solicitada pelo cliente usuário recebedor."),
        new CodeInfo(ReasonCreditorService, "SpecificServiceOfferedByCreditorAgent", "Returned due to a specific service offered by the creditor agent.", "Devolução por serviço específico do participante recebedor."),
    });

    /// <summary>
    /// Builds a return of <paramref name="amount"/> for a transaction of
    /// <paramref name="original"/>. The return is issued by the creditor's
    /// participant: its ISPB (<c>CdtrAgt</c>) is used for the generated MsgId
    /// and RtrId. Partial returns are allowed; <paramref name="amount"/> plus
    /// <see cref="Pacs004Options.PriorReturns"/> must not exceed the original
    /// <c>IntrBkSttlmAmt</c>.
    /// </summary>
    /// <param name="original">The payment being returned.</param>
    /// <param name="reason">The return reason code, e.g. <see cref="ReasonCustomerRefund"/>.</param>
    /// <param name="amount">The amount to return.</param>
    /// <param name="options">The options.</param>
    /// <returns>The validated return.</returns>
    /// <exception cref="MessageBuildException">The transaction is not found, the reason is unknown or the amount is not available for return.</exception>
    /// <exception cref="InvalidIdentifierException">Identifiers cannot be generated for the creditor ISPB.</exception>
    /// <exception cref="MessageValidationException">The return is invalid.</exception>
    public static Pacs004Document FromPacs008(Pacs008Document? original, string reason, BrlAmount amount, Pacs004Options? options = null)
    {
        Pacs004Options o = options ?? new Pacs004Options();
        reason ??= string.Empty;
        if (original is null)
        {
            throw NotFound(string.Empty);
        }

        CreditTransferTransaction tx = Pick(original, o.EndToEndId ?? string.Empty);
        if (!Reasons.Contains(reason) && !o.AllowUnknownReason)
        {
            throw new MessageBuildException(MessageBuildErrorKind.UnknownReason, "pacs004: unknown return reason code: " + GoFormat.Quote(reason));
        }

        if (!amount.IsPositive)
        {
            throw new MessageBuildException(MessageBuildErrorKind.InvalidAmount, "pacs004: return amount must be positive, got " + amount.ToString());
        }

        if (o.PriorReturns.Cents < 0)
        {
            throw new MessageBuildException(MessageBuildErrorKind.InvalidAmount, "pacs004: prior returns must not be negative, got " + o.PriorReturns.ToString());
        }

        const string exceeded = "pacs004: return amount exceeds the amount available for return: ";
        BrlAmount paid = tx.Amount;
        if (!paid.TrySubtract(o.PriorReturns, out BrlAmount available) || available.Cents < 0)
        {
            throw new MessageBuildException(
                MessageBuildErrorKind.AmountExceeded,
                exceeded + "prior returns " + o.PriorReturns.ToString() + " exceed the original " + paid.ToString());
        }

        if (amount > available)
        {
            throw new MessageBuildException(
                MessageBuildErrorKind.AmountExceeded,
                exceeded + amount.ToString() + " requested, " + available.ToString() + " available of the original " + paid.ToString());
        }

        DateTimeOffset at = o.At ?? DateTimeOffset.UtcNow;
        string ispb = tx.CdtrAgt.Ispb;
        string msgId = string.IsNullOrEmpty(o.MsgId) ? PixIdentifiers.NewMsgId(ispb, o.Random) : o.MsgId;
        string returnId = string.IsNullOrEmpty(o.ReturnId) ? PixIdentifiers.NewReturnId(ispb, at, o.Random) : o.ReturnId;

        GroupHeader h = original.FIToFICstmrCdtTrf.GrpHdr;
        var d = new Pacs004Document { Namespace = DefaultNamespace };
        d.PmtRtr.GrpHdr = new GroupHeader
        {
            MsgId = msgId,
            CreDtTm = IsoDateTime.Format(at),
            NbOfTxs = "1",
            SttlmInf = new SettlementInstruction { SttlmMtd = Pacs008.SettlementClearing },
        };

        var reasonInfo = new ReturnReasonInfo { Rsn = CodeOrProprietary.Code(reason) };
        reasonInfo.AddtlInf.AddRange(o.AdditionalInfo ?? Array.Empty<string>());
        d.PmtRtr.TxInf.Add(new ReturnTransaction
        {
            RtrId = returnId,
            OrgnlGrpInf = new OriginalGroupInformation
            {
                OrgnlMsgId = h.MsgId,
                OrgnlMsgNmId = original.MessageName,
                OrgnlCreDtTm = h.CreDtTm,
            },
            OrgnlEndToEndId = tx.PmtId.EndToEndId,
            RtrdIntrBkSttlmAmt = CurrencyAmount.InBrl(amount),
            ChrgBr = Pacs008.ChargesSlev,
            RtrRsnInf = { reasonInfo },
        });

        IReadOnlyList<Violation> violations = d.Validate();
        if (violations.Count > 0)
        {
            throw new MessageValidationException(violations, d);
        }

        return d;
    }

    private static CreditTransferTransaction Pick(Pacs008Document d, string e2e)
    {
        List<CreditTransferTransaction> txs = d.Transactions;
        if (txs.Count == 0)
        {
            throw NotFound(string.Empty);
        }

        if (e2e.Length == 0)
        {
            return txs[0];
        }

        foreach (CreditTransferTransaction tx in txs)
        {
            if (tx.PmtId.EndToEndId == e2e)
            {
                return tx;
            }
        }

        throw NotFound(e2e);
    }

    private static MessageBuildException NotFound(string e2e) => new(
        MessageBuildErrorKind.TransactionNotFound,
        e2e.Length == 0
            ? "pacs004: original transaction not found"
            : "pacs004: original transaction not found: EndToEndId " + GoFormat.Quote(e2e));
}

/// <summary>A pacs.004 message (PaymentReturn), in the element order of pacs.004.001.09.</summary>
public sealed record Pacs004Document
{
    private const string Root = "/Document/PmtRtr";

    /// <summary>The Document namespace, as parsed; empty means <see cref="Pacs004.DefaultNamespace"/>.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The message root.</summary>
    public PaymentReturn PmtRtr { get; set; } = new();

    /// <summary>The <c>TxInf</c> entries.</summary>
    public List<ReturnTransaction> Transactions => PmtRtr.TxInf;

    /// <summary>The message version from the namespace, e.g. <c>09</c>, or an empty string.</summary>
    public string Version => MessageType.TryParse(Namespace, out MessageType t) ? t.Version : string.Empty;

    /// <summary>Parses a pacs.004 message (bare Document or business message).</summary>
    /// <param name="xml">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.004 message.</exception>
    public static Pacs004Document Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return XmlInput.ReadDocument<Pacs004Document>(r, MessageIds.Pacs004, Read);
    }

    /// <summary>Parses a pacs.004 message from a stream, which is not closed.</summary>
    /// <param name="stream">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.004 message.</exception>
    public static Pacs004Document Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return XmlInput.ReadDocument<Pacs004Document>(r, MessageIds.Pacs004, Read);
    }

    /// <summary>Parses a pacs.004 message from UTF-8 bytes.</summary>
    /// <param name="utf8">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.004 message.</exception>
    public static Pacs004Document Parse(byte[] utf8)
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
        w.Start("Document", string.IsNullOrEmpty(Namespace) ? Pacs004.DefaultNamespace : Namespace);
        w.Start("PmtRtr");
        GroupHeader g = PmtRtr.GrpHdr;
        g.Write(w);
        foreach (ReturnTransaction t in PmtRtr.TxInf)
        {
            w.Start("TxInf");
            w.Element("RtrId", t.RtrId);
            if (t.OrgnlGrpInf is not null)
            {
                w.Start("OrgnlGrpInf");
                w.Element("OrgnlMsgId", t.OrgnlGrpInf.OrgnlMsgId);
                w.Element("OrgnlMsgNmId", t.OrgnlGrpInf.OrgnlMsgNmId);
                w.Optional("OrgnlCreDtTm", t.OrgnlGrpInf.OrgnlCreDtTm);
                w.End("OrgnlGrpInf");
            }

            w.Element("OrgnlEndToEndId", t.OrgnlEndToEndId);
            w.Optional("OrgnlTxId", t.OrgnlTxId);
            if (t.OrgnlIntrBkSttlmAmt is not null)
            {
                w.Amount("OrgnlIntrBkSttlmAmt", t.OrgnlIntrBkSttlmAmt);
            }

            w.Amount("RtrdIntrBkSttlmAmt", t.RtrdIntrBkSttlmAmt);
            w.Optional("ChrgBr", t.ChrgBr);
            foreach (ReturnReasonInfo s in t.RtrRsnInf)
            {
                w.Start("RtrRsnInf");
                Xsd.WriteChoice(w, "Rsn", s.Rsn);
                w.OptionalElements("AddtlInf", s.AddtlInf);
                w.End("RtrRsnInf");
            }

            w.End("TxInf");
        }

        w.End("PmtRtr");
        w.End("Document");
        return w.Finish();
    }

    /// <summary>Serializes the document as UTF-8 bytes; see <see cref="ToXml"/>.</summary>
    /// <returns>The XML bytes, without a byte order mark.</returns>
    public byte[] ToXmlBytes() => Encoding.UTF8.GetBytes(ToXml());

    /// <summary>
    /// Checks the return and returns every violation: the group header as in
    /// pacs.008 and, for each <c>TxInf</c>, a valid RtrId and OrgnlEndToEndId,
    /// a positive BRL returned amount not above <c>OrgnlIntrBkSttlmAmt</c> when
    /// that is present, ChrgBr SLEV when present and at least one reason code.
    /// Reason codes are not restricted to <see cref="Pacs004.Reasons"/>.
    /// </summary>
    /// <returns>The violations; empty when the return is valid.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        var c = new Checker();
        if (!string.IsNullOrEmpty(Namespace)
            && (!MessageType.TryParse(Namespace, out MessageType id) || id.Id != MessageIds.Pacs004))
        {
            c.Add("/Document/@xmlns", "must be a pacs.004 namespace, got " + GoFormat.Quote(Namespace));
        }

        PaymentReturn r = PmtRtr;
        const string g = Root + "/GrpHdr";
        c.Max35(g + "/MsgId", r.GrpHdr.MsgId, true);
        c.DateTime(g + "/CreDtTm", r.GrpHdr.CreDtTm, true);
        c.NbOfTxs(g + "/NbOfTxs", r.GrpHdr.NbOfTxs, r.TxInf.Count);
        c.OneOf(g + "/SttlmInf/SttlmMtd", r.GrpHdr.SttlmInf.SttlmMtd, true, Pacs008.SettlementClearing);
        if (r.TxInf.Count == 0)
        {
            c.Add(Root + "/TxInf", "at least one transaction is required");
        }

        for (int i = 0; i < r.TxInf.Count; i++)
        {
            ReturnTransaction t = r.TxInf[i];
            string p = Checker.Idx(Root + "/TxInf", i);
            c.ReturnId(p + "/RtrId", t.RtrId);
            if (t.OrgnlGrpInf is not null)
            {
                c.Max35(p + "/OrgnlGrpInf/OrgnlMsgId", t.OrgnlGrpInf.OrgnlMsgId, true);
                c.Max35(p + "/OrgnlGrpInf/OrgnlMsgNmId", t.OrgnlGrpInf.OrgnlMsgNmId, true);
                c.DateTime(p + "/OrgnlGrpInf/OrgnlCreDtTm", t.OrgnlGrpInf.OrgnlCreDtTm, false);
            }

            c.EndToEndId(p + "/OrgnlEndToEndId", t.OrgnlEndToEndId);
            c.Max35(p + "/OrgnlTxId", t.OrgnlTxId, false);
            if (t.OrgnlIntrBkSttlmAmt is not null)
            {
                c.Brl(p + "/OrgnlIntrBkSttlmAmt", t.OrgnlIntrBkSttlmAmt, true);
                if (t.RtrdIntrBkSttlmAmt.Value > t.OrgnlIntrBkSttlmAmt.Value)
                {
                    c.Add(
                        p + "/RtrdIntrBkSttlmAmt",
                        t.RtrdIntrBkSttlmAmt.Value.ToString() + " exceeds OrgnlIntrBkSttlmAmt " + t.OrgnlIntrBkSttlmAmt.Value.ToString());
                }
            }

            c.Brl(p + "/RtrdIntrBkSttlmAmt", t.RtrdIntrBkSttlmAmt, true);
            c.OneOf(p + "/ChrgBr", t.ChrgBr, false, Pacs008.ChargesSlev);
            if (t.Reason.Length == 0)
            {
                c.Add(p + "/RtrRsnInf", "a return reason code is required");
            }

            for (int j = 0; j < t.RtrRsnInf.Count; j++)
            {
                ReturnReasonInfo s = t.RtrRsnInf[j];
                string q = Checker.Idx(p + "/RtrRsnInf", j);
                c.Choice(q + "/Rsn", s.Rsn, 35);
                for (int k = 0; k < s.AddtlInf.Count; k++)
                {
                    c.Text(Checker.Idx(q + "/AddtlInf", k), s.AddtlInf[k], 105, true);
                }
            }
        }

        return c.Result;
    }

    private static Pacs004Document Read(XmlReader r, string ns)
    {
        var d = new Pacs004Document { Namespace = ns };
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "PmtRtr")
            {
                ReadReturn(r, d.PmtRtr);
            }
            else
            {
                r.Skip();
            }
        }

        return d;
    }

    private static void ReadReturn(XmlReader r, PaymentReturn rtr)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "GrpHdr":
                    rtr.GrpHdr.ReadInto(r);
                    break;
                case "TxInf":
                    rtr.TxInf.Add(ReadTransaction(r));
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static ReturnTransaction ReadTransaction(XmlReader r)
    {
        var t = new ReturnTransaction();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "RtrId":
                    t.RtrId = XmlInput.ReadText(r);
                    break;
                case "OrgnlGrpInf":
                    t.OrgnlGrpInf ??= new OriginalGroupInformation();
                    ReadOriginal(r, t.OrgnlGrpInf);
                    break;
                case "OrgnlEndToEndId":
                    t.OrgnlEndToEndId = XmlInput.ReadText(r);
                    break;
                case "OrgnlTxId":
                    t.OrgnlTxId = XmlInput.ReadText(r);
                    break;
                case "OrgnlIntrBkSttlmAmt":
                    t.OrgnlIntrBkSttlmAmt = Xsd.ReadAmountInto(r, t.OrgnlIntrBkSttlmAmt);
                    break;
                case "RtrdIntrBkSttlmAmt":
                    XmlInput.ReadAmount(r, t.RtrdIntrBkSttlmAmt);
                    break;
                case "ChrgBr":
                    t.ChrgBr = XmlInput.ReadText(r);
                    break;
                case "RtrRsnInf":
                    t.RtrRsnInf.Add(ReadReason(r));
                    break;
                default:
                    r.Skip();
                    break;
            }
        }

        return t;
    }

    private static void ReadOriginal(XmlReader r, OriginalGroupInformation g)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "OrgnlMsgId": g.OrgnlMsgId = XmlInput.ReadText(r); break;
                case "OrgnlMsgNmId": g.OrgnlMsgNmId = XmlInput.ReadText(r); break;
                case "OrgnlCreDtTm": g.OrgnlCreDtTm = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }

    private static ReturnReasonInfo ReadReason(XmlReader r)
    {
        var s = new ReturnReasonInfo();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Rsn": s.Rsn = Xsd.ReadChoiceInto(r, s.Rsn); break;
                case "AddtlInf": s.AddtlInf.Add(XmlInput.ReadText(r)); break;
                default: r.Skip(); break;
            }
        }

        return s;
    }
}

/// <summary>The pacs.004 message root, <c>PmtRtr</c>.</summary>
public sealed record PaymentReturn
{
    /// <summary>The group header.</summary>
    public GroupHeader GrpHdr { get; set; } = new();

    /// <summary>The returned transactions.</summary>
    public List<ReturnTransaction> TxInf { get; set; } = new();
}

/// <summary>One <c>TxInf</c>: a returned transaction.</summary>
public sealed record ReturnTransaction
{
    /// <summary>The return identification (a Pix RtrId).</summary>
    public string RtrId { get; set; } = string.Empty;

    /// <summary>The original group information.</summary>
    public OriginalGroupInformation? OrgnlGrpInf { get; set; }

    /// <summary>The EndToEndId of the original payment.</summary>
    public string OrgnlEndToEndId { get; set; } = string.Empty;

    /// <summary>The original transaction identification.</summary>
    public string OrgnlTxId { get; set; } = string.Empty;

    /// <summary>The original interbank settlement amount.</summary>
    public CurrencyAmount? OrgnlIntrBkSttlmAmt { get; set; }

    /// <summary>The returned amount.</summary>
    public CurrencyAmount RtrdIntrBkSttlmAmt { get; set; } = new();

    /// <summary>The charge bearer.</summary>
    public string ChrgBr { get; set; } = string.Empty;

    /// <summary>The return reasons.</summary>
    public List<ReturnReasonInfo> RtrRsnInf { get; set; } = new();

    /// <summary>The returned amount.</summary>
    public BrlAmount Amount => RtrdIntrBkSttlmAmt.Value;

    /// <summary>The first return reason code, or an empty string.</summary>
    public string Reason
    {
        get
        {
            foreach (ReturnReasonInfo r in RtrRsnInf)
            {
                string v = r.Rsn?.Value ?? string.Empty;
                if (v.Length > 0)
                {
                    return v;
                }
            }

            return string.Empty;
        }
    }
}

/// <summary><c>TxInf/OrgnlGrpInf</c>: a reference to the original message.</summary>
public sealed record OriginalGroupInformation
{
    /// <summary>The original MsgId.</summary>
    public string OrgnlMsgId { get; set; } = string.Empty;

    /// <summary>The original message name, e.g. <c>pacs.008.001.08</c>.</summary>
    public string OrgnlMsgNmId { get; set; } = string.Empty;

    /// <summary>The original creation date and time.</summary>
    public string OrgnlCreDtTm { get; set; } = string.Empty;
}

/// <summary><c>RtrRsnInf</c>: a return reason.</summary>
public sealed record ReturnReasonInfo
{
    /// <summary>The reason code.</summary>
    public CodeOrProprietary? Rsn { get; set; }

    /// <summary>Additional information lines (Max105Text).</summary>
    public List<string> AddtlInf { get; set; } = new();
}
