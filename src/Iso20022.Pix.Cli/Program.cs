using System.Globalization;
using Iso20022.Pix.Components;

namespace Iso20022.Pix.Cli;

/// <summary>
/// Command iso20022 inspects, validates and builds returns for Pix ISO 20022 messages.
/// <code>
/// iso20022 inspect  FILE            detect the message and print key fields
/// iso20022 validate FILE            validate and list every violation
/// iso20022 return [flags] FILE      build a pacs.004 return for a pacs.008
/// </code>
/// FILE may be "-" for standard input. Exit status is 0 on success, 1 when a
/// message is invalid and 2 on usage or I/O errors.
/// </summary>
internal static class Program
{
    /// <summary>The maximum size of a message read by the tool.</summary>
    internal const int MaxInput = 16 << 20;

    internal const string Usage =
        "usage:\n"
        + "  iso20022 inspect  FILE\n"
        + "  iso20022 validate FILE\n"
        + "  iso20022 return -reason CODE [-amount VALUE] [-prior VALUE] [-info TEXT] [-e2e ID] FILE\n";

    public static int Main(string[] args)
    {
        using Stream stdin = Console.OpenStandardInput();
        return Run(args, stdin, Console.Out, Console.Error);
    }

    internal static int Run(string[] args, Stream stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.Write(Usage);
            return 2;
        }

        string[] rest = args.Skip(1).ToArray();
        switch (args[0])
        {
            case "inspect":
                return WithFile(rest, stdin, stderr, data => InspectCommand(data, stdout, stderr));
            case "validate":
                return WithFile(rest, stdin, stderr, data => ValidateCommand(data, stdout, stderr));
            case "return":
                return ReturnCommand(rest, stdin, stdout, stderr);
            case "help" or "-h" or "-help" or "--help":
                stdout.Write(Usage);
                return 0;
        }

        stderr.Write("iso20022: unknown command \"" + args[0] + "\"\n" + Usage);
        return 2;
    }

    internal static byte[] ReadInput(string name, Stream stdin)
    {
        Stream input = name == "-" ? stdin : File.OpenRead(name);
        try
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxInput)
                {
                    throw new IOException("input larger than " + MaxInput.ToString(CultureInfo.InvariantCulture) + " bytes");
                }
            }

            return buffer.ToArray();
        }
        finally
        {
            if (!ReferenceEquals(input, stdin))
            {
                input.Dispose();
            }
        }
    }

    private static int WithFile(string[] args, Stream stdin, TextWriter stderr, Func<byte[], int> command)
    {
        if (args.Length != 1)
        {
            stderr.Write(Usage);
            return 2;
        }

        byte[] data;
        try
        {
            data = ReadInput(args[0], stdin);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 2;
        }

        return command(data);
    }

    // The parsed message: the envelope, the document and its violations.
    private sealed record Parsed(Envelope Envelope, object Document, IReadOnlyList<Violation> Violations);

    private static Parsed Parse(byte[] data)
    {
        Envelope env = MessageDetector.Inspect(data);
        switch (env.Type.Id)
        {
            case MessageIds.Pacs008:
                Pacs008Document p8 = Pacs008Document.Parse(data);
                return new Parsed(env, p8, p8.Validate());
            case MessageIds.Pacs002:
                Pacs002Document p2 = Pacs002Document.Parse(data);
                return new Parsed(env, p2, p2.Validate());
            case MessageIds.Pacs004:
                Pacs004Document p4 = Pacs004Document.Parse(data);
                return new Parsed(env, p4, p4.Validate());
            case MessageIds.Camt054:
                Camt054Document c54 = Camt054Document.Parse(data);
                return new Parsed(env, c54, c54.Validate());
            default:
                throw new NotSupportedException(env.Type.ToString() + " is not supported");
        }
    }

    private static int InspectCommand(byte[] data, TextWriter stdout, TextWriter stderr)
    {
        Parsed parsed;
        try
        {
            parsed = Parse(data);
        }
        catch (Exception e) when (e is Iso20022Exception or NotSupportedException)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 1;
        }

        var w = new Printer(stdout);
        Envelope env = parsed.Envelope;
        w.Kv("Message", env.Type.ToString() + " (" + env.Type.Name + ")");
        if (env.Header is AppHdr h)
        {
            w.Kv("AppHdr", h.Fr.Ispb + " -> " + h.To.Ispb + "  BizMsgIdr=" + h.BizMsgIdr + "  MsgDefIdr=" + h.MsgDefIdr + "  CreDt=" + h.CreDt);
        }

        switch (parsed.Document)
        {
            case Pacs008Document d:
                PrintPacs008(w, d);
                break;
            case Pacs002Document d:
                PrintPacs002(w, d);
                break;
            case Pacs004Document d:
                PrintPacs004(w, d);
                break;
            case Camt054Document d:
                PrintCamt054(w, d);
                break;
        }

        w.Kv("Valid", parsed.Violations.Count > 0 ? "no (run validate for details)" : "yes");
        return 0;
    }

    private static void PrintPacs008(Printer w, Pacs008Document d)
    {
        GroupHeader g = d.FIToFICstmrCdtTrf.GrpHdr;
        w.Kv("MsgId", g.MsgId);
        w.Kv("CreDtTm", g.CreDtTm);
        w.Kv("NbOfTxs", g.NbOfTxs);
        for (int i = 0; i < d.Transactions.Count; i++)
        {
            CreditTransferTransaction t = d.Transactions[i];
            w.Section("Transaction " + i.ToString(CultureInfo.InvariantCulture));
            w.Kv("EndToEndId", t.PmtId.EndToEndId);
            w.Opt("TxId", t.PmtId.TxId);
            w.Kv("Amount", t.IntrBkSttlmAmt.ToString());
            w.Kv("Accepted", t.AccptncDtTm);
            w.Opt("Initiation", t.Initiation);
            w.Kv("Debtor", PartyLine(t.Dbtr, t.DbtrAgt, t.DbtrAcct));
            w.Kv("Creditor", PartyLine(t.Cdtr, t.CdtrAgt, t.CdtrAcct));
            w.Opt("PixKey", t.CdtrAcct?.PixKey ?? string.Empty);
            w.Opt("Remittance", t.RemittanceInfo);
        }
    }

    private static string PartyLine(Party p, Agent agent, Account? account) =>
        p.Nm + " (" + p.TaxId + ") ISPB " + agent.Ispb + " ag " + (account?.Branch ?? string.Empty) + " cc " + (account?.Number ?? string.Empty);

    private static void PrintPacs002(Printer w, Pacs002Document d)
    {
        MessageHeader g = d.FIToFIPmtStsRpt.GrpHdr;
        w.Kv("MsgId", g.MsgId);
        w.Kv("CreDtTm", g.CreDtTm);
        foreach (OriginalGroupHeader o in d.FIToFIPmtStsRpt.OrgnlGrpInfAndSts)
        {
            w.Kv("Original", o.OrgnlMsgId + " (" + o.OrgnlMsgNmId + ")");
        }

        for (int i = 0; i < d.Transactions.Count; i++)
        {
            TransactionStatus t = d.Transactions[i];
            w.Section("Transaction " + i.ToString(CultureInfo.InvariantCulture));
            w.Kv("OrgnlEndToEndId", t.OrgnlEndToEndId);
            w.Kv("Status", t.TxSts);
            if (t.Reason.Length > 0)
            {
                w.Kv("Reason", Describe(Pacs002.Reasons, t.Reason));
            }

            foreach (StatusReasonInfo s in t.StsRsnInf)
            {
                foreach (string a in s.AddtlInf)
                {
                    w.Kv("AddtlInf", a);
                }
            }

            if (t.FctvIntrBkSttlmDt is DateAndDateTime settled)
            {
                w.Kv("Settled", settled.DtTm + settled.Dt);
            }
        }
    }

    private static void PrintPacs004(Printer w, Pacs004Document d)
    {
        GroupHeader g = d.PmtRtr.GrpHdr;
        w.Kv("MsgId", g.MsgId);
        w.Kv("CreDtTm", g.CreDtTm);
        for (int i = 0; i < d.Transactions.Count; i++)
        {
            ReturnTransaction t = d.Transactions[i];
            w.Section("Transaction " + i.ToString(CultureInfo.InvariantCulture));
            w.Kv("RtrId", t.RtrId);
            w.Kv("OrgnlEndToEndId", t.OrgnlEndToEndId);
            w.Kv("Returned", t.RtrdIntrBkSttlmAmt.ToString());
            w.Kv("Reason", Describe(Pacs004.Reasons, t.Reason));
        }
    }

    private static void PrintCamt054(Printer w, Camt054Document d)
    {
        MessageHeader g = d.BkToCstmrDbtCdtNtfctn.GrpHdr;
        w.Kv("MsgId", g.MsgId);
        w.Kv("CreDtTm", g.CreDtTm);
        foreach (AccountNotification n in d.BkToCstmrDbtCdtNtfctn.Ntfctn)
        {
            w.Section("Notification " + n.Id + ", account " + n.Acct.Branch + "/" + n.Acct.Number);
            foreach (Entry e in n.Ntry)
            {
                string booked = e.BookgDt is null ? string.Empty : e.BookgDt.DtTm + e.BookgDt.Dt;
                w.Kv(e.CdtDbtInd, e.Amt.ToString() + "  " + e.Sts.Value + "  " + booked + "  " + string.Join(",", e.EndToEndIds));
            }
        }
    }

    private static string Describe(CodeSet set, string code) =>
        set.TryLookup(code, out CodeInfo? info)
            ? code + " " + info.Name + ": " + info.English + " / " + info.Portuguese
            : code + " (unknown code, kept as-is)";

    private static int ValidateCommand(byte[] data, TextWriter stdout, TextWriter stderr)
    {
        Parsed parsed;
        try
        {
            parsed = Parse(data);
        }
        catch (Exception e) when (e is Iso20022Exception or NotSupportedException)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 1;
        }

        var all = new List<Violation>(parsed.Envelope.Validate());
        all.AddRange(parsed.Violations);
        string type = parsed.Envelope.Type.ToString();
        if (all.Count == 0)
        {
            stdout.Write(type + ": valid\n");
            return 0;
        }

        stdout.Write(type + ": " + all.Count.ToString(CultureInfo.InvariantCulture) + " violation(s)\n");
        foreach (Violation v in all)
        {
            stdout.Write("  " + v.ToString() + "\n");
        }

        return 1;
    }

    private static int ReturnCommand(string[] args, Stream stdin, TextWriter stdout, TextWriter stderr)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reason"] = string.Empty,
            ["amount"] = string.Empty,
            ["prior"] = string.Empty,
            ["info"] = string.Empty,
            ["e2e"] = string.Empty,
        };

        string[]? positional = ParseFlags(args, flags, stderr);
        if (positional is null)
        {
            return 2;
        }

        if (positional.Length != 1 || flags["reason"].Length == 0)
        {
            stderr.Write(Usage);
            return 2;
        }

        byte[] data;
        try
        {
            data = ReadInput(positional[0], stdin);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 2;
        }

        Pacs008Document original;
        try
        {
            original = Pacs008Document.Parse(data);
        }
        catch (MessageParseException e)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 1;
        }

        BrlAmount prior = BrlAmount.Zero;
        if (flags["prior"].Length > 0 && !TryAmount(flags["prior"], "-prior", stderr, out prior))
        {
            return 2;
        }

        string e2e = flags["e2e"];
        BrlAmount amount;
        if (flags["amount"].Length == 0)
        {
            if (original.Transactions.Count == 0)
            {
                stderr.WriteLine("iso20022: pacs.008 has no transactions");
                return 1;
            }

            amount = original.Transactions[0].Amount;
            foreach (CreditTransferTransaction t in original.Transactions)
            {
                if (t.PmtId.EndToEndId == e2e)
                {
                    amount = t.Amount;
                }
            }

            amount = new BrlAmount(unchecked(amount.Cents - prior.Cents));
        }
        else if (!TryAmount(flags["amount"], "-amount", stderr, out amount))
        {
            return 2;
        }

        var options = new Pacs004Options
        {
            EndToEndId = e2e.Length > 0 ? e2e : null,
            AdditionalInfo = flags["info"].Length > 0 ? new[] { flags["info"] } : Array.Empty<string>(),
            PriorReturns = prior,
        };

        string xml;
        try
        {
            xml = Pacs004.FromPacs008(original, flags["reason"], amount, options).ToXml();
        }
        catch (Iso20022Exception e)
        {
            stderr.WriteLine("iso20022: " + e.Message);
            return 1;
        }

        stdout.Write(xml);
        return 0;
    }

    private static bool TryAmount(string text, string flag, TextWriter stderr, out BrlAmount amount)
    {
        try
        {
            amount = BrlAmount.Parse(text);
            return true;
        }
        catch (InvalidAmountException e)
        {
            stderr.WriteLine("iso20022: " + flag + ": " + e.Message);
            amount = default;
            return false;
        }
    }

    // A subset of Go's flag package: -name VALUE, --name VALUE, -name=VALUE;
    // parsing stops at the first non-flag argument or after "--".
    private static string[]? ParseFlags(string[] args, Dictionary<string, string> flags, TextWriter stderr)
    {
        int i = 0;
        while (i < args.Length)
        {
            string a = args[i];
            if (a.Length < 2 || a[0] != '-')
            {
                break;
            }

            i++;
            if (a == "--")
            {
                break;
            }

            string name = a.StartsWith("--", StringComparison.Ordinal) ? a.Substring(2) : a.Substring(1);
            string? value = null;
            int eq = name.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                value = name.Substring(eq + 1);
                name = name.Substring(0, eq);
            }

            if (!flags.ContainsKey(name))
            {
                stderr.Write("flag provided but not defined: -" + name + "\n" + Usage);
                return null;
            }

            if (value is null)
            {
                if (i >= args.Length)
                {
                    stderr.Write("flag needs an argument: -" + name + "\n" + Usage);
                    return null;
                }

                value = args[i++];
            }

            flags[name] = value;
        }

        return args.Skip(i).ToArray();
    }

    private sealed class Printer
    {
        private readonly TextWriter _w;

        public Printer(TextWriter w)
        {
            _w = w;
        }

        public void Kv(string key, string value) => _w.Write((key + ":").PadRight(16) + " " + value + "\n");

        public void Opt(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Kv(key, value);
            }
        }

        public void Section(string title) => _w.Write("-- " + title + "\n");
    }
}
