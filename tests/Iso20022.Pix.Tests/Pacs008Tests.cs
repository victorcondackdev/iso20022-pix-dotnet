using Iso20022.Pix.Components;

namespace Iso20022.Pix.Tests;

public class Pacs008Tests
{
    private const string Root = "/Document/FIToFICstmrCdtTrf";

    [Fact]
    public void ReadsTheGoldenMessage()
    {
        Pacs008Document d = TestData.Original();
        CreditTransferTransaction tx = d.Transactions[0];
        Assert.Equal("E99999999202611211030a1B2c3D4e5F", tx.PmtId.EndToEndId);
        Assert.Equal(BrlAmount.Parse("150.75"), tx.Amount);
        Assert.Equal("52998224725", tx.Dbtr.TaxId);
        Assert.Equal("11222333000181", tx.Cdtr.TaxId);
        Assert.Equal("99999999", tx.DbtrAgt.Ispb);
        Assert.Equal("88888888", tx.CdtrAgt.Ispb);
        Assert.Equal("pagamentos@example.com", tx.CdtrAcct!.PixKey);
        Assert.Equal("0001", tx.DbtrAcct!.Branch);
        Assert.Equal("123456", tx.DbtrAcct!.Number);
        Assert.Equal(Pacs008.InitiationDict, tx.Initiation);
        Assert.Equal("Pedido 4521", tx.RemittanceInfo);
        Assert.Equal("08", d.Version);
        Assert.Equal("pacs.008.001.08", d.MessageName);
        Assert.Equal("150.75 BRL", tx.IntrBkSttlmAmt.ToString());
    }

    [Fact]
    public void ParsesOtherVersions()
    {
        string data = TestData.Load("pacs008.xml").Replace("pacs.008.001.08", "pacs.008.001.10", StringComparison.Ordinal);
        Assert.Equal("10", Pacs008Document.Parse(data).Version);
    }

    [Theory]
    [InlineData("<Foo/>", MessageParseErrorKind.NoDocument)]
    [InlineData("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10\"/>", MessageParseErrorKind.UnexpectedMessage)]
    [InlineData("<Document xmlns=\"urn:example\"/>", MessageParseErrorKind.UnknownNamespace)]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"b\">]><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/>", MessageParseErrorKind.DirectiveForbidden)]
    [InlineData("<E><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/></E>", MessageParseErrorKind.MultipleDocuments)]
    [InlineData("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"><FIToFICstmrCdtTrf><CdtTrfTxInf><IntrBkSttlmAmt Ccy=\"BRL\">1.234</IntrBkSttlmAmt></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>", MessageParseErrorKind.InvalidAmount)]
    [InlineData("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\">", MessageParseErrorKind.Malformed)]
    [InlineData("<E><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/><x></E>", MessageParseErrorKind.Malformed)]
    public void ParseErrors(string input, MessageParseErrorKind kind)
    {
        Assert.Equal(kind, Assert.Throws<MessageParseException>(() => Pacs008Document.Parse(input)).Kind);
    }

    [Fact]
    public void BuilderGeneratesIdentifiersAndMaps()
    {
        Pacs008Document d = new Pacs008Builder(TestData.Created).Add(TestData.SampleTransfer()).Build();
        GroupHeader h = d.FIToFICstmrCdtTrf.GrpHdr;
        Assert.Equal(32, h.MsgId.Length);
        Assert.Equal("1", h.NbOfTxs);
        Assert.Equal("2026-11-21T10:30:01.000Z", h.CreDtTm);
        CreditTransferTransaction tx = d.Transactions[0];
        Assert.StartsWith("E99999999202611211030", tx.PmtId.EndToEndId);
        Assert.Equal("IPAY", tx.Purp!.Value);
        Assert.Equal("+5521987654321", tx.CdtrAcct!.PixKey);
        Assert.NotNull(tx.Dbtr.Id!.PrvtId);
        Assert.NotNull(tx.Cdtr.Id!.OrgId);
    }

    [Fact]
    public void BuilderKeepsExplicitValues()
    {
        Transfer tr = TestData.SampleTransfer() with
        {
            EndToEndId = "E99999999202611211030a1B2c3D4e5F",
            AcceptedAt = null,
            Priority = "HIGH",
            Initiation = string.Empty,
        };
        Pacs008Document d = new Pacs008Builder(TestData.Created).WithMsgId("custom").Add(tr).Build();
        Assert.Equal("custom", d.FIToFICstmrCdtTrf.GrpHdr.MsgId);
        Assert.Equal(IsoDateTime.Format(TestData.Created), d.Transactions[0].AccptncDtTm);
        Assert.Null(d.Transactions[0].PmtTpInf!.LclInstrm);
        Assert.Equal(MessageBuildErrorKind.NoTransactions, Assert.Throws<MessageBuildException>(() => new Pacs008Builder(TestData.Created).Build()).Kind);
    }

    [Fact]
    public void DeterministicRandomnessGivesDeterministicIds()
    {
        string Build()
        {
            using MemoryStream zeros = TestData.Stream(new byte[256]);
            return new Pacs008Builder(TestData.Created).WithRandom(zeros).Add(TestData.SampleTransfer()).Build().ToXml();
        }

        string first = Build();
        Assert.Equal(first, Build());
        Assert.Contains("<EndToEndId>E9999999920261121103000000000000</EndToEndId>", first);
    }

    [Fact]
    public void BuilderReportsEveryViolation()
    {
        Transfer tr = TestData.SampleTransfer() with
        {
            Amount = BrlAmount.Zero,
            Debtor = new TransferParty { Name = "Fulano de Tal", TaxId = "52998224724" },
            CreditorAccount = TestData.SampleTransfer().CreditorAccount with { Ispb = "1234" },
            PixKey = "not a key",
        };
        MessageValidationException e = Assert.Throws<MessageValidationException>(() => new Pacs008Builder(DateTimeOffset.UtcNow).Add(tr).Build());
        Assert.IsType<Pacs008Document>(e.Document);
        string[] want =
        {
            Root + "/CdtTrfTxInf[0]/IntrBkSttlmAmt: must be greater than zero",
            Root + "/CdtTrfTxInf[0]/Dbtr/Id/PrvtId/Othr[0]/Id: must be a valid CPF, got \"52998224724\"",
            Root + "/CdtTrfTxInf[0]/CdtrAgt/FinInstnId/ClrSysMmbId/MmbId: must be an 8-digit ISPB, got \"1234\"",
            Root + "/CdtTrfTxInf[0]/CdtrAcct/Prxy/Id: \"not a key\" is not a CPF, CNPJ, e-mail, phone or EVP key",
        };
        Assert.Equal(want, e.Violations.Select(v => v.ToString()).ToArray());
        Assert.StartsWith("4 violations:\n\t", e.Message);
    }

    [Fact]
    public void ValidatesAnEmptyDocument()
    {
        string all = string.Join("\n", new Pacs008Document().Validate().Select(v => v.ToString()));
        foreach (string fragment in new[]
        {
            "GrpHdr/MsgId: is required",
            "GrpHdr/CreDtTm: is required",
            "GrpHdr/NbOfTxs: is required",
            "SttlmMtd: is required",
            "CdtTrfTxInf: at least one transaction is required",
        })
        {
            Assert.Contains(fragment, all);
        }
    }

    public static TheoryData<string, string> Rules => new()
    {
        { "namespace", "/Document/@xmlns: must be a pacs.008 namespace" },
        { "msgid", "MsgId: must be at most 35 characters, got 36" },
        { "date", "CreDtTm: must be an ISO 8601 date and time" },
        { "nboftxs", "NbOfTxs: is 2 but the message has 1 transactions" },
        { "nboftxs-sign", "must be a number of up to 15 digits" },
        { "sttlmmtd", "SttlmMtd: must be one of [CLRG], got \"INDA\"" },
        { "e2e", "EndToEndId: must be 32 characters, got 4" },
        { "ccy", "IntrBkSttlmAmt/@Ccy: must be \"BRL\", got \"USD\"" },
        { "negative", "IntrBkSttlmAmt: must not be negative" },
        { "huge", "IntrBkSttlmAmt: exceeds 18 digits" },
        { "both-ids", "Dbtr/Id: must contain exactly one of OrgId or PrvtId, got both" },
        { "empty-id", "Dbtr/Id: must contain OrgId or PrvtId" },
        { "no-id", "Cdtr/Id: is required (CPF or CNPJ)" },
        { "second-othr", "Othr[1]/SchmeNm: must contain exactly one of Cd or Prtry" },
        { "control", "Cdtr/Nm: contains U+0000, which XML 1.0 does not allow" },
        { "surrogate", "Cdtr/Nm: is not valid UTF-8" },
        { "account-type", "DbtrAcct/Tp/Prtry: must be one of [CACC SVGS SLRY TRAN]" },
        { "iban-and-othr", "DbtrAcct/Id: must contain exactly one of IBAN or Othr, got both" },
        { "no-agent", "DbtrAgt/FinInstnId/ClrSysMmbId/MmbId: is required (ISPB)" },
        { "lclinstrm", "PmtTpInf/LclInstrm: must contain exactly one of Cd or Prtry, got both" },
        { "ustrd", "RmtInf/Ustrd[0]: must be at most 140" },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void ValidationRules(string rule, string expected)
    {
        Pacs008Document d = TestData.Original();
        CreditTransferTransaction tx = d.Transactions[0];
        switch (rule)
        {
            case "namespace": d.Namespace = "urn:x"; break;
            case "msgid": d.FIToFICstmrCdtTrf.GrpHdr.MsgId = new string('M', 36); break;
            case "date": d.FIToFICstmrCdtTrf.GrpHdr.CreDtTm = "21/11/2026"; break;
            case "nboftxs": d.FIToFICstmrCdtTrf.GrpHdr.NbOfTxs = "2"; break;
            case "nboftxs-sign": d.FIToFICstmrCdtTrf.GrpHdr.NbOfTxs = "+1"; break;
            case "sttlmmtd": d.FIToFICstmrCdtTrf.GrpHdr.SttlmInf.SttlmMtd = "INDA"; break;
            case "e2e": tx.PmtId.EndToEndId = "E123"; break;
            case "ccy": tx.IntrBkSttlmAmt.Ccy = "USD"; break;
            case "negative": tx.IntrBkSttlmAmt.Value = new BrlAmount(-1); break;
            case "huge": tx.IntrBkSttlmAmt.Value = BrlAmount.MaxValue + new BrlAmount(1); break;
            case "both-ids": tx.Dbtr.Id!.OrgId = tx.Cdtr.Id!.OrgId; break;
            case "empty-id": tx.Dbtr.Id = new PartyId(); break;
            case "no-id": tx.Cdtr.Id = null; break;
            case "second-othr": tx.Cdtr.Id!.OrgId!.Othr.Add(new GenericId { SchmeNm = new CodeOrProprietary() }); break;
            case "control": tx.Cdtr.Nm = "a" + (char)0 + "b"; break;
            case "surrogate": tx.Cdtr.Nm = ((char)0xD800).ToString(); break;
            case "account-type": tx.DbtrAcct!.Tp = CodeOrProprietary.Proprietary("XXXX"); break;
            case "iban-and-othr": tx.DbtrAcct!.Id.IBAN = "BR00"; break;
            case "no-agent": tx.DbtrAgt = new Agent(); break;
            case "lclinstrm": tx.PmtTpInf!.LclInstrm!.Cd = "X"; break;
            case "ustrd": tx.RmtInf!.Ustrd[0] = new string('x', 141); break;
            default: throw new ArgumentException(rule, nameof(rule));
        }

        Assert.Contains(d.Validate(), v => v.ToString().Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void SerializesWithTheDefaultNamespace()
    {
        var d = new Pacs008Document();
        Assert.Contains("<Document xmlns=\"" + Pacs008.DefaultNamespace + "\">", d.ToXml());
        Assert.Equal(string.Empty, d.Namespace);
        Assert.Equal(string.Empty, d.Version);
        Assert.Equal("pacs.008.001.08", d.MessageName);
        d.FIToFICstmrCdtTrf.CdtTrfTxInf.Add(new CreditTransferTransaction { IntrBkSttlmAmt = CurrencyAmount.InBrl(new BrlAmount(-1)) });
        Assert.Throws<InvalidAmountException>(() => d.ToXml());
    }

    [Fact]
    public void EscapesLikeGo()
    {
        Pacs008Document d = TestData.Original();
        d.Transactions[0].Cdtr.Nm = "A&B <\"x\"> 'y'\t\n\r" + (char)1;
        string xml = d.ToXml();
        Assert.Contains("<Nm>A&amp;B &lt;&#34;x&#34;&gt; &#39;y&#39;&#x9;&#xA;&#xD;" + (char)0xFFFD + "</Nm>", xml);
        Pacs008Document back = Pacs008Document.Parse(xml);
        Assert.Equal("A&B <\"x\"> 'y'\t\n\r" + (char)0xFFFD, back.Transactions[0].Cdtr.Nm);
    }
}
