using System.Security.Cryptography;
using System.Text;

namespace Iso20022.Pix.Tests;

/// <summary>Access to the golden messages shared with the Go implementation, and small helpers.</summary>
internal static class TestData
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", name);

    public static string Load(string name) => File.ReadAllText(Path(name), new UTF8Encoding(false));

    public static byte[] LoadBytes(string name) => File.ReadAllBytes(Path(name));

    /// <summary>The deterministic byte stream shared with tools/vecgen: <c>b[i] = byte(i*seed + 7)</c>.</summary>
    public static byte[] Pattern(int seed, int n)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = unchecked((byte)((i * seed) + 7));
        }

        return b;
    }

    public static MemoryStream Stream(byte[] bytes) => new(bytes, writable: false);

    public static string Sha(string xml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));

    public static string Join(IReadOnlyList<Violation> violations) => string.Join("\n", violations.Select(v => v.ToString()));

    public static readonly DateTimeOffset Created = new(2026, 11, 21, 10, 30, 1, TimeSpan.Zero);

    public static Transfer SampleTransfer() => new()
    {
        Amount = BrlAmount.Parse("10.00"),
        AcceptedAt = new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero),
        Initiation = Pacs008.InitiationManual,
        Debtor = new TransferParty { Name = "Fulano de Tal", TaxId = "52998224725" },
        DebtorAccount = new TransferAccount { Ispb = "99999999", Branch = "0001", Number = "123456", Type = Components.AccountTypes.Current },
        Creditor = new TransferParty { Name = "Loja Exemplo Ltda", TaxId = "11222333000181" },
        CreditorAccount = new TransferAccount { Ispb = "88888888", Number = "000123", Type = Components.AccountTypes.Payments },
        PixKey = "+5521987654321",
        Purpose = "IPAY",
        RemittanceInfo = "obrigado",
    };

    public static Pacs008Document Original() => Pacs008Document.Parse(Load("pacs008.xml"));
}
