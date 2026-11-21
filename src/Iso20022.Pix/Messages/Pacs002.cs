using System.Text;
using System.Xml;
using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>Options of <see cref="Pacs002.Accept"/> and <see cref="Pacs002.Reject"/>.</summary>
public sealed record Pacs002Options
{
    /// <summary><c>GrpHdr/CreDtTm</c>; defaults to the current time.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary><c>FctvIntrBkSttlmDt/DtTm</c> on accepted transactions; omitted when null.</summary>
    public DateTimeOffset? SettledAt { get; init; }

    /// <summary><c>GrpHdr/MsgId</c>; by default one is generated for the reporting ISPB.</summary>
    public string? MsgId { get; init; }

    /// <summary>The ISPB of the reporting participant, used to generate the MsgId; defaults to the <c>CdtrAgt</c> of the first original transaction.</summary>
    public string? Ispb { get; init; }

    /// <summary>The source of random bytes for generated identifiers.</summary>
    public Stream? Random { get; init; }

    /// <summary><c>StsRsnInf/AddtlInf</c> lines (Max105Text) added to rejections.</summary>
    public IReadOnlyList<string> AdditionalInfo { get; init; } = Array.Empty<string>();

    /// <summary>Lets <see cref="Pacs002.Reject"/> use a code that is not in <see cref="Pacs002.Reasons"/>.</summary>
    public bool AllowUnknownReason { get; init; }
}

/// <summary>
/// Constants, reason codes and builders of the pacs.002 message
/// (FIToFIPaymentStatusReport), which reports the outcome of a Pix payment:
/// accepted and settled (ACSC) or rejected (RJCT) with a reason code.
/// </summary>
public static class Pacs002
{
    /// <summary>The namespace used when serializing a document whose namespace is empty.</summary>
    public const string DefaultNamespace = MessageIds.NamespacePrefix + "pacs.002.001.10";

    /// <summary>Accepted, settlement completed on the debtor side.</summary>
    public const string StatusAcceptedSettlementCompleted = "ACSC";

    /// <summary>Rejected.</summary>
    public const string StatusRejected = "RJCT";

    /// <summary>Accepted, credited to the creditor account.</summary>
    public const string StatusAcceptedCreditorAccount = "ACCC";

    /// <summary>Accepted, settlement in process.</summary>
    public const string StatusAcceptedSettlementInProcess = "ACSP";

    /// <summary>Pending.</summary>
    public const string StatusPending = "PDNG";

    /// <summary>
    /// Common ISO 20022 status reason codes (ExternalStatusReason1Code) seen in
    /// Pix rejections. Only codes whose ISO meaning is well established are
    /// included; the SPI catalog defines which apply to which situation and
    /// may use others, which parsing and validation accept unchanged.
    /// </summary>
    public static readonly CodeSet Reasons = new(new[]
    {
        new CodeInfo("AB03", "AbortedSettlementTimeout", "Settlement aborted due to timeout.", "Liquidação abortada por tempo esgotado."),
        new CodeInfo("AB09", "ErrorCreditorAgent", "Transaction stopped due to an error at the creditor agent.", "Transação interrompida por erro no participante recebedor."),
        new CodeInfo("AC01", "IncorrectAccountNumber", "Account number format is incorrect.", "Formato do número da conta incorreto."),
        new CodeInfo("AC03", "InvalidCreditorAccountNumber", "Creditor account number is invalid or missing.", "Número da conta do recebedor inválido ou ausente."),
        new CodeInfo("AC06", "BlockedAccount", "Account is blocked; postings are not allowed.", "Conta bloqueada; lançamentos não permitidos."),
        new CodeInfo("AC07", "ClosedCreditorAccountNumber", "Creditor account is closed.", "Conta do recebedor encerrada."),
        new CodeInfo("AC14", "InvalidCreditorAccountType", "Creditor account type is invalid or missing.", "Tipo de conta do recebedor inválido ou ausente."),
        new CodeInfo("AG03", "TransactionNotSupported", "Transaction type is not supported or authorised on this account.", "Tipo de transação não suportado ou não autorizado nesta conta."),
        new CodeInfo("AM01", "ZeroAmount", "Amount is zero.", "Valor igual a zero."),
        new CodeInfo("AM02", "NotAllowedAmount", "Amount is greater than the allowed maximum.", "Valor acima do máximo permitido."),
        new CodeInfo("AM04", "InsufficientFunds", "Insufficient funds to cover the amount.", "Saldo insuficiente para cobrir o valor."),
        new CodeInfo("AM09", "WrongAmount", "Amount received is not the amount agreed or expected.", "Valor recebido diferente do acordado ou esperado."),
        new CodeInfo("BE01", "InconsistentWithEndCustomer", "End customer identification is not consistent with the account.", "Identificação do cliente não confere com a conta."),
        new CodeInfo("BE17", "InvalidCreditorIdentificationCode", "Creditor identification code is invalid or missing.", "Identificação (CPF/CNPJ) do recebedor inválida ou ausente."),
        new CodeInfo("DS04", "OrderRejected", "Order rejected by the bank for reasons concerning its content.", "Ordem rejeitada pelo participante por motivo de conteúdo."),
        new CodeInfo("DT02", "InvalidCreationDate", "Invalid creation date and time in the group header.", "Data e hora de criação inválidas no cabeçalho."),
        new CodeInfo("DUPL", "DuplicatePayment", "Payment is a duplicate of another payment.", "Pagamento duplicado."),
        new CodeInfo("ED05", "SettlementFailed", "Settlement of the transaction failed.", "Falha na liquidação da transação."),
        new CodeInfo("MD01", "NoMandate", "No mandate.", "Ausência de autorização (mandato)."),
        new CodeInfo("MS03", "NotSpecifiedReasonAgentGenerated", "Reason not specified by the agent.", "Motivo não especificado pelo participante."),
        new CodeInfo("RR04", "RegulatoryReason", "Regulatory reason.", "Motivo regulatório."),
        new CodeInfo("SL02", "SpecificServiceOfferedByCreditorAgent", "Due to a specific service offered by the creditor agent.", "Devido a serviço específico oferecido pelo participante recebedor."),
    });

    /// <summary>Builds a status report accepting every transaction of <paramref name="original"/> with <c>TxSts</c> ACSC.</summary>
    /// <param name="original">The payment being answered.</param>
    /// <param name="options">The options.</param>
    /// <returns>The validated report.</returns>
    /// <exception cref="MessageBuildException">The original has no transactions.</exception>
    /// <exception cref="InvalidIdentifierException">A MsgId cannot be generated for the reporting ISPB.</exception>
    /// <exception cref="MessageValidationException">The report is invalid.</exception>
    public static Pacs002Document Accept(Pacs008Document? original, Pacs002Options? options = null) =>
        Report(original, StatusAcceptedSettlementCompleted, string.Empty, options ?? new Pacs002Options());

    /// <summary>
    /// Builds a status report rejecting every transaction of <paramref name="original"/>
    /// with <c>TxSts</c> RJCT and the given reason code (<c>StsRsnInf/Rsn/Cd</c>).
    /// </summary>
    /// <param name="original">The payment being answered.</param>
    /// <param name="reason">The reason code, e.g. <c>AC03</c>.</param>
    /// <param name="options">The options.</param>
    /// <returns>The validated report.</returns>
    /// <exception cref="MessageBuildException">The original has no transactions, or the reason is missing or unknown.</exception>
    /// <exception cref="InvalidIdentifierException">A MsgId cannot be generated for the reporting ISPB.</exception>
    /// <exception cref="MessageValidationException">The report is invalid.</exception>
    public static Pacs002Document Reject(Pacs008Document? original, string reason, Pacs002Options? options = null) =>
        Report(original, StatusRejected, reason ?? string.Empty, options ?? new Pacs002Options());

    private static Pacs002Document Report(Pacs008Document? original, string status, string reason, Pacs002Options o)
    {
        if (original is null || original.Transactions.Count == 0)
        {
            throw new MessageBuildException(MessageBuildErrorKind.NoTransactions, "pacs002: original pacs.008 has no transactions");
        }

        if (status == StatusRejected)
        {
            if (reason.Length == 0)
            {
                throw new MessageBuildException(MessageBuildErrorKind.MissingReason, "pacs002: a rejection needs a reason code");
            }

            if (!Reasons.Contains(reason) && !o.AllowUnknownReason)
            {
                throw new MessageBuildException(MessageBuildErrorKind.UnknownReason, "pacs002: unknown reason code: " + GoFormat.Quote(reason));
            }
        }

        List<CreditTransferTransaction> txs = original.Transactions;
        DateTimeOffset at = o.At ?? DateTimeOffset.UtcNow;
        string ispb = string.IsNullOrEmpty(o.Ispb) ? txs[0].CdtrAgt.Ispb : o.Ispb;
        string msgId = string.IsNullOrEmpty(o.MsgId) ? PixIdentifiers.NewMsgId(ispb, o.Random) : o.MsgId;

        GroupHeader h = original.FIToFICstmrCdtTrf.GrpHdr;
        var d = new Pacs002Document { Namespace = DefaultNamespace };
        StatusReport r = d.FIToFIPmtStsRpt;
        r.GrpHdr = new MessageHeader { MsgId = msgId, CreDtTm = IsoDateTime.Format(at) };
        r.OrgnlGrpInfAndSts.Add(new OriginalGroupHeader
        {
            OrgnlMsgId = h.MsgId,
            OrgnlMsgNmId = original.MessageName,
            OrgnlCreDtTm = h.CreDtTm,
        });

        foreach (CreditTransferTransaction tx in txs)
        {
            var ts = new TransactionStatus { OrgnlEndToEndId = tx.PmtId.EndToEndId, TxSts = status };
            if (status == StatusRejected)
            {
                var info = new StatusReasonInfo { Rsn = CodeOrProprietary.Code(reason) };
                info.AddtlInf.AddRange(o.AdditionalInfo ?? Array.Empty<string>());
                ts.StsRsnInf.Add(info);
            }
            else if (o.SettledAt is DateTimeOffset settled)
            {
                ts.FctvIntrBkSttlmDt = new DateAndDateTime { DtTm = IsoDateTime.Format(settled) };
            }

            r.TxInfAndSts.Add(ts);
        }

        IReadOnlyList<Violation> violations = d.Validate();
        if (violations.Count > 0)
        {
            throw new MessageValidationException(violations, d);
        }

        return d;
    }
}

/// <summary>A pacs.002 message (FIToFIPaymentStatusReport), in the element order of pacs.002.001.10.</summary>
public sealed record Pacs002Document
{
    private const string Root = "/Document/FIToFIPmtStsRpt";

    /// <summary>The Document namespace, as parsed; empty means <see cref="Pacs002.DefaultNamespace"/>.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The message root.</summary>
    public StatusReport FIToFIPmtStsRpt { get; set; } = new();

    /// <summary>The <c>TxInfAndSts</c> entries.</summary>
    public List<TransactionStatus> Transactions => FIToFIPmtStsRpt.TxInfAndSts;

    /// <summary>The message version from the namespace, e.g. <c>10</c>, or an empty string.</summary>
    public string Version => MessageType.TryParse(Namespace, out MessageType t) ? t.Version : string.Empty;

    /// <summary>Parses a pacs.002 message (bare Document or business message).</summary>
    /// <param name="xml">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.002 message.</exception>
    public static Pacs002Document Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return XmlInput.ReadDocument<Pacs002Document>(r, MessageIds.Pacs002, Read);
    }

    /// <summary>Parses a pacs.002 message from a stream, which is not closed.</summary>
    /// <param name="stream">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.002 message.</exception>
    public static Pacs002Document Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return XmlInput.ReadDocument<Pacs002Document>(r, MessageIds.Pacs002, Read);
    }

    /// <summary>Parses a pacs.002 message from UTF-8 bytes.</summary>
    /// <param name="utf8">The message.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MessageParseException">The payload is not a pacs.002 message.</exception>
    public static Pacs002Document Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var stream = new MemoryStream(utf8, writable: false);
        return Parse(stream);
    }

    /// <summary>Serializes the document as indented XML, byte-identical to the Go implementation. Does not validate.</summary>
    /// <returns>The XML.</returns>
    public string ToXml()
    {
        var w = new XmlOutput();
        w.Start("Document", string.IsNullOrEmpty(Namespace) ? Pacs002.DefaultNamespace : Namespace);
        StatusReport rpt = FIToFIPmtStsRpt;
        w.Start("FIToFIPmtStsRpt");
        w.Start("GrpHdr");
        w.Element("MsgId", rpt.GrpHdr.MsgId);
        w.Element("CreDtTm", rpt.GrpHdr.CreDtTm);
        w.End("GrpHdr");
        foreach (OriginalGroupHeader g in rpt.OrgnlGrpInfAndSts)
        {
            w.Start("OrgnlGrpInfAndSts");
            w.Element("OrgnlMsgId", g.OrgnlMsgId);
            w.Element("OrgnlMsgNmId", g.OrgnlMsgNmId);
            w.Optional("OrgnlCreDtTm", g.OrgnlCreDtTm);
            w.Optional("GrpSts", g.GrpSts);
            w.End("OrgnlGrpInfAndSts");
        }

        foreach (TransactionStatus t in rpt.TxInfAndSts)
        {
            w.Start("TxInfAndSts");
            w.Optional("StsId", t.StsId);
            w.Element("OrgnlEndToEndId", t.OrgnlEndToEndId);
            w.Optional("OrgnlTxId", t.OrgnlTxId);
            w.Element("TxSts", t.TxSts);
            foreach (StatusReasonInfo s in t.StsRsnInf)
            {
                w.Start("StsRsnInf");
                Xsd.WriteChoice(w, "Rsn", s.Rsn);
                w.OptionalElements("AddtlInf", s.AddtlInf);
                w.End("StsRsnInf");
            }

            Xsd.WriteDateAndDateTime(w, "FctvIntrBkSttlmDt", t.FctvIntrBkSttlmDt);
            w.End("TxInfAndSts");
        }

        w.End("FIToFIPmtStsRpt");
        w.End("Document");
        return w.Finish();
    }

    /// <summary>Serializes the document as UTF-8 bytes; see <see cref="ToXml"/>.</summary>
    /// <returns>The XML bytes, without a byte order mark.</returns>
    public byte[] ToXmlBytes() => Encoding.UTF8.GetBytes(ToXml());

    /// <summary>
    /// Checks the report and returns every violation: MsgId and CreDtTm, at
    /// least one <c>TxInfAndSts</c>, a valid Pix <c>OrgnlEndToEndId</c>, a status
    /// code, a reason on rejections, and well-formed optional elements. Reason
    /// codes are not restricted to <see cref="Pacs002.Reasons"/>.
    /// </summary>
    /// <returns>The violations; empty when the report is valid.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        var c = new Checker();
        if (!string.IsNullOrEmpty(Namespace)
            && (!MessageType.TryParse(Namespace, out MessageType id) || id.Id != MessageIds.Pacs002))
        {
            c.Add("/Document/@xmlns", "must be a pacs.002 namespace, got " + GoFormat.Quote(Namespace));
        }

        StatusReport r = FIToFIPmtStsRpt;
        c.Max35(Root + "/GrpHdr/MsgId", r.GrpHdr.MsgId, true);
        c.DateTime(Root + "/GrpHdr/CreDtTm", r.GrpHdr.CreDtTm, true);
        for (int i = 0; i < r.OrgnlGrpInfAndSts.Count; i++)
        {
            OriginalGroupHeader g = r.OrgnlGrpInfAndSts[i];
            string p = Checker.Idx(Root + "/OrgnlGrpInfAndSts", i);
            c.Max35(p + "/OrgnlMsgId", g.OrgnlMsgId, true);
            c.Max35(p + "/OrgnlMsgNmId", g.OrgnlMsgNmId, true);
            c.DateTime(p + "/OrgnlCreDtTm", g.OrgnlCreDtTm, false);
            c.Code(p + "/GrpSts", g.GrpSts, false);
        }

        if (r.TxInfAndSts.Count == 0)
        {
            c.Add(Root + "/TxInfAndSts", "at least one transaction status is required");
        }

        for (int i = 0; i < r.TxInfAndSts.Count; i++)
        {
            TransactionStatus t = r.TxInfAndSts[i];
            string p = Checker.Idx(Root + "/TxInfAndSts", i);
            c.Max35(p + "/StsId", t.StsId, false);
            c.EndToEndId(p + "/OrgnlEndToEndId", t.OrgnlEndToEndId);
            c.Max35(p + "/OrgnlTxId", t.OrgnlTxId, false);
            c.Code(p + "/TxSts", t.TxSts, true);
            if (t.TxSts == Pacs002.StatusRejected && t.Reason.Length == 0)
            {
                c.Add(p + "/StsRsnInf", "a rejection (RJCT) requires a reason code");
            }

            for (int j = 0; j < t.StsRsnInf.Count; j++)
            {
                StatusReasonInfo s = t.StsRsnInf[j];
                string q = Checker.Idx(p + "/StsRsnInf", j);
                c.Choice(q + "/Rsn", s.Rsn, 35);
                for (int k = 0; k < s.AddtlInf.Count; k++)
                {
                    c.Text(Checker.Idx(q + "/AddtlInf", k), s.AddtlInf[k], 105, true);
                }
            }

            c.DateOrDateTime(p + "/FctvIntrBkSttlmDt", t.FctvIntrBkSttlmDt, false);
        }

        return c.Result;
    }

    private static Pacs002Document Read(XmlReader r, string ns)
    {
        var d = new Pacs002Document { Namespace = ns };
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "FIToFIPmtStsRpt")
            {
                ReadReport(r, d.FIToFIPmtStsRpt);
            }
            else
            {
                r.Skip();
            }
        }

        return d;
    }

    private static void ReadReport(XmlReader r, StatusReport rpt)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "GrpHdr":
                    rpt.GrpHdr.ReadInto(r);
                    break;
                case "OrgnlGrpInfAndSts":
                    rpt.OrgnlGrpInfAndSts.Add(ReadOriginal(r));
                    break;
                case "TxInfAndSts":
                    rpt.TxInfAndSts.Add(ReadStatus(r));
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static OriginalGroupHeader ReadOriginal(XmlReader r)
    {
        var g = new OriginalGroupHeader();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "OrgnlMsgId": g.OrgnlMsgId = XmlInput.ReadText(r); break;
                case "OrgnlMsgNmId": g.OrgnlMsgNmId = XmlInput.ReadText(r); break;
                case "OrgnlCreDtTm": g.OrgnlCreDtTm = XmlInput.ReadText(r); break;
                case "GrpSts": g.GrpSts = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }

        return g;
    }

    private static TransactionStatus ReadStatus(XmlReader r)
    {
        var t = new TransactionStatus();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "StsId": t.StsId = XmlInput.ReadText(r); break;
                case "OrgnlEndToEndId": t.OrgnlEndToEndId = XmlInput.ReadText(r); break;
                case "OrgnlTxId": t.OrgnlTxId = XmlInput.ReadText(r); break;
                case "TxSts": t.TxSts = XmlInput.ReadText(r); break;
                case "StsRsnInf": t.StsRsnInf.Add(ReadReason(r)); break;
                case "FctvIntrBkSttlmDt": t.FctvIntrBkSttlmDt = Xsd.ReadDateAndDateTime(r, t.FctvIntrBkSttlmDt); break;
                default: r.Skip(); break;
            }
        }

        return t;
    }

    private static StatusReasonInfo ReadReason(XmlReader r)
    {
        var s = new StatusReasonInfo();
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

/// <summary>The pacs.002 message root, <c>FIToFIPmtStsRpt</c>.</summary>
public sealed record StatusReport
{
    /// <summary>The group header.</summary>
    public MessageHeader GrpHdr { get; set; } = new();

    /// <summary>References to the messages being reported on.</summary>
    public List<OriginalGroupHeader> OrgnlGrpInfAndSts { get; set; } = new();

    /// <summary>The transaction statuses.</summary>
    public List<TransactionStatus> TxInfAndSts { get; set; } = new();
}

/// <summary>The group header (<c>GrpHdr</c>) of pacs.002 and camt.054: identification and creation time.</summary>
public sealed record MessageHeader
{
    /// <summary>The message identification (Max35Text).</summary>
    public string MsgId { get; set; } = string.Empty;

    /// <summary>The creation date and time (ISODateTime).</summary>
    public string CreDtTm { get; set; } = string.Empty;

    internal void ReadInto(XmlReader r)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "MsgId": MsgId = XmlInput.ReadText(r); break;
                case "CreDtTm": CreDtTm = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }
}

/// <summary><c>OrgnlGrpInfAndSts</c>: a reference to the message being reported on.</summary>
public sealed record OriginalGroupHeader
{
    /// <summary>The original MsgId.</summary>
    public string OrgnlMsgId { get; set; } = string.Empty;

    /// <summary>The original message name, e.g. <c>pacs.008.001.08</c>.</summary>
    public string OrgnlMsgNmId { get; set; } = string.Empty;

    /// <summary>The original creation date and time.</summary>
    public string OrgnlCreDtTm { get; set; } = string.Empty;

    /// <summary>The group status.</summary>
    public string GrpSts { get; set; } = string.Empty;
}

/// <summary>One <c>TxInfAndSts</c>: the status of one original transaction.</summary>
public sealed record TransactionStatus
{
    /// <summary>The status identification.</summary>
    public string StsId { get; set; } = string.Empty;

    /// <summary>The EndToEndId of the original payment.</summary>
    public string OrgnlEndToEndId { get; set; } = string.Empty;

    /// <summary>The original transaction identification.</summary>
    public string OrgnlTxId { get; set; } = string.Empty;

    /// <summary>The transaction status, e.g. <see cref="Pacs002.StatusAcceptedSettlementCompleted"/>.</summary>
    public string TxSts { get; set; } = string.Empty;

    /// <summary>The status reasons.</summary>
    public List<StatusReasonInfo> StsRsnInf { get; set; } = new();

    /// <summary>The effective interbank settlement date.</summary>
    public DateAndDateTime? FctvIntrBkSttlmDt { get; set; }

    /// <summary>The first reason code (Cd or Prtry), or an empty string.</summary>
    public string Reason
    {
        get
        {
            foreach (StatusReasonInfo r in StsRsnInf)
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

    /// <summary>Whether <see cref="TxSts"/> is ACSC or ACCC.</summary>
    public bool IsAccepted => TxSts is Pacs002.StatusAcceptedSettlementCompleted or Pacs002.StatusAcceptedCreditorAccount;
}

/// <summary><c>StsRsnInf</c>: a status reason.</summary>
public sealed record StatusReasonInfo
{
    /// <summary>The reason code.</summary>
    public CodeOrProprietary? Rsn { get; set; }

    /// <summary>Additional information lines (Max105Text).</summary>
    public List<string> AddtlInf { get; set; } = new();
}
