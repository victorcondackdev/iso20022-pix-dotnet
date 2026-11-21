using System.Text;
using Iso20022.Pix.Cli;

namespace Iso20022.Pix.Tests;

public class CliTests
{
    private static (int Code, string Stdout, string Stderr) Run(string stdin, params string[] args)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(stdin));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int code = Program.Run(args, input, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData("pacs008.xml", "pacs.008.001.08 (FIToFICustomerCreditTransfer)")]
    [InlineData("pacs008.xml", "Fulano de Tal (52998224725) ISPB 99999999 ag 0001 cc 123456")]
    [InlineData("pacs008.xml", "Valid:           yes")]
    [InlineData("envelope_pacs008.xml", "AppHdr:          99999999 -> 00038166")]
    [InlineData("pacs002_reject.xml", "AC03 InvalidCreditorAccountNumber")]
    [InlineData("pacs002_reject.xml", "Conta do recebedor inexistente")]
    [InlineData("pacs002_accept.xml", "Settled:         2026-11-21T10:30:01.789Z")]
    [InlineData("pacs004.xml", "MD06 RefundRequestByEndCustomer")]
    [InlineData("camt054.xml", "account 0001/6543210")]
    [InlineData("camt054.xml", "150.75 BRL  BOOK")]
    public void Inspect(string file, string expected)
    {
        (int code, string stdout, string stderr) = Run(string.Empty, "inspect", TestData.Path(file));
        Assert.True(code == 0, stderr);
        Assert.Contains(expected, stdout);
    }

    [Fact]
    public void InspectFromStdinAndErrors()
    {
        string data = TestData.Load("pacs004.xml");
        Assert.Contains("pacs.004", Run(data, "inspect", "-").Stdout);
        Assert.Contains("ZZ01 (unknown code, kept as-is)", Run(data.Replace("MD06", "ZZ01", StringComparison.Ordinal), "inspect", "-").Stdout);
        Assert.Contains("Valid:           no", Run(data.Replace("<NbOfTxs>1", "<NbOfTxs>2", StringComparison.Ordinal), "inspect", "-").Stdout);
        Assert.Equal(1, Run("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.08\"/>", "inspect", "-").Code);
        Assert.Equal(1, Run("<a/>", "inspect", "-").Code);
        Assert.Equal(2, Run(string.Empty, "inspect", "does-not-exist.xml").Code);
        Assert.Equal(2, Run(string.Empty, "inspect").Code);
    }

    [Fact]
    public void Validate()
    {
        foreach (string f in new[] { "pacs008.xml", "envelope_pacs008.xml", "pacs002_accept.xml", "pacs002_reject.xml", "pacs004.xml", "camt054.xml" })
        {
            (int code, string stdout, _) = Run(string.Empty, "validate", TestData.Path(f));
            Assert.Equal(0, code);
            Assert.Contains(": valid", stdout);
        }

        string bad = TestData.Load("envelope_pacs008.xml")
            .Replace("<MsgDefIdr>pacs.008.001.08", "<MsgDefIdr>pacs.008.001.09", StringComparison.Ordinal)
            .Replace("<SttlmMtd>CLRG", "<SttlmMtd>INDA", StringComparison.Ordinal);
        (int c, string o, _) = Run(bad, "validate", "-");
        Assert.Equal(1, c);
        Assert.Contains("2 violation(s)", o);
        Assert.Contains("/AppHdr/MsgDefIdr", o);
        Assert.Contains("SttlmMtd", o);
        Assert.Equal(1, Run("<x", "validate", "-").Code);
    }

    [Fact]
    public void Return()
    {
        (int code, string stdout, string stderr) = Run(string.Empty, "return", "-reason", "MD06", "-amount", "10.00", "-info", "parcial", TestData.Path("pacs008.xml"));
        Assert.True(code == 0, stderr);
        Pacs004Document d = Pacs004Document.Parse(stdout);
        ReturnTransaction tx = d.Transactions[0];
        Assert.Equal(BrlAmount.Parse("10"), tx.Amount);
        Assert.Equal("MD06", tx.Reason);
        Assert.Equal("parcial", tx.RtrRsnInf[0].AddtlInf[0]);
        Assert.Empty(d.Validate());

        // Full amount by default, minus prior returns; --flag and -flag=value forms.
        (_, string full, _) = Run(string.Empty, "return", "--reason", "BE08", "--prior=50.75", "-e2e", "E99999999202611211030a1B2c3D4e5F", TestData.Path("pacs008.xml"));
        Assert.Equal(BrlAmount.Parse("100"), Pacs004Document.Parse(full).Transactions[0].Amount);
    }

    [Theory]
    [InlineData(1, "exceeds", "return", "-reason", "MD06", "-amount", "150.76", "@pacs008.xml")]
    [InlineData(1, "unknown return reason", "return", "-reason", "XX99", "@pacs008.xml")]
    [InlineData(2, "-amount", "return", "-reason", "MD06", "-amount", "1,00", "@pacs008.xml")]
    [InlineData(2, "-prior", "return", "-reason", "MD06", "-prior", "x", "@pacs008.xml")]
    [InlineData(2, "usage", "return", "@pacs008.xml")]
    [InlineData(2, "flag provided but not defined", "return", "-bogus", "@pacs008.xml")]
    [InlineData(1, "unexpected message type", "return", "-reason", "MD06", "@pacs004.xml")]
    [InlineData(2, "nope.xml", "return", "-reason", "MD06", "nope.xml")]
    public void ReturnErrors(int expectedCode, string fragment, params string[] args)
    {
        string[] resolved = args.Select(a => a.StartsWith('@') ? TestData.Path(a.Substring(1)) : a).ToArray();
        (int code, _, string stderr) = Run(string.Empty, resolved);
        Assert.Equal(expectedCode, code);
        Assert.Contains(fragment, stderr);
    }

    [Fact]
    public void ReturnOfAnEmptyPayment()
    {
        (int code, _, string stderr) = Run("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/>", "return", "-reason", "MD06", "-");
        Assert.Equal(1, code);
        Assert.Contains("no transactions", stderr);
    }

    [Fact]
    public void Usage()
    {
        (int code, _, string stderr) = Run(string.Empty);
        Assert.Equal(2, code);
        Assert.Contains("usage", stderr);
        (int helpCode, string help, _) = Run(string.Empty, "help");
        Assert.Equal(0, helpCode);
        Assert.Contains("usage", help);
        (int unknownCode, _, string unknown) = Run(string.Empty, "frobnicate");
        Assert.Equal(2, unknownCode);
        Assert.Contains("unknown command", unknown);
    }

    [Fact]
    public void InputSizeIsBounded()
    {
        using var big = new MemoryStream(new byte[Program.MaxInput + 1]);
        Assert.Throws<IOException>(() => Program.ReadInput("-", big));
    }
}
