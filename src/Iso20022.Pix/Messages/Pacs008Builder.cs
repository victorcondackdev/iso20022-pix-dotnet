using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>
/// A payer or payee: a name and a CPF (11 digits) or CNPJ (14 characters).
/// The ISO 20022 identification (<c>PrvtId</c> or <c>OrgId</c>) is chosen from
/// the length of <see cref="TaxId"/>.
/// </summary>
public sealed record TransferParty
{
    /// <summary>The name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The CPF or CNPJ, unformatted.</summary>
    public string TaxId { get; init; } = string.Empty;

    internal Party ToParty() =>
        Text.Utf8Length(TaxId ?? string.Empty) == 14
            ? Party.Organisation(Name, TaxId ?? string.Empty)
            : Party.Person(Name, TaxId ?? string.Empty);
}

/// <summary>
/// A Pix account: the participant ISPB, the branch (agência; empty for
/// payment accounts without branch), the account number and the account
/// type (<see cref="AccountTypes"/>).
/// </summary>
public sealed record TransferAccount
{
    /// <summary>The 8-digit ISPB of the participant.</summary>
    public string Ispb { get; init; } = string.Empty;

    /// <summary>The branch.</summary>
    public string Branch { get; init; } = string.Empty;

    /// <summary>The account number.</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>The account type, one of <see cref="AccountTypes"/>.</summary>
    public string Type { get; init; } = string.Empty;
}

/// <summary>The flat, Pix-oriented description of one credit transfer that <see cref="Pacs008Builder"/> maps onto <c>CdtTrfTxInf</c>.</summary>
public sealed record Transfer
{
    /// <summary>The Pix EndToEndId. When empty, one is generated for the debtor ISPB at <see cref="AcceptedAt"/>.</summary>
    public string EndToEndId { get; init; } = string.Empty;

    /// <summary>The QR code transaction id (<c>PmtId/TxId</c>), optional.</summary>
    public string TxId { get; init; } = string.Empty;

    /// <summary>The amount. It must be positive.</summary>
    public BrlAmount Amount { get; init; }

    /// <summary>When the payer's participant accepted the order (<c>AccptncDtTm</c>); defaults to the builder's creation time.</summary>
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary><c>PmtTpInf/LclInstrm/Prtry</c>, e.g. <see cref="Pacs008.InitiationDict"/>; optional.</summary>
    public string Initiation { get; init; } = string.Empty;

    /// <summary><c>PmtTpInf/InstrPrty</c>, e.g. <c>HIGH</c>; optional.</summary>
    public string Priority { get; init; } = string.Empty;

    /// <summary>The payer.</summary>
    public TransferParty Debtor { get; init; } = new();

    /// <summary>The payer's account.</summary>
    public TransferAccount DebtorAccount { get; init; } = new();

    /// <summary>The payee.</summary>
    public TransferParty Creditor { get; init; } = new();

    /// <summary>The payee's account.</summary>
    public TransferAccount CreditorAccount { get; init; } = new();

    /// <summary>The payee's Pix key, carried in <c>CdtrAcct/Prxy/Id</c>; optional.</summary>
    public string PixKey { get; init; } = string.Empty;

    /// <summary><c>Purp/Cd</c>; optional.</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>The message for the payee (<c>RmtInf/Ustrd</c>, at most 140 characters); optional.</summary>
    public string RemittanceInfo { get; init; } = string.Empty;
}

/// <summary>Assembles and validates a pacs.008 Document.</summary>
/// <example>
/// <code>
/// var doc = new Pacs008Builder(DateTimeOffset.UtcNow)
///     .Add(new Transfer { Amount = BrlAmount.Parse("150.75"), ... })
///     .Build();
/// </code>
/// </example>
public sealed class Pacs008Builder
{
    private readonly DateTimeOffset _created;
    private readonly List<Transfer> _transfers = new();
    private string _msgId = string.Empty;
    private Stream? _random;

    /// <summary>Starts a message created at the given time.</summary>
    /// <param name="created">The creation time (<c>GrpHdr/CreDtTm</c>).</param>
    public Pacs008Builder(DateTimeOffset created)
    {
        _created = created;
    }

    /// <summary>Sets <c>GrpHdr/MsgId</c>. When not set, one is generated for the debtor ISPB of the first transfer.</summary>
    /// <param name="msgId">The message identification.</param>
    /// <returns>This builder.</returns>
    public Pacs008Builder WithMsgId(string msgId)
    {
        _msgId = msgId ?? string.Empty;
        return this;
    }

    /// <summary>Sets the source of random bytes for generated identifiers (a cryptographic generator by default).</summary>
    /// <param name="random">The source, read 32 bytes at a time.</param>
    /// <returns>This builder.</returns>
    public Pacs008Builder WithRandom(Stream? random)
    {
        _random = random;
        return this;
    }

    /// <summary>Appends a transfer.</summary>
    /// <param name="transfer">The transfer.</param>
    /// <returns>This builder.</returns>
    public Pacs008Builder Add(Transfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        _transfers.Add(transfer);
        return this;
    }

    /// <summary>Maps the transfers onto a Document, generating missing identifiers, and validates it.</summary>
    /// <returns>The document.</returns>
    /// <exception cref="MessageBuildException">No transfer was added.</exception>
    /// <exception cref="MessageValidationException">The document is invalid; it is available in <see cref="MessageValidationException.Document"/>.</exception>
    /// <exception cref="InvalidOperationException">The random source fails.</exception>
    public Pacs008Document Build()
    {
        if (_transfers.Count == 0)
        {
            throw new MessageBuildException(MessageBuildErrorKind.NoTransactions, "pacs008: no transfers added");
        }

        var d = new Pacs008Document { Namespace = Pacs008.DefaultNamespace };
        FIToFICustomerCreditTransfer m = d.FIToFICstmrCdtTrf;
        m.GrpHdr = new GroupHeader
        {
            MsgId = _msgId,
            CreDtTm = IsoDateTime.Format(_created),
            NbOfTxs = GoFormat.Int(_transfers.Count),
            SttlmInf = new SettlementInstruction { SttlmMtd = Pacs008.SettlementClearing },
        };

        string firstIspb = _transfers[0].DebtorAccount?.Ispb ?? string.Empty;
        if (m.GrpHdr.MsgId.Length == 0 && PixIdentifiers.IsValidIspb(firstIspb))
        {
            m.GrpHdr.MsgId = PixIdentifiers.NewMsgId(firstIspb, _random);
        }

        foreach (Transfer t in _transfers)
        {
            m.CdtTrfTxInf.Add(Map(t));
        }

        IReadOnlyList<Violation> violations = d.Validate();
        if (violations.Count > 0)
        {
            throw new MessageValidationException(violations, d);
        }

        return d;
    }

    private CreditTransferTransaction Map(Transfer t)
    {
        DateTimeOffset accepted = t.AcceptedAt ?? _created;
        TransferAccount debtorAccount = t.DebtorAccount ?? new TransferAccount();
        TransferAccount creditorAccount = t.CreditorAccount ?? new TransferAccount();
        string e2e = t.EndToEndId ?? string.Empty;
        if (e2e.Length == 0 && PixIdentifiers.IsValidIspb(debtorAccount.Ispb))
        {
            e2e = PixIdentifiers.NewEndToEndId(debtorAccount.Ispb, accepted, _random);
        }

        Account creditorAcct = Account.Create(creditorAccount.Branch, creditorAccount.Number, creditorAccount.Type);
        var tx = new CreditTransferTransaction
        {
            PmtId = new PaymentIdentification { EndToEndId = e2e, TxId = t.TxId ?? string.Empty },
            IntrBkSttlmAmt = CurrencyAmount.InBrl(t.Amount),
            AccptncDtTm = IsoDateTime.Format(accepted),
            ChrgBr = Pacs008.ChargesSlev,
            Dbtr = (t.Debtor ?? new TransferParty()).ToParty(),
            DbtrAcct = Account.Create(debtorAccount.Branch, debtorAccount.Number, debtorAccount.Type),
            DbtrAgt = Agent.FromIspb(debtorAccount.Ispb),
            CdtrAgt = Agent.FromIspb(creditorAccount.Ispb),
            Cdtr = (t.Creditor ?? new TransferParty()).ToParty(),
            CdtrAcct = creditorAcct,
        };

        if (!string.IsNullOrEmpty(t.Initiation) || !string.IsNullOrEmpty(t.Priority))
        {
            var typeInfo = new PaymentTypeInformation { InstrPrty = t.Priority ?? string.Empty };
            if (!string.IsNullOrEmpty(t.Initiation))
            {
                typeInfo.LclInstrm = CodeOrProprietary.Proprietary(t.Initiation);
            }

            tx.PmtTpInf = typeInfo;
        }

        if (!string.IsNullOrEmpty(t.PixKey))
        {
            creditorAcct.Prxy = new Proxy { Id = t.PixKey };
        }

        if (!string.IsNullOrEmpty(t.Purpose))
        {
            tx.Purp = CodeOrProprietary.Code(t.Purpose);
        }

        if (!string.IsNullOrEmpty(t.RemittanceInfo))
        {
            tx.RmtInf = new RemittanceInformation { Ustrd = { t.RemittanceInfo } };
        }

        return tx;
    }
}
