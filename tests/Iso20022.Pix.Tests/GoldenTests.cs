using Iso20022.Pix.Tests.Parity;

namespace Iso20022.Pix.Tests;

/// <summary>
/// The golden messages in testdata/ are shared with the Go implementation:
/// parsing and re-serializing them must reproduce the files byte for byte,
/// and the builders must reproduce them from the same inputs.
/// </summary>
public class GoldenTests
{
    [Theory]
    [InlineData("pacs008.xml")]
    [InlineData("pacs002_accept.xml")]
    [InlineData("pacs002_reject.xml")]
    [InlineData("pacs004.xml")]
    [InlineData("camt054.xml")]
    public void RoundTripIsByteIdentical(string file)
    {
        byte[] golden = TestData.LoadBytes(file);
        (IReadOnlyList<Violation> violations, byte[] output) = file switch
        {
            "pacs008.xml" => Run(Pacs008Document.Parse(golden)),
            "pacs004.xml" => Run(Pacs004Document.Parse(golden)),
            "camt054.xml" => Run(Camt054Document.Parse(golden)),
            _ => Run(Pacs002Document.Parse(golden)),
        };

        Assert.Empty(violations);
        Assert.Equal(golden, output);
    }

    [Fact]
    public void EnvelopeParsesAndValidates()
    {
        Pacs008Document d = Pacs008Document.Parse(TestData.Load("envelope_pacs008.xml"));
        Assert.Empty(d.Validate());
        Assert.Equal(string.Empty, d.Transactions[0].Initiation);
        Assert.Equal(string.Empty, d.Transactions[0].RemittanceInfo);
    }

    [Fact]
    public void BuildersReproduceTheGoldenFiles()
    {
        Assert.Equal(TestData.Load("pacs008.xml"), ParityTests.GoldenPacs008().ToXml());

        var at = new DateTimeOffset(2026, 11, 21, 10, 30, 1, 456, TimeSpan.Zero);
        Pacs002Document accept = Pacs002.Accept(TestData.Original(), new Pacs002Options
        {
            At = at,
            SettledAt = at.AddMilliseconds(333),
            MsgId = "M8888888820261121103000000000007",
        });
        Assert.Equal(TestData.Load("pacs002_accept.xml"), accept.ToXml());

        Pacs002Document reject = Pacs002.Reject(TestData.Original(), "AC03", new Pacs002Options
        {
            At = at,
            MsgId = "M8888888820261121103000000000008",
            AdditionalInfo = new[] { "Conta do recebedor inexistente" },
        });
        Assert.Equal(TestData.Load("pacs002_reject.xml"), reject.ToXml());

        Pacs004Document ret = Pacs004.FromPacs008(TestData.Original(), Pacs004.ReasonCustomerRefund, BrlAmount.Parse("50.00"), new Pacs004Options
        {
            At = new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero),
            MsgId = "M8888888820261121150000000000003",
            ReturnId = "D88888888202611211500Zx9Yw8Vu7Ts",
            AdditionalInfo = new[] { "Devolucao parcial a pedido do recebedor" },
        });
        Assert.Equal(TestData.Load("pacs004.xml"), ret.ToXml());
    }

    [Fact]
    public void StreamAndStringParsingAgree()
    {
        using FileStream s = File.OpenRead(TestData.Path("camt054.xml"));
        Camt054Document fromStream = Camt054Document.Parse(s);
        Camt054Document fromString = Camt054Document.Parse(TestData.Load("camt054.xml"));
        Assert.Equal(fromString.ToXml(), fromStream.ToXml());
    }

    private static (IReadOnlyList<Violation>, byte[]) Run(Pacs008Document d) => (d.Validate(), d.ToXmlBytes());

    private static (IReadOnlyList<Violation>, byte[]) Run(Pacs002Document d) => (d.Validate(), d.ToXmlBytes());

    private static (IReadOnlyList<Violation>, byte[]) Run(Pacs004Document d) => (d.Validate(), d.ToXmlBytes());

    private static (IReadOnlyList<Violation>, byte[]) Run(Camt054Document d) => (d.Validate(), d.ToXmlBytes());
}
