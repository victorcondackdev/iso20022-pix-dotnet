namespace Iso20022.Pix.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("0.0", 0L)]
    [InlineData("0.00", 0L)]
    [InlineData("0.01", 1L)]
    [InlineData("0.1", 10L)]
    [InlineData("1", 100L)]
    [InlineData("10.5", 1050L)]
    [InlineData("10.50", 1050L)]
    [InlineData("010.50", 1050L)]
    [InlineData("1234567.89", 123456789L)]
    [InlineData("9999999999999999.99", 999999999999999999L)]
    [InlineData("0000000000000000.01", 1L)]
    public void Parses(string input, long cents)
    {
        Assert.Equal(cents, BrlAmount.Parse(input).Cents);
        Assert.True(BrlAmount.TryParse(input, out BrlAmount a));
        Assert.Equal(cents, a.Cents);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1.234")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e2")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,00")]
    [InlineData("1.0.0")]
    [InlineData("1_000")]
    [InlineData("NaN")]
    [InlineData("1234567890123456789")]
    [InlineData("99999999999999999.9")]
    [InlineData("999999999999999999")]
    [InlineData("10000000000000000")]
    public void RejectsInvalidAmounts(string input)
    {
        InvalidAmountException e = Assert.Throws<InvalidAmountException>(() => BrlAmount.Parse(input));
        Assert.StartsWith("money: invalid amount: ", e.Message);
        Assert.False(BrlAmount.TryParse(input, out _));
    }

    [Fact]
    public void ErrorMessagesMatchGo()
    {
        Assert.Equal("money: invalid amount: \"1.234\" has more than 2 fraction digits", Message("1.234"));
        Assert.Equal("money: invalid amount: \"1,5\" contains ','", Message("1,5"));
        Assert.Equal("money: invalid amount: empty", Message(string.Empty));
        Assert.Equal("money: invalid amount: \"99999999999999999.9\" exceeds 9999999999999999.99", Message("99999999999999999.9"));

        static string Message(string s) => Assert.Throws<InvalidAmountException>(() => BrlAmount.Parse(s)).Message;
    }

    [Theory]
    [InlineData(0L, "0.00")]
    [InlineData(1L, "0.01")]
    [InlineData(10L, "0.10")]
    [InlineData(1050L, "10.50")]
    [InlineData(-1L, "-0.01")]
    [InlineData(long.MaxValue, "92233720368547758.07")]
    [InlineData(long.MinValue, "-92233720368547758.08")]
    public void Formats(long cents, string text) => Assert.Equal(text, new BrlAmount(cents).ToString());

    [Fact]
    public void IsoFormRejectsUnrepresentableAmounts()
    {
        Assert.Equal("9999999999999999.99", BrlAmount.MaxValue.ToIsoString());
        Assert.Throws<InvalidAmountException>(() => new BrlAmount(-1).ToIsoString());
        Assert.Throws<InvalidAmountException>(() => (BrlAmount.MaxValue + new BrlAmount(1)).ToIsoString());
    }

    [Fact]
    public void ArithmeticIsExactAndChecked()
    {
        BrlAmount sum = BrlAmount.Parse("0.1") + BrlAmount.Parse("0.2");
        Assert.Equal(BrlAmount.Parse("0.30"), sum);
        Assert.True((sum - BrlAmount.Parse("0.30")).IsZero);
        Assert.Throws<OverflowException>(() => new BrlAmount(long.MaxValue) + new BrlAmount(1));
        Assert.Throws<OverflowException>(() => new BrlAmount(long.MinValue) - new BrlAmount(1));
        Assert.False(new BrlAmount(long.MaxValue).TryAdd(new BrlAmount(1), out _));
        Assert.False(new BrlAmount(long.MinValue).TrySubtract(new BrlAmount(1), out _));
        Assert.True(new BrlAmount(5).TrySubtract(new BrlAmount(7), out BrlAmount d));
        Assert.Equal(-2, d.Cents);
        Assert.True(BrlAmount.Parse("1") > BrlAmount.Parse("0.99"));
        Assert.Equal(42, BrlAmount.FromCents(42).Cents);
        Assert.True(new BrlAmount(1).IsPositive);
    }
}
