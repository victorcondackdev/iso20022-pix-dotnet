using System.Globalization;
using System.Text;
using Iso20022.Pix.Components;
using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Tests.Parity;

/// <summary>
/// Cross-language parity: every vector in <see cref="GoVectors"/> was produced
/// by running the Go implementation (github.com/victorcondackdev/iso20022-pix-go)
/// and the C# port must produce identical outputs: amounts, identifiers, error
/// and violation messages, and serialized XML (compared by SHA-256).
/// </summary>
public class ParityTests
{
    [Fact]
    public void AmountsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string result) in GoVectors.Amounts)
        {
            string got;
            try
            {
                got = "ok:" + BrlAmount.Parse(input).Cents.ToString(CultureInfo.InvariantCulture);
            }
            catch (InvalidAmountException e)
            {
                got = "err:" + e.Message;
            }

            Check(failures, input, result, got);
            Check(failures, input + " (TryParse)", result.StartsWith("ok:", StringComparison.Ordinal), BrlAmount.TryParse(input, out _));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void AmountFormattingMatchesGo()
    {
        var failures = new List<string>();
        foreach ((long cents, string text, string iso) in GoVectors.AmountFormats)
        {
            var a = new BrlAmount(cents);
            Check(failures, cents + " text", text, a.ToString());
            string got;
            try
            {
                got = a.ToIsoString();
            }
            catch (InvalidAmountException e)
            {
                got = "err:" + e.Message;
            }

            Check(failures, cents + " iso", iso, got);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DateTimesMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, bool isDateTime, bool isDate) in GoVectors.DateTimes)
        {
            Check(failures, input + " ISODateTime", isDateTime, IsoDateTime.IsValid(input));
            Check(failures, input + " ISODate", isDate, IsoDateTime.IsValidDate(input));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void PixIdsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string endToEnd, string ret) in GoVectors.PixIds)
        {
            Check(failures, input + " E", endToEnd, Describe(() => PixIdentifiers.ParseEndToEndId(input)));
            Check(failures, input + " D", ret, Describe(() => PixIdentifiers.ParseReturnId(input)));
            Check(failures, input + " TryE", endToEnd.StartsWith("ok:", StringComparison.Ordinal), PixIdentifiers.TryParseEndToEndId(input, out _));
        }

        Assert.Empty(failures);

        static string Describe(Func<PixId> parse)
        {
            try
            {
                PixId id = parse();
                return "ok:" + id.Ispb + "|" + id.Time.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + "|" + id.Sequence;
            }
            catch (InvalidIdentifierException e)
            {
                return "err:" + e.Message;
            }
        }
    }

    [Fact]
    public void GeneratedIdsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string kind, string ispb, long unix, string stream, string result) in GoVectors.GeneratedIds)
        {
            DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(unix);
            using MemoryStream random = TestData.Stream(Streams(stream));
            string got;
            try
            {
                got = kind switch
                {
                    "E" => PixIdentifiers.NewEndToEndId(ispb, at, random),
                    "D" => PixIdentifiers.NewReturnId(ispb, at, random),
                    _ => PixIdentifiers.NewMsgId(ispb, random),
                };
            }
            catch (Exception e) when (e is InvalidIdentifierException or InvalidOperationException)
            {
                got = "err:" + e.Message;
            }

            Check(failures, kind + " " + ispb + " " + unix + " " + stream, result, got);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void PixKeysMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string result) in GoVectors.PixKeys)
        {
            string got;
            try
            {
                got = PixKeys.DictName(PixKeys.Classify(input));
            }
            catch (InvalidIdentifierException e)
            {
                got = "err:" + e.Message;
            }

            Check(failures, input, result, got);
            Check(failures, input + " (TryClassify)", !result.StartsWith("err:", StringComparison.Ordinal), PixKeys.TryClassify(input, out _));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TaxIdsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, bool cpf, bool cnpj, bool ispb) in GoVectors.TaxIds)
        {
            Check(failures, input + " CPF", cpf, TaxIds.IsValidCpf(input));
            Check(failures, input + " CNPJ", cnpj, TaxIds.IsValidCnpj(input));
            Check(failures, input + " ISPB", ispb, PixIdentifiers.IsValidIspb(input));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void MsgIdsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string error) in GoVectors.MsgIds)
        {
            string got = string.Empty;
            try
            {
                PixIdentifiers.ValidateMsgId(input);
            }
            catch (InvalidIdentifierException e)
            {
                got = e.Message;
            }

            Check(failures, input, error, got);
            Check(failures, input + " (IsValidMsgId)", error.Length == 0, PixIdentifiers.IsValidMsgId(input));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DetectionMatchesGo()
    {
        var failures = new List<string>();
        foreach ((string input, string detect, string inspect, string violations) in GoVectors.Detection)
        {
            string label = Shorten(input);
            Check(failures, label + " Detect", detect, Capture(() => "ok:" + MessageDetector.Detect(input).ToString()));
            Check(failures, label + " Detect(bytes)", detect, Capture(() => "ok:" + MessageDetector.Detect(Encoding.UTF8.GetBytes(input)).ToString()));
            string gotViolations = string.Empty;
            string got = Capture(() =>
            {
                Envelope env = MessageDetector.Inspect(input);
                gotViolations = TestData.Join(env.Validate());
                return "ok:" + env.Type.ToString() + "|" + Header(env.Header);
            });
            Check(failures, label + " Inspect", inspect, got);
            Check(failures, label + " Inspect violations", violations, gotViolations);
            Check(failures, label + " Inspect(bytes)", inspect, Capture(() =>
            {
                using MemoryStream s = TestData.Stream(Encoding.UTF8.GetBytes(input));
                Envelope env = MessageDetector.Inspect(s);
                return "ok:" + env.Type.ToString() + "|" + Header(env.Header);
            }));
        }

        Assert.Empty(failures);

        static string Header(AppHdr? h) => h is null
            ? "nohdr"
            : string.Join("|", h.Namespace, h.Fr.Ispb, h.To.Ispb, h.BizMsgIdr, h.MsgDefIdr, h.BizSvc, h.CreDt);
    }

    [Fact]
    public void DocumentsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string message, string input, string result, string violations) in GoVectors.Documents)
        {
            string label = message + " " + Shorten(input);
            (string got, string gotViolations) = RunDocument(message, () => input);
            Check(failures, label, result, got);
            Check(failures, label + " violations", violations, gotViolations);

            (string gotBytes, _) = RunDocument(message, () => Encoding.UTF8.GetBytes(input));
            Check(failures, label + " (bytes)", result, gotBytes);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ModelMutationsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string message, string field, string value, string violations, string xml) in GoVectors.Models)
        {
            string label = message + " " + field + "=" + Escape(value);
            long cents = long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long c) ? c : 0;
            (IReadOnlyList<Violation> got, Func<string> toXml) = ApplyModel(message, field, value, cents);
            Check(failures, label + " violations", violations, TestData.Join(got));
            string gotXml;
            try
            {
                gotXml = TestData.Sha(toXml());
            }
            catch (InvalidAmountException e)
            {
                gotXml = "err:" + e.Message;
            }

            Check(failures, label + " xml", xml, gotXml);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void BuildersMatchGo()
    {
        var failures = new List<string>();
        foreach ((string name, string result) in GoVectors.Builders)
        {
            Check(failures, name, result, RunBuilder(name));
        }

        Assert.Empty(failures);
    }

    // ------------------------------------------------------------------ helpers

    private static byte[] Streams(string name) => name switch
    {
        "zeros256" => new byte[256],
        "p37x256" => TestData.Pattern(37, 256),
        "p5x64" => TestData.Pattern(5, 64),
        "p11x32" => TestData.Pattern(11, 32),
        "z61ff" => Enumerable.Repeat(new byte[] { 0, 61, 255 }, 20).SelectMany(b => b).ToArray(),
        "ffx4096" => Enumerable.Repeat((byte)255, 4096).ToArray(),
        "ffx2047+00" => Enumerable.Repeat((byte)255, 2047).Concat(new byte[64]).ToArray(),
        "short10" => TestData.Pattern(3, 10),
        "empty" => Array.Empty<byte>(),
        "x32ff" => Enumerable.Repeat((byte)250, 31).Append((byte)1).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown stream"),
    };

    private static string Capture(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (MessageParseException e)
        {
            return "err:" + Kind(e);
        }
    }

    private static string Kind(MessageParseException e) =>
        e.Kind == MessageParseErrorKind.Malformed ? "Malformed" : e.Kind.ToString() + ":" + e.Message;

    private static (string Result, string Violations) RunDocument(string message, Func<object> input)
    {
        try
        {
            object source = input();
            (IReadOnlyList<Violation> violations, Func<string> toXml) = message switch
            {
                "pacs.008" => Wrap(Parse<Pacs008Document>(source, Pacs008Document.Parse, Pacs008Document.Parse)),
                "pacs.002" => Wrap(Parse<Pacs002Document>(source, Pacs002Document.Parse, Pacs002Document.Parse)),
                "pacs.004" => Wrap(Parse<Pacs004Document>(source, Pacs004Document.Parse, Pacs004Document.Parse)),
                _ => Wrap(Parse<Camt054Document>(source, Camt054Document.Parse, Camt054Document.Parse)),
            };
            string joined = TestData.Join(violations);
            try
            {
                return ("ok:" + TestData.Sha(toXml()), joined);
            }
            catch (InvalidAmountException e)
            {
                return ("marshal-err:" + e.Message, joined);
            }
        }
        catch (MessageParseException e)
        {
            return ("err:" + Kind(e), string.Empty);
        }
    }

    private static T Parse<T>(object source, Func<string, T> fromString, Func<byte[], T> fromBytes) =>
        source is string s ? fromString(s) : fromBytes((byte[])source);

    private static (IReadOnlyList<Violation>, Func<string>) Wrap(object doc) => doc switch
    {
        Pacs008Document d => (d.Validate(), new Func<string>(d.ToXml)),
        Pacs002Document d => (d.Validate(), new Func<string>(d.ToXml)),
        Pacs004Document d => (d.Validate(), new Func<string>(d.ToXml)),
        Camt054Document d => (d.Validate(), new Func<string>(d.ToXml)),
        _ => throw new ArgumentException("unexpected document", nameof(doc)),
    };

    private static (IReadOnlyList<Violation>, Func<string>) ApplyModel(string message, string field, string v, long cents)
    {
        switch (message)
        {
            case "pacs.008":
            {
                Pacs008Document d = TestData.Original();
                CreditTransferTransaction t = d.Transactions[0];
                switch (field)
                {
                    case "Namespace": d.Namespace = v; break;
                    case "MsgId": d.FIToFICstmrCdtTrf.GrpHdr.MsgId = v; break;
                    case "CreDtTm": d.FIToFICstmrCdtTrf.GrpHdr.CreDtTm = v; break;
                    case "NbOfTxs": d.FIToFICstmrCdtTrf.GrpHdr.NbOfTxs = v; break;
                    case "SttlmMtd": d.FIToFICstmrCdtTrf.GrpHdr.SttlmInf.SttlmMtd = v; break;
                    case "EndToEndId": t.PmtId.EndToEndId = v; break;
                    case "TxId": t.PmtId.TxId = v; break;
                    case "InstrPrty": t.PmtTpInf!.InstrPrty = v; break;
                    case "LclInstrm.Cd": t.PmtTpInf!.LclInstrm!.Cd = v; break;
                    case "LclInstrm.Prtry": t.PmtTpInf!.LclInstrm!.Prtry = v; break;
                    case "Amount.Ccy": t.IntrBkSttlmAmt.Ccy = v; break;
                    case "Amount.Cents": t.IntrBkSttlmAmt.Value = new BrlAmount(cents); break;
                    case "AccptncDtTm": t.AccptncDtTm = v; break;
                    case "ChrgBr": t.ChrgBr = v; break;
                    case "Dbtr.Nm": t.Dbtr.Nm = v; break;
                    case "Dbtr.TaxId": t.Dbtr.Id!.PrvtId!.Othr[0].Id = v; break;
                    case "Cdtr.Nm": t.Cdtr.Nm = v; break;
                    case "Cdtr.TaxId": t.Cdtr.Id!.OrgId!.Othr[0].Id = v; break;
                    case "DbtrAcct.Number": t.DbtrAcct!.Id.Othr!.Id = v; break;
                    case "DbtrAcct.Branch": t.DbtrAcct!.Id.Othr!.Issr = v; break;
                    case "DbtrAcct.Tp": t.DbtrAcct!.Tp = CodeOrProprietary.Proprietary(v); break;
                    case "DbtrAcct.Ccy": t.DbtrAcct!.Ccy = v; break;
                    case "DbtrAcct.IBAN": t.DbtrAcct!.Id.IBAN = v; break;
                    case "CdtrAcct.Nm": t.CdtrAcct!.Nm = v; break;
                    case "CdtrAcct.PixKey": t.CdtrAcct!.Prxy!.Id = v; break;
                    case "DbtrAgt.Ispb": t.DbtrAgt.FinInstnId.ClrSysMmbId!.MmbId = v; break;
                    case "CdtrAgt.Ispb": t.CdtrAgt.FinInstnId.ClrSysMmbId!.MmbId = v; break;
                    case "Purp": t.Purp = CodeOrProprietary.Code(v); break;
                    case "Ustrd": t.RmtInf!.Ustrd[0] = v; break;
                    default: throw new ArgumentException("unknown field " + field, nameof(field));
                }

                return (d.Validate(), new Func<string>(d.ToXml));
            }

            case "pacs.002":
            {
                Pacs002Document d = Pacs002Document.Parse(TestData.Load("pacs002_reject.xml"));
                TransactionStatus t = d.Transactions[0];
                switch (field)
                {
                    case "MsgId": d.FIToFIPmtStsRpt.GrpHdr.MsgId = v; break;
                    case "OrgnlMsgNmId": d.FIToFIPmtStsRpt.OrgnlGrpInfAndSts[0].OrgnlMsgNmId = v; break;
                    case "GrpSts": d.FIToFIPmtStsRpt.OrgnlGrpInfAndSts[0].GrpSts = v; break;
                    case "OrgnlEndToEndId": t.OrgnlEndToEndId = v; break;
                    case "TxSts": t.TxSts = v; break;
                    case "Rsn": t.StsRsnInf[0].Rsn!.Cd = v; break;
                    case "AddtlInf": t.StsRsnInf[0].AddtlInf[0] = v; break;
                    case "FctvDt": t.FctvIntrBkSttlmDt = new DateAndDateTime { Dt = v }; break;
                    case "FctvDtTm": t.FctvIntrBkSttlmDt = new DateAndDateTime { DtTm = v }; break;
                    default: throw new ArgumentException("unknown field " + field, nameof(field));
                }

                return (d.Validate(), new Func<string>(d.ToXml));
            }

            case "pacs.004":
            {
                Pacs004Document d = Pacs004Document.Parse(TestData.Load("pacs004.xml"));
                ReturnTransaction t = d.Transactions[0];
                switch (field)
                {
                    case "RtrId": t.RtrId = v; break;
                    case "OrgnlEndToEndId": t.OrgnlEndToEndId = v; break;
                    case "OrgnlMsgId": t.OrgnlGrpInf!.OrgnlMsgId = v; break;
                    case "Rtrd.Cents": t.RtrdIntrBkSttlmAmt.Value = new BrlAmount(cents); break;
                    case "Orgnl.Cents": t.OrgnlIntrBkSttlmAmt = CurrencyAmount.InBrl(new BrlAmount(cents)); break;
                    case "ChrgBr": t.ChrgBr = v; break;
                    case "Rsn": t.RtrRsnInf[0].Rsn!.Cd = v; break;
                    case "AddtlInf": t.RtrRsnInf[0].AddtlInf[0] = v; break;
                    case "NbOfTxs": d.PmtRtr.GrpHdr.NbOfTxs = v; break;
                    default: throw new ArgumentException("unknown field " + field, nameof(field));
                }

                return (d.Validate(), new Func<string>(d.ToXml));
            }

            case "camt.054":
            {
                Camt054Document d = Camt054Document.Parse(TestData.Load("camt054.xml"));
                AccountNotification n = d.BkToCstmrDbtCdtNtfctn.Ntfctn[0];
                Entry e0 = n.Ntry[0];
                switch (field)
                {
                    case "Ntfctn.Id": n.Id = v; break;
                    case "Acct.Number": n.Acct.Id.Othr!.Id = v; break;
                    case "Ntry.Amt.Cents": e0.Amt.Value = new BrlAmount(cents); break;
                    case "Tx.Amt.Cents": e0.NtryDtls[0].TxDtls[0].Amt!.Value = new BrlAmount(cents); break;
                    case "CdtDbtInd": e0.CdtDbtInd = v; break;
                    case "Sts.Cd": e0.Sts.Cd = v; break;
                    case "Refs.EndToEndId": e0.NtryDtls[0].TxDtls[0].Refs!.EndToEndId = v; break;
                    case "AddtlTxInf": n.Ntry[1].NtryDtls[0].TxDtls[0].AddtlTxInf = v; break;
                    case "BookgDt.Dt": e0.BookgDt = new DateAndDateTime { Dt = v }; break;
                    case "Domn.Cd": e0.BkTxCd.Domn!.Cd = v; break;
                    default: throw new ArgumentException("unknown field " + field, nameof(field));
                }

                return (d.Validate(), new Func<string>(d.ToXml));
            }

            default:
            {
                Envelope env = MessageDetector.Inspect(TestData.Load("envelope_pacs008.xml"));
                AppHdr h = env.Header!;
                switch (field)
                {
                    case "Fr.Ispb": h.Fr.FIId!.FinInstnId.ClrSysMmbId!.MmbId = v; break;
                    case "MsgDefIdr": h.MsgDefIdr = v; break;
                    case "BizMsgIdr": h.BizMsgIdr = v; break;
                    case "BizSvc": h.BizSvc = v; break;
                    case "CreDt": h.CreDt = v; break;
                    default: throw new ArgumentException("unknown field " + field, nameof(field));
                }

                return (env.Validate(), new Func<string>(h.ToXml));
            }
        }
    }

    private static string RunBuilder(string name)
    {
        try
        {
            object doc = BuildScenario(name);
            return "xml:" + TestData.Sha(ToXml(doc));
        }
        catch (MessageValidationException e)
        {
            return "invalid:" + TestData.Sha(ToXml(e.Document)) + "|" + TestData.Join(e.Violations);
        }
        catch (Exception e) when (e is MessageBuildException or InvalidIdentifierException or InvalidOperationException)
        {
            return "err:" + e.Message;
        }
    }

    private static string ToXml(object doc) => doc switch
    {
        Pacs008Document d => d.ToXml(),
        Pacs002Document d => d.ToXml(),
        Pacs004Document d => d.ToXml(),
        _ => throw new ArgumentException("unexpected document", nameof(doc)),
    };

    private static object BuildScenario(string name)
    {
        DateTimeOffset created = TestData.Created;
        var at2 = new DateTimeOffset(2026, 11, 21, 10, 30, 1, 456, TimeSpan.Zero);
        var at4 = new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
        Transfer sample = TestData.SampleTransfer();
        const string goldenE2E = "E99999999202611211030a1B2c3D4e5F";
        string longInfo = new('x', 106);

        switch (name)
        {
            case "p008.generated":
                return new Pacs008Builder(created).WithRandom(TestData.Stream(TestData.Pattern(37, 256))).Add(sample).Build();
            case "p008.two":
                Transfer second = sample with
                {
                    Amount = BrlAmount.Parse("0.01"),
                    Creditor = new TransferParty { Name = "Beltrano", TaxId = "11144477735" },
                    Initiation = string.Empty,
                    Priority = "HIGH",
                    TxId = "qr-txid-42",
                    AcceptedAt = null,
                };
                return new Pacs008Builder(created).WithRandom(TestData.Stream(TestData.Pattern(13, 512))).Add(sample).Add(second).Build();
            case "p008.explicit":
                return new Pacs008Builder(created)
                    .WithMsgId("M9999999920261121103000000000001")
                    .Add(sample with { EndToEndId = goldenE2E, AcceptedAt = null, Priority = "HIGH", Initiation = string.Empty })
                    .Build();
            case "p008.golden":
                return GoldenPacs008();
            case "p008.invalid":
                return new Pacs008Builder(created)
                    .WithRandom(TestData.Stream(TestData.Pattern(5, 256)))
                    .Add(sample with
                    {
                        Amount = BrlAmount.Zero,
                        Debtor = sample.Debtor with { TaxId = "52998224724" },
                        CreditorAccount = sample.CreditorAccount with { Ispb = "1234" },
                        PixKey = "not a key",
                    })
                    .Build();
            case "p008.noispb":
                return new Pacs008Builder(created).Add(sample with { DebtorAccount = sample.DebtorAccount with { Ispb = string.Empty } }).Build();
            case "p008.empty":
                return new Pacs008Builder(created).Build();
            case "p008.exhausted":
                return new Pacs008Builder(created).WithRandom(TestData.Stream(Enumerable.Repeat((byte)255, 4096).ToArray())).Add(sample).Build();
            case "p008.eof":
                return new Pacs008Builder(created).WithMsgId("m").WithRandom(TestData.Stream(Array.Empty<byte>())).Add(sample).Build();

            case "p002.accept.golden":
                return Pacs002.Accept(TestData.Original(), new Pacs002Options
                {
                    At = at2,
                    SettledAt = at2.AddMilliseconds(333),
                    MsgId = "M8888888820261121103000000000007",
                });
            case "p002.reject.golden":
                return Pacs002.Reject(TestData.Original(), "AC03", new Pacs002Options
                {
                    At = at2,
                    MsgId = "M8888888820261121103000000000008",
                    AdditionalInfo = new[] { "Conta do recebedor inexistente" },
                });
            case "p002.reject.generated":
                return Pacs002.Reject(TestData.Original(), "AM04", new Pacs002Options { At = at2, Random = TestData.Stream(TestData.Pattern(11, 128)) });
            case "p002.accept.ispb":
                return Pacs002.Accept(TestData.Original(), new Pacs002Options { At = at2, Ispb = "77777777", Random = TestData.Stream(TestData.Pattern(3, 128)) });
            case "p002.unknown":
                return Pacs002.Reject(TestData.Original(), "ZZ99", new Pacs002Options { At = at2 });
            case "p002.allowunknown":
                return Pacs002.Reject(TestData.Original(), "ZZ99", new Pacs002Options { AllowUnknownReason = true, At = at2, Random = TestData.Stream(new byte[64]) });
            case "p002.empty_reason":
                return Pacs002.Reject(TestData.Original(), string.Empty, new Pacs002Options { At = at2 });
            case "p002.toolong":
                return Pacs002.Reject(TestData.Original(), "TOOLONG", new Pacs002Options { AllowUnknownReason = true, At = at2, MsgId = "m" });
            case "p002.no_original":
                return Pacs002.Accept(new Pacs008Document(), new Pacs002Options { At = at2 });
            case "p002.bad_ispb":
                return Pacs002.Accept(TestData.Original(), new Pacs002Options { Ispb = "x", At = at2 });
            case "p002.long_info":
                return Pacs002.Reject(TestData.Original(), "AC03", new Pacs002Options { At = at2, MsgId = "m", AdditionalInfo = new[] { longInfo } });

            case "p004.golden":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonCustomerRefund, BrlAmount.Parse("50.00"), new Pacs004Options
                {
                    At = at4,
                    MsgId = "M8888888820261121150000000000003",
                    ReturnId = "D88888888202611211500Zx9Yw8Vu7Ts",
                    AdditionalInfo = new[] { "Devolucao parcial a pedido do recebedor" },
                });
            case "p004.generated":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonFraud, BrlAmount.Parse("150.75"), new Pacs004Options
                {
                    At = at4,
                    Random = TestData.Stream(TestData.Pattern(29, 256)),
                });
            case "p004.over":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse("150.76"), new Pacs004Options { At = at4 });
            case "p004.prior":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse("100.75"), new Pacs004Options
                {
                    At = at4,
                    PriorReturns = BrlAmount.Parse("50"),
                    Random = TestData.Stream(TestData.Pattern(7, 256)),
                });
            case "p004.prior_over":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse("100.76"), new Pacs004Options
                {
                    At = at4,
                    PriorReturns = BrlAmount.Parse("50"),
                });
            case "p004.prior_above":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse("0.01"), new Pacs004Options
                {
                    At = at4,
                    PriorReturns = BrlAmount.Parse("200"),
                });
            case "p004.negative_prior":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Parse("0.01"), new Pacs004Options
                {
                    At = at4,
                    PriorReturns = new BrlAmount(long.MinValue + 1),
                });
            case "p004.zero":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, BrlAmount.Zero, new Pacs004Options { At = at4 });
            case "p004.unknown":
                return Pacs004.FromPacs008(TestData.Original(), "XX01", new BrlAmount(1), new Pacs004Options { At = at4 });
            case "p004.allowunknown":
                return Pacs004.FromPacs008(TestData.Original(), "XX01", new BrlAmount(1), new Pacs004Options
                {
                    AllowUnknownReason = true,
                    At = at4,
                    Random = TestData.Stream(TestData.Pattern(17, 256)),
                });
            case "p004.e2e_missing":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { EndToEndId = "nope", At = at4 });
            case "p004.e2e":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options
                {
                    EndToEndId = goldenE2E,
                    At = at4,
                    Random = TestData.Stream(TestData.Pattern(19, 256)),
                });
            case "p004.nil":
                return Pacs004.FromPacs008(null, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { At = at4 });
            case "p004.empty":
                return Pacs004.FromPacs008(new Pacs008Document(), Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { At = at4 });
            case "p004.bad_ispb":
            {
                Pacs008Document d = TestData.Original();
                d.Transactions[0].CdtrAgt = Agent.FromIspb("x");
                return Pacs004.FromPacs008(d, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { At = at4 });
            }

            case "p004.bad_ispb_rtr":
            {
                Pacs008Document d = TestData.Original();
                d.Transactions[0].CdtrAgt = Agent.FromIspb("x");
                return Pacs004.FromPacs008(d, Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options { At = at4, MsgId = "m" });
            }

            case "p004.long_info":
                return Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonBankError, new BrlAmount(1), new Pacs004Options
                {
                    At = at4,
                    MsgId = "m",
                    ReturnId = "D88888888202611211500Zx9Yw8Vu7Ts",
                    AdditionalInfo = new[] { longInfo },
                });
            default:
                throw new ArgumentException("unknown scenario " + name, nameof(name));
        }
    }

    internal static Pacs008Document GoldenPacs008() =>
        new Pacs008Builder(new DateTimeOffset(2026, 11, 21, 10, 30, 1, 123, TimeSpan.Zero))
            .WithMsgId("M9999999920261121103000000000001")
            .Add(new Transfer
            {
                EndToEndId = "E99999999202611211030a1B2c3D4e5F",
                Amount = BrlAmount.Parse("150.75"),
                AcceptedAt = new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero),
                Initiation = Pacs008.InitiationDict,
                Debtor = new TransferParty { Name = "Fulano de Tal", TaxId = "52998224725" },
                DebtorAccount = new TransferAccount { Ispb = "99999999", Branch = "0001", Number = "123456", Type = AccountTypes.Current },
                Creditor = new TransferParty { Name = "Loja Exemplo Ltda", TaxId = "11222333000181" },
                CreditorAccount = new TransferAccount { Ispb = "88888888", Branch = "0001", Number = "6543210", Type = AccountTypes.Current },
                PixKey = "pagamentos@example.com",
                RemittanceInfo = "Pedido 4521",
            })
            .Build();

    private static void Check(List<string> failures, string label, string want, string got)
    {
        if (!string.Equals(want, got, StringComparison.Ordinal))
        {
            failures.Add(Escape(label) + "\n    want: " + Escape(want) + "\n    got:  " + Escape(got));
        }
    }

    private static void Check(List<string> failures, string label, bool want, bool got)
    {
        if (want != got)
        {
            failures.Add(Escape(label) + ": want " + want + ", got " + got);
        }
    }

    private static string Shorten(string s) => s.Length <= 160 ? s : s.Substring(0, 160) + "...";

    // Makes failures readable: control and non-ASCII characters are shown as \uXXXX.
    private static string Escape(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= 0x20 && c < 0x7F)
            {
                b.Append(c);
            }
            else
            {
                b.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return b.ToString();
    }
}
