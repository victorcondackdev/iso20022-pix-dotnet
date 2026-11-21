using Iso20022.Pix.Components;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix.Tests;

public class CheckerTests
{
    [Fact]
    public void AccumulatesViolations()
    {
        var c = new Checker();
        Assert.Empty(c.Result);
        c.Date("/d", string.Empty, true);
        c.Date("/d2", string.Empty, false);
        c.DateTime("/dt", string.Empty, false);
        c.DateOrDateTime("/dd", null, true);
        c.OneOf("/o", string.Empty, false, "A");
        c.Text("/t", string.Empty, 3, false);
        c.Account("/acc", null, false);
        c.Account("/acc2", new Account { Id = new AccountId { IBAN = "BR123" }, Tp = CodeOrProprietary.Proprietary("CACC") }, true);
        c.Choice("/ch", null, 35);
        c.Remittance("/r", null);
        c.Party("/p", new Party { Nm = "x" }, true, false);
        c.Account("/acc3", new Account
        {
            Id = new AccountId { Othr = new GenericAccountId { Id = "1" } },
            Tp = new CodeOrProprietary { Cd = "CACC", Prtry = "CACC" },
        }, true);
        c.NbOfTxs("/n", "1234567890123456", 1);
        c.ReturnId("/rid", string.Empty);
        c.Text("/ctl", "a" + (char)0 + "b", 10, true);

        string[] want =
        {
            "/d: is required",
            "/dd: is required",
            "/acc3/Tp: must contain exactly one of Cd or Prtry, got both",
            "/n: must be a number of up to 15 digits, got \"1234567890123456\"",
            "/rid: is required",
            "/ctl: contains U+0000, which XML 1.0 does not allow",
        };
        Assert.Equal(want, c.Result.Select(v => v.ToString()).ToArray());
    }

    [Fact]
    public void FormatsViolationsLikeGo()
    {
        Assert.Equal("no violations", MessageValidationException.Format(Array.Empty<Violation>()));
        var one = new[] { new Violation("/a", "bad") };
        Assert.Equal("/a: bad", MessageValidationException.Format(one));
        var two = new[] { new Violation("/a", "bad"), new Violation("/b", "worse") };
        Assert.Equal("2 violations:\n\t/a: bad\n\t/b: worse", MessageValidationException.Format(two));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("quote\"back\\slash", "\"quote\\\"back\\\\slash\"")]
    [InlineData("tab\tnl\n", "\"tab\\tnl\\n\"")]
    public void QuotesLikeGo(string input, string quoted) => Assert.Equal(quoted, GoFormat.Quote(input));

    [Fact]
    public void QuotesNonPrintableCharactersLikeGo()
    {
        Assert.Equal("\"\\x00\\x7f\"", GoFormat.Quote(((char)0).ToString() + (char)0x7F));
        Assert.Equal("\"\\u00a0\\u2028\\u200b\"", GoFormat.Quote(new string(new[] { (char)0xA0, (char)0x2028, (char)0x200B })));
        Assert.Equal("\"\\U000e0001\"", GoFormat.Quote(char.ConvertFromUtf32(0xE0001)));
        Assert.Equal("'\\''", GoFormat.QuoteRune('\''));
        Assert.Equal("'E'", GoFormat.QuoteRune('E'));
        Assert.Equal("'\\u0080'", GoFormat.QuoteRune(0x80));
        Assert.Equal("\"\\xff\"", GoFormat.QuoteUtf8(new byte[] { 0xFF }));
        Assert.Equal("U+0000", GoFormat.CodePoint(0));
        Assert.Equal("U+1F642", GoFormat.CodePoint(0x1F642));
    }
}
