using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Tests;

public class Pacs002Tests
{
    [Fact]
    public void ReadsTheRejection()
    {
        Pacs002Document d = Pacs002Document.Parse(TestData.Load("pacs002_reject.xml"));
        TransactionStatus tx = d.Transactions[0];
        Assert.False(tx.IsAccepted);
        Assert.Equal("AC03", tx.Reason);
        Assert.Equal("10", d.Version);
        Assert.Empty(d.Validate());
    }

    [Fact]
    public void GeneratedMsgIdUsesTheCreditorAgentByDefault()
    {
        Pacs002Document d = Pacs002.Reject(TestData.Original(), "AM04");
        Assert.StartsWith("M88888888", d.FIToFIPmtStsRpt.GrpHdr.MsgId);
        Assert.Equal(32, d.FIToFIPmtStsRpt.GrpHdr.MsgId.Length);

        Pacs002Document a = Pacs002.Accept(TestData.Original(), new Pacs002Options { Ispb = "77777777" });
        Assert.StartsWith("M77777777", a.FIToFIPmtStsRpt.GrpHdr.MsgId);
        Assert.True(a.Transactions[0].IsAccepted);
        Assert.Equal(string.Empty, a.Transactions[0].Reason);
    }

    [Fact]
    public void RejectionRules()
    {
        Assert.Equal(MessageBuildErrorKind.UnknownReason, Assert.Throws<MessageBuildException>(() => Pacs002.Reject(TestData.Original(), "ZZ99")).Kind);
        using MemoryStream zeros = TestData.Stream(new byte[64]);
        Pacs002.Reject(TestData.Original(), "ZZ99", new Pacs002Options { AllowUnknownReason = true, Random = zeros });
        Assert.Equal(MessageBuildErrorKind.MissingReason, Assert.Throws<MessageBuildException>(() => Pacs002.Reject(TestData.Original(), string.Empty)).Kind);
        Assert.Throws<MessageValidationException>(() => Pacs002.Reject(TestData.Original(), "TOOLONG", new Pacs002Options { AllowUnknownReason = true }));
        Assert.Equal(MessageBuildErrorKind.NoTransactions, Assert.Throws<MessageBuildException>(() => Pacs002.Accept(new Pacs008Document())).Kind);
        Assert.Equal(MessageBuildErrorKind.NoTransactions, Assert.Throws<MessageBuildException>(() => Pacs002.Accept(null)).Kind);
        Assert.Throws<InvalidIdentifierException>(() => Pacs002.Accept(TestData.Original(), new Pacs002Options { Ispb = "x" }));
    }

    [Fact]
    public void UnknownReasonCodesAreValidAndPreserved()
    {
        string xml = TestData.Load("pacs002_reject.xml").Replace("AC03", "XY12", StringComparison.Ordinal);
        Pacs002Document d = Pacs002Document.Parse(xml);
        Assert.Empty(d.Validate());
        Assert.Equal("XY12", d.Transactions[0].Reason);
        Assert.Equal(MessageParseErrorKind.UnexpectedMessage, Assert.Throws<MessageParseException>(() => Pacs002Document.Parse(TestData.Load("pacs008.xml"))).Kind);
    }

    [Fact]
    public void SettlementDateChoice()
    {
        Pacs002Document d = Pacs002Document.Parse(TestData.Load("pacs002_reject.xml"));
        TransactionStatus tx = d.Transactions[0];
        tx.FctvIntrBkSttlmDt = new DateAndDateTime { Dt = "2026-11-21", DtTm = "2026-11-21T10:00:00Z" };
        Assert.Contains(d.Validate(), v => v.Message == "must contain exactly one of Dt or DtTm, got both");
        tx.FctvIntrBkSttlmDt = new DateAndDateTime { Dt = "21-11-2026" };
        Assert.Contains(d.Validate(), v => v.Path.EndsWith("FctvIntrBkSttlmDt/Dt", StringComparison.Ordinal));
        tx.StsRsnInf.Clear();
        Assert.Contains(d.Validate(), v => v.Message == "a rejection (RJCT) requires a reason code");
    }

    [Fact]
    public void ReasonTable()
    {
        Assert.True(Pacs002.Reasons.TryLookup("AM04", out CodeInfo? info));
        Assert.Equal("InsufficientFunds", info!.Name);
        Assert.False(Pacs002.Reasons.TryLookup("ZZZZ", out _));
        IReadOnlyList<CodeInfo> all = Pacs002.Reasons.Sorted();
        for (int i = 0; i < all.Count; i++)
        {
            Assert.Equal(4, all[i].Code.Length);
            Assert.NotEqual(string.Empty, all[i].Portuguese);
            if (i > 0)
            {
                Assert.True(string.CompareOrdinal(all[i - 1].Code, all[i].Code) < 0);
            }
        }
    }
}

public class Pacs004Tests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GeneratedIdentifiersBelongToTheCreditorParticipant()
    {
        Pacs004Document d = Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonFraud, BrlAmount.Parse("150.75"), new Pacs004Options { At = At });
        ReturnTransaction tx = d.Transactions[0];
        PixId rtr = PixIdentifiers.ParseReturnId(tx.RtrId);
        Assert.Equal("88888888", rtr.Ispb);
        Assert.Equal(At.UtcDateTime, rtr.Time);
        Assert.StartsWith("M88888888", d.PmtRtr.GrpHdr.MsgId);
        Assert.Equal(Pacs004.ReasonFraud, tx.Reason);
        Assert.Equal(BrlAmount.Parse("150.75"), tx.Amount);
    }

    [Theory]
    [InlineData("150.75", "0", true)]
    [InlineData("0.01", "0", true)]
    [InlineData("150.76", "0", false)]
    [InlineData("100.75", "50", true)]
    [InlineData("100.76", "50", false)]
    [InlineData("0.01", "200", false)]
    public void PartialReturnGuard(string amount, string prior, bool ok)
    {
        var options = new Pacs004Options { At = At, PriorReturns = BrlAmount.Parse(prior) };
        if (ok)
        {
            Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse(amount), options);
            return;
        }

        MessageBuildException e = Assert.Throws<MessageBuildException>(
            () => Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse(amount), options));
        Assert.Equal(MessageBuildErrorKind.AmountExceeded, e.Kind);
    }

    [Fact]
    public void InvariantHoldsForRandomAmounts()
    {
        // The return is built if and only if 0 < amount <= original - prior.
        var random = new Random(15075);
        Pacs008Document original = TestData.Original();
        long paid = original.Transactions[0].Amount.Cents;
        for (int i = 0; i < 300; i++)
        {
            long amount = random.Next(-100, 20000);
            long prior = random.Next(-100, 20000);
            bool ok = amount > 0 && prior >= 0 && prior <= paid && amount <= paid - prior;
            bool built;
            try
            {
                Pacs004.FromPacs008(original, Pacs004.ReasonBankError, new BrlAmount(amount), new Pacs004Options { At = At, PriorReturns = new BrlAmount(prior) });
                built = true;
            }
            catch (MessageBuildException)
            {
                built = false;
            }

            Assert.True(ok == built, "amount=" + amount + " prior=" + prior);
        }
    }

    [Fact]
    public void Refusals()
    {
        Pacs008Document orig = TestData.Original();
        Assert.Equal(MessageBuildErrorKind.InvalidAmount, Build(orig, Pacs004.ReasonBankError, 0, null).Kind);
        Assert.Equal(MessageBuildErrorKind.InvalidAmount, Build(orig, Pacs004.ReasonBankError, 1, new Pacs004Options { PriorReturns = new BrlAmount(long.MinValue + 1) }).Kind);
        Assert.Equal(MessageBuildErrorKind.UnknownReason, Build(orig, "XX01", 1, null).Kind);
        Pacs004.FromPacs008(orig, "XX01", new BrlAmount(1), new Pacs004Options { AllowUnknownReason = true });
        Assert.Equal(MessageBuildErrorKind.TransactionNotFound, Build(null, Pacs004.ReasonBankError, 1, null).Kind);
        Assert.Equal(MessageBuildErrorKind.TransactionNotFound, Build(new Pacs008Document(), Pacs004.ReasonBankError, 1, null).Kind);
        Pacs004.FromPacs008(orig, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { EndToEndId = "E99999999202611211030a1B2c3D4e5F" });
        MessageBuildException missing = Build(orig, Pacs004.ReasonBankError, 1, new Pacs004Options { EndToEndId = "nope" });
        Assert.Equal("pacs004: original transaction not found: EndToEndId \"nope\"", missing.Message);

        Pacs008Document bad = TestData.Original();
        bad.Transactions[0].CdtrAgt = Agent.FromIspb("x");
        Assert.Throws<InvalidIdentifierException>(() => Pacs004.FromPacs008(bad, Pacs004.ReasonBankError, new BrlAmount(1)));
        Assert.Throws<InvalidIdentifierException>(() => Pacs004.FromPacs008(bad, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { MsgId = "m" }));
        Assert.Throws<MessageValidationException>(() => Pacs004.FromPacs008(orig, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { AdditionalInfo = new[] { new string('x', 106) } }));

        using MemoryStream ones = TestData.Stream(Enumerable.Repeat((byte)1, 128).ToArray());
        Pacs004Document d = Pacs004.FromPacs008(orig, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { Random = ones });
        Assert.EndsWith("11111111111", d.Transactions[0].RtrId);

        static MessageBuildException Build(Pacs008Document? o, string reason, long cents, Pacs004Options? options) =>
            Assert.Throws<MessageBuildException>(() => Pacs004.FromPacs008(o, reason, new BrlAmount(cents), options));
    }

    [Fact]
    public void ValidationRules()
    {
        Pacs004Document Fresh() => Pacs004Document.Parse(TestData.Load("pacs004.xml"));

        Pacs004Document d = Fresh();
        d.Transactions[0].OrgnlIntrBkSttlmAmt = CurrencyAmount.InBrl(BrlAmount.Parse("40"));
        Assert.Contains(d.Validate(), v => v.ToString().EndsWith("RtrdIntrBkSttlmAmt: 50.00 exceeds OrgnlIntrBkSttlmAmt 40.00", StringComparison.Ordinal));

        d = Fresh();
        d.Transactions[0].RtrId = "E99999999202611211030a1B2c3D4e5F";
        Assert.Contains(d.Validate(), v => v.ToString().EndsWith("RtrId: must start with 'D'", StringComparison.Ordinal));

        d = Fresh();
        d.Transactions[0].RtrRsnInf.Clear();
        Assert.Contains(d.Validate(), v => v.ToString().EndsWith("RtrRsnInf: a return reason code is required", StringComparison.Ordinal));

        d = Fresh();
        d.PmtRtr.TxInf.Clear();
        Assert.Contains(d.Validate(), v => v.ToString().EndsWith("TxInf: at least one transaction is required", StringComparison.Ordinal));
        Assert.Equal("09", Fresh().Version);
        Assert.Equal(string.Empty, new ReturnTransaction().Reason);
    }

    [Fact]
    public void ReasonTable()
    {
        foreach (string code in new[] { Pacs004.ReasonBankError, Pacs004.ReasonFraud, Pacs004.ReasonCustomerRefund, Pacs004.ReasonCreditorService })
        {
            Assert.True(Pacs004.Reasons.TryLookup(code, out CodeInfo? info));
            Assert.NotEqual(string.Empty, info!.Portuguese);
        }
    }
}

public class Camt054Tests
{
    [Fact]
    public void ReadsEntries()
    {
        Camt054Document d = Camt054Document.Parse(TestData.Load("camt054.xml"));
        IReadOnlyList<Entry> entries = d.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("08", d.Version);
        BrlAmount net = entries.Aggregate(BrlAmount.Zero, (sum, e) => sum + e.Signed);
        Assert.Equal(BrlAmount.Parse("100.75"), net);
        Assert.Equal("E99999999202611211030a1B2c3D4e5F", Assert.Single(entries[0].EndToEndIds));
    }

    [Fact]
    public void PendingEntriesAndDetailsWithoutAmounts()
    {
        Camt054Document d = Camt054Document.Parse(TestData.Load("camt054.xml"));
        Entry e = d.BkToCstmrDbtCdtNtfctn.Ntfctn[0].Ntry[0];
        e.Sts = new CodeOrProprietary { Cd = Camt054.StatusPending };
        e.BookgDt = null;
        e.RvslInd = true;
        e.BkTxCd = new BankTransactionCode { Prtry = new ProprietaryBankTransactionCode { Cd = "PIX", Issr = "X" } };
        e.NtryDtls[0].TxDtls[0].Amt = null;
        Assert.Empty(d.Validate());
        Assert.Contains("<RvslInd>true</RvslInd>", d.ToXml());
        Assert.True(Camt054Document.Parse(d.ToXml()).BkToCstmrDbtCdtNtfctn.Ntfctn[0].Ntry[0].RvslInd);
    }

    [Fact]
    public void ValidationRules()
    {
        Camt054Document d = Camt054Document.Parse(TestData.Load("camt054.xml"));
        Entry e = d.BkToCstmrDbtCdtNtfctn.Ntfctn[0].Ntry[0];
        e.NtryDtls[0].TxDtls[0].Amt!.Value = new BrlAmount(1);
        e.CdtDbtInd = "CR";
        string all = string.Join("\n", d.Validate().Select(v => v.ToString()));
        Assert.Contains("Ntry[0]/Amt: is 150.75 but the transaction details add up to 0.01", all);
        Assert.Contains("Ntry[0]/CdtDbtInd: must be one of [CRDT DBIT], got \"CR\"", all);
    }

    [Theory]
    [InlineData("<RvslInd>1</RvslInd>", true)]
    [InlineData("<RvslInd> false </RvslInd>", false)]
    [InlineData("<RvslInd></RvslInd>", false)]
    public void ReadsBooleansLikeGo(string element, bool value)
    {
        string xml = TestData.Load("camt054.xml").Replace("<CdtDbtInd>CRDT</CdtDbtInd>\n        <Sts>", "<CdtDbtInd>CRDT</CdtDbtInd>" + element + "<Sts>", StringComparison.Ordinal);
        Assert.Equal(value, Camt054Document.Parse(xml).Entries[0].RvslInd);
    }

    [Fact]
    public void RejectsInvalidBooleans()
    {
        string xml = TestData.Load("camt054.xml").Replace("<CdtDbtInd>CRDT</CdtDbtInd>\n        <Sts>", "<CdtDbtInd>CRDT</CdtDbtInd><RvslInd>yes</RvslInd><Sts>", StringComparison.Ordinal);
        MessageParseException e = Assert.Throws<MessageParseException>(() => Camt054Document.Parse(xml));
        Assert.Equal(MessageParseErrorKind.InvalidBoolean, e.Kind);
        Assert.Equal("strconv.ParseBool: parsing \"yes\": invalid syntax", e.Message);
    }
}
